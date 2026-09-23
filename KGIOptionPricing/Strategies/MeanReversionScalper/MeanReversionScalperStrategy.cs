using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using KGIOptionPricing.OrderManagement.Core;
using KGIOptionPricing.StrategyFramework;
using KGIOptionPricing.StrategyFramework.Recovery;

namespace KGIOptionPricing.Strategies.MeanReversionScalper;

public sealed class MeanReversionScalperFactory : IStrategyFactory
{
    public const string TypeName = "MeanReversionScalper";
    public string StrategyType => TypeName;
    public string DisplayName => "超短週期均值回歸（模擬）";
    public ISimulationStrategy Create(StrategyDefinition definition) => new MeanReversionScalperStrategy(definition);
}

public sealed class MeanReversionScalperStrategy : ISimulationStrategy, IManagedOrderStrategy,
    IManagedOrderSubmissionObserver, IStrategyRecoveryParticipant
{
    private sealed record RangeSample(DateTimeOffset Time, decimal Price);
    private sealed record RecoveryState(RangeSample[] RangeSamples, KeyValuePair<string, MarketEvent>[] Latest,
        DateTimeOffset? FirstTradableTradeAt, DateTimeOffset CooldownUntil, decimal Center, decimal Range,
        decimal BracketRange, decimal BuyEntry, decimal SellEntry, decimal LongStop, decimal ShortStop,
        decimal StopPrice, string Reason, int CompletedTrades, bool BracketWorking, int CycleSequence,
        string CurrentCycleId, StrategyLifecycleState State, ManagedOrderBookCheckpoint OrderBook);
    private readonly StrategyDefinition _definition;
    private readonly Queue<RangeSample> _rangeSamples = new();
    private readonly Dictionary<string, MarketEvent> _latest = new(StringComparer.OrdinalIgnoreCase);
    private StrategyRuntimeContext? _context;
    private readonly int _horizonSeconds;
    private readonly decimal _f;
    private readonly decimal _k;
    private readonly int _minimumRangeTicks;
    private readonly int _maximumSpreadTicks;
    private readonly int _freshnessSeconds;
    private readonly int _cooldownSeconds;
    private DateTimeOffset? _firstTradableTradeAt;
    private DateTimeOffset _cooldownUntil;
    private decimal _center;
    private decimal _range;
    // Entry and risk levels must remain tied to the same observed range.  The rolling
    // range keeps changing while an entry order waits, so recomputing a stop on fill
    // can otherwise put a short stop below its entry (or a long stop above its entry).
    private decimal _bracketRange;
    private decimal _buyEntry;
    private decimal _sellEntry;
    private decimal _longStop;
    private decimal _shortStop;
    private decimal _stopPrice;
    private string _reason = "尚未啟動";
    private int _completedTrades;
    private bool _bracketWorking;
    private ManagedStrategyOrderBook? _orderBook;
    private long _strategyRunGeneration;
    private int _cycleSequence;
    private string _currentCycleId = string.Empty;
    private bool _restoredBeforeStart;

    public string StrategyType => _definition.StrategyType;
    public string InstanceId => _definition.InstanceId;
    public StrategyLifecycleState State { get; private set; } = StrategyLifecycleState.Created;
    public event Action<PaperOrderRequest>? OrderRequested;
    public event Action? CancelAllRequested;
    public event Action<StrategySnapshot>? SnapshotChanged;
    public event Action<string>? CancelOrderRequested;
    public string RecoverySchema => "mean-reversion-scalper/v1";

    public MeanReversionScalperStrategy(StrategyDefinition definition)
    {
        _definition = definition;
        _horizonSeconds = Int("HorizonSeconds", 900, 10, 86400);
        _f = Decimal("F", 0.75m, 0.01m, 4m);
        _k = Decimal("K", 0.87m, _f + 0.01m, 8m);
        _minimumRangeTicks = Int("MinimumRangeTicks", 4, 1, 100000);
        _maximumSpreadTicks = Int("MaximumSpreadTicks", 4, 1, 1000);
        _freshnessSeconds = Int("FreshnessSeconds", 3, 1, 60);
        _cooldownSeconds = Int("CooldownSeconds", 5, 0, 3600);
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
            throw new InvalidOperationException("MR recovery run generation does not match runtime.");
        if (_restoredBeforeStart) _restoredBeforeStart = false;
        else
        {
            State = StrategyLifecycleState.WarmingUp;
            _reason = $"累積 {_horizonSeconds} 秒成交資料";
        }
        Publish();
    }

    public void OnMarketEvent(MarketEvent marketEvent)
    {
        if (_context == null || State is StrategyLifecycleState.Stopped or StrategyLifecycleState.Faulted) return;
        _latest[marketEvent.Symbol] = marketEvent;
        if (!IsTradable(marketEvent.Symbol)) { Publish(); return; }

        if (marketEvent.Kind == MarketEventKind.Trade && marketEvent.LastPrice > 0m)
        {
            _firstTradableTradeAt ??= marketEvent.Timestamp;
            _rangeSamples.Enqueue(new RangeSample(marketEvent.Timestamp, marketEvent.LastPrice));
            DateTimeOffset cutoff = marketEvent.Timestamp.AddSeconds(-_horizonSeconds);
            while (_rangeSamples.Count > 0 && _rangeSamples.Peek().Time < cutoff) _rangeSamples.Dequeue();
            _range = _rangeSamples.Count == 0 ? 0m : _rangeSamples.Max(x => x.Price) - _rangeSamples.Min(x => x.Price);
        }

        PositionSnapshot position = Position();
        if (position.NetQuantity != 0 || _bracketWorking)
        {
            State = StrategyLifecycleState.Running;
            _reason = position.NetQuantity == 0 ? "雙邊模擬掛單中" : position.NetQuantity > 0 ? "模擬多單持有中" : "模擬空單持有中";
            Publish();
            return;
        }

        if (marketEvent.Kind != MarketEventKind.Trade) { Publish(); return; }
        if (marketEvent.Timestamp < _cooldownUntil)
        {
            State = StrategyLifecycleState.Ready;
            _reason = $"冷卻至 {_cooldownUntil:HH:mm:ss}";
            Publish();
            return;
        }

        if (!TryValidate(marketEvent.Timestamp, out string validationReason))
        {
            State = !WarmupComplete(marketEvent.Timestamp)
                ? StrategyLifecycleState.WarmingUp : StrategyLifecycleState.Suspended;
            _reason = validationReason;
            Publish();
            return;
        }

        PlaceEntryBracket();
        Publish();
    }

    public void OnExecution(ExecutionEvent execution)
    {
        if (_context == null || State is StrategyLifecycleState.Stopped or StrategyLifecycleState.Faulted ||
            !string.Equals(execution.StrategyInstanceId, InstanceId, StringComparison.OrdinalIgnoreCase)) return;
        if (execution.Role is PaperOrderRole.EntryLong or PaperOrderRole.EntryShort && string.IsNullOrEmpty(_currentCycleId))
            _currentCycleId = string.IsNullOrWhiteSpace(execution.CycleId) ? $"cycle-{++_cycleSequence}" : execution.CycleId;
        switch (execution.Role)
        {
            case PaperOrderRole.EntryLong:
                CancelActiveOrders();
                _bracketWorking = false;
                SubmitExit(BuySell.Sell, PaperOrderType.Limit, PaperOrderRole.TakeProfit, _sellEntry);
                SubmitExit(BuySell.Sell, PaperOrderType.Stop, PaperOrderRole.StopLoss, _longStop);
                _stopPrice = _longStop;
                State = StrategyLifecycleState.Running;
                _reason = $"多單成交 {execution.Price}，等待停利或停損";
                break;
            case PaperOrderRole.EntryShort:
                CancelActiveOrders();
                _bracketWorking = false;
                SubmitExit(BuySell.Buy, PaperOrderType.Limit, PaperOrderRole.TakeProfit, _buyEntry);
                SubmitExit(BuySell.Buy, PaperOrderType.Stop, PaperOrderRole.StopLoss, _shortStop);
                _stopPrice = _shortStop;
                State = StrategyLifecycleState.Running;
                _reason = $"空單成交 {execution.Price}，等待停利或停損";
                break;
            case PaperOrderRole.TakeProfit:
            case PaperOrderRole.StopLoss:
            case PaperOrderRole.Flatten:
                CancelActiveOrders();
                _bracketWorking = false;
                _completedTrades++;
                _cooldownUntil = execution.Timestamp.AddSeconds(_cooldownSeconds);
                _stopPrice = 0m;
                _bracketRange = 0m;
                State = StrategyLifecycleState.Ready;
                _reason = execution.Role == PaperOrderRole.TakeProfit ? "停利完成，進入冷卻" : "停損完成，進入冷卻";
                break;
        }
        Publish();
    }

    public void Stop(string reason)
    {
        CancelActiveOrders();
        _bracketWorking = false;
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
            _bracketWorking = false;
        }
        State = StrategyLifecycleState.Suspended;
        _reason = reason;
        Publish();
    }

    public void Resume()
    {
        if (State != StrategyLifecycleState.Suspended) return;
        DateTimeOffset now = _context?.Clock.Now ?? DateTimeOffset.Now;
        State = WarmupComplete(now) ? StrategyLifecycleState.Ready : StrategyLifecycleState.WarmingUp;
        _reason = State == StrategyLifecycleState.Ready ? "已繼續，等待下一筆行情" : $"繼續暖機：累積 {_horizonSeconds} 秒成交資料";
        Publish();
    }

    public StrategySnapshot GetSnapshot()
    {
        PositionSnapshot position = Position();
        string references = _context == null ? string.Empty : string.Join(", ", _context.Universe.References.Select(reference =>
        {
            if (!_latest.TryGetValue(reference.Symbol, out MarketEvent? market)) return $"{reference.ProductId}:--";
            decimal price = market.LastPrice > 0m ? market.LastPrice : market.MidPrice;
            return $"{reference.ProductId}:{price}";
        }));
        decimal last = 0m;
        if (_context != null && _latest.TryGetValue(_context.Universe.Tradable.Symbol, out MarketEvent? tradable))
            last = tradable.LastPrice > 0m ? tradable.LastPrice : tradable.MidPrice;
        return new StrategySnapshot
        {
            StrategyType = StrategyType,
            InstanceId = InstanceId,
            ParameterSummary = $"H={_horizonSeconds}s; F={_f}; K={_k}; MinR={_minimumRangeTicks}t; MaxSpread={_maximumSpreadTicks}t; Fresh={_freshnessSeconds}s; Cooldown={_cooldownSeconds}s",
            TradableProductId = _context?.Universe.Tradable.ProductId ?? _definition.Universe.Tradable.ProductId,
            TradableSymbol = _context?.Universe.Tradable.Symbol ?? string.Empty,
            ReferenceSummary = references,
            State = State,
            Reason = _reason,
            LastPrice = last,
            CenterPrice = _center,
            Range = _bracketWorking || position.NetQuantity != 0 ? _bracketRange : _range,
            BuyEntry = _buyEntry,
            SellEntry = _sellEntry,
            StopPrice = _stopPrice,
            Position = position.NetQuantity,
            RealizedPnl = position.RealizedPnl,
            UnrealizedPnl = position.UnrealizedPnl,
            NetPnl = position.NetPnl,
            CompletedTrades = _completedTrades,
            UpdatedAt = _context?.Clock.Now ?? DateTimeOffset.Now
        };
    }

    private void PlaceEntryBracket()
    {
        if (_context == null) return;
        MarketEvent book = _latest[_context.Universe.Tradable.Symbol];
        _center = book.MidPrice;
        _bracketRange = _range;
        _buyEntry = RoundDown(_center - _bracketRange * _f / 2m);
        _sellEntry = RoundUp(_center + _bracketRange * _f / 2m);
        _longStop = RoundDown(_center - _bracketRange * _k / 2m);
        _shortStop = RoundUp(_center + _bracketRange * _k / 2m);
        _currentCycleId = $"cycle-{++_cycleSequence}";
        SubmitExit(BuySell.Buy, PaperOrderType.Limit, PaperOrderRole.EntryLong, _buyEntry);
        SubmitExit(BuySell.Sell, PaperOrderType.Limit, PaperOrderRole.EntryShort, _sellEntry);
        _bracketWorking = true;
        State = StrategyLifecycleState.Running;
        _reason = $"模擬雙邊掛單：買 {_buyEntry}／賣 {_sellEntry}";
    }

    private void SubmitExit(BuySell side, PaperOrderType type, PaperOrderRole role, decimal price)
    {
        if (_context == null || price <= 0m) return;
        OrderRequested?.Invoke(new PaperOrderRequest
        {
            ClientOrderId = (_orderBook ?? throw new InvalidOperationException("Order book not initialized."))
                .NextClientOrderId(role.ToString(), _currentCycleId),
            CycleId = _currentCycleId,
            StrategyRunGeneration = _strategyRunGeneration,
            Generation = 0,
            StrategyInstanceId = InstanceId,
            Symbol = _context.Universe.Tradable.Symbol,
            Side = side,
            Type = type,
            Role = role,
            Price = price,
            Quantity = 1
        });
    }

    private bool TryValidate(DateTimeOffset now, out string reason)
    {
        if (_context == null) { reason = "策略尚未初始化"; return false; }
        if (!WarmupComplete(now))
        {
            int elapsed = _firstTradableTradeAt.HasValue ?
                Math.Max(0, (int)(now - _firstTradableTradeAt.Value).TotalSeconds) : 0;
            reason = $"暖機中：{Math.Min(_horizonSeconds, elapsed)}/{_horizonSeconds} 秒";
            return false;
        }

        decimal tick = TickSize();
        if (_range < tick * _minimumRangeTicks)
        { reason = $"R={_range} 小於 {_minimumRangeTicks} ticks"; return false; }

        foreach (ResolvedProduct product in _context.Universe.AllProducts)
        {
            if (!_latest.TryGetValue(product.Symbol, out MarketEvent? market))
            { reason = $"等待 {product.ProductId} 行情"; return false; }
            if ((now - market.Timestamp).TotalSeconds > _freshnessSeconds)
            { reason = $"{product.ProductId} 行情過期"; return false; }
            if (market.BestBid <= 0m || market.BestAsk <= 0m || market.BestBid >= market.BestAsk)
            { reason = $"{product.ProductId} 委託簿無效"; return false; }
        }

        MarketEvent primary = _latest[_context.Universe.Tradable.Symbol];
        if ((primary.BestAsk - primary.BestBid) / tick > _maximumSpreadTicks)
        { reason = "交易商品價差過大"; return false; }
        reason = string.Empty;
        return true;
    }

    private bool IsTradable(string symbol) => _context != null &&
        string.Equals(symbol, _context.Universe.Tradable.Symbol, StringComparison.OrdinalIgnoreCase);
    private PositionSnapshot Position() => _context == null ? new PositionSnapshot() :
        _context.Performance.GetPosition(InstanceId, _context.Universe.Tradable.Symbol);
    private bool WarmupComplete(DateTimeOffset now) => _firstTradableTradeAt.HasValue &&
        _rangeSamples.Count >= 2 && (now - _firstTradableTradeAt.Value).TotalSeconds >= _horizonSeconds;
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
            _bracketWorking = _orderBook.HasActiveRole(PaperOrderRole.EntryLong) || _orderBook.HasActiveRole(PaperOrderRole.EntryShort);
            if (!_bracketWorking && Position().NetQuantity == 0 && State != StrategyLifecycleState.Stopped)
            { State = StrategyLifecycleState.Ready; _reason = "進場單已撤銷／拒絕，等待新訊號"; Publish(); }
        }
        else if (request.Role is PaperOrderRole.TakeProfit or PaperOrderRole.StopLoss && Position().NetQuantity != 0)
        { State = StrategyLifecycleState.Suspended; _reason = $"保護單{(orderEvent.State == OrderLifecycleState.Rejected ? "遭拒" : "已撤銷")}，需人工處理"; Publish(); }
    }

    public StrategyRecoveryCheckpoint CaptureRecoveryCheckpoint()
    {
        if (_orderBook == null) throw new InvalidOperationException("MR strategy has not initialized its managed order book.");
        var state = new RecoveryState(_rangeSamples.ToArray(), _latest.ToArray(), _firstTradableTradeAt,
            _cooldownUntil, _center, _range, _bracketRange, _buyEntry, _sellEntry, _longStop, _shortStop,
            _stopPrice, _reason, _completedTrades, _bracketWorking, _cycleSequence, _currentCycleId, State, _orderBook.Capture());
        return new StrategyRecoveryCheckpoint(StrategyType, InstanceId, _strategyRunGeneration, RecoverySchema,
            DateTimeOffset.UtcNow, JsonSerializer.Serialize(state));
    }

    public void RestoreRecoveryCheckpoint(StrategyRecoveryCheckpoint checkpoint)
    {
        StrategyRecoveryCheckpointValidator.EnsureCompatible(checkpoint, StrategyType, InstanceId,
            checkpoint.StrategyRunGeneration, RecoverySchema);
        RecoveryState state = JsonSerializer.Deserialize<RecoveryState>(checkpoint.PayloadJson)
            ?? throw new InvalidOperationException("MR recovery payload is empty.");
        if (_strategyRunGeneration != 0 && _strategyRunGeneration != checkpoint.StrategyRunGeneration)
            throw new InvalidOperationException("MR checkpoint run generation conflicts with the runtime.");
        _strategyRunGeneration = checkpoint.StrategyRunGeneration;
        _orderBook ??= new ManagedStrategyOrderBook(InstanceId, _strategyRunGeneration);
        _orderBook.Restore(state.OrderBook);
        _rangeSamples.Clear(); foreach (RangeSample sample in state.RangeSamples) _rangeSamples.Enqueue(sample);
        _latest.Clear(); foreach (var item in state.Latest) _latest[item.Key] = item.Value;
        _firstTradableTradeAt = state.FirstTradableTradeAt; _cooldownUntil = state.CooldownUntil;
        _center = state.Center; _range = state.Range; _bracketRange = state.BracketRange;
        _buyEntry = state.BuyEntry; _sellEntry = state.SellEntry; _longStop = state.LongStop; _shortStop = state.ShortStop;
        _stopPrice = state.StopPrice; _reason = state.Reason; _completedTrades = state.CompletedTrades;
        _bracketWorking = state.BracketWorking; _cycleSequence = state.CycleSequence; _currentCycleId = state.CurrentCycleId;
        State = state.State;
        _restoredBeforeStart = _context == null;
    }

    private void CancelActiveOrders()
    {
        if (_orderBook == null) return;
        foreach (string clientOrderId in _orderBook.ActiveClientOrderIds)
            CancelOrderRequested?.Invoke(clientOrderId);
    }

    private int Int(string key, int fallback, int minimum, int maximum)
    {
        if (!_definition.Parameters.TryGetValue(key, out string? text) ||
            !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)) return fallback;
        return Math.Clamp(value, minimum, maximum);
    }

    private decimal Decimal(string key, decimal fallback, decimal minimum, decimal maximum)
    {
        if (!_definition.Parameters.TryGetValue(key, out string? text) ||
            !decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value)) return fallback;
        return Math.Clamp(value, minimum, maximum);
    }
}
