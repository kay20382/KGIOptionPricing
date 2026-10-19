using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;

namespace KGIOptionPricing.ProductSpreads;

public sealed class ProductSpreadService : IDisposable
{
    private readonly KGIQuoteService _quoteService;
    private readonly ProductSpreadCsvSnapshotStore _snapshotStore;
    private readonly Action<string>? _log;
    private readonly Dictionary<string, SpreadRuntime> _runtimes;
    private readonly Channel<SnapshotWorkItem> _snapshotQueue = Channel.CreateUnbounded<SnapshotWorkItem>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly CancellationTokenSource _schedulerCancellation = new();
    private Task? _schedulerTask;
    private Task? _writerTask;
    private int _started;

    public event Action<ProductSpreadQuote>? QuoteUpdated;

    public ProductSpreadService(
        KGIQuoteService quoteService,
        IEnumerable<ProductSpreadDefinition> definitions,
        string configDirectory,
        Action<string>? log = null)
    {
        _quoteService = quoteService;
        _snapshotStore = new ProductSpreadCsvSnapshotStore(configDirectory, log);
        _log = log;
        _runtimes = BuildRuntimes(definitions);
    }

    public static IReadOnlyList<ProductSpreadDefinition> LoadDefinitions(string configPath)
    {
        if (!File.Exists(configPath)) return Array.Empty<ProductSpreadDefinition>();

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(configPath));
        if (!document.RootElement.TryGetProperty("ProductSpreads", out JsonElement spreads) ||
            spreads.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<ProductSpreadDefinition>();
        }

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return JsonSerializer.Deserialize<List<ProductSpreadDefinition>>(spreads.GetRawText(), options) ??
               new List<ProductSpreadDefinition>();
    }

    public IReadOnlyList<ProductSpreadQuote> GetQuotes()
    {
        return _runtimes.Values
            .Select(runtime => runtime.GetQuote())
            .OrderBy(quote => quote.SpreadId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public ProductSpreadQuote? GetQuote(string spreadId)
    {
        return _runtimes.TryGetValue(spreadId, out SpreadRuntime? runtime)
            ? runtime.GetQuote()
            : null;
    }

    public void Start()
    {
        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0) return;

        _quoteService.OnSymbolRepositoryLoaded += ResolveContractsAndSubscribe;
        _quoteService.OnMarketDataReceived += HandleMarketData;
        _writerTask = Task.Run(WriteSnapshotsAsync);
        _schedulerTask = Task.Run(() => ScheduleSnapshotsAsync(_schedulerCancellation.Token));

        if (SymbolRepository.Instance.IsInitialized)
            ResolveContractsAndSubscribe();

        _log?.Invoke($"[價差] 已啟動 {_runtimes.Count} 個商品價差。");
    }

    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _started, 0) == 0) return;

        _quoteService.OnSymbolRepositoryLoaded -= ResolveContractsAndSubscribe;
        _quoteService.OnMarketDataReceived -= HandleMarketData;
        _schedulerCancellation.Cancel();
        if (_schedulerTask != null)
        {
            try { await _schedulerTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }

        _snapshotQueue.Writer.TryComplete();
        if (_writerTask != null) await _writerTask.ConfigureAwait(false);
        _log?.Invoke("[價差] 快照佇列已寫入完成，服務停止。");
    }

    private Dictionary<string, SpreadRuntime> BuildRuntimes(IEnumerable<ProductSpreadDefinition> definitions)
    {
        var result = new Dictionary<string, SpreadRuntime>(StringComparer.OrdinalIgnoreCase);
        foreach (ProductSpreadDefinition definition in definitions)
        {
            IReadOnlyList<string> errors = ProductSpreadCalculator.ValidateDefinition(definition);
            if (errors.Count > 0)
            {
                _log?.Invoke($"[價差設定錯誤] {definition.Id}: {string.Join("；", errors)}");
                continue;
            }

            if (!result.TryAdd(definition.Id, new SpreadRuntime(definition)))
                _log?.Invoke($"[價差設定錯誤] Spread Id 重複：{definition.Id}");
        }
        return result;
    }

    private void ResolveContractsAndSubscribe()
    {
        foreach (SpreadRuntime runtime in _runtimes.Values)
        {
            ProductSpreadDefinition definition = runtime.Definition;
            ContractInfo? legA = SymbolRepository.Instance.GetHotMonthContract(definition.LegA.ProductID);
            ContractInfo? legB = SymbolRepository.Instance.GetHotMonthContract(definition.LegB.ProductID);
            runtime.SetContracts(legA, legB);

            if (legA == null || legB == null)
            {
                string missing = legA == null ? definition.LegA.ProductID : definition.LegB.ProductID;
                _log?.Invoke($"[價差] {definition.Id} 找不到 ProductID={missing} 的熱門月期貨合約。");
                Publish(runtime.Recalculate(_quoteService, DateTime.Now));
                continue;
            }

            _quoteService.Subscribe(legA.Symbol);
            if (!string.Equals(legA.Symbol, legB.Symbol, StringComparison.OrdinalIgnoreCase))
                _quoteService.Subscribe(legB.Symbol);

            _log?.Invoke($"[價差] {definition.Id} 已解析熱門月：A={legA.Symbol}, B={legB.Symbol}");
            Publish(runtime.Recalculate(_quoteService, DateTime.Now));
        }
    }

    private void HandleMarketData(string symbol, KGIQuoteService.MarketDataStruct marketData)
    {
        DateTime now = DateTime.Now;
        foreach (SpreadRuntime runtime in _runtimes.Values)
        {
            if (!runtime.MarkUpdated(symbol, now)) continue;
            Publish(runtime.Recalculate(_quoteService, now));
        }
    }

    private void Publish(ProductSpreadQuote quote)
    {
        QuoteUpdated?.Invoke(quote);
    }

    private async Task ScheduleSnapshotsAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            DateTime now = DateTime.Now;
            foreach (SpreadRuntime runtime in _runtimes.Values)
            {
                if (!runtime.TryTakeSnapshot(now, out ProductSpreadSnapshot snapshot)) continue;
                await _snapshotQueue.Writer
                    .WriteAsync(new SnapshotWorkItem(runtime.Definition, snapshot), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task WriteSnapshotsAsync()
    {
        await foreach (SnapshotWorkItem item in _snapshotQueue.Reader.ReadAllAsync())
        {
            try
            {
                await _snapshotStore.SaveAsync(item.Definition, item.Snapshot).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[價差快照錯誤] {item.Definition.Id}: {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
        _schedulerCancellation.Dispose();
        _snapshotStore.Dispose();
    }

    private sealed class SpreadRuntime
    {
        private readonly object _sync = new();
        private ContractInfo? _legAContract;
        private ContractInfo? _legBContract;
        private DateTime _legAUpdateTime;
        private DateTime _legBUpdateTime;
        private DateTime _nextSnapshotTime;
        private ProductSpreadQuote _quote;

        public ProductSpreadDefinition Definition { get; }

        public SpreadRuntime(ProductSpreadDefinition definition)
        {
            Definition = definition;
            _nextSnapshotTime = DateTime.Now.AddSeconds(definition.SnapshotIntervalSeconds);
            _quote = ProductSpreadCalculator.Calculate(
                definition,
                EmptyLeg(definition.LegA),
                EmptyLeg(definition.LegB),
                DateTime.Now);
        }

        public void SetContracts(ContractInfo? legA, ContractInfo? legB)
        {
            lock (_sync)
            {
                _legAContract = legA;
                _legBContract = legB;
                _legAUpdateTime = default;
                _legBUpdateTime = default;
            }
        }

        public bool MarkUpdated(string symbol, DateTime updateTime)
        {
            lock (_sync)
            {
                bool matched = false;
                if (SymbolEquals(_legAContract?.Symbol, symbol))
                {
                    _legAUpdateTime = updateTime;
                    matched = true;
                }
                if (SymbolEquals(_legBContract?.Symbol, symbol))
                {
                    _legBUpdateTime = updateTime;
                    matched = true;
                }
                return matched;
            }
        }

        public ProductSpreadQuote Recalculate(KGIQuoteService quoteService, DateTime calculationTime)
        {
            lock (_sync)
            {
                SpreadLegQuote legA = BuildLeg(Definition.LegA, _legAContract, _legAUpdateTime, quoteService);
                SpreadLegQuote legB = BuildLeg(Definition.LegB, _legBContract, _legBUpdateTime, quoteService);
                _quote = ProductSpreadCalculator.Calculate(Definition, legA, legB, calculationTime);
                return _quote;
            }
        }

        public ProductSpreadQuote GetQuote()
        {
            lock (_sync) return _quote;
        }

        public bool TryTakeSnapshot(DateTime now, out ProductSpreadSnapshot snapshot)
        {
            lock (_sync)
            {
                snapshot = default!;
                if (!Definition.SnapshotEnabled || now < _nextSnapshotTime) return false;

                TimeSpan interval = TimeSpan.FromSeconds(Definition.SnapshotIntervalSeconds);
                do { _nextSnapshotTime = _nextSnapshotTime.Add(interval); }
                while (_nextSnapshotTime <= now);

                snapshot = new ProductSpreadSnapshot { SnapshotTime = now, Quote = _quote };
                return true;
            }
        }

        private static SpreadLegQuote BuildLeg(
            ProductSpreadLegDefinition definition,
            ContractInfo? contract,
            DateTime updateTime,
            KGIQuoteService quoteService)
        {
            if (contract == null) return EmptyLeg(definition);
            KGIQuoteService.MarketDataStruct? market = quoteService.GetMarketData(contract.Symbol) ??
                                                           quoteService.GetMarketData("TW." + contract.Symbol);
            int decimals = Math.Clamp(Math.Max(contract.PxDecimal, PriceTick.getPrcDigital(contract.ProductId)), 0, 8);
            return new SpreadLegQuote
            {
                ProductID = definition.ProductID,
                Symbol = contract.Symbol,
                ContractMonth = ContractMonth(contract),
                Direction = definition.Direction,
                Quantity = definition.Quantity,
                ContractMultiplier = contract.ContractValue,
                PxDecimal = decimals,
                LastPrice = Positive(market?.MatchcPx) ?? Positive(market?.LastPx),
                BidPrice = market != null && market.BuyPx.Length > 0 ? Positive(market.BuyPx[0]) : null,
                AskPrice = market != null && market.SellPx.Length > 0 ? Positive(market.SellPx[0]) : null,
                BidQuantity = market != null && market.BuyQty.Length > 0 ? Math.Max(0, market.BuyQty[0]) : 0,
                AskQuantity = market != null && market.SellQty.Length > 0 ? Math.Max(0, market.SellQty[0]) : 0,
                UpdateTime = updateTime
            };
        }

        private static SpreadLegQuote EmptyLeg(ProductSpreadLegDefinition definition) => new()
        {
            ProductID = definition.ProductID,
            Direction = definition.Direction,
            Quantity = definition.Quantity
        };

        private static decimal? Positive(decimal? value) => value is > 0 ? value : null;

        private static bool SymbolEquals(string? left, string right)
        {
            if (string.IsNullOrWhiteSpace(left)) return false;
            static string Normalize(string value) => value.StartsWith("TW.", StringComparison.OrdinalIgnoreCase)
                ? value.Substring(3)
                : value;
            return string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
        }

        private static string ContractMonth(ContractInfo contract) =>
            !string.IsNullOrWhiteSpace(contract.ComYM2) ? contract.ComYM2 :
            !string.IsNullOrWhiteSpace(contract.ComYM) ? contract.ComYM :
            contract.Month;
    }

    private sealed record SnapshotWorkItem(ProductSpreadDefinition Definition, ProductSpreadSnapshot Snapshot);
}
