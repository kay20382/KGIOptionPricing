namespace KGIOptionPricing.Strategies.TradeFlowTrend;
public sealed class ParabolicSarTracker
{
    private readonly decimal _start,_step,_max;
    private TradeBar? _last,_previous;
    public SarSnapshot? Current {get;private set;}
    public ParabolicSarTracker(decimal start,decimal step,decimal maximum)
    {if(start<=0 || step<=0 || start>maximum || step>maximum || maximum>1)throw new ArgumentException("AF");_start=start;_step=step;_max=maximum;}
    public void Reset(){Current=null;_last=_previous=null;}
    public SarSnapshot? Add(TradeBar bar)
    {
        if(_last!=null && bar.End<=_last.End)throw new ArgumentException("SAR bar order");
        if(_last!=null)
        {
            if(Current is null && bar.Close!=_last.Close)
            {bool up=bar.Close>_last.Close;Current=new(up?Math.Min(_last.Low,bar.Low):Math.Max(_last.High,bar.High),up,
                up?Math.Max(_last.High,bar.High):Math.Min(_last.Low,bar.Low),_start,bar.End);}
            else if(Current is { } s)
            {
                decimal next=s.Value+s.Acceleration*(s.Extreme-s.Value);
                bool reverse=s.IsLong?bar.Low<next:bar.High>next;
                if(reverse)Current=new(s.Extreme,!s.IsLong,s.IsLong?bar.Low:bar.High,_start,bar.End);
                else
                {
                    next=s.IsLong?Math.Min(next,Math.Min(_last.Low,_previous?.Low??_last.Low)):Math.Max(next,Math.Max(_last.High,_previous?.High??_last.High));
                    decimal extreme=s.IsLong?Math.Max(s.Extreme,bar.High):Math.Min(s.Extreme,bar.Low);
                    Current=new(next,s.IsLong,extreme,extreme!=s.Extreme?Math.Min(_max,s.Acceleration+_step):s.Acceleration,bar.End);
                }
            }
        }
        _previous=_last;_last=bar;return Current;
    }
}
