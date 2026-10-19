using KGIOptionPricing.StrategyFramework;

namespace KGIOptionPricing.Strategies.MultiFactorMeanR17;

static class MiniReturnGateTests
{
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    public static void Run()
    {
        var start=DateTimeOffset.Parse("2026-09-28T08:00:00+08:00");
        var power=new PowerSnapshot(100,0,0,100,100,100,0,true,true,"ready",null,
            Array.Empty<MovementSegment>(),null,null,0,0);
        var shortPower=power with {BuyPower=-100};
        Dictionary<string,string> Parameters(string flag="1",string floor="0")=>new()
        {
            ["ResearchFactorMode"]="1",["ResearchFactorConfirmSeconds"]="0",
            ["ResearchQueueFloor"]="-1",["ResearchFlowFloor"]="-1",
            ["ResearchUseMiniLastReturnFloor"]=flag,["ResearchMinSignedMiniLastReturnTicks"]=floor
        };
        void Feed(int last,int slope)
        {
            FactorState.Shared=new();
            for(int i=0;i<=last;i++)foreach(var id in new[]{"225MC","225M","225"})
            {
                decimal bid=1000+(id=="225M"?i*slope:0);
                FactorState.Shared.OnMarket(new MarketEvent{ProductId=id,Symbol=id,Timestamp=start.AddSeconds(i),Sequence=i+1,Kind=MarketEventKind.Book,
                    BidPrices=new[]{bid,bid-5,bid-10},AskPrices=new[]{bid+5,bid+10,bid+15},
                    BidQuantities=new[]{30,20,10},AskQuantities=new[]{10,20,30}});
            }
        }
        Feed(0,0);
        Check(new MultiFactorSignal(Parameters()).Evaluate(start,power,power,true)==0,"Mini completed return gate must reject missing samples");
        Check(new MultiFactorSignal(Parameters("0")).Evaluate(start,power,power,true)==1,"disabled Mini return gate must preserve default signal");
        Feed(31,5);
        var now=start.AddSeconds(31);
        Check(new MultiFactorSignal(Parameters()).Evaluate(now,power,power,true)==1,"completed positive Mini return supports long");
        Check(new MultiFactorSignal(Parameters()).Evaluate(now,shortPower,shortPower,true)==0,"completed positive Mini return rejects short");
        Check(new MultiFactorSignal(Parameters("1","-1")).Evaluate(now,shortPower,shortPower,true)==-1,"signed Mini floor equality is inclusive for short");
        Check(new MultiFactorSignal(Parameters("1","-0.5")).Evaluate(now,shortPower,shortPower,true)==0,"short floor rejects stronger opposite Mini return");
        Check(new MultiFactorSignal(Parameters()).Evaluate(now,power,power,false)==0,"Mini return gate cannot override unhealthy data");
        Check(new MultiFactorSignal(Parameters()).Evaluate(now.AddSeconds(-1),power,power,true)==0,"Mini return gate cannot read future observer state");
        Feed(31,-5);
        Check(new MultiFactorSignal(Parameters()).Evaluate(now,shortPower,shortPower,true)==-1,"completed negative Mini return supports short");
        Check(new MultiFactorSignal(Parameters()).Evaluate(now,power,power,true)==0,"completed negative Mini return rejects long");
        Check(new MultiFactorSignal(Parameters("1","-1")).Evaluate(now,power,power,true)==1,"signed Mini floor equality is inclusive for long");
        Feed(31,0);
        Check(new MultiFactorSignal(Parameters()).Evaluate(now,power,power,true)==1,"observed zero Mini return may pass zero floor without imputing missing data");
        foreach(string flag in new[]{"2","-1","0.5"})
        {
            bool rejected=false;try{new MultiFactorSignal(Parameters(flag));}catch(ArgumentOutOfRangeException){rejected=true;}
            Check(rejected,"Mini return enable flag must be exactly zero or one");
        }
    }
}
