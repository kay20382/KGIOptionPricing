using KGIOptionPricing.StrategyFramework;

namespace KGIOptionPricing.Strategies.PeriodicQueueCycle;

public sealed class PeriodicQueueCycleStrategy : ISimulationStrategy, IManagedOrderStrategy
{
    private readonly StrategyDefinition _definition;
    private readonly PeriodicQueueCycleParameters _parameters;
    private StrategyRuntimeContext? _context;
    private DirectionWindow? _directionWindow;
    private QueueCycleOrderCoordinator? _coordinator;
    private DirectionAssessment? _lastAssessment;
    private MarketEvent? _lastBook;
    private string _reason = "尚未啟動";
    private int _oppositeDirectionSeconds;
    private long _lastOppositeUtcSecond = long.MinValue;
    private bool _paused;
    private bool _stopRequested;
    private string _stopReason = string.Empty;

    internal PeriodicQueueCycleStrategy(
        StrategyDefinition definition,
        PeriodicQueueCycleParameters parameters)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
    }

    public string StrategyType => PeriodicQueueCycleFactory.TypeName;
    public string InstanceId => _definition.InstanceId;
    public StrategyLifecycleState State { get; private set; } = StrategyLifecycleState.Created;

    public event Action<PaperOrderRequest>? OrderRequested;
    public event Action? CancelAllRequested { add { } remove { } }
    public event Action<StrategySnapshot>? SnapshotChanged;
    public event Action<string>? CancelOrderRequested;

    public void Start(StrategyRuntimeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (State != StrategyLifecycleState.Created) throw new InvalidOperationException("策略已啟動。");
        if (context.Universe.Tradable.TickSize <= 0m)
            throw new ArgumentException("交易商品 TickSize 必須大於零。", nameof(context));

        _context = context;
        _directionWindow = new DirectionWindow(context.Universe.Tradable.TickSize, _parameters);
        _coordinator = new QueueCycleOrderCoordinator(
            InstanceId, context.Universe.Tradable.Symbol, _parameters);
        _coordinator.OrderRequested += ForwardOrder;
        _coordinator.CancelOrderRequested += ForwardCancel;
        State = StrategyLifecycleState.WarmingUp;
        _reason = "WarmupIncomplete";
        Publish();
    }

    public void OnMarketEvent(MarketEvent marketEvent)
    {
        if (_context is null || _directionWindow is null || _coordinator is null ||
            State is StrategyLifecycleState.Stopped or StrategyLifecycleState.Faulted ||
            marketEvent.Kind != MarketEventKind.Book ||
            !string.Equals(marketEvent.Symbol, _context.Universe.Tradable.Symbol, StringComparison.OrdinalIgnoreCase))
            return;
        if (_paused && Position().NetQuantity == 0) return;

        _lastBook = marketEvent;
        bool added = _directionWindow.TryAdd(marketEvent, _context.Clock.Now);
        _lastAssessment = _directionWindow.Evaluate(
            _context.Clock.Now, _context.Universe.Tradable.TickSize, _parameters);

        if (_coordinator.Phase is QueueCyclePhase.WorkingBoth or QueueCyclePhase.WorkingOneLeg or
            QueueCyclePhase.CancellingForReprice or QueueCyclePhase.CancellingForExit)
        {
            bool currentBookHealthy = added && !IsHealthFailure(_lastAssessment.PrimaryReasonCode);
            if (currentBookHealthy && ManagePreFillReversal(marketEvent.Timestamp))
            {
                State = _coordinator.IsFaulted
                    ? StrategyLifecycleState.Faulted
                    : _paused ? StrategyLifecycleState.Suspended : StrategyLifecycleState.Running;
                _reason = _coordinator.ReasonCode;
                Publish();
                return;
            }
            if (currentBookHealthy) _coordinator.EvaluateReprices(marketEvent);
            State = _coordinator.IsFaulted
                ? StrategyLifecycleState.Faulted
                : _paused ? StrategyLifecycleState.Suspended : StrategyLifecycleState.Running;
            _reason = currentBookHealthy ? _coordinator.ReasonCode : _lastAssessment.PrimaryReasonCode;
            Publish();
            return;
        }

        if (_lastAssessment.PrimaryReasonCode == "WarmupIncomplete")
        {
            State = StrategyLifecycleState.WarmingUp;
            _reason = "WarmupIncomplete";
            Publish();
            return;
        }

        State = StrategyLifecycleState.Ready;
        _reason = _lastAssessment.PrimaryReasonCode;
        Publish();
        if (_lastAssessment.Direction == CycleDirection.Neutral) return;
        if (Position().NetQuantity != 0)
        {
            State = StrategyLifecycleState.Suspended;
            _reason = "PositionNotFlat";
            Publish();
            return;
        }

        FrozenPriceBounds bounds = QueueCyclePricingPolicy.CreateBounds(
            marketEvent.MidPrice,
            _context.Universe.Tradable.TickSize,
            _parameters.MaxChaseTicks);
        if (_coordinator.TryStart(_lastAssessment.Direction, _lastAssessment, bounds, marketEvent))
        {
            State = _coordinator.IsFaulted ? StrategyLifecycleState.Faulted : StrategyLifecycleState.Running;
            _reason = _coordinator.ReasonCode;
        }
        else
        {
            _reason = _coordinator.ReasonCode;
        }
        Publish();
    }

    public void OnExecution(ExecutionEvent execution)
    {
        if (!string.Equals(execution.StrategyInstanceId, InstanceId, StringComparison.OrdinalIgnoreCase)) return;
        _coordinator?.OnExecution(execution);
        _oppositeDirectionSeconds = 0;
        _reason = _coordinator?.ReasonCode ?? _reason;
        if (_coordinator?.IsFaulted == true) State = StrategyLifecycleState.Faulted;
        TryCompleteStop();
        Publish();
    }

    public void OnOrderLifecycle(OrderLifecycleEvent orderEvent)
    {
        if (!string.Equals(orderEvent.StrategyInstanceId, InstanceId, StringComparison.OrdinalIgnoreCase)) return;
        _coordinator?.OnOrderLifecycle(orderEvent);
        _reason = _coordinator?.ReasonCode ?? _reason;
        if (_coordinator?.IsFaulted == true) State = StrategyLifecycleState.Faulted;
        TryCompleteStop();
        Publish();
    }

    public void Pause(string reason)
    {
        if (State is StrategyLifecycleState.Stopped or StrategyLifecycleState.Faulted) return;
        _paused = true;
        string pauseReason = string.IsNullOrWhiteSpace(reason) ? "使用者暫停" : reason;
        if (Position().NetQuantity == 0) _coordinator?.CancelAllWorking("Paused");
        State = _coordinator?.IsFaulted == true
            ? StrategyLifecycleState.Faulted
            : StrategyLifecycleState.Suspended;
        _reason = _coordinator?.IsFaulted == true ? _coordinator.ReasonCode : pauseReason;
        Publish();
    }

    public void Resume()
    {
        if (State != StrategyLifecycleState.Suspended || _stopRequested) return;
        if (Position().NetQuantity == 0)
        {
            if (_coordinator?.HasOutstandingOrders == true) return;
            if (_coordinator?.ResetCancelledFlatCycle() == true && _context is not null)
                _directionWindow = new DirectionWindow(_context.Universe.Tradable.TickSize, _parameters);
            _paused = false;
            State = StrategyLifecycleState.WarmingUp;
            _reason = "WarmupIncomplete";
            Publish();
            return;
        }
        _paused = false;
        State = StrategyLifecycleState.Running;
        _reason = _coordinator?.ReasonCode ?? "持倉保護恢復";
        Publish();
    }

    public void Stop(string reason)
    {
        if (State is StrategyLifecycleState.Stopped or StrategyLifecycleState.Faulted) return;
        _stopRequested = true;
        _paused = true;
        _stopReason = string.IsNullOrWhiteSpace(reason) ? "策略已停止" : reason;
        _coordinator?.CancelAllWorking("StopRequested");
        if (_coordinator?.IsFaulted == true)
        {
            State = StrategyLifecycleState.Faulted;
            _reason = _coordinator.ReasonCode;
        }
        else
        {
            State = StrategyLifecycleState.Suspended;
            _reason = "StopPending";
            TryCompleteStop();
        }
        Publish();
    }

    public StrategySnapshot GetSnapshot()
    {
        PositionSnapshot position = Position();
        FrozenPriceBounds? bounds = _coordinator?.FrozenBounds;
        return new StrategySnapshot
        {
            StrategyType = StrategyType,
            InstanceId = InstanceId,
            ParameterSummary = _parameters.ParameterSummary,
            TradableProductId = _context?.Universe.Tradable.ProductId ?? _definition.Universe.Tradable.ProductId,
            TradableSymbol = _context?.Universe.Tradable.Symbol ?? _definition.Universe.Tradable.Symbol ?? string.Empty,
            ReferenceSummary = _coordinator is null
                ? "Cycle=none;Direction=Neutral;Buy=none;Sell=none"
                : _coordinator.CycleSummary,
            State = State,
            Reason = _reason,
            LastPrice = _lastBook?.LastPrice ?? 0m,
            CenterPrice = bounds?.Anchor ?? _lastBook?.MidPrice ?? 0m,
            Range = _lastAssessment?.PathTicks ?? 0m,
            BuyEntry = bounds?.BuyLimit ?? 0m,
            SellEntry = bounds?.SellLimit ?? 0m,
            Position = position.NetQuantity,
            RealizedPnl = position.RealizedPnl,
            UnrealizedPnl = position.UnrealizedPnl,
            NetPnl = position.NetPnl,
            CommissionPerContract = _parameters.CommissionPerContract,
            UpdatedAt = _context?.Clock.Now ?? DateTimeOffset.Now
        };
    }

    private PositionSnapshot Position() => _context is null
        ? new PositionSnapshot { StrategyInstanceId = InstanceId }
        : _context.Performance.GetPosition(InstanceId, _context.Universe.Tradable.Symbol);

    private void ForwardOrder(PaperOrderRequest request) => OrderRequested?.Invoke(request);
    private void ForwardCancel(string clientOrderId) => CancelOrderRequested?.Invoke(clientOrderId);
    private void Publish() => SnapshotChanged?.Invoke(GetSnapshot());

    private bool ManagePreFillReversal(DateTimeOffset timestamp)
    {
        if (_coordinator is null || _lastAssessment is null || !_coordinator.BothUnfilled ||
            _lastAssessment.Direction == CycleDirection.Neutral ||
            _lastAssessment.Direction == _coordinator.FrozenDirection)
        {
            _oppositeDirectionSeconds = 0;
            _lastOppositeUtcSecond = long.MinValue;
            return false;
        }

        long utcSecond = timestamp.ToUniversalTime().Ticks / TimeSpan.TicksPerSecond;
        if (utcSecond == _lastOppositeUtcSecond) return false;
        if (_lastOppositeUtcSecond != long.MinValue && utcSecond != _lastOppositeUtcSecond + 1L)
            _oppositeDirectionSeconds = 0;
        _lastOppositeUtcSecond = utcSecond;
        _oppositeDirectionSeconds++;
        if (_oppositeDirectionSeconds < _parameters.DirectionReverseSeconds) return false;
        return _coordinator.CancelBothBeforeFill("SignalReversedBeforeFill");
    }

    private void TryCompleteStop()
    {
        if (!_stopRequested || State == StrategyLifecycleState.Faulted) return;
        if (_coordinator?.IsFaulted == true)
        {
            State = StrategyLifecycleState.Faulted;
            _reason = _coordinator.ReasonCode;
            return;
        }
        if (_coordinator?.HasOutstandingOrders == true || Position().NetQuantity != 0) return;
        State = StrategyLifecycleState.Stopped;
        _reason = _stopReason;
    }

    private static bool IsHealthFailure(string reasonCode) => reasonCode is
        "StaleQuote" or "InvalidBook" or "InsufficientSideDepth" or
        "InsufficientNearDepth" or "DepthImbalance" or "SpreadTooWide" or
        "VolatilityTooHigh";
}
