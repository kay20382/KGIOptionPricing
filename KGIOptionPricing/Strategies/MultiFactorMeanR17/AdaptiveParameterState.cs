
namespace KGIOptionPricing.Strategies.MultiFactorMeanR17;

public sealed record AdaptiveSignalThresholds(decimal Power,decimal Joint,decimal Difference);

/// <summary>Past-only, at-most-one-per-second observations; no portfolio authority.</summary>
public sealed class AdaptiveParameterState(R17BaseParameters p)
{
    private readonly Queue<(DateTimeOffset At,decimal Joint,decimal Difference)> _samples=new();
    private DateTimeOffset? _lastSample,_computedAt;
    private AdaptiveSignalThresholds? _cached;
    public int SampleCount=>_samples.Count;
    public bool NeedsSample(DateTimeOffset at)=>(p["AdaptiveForceMode"]==1 || p["AdaptiveAgreementMode"]==1) && (!_lastSample.HasValue || at>=_lastSample.Value.AddSeconds(1));
    public void Reset(){_samples.Clear();_lastSample=null;_computedAt=null;_cached=null;}
    public void Observe(DateTimeOffset at,PowerSnapshot mini,PowerSnapshot micro)
    {
        if(!NeedsSample(at) || !mini.IsWarm || !micro.IsWarm || !mini.BuyPower.HasValue || !micro.BuyPower.HasValue)return;
        _samples.Enqueue((at,Math.Abs((mini.BuyPower.Value+micro.BuyPower.Value)/2),Math.Abs(mini.BuyPower.Value-micro.BuyPower.Value)));_lastSample=at;
    }
    public AdaptiveSignalThresholds? GetThresholds(DateTimeOffset at)
    {
        if(p["AdaptiveForceMode"]==0 && p["AdaptiveAgreementMode"]==0)return null;
        if(_computedAt.HasValue && at<_computedAt.Value.AddSeconds(1))return _cached;
        _computedAt=at;
        while(_samples.TryPeek(out var first) && first.At<=at.AddSeconds(-(double)p["AdaptiveLookbackSeconds"]))_samples.Dequeue();
        if(_samples.Count<p["AdaptiveMinSamples"]){_cached=null;return null;}
        decimal joint=p["AdaptiveForceMode"]==1?Math.Clamp(Quantile(_samples.Select(x=>x.Joint),p["AdaptiveForcePercentile"]),20,60):p["MinJointPower"];
        decimal power=p["AdaptiveForceMode"]==1?Math.Clamp(joint/2,10,30):p["PowerThreshold"];
        decimal difference=p["AdaptiveAgreementMode"]==1?Math.Clamp(Quantile(_samples.Select(x=>x.Difference),90),10,40):p["MaxPowerDifference"];
        return _cached=new(power,joint,difference);
    }
    private static decimal Quantile(IEnumerable<decimal> values,decimal percentile)
    {var sorted=values.Order().ToArray();return sorted[(int)decimal.Ceiling(sorted.Length*percentile/100)-1];}
    public static decimal HoldingSeconds(PowerSnapshot s,int direction,R17BaseParameters p)
    {
        if(p["AdaptiveHoldingMode"]==0)return p["MaxHoldingSeconds"];
        var durations=s.History.Where(x=>x.IsCompleted && x.Direction==(direction>0?"上漲":"下跌") && x.DurationSeconds>0).TakeLast((int)p["MovementAveragePeriods"]).Select(x=>x.DurationSeconds).ToArray();
        return durations.Length<p["MinMovementSamples"]?p["MaxHoldingSeconds"]:Math.Clamp(decimal.Ceiling(durations.Average()*p["AdaptiveHoldingFraction"]),p["AdaptiveMinHoldingSeconds"],p["MaxHoldingSeconds"]);
    }
}
