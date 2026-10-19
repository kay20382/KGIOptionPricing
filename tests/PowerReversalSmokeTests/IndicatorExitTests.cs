using KGIOptionPricing.Strategies.BuySellPower;
using KGIOptionPricing.Strategies.PowerReversal;

static class IndicatorExitTests
{
    static readonly DateTimeOffset T = new(2026,10,8,8,0,0,TimeSpan.FromHours(8));
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
    static PowerSnapshot S(decimal power,decimal bais=0) => new(1,1,0,power,power,power-bais,bais,true,true,"就緒",null,[],null,null,0,0);
    public static void Run()
    {
        foreach(int direction in new[]{1,-1})
        {
            var p=PowerReversalParameters.Parse(new Dictionary<string,string>{{"PowerExitMode","1"},{"ExitConfirmSeconds","5"},{"TrailingExitMode","1"}});
            var exit=new IndicatorExitPolicy(p);exit.Reset(70000m,70000m-direction*40);
            var reverse=S(-direction*20);
            Require(!exit.PowerDue(T,direction,reverse,S(direction*10),true,true),"mini must confirm reversal");
            Require(!exit.PowerDue(T.AddSeconds(1),direction,reverse,reverse,true,true),"confirmation begins");
            Require(!exit.PowerDue(T.AddSeconds(6),direction,reverse,reverse,true,false),"stale mini breaks confirmation");
            Require(!exit.PowerDue(T.AddSeconds(7),direction,reverse,reverse,true,true),"fresh confirmation restarts");
            Require(exit.PowerDue(T.AddSeconds(12),direction,reverse,reverse,true,true),"confirmed reversal exits symmetrically");
            Require(!exit.TrailHit(direction,70000+direction*35,5,true),"not activated before one risk");
            Require(!exit.TrailHit(direction,70000+direction*40,5,false),"stale quote cannot activate trail");
            Require(exit.TrailTrigger==null,"no stale trail");
            Require(!exit.TrailHit(direction,70000+direction*40,5,true),"activation is not exit");
            Require(exit.TrailTrigger==70000+direction*20,"trail half risk behind high-water mark");
            Require(!exit.TrailHit(direction,70000+direction*30,5,true),"trail remains fixed during pullback");
            Require(exit.TrailTrigger==70000+direction*20,"never loosen trail");
            Require(exit.TrailHit(direction,70000+direction*20,5,true),"touch triggers trailing exit");
            exit.Reset(70000m,70000m-direction*40);Require(exit.TrailTrigger==null && exit.PowerSince==null,"new trade clears state");
        }
        var weak=new IndicatorExitPolicy(PowerReversalParameters.Parse(new Dictionary<string,string>{{"PowerExitMode","2"},{"ExitConfirmSeconds","5"}}));
        weak.Reset(100,70);
        Require(!weak.PowerDue(T,1,S(5,-3),S(5,-3),true,true),"weakening confirmation begins");
        Require(weak.PowerDue(T.AddSeconds(5),1,S(5,-3),S(5,-3),true,true),"weak positive force with negative bias can exit");
        var disabled=new IndicatorExitPolicy(PowerReversalParameters.Parse(new Dictionary<string,string>()));disabled.Reset(100,70);
        Require(!disabled.TrailHit(1,200,5,true) && disabled.TrailTrigger==null,"default trail disabled");
        Require(!disabled.PowerDue(T,1,S(-20),S(100),true,false),"legacy micro reversal starts without mini confirmation");
        Require(disabled.PowerDue(T.AddSeconds(5),1,S(-20),S(100),true,false),"legacy mode ignores stale mini exactly as before");
        Require(!disabled.PowerDue(T.AddSeconds(6),1,S(-20),S(-20),false,true),"stale micro resets legacy confirmation");
        bool invalid=false;try{PowerReversalParameters.Parse(new Dictionary<string,string>{{"PowerExitMode","3"}});}catch(ArgumentOutOfRangeException){invalid=true;}
        Require(invalid,"invalid mode rejected");
        Console.WriteLine("PASS indicator exit confirmation, freshness, symmetric trailing and reset");
    }
}
