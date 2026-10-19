using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KGIOptionPricing.OrderManagement.Core;
using KGIOptionPricing.StrategyFramework;
using KGIOptionPricing.StrategyFramework.Storage;

namespace KGIOptionPricing.Strategies.MeanReversionScalper;

public sealed class MeanReversionScalperFactory : IStrategyFactory
{
    public const string TypeName = "MeanReversionScalper";
    public string StrategyType => TypeName;
    public string DisplayName => "超短週期均值回歸（模擬）";
    public ISimulationStrategy Create(StrategyDefinition definition) => new MeanReversionScalperStrategy(definition);
}

public sealed class MeanReversionScalperStrategy : ISimulationStrategy, IManagedOrderStrategy,
    IManagedOrderSubmissionObserver, IStrategyHeartbeat, IStrategyHeartbeatWhilePaused,
    IStrategyDecisionSource
{
    private sealed record RangeSample(DateTimeOffset Time, decimal Price);
    private enum ClosePhase { Open, CancellingGeneral, CancellingStop, FlattenPending, Closed, Blocked }
    private readonly StrategyDefinition _definition;
    private readonly Queue<RangeSample> _rangeSamples = new();
    private readonly Dictionary<string, Queue<RangeSample>> _referenceSamples = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MarketEvent> _latest = new(StringComparer.OrdinalIgnoreCase);
    private StrategyRuntimeContext? _context;
    private readonly int _horizonSeconds;
    private readonly decimal _f;
    private readonly decimal _k;
    private readonly int _minimumRangeTicks;
    private readonly int _maximumRangeTicks;
    private readonly int _maximumSpreadTicks;
    private readonly int _freshnessSeconds;
    private readonly int _cooldownSeconds;
    private readonly bool _useMovingAverage;
    private readonly int _trendShiftTicks;
    private readonly string _movingAverageReferenceProductId;
    private readonly MeanReversionMovingAverage _movingAverage = new();
    private decimal _referenceSma;
    private int _appliedShiftTicks;
    private readonly MeanReversionSessionSchedule _schedule;
    private string? _sessionId;
    private MeanReversionSessionWindow? _sessionWindow;
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
    private bool _submissionQuarantined;
    private int _observedDirection;
    private ManagedStrategyOrderBook? _orderBook;
    private long _strategyRunGeneration;
    private int _cycleSequence;
    private string _currentCycleId = string.Empty;
    private PaperOrderRole? _filledEntryRole;
    private readonly Dictionary<string, int> _deliveredEntryFillQuantities = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _deliveredFillQuantities = new(StringComparer.Ordinal);
    private readonly HashSet<string> _cancelRequested = new(StringComparer.Ordinal);
    private ClosePhase _closePhase = ClosePhase.Open;
    private bool _flattenSubmitted;
    private decimal _lastValidTradePrice;
    private DateTimeOffset _lastValidTradeAt;
    private DateTimeOffset _observationTime;
    private string _lastDecisionKey = string.Empty;
    private long _decisionSequence;

    public string StrategyType => _definition.StrategyType;
    public string InstanceId => _definition.InstanceId;
    public StrategyLifecycleState State { get; private set; } = StrategyLifecycleState.Created;
    public bool ShouldPulseWhilePaused => true;
    public event Action<PaperOrderRequest>? OrderRequested;
    public event Action? CancelAllRequested;
    public event Action<StrategySnapshot>? SnapshotChanged;
    public event Action<string>? CancelOrderRequested;
    public event Action<StrategyDecisionObservation>? DecisionObserved;

    public MeanReversionScalperStrategy(StrategyDefinition definition)
    {
        _definition = definition;
        _horizonSeconds = Int("HorizonSeconds", 600, 10, 86400);
        _f = Decimal("F", 0.60m, 0.01m, 4m);
        _k = Decimal("K", 1.20m, _f + 0.01m, 8m);
        _minimumRangeTicks = Int("MinimumRangeTicks", 8, 1, 100000);
        _maximumRangeTicks = Int("MaximumRangeTicks", 20, 1, 100000);
        if (_maximumRangeTicks < _minimumRangeTicks)
            throw new ArgumentException("最大 R ticks 不得小於最小 R ticks。", nameof(definition));
        _maximumSpreadTicks = Int("MaximumSpreadTicks", 4, 1, 1000);
        _freshnessSeconds = Int("FreshnessSeconds", 3, 1, 60);
        _cooldownSeconds = Int("CooldownSeconds", 5, 0, 3600);
        _useMovingAverage = definition.Parameters.TryGetValue("UseMovingAverage", out string? useMa) &&
            bool.TryParse(useMa, out bool enabled) && enabled;
        _trendShiftTicks = Int("TrendShiftTicks", 1, 0, 20);
        _movingAverageReferenceProductId = definition.Parameters.TryGetValue("MovingAverageReferenceProductId", out string? reference) &&
            !string.IsNullOrWhiteSpace(reference) ? reference.Trim() : "225M";
        _schedule = MeanReversionSessionSchedule.Parse(definition.Parameters);
    }

    public void Start(StrategyRuntimeContext context)
    {
        if (State != StrategyLifecycleState.Created) throw new InvalidOperationException("策略已啟動。");
        _context = context;
        _strategyRunGeneration = context.StrategyRunGeneration;
        _orderBook = new ManagedStrategyOrderBook(InstanceId, _strategyRunGeneration);
        State = StrategyLifecycleState.WarmingUp;
        _reason = $"累積 {_horizonSeconds} 秒成交資料";
        Publish();
    }

    public void OnMarketEvent(MarketEvent marketEvent)
    {
        if (_context == null || State is StrategyLifecycleState.Stopped or StrategyLifecycleState.Faulted) return;
        AdvanceSession(marketEvent.Timestamp);
        _observationTime = marketEvent.Timestamp;
        MeanReversionSessionWindow? window = _schedule.Resolve(marketEvent.Timestamp);
        if (!string.Equals(window?.SessionId, _sessionId, StringComparison.Ordinal))
        {
            _sessionId = window?.SessionId;
            _rangeSamples.Clear();
            _referenceSamples.Clear();
            _movingAverage.Reset();
            _firstTradableTradeAt = null;
            _range = 0m;
            _latest.Clear();
        }
        _sessionWindow = window;
        _latest[marketEvent.Symbol] = marketEvent;
        if (!IsTradable(marketEvent.Symbol))
        {
            ObserveReference(marketEvent);
            Publish();
            return;
        }

        if (window != null && marketEvent.Kind == MarketEventKind.Trade && marketEvent.LastPrice > 0m)
        {
            _lastValidTradePrice = marketEvent.LastPrice;
            _lastValidTradeAt = marketEvent.Timestamp;
            _firstTradableTradeAt ??= marketEvent.Timestamp;
            _rangeSamples.Enqueue(new RangeSample(marketEvent.Timestamp, marketEvent.LastPrice));
            DateTimeOffset cutoff = marketEvent.Timestamp.AddSeconds(-_horizonSeconds);
            while (_rangeSamples.Count > 0 && _rangeSamples.Peek().Time < cutoff) _rangeSamples.Dequeue();
            _range = _rangeSamples.Count == 0 ? 0m : _rangeSamples.Max(x => x.Price) - _rangeSamples.Min(x => x.Price);
        }

        if (_closePhase != ClosePhase.Open)
        {
            TryAdvanceClose();
            Publish();
            return;
        }

        if (_submissionQuarantined)
        {
            State = StrategyLifecycleState.Suspended;
            Publish();
            return;
        }

        PositionSnapshot position = Position();
        if (position.NetQuantity != 0 || _bracketWorking)
        {
            State = StrategyLifecycleState.Running;
            _reason = position.NetQuantity == 0 ? "雙側限價單工作中" :
                position.NetQuantity > 0 ? "多單持有；原賣單作限價出場" : "空單持有；原買單作限價出場";
            Publish();
            return;
        }

        if (marketEvent.Kind != MarketEventKind.Trade) { Publish(); return; }
        if (window == null || marketEvent.Timestamp < window.TradingStart || marketEvent.Timestamp >= window.Stop)
        {
            State = StrategyLifecycleState.Suspended;
            _reason = window == null ? "非交易時段，禁止進場" :
                marketEvent.Timestamp < window.TradingStart ? $"PreStart：{window.TradingStart:HH:mm} 前暖機，禁止進場" :
                "SessionStop：禁止新進場";
            Publish();
            return;
        }
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
        if (_orderBook == null || !_orderBook.TryGet(execution.ClientOrderId, out PaperOrderRequest? owned) ||
            owned == null || owned.StrategyRunGeneration != execution.StrategyRunGeneration ||
            !string.Equals(owned.CycleId, execution.CycleId, StringComparison.Ordinal) ||
            (!string.IsNullOrWhiteSpace(execution.PairId) &&
             !string.Equals(owned.PairId, execution.PairId, StringComparison.Ordinal))) return;
        _observationTime = execution.Timestamp;
        if (execution.Role is PaperOrderRole.EntryLong or PaperOrderRole.EntryShort && string.IsNullOrEmpty(_currentCycleId))
            _currentCycleId = string.IsNullOrWhiteSpace(execution.CycleId) ? $"cycle-{++_cycleSequence}" : execution.CycleId;
        if (execution.Role is PaperOrderRole.EntryLong or PaperOrderRole.EntryShort &&
            !string.IsNullOrWhiteSpace(execution.ClientOrderId))
            _deliveredEntryFillQuantities[execution.ClientOrderId] =
                _deliveredEntryFillQuantities.GetValueOrDefault(execution.ClientOrderId) + execution.Quantity;
        if (!string.IsNullOrWhiteSpace(execution.ClientOrderId))
            _deliveredFillQuantities[execution.ClientOrderId] =
                _deliveredFillQuantities.GetValueOrDefault(execution.ClientOrderId) + execution.Quantity;
        if (_closePhase != ClosePhase.Open)
        {
            TryAdvanceClose();
            Publish();
            return;
        }
        switch (execution.Role)
        {
            case PaperOrderRole.EntryLong:
                if (_filledEntryRole is PaperOrderRole.EntryShort)
                {
                    SuspendForEntryRace("雙側進場均成交，等待策略部位與委託人工核對");
                    break;
                }
                _filledEntryRole = PaperOrderRole.EntryLong;
                SubmitExit(BuySell.Sell, PaperOrderType.Stop, PaperOrderRole.StopLoss, _longStop);
                _stopPrice = _longStop;
                State = StrategyLifecycleState.Running;
                _reason = $"多單成交 {execution.Price}，原賣單維持掛單作停利，固定停損監控中";
                break;
            case PaperOrderRole.EntryShort:
                if (_filledEntryRole is PaperOrderRole.EntryLong)
                {
                    SuspendForEntryRace("雙側進場均成交，等待策略部位與委託人工核對");
                    break;
                }
                _filledEntryRole = PaperOrderRole.EntryShort;
                SubmitExit(BuySell.Buy, PaperOrderType.Stop, PaperOrderRole.StopLoss, _shortStop);
                _stopPrice = _shortStop;
                State = StrategyLifecycleState.Running;
                _reason = $"空單成交 {execution.Price}，原買單維持掛單作停利，固定停損監控中";
                break;
            case PaperOrderRole.TakeProfit:
            case PaperOrderRole.StopLoss:
            case PaperOrderRole.Flatten:
                _completedTrades++;
                _cooldownUntil = execution.Timestamp.AddSeconds(_cooldownSeconds);
                _stopPrice = 0m;
                _bracketRange = 0m;
                _filledEntryRole = null;
                State = StrategyLifecycleState.Ready;
                _reason = execution.Role == PaperOrderRole.TakeProfit ? "停利成交，撤銷本週期停損監控" : "停損成交，等待本週期委託結清";
                if (execution.Role == PaperOrderRole.TakeProfit)
                    CancelCycleStops();
                else if (execution.Role == PaperOrderRole.StopLoss)
                    CancelCycleEntryOrders();
                break;
        }
        TryCompletePairedCycle();
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
        if (_submissionQuarantined) return;
        DateTimeOffset now = _context?.Clock.Now ?? DateTimeOffset.Now;
        State = WarmupComplete(now) ? StrategyLifecycleState.Ready : StrategyLifecycleState.WarmingUp;
        _reason = State == StrategyLifecycleState.Ready ? "已繼續，等待下一筆行情" : $"繼續暖機：累積 {_horizonSeconds} 秒成交資料";
        Publish();
    }

    public void OnHeartbeat(DateTimeOffset now)
    {
        if (_context == null || State is StrategyLifecycleState.Stopped or StrategyLifecycleState.Faulted) return;
        _observationTime = now;
        AdvanceSession(now);
        if (_closePhase != ClosePhase.Open) TryAdvanceClose();
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
        _latest.TryGetValue(_context?.Universe.Tradable.Symbol ?? string.Empty, out MarketEvent? lastBook);
        return new StrategySnapshot
        {
            StrategyType = StrategyType,
            InstanceId = InstanceId,
            ParameterSummary = $"H={_horizonSeconds}s; F={_f}; K={_k}; MinR={_minimumRangeTicks}t; MaxR={_maximumRangeTicks}t; MA={_useMovingAverage}; MARef={_movingAverageReferenceProductId}; N={_trendShiftTicks}/{_appliedShiftTicks}t; SMA={_referenceSma}; Buy={_buyEntry}; Sell={_sellEntry}; LongStop={_longStop}; ShortStop={_shortStop}; MaxSpread={_maximumSpreadTicks}t; Fresh={_freshnessSeconds}s; Cooldown={_cooldownSeconds}s; DayStart={_schedule.DayStartTime:HH:mm}; DayStop={_schedule.DayStopTime:HH:mm}; NightStart={_schedule.NightStartTime:HH:mm}; NightStop={_schedule.NightStopTime:HH:mm}",
            TradableProductId = _context?.Universe.Tradable.ProductId ?? _definition.Universe.Tradable.ProductId,
            TradableSymbol = _context?.Universe.Tradable.Symbol ?? string.Empty,
            ReferenceSummary = references,
            State = State,
            Reason = _reason,
            Features = new Dictionary<string, decimal?>
            {
                ["RollingRangePoints"] = _range, ["FrozenRangePoints"] = _bracketRange,
                ["ReferenceSma"] = _referenceSma, ["TrendSign"] = _observedDirection,
                ["RequestedShiftTicks"] = _useMovingAverage ? _trendShiftTicks : 0,
                ["AppliedShiftTicks"] = _appliedShiftTicks,
                ["Bid1"] = lastBook?.BestBid, ["Ask1"] = lastBook?.BestAsk,
                ["FrozenBuy"] = _buyEntry, ["FrozenSell"] = _sellEntry,
                ["FrozenLongStop"] = _longStop, ["FrozenShortStop"] = _shortStop
            },
            Diagnostics = new Dictionary<string, string>
            {
                ["PairId"] = _currentCycleId,
                ["MovingAverageReferenceProductId"] = _movingAverageReferenceProductId,
                ["UseMovingAverage"] = _useMovingAverage.ToString(),
                ["LocalCycleState"] = _closePhase != ClosePhase.Open ? _closePhase.ToString() :
                    _filledEntryRole == null ? (_bracketWorking ? "Working" : "Idle") : "Held"
            },
            LastPrice = last,
            CenterPrice = _center,
            Range = _bracketWorking || position.NetQuantity != 0 ? _bracketRange : _range,
            RollingRange = _range,
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
        if (!MeanReversionPairPricing.TryCompute(_center, _bracketRange, _f, _k,
            TickSize(), book.BestBid, book.BestAsk, _observedDirection,
            _useMovingAverage ? _trendShiftTicks : 0, out MeanReversionPairPrices prices))
            throw new InvalidOperationException("No passive paired prices at the validated book.");
        _buyEntry = prices.Buy;
        _sellEntry = prices.Sell;
        _longStop = prices.LongStop;
        _shortStop = prices.ShortStop;
        _appliedShiftTicks = prices.AppliedShiftTicks;
        _currentCycleId = $"cycle-{++_cycleSequence}";
        _filledEntryRole = null;
        _deliveredEntryFillQuantities.Clear();
        _bracketWorking = true;
        State = StrategyLifecycleState.Running;
        _reason = $"雙側限價掛單：買 {_buyEntry}、賣 {_sellEntry}，MA 位移 {_appliedShiftTicks} 跳";
        try
        {
            SubmitExit(BuySell.Buy, PaperOrderType.BrokerLimit, PaperOrderRole.EntryLong, _buyEntry);
            if (_submissionQuarantined) return;
            SubmitExit(BuySell.Sell, PaperOrderType.BrokerLimit, PaperOrderRole.EntryShort, _sellEntry);
        }
        catch (Exception error)
        {
            _bracketWorking = true;
            _submissionQuarantined = true;
            State = StrategyLifecycleState.Suspended;
            _reason = $"雙側委託提交不完整：{error.Message}；等待已送委託撤銷及人工核對";
            CancelCycleEntryOrders();
        }
    }

    private void SubmitExit(BuySell side, PaperOrderType type, PaperOrderRole role, decimal price)
    {
        if (_context == null || price <= 0m) return;
        OrderRequested?.Invoke(new PaperOrderRequest
        {
            ClientOrderId = (_orderBook ?? throw new InvalidOperationException("Order book not initialized."))
                .NextClientOrderId(role.ToString(), _currentCycleId),
            CycleId = _currentCycleId,
            OcoGroupId = _currentCycleId,
            PairId = _currentCycleId,
            RestingPairMode = role is PaperOrderRole.EntryLong or PaperOrderRole.EntryShort
                ? RestingPairMode.OppositeLegIsExit : RestingPairMode.None,
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
        if (_range > tick * _maximumRangeTicks)
        { reason = $"R={_range} 大於 {_maximumRangeTicks} ticks"; return false; }

        IEnumerable<ResolvedProduct> required = _useMovingAverage
            ? new[] { _context.Universe.Tradable }.Concat(_context.Universe.References.Where(x =>
                string.Equals(x.ProductId, _movingAverageReferenceProductId, StringComparison.OrdinalIgnoreCase)))
            : new[] { _context.Universe.Tradable };
        if (_useMovingAverage && !required.Skip(1).Any())
        { reason = $"找不到均線參考商品 {_movingAverageReferenceProductId}"; return false; }
        foreach (ResolvedProduct product in required)
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
        _observedDirection = 0;
        _referenceSma = 0m;
        if (_useMovingAverage)
        {
            ResolvedProduct reference = required.Skip(1).First();
            MarketEvent referenceQuote = _latest[reference.Symbol];
            if (!_movingAverage.TryGet(now, _horizonSeconds, _freshnessSeconds,
                referenceQuote.MidPrice, out _referenceSma, out _observedDirection))
            { reason = $"均線參考 {_movingAverageReferenceProductId} 尚無完整 H 秒報價"; return false; }
        }
        if (!MeanReversionPairPricing.TryCompute(primary.MidPrice, _range, _f, _k,
            tick, primary.BestBid, primary.BestAsk, _observedDirection,
            _useMovingAverage ? _trendShiftTicks : 0, out _))
        { reason = "目前買賣盤無可行的被動雙側價格"; return false; }
        reason = string.Empty;
        return true;
    }

    private void ObserveReference(MarketEvent market)
    {
        if (_context == null || _sessionWindow == null ||
            !_context.Universe.References.Any(x => string.Equals(x.Symbol, market.Symbol, StringComparison.OrdinalIgnoreCase)) ||
            market.BestBid <= 0m || market.BestAsk <= market.BestBid) return;
        if (string.Equals(market.ProductId, _movingAverageReferenceProductId, StringComparison.OrdinalIgnoreCase))
            _movingAverage.Observe(market.Timestamp, market.BestBid, market.BestAsk);
        if (!_referenceSamples.TryGetValue(market.Symbol, out Queue<RangeSample>? samples))
            _referenceSamples[market.Symbol] = samples = new Queue<RangeSample>();
        // Direction does not need tick-by-tick depth history. One quote per second
        // bounds memory while the latest valid quote remains in _latest.
        if (samples.Count == 0 || (market.Timestamp - samples.Last().Time).TotalSeconds >= 1)
            samples.Enqueue(new RangeSample(market.Timestamp, market.MidPrice));
        DateTimeOffset cutoff = market.Timestamp.AddSeconds(-_horizonSeconds);
        // Keep the most recent observation at/before the start of H, so a full
        // horizon is measured rather than the first conveniently available quote.
        while (samples.Count > 1 && samples.Skip(1).First().Time <= cutoff) samples.Dequeue();
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
    private void Publish()
    {
        SnapshotChanged?.Invoke(GetSnapshot());
        if (_context == null) return;
        DateTimeOffset at = _observationTime == default ? _context.Clock.Now : _observationTime;
        string reasonCode = DecisionReasonCode();
        string key = $"{_sessionId}|{State}|{reasonCode}|{_currentCycleId}|{_observedDirection}";
        if (key == _lastDecisionKey) return;
        _lastDecisionKey = key;
        decimal elapsed = _firstTradableTradeAt.HasValue
            ? Math.Max(0m, (decimal)(at - _firstTradableTradeAt.Value).TotalSeconds) : 0m;
        decimal minRange = TickSize() * _minimumRangeTicks;
        decimal maxRange = TickSize() * _maximumRangeTicks;
        bool inEntryWindow = _sessionWindow != null && at >= _sessionWindow.TradingStart && at < _sessionWindow.Stop;
        var features = new Dictionary<string, decimal?>
        {
            ["HorizonSeconds"] = _horizonSeconds, ["RangePoints"] = _range,
            ["MinimumRangePoints"] = minRange, ["MaximumRangePoints"] = maxRange,
            ["ReferenceDirection"] = _observedDirection, ["ReferenceSma"] = _referenceSma,
            ["RequestedShiftTicks"] = _useMovingAverage ? _trendShiftTicks : 0,
            ["AppliedShiftTicks"] = _appliedShiftTicks,
            ["Position"] = Position().NetQuantity,
            ["InEntryWindow"] = inEntryWindow ? 1m : 0m
        };
        foreach (ResolvedProduct reference in _context.Universe.References)
            features[$"{reference.ProductId}ChangePoints"] =
                _referenceSamples.TryGetValue(reference.Symbol, out Queue<RangeSample>? samples) &&
                samples.Count > 0 && _latest.TryGetValue(reference.Symbol, out MarketEvent? latest) &&
                latest.BestBid > 0m && latest.BestAsk > latest.BestBid ?
                    latest.MidPrice - samples.Peek().Price : null;
        DecisionObserved?.Invoke(new StrategyDecisionObservation
        {
            Sequence = ++_decisionSequence,
            Timestamp = at,
            StrategyType = StrategyType,
            StrategyInstanceId = InstanceId,
            CandidateId = _currentCycleId,
            IsCandidate = reasonCode == "EntryBracket",
            CycleId = _currentCycleId,
            Symbol = _context.Universe.Tradable.Symbol,
            Direction = _observedDirection > 0 ? "long" : _observedDirection < 0 ? "short" : string.Empty,
            State = State,
            Decision = reasonCode == "EntryBracket" ? StrategyDecision.Accept :
                reasonCode == "Warmup" ? StrategyDecision.Warmup :
                reasonCode is "SessionStop" or "CancelPending" or "FlattenPending" or "CloseBlocked"
                    ? StrategyDecision.Halt : State == StrategyLifecycleState.Suspended
                        ? StrategyDecision.Reject : StrategyDecision.Hold,
            Features = features,
            Thresholds = new[]
            {
                new ThresholdObservation("HorizonSeconds", elapsed, _horizonSeconds, "s", WarmupComplete(at)),
                new ThresholdObservation("MinimumRangePoints", _range, minRange, "points", _range >= minRange),
                new ThresholdObservation("MaximumRangePoints", _range, maxRange, "points", _range <= maxRange),
                new ThresholdObservation("MovingAverageReady", !_useMovingAverage || _referenceSma > 0m ? 1m : 0m,
                    1m, "bool", !_useMovingAverage || _referenceSma > 0m),
                new ThresholdObservation("EntryWindow", inEntryWindow ? 1m : 0m, 1m, "bool", inEntryWindow)
            },
            PrimaryReasonCode = reasonCode
        });
    }

    private string DecisionReasonCode()
    {
        if (_closePhase == ClosePhase.Blocked) return "CloseBlocked";
        if (_closePhase == ClosePhase.FlattenPending) return "FlattenPending";
        if (_closePhase is ClosePhase.CancellingGeneral or ClosePhase.CancellingStop) return "CancelPending";
        if (_closePhase == ClosePhase.Closed) return "SessionStop";
        if (_reason.StartsWith("PreStart", StringComparison.Ordinal)) return "PreStart";
        if (State == StrategyLifecycleState.WarmingUp) return "Warmup";
        if (_reason.StartsWith("雙側限價掛單", StringComparison.Ordinal)) return "EntryBracket";
        if (_reason.StartsWith("冷卻", StringComparison.Ordinal)) return "Cooldown";
        if (_reason.StartsWith("R=", StringComparison.Ordinal))
            return _reason.Contains("大於", StringComparison.Ordinal) ? "MaximumRange" : "MinimumRange";
        if (_reason.StartsWith("方向", StringComparison.Ordinal)) return "ReferenceDirection";
        if (_reason.Contains("行情過期", StringComparison.Ordinal)) return "StaleMarketData";
        if (_reason.Contains("委託簿無效", StringComparison.Ordinal)) return "InvalidBook";
        if (_reason.Contains("價差過大", StringComparison.Ordinal)) return "SpreadTooWide";
        return State.ToString();
    }

    public void OnOrderSubmitted(PaperOrderRequest request) =>
        (_orderBook ?? throw new InvalidOperationException("Order book not initialized.")).Track(request);

    public void OnOrderLifecycle(OrderLifecycleEvent orderEvent)
    {
        if (_orderBook == null || !_orderBook.TryGet(orderEvent.ClientOrderId, out PaperOrderRequest? request) ||
            request == null) return;
        _observationTime = orderEvent.Timestamp;
        if (_closePhase != ClosePhase.Open &&
            orderEvent.ReasonCode == "CancelRequestNotRouted" &&
            string.Equals(orderEvent.StrategyInstanceId, InstanceId, StringComparison.Ordinal) &&
            orderEvent.StrategyRunGeneration == _strategyRunGeneration)
        {
            BlockClose($"委託 {orderEvent.ClientOrderId} 撤單無法路由");
            Publish();
            return;
        }
        if (!_orderBook.Apply(orderEvent)) return;
        if (_closePhase != ClosePhase.Open)
        {
            TryAdvanceClose();
            Publish();
            return;
        }
        if (request.Role is PaperOrderRole.EntryLong or PaperOrderRole.EntryShort)
        {
            if (orderEvent.State is not (OrderLifecycleState.Filled or OrderLifecycleState.Cancelled or OrderLifecycleState.Rejected)) return;
            if (orderEvent.State == OrderLifecycleState.Rejected)
            {
                _submissionQuarantined = true;
                State = StrategyLifecycleState.Suspended;
                _reason = "雙側委託有一邊被拒，撤銷另一邊並等待人工核對";
                CancelCycleEntryOrders();
                Publish();
                return;
            }
            if (!orderEvent.HasFinalCumulativeFillWatermark ||
                orderEvent.FilledQuantity != _deliveredEntryFillQuantities.GetValueOrDefault(orderEvent.ClientOrderId))
            {
                State = StrategyLifecycleState.Suspended;
                _reason = "進場委託終態成交水位未知或未與帳本對平，需人工核對";
                Publish();
                return;
            }
            if (string.Equals(request.CycleId, _currentCycleId, StringComparison.Ordinal))
            {
                TryCompletePairedCycle();
                Publish();
            }
            return;
        }
        TryCompletePairedCycle();
        if (orderEvent.State is not (OrderLifecycleState.Cancelled or OrderLifecycleState.Rejected)) return;
        if (request.Role is PaperOrderRole.TakeProfit or PaperOrderRole.StopLoss && Position().NetQuantity != 0)
        { State = StrategyLifecycleState.Suspended; _reason = $"保護單{(orderEvent.State == OrderLifecycleState.Rejected ? "遭拒" : "已撤銷")}，需人工處理"; Publish(); }
    }

    private void AdvanceSession(DateTimeOffset now)
    {
        MeanReversionSessionWindow? window = _schedule.Resolve(now);
        MeanReversionSessionWindow? ending = _sessionWindow ?? window;
        if (_closePhase == ClosePhase.Open && ending != null && now >= ending.Stop)
        {
            _sessionId ??= ending.SessionId;
            _sessionWindow ??= ending;
            _closePhase = ClosePhase.CancellingGeneral;
            _reason = "SessionStop：撤銷既有委託";
            State = StrategyLifecycleState.Suspended;
            TryAdvanceClose();
        }
        if (_closePhase == ClosePhase.Closed && _sessionId != null && window != null &&
            !string.Equals(window.SessionId, _sessionId, StringComparison.Ordinal) &&
            Position().NetQuantity == 0 &&
            _orderBook?.Snapshot().Orders.All(IsSettled) != false)
        {
            _closePhase = ClosePhase.Open;
            _flattenSubmitted = false;
            _cancelRequested.Clear();
            _bracketWorking = false;
            _sessionId = window.SessionId;
            _sessionWindow = window;
            _rangeSamples.Clear();
            _referenceSamples.Clear();
            _firstTradableTradeAt = null;
            _range = 0m;
            _latest.Clear();
            _lastValidTradePrice = 0m;
            _lastValidTradeAt = default;
            _reason = "新時段重新暖機";
            State = StrategyLifecycleState.WarmingUp;
        }
    }

    private void TryAdvanceClose()
    {
        if (_closePhase is ClosePhase.Open or ClosePhase.Blocked || _orderBook == null || _context == null) return;
        ManagedOrderRecord[] records = _orderBook.Snapshot().Orders.ToArray();
        ManagedOrderRecord[] general = records.Where(x => x.Request.Role is not
            (PaperOrderRole.StopLoss or PaperOrderRole.Flatten)).ToArray();
        if (general.Any(HasProtocolGap)) { BlockClose("一般委託成交水位或撤單路由未確認"); return; }
        if (!general.All(IsSettled))
        {
            _closePhase = ClosePhase.CancellingGeneral;
            _reason = "CancelPending：等待一般委託撤單及成交水位";
            CancelActive(general);
            return;
        }

        ManagedOrderRecord[] stops = records.Where(x => x.Request.Role == PaperOrderRole.StopLoss).ToArray();
        if (stops.Any(HasProtocolGap)) { BlockClose("停損委託成交水位或撤單路由未確認"); return; }
        if (!stops.All(IsSettled))
        {
            _closePhase = ClosePhase.CancellingStop;
            _reason = "CancelPending：等待保護停損撤單及成交水位";
            CancelActive(stops);
            return;
        }

        ManagedOrderRecord[] flatten = records.Where(x => x.Request.Role == PaperOrderRole.Flatten).ToArray();
        if (flatten.Length > 0)
        {
            if (flatten.Any(HasProtocolGap)) { BlockClose("平倉委託成交水位未確認"); return; }
            if (flatten.All(IsSettled))
            {
                if (Position().NetQuantity != 0) { BlockClose("平倉回報已終結，但策略帳本仍有部位"); return; }
                CloseCompleted();
            }
            else
            {
                _closePhase = ClosePhase.FlattenPending;
                _reason = "FlattenPending：等待風險平倉成交";
            }
            return;
        }
        PositionSnapshot position = Position();
        if (position.NetQuantity == 0) { CloseCompleted(); return; }
        // Market flatten uses this price only as a required request reference;
        // its executable price is determined by the broker. A stale last trade
        // must not suppress a risk-reducing close after all orders settle.
        if (_flattenSubmitted || _lastValidTradePrice <= 0m || _lastValidTradeAt == default)
        { BlockClose("缺少有效成交價或平倉已送出，需人工核對"); return; }
        _flattenSubmitted = true;
        _closePhase = ClosePhase.FlattenPending;
        _reason = "FlattenPending：送出一次風險市價平倉";
        try
        {
            SubmitExit(position.NetQuantity > 0 ? BuySell.Sell : BuySell.Buy,
                PaperOrderType.Market, PaperOrderRole.Flatten, _lastValidTradePrice);
        }
        catch (Exception error)
        {
            BlockClose($"風險平倉送單失敗：{error.Message}");
        }
    }

    private void CancelActive(IEnumerable<ManagedOrderRecord> records)
    {
        foreach (ManagedOrderRecord record in records.Where(x => !IsSettled(x)))
            if (_cancelRequested.Add(record.Request.ClientOrderId))
            {
                try { CancelOrderRequested?.Invoke(record.Request.ClientOrderId); }
                catch (Exception error) { BlockClose($"委託 {record.Request.ClientOrderId} 撤單失敗：{error.Message}"); return; }
            }
    }

    private bool IsSettled(ManagedOrderRecord record) =>
        record.LastLifecycle is { State: OrderLifecycleState.Filled or
            OrderLifecycleState.Cancelled or OrderLifecycleState.Rejected,
            HasFinalCumulativeFillWatermark: true } terminal &&
        terminal.FilledQuantity == _deliveredFillQuantities.GetValueOrDefault(record.Request.ClientOrderId);

    private bool HasProtocolGap(ManagedOrderRecord record) =>
        record.LastLifecycle is { ReasonCode: "CancelRequestNotRouted" } ||
        record.LastLifecycle is { State: OrderLifecycleState.Filled or
            OrderLifecycleState.Cancelled or OrderLifecycleState.Rejected } terminal &&
        (!terminal.HasFinalCumulativeFillWatermark ||
         terminal.FilledQuantity != _deliveredFillQuantities.GetValueOrDefault(record.Request.ClientOrderId));

    private void BlockClose(string detail)
    {
        _closePhase = ClosePhase.Blocked;
        State = StrategyLifecycleState.Suspended;
        _reason = $"CloseBlocked：{detail}";
    }

    private void CloseCompleted()
    {
        _closePhase = ClosePhase.Closed;
        _bracketWorking = false;
        State = StrategyLifecycleState.Suspended;
        _reason = "SessionStop：委託已結清，策略部位為零；本時段禁止新進場";
    }


    private void CancelActiveOrders()
    {
        if (_orderBook == null) return;
        foreach (string clientOrderId in _orderBook.ActiveClientOrderIds)
            CancelOrderRequested?.Invoke(clientOrderId);
    }

    private void CancelCycleStops()
    {
        if (_orderBook == null) return;
        foreach (ManagedOrderRecord record in _orderBook.Snapshot().Orders.Where(x =>
            x.Request.CycleId == _currentCycleId && x.Request.Role == PaperOrderRole.StopLoss && !IsSettled(x)))
            if (_cancelRequested.Add(record.Request.ClientOrderId))
                CancelOrderRequested?.Invoke(record.Request.ClientOrderId);
    }

    private void CancelCycleEntryOrders()
    {
        if (_orderBook == null) return;
        foreach (ManagedOrderRecord record in _orderBook.Snapshot().Orders.Where(x =>
            x.Request.CycleId == _currentCycleId &&
            x.Request.Role is PaperOrderRole.EntryLong or PaperOrderRole.EntryShort &&
            x.LastLifecycle?.State is not OrderLifecycleState.Rejected && !IsSettled(x)))
            if (_cancelRequested.Add(record.Request.ClientOrderId))
                CancelOrderRequested?.Invoke(record.Request.ClientOrderId);
    }

    private void TryCompletePairedCycle()
    {
        if (!_bracketWorking || _orderBook == null || Position().NetQuantity != 0) return;
        ManagedOrderRecord[] cycle = _orderBook.Snapshot().Orders.Where(x =>
            x.Request.CycleId == _currentCycleId).ToArray();
        if (cycle.Length < 2 || !cycle.All(IsSettled)) return;
        _bracketWorking = false;
        State = _submissionQuarantined ? StrategyLifecycleState.Suspended : StrategyLifecycleState.Ready;
        _reason = _submissionQuarantined ? "雙側提交失敗：委託已結清，需人工核對後重啟" :
            "雙側委託及保護單已結清，等待冷卻";
    }

    private void SuspendForEntryRace(string reason)
    {
        _bracketWorking = false;
        State = StrategyLifecycleState.Suspended;
        _reason = reason;
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
