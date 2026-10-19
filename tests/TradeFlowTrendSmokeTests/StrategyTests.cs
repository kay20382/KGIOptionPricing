using KGIOptionPricing.Strategies.TradeFlowTrend;
using KGIOptionPricing.StrategyFramework;
using KGIOptionPricing.StrategyFramework.MarketData;
using KGIOptionPricing.StrategyFramework.Execution;
using KGIOptionPricing.StrategyFramework.Performance;
using static TradeFlowTrendSmokeTests.TestSupport;
namespace TradeFlowTrendSmokeTests;
internal static class StrategyTests
{
    public static void Run()
    {
        LateTradeAfterHeartbeatDoesNotRewindWindow();
        var p=new TradeFlowParameters();var up=new FlowInputs(120,110,20,10,15,5);
        Check(TradeFlowSignalRules.EntryDirection(up,p)==1,"long");
        Check(TradeFlowSignalRules.EntryDirection(up with{MiniReal=10},p)==0,"equal avg");
        Check(TradeFlowSignalRules.EntryDirection(up with{LargeReal=null},p)==0,"missing");
        Check(TradeFlowSignalRules.TrendExit(1,new(100,110,-1,null,-2,null),p),"C independent of avg");
        var start=At("2026-09-25T08:00:00+08:00");var clock=new ManualClock(start);
        var universe=new ResolvedStrategyUniverse{Tradable=new(){ProductId="225MC",Symbol="MC",TickSize=5,ContractMultiplier=10},References=[new(){ProductId="225M",Symbol="M",TickSize=5},new(){ProductId="225",Symbol="L",TickSize=10}]};
        var strategy=new TradeFlowTrendStrategy(new(){InstanceId="tf",StrategyType="TradeFlowTrend",Parameters=(p with{FastSeconds=2,SlowSeconds=3,History30=1,History60=1,History180=1}).ToDictionary()});
        var ledger=new PaperPortfolioLedger();var broker=new CrossPricePaperExecutionService(0,clock,false,true);using var runner=new StrategyInstanceRunner(strategy,universe,ledger,clock,0,broker);
        long seq=0;var orders=new List<PaperOrderRequest>();strategy.OrderRequested+=orders.Add;
        void Feed(int second,decimal price,bool rising)
        {
            var at=start.AddSeconds(second);clock.Advance(at);
            foreach(var prod in universe.AllProducts)
            {
                runner.OnMarketEvent(new(){Sequence=++seq,Timestamp=at,ProductId=prod.ProductId,Symbol=prod.Symbol,Kind=MarketEventKind.Book,BidPrices=[price],AskPrices=[price+10],BidQuantities=[10],AskQuantities=[10]});
            }
            foreach(var prod in universe.AllProducts) {
                runner.OnMarketEvent(new(){Sequence=++seq,Timestamp=at.AddMilliseconds(1),ProductId=prod.ProductId,Symbol=prod.Symbol,Kind=MarketEventKind.Trade,LastPrice=rising?price+10:price,TradeQuantity=1});
            }
        }
        for(int i=0;i<30;i++)Feed(i,40000,false);
        Check(orders.Count==0,"warmup must not enter");
        for(int i=30;i<60;i++)Feed(i,40000+(i-30)*10,true);
        Check(orders.Any(x=>x.Role==PaperOrderRole.EntryLong),"warm history plus flow and MA opens");
        Check(orders.First().Price%5==0 && orders.First().Type==PaperOrderType.BrokerLimit,"tick aligned entry");
        runner.Pause();var before=orders.Count;runner.Pulse(start.AddSeconds(61));Check(orders.Count==before,"pause no new entry");
        Check(strategy.GetSnapshot().Features.ContainsKey("225M.30.Real"),"feature snapshot");
        Check(StrategySimulationService.DefaultFactories.Any(x=>x.StrategyType=="TradeFlowTrend") && LiveStrategyService.DefaultFactories.Any(x=>x.StrategyType=="TradeFlowTrend"),"dual registration");
    }

    private static void LateTradeAfterHeartbeatDoesNotRewindWindow()
    {
        var start = At("2026-09-29T12:31:46+08:00");
        var universe = new ResolvedStrategyUniverse
        {
            Tradable = new() { ProductId = "225MC", Symbol = "MC", TickSize = 5, ContractMultiplier = 10 },
            References = [new() { ProductId = "225M", Symbol = "M", TickSize = 5 },
                          new() { ProductId = "225", Symbol = "L", TickSize = 10 }]
        };
        var strategy = new TradeFlowTrendStrategy(new()
        {
            InstanceId = "tf-late-trade", StrategyType = "TradeFlowTrend",
            Parameters = new TradeFlowParameters().ToDictionary()
        });
        strategy.Start(new StrategyRuntimeContext(universe, new ManualClock(start), new PaperPortfolioLedger()));
        strategy.OnMarketEvent(new MarketEvent
        {
            Sequence = 1, Timestamp = start, ProductId = "225MC", Symbol = "MC",
            Kind = MarketEventKind.Book, BidPrices = [40000m], AskPrices = [40010m],
            BidQuantities = [10], AskQuantities = [10]
        });
        strategy.OnHeartbeat(start.AddSeconds(2));

        strategy.OnMarketEvent(new MarketEvent
        {
            Sequence = 2, Timestamp = start.AddSeconds(1).AddMilliseconds(900),
            ProductId = "225MC", Symbol = "MC", Kind = MarketEventKind.Trade,
            LastPrice = 40005m, TradeQuantity = 1
        });

        Check(strategy.GetSnapshot().Reason == "OutOfOrderEvent",
            "整秒視窗結算後的晚到成交須明確略過，不得倒填視窗或擲出例外");
        strategy.OnMarketEvent(new MarketEvent
        {
            Sequence = 3, Timestamp = start.AddSeconds(2).AddMilliseconds(100),
            ProductId = "225MC", Symbol = "MC", Kind = MarketEventKind.Trade,
            LastPrice = 40005m, TradeQuantity = 1
        });
        Check(strategy.GetSnapshot().Reason != "OutOfOrderEvent",
            "略過晚到成交後，較新的成交仍須可繼續處理");
    }
}
