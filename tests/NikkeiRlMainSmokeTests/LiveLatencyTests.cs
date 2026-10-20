using KGIOptionPricing.Strategies.NikkeiRlDirection;
using KGIOptionPricing.StrategyFramework;

static class LiveLatencyTests
{
    public static void Run()
    {
        var failures=new List<string>();int checks=0;
        void Check(bool value,string text){checks++;if(!value)failures.Add(text);}
        Check(new NikkeiRlParameters{LiveTradingEnabled=true}.ToDictionary()["DecisionLatencyMs"]=="0","live effective action delay must be zero");
        Check(new NikkeiRlParameters().ToDictionary()["DecisionLatencyMs"]=="1","simulation must retain one millisecond");
        var start=new DateTimeOffset(2026,10,13,8,0,0,TimeSpan.FromHours(8));
        var universe=new ResolvedStrategyUniverse{Tradable=new(){ProductId="225MC",Symbol="MC",TickSize=5,ContractMultiplier=10},References=[new(){ProductId="225M",Symbol="M",TickSize=5,ContractMultiplier=100}]};
        foreach(var variant in new[]{"earlier","grid15-positive-net","grid15-q90-research"})
        foreach(var scenario in new[]{(Action:1,Position:0),(Action:2,Position:0),(Action:2,Position:1),(Action:3,Position:1)}){
            var view=new View();var clock=new TestClock{Now=start};var policy=new ForcedPolicy();
            var s=new NikkeiRlStrategy(new(){InstanceId="latency",Universe=new(){Tradable=new("225MC"),References=[new("225M")]},Parameters=new NikkeiRlParameters{PolicyVariant=variant,EnableNightSession=variant=="earlier",LiveTradingEnabled=true}.ToDictionary()},_=>policy);
            s.Start(new(universe,clock,view,1));var orders=new List<PaperOrderRequest>();var cancelled=new List<string>();
            s.OrderRequested+=orders.Add;s.CancelOrderRequested+=cancelled.Add;
            for(int sec=0;sec<240;sec++){
                foreach(var product in new[]{"225MC","225M"}){
                    var at=start.AddSeconds(sec);var symbol=product=="225MC"?"MC":"M";
                    Feed(new(){ProductId=product,Symbol=symbol,Kind=MarketEventKind.Book,Timestamp=at,BidPrices=[40000,39995,39990,39985,39980],AskPrices=[40005,40010,40015,40020,40025],BidQuantities=[10,10,10,10,10],AskQuantities=[10,10,10,10,10]});
                }
                foreach(var product in new[]{"225MC","225M"})Feed(new(){ProductId=product,Symbol=product=="225MC"?"MC":"M",Kind=MarketEventKind.Trade,Timestamp=start.AddSeconds(sec).AddMilliseconds(1),LastPrice=40005,TradeQuantity=1});
            }
            void Feed(MarketEvent e){clock.Now=e.Timestamp;if(s.BeforeMarketExecution(e))s.OnMarketEvent(e);}
            view.Position=scenario.Position;policy.Action=scenario.Action;clock.Now=start.AddSeconds(240);s.OnHeartbeat(clock.Now);
            var role=scenario.Position!=0?PaperOrderRole.Flatten:scenario.Action==1?PaperOrderRole.EntryLong:PaperOrderRole.EntryShort;
            Check(orders.Count==1&&orders[0].Role==role,$"live action {scenario.Action}, position {scenario.Position} must submit at heartbeat without another trade");
            if(scenario.Position==0){
                Check(orders.Count==0||orders[0].Type==PaperOrderType.Limit,"removing delay cannot turn strict-cross entry into market order");
                policy.Action=4;clock.Now=start.AddSeconds(243);s.OnHeartbeat(clock.Now);
                Check(cancelled.Count==1,"live cancel must submit at heartbeat without waiting for a trade");
            }
            view.Position=0;s.RequestClose("test complete");
        }
        if(failures.Count>0)throw new Exception(string.Join("\n",failures));
        Console.WriteLine($"PASS live zero artificial delay {checks} checks: long/short, reverse, flatten, cancel and simulation contract");
    }
    sealed class View:IStrategyPerformanceView
    {
        public int Position;
        public PositionSnapshot GetPosition(string id,string symbol)=>new(){NetQuantity=Position,AveragePrice=40000,MarkPrice=40005};
    }
}
