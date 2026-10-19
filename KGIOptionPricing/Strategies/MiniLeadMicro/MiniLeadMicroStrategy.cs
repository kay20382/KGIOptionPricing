using System.Globalization;
using KGIOptionPricing.OrderManagement.Core;
using KGIOptionPricing.StrategyFramework;

namespace KGIOptionPricing.Strategies.MiniLeadMicro;

public sealed class MiniLeadMicroFactory : IStrategyFactory
{
    public const string TypeName = "MiniLeadMicro";
    public string StrategyType => TypeName;
    public string DisplayName => "OSE Mini 領先 Micro 延遲反應";
    public ISimulationStrategy Create(StrategyDefinition definition) => new MiniLeadMicroStrategy(definition);
}

public enum MiniLeadMode { DelayedResponse, ExecutableGap }
internal enum LargeConfirmationState { Neutral, ConfirmedUp, ConfirmedDown, Divergent, Stale }

/// <summary>
/// Trades 225MC only after a directional 225M move is confirmed by 225.  All
/// calculations use top-of-book prices in canonical five-point ticks.  Expected
/// move is the transparent unfilled part of the two leaders' common move; it is
/// intentionally not a fitted or look-ahead model.
/// </summary>
public sealed class MiniLeadMicroStrategy : ISimulationStrategy, IManagedOrderStrategy,
    IManagedOrderSubmissionObserver
{
    private sealed record Sample(DateTimeOffset Time, decimal Mid);
    private readonly StrategyDefinition _definition;
    private readonly Dictionary<string, MarketEvent> _books = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<Sample> _mini = new();
    private readonly Queue<Sample> _large = new();
    private readonly Queue<Sample> _micro = new();
    private readonly Queue<DateTimeOffset> _miniTrades = new();
    private readonly MiniLeadMode _mode;
    private readonly int _windowMs, _largeWindowMs, _largeTimeoutMs, _maxQuoteAgeMs, _maxLargeQuoteAgeMs;
    private readonly int _maxSkewMs, _warmupSeconds, _profitTicks, _stopTicks, _leaderReversalTicks;
    private readonly int _largeReversalTicks, _emergencyDispersionTicks, _maxHoldingMs, _cooldownMs;
    private readonly int _maxTrades, _maxConsecutiveLosses;
    private readonly decimal _leaderThreshold, _minPathEfficiency, _minTradeIntensity, _largeConfirmTicks;
    private readonly decimal _minLargePathEfficiency, _maxDispersionTicks, _maxCatchUpRatio;
    private readonly decimal _minGapTicks, _minExpectedMoveTicks, _minExpectedNetYen, _minWinProbability;
    private readonly decimal _maxMicroSpreadTicks, _commissionPerSideYen, _yenPerPoint;
    private StrategyRuntimeContext? _context;
    private DateTimeOffset? _firstValidBookAt, _signalCrossedAt;
    private DateTimeOffset _entryAt, _cooldownUntil;
    private decimal _entryPrice, _stopPrice, _miniExtreme, _largeExtreme;
    private decimal _leaderMove, _largeMove, _followerMove, _pathEfficiency, _largeEfficiency;
    private decimal _tradeIntensity, _catchUpRatio, _executableGap, _expectedMove;
    private string _reason = "尚未啟動";
    private int _signalDirection, _completedTrades, _consecutiveLosses;
    private bool _entryWorking, _exitWorking;
    private bool _paused;
    private ManagedStrategyOrderBook? _orderBook;
    private long _strategyRunGeneration;
    private int _cycleSequence;
    private string _currentCycleId = string.Empty;

    public string StrategyType => MiniLeadMicroFactory.TypeName;
    public string InstanceId => _definition.InstanceId;
    public StrategyLifecycleState State { get; private set; } = StrategyLifecycleState.Created;
    public event Action<PaperOrderRequest>? OrderRequested;
    public event Action? CancelAllRequested;
    public event Action<StrategySnapshot>? SnapshotChanged;
    public event Action<string>? CancelOrderRequested;

    public MiniLeadMicroStrategy(StrategyDefinition definition)
    {
        _definition = definition;
        _mode = Enum.TryParse(Text("StrategyMode", "DelayedResponse"), true, out MiniLeadMode mode) ? mode : MiniLeadMode.DelayedResponse;
        _windowMs = Int("WindowMs", 1000, 50, 60000);
        _leaderThreshold = Decimal("LeaderMoveTicks", 3m, .5m, 100m);
        _minPathEfficiency = Decimal("MinPathEfficiency", .65m, 0m, 1m);
        _minTradeIntensity = Decimal("MinTradeIntensity", 2.5m, 0m, 100m);
        _largeWindowMs = Int("LargeWindowMs", 1500, 50, 60000);
        _largeConfirmTicks = Decimal("LargeConfirmMoveTicks", 2m, .5m, 100m);
        _minLargePathEfficiency = Decimal("MinLargePathEfficiency", .50m, 0m, 1m);
        _largeTimeoutMs = Int("LargeConfirmationTimeoutMs", 500, 0, 10000);
        _maxDispersionTicks = Decimal("MaxLargeMiniDispersionTicks", 2m, 0m, 100m);
        _maxCatchUpRatio = Decimal("MaxCatchUpRatio", .50m, -2m, 2m);
        _minGapTicks = Decimal("MinExecutableGapTicks", 1m, 0m, 100m);
        _minExpectedMoveTicks = Decimal("MinExpectedMoveTicks", 4m, 0m, 100m);
        _minExpectedNetYen = Decimal("MinExpectedNetValueYen", 50m, -100000m, 100000m);
        _minWinProbability = Decimal("MinWinProbability", .55m, 0m, 1m);
        _maxQuoteAgeMs = Int("MaxQuoteAgeMs", 250, 1, 60000);
        _maxLargeQuoteAgeMs = Int("MaxLargeQuoteAgeMs", 250, 1, 60000);
        _maxSkewMs = Int("MaxCrossSymbolSkewMs", 100, 0, 60000);
        _maxMicroSpreadTicks = Decimal("MaxMicroSpreadTicks", 1m, .5m, 100m);
        _warmupSeconds = Int("WarmupSeconds", 300, 1, 86400);
        _profitTicks = Int("ProfitTargetTicks", 5, 1, 1000);
        _stopTicks = Int("StopLossTicks", 2, 1, 1000);
        _leaderReversalTicks = Int("LeaderReversalTicks", 2, 1, 1000);
        _largeReversalTicks = Int("LargeReversalTicks", 2, 1, 1000);
        _emergencyDispersionTicks = Int("EmergencyLeaderDispersionTicks", 4, 1, 1000);
        _maxHoldingMs = Int("MaxHoldingMs", 5000, 100, 3600000);
        _cooldownMs = Int("CooldownMs", 10000, 0, 3600000);
        _maxTrades = Int("MaxTradesPerSession", 20, 1, 10000);
        _maxConsecutiveLosses = Int("MaxConsecutiveLosses", 3, 1, 1000);
        _commissionPerSideYen = Decimal("CommissionPerSideYen", 36m, 0m, 100000m);
        _yenPerPoint = Decimal("MicroYenPerPoint", 10m, .01m, 100000m);
    }

    public void Start(StrategyRuntimeContext context)
    {
        if (State != StrategyLifecycleState.Created) throw new InvalidOperationException("策略已啟動。");
        if (!string.Equals(context.Universe.Tradable.ProductId, "225MC", StringComparison.OrdinalIgnoreCase) ||
            !context.Universe.References.Any(x => string.Equals(x.ProductId, "225M", StringComparison.OrdinalIgnoreCase)) ||
            !context.Universe.References.Any(x => string.Equals(x.ProductId, "225", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Mini 領先策略需要交易 225MC，並以 225M、225 為參考商品。");
        _context = context;
        _strategyRunGeneration = context.StrategyRunGeneration;
        _orderBook = new ManagedStrategyOrderBook(InstanceId, _strategyRunGeneration);
        State = StrategyLifecycleState.WarmingUp;
        _reason = $"等待三商品有效第一檔並暖機 {_warmupSeconds} 秒";
        Publish();
    }

    public void OnMarketEvent(MarketEvent marketEvent)
    {
        if (_context == null || State is StrategyLifecycleState.Stopped or StrategyLifecycleState.Faulted) return;
        if (!IsRequired(marketEvent.Symbol)) return;
        if (marketEvent.BestBid > 0m && marketEvent.BestAsk > marketEvent.BestBid)
        {
            _books[marketEvent.Symbol] = marketEvent;
            _firstValidBookAt ??= marketEvent.Timestamp;
            QueueFor(marketEvent.Symbol)?.Enqueue(new Sample(marketEvent.Timestamp, marketEvent.MidPrice));
        }
        if (IsProduct(marketEvent.Symbol, "225M") && marketEvent.Kind == MarketEventKind.Trade && marketEvent.LastPrice > 0m)
            _miniTrades.Enqueue(marketEvent.Timestamp);
        Trim(marketEvent.Timestamp);
        if (_paused && Position().NetQuantity == 0) { State = StrategyLifecycleState.Suspended; Publish(); return; }

        PositionSnapshot position = Position();
        if (position.NetQuantity != 0)
        {
            ManagePosition(marketEvent.Timestamp, position.NetQuantity);
            Publish();
            return;
        }
        if (_entryWorking || _exitWorking)
        {
            State = StrategyLifecycleState.Running;
            _reason = _entryWorking ? "進場委託等待成交" : "出場委託等待成交";
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
        if (!TryBuildSignal(marketEvent.Timestamp, out int direction, out string reason))
        {
            State = WarmupComplete(marketEvent.Timestamp) ? StrategyLifecycleState.Ready : StrategyLifecycleState.WarmingUp;
            _reason = reason;
            Publish();
            return;
        }
        SubmitEntry(direction);
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
            _signalDirection = execution.Role == PaperOrderRole.EntryLong ? 1 : -1;
            _stopPrice = _entryPrice - _signalDirection * _stopTicks * TickSize();
            _miniExtreme = CurrentMid("225M");
            _largeExtreme = CurrentMid("225");
            SubmitProtection();
            State = StrategyLifecycleState.Running;
            _reason = $"{(_signalDirection > 0 ? "多" : "空")}單成交 {execution.Price}，保護單已建立";
        }
        else
        {
            CancelActiveOrders();
            _exitWorking = false;
            _completedTrades++;
            decimal gross = _signalDirection * (execution.Price - _entryPrice) * _yenPerPoint - 2m * _commissionPerSideYen;
            _consecutiveLosses = gross < 0m ? _consecutiveLosses + 1 : 0;
            _cooldownUntil = execution.Timestamp.AddMilliseconds(_cooldownMs);
            _entryPrice = _stopPrice = 0m;
            State = StrategyLifecycleState.Ready;
            _reason = execution.Role switch
            {
                PaperOrderRole.TakeProfit => "停利完成，進入冷卻",
                PaperOrderRole.StopLoss => "固定價格停損完成，進入冷卻",
                _ => "訊號／時間風險平倉完成，進入冷卻"
            };
        }
        Publish();
    }

    public void Pause(string reason)
    {
        if (State is StrategyLifecycleState.Stopped or StrategyLifecycleState.Faulted) return;
        _paused = true;
        if (Position().NetQuantity == 0) { CancelActiveOrders(); _entryWorking = false; }
        State = StrategyLifecycleState.Suspended;
        _reason = reason;
        Publish();
    }

    public void Resume()
    {
        if (State != StrategyLifecycleState.Suspended) return;
        _paused = false;
        State = WarmupComplete(_context?.Clock.Now ?? DateTimeOffset.Now) ? StrategyLifecycleState.Ready : StrategyLifecycleState.WarmingUp;
        _reason = State == StrategyLifecycleState.Ready ? "已繼續，等待有效訊號" : "已繼續暖機";
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
            StrategyType = StrategyType, InstanceId = InstanceId,
            ParameterSummary = $"Mode={_mode}; W={_windowMs}ms; Lead={_leaderThreshold}t; Path>={_minPathEfficiency}; TI>={_minTradeIntensity}; LargeW={_largeWindowMs}ms; Large={_largeConfirmTicks}t/{_largeTimeoutMs}ms; LargePath>={_minLargePathEfficiency}; Disp<={_maxDispersionTicks}t; Catch<={_maxCatchUpRatio}; Gap>={_minGapTicks}t; Exp>={_minExpectedMoveTicks}t/¥{_minExpectedNetYen}; Win>={_minWinProbability}; Age={_maxQuoteAgeMs}/{_maxLargeQuoteAgeMs}ms; Skew<={_maxSkewMs}ms; Spread<={_maxMicroSpreadTicks}t; Warm={_warmupSeconds}s; TP/SL={_profitTicks}/{_stopTicks}t; Rev={_leaderReversalTicks}/{_largeReversalTicks}t; EmergencyDisp={_emergencyDispersionTicks}t; Hold={_maxHoldingMs}ms; Cool={_cooldownMs}ms; MaxTrades={_maxTrades}; MaxLosses={_maxConsecutiveLosses}; Fee=¥{_commissionPerSideYen}/side; Value=¥{_yenPerPoint}/point",
            TradableProductId = _context?.Universe.Tradable.ProductId ?? _definition.Universe.Tradable.ProductId,
            TradableSymbol = _context?.Universe.Tradable.Symbol ?? string.Empty,
            ReferenceSummary = $"225M:{CurrentMid("225M")}; 225:{CurrentMid("225")}; lead={_leaderMove:F2}t; large={_largeMove:F2}t; catch={_catchUpRatio:F2}; TI={_tradeIntensity:F2}; exp={_expectedMove:F2}t",
            State = State, Reason = _reason, LastPrice = CurrentMid("225MC"), CenterPrice = CurrentMid("225M"),
            Range = _leaderMove, BuyEntry = _signalDirection > 0 ? CurrentAsk("225MC") : 0m,
            SellEntry = _signalDirection < 0 ? CurrentBid("225MC") : 0m, StopPrice = _stopPrice,
            Position = position.NetQuantity, RealizedPnl = position.RealizedPnl, UnrealizedPnl = position.UnrealizedPnl,
            NetPnl = position.NetPnl, CompletedTrades = _completedTrades,
            UpdatedAt = _context?.Clock.Now ?? DateTimeOffset.Now
        };
    }

    private bool TryBuildSignal(DateTimeOffset now, out int direction, out string reason)
    {
        direction = 0;
        if (!WarmupComplete(now)) { reason = $"暖機中：{WarmupElapsed(now)}/{_warmupSeconds} 秒"; return false; }
        if (_completedTrades >= _maxTrades) { reason = "本時段交易次數已達上限"; return false; }
        if (_consecutiveLosses >= _maxConsecutiveLosses) { reason = "連續虧損次數已達上限"; return false; }
        if (!ValidateBooks(now, out reason)) return false;
        _leaderMove = Move(_mini, _windowMs); _largeMove = Move(_large, _largeWindowMs); _followerMove = Move(_micro, _windowMs);
        _pathEfficiency = Efficiency(_mini, _windowMs); _largeEfficiency = Efficiency(_large, _largeWindowMs);
        direction = _leaderMove >= _leaderThreshold ? 1 : _leaderMove <= -_leaderThreshold ? -1 : 0;
        if (direction == 0) { _signalCrossedAt = null; reason = $"等待 mini 衝擊：{_leaderMove:F2}t"; return false; }
        _signalCrossedAt ??= now;
        if (_pathEfficiency < _minPathEfficiency) { reason = $"mini 路徑效率 {_pathEfficiency:F2} 不足"; return false; }
        decimal expectedTrades = Math.Max(1m, _warmupSeconds == 0 ? 1m : _miniTrades.Count * _windowMs / (_warmupSeconds * 1000m));
        int currentTrades = _miniTrades.Count(x => x >= now.AddMilliseconds(-_windowMs));
        _tradeIntensity = currentTrades / expectedTrades;
        if (_tradeIntensity < _minTradeIntensity) { reason = $"成交強度 {_tradeIntensity:F2} 不足"; return false; }
        LargeConfirmationState largeState = LargeState(now, direction);
        if (largeState != (direction > 0 ? LargeConfirmationState.ConfirmedUp : LargeConfirmationState.ConfirmedDown))
        { reason = $"大型日經未同向確認：{largeState}"; return false; }
        double delay = Math.Max(0d, (now - _signalCrossedAt.Value).TotalMilliseconds);
        if (delay > _largeTimeoutMs) { reason = $"大型日經確認逾時 {delay:F0}ms"; return false; }
        _catchUpRatio = _leaderMove == 0m ? 0m : _followerMove / _leaderMove;
        if (_catchUpRatio < 0m) { reason = "micro 反向移動，視為資料／市場異常"; return false; }
        if (_catchUpRatio > _maxCatchUpRatio) { reason = $"micro 已跟進 {_catchUpRatio:P0}"; return false; }
        _executableGap = direction > 0 ? (CurrentBid("225M") - CurrentAsk("225MC")) / 5m : (CurrentBid("225MC") - CurrentAsk("225M")) / 5m;
        _expectedMove = direction * Math.Max(0m, Math.Min(Math.Abs(_leaderMove), Math.Abs(_largeMove)) - Math.Abs(_followerMove));
        if (_mode == MiniLeadMode.ExecutableGap && _executableGap < _minGapTicks) { reason = $"可成交價差 {_executableGap:F2}t 不足"; return false; }
        if (_mode == MiniLeadMode.DelayedResponse)
        {
            if (direction * _expectedMove < _minExpectedMoveTicks) { reason = $"透明預期空間 {direction * _expectedMove:F2}t 不足"; return false; }
            decimal expectedNet = direction * _expectedMove * 5m * _yenPerPoint - 2m * _commissionPerSideYen;
            decimal breakEven = (_stopTicks * 5m * _yenPerPoint + 2m * _commissionPerSideYen) /
                ((_profitTicks + _stopTicks) * 5m * _yenPerPoint);
            if (expectedNet < _minExpectedNetYen) { reason = $"預期淨值 ¥{expectedNet:F0} 不足"; return false; }
            if (_minWinProbability < breakEven) { reason = $"設定勝率 {_minWinProbability:P1} 低於損益兩平 {breakEven:P1}"; return false; }
        }
        reason = string.Empty;
        return true;
    }

    private LargeConfirmationState LargeState(DateTimeOffset now, int direction)
    {
        MarketEvent largeBook = Book("225");
        if ((now - largeBook.Timestamp).TotalMilliseconds > _maxLargeQuoteAgeMs) return LargeConfirmationState.Stale;
        decimal dispersion = Math.Abs(CurrentMid("225") - CurrentMid("225M")) / 5m;
        if (dispersion > _maxDispersionTicks || Math.Sign(_largeMove) == -direction) return LargeConfirmationState.Divergent;
        if (_largeEfficiency < _minLargePathEfficiency || Math.Abs(_largeMove) < _largeConfirmTicks) return LargeConfirmationState.Neutral;
        return _largeMove > 0m ? LargeConfirmationState.ConfirmedUp : LargeConfirmationState.ConfirmedDown;
    }

    private bool ValidateBooks(DateTimeOffset now, out string reason)
    {
        foreach (string productId in new[] { "225MC", "225M", "225" })
        {
            MarketEvent book = Book(productId);
            if (book.Symbol.Length == 0) { reason = $"等待 {productId} 第一檔"; return false; }
            int ageLimit = productId == "225" ? _maxLargeQuoteAgeMs : _maxQuoteAgeMs;
            if ((now - book.Timestamp).TotalMilliseconds > ageLimit) { reason = $"{productId} 報價過期"; return false; }
        }
        var times = new[] { Book("225MC").Timestamp, Book("225M").Timestamp, Book("225").Timestamp };
        if ((times.Max() - times.Min()).TotalMilliseconds > _maxSkewMs) { reason = "三商品接收時間差過大"; return false; }
        if ((CurrentAsk("225MC") - CurrentBid("225MC")) / TickSize() > _maxMicroSpreadTicks) { reason = "micro 買賣價差過大"; return false; }
        reason = string.Empty; return true;
    }

    private void SubmitEntry(int direction)
    {
        _signalDirection = direction;
        _currentCycleId = $"cycle-{++_cycleSequence}";
        decimal price = direction > 0 ? RoundUp(CurrentAsk("225MC")) : RoundDown(CurrentBid("225MC"));
        EmitOrder(direction > 0 ? BuySell.Buy : BuySell.Sell, PaperOrderType.Limit,
            direction > 0 ? PaperOrderRole.EntryLong : PaperOrderRole.EntryShort, price, 1);
        _entryWorking = true; State = StrategyLifecycleState.Running;
        _reason = $"{(_mode == MiniLeadMode.ExecutableGap ? "價差" : "延遲反應")}訊號，送出{(direction > 0 ? "買" : "賣")}進場 {price}";
    }

    private void SubmitProtection()
    {
        if (_context == null) return;
        decimal target = _entryPrice + _signalDirection * _profitTicks * TickSize();
        BuySell exitSide = _signalDirection > 0 ? BuySell.Sell : BuySell.Buy;
        EmitOrder(exitSide, PaperOrderType.Limit, PaperOrderRole.TakeProfit, target, 1);
        EmitOrder(exitSide, PaperOrderType.Stop, PaperOrderRole.StopLoss, _stopPrice, 1);
        _exitWorking = true;
    }

    private void ManagePosition(DateTimeOffset now, int position)
    {
        State = StrategyLifecycleState.Running;
        if (_exitWorking == false) SubmitProtection();
        decimal miniMid = CurrentMid("225M"), largeMid = CurrentMid("225");
        if (_signalDirection > 0) { _miniExtreme = Math.Max(_miniExtreme, miniMid); _largeExtreme = Math.Max(_largeExtreme, largeMid); }
        else { _miniExtreme = Math.Min(_miniExtreme, miniMid); _largeExtreme = Math.Min(_largeExtreme, largeMid); }
        bool miniReversed = _signalDirection * (_miniExtreme - miniMid) >= _leaderReversalTicks * 5m;
        bool largeReversed = _signalDirection * (_largeExtreme - largeMid) >= _largeReversalTicks * 5m;
        bool dispersed = Math.Abs(largeMid - miniMid) / 5m >= _emergencyDispersionTicks;
        bool timedOut = (now - _entryAt).TotalMilliseconds >= _maxHoldingMs;
        if (!(miniReversed || largeReversed || dispersed || timedOut)) { _reason = "持倉中：固定停利／停損與領先訊號風控有效"; return; }
        CancelActiveOrders();
        decimal price = position > 0 ? RoundDown(CurrentBid("225MC")) : RoundUp(CurrentAsk("225MC"));
        if (price <= 0m) { _reason = "需要風險平倉但 micro 第一檔無效"; return; }
        EmitOrder(position > 0 ? BuySell.Sell : BuySell.Buy, PaperOrderType.Market, PaperOrderRole.Flatten, price, 1);
        _exitWorking = true;
        _reason = timedOut ? "持有時間到期，送出風險平倉" : dispersed ? "領先商品離散過大，送出風險平倉" : "領先訊號反轉，送出風險平倉";
    }

    private void Trim(DateTimeOffset now)
    {
        Trim(_mini, now.AddMilliseconds(-Math.Max(_windowMs, _largeWindowMs) - 1000));
        Trim(_large, now.AddMilliseconds(-_largeWindowMs - 1000));
        Trim(_micro, now.AddMilliseconds(-_windowMs - 1000));
        DateTimeOffset tradeCutoff = now.AddSeconds(-_warmupSeconds);
        while (_miniTrades.Count > 0 && _miniTrades.Peek() < tradeCutoff) _miniTrades.Dequeue();
    }
    private static void Trim(Queue<Sample> queue, DateTimeOffset cutoff) { while (queue.Count > 0 && queue.Peek().Time < cutoff) queue.Dequeue(); }
    private Queue<Sample>? QueueFor(string symbol) => IsProduct(symbol, "225M") ? _mini : IsProduct(symbol, "225") ? _large : IsProduct(symbol, "225MC") ? _micro : null;
    private decimal Move(Queue<Sample> q, int windowMs)
    {
        decimal[] a = Window(q, windowMs);
        return a.Length < 2 ? 0m : (a[^1] - a[0]) / 5m;
    }
    private decimal Efficiency(Queue<Sample> q, int windowMs)
    {
        decimal[] a = Window(q, windowMs);
        if (a.Length < 2) return 0m;
        decimal path = 0m;
        for (int i = 1; i < a.Length; i++) path += Math.Abs(a[i] - a[i - 1]);
        return path == 0m ? 0m : Math.Abs(a[^1] - a[0]) / path;
    }
    private static decimal[] Window(Queue<Sample> q, int windowMs)
    {
        if (q.Count == 0) return Array.Empty<decimal>();
        DateTimeOffset cutoff = q.Last().Time.AddMilliseconds(-windowMs);
        Sample anchor = q.Where(x => x.Time <= cutoff).DefaultIfEmpty(q.Peek()).Last();
        return new[] { anchor }.Concat(q.Where(x => x.Time > anchor.Time)).Select(x => x.Mid).ToArray();
    }
    private bool WarmupComplete(DateTimeOffset now) => _firstValidBookAt.HasValue && (now - _firstValidBookAt.Value).TotalSeconds >= _warmupSeconds && _mini.Count > 1 && _large.Count > 1 && _micro.Count > 1;
    private int WarmupElapsed(DateTimeOffset now) => _firstValidBookAt.HasValue ? Math.Min(_warmupSeconds, Math.Max(0, (int)(now - _firstValidBookAt.Value).TotalSeconds)) : 0;
    private bool IsRequired(string symbol) => _context!.Universe.AllProducts.Any(x => string.Equals(x.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
    private bool IsProduct(string symbol, string productId) => _context!.Universe.AllProducts.Any(x => string.Equals(x.ProductId, productId, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
    private MarketEvent Book(string productId) { if (_context == null) return new(); ResolvedProduct? p = _context.Universe.AllProducts.FirstOrDefault(x => string.Equals(x.ProductId, productId, StringComparison.OrdinalIgnoreCase)); return p != null && _books.TryGetValue(p.Symbol, out MarketEvent? b) ? b : new(); }
    private decimal CurrentMid(string productId) => Book(productId).MidPrice;
    private decimal CurrentBid(string productId) => Book(productId).BestBid;
    private decimal CurrentAsk(string productId) => Book(productId).BestAsk;
    private PositionSnapshot Position() => _context == null ? new() : _context.Performance.GetPosition(InstanceId, _context.Universe.Tradable.Symbol);
    private decimal TickSize() => Math.Max(.00001m, _context?.Universe.Tradable.TickSize ?? 5m);

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
            { State = StrategyLifecycleState.Ready; _reason = "進場委託已撤銷／拒絕"; Publish(); }
        }
        else if (Position().NetQuantity != 0)
        { _exitWorking = false; State = StrategyLifecycleState.Suspended; _reason = "保護／平倉委託已撤銷／拒絕，需人工處理"; Publish(); }
    }


    private void CancelActiveOrders()
    {
        if (_orderBook == null) return;
        foreach (string clientOrderId in _orderBook.ActiveClientOrderIds) CancelOrderRequested?.Invoke(clientOrderId);
    }

    private decimal RoundDown(decimal p) => Math.Floor(p / TickSize()) * TickSize();
    private decimal RoundUp(decimal p) => Math.Ceiling(p / TickSize()) * TickSize();
    private void Publish() => SnapshotChanged?.Invoke(GetSnapshot());
    private string Text(string key, string fallback) => _definition.Parameters.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;
    private int Int(string key, int fallback, int min, int max) => _definition.Parameters.TryGetValue(key, out string? text) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? Math.Clamp(value, min, max) : fallback;
    private decimal Decimal(string key, decimal fallback, decimal min, decimal max) => _definition.Parameters.TryGetValue(key, out string? text) && decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value) ? Math.Clamp(value, min, max) : fallback;
}
