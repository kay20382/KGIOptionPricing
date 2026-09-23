using System.Globalization;
using System.Text.Json;
using KGIOptionPricing.OrderManagement.Core;
using KGIOptionPricing.StrategyFramework;
using KGIOptionPricing.StrategyFramework.Recovery;

namespace KGIOptionPricing.Strategies.OrderBookTrend;

public sealed class OrderBookTrendFactory : IStrategyFactory
{
    public const string TypeName = "OrderBookTrend";
    public string StrategyType => TypeName;
    public string DisplayName => "OSE 微型日經五檔順勢";
    public ISimulationStrategy Create(StrategyDefinition definition) => new OrderBookTrendStrategy(definition);
}

/// <summary>
/// Event-time trend strategy for OSE Nikkei 225 Micro futures.  It combines mid-price
/// momentum, five-level OBI/OFI, classified trade imbalance, microprice, volatility,
/// liquidity, and a prior-calibrated expected-MFE gate.  The strategy emits intents
/// only; execution, positions, PnL, multipliers, and costs remain framework-owned.
/// </summary>
public sealed class OrderBookTrendStrategy : ISimulationStrategy, IManagedOrderStrategy,
    IManagedOrderSubmissionObserver, IStrategyRecoveryParticipant
{
    private sealed record RecoveryState(decimal[] MidHistory, decimal[] MidChanges, decimal[] OfiEvents,
        int[] TradeDirections, bool[] Confirmations, MarketEvent? PreviousBook, DateTimeOffset LastValidAt,
        DateTimeOffset EntryAt, DateTimeOffset CooldownUntil, decimal PreviousTradePrice, decimal EntryPrice,
        decimal FixedStop, decimal ActiveStop, decimal BestMid, decimal EntryNetPnl, decimal MomentumTicks,
        decimal Obi, decimal OfiRaw, decimal OfiZ, decimal Ti, decimal Microprice, decimal MicropriceEdgeTicks,
        decimal RvTicks, decimal WeightedDepth, decimal BuyEntry, decimal SellEntry, int LastTradeDirection,
        int CandidateDirection, int EntryWorkingEvents, int HoldingEvents, long LastSequence, int CompletedTrades,
        int TradesToday, int ConsecutiveLosses, DateOnly TradingDate, bool EntryWorking, bool ExitWorking,
        bool Paused, string Reason, StrategyLifecycleState State, int CycleSequence, string CurrentCycleId,
        ManagedOrderBookCheckpoint OrderBook);
    private static readonly decimal[] DepthWeights = { 1m, .70m, .50m, .35m, .25m };
    private readonly StrategyDefinition _definition;
    private readonly Queue<decimal> _midHistory = new();
    private readonly Queue<decimal> _midChanges = new();
    private readonly Queue<decimal> _ofiEvents = new();
    private readonly Queue<int> _tradeDirections = new();
    private readonly Queue<bool> _confirmations = new();

    private readonly int _momentumEvents, _ofiWindow, _tiTrades, _volatilityWindow;
    private readonly int _confirmEvents, _confirmPasses, _entryTimeoutEvents, _maxContracts;
    private readonly int _maxHoldEvents, _preCloseMinutes, _freshnessSeconds, _maxDailyTrades;
    private readonly int _maxConsecutiveLosses, _lossCooldownMinutes;
    private readonly decimal _momentumEntryTicks, _obiEntry, _ofiZEntry, _ofiMean, _ofiStd;
    private readonly decimal _tiEntry, _micropriceEntryTicks, _maximumSpreadTicks, _minimumWeightedDepth;
    private readonly decimal _volatilityLowTicks, _volatilityHighTicks, _expectedMfeTicks, _minimumExpectedMoveTicks;
    private readonly decimal _maxParticipationRate, _stopLossTicks, _breakevenTriggerTicks, _costBufferTicks;
    private readonly decimal _trailingDrawdownTicks, _obiExit, _tiExit, _ofiZExit, _minProgressTicks;

    private StrategyRuntimeContext? _context;
    private MarketEvent? _previousBook;
    private DateTimeOffset _lastValidAt, _entryAt, _cooldownUntil;
    private decimal _previousTradePrice, _entryPrice, _fixedStop, _activeStop, _bestMid, _entryNetPnl;
    private decimal _momentumTicks, _obi, _ofiRaw, _ofiZ, _ti, _microprice, _micropriceEdgeTicks, _rvTicks, _weightedDepth;
    private decimal _buyEntry, _sellEntry;
    private int _lastTradeDirection, _candidateDirection, _entryWorkingEvents, _holdingEvents;
    private long _lastSequence;
    private int _completedTrades, _tradesToday, _consecutiveLosses;
    private DateOnly _tradingDate;
    private bool _entryWorking, _exitWorking, _paused;
    private string _reason = "尚未啟動";
    private ManagedStrategyOrderBook? _orderBook;
    private long _strategyRunGeneration;
    private int _cycleSequence;
    private string _currentCycleId = string.Empty;
    private bool _restoredBeforeStart;

    public string StrategyType => OrderBookTrendFactory.TypeName;
    public string InstanceId => _definition.InstanceId;
    public StrategyLifecycleState State { get; private set; } = StrategyLifecycleState.Created;
    public event Action<PaperOrderRequest>? OrderRequested;
    public event Action? CancelAllRequested;
    public event Action<StrategySnapshot>? SnapshotChanged;
    public event Action<string>? CancelOrderRequested;
    public string RecoverySchema => "order-book-trend/v1";

    public OrderBookTrendStrategy(StrategyDefinition definition)
    {
        _definition = definition;
        _momentumEvents = Int("MomentumEvents", 120, 2, 10000);
        _momentumEntryTicks = Decimal("MomentumEntryTicks", 3m, .1m, 100m);
        _obiEntry = Decimal("ObiEntry", .22m, .01m, .99m);
        _ofiWindow = Int("OfiWindow", 100, 2, 10000);
        _ofiZEntry = Decimal("OfiZEntry", 1.25m, .01m, 20m);
        _ofiMean = Decimal("OfiMean", 0m, -1000000m, 1000000m);
        _ofiStd = Decimal("OfiStd", 100m, .0001m, 1000000m);
        _tiTrades = Int("TiTrades", 50, 2, 10000);
        _tiEntry = Decimal("TiEntry", .18m, .01m, .99m);
        _micropriceEntryTicks = Decimal("MicropriceEntryTicks", .20m, .01m, 1m);
        _confirmEvents = Int("ConfirmEvents", 5, 1, 100);
        _confirmPasses = Int("ConfirmPasses", 4, 1, _confirmEvents);
        _maximumSpreadTicks = Decimal("MaximumSpreadTicks", 1m, 1m, 100m);
        _minimumWeightedDepth = Decimal("MinimumWeightedDepth", 20m, 0m, 1000000m);
        _volatilityWindow = Int("VolatilityWindow", 100, 2, 10000);
        _volatilityLowTicks = Decimal("VolatilityLowTicks", .05m, 0m, 100m);
        _volatilityHighTicks = Decimal("VolatilityHighTicks", 5m, .01m, 1000m);
        _expectedMfeTicks = Decimal("ExpectedMfeTicks", 4.5m, 0m, 100m);
        _minimumExpectedMoveTicks = Decimal("MinimumExpectedMoveTicks", 4.5m, 0m, 100m);
        _maxParticipationRate = Decimal("MaxParticipationRate", .10m, .01m, 1m);
        _maxContracts = Int("MaxContracts", 1, 1, 100);
        _entryTimeoutEvents = Int("EntryTimeoutEvents", 10, 1, 10000);
        _stopLossTicks = Decimal("StopLossTicks", 4m, 1m, 100m);
        _breakevenTriggerTicks = Decimal("BreakevenTriggerTicks", 4m, .1m, 100m);
        _costBufferTicks = Decimal("CostBufferTicks", 1.5m, 0m, 100m);
        _trailingDrawdownTicks = Decimal("TrailingDrawdownTicks", 3m, .1m, 100m);
        _obiExit = Decimal("ObiExit", -.05m, -.99m, .99m);
        _tiExit = Decimal("TiExit", -.05m, -.99m, .99m);
        _ofiZExit = Decimal("OfiZExit", -.25m, -20m, 20m);
        _maxHoldEvents = Int("MaxHoldEvents", 1200, 1, 1000000);
        _minProgressTicks = Decimal("MinProgressTicks", 3m, 0m, 100m);
        _preCloseMinutes = Int("PreCloseMinutes", 10, 1, 60);
        _freshnessSeconds = Int("FreshnessSeconds", 2, 1, 60);
        _maxDailyTrades = Int("MaxDailyTrades", 20, 1, 10000);
        _maxConsecutiveLosses = Int("MaxConsecutiveLosses", 3, 1, 100);
        _lossCooldownMinutes = Int("LossCooldownMinutes", 15, 1, 1440);
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
            throw new InvalidOperationException("Order Book Trend recovery run generation does not match runtime.");
        if (_restoredBeforeStart)
        {
            _restoredBeforeStart = false;
            _paused = true;
            State = StrategyLifecycleState.Suspended;
            _reason = "RecoveryAwaitingResume";
        }
        else { State = StrategyLifecycleState.WarmingUp; _reason = WarmupReason(); }
        Publish();
    }

    public void OnMarketEvent(MarketEvent marketEvent)
    {
        if (_context == null || State is StrategyLifecycleState.Stopped or StrategyLifecycleState.Faulted ||
            !string.Equals(marketEvent.Symbol, _context.Universe.Tradable.Symbol, StringComparison.OrdinalIgnoreCase)) return;

        PositionSnapshot position = Position();
        if (!TryUpdateFeatures(marketEvent, out string dataReason))
        {
            if (position.NetQuantity != 0) ManagePosition(marketEvent, position, featuresValid: false);
            else if (!_entryWorking) { State = WarmupComplete() ? StrategyLifecycleState.Suspended : StrategyLifecycleState.WarmingUp; _reason = dataReason; }
            Publish();
            return;
        }

        ResetDailyCounter(marketEvent.Timestamp);
        position = Position();
        if (position.NetQuantity != 0)
        {
            ManagePosition(marketEvent, position, featuresValid: true);
            Publish();
            return;
        }

        if (_exitWorking) { _reason = "等待平倉成交回報"; Publish(); return; }
        if (_entryWorking)
        {
            ManageWorkingEntry(marketEvent);
            Publish();
            return;
        }
        if (_paused)
        {
            State = StrategyLifecycleState.Suspended;
            _reason = "使用者暫停；無部位，不建立新訊號";
            Publish();
            return;
        }
        if (!WarmupComplete())
        {
            State = StrategyLifecycleState.WarmingUp;
            _reason = WarmupReason();
            Publish();
            return;
        }
        if (marketEvent.Timestamp < _cooldownUntil)
        {
            State = StrategyLifecycleState.Ready;
            _reason = $"連續虧損冷卻至 {_cooldownUntil:MM-dd HH:mm:ss}";
            Publish();
            return;
        }
        if (_tradesToday >= _maxDailyTrades)
        {
            State = StrategyLifecycleState.Suspended;
            _reason = $"已達單日交易上限 {_maxDailyTrades} 筆";
            Publish();
            return;
        }
        if (!CanOpen(marketEvent.Timestamp, out string sessionReason))
        {
            State = StrategyLifecycleState.Suspended;
            _reason = sessionReason;
            Publish();
            return;
        }
        if (!PassesLiquidityAndVolatility(marketEvent, out string filterReason))
        {
            ResetConfirmationIfOpposed();
            State = StrategyLifecycleState.Ready;
            _reason = filterReason;
            Publish();
            return;
        }

        int direction = SignalDirection();
        if (!Confirm(direction))
        {
            State = StrategyLifecycleState.Ready;
            _reason = direction == 0
                ? FeatureReason("等待五項訊號同向")
                : $"{(direction > 0 ? "多" : "空")}方確認 {_confirmations.Count(x => x)}/{_confirmPasses}（窗 {_confirmations.Count}/{_confirmEvents}）";
            Publish();
            return;
        }
        if (_expectedMfeTicks < _minimumExpectedMoveTicks)
        {
            State = StrategyLifecycleState.Ready;
            _reason = $"校準 MFE={_expectedMfeTicks:F2}t 低於門檻 {_minimumExpectedMoveTicks:F2}t";
            Publish();
            return;
        }

        SubmitEntry(direction, marketEvent);
        Publish();
    }

    public void OnExecution(ExecutionEvent execution)
    {
        if (_context == null || !string.Equals(execution.StrategyInstanceId, InstanceId, StringComparison.OrdinalIgnoreCase)) return;
        if (execution.Role is PaperOrderRole.EntryLong or PaperOrderRole.EntryShort && string.IsNullOrEmpty(_currentCycleId))
            _currentCycleId = string.IsNullOrWhiteSpace(execution.CycleId) ? $"cycle-{++_cycleSequence}" : execution.CycleId;
        if (execution.Role is PaperOrderRole.EntryLong or PaperOrderRole.EntryShort)
        {
            CancelActiveOrders();
            _entryWorking = false;
            _entryPrice = execution.Price;
            _entryAt = execution.Timestamp;
            _holdingEvents = 0;
            _bestMid = _microprice > 0m ? _microprice : execution.Price;
            bool isLong = execution.Role == PaperOrderRole.EntryLong;
            _fixedStop = isLong ? RoundDown(execution.Price - _stopLossTicks * TickSize()) : RoundUp(execution.Price + _stopLossTicks * TickSize());
            _activeStop = _fixedStop;
            SubmitProtectiveStop(isLong, _activeStop, PaperOrderRole.StopLoss);
            State = StrategyLifecycleState.Running;
            _reason = $"{(isLong ? "多" : "空")}單成交 {execution.Price}；固定停損 {_activeStop}";
        }
        else if (execution.Role is PaperOrderRole.TakeProfit or PaperOrderRole.StopLoss or PaperOrderRole.Flatten)
        {
            CancelActiveOrders();
            _exitWorking = false;
            _completedTrades++;
            _tradesToday++;
            decimal tradeNet = Position().NetPnl - _entryNetPnl;
            if (tradeNet < 0m) _consecutiveLosses++; else _consecutiveLosses = 0;
            if (_consecutiveLosses >= _maxConsecutiveLosses)
            {
                _cooldownUntil = execution.Timestamp.AddMinutes(_lossCooldownMinutes);
                _consecutiveLosses = 0;
            }
            _entryPrice = _fixedStop = _activeStop = _bestMid = 0m;
            State = _paused ? StrategyLifecycleState.Suspended : StrategyLifecycleState.Ready;
            _reason = $"{ExitLabel(execution.Role)}完成；本筆淨變動 ¥{tradeNet:F0}";
        }
        Publish();
    }

    public void Pause(string reason)
    {
        if (State is StrategyLifecycleState.Stopped or StrategyLifecycleState.Faulted) return;
        _paused = true;
        if (Position().NetQuantity == 0)
        {
            CancelActiveOrders();
            _entryWorking = false;
        }
        State = StrategyLifecycleState.Suspended;
        _reason = Position().NetQuantity == 0 ? reason : reason + "；既有部位仍受停損與風控保護";
        Publish();
    }

    public void Resume()
    {
        if (State != StrategyLifecycleState.Suspended) return;
        _paused = false;
        State = Position().NetQuantity != 0 ? StrategyLifecycleState.Running : WarmupComplete() ? StrategyLifecycleState.Ready : StrategyLifecycleState.WarmingUp;
        _reason = State == StrategyLifecycleState.WarmingUp ? WarmupReason() : "已繼續，等待下一筆有效事件";
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

    public StrategySnapshot GetSnapshot()
    {
        PositionSnapshot position = Position();
        return new StrategySnapshot
        {
            StrategyType = StrategyType,
            InstanceId = InstanceId,
            ParameterSummary = ParameterSummary(),
            TradableProductId = _context?.Universe.Tradable.ProductId ?? _definition.Universe.Tradable.ProductId,
            TradableSymbol = _context?.Universe.Tradable.Symbol ?? string.Empty,
            ReferenceSummary = "僅交易商品五檔＋逐筆；時間戳目前為接收端",
            State = State,
            Reason = _reason,
            LastPrice = _previousTradePrice,
            CenterPrice = _microprice,
            Range = _rvTicks,
            BuyEntry = _buyEntry,
            SellEntry = _sellEntry,
            StopPrice = _activeStop,
            Position = position.NetQuantity,
            RealizedPnl = position.RealizedPnl,
            UnrealizedPnl = position.UnrealizedPnl,
            NetPnl = position.NetPnl,
            CompletedTrades = _completedTrades,
            UpdatedAt = _context?.Clock.Now ?? DateTimeOffset.Now
        };
    }

    private bool TryUpdateFeatures(MarketEvent market, out string reason)
    {
        if (_lastSequence > 0 && market.Sequence > 0 && market.Sequence <= _lastSequence)
        {
            reason = $"事件序號未遞增：{market.Sequence} <= {_lastSequence}";
            return false;
        }
        if (!ValidFiveLevelBook(market))
        {
            reason = "等待有效且未交叉的完整五檔委託簿";
            return false;
        }
        if (_lastValidAt != default && market.Timestamp < _lastValidAt)
        {
            reason = "事件時間倒退，已排除該事件";
            return false;
        }

        decimal tick = TickSize();
        decimal mid = market.MidPrice;
        decimal priorMid = _midHistory.Count == 0 ? mid : _midHistory.Last();
        _midHistory.Enqueue(mid);
        while (_midHistory.Count > _momentumEvents + 1) _midHistory.Dequeue();
        if (_midHistory.Count > 1)
        {
            _midChanges.Enqueue((mid - priorMid) / tick);
            while (_midChanges.Count > _volatilityWindow) _midChanges.Dequeue();
        }
        _momentumTicks = _midHistory.Count > _momentumEvents ? (mid - _midHistory.Peek()) / tick : 0m;
        _rvTicks = StandardDeviation(_midChanges);

        decimal weightedBid = 0m, weightedAsk = 0m;
        for (int i = 0; i < 5; i++)
        {
            weightedBid += DepthWeights[i] * market.BidQuantities[i];
            weightedAsk += DepthWeights[i] * market.AskQuantities[i];
        }
        _weightedDepth = weightedBid + weightedAsk;
        _obi = _weightedDepth <= 0m ? 0m : (weightedBid - weightedAsk) / _weightedDepth;
        decimal topTotal = market.BidQuantities[0] + market.AskQuantities[0];
        _microprice = topTotal <= 0m ? mid :
            (market.AskPrices[0] * market.BidQuantities[0] + market.BidPrices[0] * market.AskQuantities[0]) / topTotal;
        _micropriceEdgeTicks = (_microprice - mid) / tick;

        decimal ofiEvent = _previousBook == null ? 0m : CalculateOfi(_previousBook, market);
        _ofiEvents.Enqueue(ofiEvent);
        while (_ofiEvents.Count > _ofiWindow) _ofiEvents.Dequeue();
        _ofiRaw = _ofiEvents.Sum();
        _ofiZ = (_ofiRaw - _ofiMean) / _ofiStd;

        if (market.Kind == MarketEventKind.Trade && market.LastPrice > 0m && market.TradeQuantity > 0)
        {
            int direction = ClassifyTrade(market);
            if (direction != 0)
            {
                _tradeDirections.Enqueue(direction * market.TradeQuantity);
                while (_tradeDirections.Count > _tiTrades) _tradeDirections.Dequeue();
                _lastTradeDirection = direction;
            }
            _previousTradePrice = market.LastPrice;
        }
        int absoluteTradeVolume = _tradeDirections.Sum(x => Math.Abs(x));
        _ti = absoluteTradeVolume == 0 ? 0m : (decimal)_tradeDirections.Sum() / absoluteTradeVolume;
        _previousBook = market;
        if (market.Sequence > 0) _lastSequence = market.Sequence;
        _lastValidAt = market.Timestamp;
        reason = string.Empty;
        return true;
    }

    private void ManageWorkingEntry(MarketEvent market)
    {
        _entryWorkingEvents++;
        int direction = SignalDirection();
        bool reversed = (_candidateDirection > 0 && (_obi < 0m || _ti < 0m || direction < 0)) ||
                        (_candidateDirection < 0 && (_obi > 0m || _ti > 0m || direction > 0));
        if (_entryWorkingEvents < _entryTimeoutEvents && !reversed)
        {
            State = StrategyLifecycleState.Running;
            _reason = $"進場限價等待成交 {_entryWorkingEvents}/{_entryTimeoutEvents} 事件";
            return;
        }
        CancelActiveOrders();
        _entryWorking = false;
        ResetConfirmation();
        State = StrategyLifecycleState.Ready;
        _reason = reversed ? "方向訊號反轉，取消未成交進場單" : "進場逾時，取消未成交進場單";
    }

    private void ManagePosition(MarketEvent market, PositionSnapshot position, bool featuresValid)
    {
        if (_exitWorking) { _reason = "等待風控平倉成交"; return; }
        bool isLong = position.NetQuantity > 0;
        _holdingEvents++;
        decimal mid = featuresValid ? market.MidPrice : (market.LastPrice > 0m ? market.LastPrice : _bestMid);
        if (mid > 0m) _bestMid = isLong ? Math.Max(_bestMid, mid) : Math.Min(_bestMid == 0m ? mid : _bestMid, mid);
        decimal favorableTicks = isLong ? (_bestMid - _entryPrice) / TickSize() : (_entryPrice - _bestMid) / TickSize();

        if (IsFlattenTime(market.Timestamp))
        {
            SubmitFlatten(isLong, market, "預定休市前強制平倉");
            return;
        }
        if (!featuresValid)
        {
            State = StrategyLifecycleState.Running;
            _reason = "持倉中但五檔無效；保留既有保護停損，不建立新條件";
            return;
        }

        bool reversal = isLong
            ? _obi <= _obiExit && _ti <= _tiExit && _ofiZ <= _ofiZExit
            : _obi >= -_obiExit && _ti >= -_tiExit && _ofiZ >= -_ofiZExit;
        bool timeStop = _holdingEvents >= _maxHoldEvents && favorableTicks < _minProgressTicks;
        if (reversal || timeStop)
        {
            SubmitFlatten(isLong, market, reversal ? "微結構三項同時反轉" : "持倉事件逾時且進展不足");
            return;
        }

        decimal desiredStop = _fixedStop;
        PaperOrderRole stopRole = PaperOrderRole.StopLoss;
        if (favorableTicks >= _breakevenTriggerTicks)
        {
            decimal breakeven = isLong ? RoundDown(_entryPrice + _costBufferTicks * TickSize()) : RoundUp(_entryPrice - _costBufferTicks * TickSize());
            decimal trailing = isLong ? RoundDown(_bestMid - _trailingDrawdownTicks * TickSize()) : RoundUp(_bestMid + _trailingDrawdownTicks * TickSize());
            desiredStop = isLong ? Math.Max(_fixedStop, Math.Max(breakeven, trailing)) : Math.Min(_fixedStop, Math.Min(breakeven, trailing));
            if (desiredStop != _fixedStop) stopRole = PaperOrderRole.Flatten;
        }

        if (desiredStop != _activeStop)
        {
            decimal executable = isLong ? market.BestBid : market.BestAsk;
            bool alreadyCrossed = isLong ? executable <= desiredStop : executable >= desiredStop;
            if (alreadyCrossed)
            {
                SubmitFlatten(isLong, market, "移動保護價已觸及");
                return;
            }
            CancelActiveOrders();
            _activeStop = desiredStop;
            SubmitProtectiveStop(isLong, desiredStop, stopRole);
        }
        State = StrategyLifecycleState.Running;
        _reason = $"持倉保護：MFE={favorableTicks:F2}t，Stop={_activeStop}，OBI={_obi:F2}，OFIz={_ofiZ:F2}，TI={_ti:F2}";
    }

    private void SubmitEntry(int direction, MarketEvent market)
    {
        int visible = direction > 0 ? market.AskQuantities[0] : market.BidQuantities[0];
        int quantity = Math.Min(_maxContracts, (int)Math.Floor(visible * _maxParticipationRate));
        if (quantity < 1)
        {
            State = StrategyLifecycleState.Ready;
            _reason = $"最佳檔可見量 {visible} 不足以符合參與率 {_maxParticipationRate:P0}";
            return;
        }
        decimal price = direction > 0 ? RoundUp(market.BestAsk) : RoundDown(market.BestBid);
        _currentCycleId = $"cycle-{++_cycleSequence}";
        _buyEntry = direction > 0 ? price : 0m;
        _sellEntry = direction < 0 ? price : 0m;
        EmitOrder(direction > 0 ? BuySell.Buy : BuySell.Sell, PaperOrderType.Limit,
            direction > 0 ? PaperOrderRole.EntryLong : PaperOrderRole.EntryShort, price, quantity);
        _entryNetPnl = Position().NetPnl;
        _entryWorking = true;
        _entryWorkingEvents = 0;
        State = StrategyLifecycleState.Running;
        _reason = $"{(direction > 0 ? "多" : "空")}方五項訊號確認，送出可成交限價 {price} × {quantity}";
    }

    private void SubmitProtectiveStop(bool isLong, decimal price, PaperOrderRole role)
    {
        EmitOrder(isLong ? BuySell.Sell : BuySell.Buy, PaperOrderType.Stop, role, price,
            Math.Max(1, Math.Abs(Position().NetQuantity)));
    }

    private void SubmitFlatten(bool isLong, MarketEvent market, string reason)
    {
        CancelActiveOrders();
        decimal price = isLong ? market.BestBid : market.BestAsk;
        if (price <= 0m) price = market.LastPrice > 0m ? market.LastPrice : _entryPrice;
        price = isLong ? RoundDown(price) : RoundUp(price);
        EmitOrder(isLong ? BuySell.Sell : BuySell.Buy, PaperOrderType.Market, PaperOrderRole.Flatten,
            price, Math.Max(1, Math.Abs(Position().NetQuantity)));
        _exitWorking = true;
        State = StrategyLifecycleState.Running;
        _reason = reason + "，送出風控平倉";
    }

    private void EmitOrder(BuySell side, PaperOrderType type, PaperOrderRole role, decimal price, int quantity)
    {
        if (_context == null || string.IsNullOrWhiteSpace(_currentCycleId)) return;
        OrderRequested?.Invoke(new PaperOrderRequest
        {
            ClientOrderId = (_orderBook ?? throw new InvalidOperationException("Order book not initialized."))
                .NextClientOrderId(role.ToString(), _currentCycleId),
            CycleId = _currentCycleId, StrategyRunGeneration = _strategyRunGeneration,
            StrategyInstanceId = InstanceId, Symbol = _context.Universe.Tradable.Symbol,
            Side = side, Type = type, Role = role, Price = price, Quantity = quantity
        });
    }

    public void OnOrderSubmitted(PaperOrderRequest request) =>
        (_orderBook ?? throw new InvalidOperationException("Order book not initialized.")).Track(request);

    public void OnOrderLifecycle(OrderLifecycleEvent orderEvent)
    {
        if (_orderBook == null || !_orderBook.TryGet(orderEvent.ClientOrderId, out PaperOrderRequest? request) ||
            request == null || !_orderBook.Apply(orderEvent) ||
            orderEvent.State is not (OrderLifecycleState.Cancelled or OrderLifecycleState.Rejected)) return;
        if (request.Role is PaperOrderRole.EntryLong or PaperOrderRole.EntryShort)
        {
            _entryWorking = _orderBook.HasActiveRole(PaperOrderRole.EntryLong) || _orderBook.HasActiveRole(PaperOrderRole.EntryShort);
            if (!_entryWorking && Position().NetQuantity == 0 && State != StrategyLifecycleState.Stopped)
            { State = _paused ? StrategyLifecycleState.Suspended : StrategyLifecycleState.Ready; _reason = "進場委託已撤銷／拒絕"; Publish(); }
        }
        else if (Position().NetQuantity != 0)
        { _exitWorking = false; State = StrategyLifecycleState.Suspended; _reason = "保護／平倉委託已撤銷／拒絕，需人工處理"; Publish(); }
    }

    public StrategyRecoveryCheckpoint CaptureRecoveryCheckpoint()
    {
        if (_orderBook == null) throw new InvalidOperationException("Order Book Trend has not initialized its managed order book.");
        var state = new RecoveryState(_midHistory.ToArray(), _midChanges.ToArray(), _ofiEvents.ToArray(),
            _tradeDirections.ToArray(), _confirmations.ToArray(), _previousBook, _lastValidAt, _entryAt, _cooldownUntil,
            _previousTradePrice, _entryPrice, _fixedStop, _activeStop, _bestMid, _entryNetPnl, _momentumTicks,
            _obi, _ofiRaw, _ofiZ, _ti, _microprice, _micropriceEdgeTicks, _rvTicks, _weightedDepth, _buyEntry,
            _sellEntry, _lastTradeDirection, _candidateDirection, _entryWorkingEvents, _holdingEvents,
            _lastSequence, _completedTrades, _tradesToday, _consecutiveLosses, _tradingDate, _entryWorking,
            _exitWorking, _paused, _reason, State, _cycleSequence, _currentCycleId, _orderBook.Capture());
        return new StrategyRecoveryCheckpoint(StrategyType, InstanceId, _strategyRunGeneration, RecoverySchema,
            DateTimeOffset.UtcNow, JsonSerializer.Serialize(state));
    }

    public void RestoreRecoveryCheckpoint(StrategyRecoveryCheckpoint checkpoint)
    {
        StrategyRecoveryCheckpointValidator.EnsureCompatible(checkpoint, StrategyType, InstanceId,
            checkpoint.StrategyRunGeneration, RecoverySchema);
        RecoveryState state = JsonSerializer.Deserialize<RecoveryState>(checkpoint.PayloadJson)
            ?? throw new InvalidOperationException("Order Book Trend recovery payload is empty.");
        if (_strategyRunGeneration != 0 && _strategyRunGeneration != checkpoint.StrategyRunGeneration)
            throw new InvalidOperationException("Order Book Trend checkpoint generation conflicts with runtime.");
        _strategyRunGeneration = checkpoint.StrategyRunGeneration;
        _orderBook ??= new ManagedStrategyOrderBook(InstanceId, _strategyRunGeneration);
        _orderBook.Restore(state.OrderBook);
        _midHistory.Clear(); foreach (decimal x in state.MidHistory) _midHistory.Enqueue(x);
        _midChanges.Clear(); foreach (decimal x in state.MidChanges) _midChanges.Enqueue(x);
        _ofiEvents.Clear(); foreach (decimal x in state.OfiEvents) _ofiEvents.Enqueue(x);
        _tradeDirections.Clear(); foreach (int x in state.TradeDirections) _tradeDirections.Enqueue(x);
        _confirmations.Clear(); foreach (bool x in state.Confirmations) _confirmations.Enqueue(x);
        _previousBook = state.PreviousBook; _lastValidAt = state.LastValidAt; _entryAt = state.EntryAt; _cooldownUntil = state.CooldownUntil;
        _previousTradePrice = state.PreviousTradePrice; _entryPrice = state.EntryPrice; _fixedStop = state.FixedStop;
        _activeStop = state.ActiveStop; _bestMid = state.BestMid; _entryNetPnl = state.EntryNetPnl;
        _momentumTicks = state.MomentumTicks; _obi = state.Obi; _ofiRaw = state.OfiRaw; _ofiZ = state.OfiZ;
        _ti = state.Ti; _microprice = state.Microprice; _micropriceEdgeTicks = state.MicropriceEdgeTicks;
        _rvTicks = state.RvTicks; _weightedDepth = state.WeightedDepth; _buyEntry = state.BuyEntry; _sellEntry = state.SellEntry;
        _lastTradeDirection = state.LastTradeDirection; _candidateDirection = state.CandidateDirection;
        _entryWorkingEvents = state.EntryWorkingEvents; _holdingEvents = state.HoldingEvents; _lastSequence = state.LastSequence;
        _completedTrades = state.CompletedTrades; _tradesToday = state.TradesToday; _consecutiveLosses = state.ConsecutiveLosses;
        _tradingDate = state.TradingDate; _entryWorking = state.EntryWorking; _exitWorking = state.ExitWorking;
        _paused = state.Paused; _reason = state.Reason; State = state.State;
        _cycleSequence = state.CycleSequence; _currentCycleId = state.CurrentCycleId;
        _restoredBeforeStart = _context == null;
    }

    private void CancelActiveOrders()
    {
        if (_orderBook == null) return;
        foreach (string clientOrderId in _orderBook.ActiveClientOrderIds) CancelOrderRequested?.Invoke(clientOrderId);
    }

    private int SignalDirection()
    {
        bool longSignal = _momentumTicks >= _momentumEntryTicks && _obi >= _obiEntry && _ofiZ >= _ofiZEntry &&
                          _ti >= _tiEntry && _micropriceEdgeTicks >= _micropriceEntryTicks;
        bool shortSignal = _momentumTicks <= -_momentumEntryTicks && _obi <= -_obiEntry && _ofiZ <= -_ofiZEntry &&
                           _ti <= -_tiEntry && _micropriceEdgeTicks <= -_micropriceEntryTicks;
        return longSignal == shortSignal ? 0 : longSignal ? 1 : -1;
    }

    private bool Confirm(int direction)
    {
        if (direction != 0 && direction != _candidateDirection)
        {
            _candidateDirection = direction;
            _confirmations.Clear();
        }
        if (direction == 0 && ((_candidateDirection > 0 && (_obi < 0m || _ti < 0m)) ||
                               (_candidateDirection < 0 && (_obi > 0m || _ti > 0m))))
            ResetConfirmation();
        if (_candidateDirection == 0) return false;
        _confirmations.Enqueue(direction == _candidateDirection);
        while (_confirmations.Count > _confirmEvents) _confirmations.Dequeue();
        return _confirmations.Count == _confirmEvents && _confirmations.Count(x => x) >= _confirmPasses;
    }

    private bool PassesLiquidityAndVolatility(MarketEvent market, out string reason)
    {
        decimal spread = (market.BestAsk - market.BestBid) / TickSize();
        if ((market.Timestamp - _lastValidAt).TotalSeconds > _freshnessSeconds) { reason = "五檔行情已過期"; return false; }
        if (spread > _maximumSpreadTicks) { reason = $"價差 {spread:F2}t 超過 {_maximumSpreadTicks:F2}t"; return false; }
        if (_weightedDepth < _minimumWeightedDepth) { reason = $"加權五檔深度 {_weightedDepth:F1} 低於 {_minimumWeightedDepth:F1}"; return false; }
        if (_rvTicks < _volatilityLowTicks || _rvTicks > _volatilityHighTicks) { reason = $"事件波動 {_rvTicks:F2}t 不在 [{_volatilityLowTicks:F2},{_volatilityHighTicks:F2}]"; return false; }
        reason = string.Empty;
        return true;
    }

    private bool WarmupComplete() => _midHistory.Count > _momentumEvents && _ofiEvents.Count >= _ofiWindow &&
                                     _tradeDirections.Count >= _tiTrades && _midChanges.Count >= _volatilityWindow;
    private string WarmupReason() => $"暖機：mid {_midHistory.Count}/{_momentumEvents + 1}，OFI {_ofiEvents.Count}/{_ofiWindow}，TI {_tradeDirections.Count}/{_tiTrades}，RV {_midChanges.Count}/{_volatilityWindow}";

    private bool CanOpen(DateTimeOffset timestamp, out string reason)
    {
        TimeSpan time = TokyoTime(timestamp).TimeOfDay;
        bool day = time >= new TimeSpan(8, 45, 0) && time < new TimeSpan(15, 45 - _preCloseMinutes, 0);
        bool night = time >= new TimeSpan(17, 0, 0) || time < new TimeSpan(6, 0, 0) - TimeSpan.FromMinutes(_preCloseMinutes);
        if (day || night) { reason = string.Empty; return true; }
        reason = $"非可進場時段（JST {time:hh\\:mm\\:ss}，休市前 {_preCloseMinutes} 分鐘禁開倉）";
        return false;
    }

    private bool IsFlattenTime(DateTimeOffset timestamp)
    {
        TimeSpan time = TokyoTime(timestamp).TimeOfDay;
        TimeSpan dayClose = new(15, 45, 0), nightClose = new(6, 0, 0);
        return (time >= dayClose - TimeSpan.FromMinutes(_preCloseMinutes) && time < new TimeSpan(17, 0, 0)) ||
               (time >= nightClose - TimeSpan.FromMinutes(_preCloseMinutes) && time < new TimeSpan(8, 45, 0));
    }

    private void ResetDailyCounter(DateTimeOffset timestamp)
    {
        DateOnly date = DateOnly.FromDateTime(TokyoTime(timestamp).AddHours(-6).DateTime);
        if (_tradingDate == date) return;
        _tradingDate = date;
        _tradesToday = 0;
    }

    private static DateTimeOffset TokyoTime(DateTimeOffset value)
    {
        try { return TimeZoneInfo.ConvertTime(value, TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time")); }
        catch (TimeZoneNotFoundException) { return value.ToOffset(TimeSpan.FromHours(9)); }
    }

    private int ClassifyTrade(MarketEvent market)
    {
        if (market.LastPrice >= market.BestAsk) return 1;
        if (market.LastPrice <= market.BestBid) return -1;
        if (_previousTradePrice > 0m)
        {
            if (market.LastPrice > _previousTradePrice) return 1;
            if (market.LastPrice < _previousTradePrice) return -1;
        }
        return _lastTradeDirection;
    }

    private static decimal CalculateOfi(MarketEvent previous, MarketEvent current)
    {
        decimal value = 0m;
        for (int i = 0; i < 5; i++)
        {
            decimal bid = current.BidPrices[i] > previous.BidPrices[i] ? current.BidQuantities[i] :
                current.BidPrices[i] == previous.BidPrices[i] ? current.BidQuantities[i] - previous.BidQuantities[i] : -previous.BidQuantities[i];
            decimal ask = current.AskPrices[i] < previous.AskPrices[i] ? current.AskQuantities[i] :
                current.AskPrices[i] == previous.AskPrices[i] ? current.AskQuantities[i] - previous.AskQuantities[i] : -previous.AskQuantities[i];
            value += DepthWeights[i] * (bid - ask);
        }
        return value;
    }

    private static bool ValidFiveLevelBook(MarketEvent market)
    {
        if (market.BidPrices.Length < 5 || market.AskPrices.Length < 5 || market.BidQuantities.Length < 5 || market.AskQuantities.Length < 5) return false;
        for (int i = 0; i < 5; i++)
        {
            if (market.BidPrices[i] <= 0m || market.AskPrices[i] <= 0m || market.BidQuantities[i] <= 0 || market.AskQuantities[i] <= 0) return false;
            if (i > 0 && (market.BidPrices[i] >= market.BidPrices[i - 1] || market.AskPrices[i] <= market.AskPrices[i - 1])) return false;
        }
        return market.BestBid < market.BestAsk;
    }

    private string ParameterSummary() =>
        $"Nmom={_momentumEvents}; M={_momentumEntryTicks}t; OBI={_obiEntry}; Nofi={_ofiWindow}; OFIz={_ofiZEntry} (mu={_ofiMean},sd={_ofiStd}); Nti={_tiTrades}; TI={_tiEntry}; MP={_micropriceEntryTicks}t; Confirm={_confirmPasses}/{_confirmEvents}; Spread={_maximumSpreadTicks}t; Depth={_minimumWeightedDepth}; RV={_volatilityLowTicks}-{_volatilityHighTicks}t/{_volatilityWindow}; MFE(cal)={_expectedMfeTicks}>={_minimumExpectedMoveTicks}t; Part={_maxParticipationRate:P0}; Qty<={_maxContracts}; EntryTO={_entryTimeoutEvents}e; SL={_stopLossTicks}t; BE={_breakevenTriggerTicks}t+{_costBufferTicks}t; Trail={_trailingDrawdownTicks}t; Exit={_obiExit}/{_ofiZExit}/{_tiExit}; Hold={_maxHoldEvents}e/{_minProgressTicks}t; PreClose={_preCloseMinutes}m; Fresh={_freshnessSeconds}s; DayTrades={_maxDailyTrades}; LossPause={_maxConsecutiveLosses}/{_lossCooldownMinutes}m";

    private string FeatureReason(string prefix) => $"{prefix}：Mom={_momentumTicks:F2}t，OBI={_obi:F2}，OFIz={_ofiZ:F2}，TI={_ti:F2}，MP={_micropriceEdgeTicks:F2}t";
    private void ResetConfirmationIfOpposed() { if ((_candidateDirection > 0 && (_obi < 0m || _ti < 0m)) || (_candidateDirection < 0 && (_obi > 0m || _ti > 0m))) ResetConfirmation(); }
    private void ResetConfirmation() { _candidateDirection = 0; _confirmations.Clear(); }
    private PositionSnapshot Position() => _context == null ? new PositionSnapshot() : _context.Performance.GetPosition(InstanceId, _context.Universe.Tradable.Symbol);
    private decimal TickSize() => Math.Max(.00001m, _context?.Universe.Tradable.TickSize ?? 5m);
    private decimal RoundDown(decimal price) => Math.Floor(price / TickSize()) * TickSize();
    private decimal RoundUp(decimal price) => Math.Ceiling(price / TickSize()) * TickSize();
    private void Publish() => SnapshotChanged?.Invoke(GetSnapshot());
    private static string ExitLabel(PaperOrderRole role) => role == PaperOrderRole.StopLoss ? "固定停損" : role == PaperOrderRole.TakeProfit ? "停利" : "風控平倉";
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
