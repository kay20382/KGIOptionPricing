using KGIOptionPricing.Strategies.BuySellPower;
using KGIOptionPricing.Strategies.PowerReversal;
static class AdaptiveParameterTests
{
    public static void Run()
    {
        void Check(bool ok,string why){if(!ok)throw new Exception(why);}
        var t=new DateTimeOffset(2026,10,6,8,0,0,TimeSpan.FromHours(8));
        var p=PowerReversalParameters.Parse(new Dictionary<string,string>{{"SignalMode","1"},{"AdaptiveForceMode","1"},{"AdaptiveAgreementMode","1"},{"AdaptiveMinSamples","10"},{"AdaptiveLookbackSeconds","60"}});
        var state=new AdaptiveParameterState(p);
        PowerSnapshot S(decimal power)=>new(11,0,0,power,10,5,5,true,true,"就緒",null,Array.Empty<MovementSegment>(),100,100,5,5);
        Check(state.GetThresholds(t)==null,"insufficient adaptive samples retain original thresholds");
        for(int i=0;i<10;i++){state.GetThresholds(t.AddSeconds(i));state.Observe(t.AddSeconds(i),S(80),S(40));state.Observe(t.AddSeconds(i).AddMilliseconds(1),S(0),S(0));}
        Check(state.SampleCount==10,"sampling is bounded to one per second, never quote-density weighted");
        var settings=state.GetThresholds(t.AddSeconds(10));
        Check(settings!.Joint==60 && settings.Power==30 && settings.Difference==40,"quantiles and safety clamps use earlier samples");
        state.Observe(t.AddSeconds(10),S(0),S(0));
        Check(state.GetThresholds(t.AddSeconds(10))==settings,"current observation cannot change the threshold for its own event");
        Check(state.GetThresholds(t.AddSeconds(80))==null,"expired samples cannot sustain adaptive readiness");
        state.Reset();Check(state.SampleCount==0,"session/reconnect clears adaptive history");
        var holding=PowerReversalParameters.Parse(new Dictionary<string,string>{{"SignalMode","1"},{"AdaptiveHoldingMode","1"},{"MinMovementSamples","1"},{"AdaptiveHoldingFraction","0.5"},{"MaxHoldingSeconds","300"}});
        var segments=new[]{new MovementSegment("上漲",100,200,t,t.AddSeconds(200),t.AddSeconds(220),true),new MovementSegment("下跌",200,100,t,t.AddSeconds(500),t.AddSeconds(510),true),new MovementSegment("上漲",100,200,t,t.AddSeconds(1000),t.AddSeconds(1100),false)};
        Check(AdaptiveParameterState.HoldingSeconds(S(50) with{History=segments},1,holding)==100,"holding uses completed same-direction extreme duration only");
        Check(AdaptiveParameterState.HoldingSeconds(S(50) with{History=segments},-1,holding)==250,"short holding uses completed downward segments");
        Check(AdaptiveParameterState.HoldingSeconds(S(50),1,holding)==300,"missing duration samples retain original holding cap");
        var gate=new ReversalSignalGate(p){Adaptive=settings};
        Check(!gate.ConsensusMatches(S(40),S(40),1),"adaptive joint threshold is used by the real signal gate");
        Check(gate.ConsensusMatches(S(80),S(40),1),"adaptive consensus inclusive boundary remains valid");
        Console.WriteLine("PASS adaptive parameter causal sampling, quantiles, expiry, duration and gate");
    }
}
