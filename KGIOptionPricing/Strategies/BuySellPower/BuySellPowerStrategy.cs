using System.Globalization;
using System.IO;
using System.Text.Json;
using KGIOptionPricing.StrategyFramework;

namespace KGIOptionPricing.Strategies.BuySellPower;

public sealed class BuySellPowerFactory : IStrategyFactory
{
    public const string TypeName = "BuySellPower";
    public string StrategyType => TypeName;
    public string DisplayName => "買賣力道（指標觀測）";
    public ISimulationStrategy Create(StrategyDefinition definition) => new BuySellPowerStrategy(definition);
}

public sealed class BuySellPowerStrategy : ISimulationStrategy, IStrategyHeartbeat, IStrategyScheduledPulseSource
{
    public static readonly string[] Products = { "225", "225M", "225MC" };
    private sealed record Session(string Id, DateTimeOffset End);
    private sealed class ProductState(ResolvedProduct product, PowerParameters parameters)
    {
        public ResolvedProduct Product { get; } = product;
        public PowerParameters Parameters { get; } = parameters;
        public PowerIndicator Core { get; set; } = new(parameters);
        public Session? Session { get; set; }
        public long Sequence { get; set; }
        public long Skipped { get; set; }
        public string TimeSource { get; set; } = "接收時間";
        public MovementSegment? LastSerializedSegment { get; set; }
        public string SerializedHistory { get; set; } = "[]";
    }
    private readonly object _sync = new();
    private readonly StrategyDefinition _definition;
    private readonly Dictionary<string, PowerParameters> _parameters;
    private readonly Dictionary<string, ProductState> _products = new(StringComparer.OrdinalIgnoreCase);
    private PowerHistoryWriter? _history;
    private DateTimeOffset? _now, _publishedAt;
    private long? _generation;
    private string _reason = "尚無資料";
    public string StrategyType => BuySellPowerFactory.TypeName;
    public string InstanceId => _definition.InstanceId;
    public StrategyLifecycleState State { get; private set; } = StrategyLifecycleState.Created;
    // Indicator-only by construction: there is no order emitter or stored order delegate.
    public event Action<PaperOrderRequest>? OrderRequested { add { } remove { } }
    public event Action? CancelAllRequested { add { } remove { } }
    public event Action<StrategySnapshot>? SnapshotChanged;
    public BuySellPowerStrategy(StrategyDefinition definition)
    {
        _definition = definition;
        _parameters = Products.ToDictionary(p => p, p => PowerParameters.Parse(definition.Parameters, p));
    }

    public void Start(StrategyRuntimeContext context)
    {
        lock (_sync)
        {
            if (State != StrategyLifecycleState.Created) throw new InvalidOperationException("此觀測實例已啟動。");
            foreach (string product in Products)
            {
                var resolved = context.Universe.AllProducts.SingleOrDefault(p => string.Equals(p.ProductId, product, StringComparison.OrdinalIgnoreCase))
                    ?? throw new ArgumentException("缺少觀測商品 " + product);
                _products[resolved.Symbol] = new(resolved, _parameters[product]);
            }
            string root = _definition.Parameters.TryGetValue("HistoryRoot", out var custom) ? custom :
                Path.Combine(AppContext.BaseDirectory, "買賣力道歷史", Safe(InstanceId) + "-" + Guid.NewGuid().ToString("N"));
            _history = new(root);
            foreach (var state in _products.Values) AttachHistory(state);
            State = StrategyLifecycleState.WarmingUp; Publish(force: true);
        }
    }

    public void OnMarketEvent(MarketEvent e)
    {
        lock (_sync)
        {
            if (State is StrategyLifecycleState.Created or StrategyLifecycleState.Stopped or StrategyLifecycleState.Suspended) return;
            if (!_products.TryGetValue(e.Symbol, out var state)) return;
            if ((_now.HasValue && e.Timestamp < _now.Value) || (_generation.HasValue && e.ConnectionGeneration < _generation))
            { state.Skipped++; return; }
            if (_generation.HasValue && _generation != e.ConnectionGeneration)
                foreach (var s in _products.Values)
                { Reset(s, _now ?? e.Timestamp, "行情連線世代變更"); s.Sequence = 0; }
            _generation = e.ConnectionGeneration;
            if ((_now.HasValue && e.Timestamp < _now.Value) ||
                (e.Sequence > 0 && e.Sequence <= state.Sequence) || e.ProvenanceWarning == "DuplicateOrOutOfOrder")
            { state.Skipped++; return; }
            if (e.Sequence > 0) state.Sequence = e.Sequence;
            Advance(e.Timestamp);
            if (e.Kind == MarketEventKind.Connection)
            {
                foreach (var s in _products.Values) Reset(s, e.Timestamp, "行情連線重置");
            }
            else if (e.Kind == MarketEventKind.Trade && state.Session != null)
            {
                // Timestamp is the reproducible receive timeline in existing live/archive data.
                // Raw exchange time-of-day has no verified timezone/date and is diagnostic only.
                state.TimeSource = e.SourceTradeTime == null ? "接收時間（無來源成交時間）" : "接收時間；來源成交時刻 " + e.SourceTradeTime;
                if (!state.Core.OnTrade(e.Timestamp, e.LastPrice, e.TradeQuantity, e.BestBid, e.BestAsk)) state.Skipped++;
            }
            Publish();
        }
    }

    public void OnHeartbeat(DateTimeOffset now)
    {
        lock (_sync)
        {
            if (State is StrategyLifecycleState.Created or StrategyLifecycleState.Stopped or StrategyLifecycleState.Suspended) return;
            if (_now.HasValue && now < _now) return;
            Advance(now); Publish();
        }
    }
    private void Advance(DateTimeOffset at)
    {
        _now = at;
        foreach (var state in _products.Values)
        {
            Session? session = ResolveSession(at);
            if (state.Session?.Id != session?.Id)
            {
                Reset(state, state.Session?.End ?? at, "跨交易時段"); state.Session = session;
            }
            state.Core.Advance(at);
        }
    }
    private void Reset(ProductState state, DateTimeOffset at, string reason)
    {
        state.Core.Movement.EndIncomplete(at, reason);
        state.Core = new(state.Parameters); AttachHistory(state);
    }
    private void AttachHistory(ProductState state) => state.Core.Movement.SegmentEnded += segment =>
    {
        if (state.Session != null) _history?.Append(new(state.Product.ProductId, state.Product.Symbol, state.Session.Id,
            segment, state.Parameters, InstanceId));
    };
    private static Session? ResolveSession(DateTimeOffset at)
    {
        var local = at.ToOffset(TimeSpan.FromHours(8)); var day = local.Date; var time = local.TimeOfDay;
        if (time >= new TimeSpan(7,45,0) && time < new TimeSpan(14,45,0))
            return new(day.ToString("yyyy-MM-dd") + "/日盤", new(day.AddHours(14).AddMinutes(45), TimeSpan.FromHours(8)));
        if (time >= new TimeSpan(16,0,0))
            return new(day.ToString("yyyy-MM-dd") + "/夜盤", new(day.AddDays(1).AddHours(5), TimeSpan.FromHours(8)));
        if (time < new TimeSpan(5,0,0))
            return new(day.AddDays(-1).ToString("yyyy-MM-dd") + "/夜盤", new(day.AddHours(5), TimeSpan.FromHours(8)));
        return null;
    }
    public IReadOnlyList<DateTimeOffset> GetPulseTimes(DateTimeOffset exclusiveStart, DateTimeOffset inclusiveEnd)
    {
        // Core integrates expirations exactly; only session boundaries need replay pulses.
        var times = new List<DateTimeOffset>();
        var day = exclusiveStart.ToOffset(TimeSpan.FromHours(8)).Date;
        var end = inclusiveEnd.ToOffset(TimeSpan.FromHours(8)).Date;
        for (; day <= end; day = day.AddDays(1))
            foreach (TimeSpan time in new[] {new TimeSpan(5,0,0),new TimeSpan(7,45,0),new TimeSpan(14,45,0),new TimeSpan(16,0,0)})
            {
                var at = new DateTimeOffset(day + time, TimeSpan.FromHours(8));
                if (at > exclusiveStart && at <= inclusiveEnd) times.Add(at);
            }
        return times;
    }
    public void Pause(string reason)
    {
        lock (_sync)
        {
            foreach (var s in _products.Values) Reset(s, _now ?? DateTimeOffset.Now, "暫停觀測");
            State = StrategyLifecycleState.Suspended; _reason = reason; Publish(true);
        }
    }
    public void Resume()
    { lock (_sync) { if (State != StrategyLifecycleState.Suspended) return; State = StrategyLifecycleState.WarmingUp; _reason = "重新暖機"; Publish(true); } }
    public void Stop(string reason)
    {
        lock (_sync)
        {
            if (State == StrategyLifecycleState.Stopped) return;
            foreach (var s in _products.Values) s.Core.Movement.EndIncomplete(_now ?? DateTimeOffset.Now, reason);
            _history?.Dispose(); State = StrategyLifecycleState.Stopped; _reason = reason; Publish(true);
        }
    }
    public void OnExecution(ExecutionEvent execution) { }
    private void Publish(bool force = false)
    {
        if (!force && _publishedAt.HasValue && _now < _publishedAt.Value.AddMilliseconds(250)) return;
        if (State is StrategyLifecycleState.WarmingUp or StrategyLifecycleState.Running)
        {
            State = _products.Values.All(s => s.Core.Snapshot().IsWarm) ? StrategyLifecycleState.Running : StrategyLifecycleState.WarmingUp;
            _reason = "指標觀測／不產生委託";
        }
        _publishedAt = _now; SnapshotChanged?.Invoke(GetSnapshot());
    }
    public StrategySnapshot GetSnapshot()
    {
        lock (_sync)
        {
            var features = new Dictionary<string, decimal?>(); var diagnostics = new Dictionary<string, string>();
            foreach (var state in _products.Values)
            {
                string p = state.Product.ProductId + "."; var s = state.Core.Snapshot(); var m = s.CurrentMovement;
                features[p+"UpQuantity"] = s.UpQuantity; features[p+"DownQuantity"] = s.DownQuantity;
                features[p+"ZeroQuantity"] = s.ZeroQuantity; features[p+"BuyPower"] = s.BuyPower;
                features[p+"UpPercent"] = s.UpPercent; features[p+"DownPercent"] = s.DownPercent; features[p+"ZeroPercent"] = s.ZeroPercent;
                features[p+"TradeCount"] = state.Core.TradeCount;
                diagnostics[p+"PowerUnits"] = "百分比；分母為視窗有效總口數；空視窗平均以0積分";
                features[p+"ShortAverage"] = s.ShortAverage; features[p+"LongAverage"] = s.LongAverage; features[p+"BaisPower"] = s.BaisPower;
                features[p+"AverageUpPoints"] = s.AverageUpPoints; features[p+"AverageDownPoints"] = s.AverageDownPoints;
                features[p+"UpSamples"] = s.UpSamples; features[p+"DownSamples"] = s.DownSamples;
                features[p+"StartPrice"] = m?.StartPrice; features[p+"ExtremePrice"] = m?.ExtremePrice; features[p+"Points"] = m?.Points;
                features[p+"DurationSeconds"] = m == null || _now == null ? null : (decimal)(_now.Value-m.StartTime).TotalSeconds;
                features[p+"Skipped"] = state.Skipped;
                diagnostics[p+"Status"] = s.Status; diagnostics[p+"Direction"] = m?.Direction ?? "尚無資料";
                diagnostics[p+"Session"] = state.Session?.Id ?? "非交易時段"; diagnostics[p+"Symbol"] = state.Product.Symbol;
                // Completed segments are immutable. Reuse identical history JSON until
                // the last segment changes; a reset to empty also invalidates this cache.
                var lastSegment=s.History.LastOrDefault();
                if(!ReferenceEquals(lastSegment,state.LastSerializedSegment))
                {state.SerializedHistory=JsonSerializer.Serialize(s.History);state.LastSerializedSegment=lastSegment;}
                diagnostics[p+"TimeSource"] = state.TimeSource; diagnostics[p+"History"] = state.SerializedHistory;
            }
            diagnostics["SaveError"] = _history?.Error ?? ""; diagnostics["HistoryRoot"] = _history?.Root ?? "";
            diagnostics["BaisPowerUnits"] = "百分點；短期平均減長期平均";
            diagnostics["PendingHistory"] = (_history?.Pending ?? 0).ToString(CultureInfo.InvariantCulture);
            return new() { StrategyType = StrategyType, InstanceId = InstanceId, State = State, Reason = _reason,
                TradableProductId = "225MC", TradableSymbol = _products.Values.FirstOrDefault(s => s.Product.ProductId == "225MC")?.Product.Symbol ?? "",
                ReferenceSummary = "225、225M（各自獨立觀測）", ParameterSummary = string.Join(";", _parameters.SelectMany(p=>p.Value.ToDictionary(p.Key+".")).Select(p=>p.Key+"="+p.Value)),
                Features = features, Diagnostics = diagnostics, UpdatedAt = _now ?? default };
        }
    }
    private static string Safe(string value) => string.Concat(value.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
    public PowerSnapshot GetPowerSnapshot(string productId)
    { lock(_sync) return _products.Values.Single(s=>s.Product.ProductId==productId).Core.Snapshot(); }
    internal string GetPowerSession(string productId)
    { lock(_sync) return _products.Values.Single(s=>s.Product.ProductId==productId).Session?.Id ?? "非交易時段"; }
    internal int GetPowerTradeCount(string productId)
    { lock(_sync) return _products.Values.Single(s=>s.Product.ProductId==productId).Core.TradeCount; }
}
