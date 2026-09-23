using System.Globalization;
using System.Text.Json;
using KGIOptionPricing.OrderManagement.Core;
using KGIOptionPricing.StrategyFramework;
using KGIOptionPricing.StrategyFramework.Recovery;

namespace KGIOptionPricing.Strategies.NikkeiMicrostructureAlpha;

public sealed class NikkeiMicrostructureAlphaFactory : IStrategyFactory
{
    public const string TypeName = "NikkeiMicrostructureAlpha";
    public string StrategyType => TypeName;
    public string DisplayName => "OSE 微型日經微結構 Alpha";
    public ISimulationStrategy Create(StrategyDefinition definition) => new NikkeiMicrostructureAlphaStrategy(definition);
}

/// <summary>
/// Explainable rule-based baseline for the OSE 225MC research specification.
/// It intentionally uses only fields present in MarketEvent. A trained LightGBM
/// model, exchange-native sequence recovery, order-level cancels and FIFO queue
/// simulation require richer data and remain separate, controlled research work.
/// </summary>
public sealed class NikkeiMicrostructureAlphaStrategy : ISimulationStrategy, IManagedOrderStrategy,
    IManagedOrderSubmissionObserver, IStrategyRecoveryParticipant
{
    private sealed record TradeFlow(DateTimeOffset Time, int SignedQuantity, int Quantity);
    private sealed record MidSample(DateTimeOffset Time, decimal Mid);
    private sealed record RecoveryState(KeyValuePair<string, MarketEvent>[] Latest,
        KeyValuePair<string, MidSample[]>[] Mids, TradeFlow[] TradeFlows, DateTimeOffset? FirstHealthyAt,
        DateTimeOffset EntryRequestedAt, DateTimeOffset EntryAt, DateTimeOffset CooldownUntil, BuySell EntrySide,
        bool EntryWorking, bool ExitWorking, decimal Alpha, decimal Obi, decimal Aofi, decimal ReferenceScore,
        decimal ExpectedMoveTicks, decimal ExpectedNetEdge, decimal Atr, decimal EntryPrice, decimal EntryOrderPrice,
        decimal TakeProfitPrice, decimal StopPrice, int CompletedTrades, long LastSequence, string Reason,
        StrategyLifecycleState State, int CycleSequence, string CurrentCycleId, ManagedOrderBookCheckpoint OrderBook);

    private readonly StrategyDefinition _definition;
    private readonly Dictionary<string, MarketEvent> _latest = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Queue<MidSample>> _mids = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<TradeFlow> _tradeFlows = new();
    private readonly int _warmupSeconds, _flowWindowSeconds, _volatilityWindowSeconds;
    private readonly int _minimumDepth, _maximumSpreadTicks, _minimumMoveTicks;
    private readonly int _baseStopTicks, _baseTakeProfitTicks, _maxHoldingSeconds;
    private readonly int _entryTimeoutMilliseconds, _freshnessSeconds, _cooldownSeconds;
    private readonly decimal _entryAlpha, _continuationAlpha, _minimumNetEdge;
    private readonly decimal _minimumRewardRisk, _roundTripCost, _stopSlippageTicks;
    private StrategyRuntimeContext? _context;
    private DateTimeOffset? _firstHealthyAt;
    private DateTimeOffset _entryRequestedAt, _entryAt, _cooldownUntil;
    private BuySell _entrySide;
    private bool _entryWorking, _exitWorking;
    private decimal _alpha, _obi, _aofi, _referenceScore, _expectedMoveTicks, _expectedNetEdge;
    private decimal _atr, _entryPrice, _entryOrderPrice, _takeProfitPrice, _stopPrice;
    private int _completedTrades;
    private long _lastSequence;
    private string _reason = "尚未啟動";
    private ManagedStrategyOrderBook? _orderBook;
    private long _strategyRunGeneration;
    private int _cycleSequence;
    private string _currentCycleId = string.Empty;
    private bool _restoredBeforeStart;

    public string StrategyType => NikkeiMicrostructureAlphaFactory.TypeName;
    public string InstanceId => _definition.InstanceId;
    public StrategyLifecycleState State { get; private set; } = StrategyLifecycleState.Created;
    public event Action<PaperOrderRequest>? OrderRequested;
    public event Action? CancelAllRequested;
    public event Action<StrategySnapshot>? SnapshotChanged;
    public event Action<string>? CancelOrderRequested;
    public string RecoverySchema => "nikkei-microstructure-alpha/v1";

    public NikkeiMicrostructureAlphaStrategy(StrategyDefinition definition)
    {
        _definition = definition;
        _warmupSeconds = Int("WarmupSeconds", 60, 1, 3600);
        _flowWindowSeconds = Int("FlowWindowSeconds", 30, 1, 300);
        _volatilityWindowSeconds = Int("VolatilityWindowSeconds", 300, 10, 3600);
        _entryAlpha = Decimal("EntryAlpha", 0.70m, 0.10m, 0.99m);
        _continuationAlpha = Decimal("ContinuationAlpha", 0.65m, 0.05m, 0.99m);
        _minimumDepth = Int("MinimumDepth", 10, 1, 1000000);
        _maximumSpreadTicks = Int("MaximumSpreadTicks", 1, 1, 2);
        _minimumMoveTicks = Int("MinimumMoveTicks", 5, 1, 100);
        _minimumNetEdge = Decimal("MinimumNetEdge", 100m, 0m, 1000000m);
        _baseStopTicks = Int("BaseStopTicks", 2, 1, 100);
        _baseTakeProfitTicks = Int("BaseTakeProfitTicks", 6, 1, 100);
        _minimumRewardRisk = Decimal("MinimumRewardRisk", 1m, 0.1m, 10m);
        _stopSlippageTicks = Decimal("StopSlippageTicks", 1m, 0m, 20m);
        _entryTimeoutMilliseconds = Int("EntryTimeoutMilliseconds", 1500, 100, 10000);
        _maxHoldingSeconds = Int("MaxHoldingSeconds", 180, 1, 3600);
        _freshnessSeconds = Int("FreshnessSeconds", 2, 1, 60);
        _cooldownSeconds = Int("CooldownSeconds", 5, 0, 3600);
        _roundTripCost = Decimal("RoundTripCost", 72m, 0m, 1000000m);
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
            throw new InvalidOperationException("Nikkei Alpha recovery run generation does not match runtime.");
        if (_restoredBeforeStart) _restoredBeforeStart = false;
        else
        {
            State = StrategyLifecycleState.WarmingUp;
            _reason = $"暖機中：等待 {_warmupSeconds} 秒的 225MC/參考商品行情";
        }
        Publish();
    }

    public void OnMarketEvent(MarketEvent marketEvent)
    {
        if (_context == null || State is StrategyLifecycleState.Stopped or StrategyLifecycleState.Faulted) return;
        if (_lastSequence > 0 && marketEvent.Sequence > 0 && marketEvent.Sequence <= _lastSequence)
        {
            SuspendForData($"行情序號未遞增：{marketEvent.Sequence} <= {_lastSequence}");
            return;
        }
        if (marketEvent.Sequence > 0) _lastSequence = marketEvent.Sequence;
        _latest[marketEvent.Symbol] = marketEvent;
        UpdateMidHistory(marketEvent);
        if (IsTradable(marketEvent.Symbol) && marketEvent.Kind == MarketEventKind.Trade && marketEvent.LastPrice > 0m)
            UpdateTradeFlow(marketEvent);

        if (!IsTradable(marketEvent.Symbol)) { Publish(); return; }

        CalculateFactors(marketEvent.Timestamp);
        PositionSnapshot position = Position();
        if (position.NetQuantity != 0)
        {
            ManagePosition(marketEvent, position);
            Publish();
            return;
        }

        if (_exitWorking) { Publish(); return; }
        if (_entryWorking)
        {
            ManageWorkingEntry(marketEvent);
            Publish();
            return;
        }
        if (marketEvent.Timestamp < _cooldownUntil)
        {
            State = StrategyLifecycleState.Ready;
            _reason = $"冷卻至 {_cooldownUntil:HH:mm:ss.fff}";
            Publish();
            return;
        }
        if (!TryValidate(marketEvent.Timestamp, out string validationReason))
        {
            State = WarmupComplete(marketEvent.Timestamp) ? StrategyLifecycleState.Suspended : StrategyLifecycleState.WarmingUp;
            _reason = validationReason;
            Publish();
            return;
        }

        if (TryGetSignal(out BuySell side, out string signalReason))
            SubmitPassiveEntry(side, marketEvent);
        else
        {
            State = StrategyLifecycleState.Ready;
            _reason = signalReason;
        }
        Publish();
    }

    public void OnExecution(ExecutionEvent execution)
    {
        if (_context == null || State is StrategyLifecycleState.Stopped or StrategyLifecycleState.Faulted ||
            !string.Equals(execution.StrategyInstanceId, InstanceId, StringComparison.OrdinalIgnoreCase)) return;
        if (execution.Role is PaperOrderRole.EntryLong or PaperOrderRole.EntryShort && string.IsNullOrEmpty(_currentCycleId))
            _currentCycleId = string.IsNullOrWhiteSpace(execution.CycleId) ? $"cycle-{++_cycleSequence}" : execution.CycleId;

        if (execution.Role is PaperOrderRole.EntryLong or PaperOrderRole.EntryShort)
        {
            CancelActiveOrders();
            _entryWorking = false;
            _entrySide = execution.Side;
            _entryPrice = execution.Price;
            _entryAt = execution.Timestamp;
            int atrTicks = Math.Max(3, (int)Math.Ceiling(_atr / TickSize()));
            int stopTicks = Math.Max(_baseStopTicks, (int)Math.Ceiling(0.4m * atrTicks));
            int targetTicks = Math.Max(_baseTakeProfitTicks, (int)Math.Ceiling(0.8m * atrTicks));
            _stopPrice = execution.Side == BuySell.Buy
                ? RoundDown(execution.Price - stopTicks * TickSize())
                : RoundUp(execution.Price + stopTicks * TickSize());
            _takeProfitPrice = execution.Side == BuySell.Buy
                ? RoundUp(execution.Price + targetTicks * TickSize())
                : RoundDown(execution.Price - targetTicks * TickSize());
            Submit(execution.Side == BuySell.Buy ? BuySell.Sell : BuySell.Buy, PaperOrderType.Limit, PaperOrderRole.TakeProfit, _takeProfitPrice);
            Submit(execution.Side == BuySell.Buy ? BuySell.Sell : BuySell.Buy, PaperOrderType.Stop, PaperOrderRole.StopLoss, _stopPrice);
            State = StrategyLifecycleState.Running;
            _reason = $"{(execution.Side == BuySell.Buy ? "多" : "空")}單成交 {execution.Price}；停利 {_takeProfitPrice}／固定停損 {_stopPrice}";
        }
        else if (execution.Role is PaperOrderRole.TakeProfit or PaperOrderRole.StopLoss or PaperOrderRole.Flatten)
        {
            CancelActiveOrders();
            _exitWorking = false;
            _completedTrades++;
            _cooldownUntil = execution.Timestamp.AddSeconds(_cooldownSeconds);
            _entryPrice = _entryOrderPrice = _takeProfitPrice = _stopPrice = 0m;
            State = StrategyLifecycleState.Ready;
            _reason = execution.Role switch
            {
                PaperOrderRole.TakeProfit => "停利完成，進入冷卻",
                PaperOrderRole.StopLoss => "固定不利停損完成，進入冷卻",
                _ => "中性風控平倉完成，進入冷卻"
            };
        }
        Publish();
    }

    public void Pause(string reason)
    {
        if (State is StrategyLifecycleState.Stopped or StrategyLifecycleState.Faulted) return;
        if (Position().NetQuantity == 0)
        {
            CancelActiveOrders();
            _entryWorking = false;
        }
        State = StrategyLifecycleState.Suspended;
        _reason = Position().NetQuantity == 0 ? reason : reason + "；既有停利／固定停損仍保留";
        Publish();
    }

    public void Resume()
    {
        if (State != StrategyLifecycleState.Suspended) return;
        DateTimeOffset now = _context?.Clock.Now ?? DateTimeOffset.Now;
        State = WarmupComplete(now) ? StrategyLifecycleState.Ready : StrategyLifecycleState.WarmingUp;
        _reason = State == StrategyLifecycleState.Ready ? "已恢復，等待合格 Alpha" : $"繼續暖機至 {_warmupSeconds} 秒";
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
        decimal last = 0m;
        if (_context != null && _latest.TryGetValue(_context.Universe.Tradable.Symbol, out MarketEvent? market))
            last = market.LastPrice > 0m ? market.LastPrice : market.MidPrice;
        string references = _context == null ? string.Empty : string.Join(", ", _context.Universe.References.Select(reference =>
            _latest.TryGetValue(reference.Symbol, out MarketEvent? item)
                ? $"{reference.ProductId}:{(item.LastPrice > 0m ? item.LastPrice : item.MidPrice)}"
                : $"{reference.ProductId}:--"));
        return new StrategySnapshot
        {
            StrategyType = StrategyType,
            InstanceId = InstanceId,
            ParameterSummary = $"Warm={_warmupSeconds}s; Flow={_flowWindowSeconds}s; Vol={_volatilityWindowSeconds}s; Alpha={_entryAlpha}/{_continuationAlpha}; Depth={_minimumDepth}; Spread={_maximumSpreadTicks}t; Move={_minimumMoveTicks}t; Edge=¥{_minimumNetEdge}; SL/TP={_baseStopTicks}/{_baseTakeProfitTicks}t; RR={_minimumRewardRisk}; Wait={_entryTimeoutMilliseconds}ms; Hold={_maxHoldingSeconds}s; Fresh={_freshnessSeconds}s; Cost=¥{_roundTripCost}; Cool={_cooldownSeconds}s",
            TradableProductId = _context?.Universe.Tradable.ProductId ?? _definition.Universe.Tradable.ProductId,
            TradableSymbol = _context?.Universe.Tradable.Symbol ?? string.Empty,
            ReferenceSummary = references,
            State = State,
            Reason = _reason,
            LastPrice = last,
            CenterPrice = _alpha,
            Range = _atr,
            BuyEntry = _entrySide == BuySell.Buy ? _entryOrderPrice : 0m,
            SellEntry = _entrySide == BuySell.Sell ? _entryOrderPrice : 0m,
            StopPrice = _stopPrice,
            Position = position.NetQuantity,
            RealizedPnl = position.RealizedPnl,
            UnrealizedPnl = position.UnrealizedPnl,
            NetPnl = position.NetPnl,
            CompletedTrades = _completedTrades,
            UpdatedAt = _context?.Clock.Now ?? DateTimeOffset.Now
        };
    }

    private void UpdateMidHistory(MarketEvent marketEvent)
    {
        decimal mid = marketEvent.MidPrice;
        if (mid <= 0m) return;
        if (!_mids.TryGetValue(marketEvent.Symbol, out Queue<MidSample>? samples))
            _mids[marketEvent.Symbol] = samples = new Queue<MidSample>();
        samples.Enqueue(new MidSample(marketEvent.Timestamp, mid));
        DateTimeOffset cutoff = marketEvent.Timestamp.AddSeconds(-_volatilityWindowSeconds);
        while (samples.Count > 0 && samples.Peek().Time < cutoff) samples.Dequeue();
    }

    private void UpdateTradeFlow(MarketEvent marketEvent)
    {
        int direction = marketEvent.BestAsk > 0m && marketEvent.LastPrice >= marketEvent.BestAsk ? 1 :
            marketEvent.BestBid > 0m && marketEvent.LastPrice <= marketEvent.BestBid ? -1 : 0;
        int quantity = Math.Max(1, marketEvent.TradeQuantity);
        _tradeFlows.Enqueue(new TradeFlow(marketEvent.Timestamp, direction * quantity, quantity));
        DateTimeOffset cutoff = marketEvent.Timestamp.AddSeconds(-Math.Max(_flowWindowSeconds, _warmupSeconds));
        while (_tradeFlows.Count > 0 && _tradeFlows.Peek().Time < cutoff) _tradeFlows.Dequeue();
    }

    private void CalculateFactors(DateTimeOffset now)
    {
        if (_context == null || !_latest.TryGetValue(_context.Universe.Tradable.Symbol, out MarketEvent? primary)) return;
        decimal weightedBid = 0m, weightedAsk = 0m;
        int levels = Math.Min(5, Math.Min(primary.BidQuantities.Length, primary.AskQuantities.Length));
        for (int i = 0; i < levels; i++)
        {
            decimal weight = 1m / (i + 1m);
            weightedBid += Math.Max(0, primary.BidQuantities[i]) * weight;
            weightedAsk += Math.Max(0, primary.AskQuantities[i]) * weight;
        }
        _obi = weightedBid + weightedAsk == 0m ? 0m : (weightedBid - weightedAsk) / (weightedBid + weightedAsk);

        DateTimeOffset flowCutoff = now.AddSeconds(-_flowWindowSeconds);
        TradeFlow[] flows = _tradeFlows.Where(x => x.Time >= flowCutoff).ToArray();
        int knownVolume = flows.Where(x => x.SignedQuantity != 0).Sum(x => x.Quantity);
        _aofi = knownVolume == 0 ? 0m : flows.Sum(x => x.SignedQuantity) / (decimal)knownVolume;

        var referenceDirections = new List<decimal>();
        foreach (ResolvedProduct reference in _context.Universe.References)
        {
            if (!_mids.TryGetValue(reference.Symbol, out Queue<MidSample>? samples) || samples.Count < 2) continue;
            MidSample first = samples.Peek();
            MidSample last = samples.Last();
            if ((last.Time - first.Time).TotalSeconds <= 0 || first.Mid <= 0m) continue;
            referenceDirections.Add(Math.Clamp((last.Mid - first.Mid) / reference.TickSize / 5m, -1m, 1m));
        }
        _referenceScore = referenceDirections.Count == 0 ? 0m : referenceDirections.Average();
        _alpha = Math.Clamp(0.50m * _obi + 0.30m * _aofi + 0.20m * _referenceScore, -1m, 1m);
        _expectedMoveTicks = Math.Abs(_alpha) * Math.Max(8, _baseTakeProfitTicks);
        _expectedNetEdge = _expectedMoveTicks * TickSize() * Multiplier() - _roundTripCost;

        if (_mids.TryGetValue(_context.Universe.Tradable.Symbol, out Queue<MidSample>? primaryMids) && primaryMids.Count >= 2)
        {
            decimal[] data = primaryMids.Select(x => x.Mid).ToArray();
            decimal totalRange = data.Max() - data.Min();
            decimal meanStep = data.Zip(data.Skip(1), (a, b) => Math.Abs(b - a)).DefaultIfEmpty(0m).Average();
            _atr = Math.Max(totalRange / Math.Max(1m, (decimal)Math.Sqrt(data.Length)), meanStep);
        }
    }

    private bool TryValidate(DateTimeOffset now, out string reason)
    {
        if (_context == null) { reason = "策略尚未初始化"; return false; }
        if (!WarmupComplete(now))
        {
            int elapsed = _firstHealthyAt.HasValue ? Math.Max(0, (int)(now - _firstHealthyAt.Value).TotalSeconds) : 0;
            reason = $"暖機中：{Math.Min(elapsed, _warmupSeconds)}/{_warmupSeconds} 秒";
            return false;
        }
        foreach (ResolvedProduct product in _context.Universe.AllProducts)
        {
            if (!_latest.TryGetValue(product.Symbol, out MarketEvent? item)) { reason = $"等待 {product.ProductId} 行情"; return false; }
            if (now < item.Timestamp || (now - item.Timestamp).TotalSeconds > _freshnessSeconds) { reason = $"{product.ProductId} 行情過期"; return false; }
            if (!ValidBook(item, product.TickSize, out reason)) { reason = $"{product.ProductId}：{reason}"; return false; }
        }
        MarketEvent primary = _latest[_context.Universe.Tradable.Symbol];
        if ((primary.BestAsk - primary.BestBid) / TickSize() > _maximumSpreadTicks) { reason = "225MC 價差超過限制"; return false; }
        int depth = primary.BidQuantities.Take(5).Sum() + primary.AskQuantities.Take(5).Sum();
        if (depth < _minimumDepth) { reason = $"五檔總深度 {depth} 低於 {_minimumDepth}"; return false; }
        int atrTicks = Math.Max(3, (int)Math.Ceiling(_atr / TickSize()));
        int stopTicks = Math.Max(_baseStopTicks, (int)Math.Ceiling(0.4m * atrTicks));
        int targetTicks = Math.Max(_baseTakeProfitTicks, (int)Math.Ceiling(0.8m * atrTicks));
        decimal reward = targetTicks * TickSize() * Multiplier() - _roundTripCost;
        decimal risk = stopTicks * TickSize() * Multiplier() + _roundTripCost + _stopSlippageTicks * TickSize() * Multiplier();
        if (risk <= 0m || reward / risk < _minimumRewardRisk) { reason = $"損益比 {reward / Math.Max(1m, risk):F2} 低於 {_minimumRewardRisk:F2}"; return false; }
        reason = string.Empty;
        return true;
    }

    private bool TryGetSignal(out BuySell side, out string reason)
    {
        side = _alpha >= 0m ? BuySell.Buy : BuySell.Sell;
        bool directionConfirmed = side == BuySell.Buy
            ? _obi > 0m && _aofi >= 0m && _referenceScore >= 0m
            : _obi < 0m && _aofi <= 0m && _referenceScore <= 0m;
        if (Math.Abs(_alpha) < _entryAlpha) { reason = FactorReason("Alpha 未達門檻"); return false; }
        if (_expectedMoveTicks < _minimumMoveTicks) { reason = FactorReason("預期行情不足"); return false; }
        if (_expectedNetEdge < _minimumNetEdge) { reason = FactorReason("預期淨邊際不足"); return false; }
        if (!directionConfirmed) { reason = FactorReason("OBI/AOFI/參考商品未同向"); return false; }
        reason = FactorReason("訊號成立");
        return true;
    }

    private void SubmitPassiveEntry(BuySell side, MarketEvent marketEvent)
    {
        _currentCycleId = $"cycle-{++_cycleSequence}";
        _entrySide = side;
        _entryOrderPrice = side == BuySell.Buy ? RoundDown(marketEvent.BestBid) : RoundUp(marketEvent.BestAsk);
        _entryRequestedAt = marketEvent.Timestamp;
        _entryWorking = true;
        Submit(side, PaperOrderType.Limit, side == BuySell.Buy ? PaperOrderRole.EntryLong : PaperOrderRole.EntryShort, _entryOrderPrice);
        State = StrategyLifecycleState.Running;
        _reason = FactorReason($"被動{(side == BuySell.Buy ? "買" : "賣")}單 {_entryOrderPrice} 等待成交");
    }

    private void ManageWorkingEntry(MarketEvent marketEvent)
    {
        bool decayed = Math.Abs(_alpha) < _continuationAlpha || _expectedNetEdge <= 0m ||
                       (_entrySide == BuySell.Buy ? _obi < 0m || _aofi < 0m : _obi > 0m || _aofi > 0m);
        bool timedOut = (marketEvent.Timestamp - _entryRequestedAt).TotalMilliseconds >= _entryTimeoutMilliseconds;
        if (!decayed && !timedOut)
        {
            _reason = FactorReason("被動進場單等待成交");
            return;
        }
        CancelActiveOrders();
        _entryWorking = false;
        if (decayed || Math.Abs(_alpha) < _continuationAlpha || _expectedNetEdge < _minimumNetEdge)
        {
            State = StrategyLifecycleState.Ready;
            _reason = FactorReason(decayed ? "訊號衰退，撤銷進場" : "逾時後剩餘優勢不足");
            return;
        }
        decimal aggressivePrice = _entrySide == BuySell.Buy ? marketEvent.BestAsk : marketEvent.BestBid;
        if (aggressivePrice <= 0m) aggressivePrice = marketEvent.LastPrice;
        Submit(_entrySide, PaperOrderType.Market, _entrySide == BuySell.Buy ? PaperOrderRole.EntryLong : PaperOrderRole.EntryShort, RoundNearest(aggressivePrice));
        _entryWorking = true;
        _reason = FactorReason("被動單逾時，剩餘優勢合格，改送價格保護 IOC");
    }

    private void ManagePosition(MarketEvent marketEvent, PositionSnapshot position)
    {
        if (_exitWorking) return;
        bool longPosition = position.NetQuantity > 0;
        bool reversed = longPosition ? _alpha < 0m && _aofi < 0m : _alpha > 0m && _aofi > 0m;
        bool timedOut = _entryAt != default && (marketEvent.Timestamp - _entryAt).TotalSeconds >= _maxHoldingSeconds;
        bool unhealthy = !TryValidate(marketEvent.Timestamp, out _);
        if (!reversed && !timedOut && !unhealthy)
        {
            State = StrategyLifecycleState.Running;
            _reason = FactorReason($"持倉中；停利 {_takeProfitPrice}／固定停損 {_stopPrice}");
            return;
        }
        CancelActiveOrders();
        decimal price = longPosition ? marketEvent.BestBid : marketEvent.BestAsk;
        if (price <= 0m) price = marketEvent.LastPrice;
        Submit(longPosition ? BuySell.Sell : BuySell.Buy, PaperOrderType.Market, PaperOrderRole.Flatten, RoundNearest(price));
        _exitWorking = true;
        _reason = unhealthy ? "資料健康風控平倉" : timedOut ? $"持倉達 {_maxHoldingSeconds} 秒，風控平倉" : "Alpha/AOFI 反轉，風控平倉";
    }

    private void Submit(BuySell side, PaperOrderType type, PaperOrderRole role, decimal price)
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

    private bool ValidBook(MarketEvent item, decimal tickSize, out string reason)
    {
        if (item.BestBid <= 0m || item.BestAsk <= item.BestBid) { reason = "委託簿無效"; return false; }
        if (item.BidPrices.Length == 0 || item.AskPrices.Length == 0) { reason = "缺少買賣深度"; return false; }
        if (!IsTickAligned(item.BestBid, tickSize) || !IsTickAligned(item.BestAsk, tickSize)) { reason = "價格不是合法跳動"; return false; }
        if (item.BidQuantities.Any(x => x < 0) || item.AskQuantities.Any(x => x < 0)) { reason = "掛單量為負"; return false; }
        reason = string.Empty;
        return true;
    }

    private void SuspendForData(string reason)
    {
        if (Position().NetQuantity == 0)
        {
            CancelActiveOrders();
            _entryWorking = false;
        }
        State = StrategyLifecycleState.Suspended;
        _reason = reason + "；停止新進場";
        Publish();
    }

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
        if (_orderBook == null) throw new InvalidOperationException("Nikkei Alpha has not initialized its managed order book.");
        var state = new RecoveryState(_latest.ToArray(),
            _mids.Select(x => new KeyValuePair<string, MidSample[]>(x.Key, x.Value.ToArray())).ToArray(),
            _tradeFlows.ToArray(), _firstHealthyAt, _entryRequestedAt, _entryAt, _cooldownUntil, _entrySide,
            _entryWorking, _exitWorking, _alpha, _obi, _aofi, _referenceScore, _expectedMoveTicks, _expectedNetEdge,
            _atr, _entryPrice, _entryOrderPrice, _takeProfitPrice, _stopPrice, _completedTrades, _lastSequence,
            _reason, State, _cycleSequence, _currentCycleId, _orderBook.Capture());
        return new StrategyRecoveryCheckpoint(StrategyType, InstanceId, _strategyRunGeneration, RecoverySchema,
            DateTimeOffset.UtcNow, JsonSerializer.Serialize(state));
    }

    public void RestoreRecoveryCheckpoint(StrategyRecoveryCheckpoint checkpoint)
    {
        StrategyRecoveryCheckpointValidator.EnsureCompatible(checkpoint, StrategyType, InstanceId,
            checkpoint.StrategyRunGeneration, RecoverySchema);
        RecoveryState state = JsonSerializer.Deserialize<RecoveryState>(checkpoint.PayloadJson)
            ?? throw new InvalidOperationException("Nikkei Alpha recovery payload is empty.");
        if (_strategyRunGeneration != 0 && _strategyRunGeneration != checkpoint.StrategyRunGeneration)
            throw new InvalidOperationException("Nikkei Alpha checkpoint run generation conflicts with the runtime.");
        _strategyRunGeneration = checkpoint.StrategyRunGeneration;
        _orderBook ??= new ManagedStrategyOrderBook(InstanceId, _strategyRunGeneration);
        _orderBook.Restore(state.OrderBook);
        _latest.Clear(); foreach (var item in state.Latest) _latest[item.Key] = item.Value;
        _mids.Clear(); foreach (var item in state.Mids) _mids[item.Key] = new Queue<MidSample>(item.Value);
        _tradeFlows.Clear(); foreach (TradeFlow flow in state.TradeFlows) _tradeFlows.Enqueue(flow);
        _firstHealthyAt = state.FirstHealthyAt; _entryRequestedAt = state.EntryRequestedAt;
        _entryAt = state.EntryAt; _cooldownUntil = state.CooldownUntil; _entrySide = state.EntrySide;
        _entryWorking = state.EntryWorking; _exitWorking = state.ExitWorking;
        _alpha = state.Alpha; _obi = state.Obi; _aofi = state.Aofi; _referenceScore = state.ReferenceScore;
        _expectedMoveTicks = state.ExpectedMoveTicks; _expectedNetEdge = state.ExpectedNetEdge; _atr = state.Atr;
        _entryPrice = state.EntryPrice; _entryOrderPrice = state.EntryOrderPrice;
        _takeProfitPrice = state.TakeProfitPrice; _stopPrice = state.StopPrice;
        _completedTrades = state.CompletedTrades; _lastSequence = state.LastSequence;
        _reason = state.Reason; State = state.State; _cycleSequence = state.CycleSequence;
        _currentCycleId = state.CurrentCycleId;
        _restoredBeforeStart = _context == null;
    }

    private void CancelActiveOrders()
    {
        if (_orderBook == null) return;
        foreach (string clientOrderId in _orderBook.ActiveClientOrderIds)
            CancelOrderRequested?.Invoke(clientOrderId);
    }

    private bool WarmupComplete(DateTimeOffset now)
    {
        if (_context == null) return false;
        bool allPresent = _context.Universe.AllProducts.All(x => _latest.ContainsKey(x.Symbol));
        if (!allPresent) { _firstHealthyAt = null; return false; }
        _firstHealthyAt ??= now;
        return (now - _firstHealthyAt.Value).TotalSeconds >= _warmupSeconds && _tradeFlows.Count >= 2;
    }

    private string FactorReason(string prefix) =>
        $"{prefix}：Alpha={_alpha:F2}, OBI={_obi:F2}, AOFI={_aofi:F2}, Ref={_referenceScore:F2}, Move={_expectedMoveTicks:F1}t, Edge=¥{_expectedNetEdge:F0}";
    private bool IsTradable(string symbol) => _context != null && string.Equals(symbol, _context.Universe.Tradable.Symbol, StringComparison.OrdinalIgnoreCase);
    private PositionSnapshot Position() => _context == null ? new PositionSnapshot() : _context.Performance.GetPosition(InstanceId, _context.Universe.Tradable.Symbol);
    private decimal TickSize() => Math.Max(0.00001m, _context?.Universe.Tradable.TickSize ?? 5m);
    private decimal Multiplier() => Math.Max(0.00001m, _context?.Universe.Tradable.ContractMultiplier ?? 10m);
    private decimal RoundDown(decimal price) => Math.Floor(price / TickSize()) * TickSize();
    private decimal RoundUp(decimal price) => Math.Ceiling(price / TickSize()) * TickSize();
    private decimal RoundNearest(decimal price) => Math.Round(price / TickSize(), MidpointRounding.AwayFromZero) * TickSize();
    private static bool IsTickAligned(decimal price, decimal tick) => tick > 0m && price % tick == 0m;
    private void Publish() => SnapshotChanged?.Invoke(GetSnapshot());

    private int Int(string key, int fallback, int minimum, int maximum) =>
        _definition.Parameters.TryGetValue(key, out string? text) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? Math.Clamp(value, minimum, maximum) : fallback;
    private decimal Decimal(string key, decimal fallback, decimal minimum, decimal maximum) =>
        _definition.Parameters.TryGetValue(key, out string? text) && decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value)
            ? Math.Clamp(value, minimum, maximum) : fallback;
}
