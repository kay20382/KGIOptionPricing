using System.Security.Cryptography;
using System.Text.Json;
using KGIOptionPricing.Strategies.TradeFlowTrend;
using KGIOptionPricing.StrategyFramework;
using KGIOptionPricing.StrategyFramework.MarketData;
using KGIOptionPricing.StrategyFramework.Execution;
using KGIOptionPricing.StrategyFramework.Performance;
using KGIOptionPricing.StrategyFramework.Storage;
using static TradeFlowTrendSmokeTests.TestSupport;
namespace TradeFlowTrendSmokeTests;
internal static class RealReplayTests
{
    public static void Run()
    {
        string input=Environment.GetEnvironmentVariable("TRADE_FLOW_REPLAY_INPUT")??throw new ArgumentException("TRADE_FLOW_REPLAY_INPUT required");
        var events=File.ReadLines(input).Select(line=>MarketDataArchiveCodec.TryDeserialize(line,out var e)?e:null).Where(e=>e!=null).Cast<MarketEvent>().ToArray();
        Check(events.Length>0,"real archive cannot be empty");
        var symbols=events.GroupBy(e=>e.ProductId).ToDictionary(g=>g.Key,g=>g.First().Symbol);
        Check(new[]{"225","225M","225MC"}.All(symbols.ContainsKey),"all required products");
        var universe=new ResolvedStrategyUniverse{Tradable=new(){ProductId="225MC",Symbol=symbols["225MC"],TickSize=5,ContractMultiplier=10},
            References=[new(){ProductId="225M",Symbol=symbols["225M"],TickSize=5,ContractMultiplier=100},new(){ProductId="225",Symbol=symbols["225"],TickSize=10,ContractMultiplier=1000}]};
        string root=Path.Combine(Path.GetDirectoryName(input)!,"runs-"+Guid.NewGuid().ToString("N"));
        string hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input)));
        foreach(var name in new[]{"baseline","candidate"})
        {
            var p=name=="baseline"?new TradeFlowParameters():new TradeFlowParameters{FastSeconds=20,SlowSeconds=90};
            var clock=new ManualClock(events[0].Timestamp);var strategy=new TradeFlowTrendStrategy(new(){InstanceId="real-flow",StrategyType="TradeFlowTrend",Parameters=p.ToDictionary()});
            var ledger=new PaperPortfolioLedger();var broker=new CrossPricePaperExecutionService(36,clock,false,true);
            using var recorder=new SimulationRunRecorder(Path.Combine(root,name));var parameters=new SortedDictionary<string,string>(StringComparer.Ordinal);foreach(var kv in p.ToDictionary())parameters[kv.Key]=kv.Value;
            recorder.RecordManifest(new(){RunId=name,StrategyType="TradeFlowTrend",InstanceId="real-flow",Mode=StrategyRunMode.HistoricalReplay,StartedAt=events[0].Timestamp,EndedAt=events[^1].Timestamp,
                RawParameters=p.ToDictionary(),EffectiveParameters=parameters,NormalizedParameters=JsonSerializer.Serialize(parameters),ParameterSha256=TradeFlowParameterStore.Hash(p),CommissionPerContract=36,
                InputArchive=new(){AbsolutePath=Path.GetFullPath(input),Sha256=hash,SizeBytes=new FileInfo(input).Length,FirstEventAt=events[0].Timestamp,LastEventAt=events[^1].Timestamp},
                Universe=universe.AllProducts.Select(x=>new RunProductIdentity(x.ProductId,x.Symbol,"archive",x.TickSize,x.ContractMultiplier)).ToArray(),
                ExecutionAssumptions=new Dictionary<string,string>{{"fillModel","strict ROD / one-use displayed depth limit IOC"},{"sessionHours","configured Taiwan day/night clock windows, no trading-day check"},{"latencyModel","synchronous replay"}},
                MissingProvenanceFields=new[]{"ExchangeSequence","HistoricalConnectionGeneration"}});
            if(name=="candidate")recorder.Record(new ParameterExperimentRecord{
                ExperimentId="real-feature-readiness",BaselineRunId="baseline",CandidateRunId=name,StrategyType="TradeFlowTrend",CodeVersion="934a7d8 plus uncommitted implementation",
                CandidateParameters=new Dictionary<string,string>{{"FastSeconds","20"},{"SlowSeconds","90"}},ChangedParameters=new Dictionary<string,string>{{"FastSeconds","20"},{"SlowSeconds","90"}},
                ParameterDefinitions=new Dictionary<string,ParameterSearchDefinition>{{"FastSeconds",new("int","second",1,3600,20,40,10)},{"SlowSeconds",new("int","second",1,3600,90,180,30)}},
                ProductMapping=universe.AllProducts.ToDictionary(x=>x.ProductId,x=>x.Symbol),InputArchiveSha256=hash,SearchMethod="readiness-pair",DataSplitId="40-minute-input-insufficient-after-warmup",
                PrimaryObjective="NetPnl",SecondaryObjectives=["CompletedTrades","MaximumDrawdown"],Guardrails=["每個 split 至少 30 筆完整交易","不可降低資料品質門檻換取績效","無未決對帳"],
                GuardrailValues=new Dictionary<string,decimal?>{{"minimumCompletedTrades",30},{"maximumDrawdown",3000},{"minimumStress2",0}},
                ExecutionAssumptions=new Dictionary<string,string>{{"fillModel","strict ROD / one-use displayed depth limit IOC"},{"latencyModel","synchronous replay"}},
                Status=ParameterExperimentStatus.Incomplete,ReasonCode="OutOfSampleMissing",
                UnavailableReasons=new Dictionary<string,string>{{"Results","Forty minute archive cannot support split research"},{"AdverseSlippageStress","Insufficient completed trades"}}
            });
            using(var runner=new StrategyInstanceRunner(strategy,universe,ledger,clock,36,broker))
            {
                runner.SnapshotChanged+=recorder.Record;runner.ExecutionOccurred+=recorder.Record;runner.OrderLifecycleOccurred+=recorder.Record;runner.DecisionObserved+=recorder.Record;
                foreach(var e in events){clock.Advance(e.Timestamp);runner.OnMarketEvent(e);}
                recorder.Record(runner.GetSnapshot());
                Check(runner.GetSnapshot().Features.ContainsKey("225M.30.Real"),"real feature output");
            }
            recorder.FinalizeSummary(events[^1].Timestamp);
        }
        Console.WriteLine("REAL_EVIDENCE="+root);
    }
}
