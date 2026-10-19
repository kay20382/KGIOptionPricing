namespace KGIOptionPricing.Strategies.TradeFlowTrend;
public sealed class TradeBarBuilder
{
    private readonly int _seconds;
    private TradeBar? _bar;
    private DateTimeOffset? _last;
    public TradeBarBuilder(int seconds){if(seconds<=0)throw new ArgumentOutOfRangeException(nameof(seconds));_seconds=seconds;}
    public IReadOnlyList<TradeBar> Add(DateTimeOffset at,decimal price,int quantity)
    {
        if(quantity<0 || price<=0 || at<_last)throw new ArgumentException("Invalid trade");
        var result=Advance(at);_last=at;if(quantity==0)return result;
        var start=TradeFlowWindow.Floor(at,_seconds);
        _bar=_bar is null?new(start,start.AddSeconds(_seconds),price,price,price,price,quantity):
            _bar with{High=Math.Max(_bar.High,price),Low=Math.Min(_bar.Low,price),Close=price,Volume=checked(_bar.Volume+quantity)};
        return result;
    }
    public IReadOnlyList<TradeBar> Advance(DateTimeOffset cutoff)
    {if(_bar is null || _bar.End>cutoff)return Array.Empty<TradeBar>();var closed=_bar;_bar=null;return new[]{closed};}
}
