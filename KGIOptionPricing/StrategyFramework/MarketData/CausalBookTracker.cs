namespace KGIOptionPricing.StrategyFramework.MarketData;

/// <summary>Local receive order provenance. This does not claim exchange sequencing.</summary>
public sealed class CausalBookTracker
{
    private readonly Dictionary<string,BookReference> _books = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string,(DateTimeOffset At,long Sequence)> _last = new(StringComparer.OrdinalIgnoreCase);
    private long? _generation;
    public MarketEvent Observe(MarketEvent e)
    {
        if (_generation != e.ConnectionGeneration || e.Kind == MarketEventKind.Connection)
        { _books.Clear(); _last.Clear(); _generation=e.ConnectionGeneration; }
        if (_last.TryGetValue(e.Symbol,out var last) && (e.Timestamp < last.At || (e.Sequence > 0 && last.Sequence > 0 && e.Sequence <= last.Sequence)))
            return e with { PriorBook=null, ProvenanceWarning="DuplicateOrOutOfOrder" };
        _last[e.Symbol]=(e.Timestamp,e.Sequence);
        _books.TryGetValue(e.Symbol,out var prior);
        if (e.Kind==MarketEventKind.Book)
        {
            // An invalid newer book invalidates the previous quote immediately.
            _books.Remove(e.Symbol);
            if (e.BestBid>0 && e.BestAsk>e.BestBid)
                _books[e.Symbol]=new(e.Symbol,e.Timestamp,e.Sequence,e.ConnectionGeneration,e.BestBid,e.BestAsk,
                    e.BidQuantities.FirstOrDefault(),e.AskQuantities.FirstOrDefault());
        }
        return e with { PriorBook=prior, ProvenanceWarning=e.Kind==MarketEventKind.Trade && prior is null ? "MissingPriorBook" : null };
    }
}
