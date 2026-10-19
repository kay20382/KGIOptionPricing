using KGIOptionPricing.StrategyFramework;

namespace KGIOptionPricing.Strategies.PeriodicQueueCycle;

public sealed record MicrostructureAssessment(bool Ready, int Seconds, decimal NormalizedOfi,
    decimal QueueImbalance, decimal WeightedMidBiasTicks, decimal DepthResilience);

// Snapshot OFI proxy: no claim to reconstruct individual adds/cancels/trades.
public sealed class MicrostructureWindow
{
    private sealed record Bucket(decimal Ofi, decimal Depth, decimal MaxBid, decimal MaxAsk);
    private readonly SortedDictionary<long,Bucket> _buckets=new();
    private readonly decimal _tickSize;
    private readonly int _seconds;
    private readonly decimal _freshness;
    private MarketEvent? _previous;

    public MicrostructureWindow(decimal tickSize,int seconds,decimal freshness)
    {
        if(tickSize<=0 || seconds<1 || freshness<=0)throw new ArgumentOutOfRangeException(nameof(seconds));
        _tickSize=tickSize;_seconds=seconds;_freshness=freshness;
    }

    public bool TryAdd(MarketEvent item,DateTimeOffset now)
    {
        if(item.Kind!=MarketEventKind.Book || DirectionWindow.HasMissingTopThreeDepth(item) ||
            item.Timestamp>now || (decimal)(now-item.Timestamp).TotalSeconds>_freshness || item.BestBid>=item.BestAsk)return false;
        for(int i=0;i<3;i++)
            if(item.BidPrices[i]<=0 || item.AskPrices[i]<=0 || item.BidQuantities[i]<=0 || item.AskQuantities[i]<=0 ||
                i>0 && (item.BidPrices[i]>=item.BidPrices[i-1] || item.AskPrices[i]<=item.AskPrices[i-1]))return false;
        if(_previous is not null && (item.Timestamp<_previous.Timestamp || item.Sequence<=_previous.Sequence))return false;
        if(_previous is not null && ((decimal)(item.Timestamp-_previous.Timestamp).TotalSeconds>_freshness ||
            item.ConnectionGeneration!=_previous.ConnectionGeneration)) {_buckets.Clear();_previous=null;}
        decimal ofi=0;
        if(_previous is not null)
        {
            // Cont/Kukanov/Stoikov e_n; equality activates BOTH terms.
            if(item.BestBid>=_previous.BestBid)ofi+=item.BidQuantities[0];
            if(item.BestBid<=_previous.BestBid)ofi-=_previous.BidQuantities[0];
            if(item.BestAsk<=_previous.BestAsk)ofi-=item.AskQuantities[0];
            if(item.BestAsk>=_previous.BestAsk)ofi+=_previous.AskQuantities[0];
        }
        long second=item.Timestamp.UtcTicks/TimeSpan.TicksPerSecond;
        Prune(second);
        decimal bid=item.BidQuantities.Take(3).Sum(),ask=item.AskQuantities.Take(3).Sum();
        var old=_buckets.GetValueOrDefault(second);
        _buckets[second]=new Bucket((old?.Ofi??0)+ofi,(item.BidQuantities[0]+(decimal)item.AskQuantities[0])/2,
            Math.Max(old?.MaxBid??0,bid),Math.Max(old?.MaxAsk??0,ask));
        _previous=item;
        return true;
    }

    public MicrostructureAssessment Evaluate(DateTimeOffset now)
    {
        Prune(now.UtcTicks/TimeSpan.TicksPerSecond);
        if(_previous is null || _buckets.Count==0)return new(false,0,0,0,0,0);
        decimal depth=_buckets.Values.Average(x=>x.Depth);
        decimal qi=(_previous.BidQuantities[0]-(decimal)_previous.AskQuantities[0])/
            (_previous.BidQuantities[0]+(decimal)_previous.AskQuantities[0]);
        decimal resilience=Math.Min(_previous.BidQuantities.Take(3).Sum()/_buckets.Values.Max(x=>x.MaxBid),
            _previous.AskQuantities.Take(3).Sum()/_buckets.Values.Max(x=>x.MaxAsk));
        bool ready=_buckets.Count==_seconds && now>=_previous.Timestamp &&
            (decimal)(now-_previous.Timestamp).TotalSeconds<=_freshness;
        return new(ready,_buckets.Count,_buckets.Values.Sum(x=>x.Ofi)/depth,qi,
            qi*(_previous.BestAsk-_previous.BestBid)/(2*_tickSize),resilience);
    }

    private void Prune(long second)
    {
        while(_buckets.Count>0 && _buckets.Keys.First()<second-_seconds+1)_buckets.Remove(_buckets.Keys.First());
    }
}
