using KGIOptionPricing.Strategies.BuySellPower;

namespace KGIOptionPricing.Strategies.PowerReversal;

/// <summary>Local exit conditions only; execution and PnL remain in the shared framework.</summary>
public sealed class IndicatorExitPolicy(PowerReversalParameters parameters)
{
    private decimal _entry, _risk;
    private decimal? _best;
    public DateTimeOffset? PowerSince { get; private set; }
    public decimal? TrailTrigger { get; private set; }
    public void Reset(decimal entry, decimal stop)
    { _entry=entry;_risk=Math.Abs(entry-stop);_best=null;PowerSince=null;TrailTrigger=null; }

    public bool PowerDue(DateTimeOffset at,int direction,PowerSnapshot micro,PowerSnapshot mini,bool microFresh,bool miniFresh)
    {
        bool opposite=microFresh && micro.IsWarm && micro.BuyPower*direction<0 && micro.ShortAverage*direction<0;
        bool match=(int)parameters["PowerExitMode"] switch
        {
            1 => opposite && miniFresh && mini.IsWarm && mini.BuyPower*direction<0 && mini.ShortAverage*direction<0,
            2 => microFresh && miniFresh && micro.IsWarm && mini.IsWarm &&
                 (micro.BuyPower+mini.BuyPower)*direction/2 <= parameters["ExitWeakPowerThreshold"] &&
                 (micro.BaisPower+mini.BaisPower)*direction/2 < 0,
            _ => opposite
        };
        if(!match){PowerSince=null;return false;}
        PowerSince??=at;
        return at>=PowerSince.Value.AddSeconds((double)parameters["ExitConfirmSeconds"]);
    }

    public bool TrailHit(int direction,decimal executable,decimal tick,bool fresh)
    {
        if(parameters["TrailingExitMode"]==0 || !fresh || executable<=0 || tick<=0 || _risk<=0)return false;
        if(!_best.HasValue || direction*(executable-_best.Value)>0)_best=executable;
        if(direction*(_best.Value-_entry)>=_risk*parameters["TrailingActivateRisk"])
        {
            decimal distance=Math.Ceiling(_risk*parameters["TrailingDistanceRisk"]/tick)*tick;
            decimal candidate=_best.Value-direction*distance;
            // Round toward a later trigger, never advertise sub-tick protection.
            candidate=(direction>0?Math.Floor(candidate/tick):Math.Ceiling(candidate/tick))*tick;
            if(!TrailTrigger.HasValue || direction*(candidate-TrailTrigger.Value)>0)TrailTrigger=candidate;
        }
        return TrailTrigger.HasValue && direction*(executable-TrailTrigger.Value)<=0;
    }
}
