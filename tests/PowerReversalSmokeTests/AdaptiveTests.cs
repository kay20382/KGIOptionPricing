using KGIOptionPricing.Strategies.BuySellPower;
using KGIOptionPricing.Strategies.PowerReversal;
using KGIOptionPricing.StrategyFramework;
using KGIOptionPricing.StrategyFramework.Storage;
using System.Text.Json;

static class AdaptiveTests
{
    public static void Run()
    {
        void Check(bool ok,string message){if(!ok)throw new Exception(message);}
        Check(PowerReversalParameters.Parse(new Dictionary<string,string>())["AdaptiveExitMode"]==0,"old profiles leave adaptive exits disabled");
        var p=PowerReversalParameters.Parse(new Dictionary<string,string>{{"SignalMode","1"},{"AdaptiveExitMode","1"}});
        var t=DateTimeOffset.Now;
        var s=new PowerSnapshot(11,0,0,100,100,50,50,true,true,"就緒",null,Array.Empty<MovementSegment>(),83,117,5,5);
        var buy=AdaptiveExitDistances.Calculate(s,1,5,p);
        var sell=AdaptiveExitDistances.Calculate(s,-1,5,p);
        Check(buy!.Value.Stop==60 && buy.Value.Target==80,"adaptive long: opposing down average rounded up; up target rounded down");
        Check(sell!.Value.Stop==45 && sell.Value.Target==115,"adaptive short swaps completed movement averages");
        Check(AdaptiveExitDistances.Calculate(s with{DownSamples=4},1,5,p)==null,"incomplete movement samples block adaptive entry");
        var capped=AdaptiveExitDistances.Calculate(s with{AverageUpPoints=9999,AverageDownPoints=9999},1,5,p);
        Check(capped!.Value.Stop==60 && capped.Value.Target==200,"adaptive caps constrain each frozen entry plan");
        var minimum=AdaptiveExitDistances.Calculate(s with{AverageDownPoints=1},1,5,p);
        Check(minimum!.Value.Stop==10,"adaptive stop minimum is two tradable ticks");
        Check(AdaptiveExitDistances.Calculate(s with{AverageUpPoints=4},1,5,p)==null,"sub-tick target cannot create an invalid plan");
        bool invalid=false;try{PowerReversalParameters.Parse(new Dictionary<string,string>{{"AdaptiveExitMode","1"}});}catch(ArgumentException){invalid=true;}
        Check(invalid,"adaptive exit requires continuous signal mode");
        string root=Path.Combine(Path.GetTempPath(),"KGI-AdaptiveMetricTest-"+Guid.NewGuid().ToString("N"));
        using(var recorder=new SimulationRunRecorder(root)){
            var snapshot=new StrategySnapshot{InstanceId="risk",TradableSymbol="MC",UpdatedAt=t,NetPnl=50};
            recorder.ObservePerformance(snapshot);
            recorder.ObservePerformance(snapshot with{NetPnl=-300});
            recorder.Record(snapshot with{NetPnl=-100});
            recorder.FinalizeSummary(t);
        }
        using var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"run-summary.json")));
        Check(json.RootElement.GetProperty("maximumDrawdown").GetDecimal()==350,"intraday transient drawdown must not disappear between recorded snapshots");
        Check(json.RootElement.GetProperty("maximumIntradayLoss").GetDecimal()==300,"maximum intraday loss measured against initial zero");
        Check(json.RootElement.GetProperty("maximumTradeLoss").ValueKind==JsonValueKind.Null,"no completed trade has no worst trade loss");
        string fillsRoot=Path.Combine(root,"fills");
        using(var recorder=new SimulationRunRecorder(fillsRoot)){
            recorder.PrepareManifest(new RunManifest{RunId="risk",StrategyType="PowerReversal",InstanceId="risk",StartedAt=t,CommissionPerContract=36,Universe=new[]{new RunProductIdentity("225MC","MC","synthetic",5,10)}});
            for(int i=0;i<4;i++)recorder.Record(new ExecutionEvent{Timestamp=t.AddSeconds(i),StrategyInstanceId="risk",Symbol="MC",CycleId="cycle"+(i/2),ClientOrderId="order"+i,ExecutionId="fill"+i,Quantity=1,Commission=36,Role=i%2==0?PaperOrderRole.EntryLong:PaperOrderRole.Flatten,Side=i%2==0?KGIOptionPricing.OrderManagement.Core.BuySell.Buy:KGIOptionPricing.OrderManagement.Core.BuySell.Sell,Price=i==1?90:i==3?110:100});
            recorder.Record(new StrategySnapshot{InstanceId="risk",TradableSymbol="MC",UpdatedAt=t.AddSeconds(4),NetPnl=-144,CompletedTrades=2});
            recorder.FinalizeSummary(t.AddSeconds(4));
        }
        using var fillsJson=JsonDocument.Parse(File.ReadAllText(Path.Combine(fillsRoot,"run-summary.json")));
        Check(fillsJson.RootElement.GetProperty("maximumTradeLoss").GetDecimal()==172,"worst trade includes both 36 JPY commissions");
        Console.WriteLine("PASS adaptive exit mapping, rounding, caps, sample gate and intraday loss metrics");
    }
}
