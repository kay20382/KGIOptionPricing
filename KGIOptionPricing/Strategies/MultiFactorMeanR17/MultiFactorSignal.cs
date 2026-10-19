
namespace KGIOptionPricing.Strategies.MultiFactorMeanR17;
using KGIOptionPricing.StrategyFramework;
public sealed class MultiFactorSignal
{
    readonly IReadOnlyDictionary<string,string> _parameters;
    readonly FactorState _state;
    public IReadOnlyList<ThresholdObservation> LastThresholds {get;private set;}=Array.Empty<ThresholdObservation>();
    DateTimeOffset? _lastFrame,_since;
    int _direction;
    readonly int _frameMilliseconds;
    readonly decimal _confirmationSeconds;
    public MultiFactorSignal(IReadOnlyDictionary<string,string> parameters,FactorState? state=null)
    {
        _parameters=parameters;_state=state??FactorState.Shared;
        decimal frame=P("DecisionFrameMilliseconds",1000);
        if(frame<100 || frame>1000 || decimal.Truncate(frame)!=frame)throw new ArgumentOutOfRangeException(nameof(parameters),"Decision frame must be an integer from100 through1000ms");
        _frameMilliseconds=(int)frame;
        _confirmationSeconds=P("FactorConfirmSeconds",1);
        if(_confirmationSeconds<0)throw new ArgumentOutOfRangeException(nameof(parameters),"Confirmation must not be negative");
        decimal varianceFloor=P("MinLargeVariancePressure",0);
        if(varianceFloor < -1 || varianceFloor > 1)throw new ArgumentOutOfRangeException(nameof(parameters),"Directional variance pressure floor must be between minus1 and1");
        decimal miniReturnFlag=P("UseMiniLastReturnFloor",0);
        if(miniReturnFlag!=0 && miniReturnFlag!=1)throw new ArgumentOutOfRangeException(nameof(parameters),"Mini completed-return enable flag must be zero or one");
    }
    decimal P(string key,decimal fallback)=>_parameters.TryGetValue("Research"+key,out var s)?decimal.Parse(s,System.Globalization.CultureInfo.InvariantCulture):fallback;
    public bool Enabled=>P("FactorMode",0)>0;
    public void Reset(){_since=null;_direction=0;_lastFrame=null;}
    public int Evaluate(DateTimeOffset now,PowerSnapshot mini,PowerSnapshot micro,bool healthy)
    {
        LastThresholds=Array.Empty<ThresholdObservation>();
        if(!healthy){_since=null;_direction=0;return 0;}
        long frame=now.ToUnixTimeMilliseconds()/_frameMilliseconds;
        if(_lastFrame.HasValue && _lastFrame.Value.ToUnixTimeMilliseconds()/_frameMilliseconds==frame)return 0;
        if(_lastFrame.HasValue && now>_lastFrame.Value.AddSeconds(2)){_since=null;_direction=0;}
        _lastFrame=now;
        var m=_state.Read("225MC",now,5);var n=_state.Read("225M",now,5);var large=_state.Read("225",now,10);
        if(m==null || n==null || large==null || !mini.BuyPower.HasValue || !micro.BuyPower.HasValue){_since=null;_direction=0;return 0;}
        decimal power=(mini.BuyPower.Value+micro.BuyPower.Value)/200;
        decimal queue=(m.QueueImbalance+n.QueueImbalance)/2;
        decimal lead=P("LeadProduct",0) switch
        {
            1=>(large.Mid-m.Mid)/5,
            2=>(large.WeightedMid!.Value-m.WeightedMid!.Value)/5,
            _=>(n.Mid-m.Mid)/5
        };
        decimal? score=P("FactorMode",0) switch
        {
            1=>power,
            2=>lead,
            3=>m.MeanDeviationTicks,
            4=>.4m*power+.4m*queue+.2m*Math.Clamp(lead/2,-1,1),
            5=>queue,
            6=>m.VwapDeviationTicks,
            7=>m.MomentumTicks,
            8=>m.TickVolatility30>0 && m.MeanDeviationTicks.HasValue?m.MeanDeviationTicks/(m.TickVolatility30*(decimal)Math.Sqrt(30)):null,
            _=>null
        };
        int direction=score.HasValue?Math.Sign(score.Value):0;
        bool passes=direction!=0 && Math.Abs(score!.Value)>=P("ScoreThreshold",.1m)
            && direction*queue>=P("QueueFloor",-.3m)
            && direction*power>=P("FlowFloor",-.2m);
        var observations=new List<ThresholdObservation>
        {
            new("R17.ScoreMagnitude",score.HasValue?Math.Abs(score.Value):null,P("ScoreThreshold",.1m),"score",direction!=0 && Math.Abs(score!.Value)>=P("ScoreThreshold",.1m)),
            new("R17.SignedQueue",direction*queue,P("QueueFloor",-.3m),"ratio",direction!=0 && direction*queue>=P("QueueFloor",-.3m)),
            new("R17.SignedPower",direction*power,P("FlowFloor",-.2m),"ratio",direction!=0 && direction*power>=P("FlowFloor",-.2m))
        };
        if(P("RequireOfi",0)==1)passes &= direction*(m.Ofi+n.Ofi)>=0;
        if(P("RequireMomentum",0)==1)passes &= m.MomentumTicks.HasValue && n.MomentumTicks.HasValue && direction*(m.MomentumTicks+n.MomentumTicks)>=0;
        if(P("MinEfficiency",0)>0)passes &= m.Efficiency>=P("MinEfficiency",0);
        if(P("RequireDepthShare",0)==1)passes &= m.BestDepthShareDifference.HasValue && direction*m.BestDepthShareDifference>=0;
        if(P("RequireLastReturn",0)==1)passes &= m.LastReturnTicks.HasValue && direction*m.LastReturnTicks.Value>0;
        if(P("MaxEfficiency",0)>0)passes &= m.Efficiency.HasValue && m.Efficiency.Value<=P("MaxEfficiency",0);
        if(P("MaxVolatilityTicks",0)>0)passes &= m.TickVolatility30.HasValue && m.TickVolatility30.Value<=P("MaxVolatilityTicks",0);
        if(P("MaxArrivalCV",0)>0)passes &= m.TradeIntervalCV200.HasValue && n.TradeIntervalCV200.HasValue
            && Math.Max(m.TradeIntervalCV200.Value,n.TradeIntervalCV200.Value)<=P("MaxArrivalCV",0);
        if(P("MinPrintRate",0)>0)passes &= m.PrintRate30.HasValue && n.PrintRate30.HasValue && m.PrintRate30.Value+n.PrintRate30.Value>=P("MinPrintRate",0);
        if(P("MinLargePrintRate",0)>0)passes &= large.PrintRate30.HasValue && large.PrintRate30.Value>=P("MinLargePrintRate",0);
        if(P("MinMiniPrintRate",0)>0)passes &= n.PrintRate30.HasValue && n.PrintRate30.Value>=P("MinMiniPrintRate",0);
        if(P("UseMiniMomentumCeiling",0)==1)passes &= n.MomentumTicks.HasValue && direction*n.MomentumTicks.Value<=P("MaxSignedMiniMomentumTicks",-3);
        if(P("MinMiniArrivalCV",0)>0)passes &= n.TradeIntervalCV200.HasValue && n.TradeIntervalCV200.Value>=P("MinMiniArrivalCV",0);
        if(P("MinLargeArrivalCV",0)>0)passes &= large.TradeIntervalCV200.HasValue && large.TradeIntervalCV200.Value>=P("MinLargeArrivalCV",0);
        if(P("MinSignedMiniQueue",0)>0)passes &= direction*n.QueueImbalance>=P("MinSignedMiniQueue",0);
        if(P("UseMiniLastReturnFloor",0)==1)passes &= n.LastReturnTicks.HasValue
            && direction*n.LastReturnTicks.Value>=P("MinSignedMiniLastReturnTicks",0);
        if(P("UseLargeVariancePressure",0)==1)passes &= large.DownsideVarianceShare30.HasValue
            && direction*(1-2*large.DownsideVarianceShare30.Value)>=P("MinLargeVariancePressure",0);
        if(P("UseMiniLastReturnFloor",0)==1)observations.Add(new("R17.MiniSignedCompletedReturn",direction*n.LastReturnTicks,P("MinSignedMiniLastReturnTicks",0),"ticks",n.LastReturnTicks.HasValue && direction*n.LastReturnTicks.Value>=P("MinSignedMiniLastReturnTicks",0)));
        if(P("UseLargeVariancePressure",0)==1)observations.Add(new("R17.LargeSignedVariancePressure",large.DownsideVarianceShare30.HasValue?direction*(1-2*large.DownsideVarianceShare30.Value):null,P("MinLargeVariancePressure",0),"ratio",large.DownsideVarianceShare30.HasValue && direction*(1-2*large.DownsideVarianceShare30.Value)>=P("MinLargeVariancePressure",0)));
        if(P("MinLargePrintRate",0)>0)observations.Add(new("R17.LargePrintRate",large.PrintRate30,P("MinLargePrintRate",0),"events/second",large.PrintRate30.HasValue && large.PrintRate30.Value>=P("MinLargePrintRate",0)));
        LastThresholds=observations;
        if(!passes){_since=null;_direction=0;return 0;}
        if(direction!=_direction || !_since.HasValue){_direction=direction;_since=now;return _confirmationSeconds==0?direction:0;}
        return now>=_since.Value.AddSeconds((double)_confirmationSeconds)?direction:0;
    }
}
