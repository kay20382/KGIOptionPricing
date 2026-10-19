using KGIOptionPricing.StrategyFramework;
using KGIOptionPricing.StrategyFramework.Execution;
using KGIOptionPricing.Strategies.PeriodicQueueCycle;
using KGIOptionPricing.OrderManagement.Core;

namespace MeanReversionBacktest;

internal static class PqcSessionRegression
{
    sealed class Clock: IEventDrivenStrategyClock {public DateTimeOffset Now{get;private set;} public void Advance(DateTimeOffset t)=>Now=t;}
    sealed class Source: IMarketEventSource
    {public event Action<MarketEvent>? MarketEventReceived;public void Emit(MarketEvent e)=>MarketEventReceived?.Invoke(e);public void Subscribe(IEnumerable<ResolvedProduct> p){} public void Dispose(){}}
    public static void Run(bool existingStop=false)
    {
        var clock=new Clock();var at=DateTimeOffset.Parse("2026-10-05T07:45:00+08:00");clock.Advance(at);
        using var source=new Source();using var host=new StrategyHost(source,clock);
        var broker=new CrossPricePaperExecutionService(36,clock,fillMarketOnBook:true);
        var universe=new ResolvedStrategyUniverse{Tradable=new ResolvedProduct{ProductId="225MC",Symbol="225MC-Z26",TickSize=5,ContractMultiplier=10}};
        var parameters=new Dictionary<string,string>{{"DirectionWindowSeconds","2"},{"ObiConfirmSeconds","1"},{"MinMoveTicks","0.5"},{"MinDepthObi","0"},{"MaxVolatilityTicks","20"}};
        var runner=host.Add(new PeriodicQueueCycleFactory().Create(new StrategyDefinition{InstanceId="session-reentrant",StrategyType="PeriodicQueueCycle",Parameters=parameters}),universe,36,broker);
        long seq=0;
        MarketEvent Book(DateTimeOffset t,decimal bid)=>new(){ProductId="225MC",Symbol="225MC-Z26",Kind=MarketEventKind.Book,Timestamp=t,Sequence=++seq,LastPrice=bid,
            BidPrices=Enumerable.Range(0,10).Select(i=>bid-i*5).ToArray(),AskPrices=Enumerable.Range(0,10).Select(i=>bid+5+i*5).ToArray(),
            BidQuantities=Enumerable.Repeat(100,10).ToArray(),AskQuantities=Enumerable.Repeat(100,10).ToArray()};
        for(int i=0;i<8 && broker.GetWorkingOrders(runner.InstanceId).Count==0;i++)
        {clock.Advance(at.AddSeconds(i));source.Emit(Book(clock.Now,1000+5*i));}
        var buy=broker.GetWorkingOrders(runner.InstanceId).Single(o=>o.Side==BuySell.Buy);
        clock.Advance(clock.Now.AddMilliseconds(100));source.Emit(new MarketEvent{ProductId="225MC",Symbol="225MC-Z26",Kind=MarketEventKind.Trade,Timestamp=clock.Now,Sequence=++seq,LastPrice=buy.Price-5,TradeQuantity=1});
        if(runner.GetSnapshot().Position!=1)throw new Exception("Regression fixture did not enter one leg.");
        if(existingStop)
        {
            clock.Advance(clock.Now.AddSeconds(1));source.Emit(Book(clock.Now,buy.Price-35));
            if(!broker.GetWorkingOrders(runner.InstanceId).Any(o=>o.Role==PaperOrderRole.StopLoss))
                throw new Exception("Regression fixture did not create old pending stop exit.");
        }
        var close=DateTimeOffset.Parse("2026-10-05T14:25:00+08:00");host.Pulse(close);
        clock.Advance(close.AddMilliseconds(100));source.Emit(Book(clock.Now,1000));
        if(runner.GetSnapshot().Position!=0 || broker.GetWorkingOrders(runner.InstanceId).Count!=0)
            throw new Exception("Session close cancelled its reentrantly created flatten instead of allowing it to close.");
        clock.Advance(DateTimeOffset.Parse("2026-10-05T16:00:00+08:00"));host.Pulse(clock.Now);
        if(runner.GetSnapshot().State!=StrategyLifecycleState.WarmingUp)throw new Exception("Next session did not reset after flat.");
        Console.WriteLine($"PASS synchronous session cancel preserves new flatten and reset; existing stop={existingStop}.");
    }
}
