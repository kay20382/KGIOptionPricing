using System.Text.Json;
using System.Security.Cryptography;
using KGIOptionPricing.Strategies.TradeFlowTrend;
using KGIOptionPricing.StrategyFramework;
using KGIOptionPricing.StrategyFramework.Execution;
using KGIOptionPricing.StrategyFramework.Performance;
using KGIOptionPricing.StrategyFramework.Storage;
using static TradeFlowTrendSmokeTests.TestSupport;
namespace TradeFlowTrendSmokeTests;
internal static class EndToEndTests
{
    public static void Run()
    {
        string root=Path.GetFullPath(Path.Combine(".testout","trade-flow-trend",Guid.NewGuid().ToString("N")));Directory.CreateDirectory(root);
        var start=At("2026-09-25T08:00:00+08:00");
        var products=new[]{new ResolvedProduct{ProductId="225MC",Symbol="MC",TickSize=5,ContractMultiplier=10},new ResolvedProduct{ProductId="225M",Symbol="M",TickSize=5,ContractMultiplier=100},new ResolvedProduct{ProductId="225",Symbol="L",TickSize=10,ContractMultiplier=1000}};
        var events=new List<MarketEvent>();long seq=0;
        for(int i=0;i<1840;i++)
        {
            var at=start.AddSeconds(i);decimal price=i<1800?40000:i<1815?40000+(i-1800)*10:39900;
            foreach(var p in products)events.Add(new(){Sequence=++seq,Timestamp=at,Kind=MarketEventKind.Book,ProductId=p.ProductId,Symbol=p.Symbol,BidPrices=[price],AskPrices=[price+(p.ProductId=="225MC"?5:10)],BidQuantities=[20],AskQuantities=[20]});
            foreach(var p in products)
            {
                events.Add(new(){Sequence=++seq,Timestamp=at.AddMilliseconds(1),Kind=MarketEventKind.Trade,ProductId=p.ProductId,Symbol=p.Symbol,LastPrice=i>=1800 && i<1815?price+10:price,TradeQuantity=1});
                if(i<1800)events.Add(new(){Sequence=++seq,Timestamp=at.AddMilliseconds(1),Kind=MarketEventKind.Trade,ProductId=p.ProductId,Symbol=p.Symbol,LastPrice=price+10,TradeQuantity=1});
            }
        }
        string input=Path.Combine(root,"synthetic-market-events.jsonl");var json=new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase};
        File.WriteAllLines(input,events.Select(e=>JsonSerializer.Serialize(e,json)));
        string inputHash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input)));
        var summaries=new List<RunSummary>();
        foreach(var name in new[]{"baseline","candidate","repeat"})
        {
            var p=name=="candidate"?new TradeFlowParameters{FastSeconds=20,SlowSeconds=80}:new TradeFlowParameters();
            var clock=new ManualClock(start);var strategy=(TradeFlowTrendStrategy)new TradeFlowTrendFactory().Create(new(){InstanceId="tf",StrategyType="TradeFlowTrend",Parameters=p.ToDictionary()});
            var universe=new ResolvedStrategyUniverse{Tradable=products[0],References=products.Skip(1).ToArray()};
            var broker=new CrossPricePaperExecutionService(36,clock,false,true);var ledger=new PaperPortfolioLedger();
            var folder=Path.Combine(root,name);using var recorder=new SimulationRunRecorder(folder);
            var ordered=new SortedDictionary<string,string>(StringComparer.Ordinal);foreach(var pair in p.ToDictionary())ordered[pair.Key]=pair.Value;
            var manifest=new RunManifest{RunId=name,StrategyType="TradeFlowTrend",InstanceId="tf",Mode=StrategyRunMode.Simulation,StartedAt=start,EndedAt=events[^1].Timestamp,
                EffectiveParameters=ordered,NormalizedParameters=JsonSerializer.Serialize(ordered),ParameterSha256=TradeFlowParameterStore.Hash(p),CommissionPerContract=36,
                InputArchive=new(){AbsolutePath=input,Sha256=inputHash,SizeBytes=new FileInfo(input).Length,FirstEventAt=start,LastEventAt=events[^1].Timestamp},
                Universe=products.Select(x=>new RunProductIdentity(x.ProductId,x.Symbol,"synthetic",x.TickSize,x.ContractMultiplier)).ToArray(),
                ExecutionAssumptions=new Dictionary<string,string>{{"fillModel","synthetic; ROD strict trade cross; IOC known contra size once at limit"},{"latencyModel","synchronous synthetic"},{"sessionHours","Taiwan day/night clock windows; trading day not checked"}}};
            recorder.RecordManifest(manifest);
            using(var runner=new StrategyInstanceRunner(strategy,universe,ledger,clock,36,broker))
            {
                runner.DecisionObserved+=recorder.Record;runner.OrderLifecycleOccurred+=recorder.Record;runner.ExecutionOccurred+=recorder.Record;runner.SnapshotChanged+=recorder.Record;
                int submissions=0;strategy.OrderRequested+=_=>submissions++;
                foreach(var e in events)
                {clock.Advance(e.Timestamp);runner.OnMarketEvent(e);if(e.Timestamp<start.AddSeconds(1800))Check(submissions==0,"default 30 minute history gate");}
                runner.Pause();runner.Pulse(start.AddSeconds(1840));recorder.Record(runner.GetSnapshot());
                Check(submissions>0,"post-warmup orders");Check(ledger.GetPosition("tf","MC").CompletedTrades>0,"stop closes actual paper position");
                Check(ledger.GetPosition("tf","MC").NetQuantity==0,"no over-close or stranded synthetic position");
            }
            recorder.FinalizeSummary(events[^1].Timestamp);
            summaries.Add(JsonSerializer.Deserialize<RunSummary>(File.ReadAllText(Path.Combine(folder,"run-summary.json")),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!);
        }
        Check(summaries[0].NetPnl==summaries[2].NetPnl && summaries[0].CompletedTrades==summaries[2].CompletedTrades,"repeat same data deterministic");
        Console.WriteLine("EVIDENCE="+root);
    }
}
