using System.Text.Json;
using KGIOptionPricing.StrategyFramework;
using KGIOptionPricing.StrategyFramework.Recovery;

namespace KGIOptionPricing.Strategies.PeriodicQueueCycle;

public sealed class PeriodicQueueCycleStrategy : ISimulationStrategy, IManagedOrderStrategy, IManagedOrderSubmissionObserver, IStrategyRecoveryParticipant, IStrategyHeartbeat, IStrategyDecisionSource
{
    private sealed record RecoveryState(ManagedOrderBookCheckpoint OrderBook, DirectionWindowCheckpoint? DirectionWindow,
        QueueCycleOrderCoordinator.QueueCycleCoordinatorCheckpoint? Coordinator, MarketEvent? LastBook, MarketEvent? LastHealthyBook,
        DateTimeOffset? LastObservedBookTimestamp, long LastObservedBookSequence, string Reason, int OppositeDirectionSeconds,
        long LastOppositeUtcSecond, bool Paused, bool StopRequested, string StopReason, DateTimeOffset? OneLegStartedAt,
        DateTimeOffset? UnhealthySince, DateTimeOffset? CooldownUntil, PaperOrderRole? PendingExitRole,
        string? PendingExitReason, int PendingCooldownSeconds, bool ExitRequested, decimal FixedStopPrice,
        string ActiveCandidateId, bool AcceptDecisionPending, StrategyLifecycleState State);
    private readonly StrategyDefinition _definition;
    private readonly PeriodicQueueCycleParameters _parameters;
    private StrategyRuntimeContext? _context;
    private DirectionWindow? _directionWindow;
    private QueueCycleOrderCoordinator? _coordinator;
    private DirectionAssessment? _lastAssessment;
    private MarketEvent? _lastBook;
    private MarketEvent? _lastHealthyBook;
    private DateTimeOffset? _lastObservedBookTimestamp;
    private long _lastObservedBookSequence = long.MinValue;
    private string _reason = "尚未啟動";
    private int _oppositeDirectionSeconds;
    private long _lastOppositeUtcSecond = long.MinValue;
    private bool _paused;
    private bool _stopRequested;
    private string _stopReason = string.Empty;
    private DateTimeOffset? _oneLegStartedAt;
    private DateTimeOffset? _unhealthySince;
    private DateTimeOffset? _cooldownUntil;
    private PaperOrderRole? _pendingExitRole;
    private string? _pendingExitReason;
    private int _pendingCooldownSeconds;
    private bool _exitRequested;
    private decimal _fixedStopPrice;
    private string _activeCandidateId = string.Empty;
    private bool _acceptDecisionPending;
    private long _strategyRunGeneration;
    private ManagedStrategyOrderBook? _managedOrderBook;
    private bool _restoreBeforeStart;
    public string RecoverySchema => "periodic-queue-cycle/v1";

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
    public event Action<StrategyDecisionObservation>? DecisionObserved;

    public void Start(StrategyRuntimeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (State != StrategyLifecycleState.Created && !_restoreBeforeStart) throw new InvalidOperationException("策略已啟動。");
        if (context.Universe.Tradable.TickSize <= 0m)
            throw new ArgumentException("交易商品 TickSize 必須大於零。", nameof(context));

        _context = context;
        if (_restoreBeforeStart)
        {
            if (_strategyRunGeneration != context.StrategyRunGeneration)
                throw new InvalidOperationException("Restored PQC run generation does not match runtime context.");
        }
        else _strategyRunGeneration = context.StrategyRunGeneration;
        _managedOrderBook ??= new ManagedStrategyOrderBook(InstanceId, context.StrategyRunGeneration);
        if (_managedOrderBook.StrategyRunGeneration != context.StrategyRunGeneration)
            throw new InvalidOperationException("PQC managed order book generation mismatch.");
        _directionWindow = new DirectionWindow(context.Universe.Tradable.TickSize, _parameters);
        _coordinator = new QueueCycleOrderCoordinator(
            InstanceId, context.Universe.Tradable.Symbol, _parameters, context.StrategyRunGeneration);
        if (_restoreBeforeStart && _restoreCoordinator is not null)
            _coordinator.Restore(_restoreCoordinator);
        if (_restoreBeforeStart && _restoreDirectionWindow is not null)
            _directionWindow.Restore(_restoreDirectionWindow);
        _coordinator.OrderRequested += ForwardOrder;
        _coordinator.CancelOrderRequested += ForwardCancel;
        if (_restoreBeforeStart)
        {
            _paused = true;
            State = StrategyLifecycleState.Suspended;
            _reason = "RecoveryAwaitingResume";
        }
        else
        {
            State = StrategyLifecycleState.WarmingUp;
            _reason = "WarmupIncomplete";
        }
        _restoreBeforeStart = false;
        Publish();
    }

    public void OnMarketEvent(MarketEvent marketEvent)
    {
        if (_context is null || _directionWindow is null || _coordinator is null ||
            State is StrategyLifecycleState.Stopped or StrategyLifecycleState.Faulted ||
            marketEvent.Kind != MarketEventKind.Book ||
            !string.Equals(marketEvent.Symbol, _context.Universe.Tradable.Symbol, StringComparison.OrdinalIgnoreCase))
            return;

        if (_cooldownUntil.HasValue)
        {
            if (_context.Clock.Now < _cooldownUntil.Value)
            {
                State = _paused ? StrategyLifecycleState.Suspended : StrategyLifecycleState.Running;
                _reason = "CoolingDown";
                Publish();
                return;
            }

            _cooldownUntil = null;
            _coordinator.ResetAfterCooldown();
            _directionWindow = new DirectionWindow(_context.Universe.Tradable.TickSize, _parameters);
            _activeCandidateId = string.Empty;
            State = _paused ? StrategyLifecycleState.Suspended : StrategyLifecycleState.WarmingUp;
            _reason = "WarmupIncomplete";
        }
        if (_paused && Position().NetQuantity == 0) return;

        if (_lastObservedBookTimestamp.HasValue &&
            (marketEvent.Timestamp < _lastObservedBookTimestamp.Value ||
             marketEvent.Sequence <= _lastObservedBookSequence))
            return;
        _lastObservedBookTimestamp = marketEvent.Timestamp;
        _lastObservedBookSequence = marketEvent.Sequence;

        _lastBook = marketEvent;
        bool added = _directionWindow.TryAdd(marketEvent, _context.Clock.Now);
        _lastAssessment = _directionWindow.Evaluate(
            _context.Clock.Now, _context.Universe.Tradable.TickSize, _parameters);
        bool currentBookHealthy = added && !IsHealthFailure(_lastAssessment.PrimaryReasonCode);
        if (currentBookHealthy) _lastHealthyBook = marketEvent;

        if (_coordinator.IsClosingBlocked)
        {
            State = StrategyLifecycleState.Suspended;
            _reason = _coordinator.ReasonCode;
            Publish();
            return;
        }

        PositionSnapshot position = Position();
        if (position.NetQuantity != 0)
        {
            EnsureExposureTimingAndStop(position, _context.Clock.Now);
            if (!currentBookHealthy)
            {
                _unhealthySince ??= _context.Clock.Now;
                _coordinator.BeginRiskCancellation(_lastAssessment.PrimaryReasonCode);
                decimal unhealthySeconds = (decimal)(_context.Clock.Now - _unhealthySince.Value).TotalSeconds;
                if (unhealthySeconds >= _parameters.MarketDataRecoverySeconds)
                    BeginRiskExit(PaperOrderRole.Flatten, "MarketDataRecoveryTimeout", _parameters.ShockCooldownSeconds);
                else
                    _reason = _lastAssessment.PrimaryReasonCode;
                TryAdvanceRiskExit();
                SetProtectedState();
                Publish();
                return;
            }

            _unhealthySince = null;
            if (_pendingExitRole is null && IsFixedStopHit(position, marketEvent))
                BeginRiskExit(PaperOrderRole.StopLoss, "FixedStopLoss", _parameters.StopLossCooldownSeconds);
            else if (_pendingExitRole is null && _oneLegStartedAt.HasValue &&
                     (_context.Clock.Now - _oneLegStartedAt.Value).TotalSeconds >= _parameters.MaxOneLegSeconds)
                BeginRiskExit(PaperOrderRole.Flatten, "OneLegTimeout", _parameters.CycleCooldownSeconds);

            if (_pendingExitRole is not null)
            {
                TryAdvanceRiskExit();
                SetProtectedState();
                Publish();
                return;
            }
        }

        if (_coordinator.Phase is QueueCyclePhase.WorkingBoth or QueueCyclePhase.WorkingOneLeg or
            QueueCyclePhase.CancellingForReprice or QueueCyclePhase.CancellingForExit)
        {
            if (currentBookHealthy && ManagePreFillReversal(marketEvent.Timestamp))
            {
                ApplyCoordinatorState();
                if (State is not StrategyLifecycleState.Faulted and not StrategyLifecycleState.Suspended)
                    State = _paused ? StrategyLifecycleState.Suspended : StrategyLifecycleState.Running;
                _reason = _coordinator.ReasonCode;
                Publish();
                return;
            }
            if (currentBookHealthy) _coordinator.EvaluateReprices(marketEvent);
            ApplyCoordinatorState();
            if (State is not StrategyLifecycleState.Faulted and not StrategyLifecycleState.Suspended)
                State = _paused ? StrategyLifecycleState.Suspended : StrategyLifecycleState.Running;
            _reason = _coordinator.IsClosingBlocked || currentBookHealthy
                ? _coordinator.ReasonCode
                : _lastAssessment.PrimaryReasonCode;
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
        if (_lastAssessment.Direction == CycleDirection.Neutral)
        {
            Publish();
            return;
        }
        if (Position().NetQuantity != 0)
        {
            State = StrategyLifecycleState.Suspended;
            _reason = "PositionNotFlat";
            Publish();
            return;
        }

        _activeCandidateId = $"{InstanceId}-cycle-{_coordinator.CycleNumber + 1}-candidate";
        _acceptDecisionPending = false;
        if (_coordinator.PrepareCycle(
            _lastAssessment.Direction,
            _lastAssessment,
            _context.Universe.Tradable.TickSize,
            marketEvent))
        {
            State = StrategyLifecycleState.Running;
            _reason = "SignalAccepted";
            _activeCandidateId = $"{InstanceId}-{_coordinator.CurrentCycleId}-candidate";
            _acceptDecisionPending = true;
            Publish();
            _coordinator.ActivatePreparedCycle();
            ApplyCoordinatorState();
            if (State is not StrategyLifecycleState.Faulted and not StrategyLifecycleState.Suspended)
                State = StrategyLifecycleState.Running;
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
        ApplyCoordinatorState();
        if (execution.Role is PaperOrderRole.EntryLong or PaperOrderRole.EntryShort)
        {
            PositionSnapshot position = Position();
            if (position.NetQuantity != 0)
            {
                EnsureExposureTimingAndStop(position, execution.Timestamp);
                if (_stopRequested && _pendingExitRole is null)
                    BeginRiskExit(PaperOrderRole.Flatten, "StopRequested", 0);
            }
        }
        TryAdvanceRiskExit();
        TryCompleteCycle();
        TryCompleteStop();
        Publish();
    }

    public void OnHeartbeat(DateTimeOffset now)
    {
        if (_context is null || _directionWindow is null || _coordinator is null ||
            State is StrategyLifecycleState.Stopped or StrategyLifecycleState.Faulted)
            return;

        if (_coordinator.IsClosingBlocked)
        {
            State = StrategyLifecycleState.Suspended;
            _reason = _coordinator.ReasonCode;
            Publish();
            return;
        }

        if (_cooldownUntil.HasValue)
        {
            if (now < _cooldownUntil.Value)
            {
                State = _paused ? StrategyLifecycleState.Suspended : StrategyLifecycleState.Running;
                _reason = "CoolingDown";
                Publish();
                return;
            }

            _cooldownUntil = null;
            _coordinator.ResetAfterCooldown();
            _directionWindow = new DirectionWindow(_context.Universe.Tradable.TickSize, _parameters);
            State = _paused ? StrategyLifecycleState.Suspended : StrategyLifecycleState.WarmingUp;
            _reason = "WarmupIncomplete";
            Publish();
            return;
        }

        PositionSnapshot position = Position();
        if (position.NetQuantity == 0)
        {
            TryAdvanceRiskExit();
            TryCompleteCycle();
            TryCompleteStop();
            Publish();
            return;
        }

        EnsureExposureTimingAndStop(position, _oneLegStartedAt ?? now);
        DateTimeOffset staleAt = (_lastHealthyBook?.Timestamp ?? now)
            .AddSeconds((double)_parameters.QuoteFreshnessSeconds);
        if (now > staleAt)
        {
            if (!_unhealthySince.HasValue || _unhealthySince.Value > staleAt) _unhealthySince = staleAt;
            _coordinator.BeginRiskCancellation("StaleQuote");
            if ((decimal)(now - _unhealthySince.Value).TotalSeconds >= _parameters.MarketDataRecoverySeconds)
                BeginRiskExit(PaperOrderRole.Flatten, "MarketDataRecoveryTimeout", _parameters.ShockCooldownSeconds);
            else
                _reason = "StaleQuote";
        }
        else if (_pendingExitRole is null && _oneLegStartedAt.HasValue &&
                 (now - _oneLegStartedAt.Value).TotalSeconds >= _parameters.MaxOneLegSeconds)
        {
            BeginRiskExit(PaperOrderRole.Flatten, "OneLegTimeout", _parameters.CycleCooldownSeconds);
        }

        TryAdvanceRiskExit();
        SetProtectedState();
        TryCompleteStop();
        Publish();
    }

    public void OnOrderLifecycle(OrderLifecycleEvent orderEvent)
    {
        if (!string.Equals(orderEvent.StrategyInstanceId, InstanceId, StringComparison.OrdinalIgnoreCase)) return;
        _managedOrderBook?.Apply(orderEvent);
        _coordinator?.OnOrderLifecycle(orderEvent);
        _reason = _coordinator?.ReasonCode ?? _reason;
        ApplyCoordinatorState();
        TryAdvanceRiskExit();
        TryCompleteCycle();
        TryCompleteStop();
        Publish();
    }

    private QueueCycleOrderCoordinator.QueueCycleCoordinatorCheckpoint? _restoreCoordinator;
    private DirectionWindowCheckpoint? _restoreDirectionWindow;

    public void OnOrderSubmitted(PaperOrderRequest request) => _managedOrderBook?.Track(request);

    public StrategyRecoveryCheckpoint CaptureRecoveryCheckpoint()
    {
        if (_strategyRunGeneration <= 0) throw new InvalidOperationException("PQC has no strategy run generation to checkpoint.");
        var state = new RecoveryState(_managedOrderBook?.Capture() ?? new ManagedOrderBookCheckpoint(InstanceId,
                _strategyRunGeneration, 0, Array.Empty<ManagedOrderRecord>()),
            _directionWindow?.Capture(), _coordinator?.Capture(), _lastBook, _lastHealthyBook,
            _lastObservedBookTimestamp, _lastObservedBookSequence, _reason, _oppositeDirectionSeconds,
            _lastOppositeUtcSecond, _paused, _stopRequested, _stopReason, _oneLegStartedAt, _unhealthySince,
            _cooldownUntil, _pendingExitRole, _pendingExitReason, _pendingCooldownSeconds, _exitRequested,
            _fixedStopPrice, _activeCandidateId, _acceptDecisionPending, State);
        return new StrategyRecoveryCheckpoint(StrategyType, InstanceId, _strategyRunGeneration, RecoverySchema,
            _context?.Clock.Now ?? DateTimeOffset.Now, JsonSerializer.Serialize(state));
    }

    public void RestoreRecoveryCheckpoint(StrategyRecoveryCheckpoint checkpoint)
    {
        long expectedRun = _context?.StrategyRunGeneration ?? checkpoint.StrategyRunGeneration;
        StrategyRecoveryCheckpointValidator.EnsureCompatible(checkpoint, StrategyType, InstanceId, expectedRun, RecoverySchema);
        RecoveryState saved = JsonSerializer.Deserialize<RecoveryState>(checkpoint.PayloadJson)
            ?? throw new InvalidOperationException("PQC recovery payload is empty.");
        _strategyRunGeneration = checkpoint.StrategyRunGeneration;
        _managedOrderBook = new ManagedStrategyOrderBook(InstanceId, _strategyRunGeneration);
        _managedOrderBook.Restore(saved.OrderBook);
        _lastBook = saved.LastBook; _lastHealthyBook = saved.LastHealthyBook;
        _lastObservedBookTimestamp = saved.LastObservedBookTimestamp; _lastObservedBookSequence = saved.LastObservedBookSequence;
        _reason = saved.Reason; _oppositeDirectionSeconds = saved.OppositeDirectionSeconds;
        _lastOppositeUtcSecond = saved.LastOppositeUtcSecond; _paused = saved.Paused;
        _stopRequested = saved.StopRequested; _stopReason = saved.StopReason; _oneLegStartedAt = saved.OneLegStartedAt;
        _unhealthySince = saved.UnhealthySince; _cooldownUntil = saved.CooldownUntil;
        _pendingExitRole = saved.PendingExitRole; _pendingExitReason = saved.PendingExitReason;
        _pendingCooldownSeconds = saved.PendingCooldownSeconds; _exitRequested = saved.ExitRequested;
        _fixedStopPrice = saved.FixedStopPrice; _activeCandidateId = saved.ActiveCandidateId;
        _acceptDecisionPending = saved.AcceptDecisionPending; State = saved.State;
        _restoreCoordinator = saved.Coordinator; _restoreDirectionWindow = saved.DirectionWindow;
        _restoreBeforeStart = true;
    }

    public void Pause(string reason)
    {
        if (State is StrategyLifecycleState.Stopped or StrategyLifecycleState.Faulted) return;
        _paused = true;
        string pauseReason = string.IsNullOrWhiteSpace(reason) ? "使用者暫停" : reason;
        if (Position().NetQuantity == 0 || _coordinator?.IsClosingBlocked == true)
            _coordinator?.CancelAllWorking("Paused");
        ApplyCoordinatorState();
        if (State != StrategyLifecycleState.Faulted && State != StrategyLifecycleState.Suspended)
            State = StrategyLifecycleState.Suspended;
        if (_coordinator?.IsClosingBlocked != true && State != StrategyLifecycleState.Faulted)
            _reason = pauseReason;
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
        if (Position().NetQuantity != 0)
            BeginRiskExit(PaperOrderRole.Flatten, "StopRequested", 0);
        _coordinator?.CancelAllWorking("StopRequested");
        ApplyCoordinatorState();
        if (State == StrategyLifecycleState.Faulted)
        {
            Publish();
            return;
        }
        if (_coordinator?.IsClosingBlocked != true)
        {
            State = StrategyLifecycleState.Suspended;
            _reason = "StopPending";
        }
        TryAdvanceRiskExit();
        TryCompleteStop();
        Publish();
    }

    public StrategySnapshot GetSnapshot()
    {
        PositionSnapshot position = Position();
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
            CenterPrice = _lastBook?.MidPrice ?? 0m,
            Range = _lastAssessment?.PathTicks ?? 0m,
            BuyEntry = _coordinator?.BuyWorkingPrice ?? 0m,
            SellEntry = _coordinator?.SellWorkingPrice ?? 0m,
            StopPrice = _fixedStopPrice,
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
    private void Publish()
    {
        SnapshotChanged?.Invoke(GetSnapshot());
        PublishDecisionObservation();
    }

    private void PublishDecisionObservation()
    {
        if (_context is null) return;
        DirectionAssessment? assessment = _lastAssessment;
        string primaryReason = StableReason(_reason);
        if (!_acceptDecisionPending && primaryReason == "SignalAccepted")
        {
            primaryReason = _coordinator?.Phase switch
            {
                QueueCyclePhase.WorkingBoth => "WorkingBoth",
                QueueCyclePhase.WorkingOneLeg => "OneLegFilled",
                _ => primaryReason
            };
        }
        StrategyDecision decision = primaryReason switch
        {
            "WarmupIncomplete" => StrategyDecision.Warmup,
            "NeutralDirection" or "StaleQuote" or "InvalidBook" or "InsufficientSideDepth" or
                "InsufficientNearDepth" or "DepthImbalance" or "SpreadTooWide" or "VolatilityTooHigh" => StrategyDecision.Reject,
            "FixedStopLoss" or "OneLegTimeout" or "MarketDataRecoveryTimeout" or
                "SignalReversedBeforeFill" => StrategyDecision.Exit,
            "DailyLossLocked" or "ClosingBlocked" => StrategyDecision.Halt,
            _ when State is StrategyLifecycleState.Stopped or StrategyLifecycleState.Faulted => StrategyDecision.Halt,
            _ => StrategyDecision.Hold
        };
        if (_acceptDecisionPending)
        {
            decision = StrategyDecision.Accept;
            primaryReason = "SignalAccepted";
        }

        decimal? quoteAge = _lastBook is null
            ? null
            : Math.Max(0m, (decimal)(_context.Clock.Now - _lastBook.Timestamp).Ticks / TimeSpan.TicksPerSecond);
        string cycleId = _coordinator?.CurrentCycleId ?? string.Empty;
        if (cycleId.Length == 0 && decision == StrategyDecision.Accept && _coordinator is not null)
            cycleId = $"cycle-{_coordinator.CycleNumber + 1}";
        string candidateId = _activeCandidateId;
        var features = new Dictionary<string, decimal?>
        {
            ["netMoveTicks"] = assessment?.NetMoveTicks,
            ["pathTicks"] = assessment?.PathTicks,
            ["trendEfficiency"] = assessment?.TrendEfficiency,
            ["averageObi"] = assessment?.AverageObi,
            ["volatilityTicks"] = assessment?.VolatilityTicks,
            ["buyWorkingPrice"] = _coordinator?.BuyWorkingPrice,
            ["sellWorkingPrice"] = _coordinator?.SellWorkingPrice,
            ["buyRepriceThresholdB"] = _coordinator?.BuyRepriceThresholdB,
            ["buyRepriceTargetC"] = _coordinator?.BuyRepriceTargetC,
            ["sellRepriceThresholdB"] = _coordinator?.SellRepriceThresholdB,
            ["sellRepriceTargetC"] = _coordinator?.SellRepriceTargetC
        };
        var thresholds = assessment?.Thresholds.Select(threshold => new ThresholdObservation(
            threshold.Name,
            threshold.Observed,
            threshold.Required,
            ThresholdUnit(threshold.Name),
            threshold.Passed)).ToArray() ?? Array.Empty<ThresholdObservation>();
        IReadOnlyList<string> secondary = assessment?.OtherFailedReasonCodes ?? Array.Empty<string>();
        bool healthFailure = IsHealthFailure(primaryReason);
        var unavailable = new Dictionary<string, string>();
        if (_lastBook is null)
        {
            unavailable["requiredProductsPresent"] = "NoBookData";
            unavailable["quoteAgeSeconds"] = "NoBookData";
            unavailable["spreadTicks"] = "NoBookData";
        }
        unavailable["crossProductReceiveSkewSeconds"] = "NotApplicableSingleProduct";
        DecisionObserved?.Invoke(new StrategyDecisionObservation
        {
            Sequence = _lastBook?.Sequence ?? 0L,
            Timestamp = _context.Clock.Now,
            StrategyType = StrategyType,
            StrategyInstanceId = InstanceId,
            CandidateId = candidateId,
            IsCandidate = _activeCandidateId.Length > 0 &&
                (cycleId.Length > 0 || assessment?.Direction != CycleDirection.Neutral),
            CycleId = cycleId,
            Symbol = _context.Universe.Tradable.Symbol,
            Direction = assessment?.Direction.ToString() ?? CycleDirection.Neutral.ToString(),
            State = State,
            Decision = decision,
            Features = features,
            Thresholds = thresholds,
            PrimaryReasonCode = primaryReason,
            SecondaryReasonCodes = secondary,
            QuoteAgeSeconds = quoteAge,
            SpreadTicks = assessment?.SpreadTicks,
            RequiredProductsPresent = _lastBook is null ? null : true,
            DataQualityFlags = healthFailure ? new[] { primaryReason } : Array.Empty<string>(),
            UnavailableReasonCode = _lastBook is null ? "NoBookData" : null,
            UnavailableReasons = unavailable
        });
        _acceptDecisionPending = false;
    }

    private static string ThresholdUnit(string name) => name switch
    {
        "MinSideDepth" or "MinNearDepth" or "DirectionWindowSeconds" => "count",
        "QuoteFreshnessSeconds" => "seconds",
        "MinDepthRatio" or "MinTrendEfficiency" or "UpMinDepthObi" or "DownMinDepthObi" => "ratio",
        _ => "ticks"
    };

    private static string StableReason(string reason) => reason switch
    {
        "使用者暫停" => "Paused",
        "尚未啟動" => "NotStarted",
        _ when string.IsNullOrWhiteSpace(reason) => "ReasonUnavailable",
        _ => reason
    };

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

    private void EnsureExposureTimingAndStop(PositionSnapshot position, DateTimeOffset timestamp)
    {
        _oneLegStartedAt ??= timestamp;
        if (_context is null || _fixedStopPrice > 0m || position.AveragePrice <= 0m) return;
        decimal distance = _parameters.StopLossTicks * _context.Universe.Tradable.TickSize;
        _fixedStopPrice = position.NetQuantity > 0
            ? position.AveragePrice - distance
            : position.AveragePrice + distance;
    }

    private bool IsFixedStopHit(PositionSnapshot position, MarketEvent book)
    {
        if (_fixedStopPrice <= 0m) return false;
        return position.NetQuantity > 0
            ? book.BestBid <= _fixedStopPrice
            : book.BestAsk >= _fixedStopPrice;
    }

    private void BeginRiskExit(PaperOrderRole role, string reasonCode, int cooldownSeconds)
    {
        if (_coordinator is null || _pendingExitRole is not null) return;
        _pendingExitRole = role;
        _pendingExitReason = reasonCode;
        _pendingCooldownSeconds = cooldownSeconds;
        _reason = reasonCode;
        _coordinator.BeginRiskCancellation(reasonCode);
    }

    private void TryAdvanceRiskExit()
    {
        if (_coordinator is null || _context is null || _pendingExitRole is null || State == StrategyLifecycleState.Faulted)
            return;

        PositionSnapshot position = Position();
        if (position.NetQuantity == 0)
        {
            if (_stopRequested)
            {
                TryCompleteStop();
                return;
            }
            if (_exitRequested)
                EnterCooldown(_pendingCooldownSeconds);
            else if (_coordinator.HasBothEntryFills)
                EnterCooldown(_parameters.CycleCooldownSeconds);
            return;
        }
        if (!_coordinator.EntryOrdersTerminal || _exitRequested) return;

        MarketEvent? priceBook = _lastHealthyBook;
        if (_pendingExitRole == PaperOrderRole.StopLoss && !HasFreshCounterpartyQuote(priceBook))
            return;

        decimal price = position.NetQuantity > 0 ? priceBook?.BestBid ?? 0m : priceBook?.BestAsk ?? 0m;
        if (price <= 0m)
        {
            if (_pendingExitRole == PaperOrderRole.StopLoss) return;
            price = position.AveragePrice;
        }

        PaperOrderType type = _pendingExitRole == PaperOrderRole.StopLoss
            ? PaperOrderType.BrokerLimit
            : PaperOrderType.Market;
        _exitRequested = _coordinator.TrySubmitExit(
            position, _pendingExitRole.Value, type, price, _pendingExitReason ?? "RiskExit");
        if (_exitRequested) _reason = _pendingExitReason ?? "RiskExit";
    }

    private void TryCompleteCycle()
    {
        if (_coordinator is null || _cooldownUntil.HasValue || Position().NetQuantity != 0) return;
        if (_exitRequested && _pendingExitRole is not null)
        {
            if (!_coordinator.HasOutstandingOrders) EnterCooldown(_pendingCooldownSeconds);
            return;
        }
        if (_coordinator.HasBothEntryFills && !_coordinator.HasOutstandingOrders)
            EnterCooldown(_parameters.CycleCooldownSeconds);
    }

    private void EnterCooldown(int seconds)
    {
        if (_coordinator is null || _context is null || _cooldownUntil.HasValue) return;
        _coordinator.EnterCooldown();
        _cooldownUntil = _context.Clock.Now.AddSeconds(seconds);
        _oneLegStartedAt = null;
        _unhealthySince = null;
        _pendingExitRole = null;
        _pendingExitReason = null;
        _pendingCooldownSeconds = 0;
        _exitRequested = false;
        _fixedStopPrice = 0m;
        _activeCandidateId = string.Empty;
        _acceptDecisionPending = false;
        State = _paused ? StrategyLifecycleState.Suspended : StrategyLifecycleState.Running;
        _reason = "CoolingDown";
    }

    private void SetProtectedState()
    {
        ApplyCoordinatorState();
        if (State == StrategyLifecycleState.Faulted)
        {
            return;
        }
        if (_coordinator?.IsClosingBlocked == true)
        {
            State = StrategyLifecycleState.Suspended;
            _reason = _coordinator.ReasonCode;
        }
        else
        {
            State = _paused ? StrategyLifecycleState.Suspended : StrategyLifecycleState.Running;
            if (_pendingExitReason is not null) _reason = _pendingExitReason;
        }
    }

    private void ApplyCoordinatorState()
    {
        if (_coordinator?.IsFaulted == true)
        {
            State = StrategyLifecycleState.Faulted;
            _reason = _coordinator.ReasonCode;
        }
        else if (_coordinator?.IsClosingBlocked == true)
        {
            State = StrategyLifecycleState.Suspended;
            _reason = _coordinator.ReasonCode;
        }
    }

    private bool HasFreshCounterpartyQuote(MarketEvent? book)
    {
        if (_context is null || book is null || book.Kind != MarketEventKind.Book ||
            book.BestBid <= 0m || book.BestAsk <= 0m || book.BestBid >= book.BestAsk)
            return false;

        decimal ageSeconds = (decimal)(_context.Clock.Now - book.Timestamp).Ticks / TimeSpan.TicksPerSecond;
        return ageSeconds >= 0m && ageSeconds <= _parameters.QuoteFreshnessSeconds;
    }

    private static bool IsHealthFailure(string reasonCode) => reasonCode is
        "StaleQuote" or "InvalidBook" or "InsufficientSideDepth" or
        "InsufficientNearDepth" or "DepthImbalance" or "SpreadTooWide" or
        "VolatilityTooHigh";
}
