using KGIOptionPricing.Strategies.TradeFlowTrend;
using KGIOptionPricing.StrategyFramework;
using KGIOptionPricing.StrategyFramework.MarketData;
using KGIOptionPricing.StrategyFramework.Performance;
using KGIOptionPricing.OrderManagement.Core;
using static TradeFlowTrendSmokeTests.TestSupport;
namespace TradeFlowTrendSmokeTests;
internal static class ReviewRegressionTests
{
    public static void Run()
    {
        var failures=new List<string>();
        void Case(string name,Action action){try{action();Console.WriteLine("PASS review "+name);}catch(Exception ex){failures.Add(name+": "+ex.Message);}}
        Case("C without average history",()=>{
            var h=new Harness(new(){FastSeconds=2,SlowSeconds=3});h.Feed(0,1000,1200,false);h.Open(1,1000);
            for(int i=1;i<5;i++)h.Feed(i,1000,1200-i*10,false);
            Check(h.Orders.Any(o=>o.Role==PaperOrderRole.Flatten),"C blocked by missing full Ri");
        });
        Case("Pause does not accept",()=>{
            var h=new Harness(new(){FastSeconds=2,SlowSeconds=3,History30=1});
            for(int i=0;i<30;i++)h.Feed(i,1000,1000,false);
            for(int i=30;i<55;i++)h.Feed(i,1000,1000+(i-30)*10,true);
            h.Strategy.Pause("使用者暫停");h.Decisions.Clear();h.Feed(55,1000,1260,true);
            Check(!h.Decisions.Any(d=>d.Kind==StrategyObservationKind.Decision && d.Decision==StrategyDecision.Accept),"paused accept contaminates funnel");
        });
        Case("Unknown volume quality",()=>{
            var h=new Harness(new());h.Strategy.OnMarketEvent(new(){Kind=MarketEventKind.Trade,Symbol="MC",ProductId="225MC",Timestamp=h.Start,Sequence=1,LastPrice=1000,TradeQuantity=2});h.Strategy.OnHeartbeat(h.Start.AddSeconds(1));
            Check(h.Decisions.Any(d=>d.DataQualityFlags.Contains("UnknownTradeClassification")),"missing quality flag");
        });
        Case("Session heartbeat exit",()=>{
            var h=new Harness(new(),At("2026-09-25T14:24:58+08:00"));h.Open(-2,1000);h.Feed(0,1000,1000,false);
            h.Strategy.OnHeartbeat(h.Start.AddSeconds(2));Check(h.Orders.Count==1 && h.Orders[0].Side==BuySell.Buy && h.Orders[0].Quantity==2 && h.Orders[0].Role==PaperOrderRole.Flatten,"session exit at no-tick cutoff");
        });
        Case("SAR reversed direction",()=>{
            Check(TradeFlowSignalRules.SarExit(1,1000,new(950,false,900,.02m,DateTimeOffset.UtcNow)),"opposite short SAR exits long even above line");
            Check(TradeFlowSignalRules.SarExit(-1,1000,new(1050,true,1100,.02m,DateTimeOffset.UtcNow)),"opposite long SAR exits short even below line");
        });
        Case("Long short protective boundaries",()=>{
            foreach(int sign in new[]{1,-1})
            {
                var stop=new Harness(new());stop.Feed(0,1000,1000,false);stop.Open(sign,1000);
                stop.Book(.1,sign>0?975:1015);Check(stop.Orders.Count==0,"5 tick loss does not stop");
                stop.Book(.2,sign>0?970:1020);Check(stop.Orders.Count==1 && stop.Orders[0].Side==(sign>0?BuySell.Sell:BuySell.Buy),"exact 6 tick stop");
                var take=new Harness(new());take.Feed(0,1000,1000,false);take.Open(sign,1000);
                take.Book(.1,sign>0?1245:745);Check(take.Orders.Count==0,"49 ticks below fixed profit");
                take.Book(.2,sign>0?1250:740);Check(take.Orders.Count==1,"exact 50 tick take profit");
                var arm=new Harness(new());arm.Feed(0,1000,1000,false);arm.Open(sign,1000);
                arm.Book(.1,sign>0?1035:955);Check(arm.Strategy.GetSnapshot().Diagnostics["SarArmed"]=="False","7 ticks below activation");
                arm.Book(.2,sign>0?1040:950);Check(arm.Strategy.GetSnapshot().Diagnostics["SarArmed"]=="True","exact 8 ticks arms SAR");
                arm.Book(.3,sign>0?1010:980);Check(arm.Strategy.GetSnapshot().Diagnostics["SarArmed"]=="True" && arm.Orders.Count==0,"SAR activation remains latched; absent bar cannot trigger");
            }
        });
        Case("Reprice on terminal",()=>{
            var h=new Harness(new(){FastSeconds=2,SlowSeconds=3,History30=1});
            for(int i=0;i<30;i++)h.Feed(i,1000,1000,false);
            for(int i=30;i<55;i++)h.Feed(i,1000,1000+(i-30)*10,true);
            var old=h.Orders.Last();int before=h.Orders.Count;
            h.Strategy.CancelOrderRequested+=id=>h.Strategy.OnOrderLifecycle(new(){StrategyInstanceId="review",StrategyRunGeneration=1,ClientOrderId=id,CycleId=old.CycleId,State=OrderLifecycleState.Cancelled,HasFinalCumulativeFillWatermark=true,BrokerFinalCumulativeFilledQuantity=0,DeliveredFilledQuantity=0});
            h.Book(54.5,1005);
            Check(h.Orders.Count==before+1 && h.Orders.Last().Price==1005,"reprice waits for next second");
        });
        Case("Exit cannot reopen in same callback",()=>{
            var h=new Harness(new(){FastSeconds=2,SlowSeconds=3,History30=1});
            for(int i=0;i<30;i++)h.Feed(i,1000,1000,false);
            for(int i=30;i<55;i++)h.Feed(i,1000,1000+(i-30)*10,true);
            var entry=h.Orders.Last();h.Open(1,750);int before=h.Orders.Count;
            h.Strategy.CancelOrderRequested+=id=>h.Strategy.OnOrderLifecycle(new(){StrategyInstanceId="review",StrategyRunGeneration=1,ClientOrderId=id,CycleId=entry.CycleId,State=OrderLifecycleState.Cancelled,HasFinalCumulativeFillWatermark=true,BrokerFinalCumulativeFilledQuantity=1,DeliveredFilledQuantity=1});
            h.Strategy.OrderRequested+=order=>{
                if(order.Role is PaperOrderRole.EntryLong or PaperOrderRole.EntryShort)return;
                h.Open(-1,1000);h.Strategy.OnExecution(new(){StrategyInstanceId="review"});
                h.Strategy.OnOrderLifecycle(new(){StrategyInstanceId="review",StrategyRunGeneration=1,ClientOrderId=order.ClientOrderId,CycleId=order.CycleId,State=OrderLifecycleState.Filled,HasFinalCumulativeFillWatermark=true,BrokerFinalCumulativeFilledQuantity=1,DeliveredFilledQuantity=1});
            };
            h.Strategy.OnHeartbeat(h.Start.AddSeconds(55));
            Check(h.Orders.Skip(before).Count()==1 && h.Orders.Last().Role==PaperOrderRole.TakeProfit,"synchronous exit must not reopen using the same signal");
        });
        Case("Explicit close versus EOF",()=>{
            var h=new Harness(new());h.Feed(0,1000,1000,false);h.Open(1,1000);h.Strategy.RequestClose("使用者關閉");
            Check(h.Orders.Count==1 && h.Orders[0].Role==PaperOrderRole.Flatten,"explicit close submits exit");
            var eof=new Harness(new());eof.Feed(0,1000,1000,false);eof.Open(1,1000);eof.Strategy.Stop("歷史重播完成");Check(eof.Orders.Count==0,"EOF must not fabricate flatten");
        });
        Case("Adapter generation reset",()=>{
            var feed=new FakeQuoteFeed();using var adapter=new KgiStrategyMarketDataSource(feed);var events=new List<MarketEvent>();adapter.MarketEventReceived+=events.Add;
            adapter.Subscribe([new(){ProductId="225MC",Symbol="MC"}]);feed.Reset();
            Check(events.Single().Kind==MarketEventKind.Connection && events[0].Symbol=="MC" && events[0].ConnectionGeneration==1,"adapter must forward routed generation");
        });
        Case("Recovery resume reports rejection",()=>{
            var h=new Harness(new());var universe=new ResolvedStrategyUniverse{Tradable=new(){ProductId="225MC",Symbol="MC",TickSize=5,ContractMultiplier=10},References=[new(){ProductId="225M",Symbol="M"},new(){ProductId="225",Symbol="L"}]};
            using var runner=new StrategyInstanceRunner(h.Strategy,universe,new PaperPortfolioLedger(),new ManualClock(h.Start),0);
            runner.Pause();Check(runner.Resume(),"normal pause can resume");
            runner.OnMarketEvent(new(){Timestamp=h.Start,Kind=MarketEventKind.Connection,ProductId="225MC",Symbol="MC",ConnectionGeneration=1});
            runner.Pause();Check(!runner.Resume(),"UI must not report resumed while reconciliation gate blocks it");
        });
        if(failures.Count>0)throw new InvalidOperationException(string.Join("\n",failures));
    }
    internal sealed class Harness
    {
        public readonly DateTimeOffset Start;
        public readonly TradeFlowTrendStrategy Strategy;
        public readonly List<PaperOrderRequest> Orders=new();public readonly List<StrategyDecisionObservation> Decisions=new();
        private readonly PaperPortfolioLedger _ledger=new();private long _seq;
        private readonly ResolvedProduct[] _products=[new(){ProductId="225MC",Symbol="MC",TickSize=5,ContractMultiplier=10},new(){ProductId="225M",Symbol="M",TickSize=5},new(){ProductId="225",Symbol="L",TickSize=10}];
        public Harness(TradeFlowParameters p,DateTimeOffset? start=null)
        {
            Start=start??At("2026-09-25T08:00:00+08:00");foreach(var product in _products)_ledger.RegisterProduct("review",1,product);
            Strategy=new(new(){InstanceId="review",Parameters=p.ToDictionary()});
            Strategy.OrderRequested+=Orders.Add;Strategy.DecisionObserved+=Decisions.Add;
            Strategy.Start(new(new(){Tradable=_products[0],References=_products.Skip(1).ToArray()},new ManualClock(Start),_ledger,1));
        }
        public void Open(int quantity,decimal price)=>_ledger.ApplyExecution(new(){StrategyInstanceId="review",StrategyRunGeneration=1,Symbol="MC",Side=quantity>0?BuySell.Buy:BuySell.Sell,Price=price,Quantity=Math.Abs(quantity),Sequence=++_seq});
        public void Book(double second,decimal price)=>Strategy.OnMarketEvent(new(){Sequence=++_seq,Timestamp=Start.AddSeconds(second),Kind=MarketEventKind.Book,ProductId="225MC",Symbol="MC",BidPrices=[price],AskPrices=[price+10],BidQuantities=[10],AskQuantities=[10]});
        public void Feed(int second,decimal mc,decimal reference,bool up)
        {
            var at=Start.AddSeconds(second);
            foreach(var p in _products){var px=p.ProductId=="225MC"?mc:reference;Strategy.OnMarketEvent(new(){Sequence=++_seq,Timestamp=at,Kind=MarketEventKind.Book,ProductId=p.ProductId,Symbol=p.Symbol,BidPrices=[px],AskPrices=[px+10],BidQuantities=[10],AskQuantities=[10]});}
            foreach(var p in _products){var px=p.ProductId=="225MC"?mc:reference;Strategy.OnMarketEvent(new(){Sequence=++_seq,Timestamp=at.AddMilliseconds(1),Kind=MarketEventKind.Trade,ProductId=p.ProductId,Symbol=p.Symbol,LastPrice=up?px+10:px,TradeQuantity=1});}
        }
    }
    private sealed class FakeQuoteFeed:IStrategyQuoteFeed
    {
        public long ConnectionGeneration{get;private set;}
        public event Action<long>? OnConnectionGenerationChanged;
        public event Action<string,string,KGIOptionPricing.KGIQuoteService.MarketDataStruct>? OnResearchMarketEvent;
        public void Subscribe(string symbol){}
        public void Reset(){ConnectionGeneration++;OnConnectionGenerationChanged?.Invoke(ConnectionGeneration);}
    }
}
