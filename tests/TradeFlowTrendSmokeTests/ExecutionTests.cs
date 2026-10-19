using KGIOptionPricing.Strategies.TradeFlowTrend;
using KGIOptionPricing.StrategyFramework;
using KGIOptionPricing.StrategyFramework.Execution;
using KGIOptionPricing.OrderManagement.Core;
using static TradeFlowTrendSmokeTests.TestSupport;
namespace TradeFlowTrendSmokeTests;
internal static class ExecutionTests
{
    public static void Run()
    {
        var now=At("2026-09-25T08:00:00+08:00");
        var r=new PaperOrderRequest{StrategyInstanceId="tf",ClientOrderId="ioc1",CycleId="c1",StrategyRunGeneration=1,Symbol="MC",Type=PaperOrderType.BrokerLimit,Role=PaperOrderRole.Flatten,Side=BuySell.Buy,Price=150,Quantity=3,TimeInForce=TimeInForce.IOC};
        var mapped=LiveStrategyOrderMapper.CreateBrokerLimit(r,new InstrumentKey{Symbol="225MC"},"B","A","real",capabilities:new(3,true));
        Check(mapped.Price==150 && mapped.Quantity==3 && mapped.TimeInForce==TimeInForce.IOC,"exact limit IOC mapping");
        Reject(()=>LiveStrategyOrderMapper.CreateBrokerLimit(r,new InstrumentKey{Symbol="225MC"},"B","A","real"));
        var broker=new CrossPricePaperExecutionService(2,new ManualClock(now),false,true,TimeSpan.FromSeconds(2));
        var fills=new List<ExecutionEvent>();var states=new List<OrderLifecycleEvent>();broker.Filled+=fills.Add;broker.OrderLifecycleChanged+=states.Add;
        var book=new MarketEvent{Kind=MarketEventKind.Book,Symbol="MC",Sequence=1,Timestamp=now,BidPrices=[100],AskPrices=[105],BidQuantities=[1],AskQuantities=[2]};
        broker.OnMarketEvent(book);broker.Submit(r,now);
        Check(fills.Sum(x=>x.Quantity)==2 && fills.Sum(x=>x.Commission)==4 && fills[0].Price==150,"IOC depth/cost");
        Check(broker.GetWorkingOrders("tf").Count==0 && states.Last().BrokerFinalCumulativeFilledQuantity==2,"IOC terminal");
        broker.Submit(r with{ClientOrderId="ioc2"},now);Check(fills.Count==1 && states.Last().ReasonCode=="IocAwaitFreshBook","no recycled liquidity");
        broker.OnMarketEvent(book with{Sequence=2});broker.Submit(r with{ClientOrderId="ioc3",Price=105},now);Check(fills.Count==1,"touch not fill");
        broker.Submit(r with{ClientOrderId="ioc4"},now.AddSeconds(3));Check(fills.Count==1,"stale no fill");
        var p=new TradeFlowParameters();Check(TradeFlowExitPricing.Plan(BuySell.Buy,100,105,5,p)==new ExitPricePlan(150,TimeInForce.IOC),"narrow");
        Check(TradeFlowExitPricing.Plan(BuySell.Buy,100,110,5,p)==new ExitPricePlan(105,TimeInForce.ROD),"exact 2");
        Check(TradeFlowExitPricing.Plan(BuySell.Sell,100,110,5,p)==new ExitPricePlan(105,TimeInForce.ROD),"sell");
        var c=new TradeFlowOrderCoordinator("tf",1,new ResolvedProduct{Symbol="MC",TickSize=5},p with{Quantity=3});
        var b=new BookReference("MC",now,1,0,100,110,5,5);var pos=new PositionSnapshot{StrategyInstanceId="tf",Symbol="MC"};
        var entry=((SubmitFlowOrder)c.Evaluate(now,b,pos,1,true,TradeFlowExitReason.None).Single()).Request;c.OnSubmitted(entry);
        Check(c.Evaluate(now,b with{Bid=95},pos,1,true,0).Count==0,"no down chase");
        Check(c.Evaluate(now,b with{Bid=105,Ask=115},pos,1,true,0).Single() is CancelFlowOrder,"up chase cancel");
        var terminal=new OrderLifecycleEvent{StrategyInstanceId="tf",StrategyRunGeneration=1,ClientOrderId=entry.ClientOrderId,CycleId=entry.CycleId,State=OrderLifecycleState.Cancelled,HasFinalCumulativeFillWatermark=true,BrokerFinalCumulativeFilledQuantity=2,DeliveredFilledQuantity=1};
        c.OnLifecycle(terminal);pos=pos with{NetQuantity=1,AveragePrice=100};
        Check(c.Evaluate(now,b,pos,0,false,TradeFlowExitReason.StopLoss).Count==0,"final watermark gap blocks replacement");
        c.OnLifecycle(terminal with{DeliveredFilledQuantity=2});pos=pos with{NetQuantity=2};
        var exit=((SubmitFlowOrder)c.Evaluate(now,b,pos,0,false,0).Single()).Request;Check(exit.Quantity==2 && exit.Side==BuySell.Sell && exit.Role==PaperOrderRole.StopLoss,"remaining only");
    }
}
internal sealed class ManualClock(DateTimeOffset initial):IEventDrivenStrategyClock
{public DateTimeOffset Now{get;private set;}=initial;public void Advance(DateTimeOffset timestamp)=>Now=timestamp;}
