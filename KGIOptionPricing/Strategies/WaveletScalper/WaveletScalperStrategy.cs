using System.Globalization;
using System.Text.Json;
using KGIOptionPricing.OrderManagement.Core;
using KGIOptionPricing.StrategyFramework;
using KGIOptionPricing.StrategyFramework.Recovery;

namespace KGIOptionPricing.Strategies.WaveletScalper;

public sealed class WaveletScalperFactory : IStrategyFactory
{
    public const string TypeName = "WaveletScalper";
    public string StrategyType => TypeName;
    public string DisplayName => "數秒級 MODWT 均值回歸（模擬）";
    public ISimulationStrategy Create(StrategyDefinition definition) => new WaveletScalperStrategy(definition);
}

/// <summary>
/// Causal Haar-MODWT level-1 mean-reversion scalper. It removes the D1 detail from
/// the latest price, then validates a deviation with top-of-book order imbalance.
/// This is a paper-only strategy and emits market-on-next-trade paper orders.
/// </summary>
public sealed class WaveletScalperStrategy : ISimulationStrategy, IManagedOrderStrategy,
    IManagedOrderSubmissionObserver, IStrategyRecoveryParticipant
{
    private sealed record RecoveryState(decimal[] D1Details, KeyValuePair<string, MarketEvent>[] Latest,
        decimal PreviousPrice, decimal Smooth, decimal Sigma, decimal EntryPrice, decimal StopPrice,
        decimal EntryObi, DateTimeOffset CooldownUntil, DateTimeOffset EntryAt, BuySell EntrySide,
        bool EntryWorking, bool ExitWorking, int CompletedTrades, string Reason, StrategyLifecycleState State,
        int CycleSequence, string CurrentCycleId, ManagedOrderBookCheckpoint OrderBook);
    private readonly StrategyDefinition _definition;
    private readonly Queue<decimal> _d1Details = new();
    private readonly Dictionary<string, MarketEvent> _latest = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _windowTicks, _stopTicks, _maxSpreadTicks, _freshnessSeconds, _maxHoldingSeconds, _cooldownSeconds;
    private readonly decimal _entrySigma, _obiThreshold, _energyMultiplier;
    private StrategyRuntimeContext? _context;
    private decimal _previousPrice, _smooth, _sigma, _entryPrice, _stopPrice, _entryObi;
    private DateTimeOffset _cooldownUntil, _entryAt;
    private BuySell _entrySide;
    private bool _entryWorking, _exitWorking;
    private int _completedTrades;
    private string _reason = "尚未啟動";
    private ManagedStrategyOrderBook? _orderBook;
    private long _strategyRunGeneration;
    private int _cycleSequence;
    private string _currentCycleId = string.Empty;
    private bool _restoredBeforeStart;

    public string StrategyType => WaveletScalperFactory.TypeName;
    public string InstanceId => _definition.InstanceId;
    public StrategyLifecycleState State { get; private set; } = StrategyLifecycleState.Created;
    public event Action<PaperOrderRequest>? OrderRequested;
    public event Action? CancelAllRequested;
    public event Action<StrategySnapshot>? SnapshotChanged;
    public event Action<string>? CancelOrderRequested;
    public string RecoverySchema => "wavelet-scalper/v1";

    public WaveletScalperStrategy(StrategyDefinition definition)
    {
        _definition = definition;
        _windowTicks = Int("WaveletWindowTicks", 64, 8, 10000);
        _entrySigma = Decimal("EntrySigma", 2m, 0.1m, 10m);
        _obiThreshold = Decimal("ObiThreshold", 0.60m, 0.01m, 0.99m);
        _stopTicks = Int("StopTicks", 3, 1, 1000);
        _energyMultiplier = Decimal("EnergyMultiplier", 1.5m, 1.01m, 10m);
        _maxSpreadTicks = Int("MaximumSpreadTicks", 1, 1, 1000);
        _freshnessSeconds = Int("FreshnessSeconds", 2, 1, 60);
        _maxHoldingSeconds = Int("MaxHoldingSeconds", 10, 1, 300);
        _cooldownSeconds = Int("CooldownSeconds", 2, 0, 300);
    }

    public void Start(StrategyRuntimeContext context)
    {
        if (State != StrategyLifecycleState.Created && !_restoredBeforeStart) throw new InvalidOperationException("策略已啟動。");
        _context = context;
        if (_orderBook == null)
        {
            _strategyRunGeneration = context.StrategyRunGeneration;
            _orderBook = new ManagedStrategyOrderBook(InstanceId, _strategyRunGeneration);
        }
        else if (_strategyRunGeneration != context.StrategyRunGeneration)
            throw new InvalidOperationException("MODWT recovery run generation does not match runtime.");
        if (_restoredBeforeStart) _restoredBeforeStart = false;
        else
        {
            State = StrategyLifecycleState.WarmingUp;
            _reason = $"累積 {_windowTicks} 個 MODWT D1 樣本";
        }
        Publish();
    }

    public void OnMarketEvent(MarketEvent marketEvent)
    {
        if (_context == null || State is StrategyLifecycleState.Stopped or StrategyLifecycleState.Faulted) return;
        _latest[marketEvent.Symbol] = marketEvent;
        if (!IsTradable(marketEvent.Symbol) || marketEvent.Kind != MarketEventKind.Trade || marketEvent.LastPrice <= 0m)
        {
            Publish();
            return;
        }

        UpdateModwt(marketEvent.LastPrice);
        PositionSnapshot position = Position();
        if (position.NetQuantity != 0)
        {
            ManagePosition(marketEvent, position);
            Publish();
            return;
        }
        if (_entryWorking || _exitWorking) { Publish(); return; }
        if (marketEvent.Timestamp < _cooldownUntil)
        {
            State = StrategyLifecycleState.Ready;
            _reason = $"冷卻至 {_cooldownUntil:HH:mm:ss.fff}";
            Publish();
            return;
        }
        if (!TryValidate(marketEvent, out string validationReason))
        {
            State = _d1Details.Count < _windowTicks ? StrategyLifecycleState.WarmingUp : StrategyLifecycleState.Suspended;
            _reason = validationReason;
            Publish();
            return;
        }

        decimal deviation = marketEvent.LastPrice - _smooth;
        decimal obi = OrderBookImbalance(marketEvent);
        decimal threshold = _entrySigma * _sigma;
        if (deviation < -threshold && obi >= _obiThreshold)
            SubmitEntry(BuySell.Buy, marketEvent, obi);
        else if (deviation > threshold && obi <= -_obiThreshold)
            SubmitEntry(BuySell.Sell, marketEvent, obi);
        else
        {
            State = StrategyLifecycleState.Ready;
            _reason = $"等待偏離／OBI：d={deviation:F2}，σD1={_sigma:F2}，OBI={obi:F2}";
        }
        Publish();
    }

    public void OnExecution(ExecutionEvent execution)
    {
        if (State is StrategyLifecycleState.Stopped or StrategyLifecycleState.Faulted ||
            !string.Equals(execution.StrategyInstanceId, InstanceId, StringComparison.OrdinalIgnoreCase)) return;
        if (execution.Role is PaperOrderRole.EntryLong or PaperOrderRole.EntryShort && string.IsNullOrEmpty(_currentCycleId))
            _currentCycleId = string.IsNullOrWhiteSpace(execution.CycleId) ? $"cycle-{++_cycleSequence}" : execution.CycleId;
        if (execution.Role is PaperOrderRole.EntryLong or PaperOrderRole.EntryShort)
        {
            _entryWorking = false;
            _entrySide = execution.Side;
            _entryPrice = execution.Price;
            _entryAt = execution.Timestamp;
            _stopPrice = execution.Side == BuySell.Buy
                ? execution.Price - _stopTicks * TickSize()
                : execution.Price + _stopTicks * TickSize();
            State = StrategyLifecycleState.Running;
            _reason = $"{(execution.Side == BuySell.Buy ? "多" : "空")}單成交；OBI={_entryObi:F2}，等待回歸均衡線";
        }
        else if (execution.Role is PaperOrderRole.TakeProfit or PaperOrderRole.StopLoss or PaperOrderRole.Flatten)
        {
            _exitWorking = false;
            _completedTrades++;
            _cooldownUntil = execution.Timestamp.AddSeconds(_cooldownSeconds);
            _entryPrice = _stopPrice = 0m;
            State = StrategyLifecycleState.Ready;
            _reason = execution.Role == PaperOrderRole.TakeProfit ? "回歸均衡線平倉" : "風險平倉，進入冷卻";
        }
        Publish();
    }

    public void Stop(string reason)
    {
        CancelActiveOrders();
        _entryWorking = _exitWorking = false;
        State = StrategyLifecycleState.Stopped;
        _reason = reason;
        Publish();
    }

    public void Pause(string reason)
    {
        if (State is StrategyLifecycleState.Stopped or StrategyLifecycleState.Faulted) return;
        if (Position().NetQuantity == 0)
        {
            CancelActiveOrders();
            _entryWorking = _exitWorking = false;
        }
        State = StrategyLifecycleState.Suspended;
        _reason = reason;
        Publish();
    }

    public void Resume()
    {
        if (State != StrategyLifecycleState.Suspended) return;
        State = _d1Details.Count < _windowTicks ? StrategyLifecycleState.WarmingUp : StrategyLifecycleState.Ready;
        _reason = State == StrategyLifecycleState.Ready ? "已繼續，等待下一筆行情" : $"繼續暖機：{_d1Details.Count}/{_windowTicks} 個 D1";
        Publish();
    }

    public StrategySnapshot GetSnapshot()
    {
        PositionSnapshot position = Position();
        decimal last = _context != null && _latest.TryGetValue(_context.Universe.Tradable.Symbol, out MarketEvent? market)
            ? market.LastPrice : 0m;
        string references = _context == null ? string.Empty : string.Join(", ", _context.Universe.References.Select(x =>
            _latest.TryGetValue(x.Symbol, out MarketEvent? item) ? $"{x.ProductId}:{item.LastPrice}" : $"{x.ProductId}:--"));
        decimal band = _entrySigma * _sigma;
        return new StrategySnapshot
        {
            StrategyType = StrategyType, InstanceId = InstanceId,
            ParameterSummary = $"Window={_windowTicks}; EntrySigma={_entrySigma}; OBI={_obiThreshold}; Stop={_stopTicks}t; Energy={_energyMultiplier}; MaxSpread={_maxSpreadTicks}t; Fresh={_freshnessSeconds}s; Hold={_maxHoldingSeconds}s; Cooldown={_cooldownSeconds}s",
            TradableProductId = _context?.Universe.Tradable.ProductId ?? _definition.Universe.Tradable.ProductId,
            TradableSymbol = _context?.Universe.Tradable.Symbol ?? string.Empty,
            ReferenceSummary = references, State = State, Reason = _reason, LastPrice = last,
            CenterPrice = _smooth, Range = _sigma,
            BuyEntry = RoundDown(_smooth - band), SellEntry = RoundUp(_smooth + band), StopPrice = _stopPrice,
            Position = position.NetQuantity, RealizedPnl = position.RealizedPnl, UnrealizedPnl = position.UnrealizedPnl,
            NetPnl = position.NetPnl, CompletedTrades = _completedTrades,
            UpdatedAt = _context?.Clock.Now ?? DateTimeOffset.Now
        };
    }

    private void UpdateModwt(decimal price)
    {
        if (_previousPrice != 0m)
        {
            // Haar MODWT D1 detail. Removing D1 reconstructs the level-1 smooth price.
            decimal d1 = (price - _previousPrice) / 2m;
            _d1Details.Enqueue(d1);
            while (_d1Details.Count > _windowTicks) _d1Details.Dequeue();
            _smooth = price - d1;
            _sigma = StandardDeviation(_d1Details);
        }
        _previousPrice = price;
    }

    private void ManagePosition(MarketEvent marketEvent, PositionSnapshot position)
    {
        if (_exitWorking) return;
        bool longPosition = position.NetQuantity > 0;
        decimal executableExit = longPosition ? marketEvent.BestBid : marketEvent.BestAsk;
        if (executableExit <= 0m) executableExit = marketEvent.LastPrice;
        // Reaching the theoretical smooth price is not a take-profit if the executable
        // bid/ask would still close below the entry because of the spread.
        bool profitableExit = longPosition ? executableExit > _entryPrice : executableExit < _entryPrice;
        bool targetHit = (longPosition ? marketEvent.LastPrice >= _smooth : marketEvent.LastPrice <= _smooth) && profitableExit;
        bool hardStop = longPosition ? marketEvent.LastPrice <= _stopPrice : marketEvent.LastPrice >= _stopPrice;
        decimal latestD1 = _d1Details.Count == 0 ? 0m : _d1Details.Last();
        bool energyBreak = _sigma > 0m && Math.Abs(latestD1) >= _energyMultiplier * _sigma;
        bool timeStop = _entryAt != default && (marketEvent.Timestamp - _entryAt).TotalSeconds >= _maxHoldingSeconds;
        if (!targetHit && !hardStop && !energyBreak && !timeStop)
        {
            State = StrategyLifecycleState.Running;
            _reason = $"持倉等待回歸：P={marketEvent.LastPrice}，Psmooth={_smooth:F2}";
            return;
        }
        // Only a fixed adverse price stop is a StopLoss.  A volatility-break or time
        // safeguard can close with a profit, so preserve it as a neutral Flatten exit.
        PaperOrderRole role = targetHit ? PaperOrderRole.TakeProfit : hardStop ? PaperOrderRole.StopLoss : PaperOrderRole.Flatten;
        SubmitMarket(longPosition ? BuySell.Sell : BuySell.Buy, role, marketEvent);
        _exitWorking = true;
        _reason = role == PaperOrderRole.TakeProfit ? "觸及均衡線，送出模擬市價平倉" : "觸發風控，送出模擬市價平倉";
    }

    private void SubmitEntry(BuySell side, MarketEvent marketEvent, decimal obi)
    {
        _entryWorking = true;
        _entryObi = obi;
        _currentCycleId = $"cycle-{++_cycleSequence}";
        SubmitMarket(side, side == BuySell.Buy ? PaperOrderRole.EntryLong : PaperOrderRole.EntryShort, marketEvent);
        State = StrategyLifecycleState.Running;
        _reason = $"訊號成立：{(side == BuySell.Buy ? "超賣做多" : "超買做空")}，OBI={obi:F2}";
    }

    private void SubmitMarket(BuySell side, PaperOrderRole role, MarketEvent marketEvent)
    {
        decimal price = side == BuySell.Buy ? marketEvent.BestAsk : marketEvent.BestBid;
        if (price <= 0m) price = marketEvent.LastPrice;
        OrderRequested?.Invoke(new PaperOrderRequest
        {
            ClientOrderId = (_orderBook ?? throw new InvalidOperationException("Order book not initialized."))
                .NextClientOrderId(role.ToString(), _currentCycleId),
            CycleId = _currentCycleId,
            StrategyRunGeneration = _strategyRunGeneration,
            Generation = 0,
            StrategyInstanceId = InstanceId,
            Symbol = _context!.Universe.Tradable.Symbol,
            Side = side,
            Type = PaperOrderType.Market,
            Role = role,
            Price = price,
            Quantity = 1
        });
    }

    private bool TryValidate(MarketEvent marketEvent, out string reason)
    {
        if (_d1Details.Count < _windowTicks) { reason = $"暖機中：{_d1Details.Count}/{_windowTicks} 個 D1"; return false; }
        if (_sigma <= 0m) { reason = "D1 波動不足"; return false; }
        if (marketEvent.BestBid <= 0m || marketEvent.BestAsk <= marketEvent.BestBid) { reason = "委託簿無效"; return false; }
        if ((marketEvent.BestAsk - marketEvent.BestBid) / TickSize() > _maxSpreadTicks) { reason = "交易商品價差過大"; return false; }
        foreach (ResolvedProduct product in _context!.Universe.References)
        {
            if (!_latest.TryGetValue(product.Symbol, out MarketEvent? reference) ||
                (marketEvent.Timestamp - reference.Timestamp).TotalSeconds > _freshnessSeconds)
            { reason = $"{product.ProductId} 行情過期"; return false; }
        }
        reason = string.Empty;
        return true;
    }

    private bool IsTradable(string symbol) => _context != null && string.Equals(symbol, _context.Universe.Tradable.Symbol, StringComparison.OrdinalIgnoreCase);
    private PositionSnapshot Position() => _context == null ? new PositionSnapshot() : _context.Performance.GetPosition(InstanceId, _context.Universe.Tradable.Symbol);
    private decimal TickSize() => Math.Max(0.00001m, _context?.Universe.Tradable.TickSize ?? 1m);
    private decimal RoundDown(decimal price) => Math.Floor(price / TickSize()) * TickSize();
    private decimal RoundUp(decimal price) => Math.Ceiling(price / TickSize()) * TickSize();
    private void Publish() => SnapshotChanged?.Invoke(GetSnapshot());

    public void OnOrderSubmitted(PaperOrderRequest request) =>
        (_orderBook ?? throw new InvalidOperationException("Order book not initialized.")).Track(request);

    public void OnOrderLifecycle(OrderLifecycleEvent orderEvent)
    {
        if (_orderBook == null || !_orderBook.TryGet(orderEvent.ClientOrderId, out PaperOrderRequest? request) ||
            request == null || !_orderBook.Apply(orderEvent)) return;
        if (orderEvent.State is not (OrderLifecycleState.Cancelled or OrderLifecycleState.Rejected)) return;
        if (request.Role is PaperOrderRole.EntryLong or PaperOrderRole.EntryShort)
        {
            _entryWorking = _orderBook.HasActiveRole(PaperOrderRole.EntryLong) || _orderBook.HasActiveRole(PaperOrderRole.EntryShort);
            if (!_entryWorking && Position().NetQuantity == 0 && State != StrategyLifecycleState.Stopped)
            { State = StrategyLifecycleState.Ready; _reason = "進場單已撤銷／拒絕，等待新訊號"; Publish(); }
        }
        else if (request.Role is PaperOrderRole.TakeProfit or PaperOrderRole.StopLoss or PaperOrderRole.Flatten)
        {
            _exitWorking = _orderBook.HasActiveRole(PaperOrderRole.TakeProfit) || _orderBook.HasActiveRole(PaperOrderRole.StopLoss) ||
                           _orderBook.HasActiveRole(PaperOrderRole.Flatten);
            if (Position().NetQuantity != 0)
            { State = StrategyLifecycleState.Suspended; _reason = "出場委託已撤銷／拒絕，需人工處理"; Publish(); }
        }
    }

    public StrategyRecoveryCheckpoint CaptureRecoveryCheckpoint()
    {
        if (_orderBook == null) throw new InvalidOperationException("MODWT strategy has not initialized its managed order book.");
        var state = new RecoveryState(_d1Details.ToArray(), _latest.ToArray(), _previousPrice, _smooth, _sigma,
            _entryPrice, _stopPrice, _entryObi, _cooldownUntil, _entryAt, _entrySide, _entryWorking, _exitWorking,
            _completedTrades, _reason, State, _cycleSequence, _currentCycleId, _orderBook.Capture());
        return new StrategyRecoveryCheckpoint(StrategyType, InstanceId, _strategyRunGeneration, RecoverySchema,
            DateTimeOffset.UtcNow, JsonSerializer.Serialize(state));
    }

    public void RestoreRecoveryCheckpoint(StrategyRecoveryCheckpoint checkpoint)
    {
        StrategyRecoveryCheckpointValidator.EnsureCompatible(checkpoint, StrategyType, InstanceId,
            checkpoint.StrategyRunGeneration, RecoverySchema);
        RecoveryState state = JsonSerializer.Deserialize<RecoveryState>(checkpoint.PayloadJson)
            ?? throw new InvalidOperationException("MODWT recovery payload is empty.");
        if (_strategyRunGeneration != 0 && _strategyRunGeneration != checkpoint.StrategyRunGeneration)
            throw new InvalidOperationException("MODWT checkpoint run generation conflicts with the runtime.");
        _strategyRunGeneration = checkpoint.StrategyRunGeneration;
        _orderBook ??= new ManagedStrategyOrderBook(InstanceId, _strategyRunGeneration);
        _orderBook.Restore(state.OrderBook);
        _d1Details.Clear(); foreach (decimal detail in state.D1Details) _d1Details.Enqueue(detail);
        _latest.Clear(); foreach (var item in state.Latest) _latest[item.Key] = item.Value;
        _previousPrice = state.PreviousPrice; _smooth = state.Smooth; _sigma = state.Sigma;
        _entryPrice = state.EntryPrice; _stopPrice = state.StopPrice; _entryObi = state.EntryObi;
        _cooldownUntil = state.CooldownUntil; _entryAt = state.EntryAt; _entrySide = state.EntrySide;
        _entryWorking = state.EntryWorking; _exitWorking = state.ExitWorking;
        _completedTrades = state.CompletedTrades; _reason = state.Reason; State = state.State;
        _cycleSequence = state.CycleSequence; _currentCycleId = state.CurrentCycleId;
        _restoredBeforeStart = _context == null;
    }

    private void CancelActiveOrders()
    {
        if (_orderBook == null) return;
        foreach (string clientOrderId in _orderBook.ActiveClientOrderIds)
            CancelOrderRequested?.Invoke(clientOrderId);
    }
    private static decimal OrderBookImbalance(MarketEvent market)
    {
        decimal bid = market.BidQuantities.Take(5).Sum(x => (decimal)x);
        decimal ask = market.AskQuantities.Take(5).Sum(x => (decimal)x);
        return bid + ask <= 0m ? 0m : (bid - ask) / (bid + ask);
    }
    private static decimal StandardDeviation(IEnumerable<decimal> values)
    {
        decimal[] data = values.ToArray();
        if (data.Length < 2) return 0m;
        decimal mean = data.Average();
        return (decimal)Math.Sqrt((double)data.Select(x => (x - mean) * (x - mean)).Average());
    }
    private int Int(string key, int fallback, int minimum, int maximum) =>
        _definition.Parameters.TryGetValue(key, out string? text) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? Math.Clamp(value, minimum, maximum) : fallback;
    private decimal Decimal(string key, decimal fallback, decimal minimum, decimal maximum) =>
        _definition.Parameters.TryGetValue(key, out string? text) && decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value)
            ? Math.Clamp(value, minimum, maximum) : fallback;
}
