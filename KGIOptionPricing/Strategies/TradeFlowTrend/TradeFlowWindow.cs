namespace KGIOptionPricing.Strategies.TradeFlowTrend;

public sealed class TradeFlowWindow
{
    private readonly int _seconds, _count;
    private readonly DateTimeOffset _started;
    private readonly Queue<ClassifiedTrade> _trades = new();
    private readonly Queue<RiSample> _samples = new();
    private DateTimeOffset _next, _watermark, _lastTrade;
    public TradeFlowWindow(int seconds,int historyCount,DateTimeOffset startedAt)
    {
        if (seconds<=0 || historyCount<=0) throw new ArgumentOutOfRangeException(nameof(seconds));
        _seconds=seconds;_count=historyCount;_started=startedAt;_watermark=startedAt;_lastTrade=startedAt;
        _next=Floor(startedAt,seconds).AddSeconds(seconds);
    }
    public static DateTimeOffset Floor(DateTimeOffset at,int seconds) =>
        DateTimeOffset.FromUnixTimeSeconds(at.ToUnixTimeSeconds()/seconds*seconds).ToOffset(TimeSpan.FromHours(8));
    public void Add(ClassifiedTrade trade)
    {
        if (trade.Quantity<0 || trade.Timestamp<_lastTrade || trade.Timestamp<_watermark) throw new ArgumentException("成交量或順序無效");
        Advance(trade.Timestamp);_lastTrade=trade.Timestamp;
        if (trade.Quantity>0) _trades.Enqueue(trade);
    }
    public IReadOnlyList<RiSample> Advance(DateTimeOffset cutoff)
    {
        if (cutoff<_watermark) throw new ArgumentException("時間不得倒退");
        var closed=new List<RiSample>();
        // Older than all retained history: skip empty gaps without unbounded allocation.
        if (cutoff-_next>TimeSpan.FromSeconds((long)(_count+1)*_seconds) && (_trades.Count==0 || _trades.Last().Timestamp<cutoff.AddSeconds(-(long)(_count+1)*_seconds)))
        { _samples.Clear();_trades.Clear();_next=Floor(cutoff,_seconds).AddSeconds(-(long)(_count-1)*_seconds); }
        while (_next<=cutoff)
        {
            var totals=Totals(_next);var total=totals.U+totals.Z+totals.D;
            var sample=new RiSample(_next,total==0?null:100m*(totals.U-totals.D)/total,_next.AddSeconds(-_seconds)<_started);
            _samples.Enqueue(sample); if (_samples.Count>_count) _samples.Dequeue();closed.Add(sample);
            _next=_next.AddSeconds(_seconds);
        }
        _watermark=cutoff;
        while (_trades.TryPeek(out var first) && first.Timestamp<cutoff.AddSeconds(-_seconds)) _trades.Dequeue();
        return closed;
    }
    private (long U,long Z,long D,long X) Totals(DateTimeOffset cutoff)
    {
        long u=0,z=0,d=0,x=0;
        foreach(var t in _trades.Where(t=>t.Timestamp>=cutoff.AddSeconds(-_seconds) && t.Timestamp<cutoff))
            checked { switch(t.Side) {case TradeFlowSide.Up:u+=t.Quantity;break;case TradeFlowSide.Zero:z+=t.Quantity;break;case TradeFlowSide.Down:d+=t.Quantity;break;default:x+=t.Quantity;break;} }
        return(u,z,d,x);
    }
    public FlowWindowSnapshot Snapshot(DateTimeOffset cutoff)
    {
        Advance(cutoff);var t=Totals(cutoff);long total=checked(t.U+t.Z+t.D);
        var valid=_samples.Where(s=>s.Value.HasValue && !s.Partial).Select(s=>s.Value!.Value).ToArray();
        return new(_seconds,total==0?null:100m*t.U/total,total==0?null:100m*t.Z/total,total==0?null:100m*t.D/total,
            total==0?null:100m*(t.U-t.D)/total,valid.Length==0?null:valid.Average(),valid.Length==0?null:valid.Max(),valid.Length==0?null:valid.Min(),
            valid.Length,_count,valid.Length==_count,total,t.X);
    }
}
