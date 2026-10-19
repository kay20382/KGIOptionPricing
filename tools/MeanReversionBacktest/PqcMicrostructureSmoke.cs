using KGIOptionPricing.Strategies.PeriodicQueueCycle;
using KGIOptionPricing.StrategyFramework;

namespace MeanReversionBacktest;

internal static class PqcMicrostructureSmoke
{
    public static void Run()
    {
        // A constant mid-price with 100 contracts added to bid has OFI=100;
        // mean L1 depth=(100+150)/2=125, normalized OFI=0.8, QI=1/3.
        // Missing lead-direction integration incorrectly returns Neutral.
        var at=DateTimeOffset.Parse("2026-09-28T08:00:00+08:00");
        var values=new Dictionary<string,string>{{"DirectionWindowSeconds","2"},{"ObiConfirmSeconds","1"},
            {"MicrostructureEnabled","true"},{"MicrostructureLeadDirection","true"},
            {"MicrostructureWindowSeconds","2"},{"MinNormalizedOfi","0.1"},{"MinQueueImbalance","0.1"}};
        var p=PeriodicQueueCycleParameters.Parse(values);
        var window=new DirectionWindow(5,p);
        window.TryAdd(Book(at,1,100,100),at);
        window.TryAdd(Book(at.AddSeconds(1),2,200,100),at.AddSeconds(1));
        var result=window.Evaluate(at.AddSeconds(1),5,p);
        if(result.Direction!=CycleDirection.Up)
            throw new Exception($"Microstructure lead must accept bid replenishment before price trend: {result.Direction}/{result.PrimaryReasonCode}");
        var ofi=result.Thresholds.Single(x=>x.Name=="NormalizedOfi");
        if(ofi.Observed!=0.8m || !ofi.Passed)throw new Exception("Snapshot OFI normalization differs from hand-derived 0.8.");
        Console.WriteLine("PASS microstructure lead uses causal OFI instead of requiring a prior price move.");

        values["MicrostructureLeadDirection"]="false";
        p=PeriodicQueueCycleParameters.Parse(values);window=new DirectionWindow(5,p);
        window.TryAdd(Book(at,1,100,100),at);window.TryAdd(Book(at.AddSeconds(1),2,200,100),at.AddSeconds(1));
        if(window.Evaluate(at.AddSeconds(1),5,p).Direction!=CycleDirection.Neutral)
            throw new Exception("Confirm mode must not bypass the original trend signal.");
        Console.WriteLine("PASS confirmation mode retains existing price direction gates.");
        TestOfiBookTransitions(at);
        TestHealthIsNotBypassed(at);
        TestQueueAndResilienceFilters(at);
        TestFactoryKeepsBothLegs(at);
        TestReversalCancellationRestarts(at);
        TestReversalCancellationRestarts(at, synchronous:true);
    }

    private static void TestOfiBookTransitions(DateTimeOffset at)
    {
        foreach(var test in new[]{(bid:1000m,bidQ:200,askQ:100,want:.8m),(bid:1005m,bidQ:200,askQ:100,want:2.4m),
            (bid:995m,bidQ:100,askQ:200,want:-2.4m),(bid:1000m,bidQ:100,askQ:200,want:-.8m)})
        {
            var micro=new MicrostructureWindow(5,2,2);
            micro.TryAdd(Book(at,1,100,100),at);
            micro.TryAdd(Book(at.AddSeconds(1),2,test.bidQ,test.askQ,test.bid),at.AddSeconds(1));
            var before=micro.Evaluate(at.AddSeconds(1));
            if(!before.Ready || before.NormalizedOfi!=test.want)throw new Exception("Wrong OFI sign/price-transition/equality contribution.");
            // Exact duplicate, missing depth and out-of-order updates must not contaminate indicators.
            if(micro.TryAdd(Book(at.AddSeconds(1),2,1,999),at.AddSeconds(1)) ||
                micro.TryAdd(Book(at.AddSeconds(1),3,0,100),at.AddSeconds(1)) ||
                micro.TryAdd(Book(at,4,999,1),at.AddSeconds(1)) || micro.Evaluate(at.AddSeconds(1))!=before)
                throw new Exception("Rejected duplicate/missing/regressed books changed the microstructure state.");
            micro.TryAdd(Book(at.AddSeconds(5),5,999,100),at.AddSeconds(5));
            var after=micro.Evaluate(at.AddSeconds(5));
            if(after.Ready || after.NormalizedOfi!=0)throw new Exception("Cross-gap OFI was fabricated instead of rewarming.");
        }
        var resilience=new MicrostructureWindow(5,2,2);
        resilience.TryAdd(Book(at,1,200,100),at);
        resilience.TryAdd(Book(at.AddSeconds(1),2,100,100),at.AddSeconds(1));
        if(resilience.Evaluate(at.AddSeconds(1)).DepthResilience!=.5m)throw new Exception("Wrong top-three depth contraction ratio.");
        Console.WriteLine("PASS OFI signs, price changes, rejection invariance, gap reset and top-three depth resilience.");
    }

    private static void TestHealthIsNotBypassed(DateTimeOffset at)
    {
        var p=PeriodicQueueCycleParameters.Parse(new Dictionary<string,string>{{"DirectionWindowSeconds","2"},{"ObiConfirmSeconds","1"},
            {"MicrostructureEnabled","true"},{"MicrostructureLeadDirection","true"},{"MicrostructureWindowSeconds","2"}});
        var window=new DirectionWindow(5,p);
        window.TryAdd(Book(at,1,100,100),at);window.TryAdd(Book(at.AddSeconds(1),2,200,100),at.AddSeconds(1));
        window.TryAdd(Book(at.AddSeconds(2),3,500,1),at.AddSeconds(2));
        var result=window.Evaluate(at.AddSeconds(2),5,p);
        if(result.Direction!=CycleDirection.Neutral || result.PrimaryReasonCode!="InsufficientSideDepth")
            throw new Exception("Microstructure lead bypassed original ten-level side-depth gate.");
        try {PeriodicQueueCycleParameters.Parse(new Dictionary<string,string>{{"MicrostructureLeadDirection","true"}});
            throw new Exception("Invalid unenabled lead mode accepted");}catch(ArgumentException){}
        Console.WriteLine("PASS lead direction cannot bypass book health or invalid parameter combinations.");
    }

    private sealed class Clock:IStrategyClock {public DateTimeOffset Now{get;set;}}
    private static void TestQueueAndResilienceFilters(DateTimeOffset at)
    {
        foreach(var test in new[]{(bid0:100,ask0:200,bid1:120,ask1:200,qi:"0.1",res:"0",failed:"QueueImbalance"),
            (bid0:200,ask0:100,bid1:100,ask1:100,qi:"0",res:"0.8",failed:"DepthResilience")})
        {
            var p=PeriodicQueueCycleParameters.Parse(new Dictionary<string,string>{{"DirectionWindowSeconds","2"},{"ObiConfirmSeconds","1"},
                {"MicrostructureEnabled","true"},{"MicrostructureLeadDirection","true"},{"MicrostructureWindowSeconds","2"},
                {"MinNormalizedOfi","0.1"},{"MinQueueImbalance",test.qi},{"MinDepthResilience",test.res}});
            var window=new DirectionWindow(5,p);window.TryAdd(Book(at,1,test.bid0,test.ask0),at);
            window.TryAdd(Book(at.AddSeconds(1),2,test.bid1,test.ask1),at.AddSeconds(1));
            var result=window.Evaluate(at.AddSeconds(1),5,p);
            if(result.Direction!=CycleDirection.Neutral || result.PrimaryReasonCode!="MicrostructureFiltered" ||
                !result.OtherFailedReasonCodes.Contains(test.failed))throw new Exception("Queue/resilience filter failed to veto its named adverse condition.");
        }
        Console.WriteLine("PASS contradictory queue imbalance and depth collapse veto lead signals with typed reasons.");
    }
    private static void TestFactoryKeepsBothLegs(DateTimeOffset at)
    {
        var definition=new StrategyDefinition{InstanceId="micro-bilateral",StrategyType="PeriodicQueueCycle",Parameters=new Dictionary<string,string>
            {{"DirectionWindowSeconds","2"},{"ObiConfirmSeconds","1"},{"MicrostructureEnabled","true"},
            {"MicrostructureLeadDirection","true"},{"MicrostructureWindowSeconds","2"}}};
        var strategy=new PeriodicQueueCycleFactory().Create(definition);
        var orders=new List<PaperOrderRequest>();var observations=new List<StrategyDecisionObservation>();
        strategy.OrderRequested+=orders.Add;((IStrategyDecisionSource)strategy).DecisionObserved+=observations.Add;
        var universe=new ResolvedStrategyUniverse{Tradable=new ResolvedProduct{ProductId="225MC",Symbol="225MC-Z26",TickSize=5,ContractMultiplier=10}};
        var ledger=new KGIOptionPricing.StrategyFramework.Performance.PaperPortfolioLedger();
        ledger.RegisterProduct(definition.InstanceId,1,universe.Tradable);
        var clock=new Clock{Now=at};strategy.Start(new StrategyRuntimeContext(universe,clock,ledger));
        strategy.OnMarketEvent(Book(at,1,100,100));clock.Now=at.AddSeconds(1);strategy.OnMarketEvent(Book(clock.Now,2,200,100));
        if(orders.Count!=2 || orders.Count(x=>x.Role==PaperOrderRole.EntryLong)!=1 || orders.Count(x=>x.Role==PaperOrderRole.EntryShort)!=1 ||
            orders.Any(x=>x.StrategyInstanceId!=definition.InstanceId))throw new Exception("Lead signal did not submit exactly two correctly owned completion orders.");
        var accepted=observations.Single(x=>x.Decision==StrategyDecision.Accept);
        if(accepted.Features["normalizedOfi"]!=.8m || accepted.Features["weightedMidBiasTicks"] is not decimal bias || Math.Abs(bias-1m/6)>.00000001m ||
            accepted.Thresholds.Any(x=>x.Name=="UpNetMoveTicks"))throw new Exception("Typed accepted observation lacks effective microstructure features/gates.");
        Console.WriteLine("PASS real factory emits bilateral owned completion orders with microstructure decision evidence.");
    }

    private static void TestReversalCancellationRestarts(DateTimeOffset at,bool synchronous=false)
    {
        var definition=new StrategyDefinition{InstanceId="reverse-restart",StrategyType="PeriodicQueueCycle",Parameters=new Dictionary<string,string>
            {{"DirectionWindowSeconds","2"},{"ObiConfirmSeconds","1"},{"DirectionReverseSeconds","1"},
             {"CycleCooldownSeconds","20"},{"MicrostructureEnabled","true"},{"MicrostructureLeadDirection","true"},{"MicrostructureWindowSeconds","2"}}};
        var strategy=new PeriodicQueueCycleFactory().Create(definition);
        var managed=(IManagedOrderStrategy)strategy;
        var orders=new List<PaperOrderRequest>();var cancels=new List<string>();
        strategy.OrderRequested+=orders.Add;managed.CancelOrderRequested+=cancels.Add;
        var universe=new ResolvedStrategyUniverse{Tradable=new ResolvedProduct{ProductId="225MC",Symbol="225MC-Z26",TickSize=5,ContractMultiplier=10}};
        var ledger=new KGIOptionPricing.StrategyFramework.Performance.PaperPortfolioLedger();
        ledger.RegisterProduct(definition.InstanceId,1,universe.Tradable);
        var clock=new Clock{Now=at};strategy.Start(new StrategyRuntimeContext(universe,clock,ledger));
        if(synchronous)managed.CancelOrderRequested+=id=>managed.OnOrderLifecycle(new OrderLifecycleEvent{
            Timestamp=clock.Now,StrategyInstanceId=definition.InstanceId,ClientOrderId=id,State=OrderLifecycleState.Cancelled});
        void Feed(int second,long sequence,int bid,int ask){clock.Now=at.AddSeconds(second);strategy.OnMarketEvent(Book(clock.Now,sequence,bid,ask));}
        Feed(0,1,100,100);Feed(1,2,200,100);Feed(2,3,100,200);
        if(orders.Count!=2 || cancels.Count!=2)throw new Exception("Fixture must cancel exactly the unfilled reversal pair.");
        void Confirm(string id)=>managed.OnOrderLifecycle(new OrderLifecycleEvent{Timestamp=clock.Now,StrategyInstanceId=definition.InstanceId,
            ClientOrderId=id,State=OrderLifecycleState.Cancelled});
        if(!synchronous)
        {
            Confirm(cancels[0]);Feed(3,4,100,200);
            if(orders.Count!=2)throw new Exception("Reversal must await both cancel confirmations before another cycle.");
            Confirm(cancels[1]);
        }
        if(strategy.GetSnapshot().Reason!="CoolingDown")throw new Exception("Confirmed flat reversal must enter cooldown, not remain SignalReversedBeforeFill.");
        Feed(22,5,100,100);
        if(orders.Count!=2)throw new Exception("Flat reversal must respect the configured 20 second cooldown.");
        Feed(24,6,100,100);Feed(25,7,200,100);
        if(orders.Count!=4 || orders.Skip(2).Any(x=>orders.Take(2).Any(old=>old.ClientOrderId==x.ClientOrderId)))
            throw new Exception("Confirmed flat reversal must warm up and submit a fresh owned pair after cooldown.");
        Console.WriteLine("PASS reversal awaits both cancels, cools down and resumes a new bilateral cycle.");
    }

    internal static MarketEvent Book(DateTimeOffset at,long seq,int bidQty,int askQty,decimal bid=1000)=>new()
    {
        Timestamp=at,Sequence=seq,Kind=MarketEventKind.Book,Symbol="225MC-Z26",ProductId="225MC",
        LastPrice=bid,BidPrices=Enumerable.Range(0,10).Select(i=>bid-i*5).ToArray(),
        AskPrices=Enumerable.Range(0,10).Select(i=>bid+5+i*5).ToArray(),
        BidQuantities=Enumerable.Repeat(bidQty,10).ToArray(),AskQuantities=Enumerable.Repeat(askQty,10).ToArray()
    };
}
