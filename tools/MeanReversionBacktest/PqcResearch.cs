using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KGIOptionPricing.Strategies.PeriodicQueueCycle;
using KGIOptionPricing.StrategyFramework;
using KGIOptionPricing.StrategyFramework.Storage;
using KGIOptionPricing.StrategyFramework.Execution;

namespace MeanReversionBacktest;

// Offline adapter only: strategy, execution, attribution, ledger and recorder stay shared.
internal static class PqcResearch
{
    private sealed class Clock : IEventDrivenStrategyClock
    {
        public DateTimeOffset Now { get; private set; }
        public void Advance(DateTimeOffset at) { if (at > Now) Now = at; }
    }
    private sealed class Candidate : IDisposable
    {
        public required string Name;
        public required SimulationRunRecorder Recorder;
        public required RunManifest Manifest;
        public StrategyInstanceRunner Runner = null!;
        public readonly PqcResearchMetrics Risk = new();
        public readonly Dictionary<string, long> Reasons = new();
        public DateTimeOffset LastSnapshot;
        public int LastTrades, LastPosition;
        public readonly List<ExecutionEvent> Fills = new();
        public void Dispose() => Recorder.Dispose();
    }
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    // --pqc-research <archive> <new-output> <profiles.json> <yyyyMMdd> <training|validation|holdout>
    public static async Task<int> Run(string[] args)
    {
        if (args.Length != 6) throw new ArgumentException("--pqc-research archive output profiles yyyyMMdd split");
        string input = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[2]);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Output must be new or empty.");
        Directory.CreateDirectory(output);
        var profiles = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(args[3]))!;
        var date = DateTime.ParseExact(args[4], "yyyyMMdd", CultureInfo.InvariantCulture);
        DateTimeOffset Time(DateTime d, int h, int m) => new(d.Year, d.Month, d.Day, h, m, 0, TimeSpan.FromHours(8));
        var start = Time(date, 7, 45); var end = Time(date.AddDays(1), 5, 0);
        var pulses = new[] {Time(date,14,25), Time(date,16,0), Time(date.AddDays(1),4,40), end};
        string symbol = "";
        foreach (string line in File.ReadLines(input))
            if (MarketDataArchiveCodec.TryDeserialize(line, out var first) && first.ProductId == "225MC") {symbol = first.Symbol; break;}
        if (string.IsNullOrEmpty(symbol)) throw new InvalidDataException("No 225MC product.");
        using var inputStream = File.OpenRead(input);
        string inputHash = Convert.ToHexString(SHA256.HashData(inputStream));
        var info = new FileInfo(input);
        var universe = new ResolvedStrategyUniverse {Tradable = new ResolvedProduct {ProductId="225MC", Symbol=symbol, TickSize=5, ContractMultiplier=10}};
        var clock = new Clock(); clock.Advance(start);
        using var source = new ArchiveReplayMarketDataSource(input);
        using var host = new StrategyHost(source, clock);
        var candidates = new Dictionary<string,Candidate>();
        host.ExecutionOccurred += e => {var c=candidates[e.StrategyInstanceId]; c.Fills.Add(e); c.Recorder.Record(e);};
        host.OrderLifecycleOccurred += e => candidates[e.StrategyInstanceId].Recorder.Record(e);
        host.DecisionObserved += o => {var c=candidates[o.StrategyInstanceId]; c.Reasons[o.PrimaryReasonCode]=c.Reasons.GetValueOrDefault(o.PrimaryReasonCode)+1; if(c.Risk.ShouldRecord(o))c.Recorder.Record(o);};
        host.SnapshotChanged += s =>
        {
            var c=candidates[s.InstanceId]; bool extrema=c.Risk.Observe(s.NetPnl);
            if (extrema || s.CompletedTrades!=c.LastTrades || s.Position!=c.LastPosition || s.UpdatedAt>=c.LastSnapshot.AddSeconds(5))
            {c.Recorder.Record(s);c.LastSnapshot=s.UpdatedAt;c.LastTrades=s.CompletedTrades;c.LastPosition=s.Position;}
        };
        foreach (var profile in profiles)
        {
            var parsed=PeriodicQueueCycleParameters.Parse(profile.Value);
            bool marketStop=profile.Value.GetValueOrDefault("ResearchStopExecution")=="NextContraMarket";
            // Public scalar properties contain parsed defaults, including disabled compatibility parameters.
            var effective=typeof(PeriodicQueueCycleParameters).GetProperties().Where(p=>p.Name!="ParameterSummary").ToDictionary(p=>p.Name,p=>
                p.GetValue(parsed) is TimeOnly t ? t.ToString("HH:mm",CultureInfo.InvariantCulture) : Convert.ToString(p.GetValue(parsed),CultureInfo.InvariantCulture)!);
            effective["ResearchStopExecution"]=marketStop?"NextContraMarket":"UnchangedLimit";
            effective["ResearchProfile"]=profile.Key;
            string normalized=JsonSerializer.Serialize(new SortedDictionary<string,string>(effective,StringComparer.Ordinal));
            string id="pqc-"+profile.Key;
            var manifest=new RunManifest {RunId=profile.Key+"-"+args[4], StrategyType="PeriodicQueueCycle",InstanceId=id,Mode=StrategyRunMode.HistoricalReplay,
                CodeVersion=Hash(string.Join("\n",Directory.GetFiles("KGIOptionPricing/Strategies/PeriodicQueueCycle","*.cs").OrderBy(x=>x,StringComparer.Ordinal).Select(File.ReadAllText))),FrameworkVersion="shared-current-source",
                StartedAt=start,EndedAt=end,RawParameters=profile.Value,EffectiveParameters=effective,NormalizedParameters=normalized,ParameterSha256=Hash(normalized),
                InputArchive=new(){AbsolutePath=input,SizeBytes=info.Length,LastWriteTimeUtc=info.LastWriteTimeUtc,Sha256=inputHash},
                Universe=new[]{new RunProductIdentity("225MC",symbol,symbol.EndsWith("Z26")?"202612":"unknown",5,10)},CommissionPerContract=36,
                ExecutionAssumptions=new Dictionary<string,string>{{"fillModel","shared-paper-strict-trade-cross; no-touch-fill; limit-at-request-price"},{"latencyModel","synchronous-simulation; NOT broker validated"},{"slippageModel","base zero; ex-post adverse 0/1/2 ticks each fill"},{"sessionPulse","07:45-14:25;16:00-04:40;UTC+08; boundary heartbeat before next event"},{"marketExit","next valid book or trade"}}};
            var c=new Candidate{Name=profile.Key,Recorder=new SimulationRunRecorder(Path.Combine(output,profile.Key)),Manifest=manifest};
            if(marketStop)c.Manifest=manifest with {ExecutionAssumptions=manifest.ExecutionAssumptions.ToDictionary(x=>x.Key,x=>x.Key=="fillModel"?x.Value+"; research StopLoss intent -> next-event Market":x.Value)};
            c.Recorder.PrepareManifest(c.Manifest);candidates[id]=c;
            IManagedStrategyExecutionService broker=new CrossPricePaperExecutionService(36,clock,fillMarketOnBook:true);
            if(marketStop)broker=new PqcResearchExecution(broker);
            c.Runner=host.Add(new PeriodicQueueCycleFactory().Create(new StrategyDefinition{InstanceId=id,StrategyType="PeriodicQueueCycle",Parameters=effective}),universe,36,
                broker);
        }
        long events=0, matching=0, regressions=0; DateTimeOffset last=start; int pulse=0;
        source.BeforeMarketEvent += at =>
        {
            if(at<start || at>=end) throw new InvalidDataException("Use archive bounded to 07:45-next05:00.");
            if(at<last) regressions++;
            while(pulse<pulses.Length && pulses[pulse]<=at) {clock.Advance(pulses[pulse]);host.Pulse(clock.Now);pulse++;}
            clock.Advance(at); if(at>last)last=at;events++;
            if(events%500000==0)Console.WriteLine($"{args[4]} events={events} clock={clock.Now:HH:mm:ss}");
        };
        source.MarketEventReceived += e =>
        {
            if(e.Symbol!=symbol)return; matching++;
            // A trade can change the shared mark without a strategy Publish call.
            foreach(var c in candidates.Values)
            {var s=c.Runner.GetSnapshot();if(c.Risk.Observe(s.NetPnl))c.Recorder.Record(s);}
        };
        try
        {
            await source.ReplayAsync();
            while(pulse<pulses.Length) {clock.Advance(pulses[pulse++]);host.Pulse(clock.Now);}
            host.StopAll("研究回測結束");
            var results=new List<object>();
            foreach(var c in candidates.Values)
            {
                var s=c.Runner.GetSnapshot();c.Risk.Observe(s.NetPnl);c.Recorder.Record(s);
                c.Recorder.FinalizeManifest(c.Manifest);c.Recorder.FinalizeSummary(end);
                using var summary=JsonDocument.Parse(File.ReadAllText(Path.Combine(c.Recorder.DirectoryPath,"run-summary.json")));
                var sr=summary.RootElement;
                if(sr.GetProperty("completedTrades").GetInt32()!=s.CompletedTrades || s.Position==0 && sr.GetProperty("netPnl").GetDecimal()!=s.NetPnl)
                    throw new InvalidOperationException("Recorder does not reconcile with shared ledger.");
                var position=host.Ledger.GetPosition(s.InstanceId,s.TradableSymbol);
                bool closed=s.Position==0 && s.State==StrategyLifecycleState.Stopped;
                var stress=new Dictionary<int,decimal?>();for(int ticks=0;ticks<=2;ticks++)stress[ticks]=s.NetPnl-c.Fills.Sum(e=>e.Quantity)*50*ticks;
                c.Recorder.Record(new ParameterExperimentRecord{ExperimentId="pqc-frequency-20261007",BaselineRunId=profiles.Keys.First()+"-"+args[4],CandidateRunId=c.Manifest.RunId,
                    StrategyType="PeriodicQueueCycle",CodeVersion=c.Manifest.CodeVersion,CandidateParameters=c.Manifest.EffectiveParameters,InputArchiveSha256=inputHash,DataSplitId=args[5]+"-"+args[4],
                    ChangedParameters=profiles[c.Name].Concat(new[]{new KeyValuePair<string,string>("ResearchProfile",c.Name)}).ToDictionary(x=>x.Key,x=>c.Manifest.EffectiveParameters[x.Key]),
                    ParameterDefinitions=c.Manifest.EffectiveParameters.ToDictionary(x=>x.Key,x=>Definition(x.Key,x.Value)),
                    ProductMapping=new Dictionary<string,string>{{"225MC",symbol}},ExecutionAssumptions=c.Manifest.ExecutionAssumptions,SearchMethod="predeclared manual single-group parameter candidates",
                    PrimaryObjective="positive net after costs and >=100 round trips EACH day",SecondaryObjectives=new[]{"net win rate","marked daily maximum loss","marked maximum drawdown"},
                    Guardrails=new[]{"no live/default changes","no book-health gates relaxed","no fabricated EOF fills","report strict-cross execution limitation"},
                    Results=new Dictionary<string,decimal?>{{"completedTrades",s.CompletedTrades},{"winRate",s.WinRate},{"grossPnl",s.RealizedPnl},{"netPnl",s.NetPnl},{"maximumDrawdown",c.Risk.MaximumDrawdown},{"maximumDailyLoss",c.Risk.MaximumLoss}},AdverseSlippageStress=stress,
                    Status=!closed?ParameterExperimentStatus.Incomplete:s.CompletedTrades<100 || stress[1]<=0?ParameterExperimentStatus.Rejected:ParameterExperimentStatus.Valid,
                    ReasonCode=!closed?"UnclosedExposure":s.CompletedTrades<100?"DailyFrequencyBelowTarget":stress[1]<=0?"NonPositiveOneTickStress":"ResearchOnly"});
                var result=new {profile=c.Name,day=args[4],split=args[5],events,matching,regressions,s.CompletedTrades,s.WinRate,grossPnl=s.RealizedPnl,costs=position.Costs,s.NetPnl,s.UnrealizedPnl,
                    maximumDailyLoss=c.Risk.MaximumLoss,maximumDrawdown=c.Risk.MaximumDrawdown,position=s.Position,state=s.State.ToString(),status=closed?"ClosedResearchRun":"IncompleteUnclosedExposure",stress,
                    exitRoles=c.Fills.GroupBy(e=>e.Role.ToString()).ToDictionary(g=>g.Key,g=>g.Count()),reasons=c.Reasons,parameters=c.Manifest.EffectiveParameters};
                File.WriteAllText(Path.Combine(c.Recorder.DirectoryPath,"comparison.json"),JsonSerializer.Serialize(result,Json));results.Add(result);
                Console.WriteLine($"{args[4]} {c.Name} trades={s.CompletedTrades} win={s.WinRate} net={s.NetPnl} loss={c.Risk.MaximumLoss} dd={c.Risk.MaximumDrawdown} position={s.Position}");
            }
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(results,Json));return 0;
        }
        finally {foreach(var c in candidates.Values)c.Dispose();}
    }

    private static ParameterSearchDefinition Definition(string key,string value)
    {
        if(bool.TryParse(value,out _))return new("bool","boolean",0,1,0,1,1);
        if(!decimal.TryParse(value,NumberStyles.Number,CultureInfo.InvariantCulture,out var number))return new("string","categorical or HH:mm; numeric ranges not applicable",0,0,0,0,1);
        var bounds=key switch
        {
            "DirectionWindowSeconds"=>(2m,300m),"MinMoveTicks"=>(.5m,50m),
            "MicrostructureWindowSeconds"=>(1m,30m),"MinNormalizedOfi" or "MaxAbsNormalizedOfi"=>(0m,1000m),
            "MinQueueImbalance" or "MinDepthResilience"=>(0m,1m),
            "MinTrendEfficiency" or "MinDepthObi" or "MinDepthRatio"=>(0m,1m),
            "ObiConfirmSeconds" or "DirectionReverseSeconds"=>(1m,30m),
            "MinSideDepth" or "MinNearDepth"=>(1m,1_000_000m),
            "MaxSpreadTicks"=>(1m,20m),"MaxVolatilityTicks" or "StopLossTicks"=>(1m,100m),
            "QuoteFreshnessSeconds" or "MarketDataRecoverySeconds"=>(.1m,30m),
            "MaxOneLegSeconds"=>(1m,300m),"CycleCooldownSeconds" or "StopLossCooldownSeconds" or "ShockCooldownSeconds"=>(0m,3600m),
            _=>(0m,1_000_000m)
        };
        string unit=key.Contains("Seconds")?"seconds":key.Contains("Ofi")?"L1-depth-equivalents":key.Contains("Ticks")?"ticks":key=="CommissionPerContract"?"JPY/contract/side":key is "MinSideDepth" or "MinNearDepth" ||key.Contains("Ahead")||key.Contains("Reprice")?"contracts":"ratio";
        // Manual finite candidates: each record's numeric search is the evaluated point.
        return new(typeof(PeriodicQueueCycleParameters).GetProperty(key)?.PropertyType==typeof(int)?"int":"decimal",unit,bounds.Item1,bounds.Item2,number,number,.01m);
    }
}
