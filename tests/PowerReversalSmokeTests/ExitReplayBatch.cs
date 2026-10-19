using System.Security.Cryptography;
using System.Text.Json;
using KGIOptionPricing.Strategies.PowerReversal;
using KGIOptionPricing.StrategyFramework;
using KGIOptionPricing.StrategyFramework.Storage;

/// <summary>Decode one fixed input once; each candidate uses an isolated common-host instance.</summary>
static class ExitReplayBatch
{
    sealed class Clock:IStrategyClock{public DateTimeOffset Now{get;set;}}
    sealed class Run:IDisposable
    {
        public required string Name,Directory;
        public required PowerReversalStrategy Strategy;
        public required SimulationRunRecorder Recorder;
        public required StrategyHost Host;
        public required RunManifest Manifest;
        public StrategyInstanceRunner Runner=null!;
        public DateTimeOffset? LastDecision,LastSnapshot;
        public string LastReason="";
        public Dictionary<string,long> Reasons=new(),Failures=new();
        public long Decisions,ParityChecks;
        public void Dispose(){Host.Dispose();Recorder.Dispose();}
    }
    public static int Execute(string[] args)
    {
        var json=new JsonSerializerOptions(JsonSerializerDefaults.Web){WriteIndented=true};
        var coverage=JsonDocument.Parse(File.ReadAllText(args[4])).RootElement;
        var input=Path.GetFullPath(args[1]);var output=Path.GetFullPath(args[2]);
        var profiles=JsonSerializer.Deserialize<Dictionary<string,Dictionary<string,string>>>(File.ReadAllText(args[3]))!;
        var start=DateTimeOffset.Parse(coverage.GetProperty("requestedStart").GetString()!);
        var end=DateTimeOffset.Parse(coverage.GetProperty("requestedEnd").GetString()!);
        using var inputFile=File.OpenRead(input);var hash=Convert.ToHexString(SHA256.HashData(inputFile));
        if(!hash.Equals(coverage.GetProperty("inputSha256").GetString(),StringComparison.OrdinalIgnoreCase))throw new Exception("Input SHA mismatch");
        var universe=new ResolvedStrategyUniverse{Tradable=new(){ProductId="225MC",Symbol="225MC-Z26",ContractMonth="202612",TickSize=5,ContractMultiplier=10},References=new[]{new ResolvedProduct{ProductId="225M",Symbol="225M-Z26",ContractMonth="202612",TickSize=5,ContractMultiplier=100},new ResolvedProduct{ProductId="225",Symbol="225-Z26",ContractMonth="202612",TickSize=10,ContractMultiplier=1000}}};
        var clock=new Clock{Now=start};using var source=new ArchiveReplayMarketDataSource(input);
        var runs=new Dictionary<string,Run>();
        Action<ExecutionEvent> onExecution=e=>runs[e.StrategyInstanceId].Recorder.Record(e);
        Action<OrderLifecycleEvent> onLifecycle=e=>runs[e.StrategyInstanceId].Recorder.Record(e);
        Action<StrategyDecisionObservation> onDecision=o=>{
            var r=runs[o.StrategyInstanceId];r.Decisions++;r.Reasons[o.PrimaryReasonCode]=r.Reasons.GetValueOrDefault(o.PrimaryReasonCode)+1;
            foreach(var t in o.Thresholds.Where(t=>!t.Passed))r.Failures[t.Name]=r.Failures.GetValueOrDefault(t.Name)+1;
            if(r.LastDecision==null || o.Timestamp>=r.LastDecision.Value.AddSeconds(1) || r.LastReason!=o.PrimaryReasonCode || o.Decision==StrategyDecision.Accept)
            {r.Recorder.Record(o);r.LastDecision=o.Timestamp;r.LastReason=o.PrimaryReasonCode;}
        };
        Action<StrategySnapshot> onSnapshot=s=>{
            var r=runs[s.InstanceId];string reason=s.Diagnostics.GetValueOrDefault("PrimaryReasonCode","");
            if(r.LastSnapshot==null || s.UpdatedAt>=r.LastSnapshot.Value.AddSeconds(10) || reason is "ENTRY_FILLED" or "EXIT_FILLED")
            {r.Recorder.Record(s with{Diagnostics=s.Diagnostics.Where(x=>!x.Key.EndsWith(".History")).ToDictionary(x=>x.Key,x=>x.Value)});r.LastSnapshot=s.UpdatedAt;}
        };
        foreach(var (name,profile) in profiles)
        {
            string directory=Path.Combine(output,name);System.IO.Directory.CreateDirectory(directory);
            var parameters=PowerReversalParameters.Parse(profile).ToDictionary();var values=new Dictionary<string,string>(parameters){["HistoryRoot"]=Path.Combine(directory,"history")};
            string id="exit-replay-"+name;var strategy=new PowerReversalStrategy(new(){InstanceId=id,StrategyType="PowerReversal",Parameters=values});
            string normalized=JsonSerializer.Serialize(new SortedDictionary<string,string>(parameters,StringComparer.Ordinal));
            var manifest=new RunManifest{RunId=name,InstanceId=id,StrategyType="PowerReversal",Mode=StrategyRunMode.HistoricalReplay,StartedAt=start,EndedAt=end,EffectiveParameters=parameters,NormalizedParameters=normalized,ParameterSha256=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalized))),InputArchive=new(){AbsolutePath=input,Sha256=hash,SizeBytes=inputFile.Length},Universe=universe.AllProducts.Select(p=>new RunProductIdentity(p.ProductId,p.Symbol,"202612",p.TickSize,p.ContractMultiplier)).ToArray(),CommissionPerContract=36,CodeVersion=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(PowerReversalStrategy).Assembly.Location))),ExecutionAssumptions=new Dictionary<string,string>{{"fillModel","market-next-trade; conditional-strict-cross"},{"latencyModel","not-configured"},{"slippageModel","not-configured"},{"dailyLossCap","not-enabled-in-research"}}};
            var recorder=new SimulationRunRecorder(directory);recorder.PrepareManifest(manifest);
            var host=new StrategyHost(source,clock);host.ExecutionOccurred+=onExecution;host.OrderLifecycleOccurred+=onLifecycle;host.DecisionObserved+=onDecision;host.SnapshotChanged+=onSnapshot;
            var r=new Run{Name=name,Directory=directory,Strategy=strategy,Recorder=recorder,Manifest=manifest,Host=host};runs.Add(id,r);
            r.Runner=host.Add(strategy,universe,36);
        }
        DateTimeOffset? last=null;long count=0,marks=0;
        source.BeforeMarketEvent+=at=>{
            if(at<start || at>end)throw new Exception("Input outside fixed interval");
            if(last.HasValue && at>=last.Value)
                foreach(var r in runs.Values)
                    foreach(var pulse in r.Strategy.GetPulseTimes(last.Value,at))r.Runner.Pulse(pulse);
            clock.Now=at;last=at;count++;
            if(count%500000==0)Console.WriteLine($"PROGRESS {count} {at:O}");
        };
        source.MarketEventReceived+=e=>{
            if(e.Symbol!=universe.Tradable.Symbol || e.LastPrice<=0)return;
            marks++;
            foreach(var (id,r) in runs){
                var p=r.Host.Ledger.GetPosition(id,r.Runner.StrategyRunGeneration,universe.Tradable.Symbol);
                if(marks%10000==0){var full=r.Runner.GetSnapshot();if(full.NetPnl!=p.NetPnl || full.Position!=p.NetQuantity || full.CompletedTrades!=p.CompletedTrades)throw new Exception("Common ledger parity failure");r.ParityChecks++;}
                r.Recorder.ObservePerformance(new(){InstanceId=id,StrategyType="PowerReversal",TradableSymbol=universe.Tradable.Symbol,StrategyRunGeneration=r.Runner.StrategyRunGeneration,UpdatedAt=e.Timestamp,Position=p.NetQuantity,RealizedPnl=p.RealizedPnl,UnrealizedPnl=p.UnrealizedPnl,NetPnl=p.NetPnl,CompletedTrades=p.CompletedTrades});
            }
        };
        try
        {
            source.ReplayAsync().GetAwaiter().GetResult();clock.Now=end;foreach(var r in runs.Values){r.Host.Pulse(end);r.Host.StopAll("研究回測結束");}
            if(count!=coverage.GetProperty("rows").GetInt64())throw new Exception("Input event count mismatch");
            foreach(var r in runs.Values){
                var s=r.Runner.GetSnapshot();r.Recorder.Record(s);r.Recorder.FinalizeManifest(r.Manifest);r.Recorder.FinalizeSummary(end);
                using var summary=JsonDocument.Parse(File.ReadAllText(Path.Combine(r.Directory,"run-summary.json")));var j=summary.RootElement;
                if(j.GetProperty("completedTrades").GetInt32()!=s.CompletedTrades || j.GetProperty("netPnl").GetDecimal()!=s.NetPnl || s.Position!=0 || s.UnrealizedPnl!=0)throw new Exception("Final common ledger parity/flatness failure");
                File.WriteAllText(Path.Combine(r.Directory,"comparison.json"),JsonSerializer.Serialize(new{candidate=r.Name,count,marks,r.ParityChecks,r.Decisions,r.Reasons,r.Failures,s.Position,s.CompletedTrades,s.NetPnl,s.WinRate,status="RetrospectiveKnownDates"},json));
                Console.WriteLine($"DONE {r.Name} trades={s.CompletedTrades} net={s.NetPnl} win={s.WinRate}");
            }
        }
        finally{foreach(var r in runs.Values)r.Dispose();}
        return 0;
    }
}
