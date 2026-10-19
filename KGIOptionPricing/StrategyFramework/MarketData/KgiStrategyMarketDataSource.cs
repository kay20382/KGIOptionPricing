using System;
using System.Collections.Generic;
using System.Linq;

namespace KGIOptionPricing.StrategyFramework.MarketData;

public interface IStrategyQuoteFeed
{
    long ConnectionGeneration { get; }
    event Action<long>? OnConnectionGenerationChanged;
    event Action<string,string,KGIQuoteService.MarketDataStruct>? OnResearchMarketEvent;
    void Subscribe(string symbol);
}

public sealed class StrategyProductResolver
{
    public ResolvedProduct Resolve(ProductSelection selection, DateTime? tradingDate = null)
    {
        ContractInfo? contract = !string.IsNullOrWhiteSpace(selection.Symbol)
            ? SymbolRepository.Instance.GetContract(selection.Symbol!)
            : SymbolRepository.Instance.GetHotMonthContract(selection.ProductId, tradingDate);
        if (contract == null || string.IsNullOrWhiteSpace(contract.Symbol))
            throw new InvalidOperationException($"找不到商品 {selection.ProductId} 的可用合約。");

        decimal resolvedTick = StrategyProductSpecifications.ResolveTickSize(selection.ProductId, contract.TickSize);
        return new ResolvedProduct
        {
            ProductId = contract.ProductId,
            Symbol = contract.Symbol,
            ContractMonth = !string.IsNullOrWhiteSpace(contract.ComYM2) ? contract.ComYM2 :
                !string.IsNullOrWhiteSpace(contract.ComYM) ? contract.ComYM : contract.Month,
            TickType = contract.TickType,
            TickSize = resolvedTick,
            ContractMultiplier = StrategyProductSpecifications.ResolveContractMultiplier(selection.ProductId, contract.ContractValue)
        };
    }

    public ResolvedStrategyUniverse Resolve(StrategyUniverse universe, DateTime? tradingDate = null) => new()
    {
        Tradable = Resolve(universe.Tradable, tradingDate),
        References = universe.References.Select(x => Resolve(x, tradingDate)).ToArray()
    };
}

/// <summary>Read-only adapter from KGI quote events to immutable strategy market events.</summary>
public sealed class KgiStrategyMarketDataSource : IMarketEventSource
{
    private readonly IStrategyQuoteFeed _quote;
    private readonly Dictionary<string, ResolvedProduct> _products = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();
    private long _sequence;
    private readonly CausalBookTracker _causal = new();
    private bool _disposed;

    public event Action<MarketEvent>? MarketEventReceived;

    public KgiStrategyMarketDataSource(IStrategyQuoteFeed quote)
    {
        _quote = quote;
        _quote.OnResearchMarketEvent += OnResearchMarketEvent;
        _quote.OnConnectionGenerationChanged += OnGenerationChanged;
    }

    public void Subscribe(IEnumerable<ResolvedProduct> products)
    {
        foreach (ResolvedProduct product in products)
        {
            bool added;
            lock (_sync) added = _products.TryAdd(product.Symbol, product);
            if (added) _quote.Subscribe(product.Symbol);
        }
    }

    private void OnResearchMarketEvent(string symbol, string kind, KGIQuoteService.MarketDataStruct data)
    {
        lock (_sync)
        {
        ResolvedProduct? product;
        lock (_sync) _products.TryGetValue(symbol, out product);
        if (product == null || _disposed || (kind != "trade" && !kind.StartsWith("depth", StringComparison.Ordinal))) return;

        var marketEvent = new MarketEvent
        {
            Sequence = System.Threading.Interlocked.Increment(ref _sequence),
            ConnectionGeneration = _quote.ConnectionGeneration,
            Timestamp = DateTimeOffset.Now,
            Symbol = symbol,
            ProductId = product.ProductId,
            Kind = kind == "trade" ? MarketEventKind.Trade : MarketEventKind.Book,
            LastPrice = data.MatchcPx > 0m ? data.MatchcPx : data.LastPx,
            TradeQuantity = kind == "trade" ? data.MatchQty : 0,
            SourceTradeTime = kind == "trade" ? data.SourceTradeTime : null,
            BidPrices = data.BuyPx?.ToArray() ?? Array.Empty<decimal>(),
            BidQuantities = data.BuyQty?.ToArray() ?? Array.Empty<int>(),
            AskPrices = data.SellPx?.ToArray() ?? Array.Empty<decimal>(),
            AskQuantities = data.SellQty?.ToArray() ?? Array.Empty<int>()
        };
        lock (_sync) MarketEventReceived?.Invoke(_causal.Observe(marketEvent));
        }
    }

    private void OnGenerationChanged(long generation)
    {
        lock (_sync)
        {
            if(_disposed)return;
            foreach(var product in _products.Values)
                MarketEventReceived?.Invoke(_causal.Observe(new MarketEvent{Kind=MarketEventKind.Connection,
                    Symbol=product.Symbol,ProductId=product.ProductId,ConnectionGeneration=generation,
                    Sequence=++_sequence,Timestamp=DateTimeOffset.Now}));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _quote.OnResearchMarketEvent -= OnResearchMarketEvent;
        _quote.OnConnectionGenerationChanged -= OnGenerationChanged;
    }
}
