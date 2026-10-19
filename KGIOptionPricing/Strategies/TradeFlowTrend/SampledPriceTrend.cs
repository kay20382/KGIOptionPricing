namespace KGIOptionPricing.Strategies.TradeFlowTrend;
public sealed class SampledPriceTrend
{
    private readonly MovingAverageKind _kind;
    private readonly int _fast,_slow;
    private readonly Queue<decimal> _prices=new();
    private DateTimeOffset? _last;
    private decimal? _f,_s;
    public TrendSnapshot Current { get; private set; }=new(null,null,false);
    public SampledPriceTrend(MovingAverageKind kind,int fast,int slow)
    {if(fast<1 || slow<=fast)throw new ArgumentException("MA periods");_kind=kind;_fast=fast;_slow=slow;}
    public void Reset(){_prices.Clear();_f=_s=null;_last=null;Current=new(null,null,false);}
    public TrendSnapshot Sample(DateTimeOffset at,decimal? price)
    {
        if(_last.HasValue && at<=_last)return Current;
        if(_last.HasValue && at-_last!=TimeSpan.FromSeconds(1))Reset();
        _last=at;
        if(price is null or <=0){Reset();_last=at;return Current;}
        _prices.Enqueue(price.Value);if(_prices.Count>_slow)_prices.Dequeue();
        decimal? Next(decimal? prev,int n)=>_prices.Count<n?null:_kind==MovingAverageKind.Sma || !prev.HasValue
            ?_prices.TakeLast(n).Average():prev+2m/(n+1)*(price-prev);
        _f=Next(_f,_fast);_s=Next(_s,_slow);return Current=new(_f,_s,_f.HasValue && _s.HasValue);
    }
}
