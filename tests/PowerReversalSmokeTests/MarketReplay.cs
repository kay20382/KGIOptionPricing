using System.Text.Json;
using System.Security.Cryptography;
using KGIOptionPricing.Strategies.PowerReversal;
using KGIOptionPricing.StrategyFramework;
using KGIOptionPricing.StrategyFramework.Storage;

static class MarketReplay
{
    // Reprocess unchanged executions through the common recorder when an exploratory
    // run omitted PrepareManifest. This repairs reporting, never trades or prices.
    public static int RebuildRecording(string original,string corrected)
    {
        var options=new JsonSerializerOptions(JsonSerializerDefaults.Web);options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        var manifest=JsonSerializer.Deserialize<RunManifest>(File.ReadAllText(Path.Combine(original,"run-manifest.json")),options)!;
        using var recorder=new SimulationRunRecorder(corrected);recorder.PrepareManifest(manifest);
        foreach(var line in File.ReadLines(Path.Combine(original,"strategy-decisions.jsonl")))recorder.Record(JsonSerializer.Deserialize<StrategyDecisionObservation>(line,options)!);
        foreach(var line in File.ReadLines(Path.Combine(original,"order-lifecycle.jsonl")))recorder.Record(JsonSerializer.Deserialize<OrderLifecycleEvent>(line,options)!);
        using var csv=new Microsoft.VisualBasic.FileIO.TextFieldParser(Path.Combine(original,"executions.csv"));csv.SetDelimiters(",");csv.HasFieldsEnclosedInQuotes=true;
        var names=csv.ReadFields()!;
        while(!csv.EndOfData){var cells=csv.ReadFields()!;var row=names.Zip(cells).ToDictionary(x=>x.First,x=>x.Second);decimal Dec(string key)=>decimal.Parse(row[key],System.Globalization.CultureInfo.InvariantCulture);int Int(string key)=>int.Parse(row[key]);
            recorder.Record(new ExecutionEvent{Timestamp=DateTimeOffset.Parse(row["timestamp"]),Sequence=long.Parse(row["sequence"]),MarketSequence=long.Parse(row["marketSequence"]),StrategyInstanceId=row["instanceId"],CycleId=row["cycleId"],ClientOrderId=row["clientOrderId"],OrderId=row["orderId"],Symbol=row["symbol"],Side=Enum.Parse<KGIOptionPricing.OrderManagement.Core.BuySell>(row["side"]),Role=Enum.Parse<PaperOrderRole>(row["role"]),Price=Dec("price"),Quantity=Int("quantity"),Commission=Dec("commission"),TriggerMarketPrice=Dec("triggerMarketPrice"),ExecutionId=row["executionId"],CumulativeFilledQuantity=Int("cumulativeFilledQuantity"),StrategyRunGeneration=long.Parse(row["strategyRunGeneration"])});
        }
        StrategySnapshot? final=null;foreach(var line in File.ReadLines(Path.Combine(original,"strategy-events.jsonl"))){final=JsonSerializer.Deserialize<StrategySnapshot>(line,options)!;recorder.Record(final);}
        recorder.FinalizeManifest(manifest);recorder.FinalizeSummary(manifest.EndedAt!.Value);
        using var summary=JsonDocument.Parse(File.ReadAllText(Path.Combine(corrected,"run-summary.json")));
        if(final==null || summary.RootElement.GetProperty("netPnl").GetDecimal()!=final.NetPnl || summary.RootElement.GetProperty("completedTrades").GetInt32()!=final.CompletedTrades || Math.Abs(summary.RootElement.GetProperty("winRate").GetDecimal()-final.WinRate!.Value)>0.000001m)throw new InvalidOperationException("Rebuilt recording differs from shared ledger.");
        File.Copy(Path.Combine(original,"comparison.json"),Path.Combine(corrected,"comparison.json"));
        Console.WriteLine("PASS unchanged executions reprocessed with original manifest; matches shared ledger.");return 0;
    }
    sealed class Clock:IStrategyClock{public DateTimeOffset Now{get;set;}}
    public static int Run(string[] args)
    {
        string input=Path.GetFullPath(args[1]),output=Path.GetFullPath(args[2]);
        string group=args[3];Directory.CreateDirectory(output);
        var values=new Dictionary<string,string>{["HistoryRoot"]=Path.Combine(output,"history")};
        if(group=="research-day") {values=PowerReversalParameters.ResearchProfile().ToDictionary();values["HistoryRoot"]=Path.Combine(output,"history");}
        if(group is "volume" or "balanced")values["MinQuantity"]="30";
        if(group is "signal" or "balanced") {values["PowerThreshold"]="10";values["BaisThreshold"]="5";values["ConfirmSeconds"]="2";values["MovementRatio"]="0.6";}
        if(group=="relaxed") {values["MinQuantity"]="30";values["PowerThreshold"]="5";values["BaisThreshold"]="3";values["ConfirmSeconds"]="1";values["MovementRatio"]="0.5";}
        if(group is "early" or "early-relaxed") {values["MinQuantity"]="30";values["PowerThreshold"]="10";values["BaisThreshold"]="5";values["ConfirmSeconds"]="2";values["MovementRatio"]="0.6";values["RequireShortAverageDirection"]="0";}
        if(group=="early-relaxed") {values["PowerThreshold"]="5";values["BaisThreshold"]="3";values["ConfirmSeconds"]="1";values["MovementRatio"]="0.5";}
        if(group is "pullback" or "pullback-longer" or "pullback-relaxed") {values["MinQuantity"]="30";values["PowerThreshold"]="10";values["BaisThreshold"]="5";values["ConfirmSeconds"]="2";values["MovementRatio"]="0.6";values["RequireShortAverageDirection"]="0";values["AllowRiskRewardPullback"]="1";}
        if(group=="pullback-longer")values["EntryTimeoutSeconds"]="30";
        if(group=="pullback-relaxed") {values["PowerThreshold"]="5";values["BaisThreshold"]="3";values["ConfirmSeconds"]="1";values["MovementRatio"]="0.5";}
        if(File.Exists(group)){values=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(group))!;values["HistoryRoot"]=Path.Combine(output,"history");group=Path.GetFileNameWithoutExtension(group);}
        var parameters=PowerReversalParameters.Parse(values).ToDictionary();
        var d=new StrategyDefinition{InstanceId="replay-"+group,StrategyType="PowerReversal",Parameters=values};
        var universe=new ResolvedStrategyUniverse{Tradable=new(){ProductId="225MC",Symbol="225MC-Z26",TickSize=5,ContractMultiplier=10},References=new[]{new ResolvedProduct{ProductId="225M",Symbol="225M-Z26",TickSize=5,ContractMultiplier=100},new ResolvedProduct{ProductId="225",Symbol="225-Z26",TickSize=10,ContractMultiplier=1000}}};
        using var source=new ArchiveReplayMarketDataSource(input);var clock=new Clock();using var host=new StrategyHost(source,clock);
        var strategy=new PowerReversalStrategy(d);using var recorder=new SimulationRunRecorder(output);
        var reasons=new Dictionary<string,int>();var failures=new Dictionary<string,int>();int decisions=0,intents=0,fills=0;
        host.ExecutionOccurred+=e=>{fills++;recorder.Record(e);};host.OrderLifecycleOccurred+=recorder.Record;
        strategy.OrderRequested+=_=>intents++;
        DateTimeOffset? lastRecorded=null;string lastReason="";
        host.DecisionObserved+=o=>{decisions++;reasons[o.PrimaryReasonCode]=reasons.GetValueOrDefault(o.PrimaryReasonCode)+1;foreach(var t in o.Thresholds.Where(x=>!x.Passed))failures[t.Name]=failures.GetValueOrDefault(t.Name)+1;
            if(lastRecorded==null || o.PrimaryReasonCode!=lastReason || (o.Timestamp-lastRecorded.Value).TotalSeconds>=1 || o.Decision==StrategyDecision.Accept){recorder.Record(o);lastRecorded=o.Timestamp;lastReason=o.PrimaryReasonCode;}};
        var start=args.Length>4?DateTimeOffset.Parse(args[4],System.Globalization.CultureInfo.InvariantCulture):new DateTimeOffset(2026,10,6,21,23,54,TimeSpan.FromHours(8));var end=args.Length>5?DateTimeOffset.Parse(args[5],System.Globalization.CultureInfo.InvariantCulture):new DateTimeOffset(2026,10,7,5,0,0,TimeSpan.FromHours(8));
        clock.Now=start;var runner=host.Add(strategy,universe,36);DateTimeOffset? last=null;long count=0;
        string normalized=JsonSerializer.Serialize(new SortedDictionary<string,string>(parameters,StringComparer.Ordinal));
        using var file=File.OpenRead(input);string hash=Convert.ToHexString(SHA256.HashData(file));
        var manifest=new RunManifest{RunId=group,StrategyType="PowerReversal",InstanceId=d.InstanceId,Mode=StrategyRunMode.HistoricalReplay,StartedAt=start,EndedAt=end,EffectiveParameters=parameters,NormalizedParameters=normalized,ParameterSha256=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalized))),InputArchive=new(){AbsolutePath=input,Sha256=hash,SizeBytes=file.Length},Universe=universe.AllProducts.Select(p=>new RunProductIdentity(p.ProductId,p.Symbol,"202612",p.TickSize,p.ContractMultiplier)).ToArray(),CommissionPerContract=36,ExecutionAssumptions=new Dictionary<string,string>{{"fillModel","paper-strict-cross"},{"latencyModel","not-configured"},{"slippageModel","not-configured"}}};
        // Product multipliers must be available before the first execution is recorded.
        recorder.PrepareManifest(manifest);
        DateTimeOffset? lastSnapshot=null;
        string lastSnapshotReason="";
        host.SnapshotChanged+=s=>{string reason=s.Diagnostics.GetValueOrDefault("PrimaryReasonCode","");if(lastSnapshot==null || s.UpdatedAt>=lastSnapshot.Value.AddSeconds(1) || reason!=lastSnapshotReason && reason is "ENTRY_FILLED" or "EXIT_FILLED"){
            recorder.Record(s with{Diagnostics=s.Diagnostics.Where(x=>!x.Key.EndsWith(".History",StringComparison.Ordinal)).ToDictionary(x=>x.Key,x=>x.Value)});lastSnapshot=s.UpdatedAt;lastSnapshotReason=reason;}};
        // The bounded input copy must contain only the common comparison interval.
        source.BeforeMarketEvent+=at=>{if(at<start || at>end)throw new ArgumentException("Input must be bounded to the requested interval.");if(last.HasValue && at>=last.Value)foreach(var pulse in strategy.GetPulseTimes(last.Value,at))host.Pulse(pulse);clock.Now=at;last=at;count++;};
        // Use a bounded copy: ArchiveReplayMarketDataSource itself is unfiltered.
        int metricMarks=0,metricParityChecks=0;
        source.MarketEventReceived+=e=>{if(e.Symbol!=universe.Tradable.Symbol || e.LastPrice<=0)return;
            var position=host.Ledger.GetPosition(d.InstanceId,runner.StrategyRunGeneration,universe.Tradable.Symbol);
            if(++metricMarks%10000==0){var full=runner.GetSnapshot();if(full.NetPnl!=position.NetPnl || full.CompletedTrades!=position.CompletedTrades || full.Position!=position.NetQuantity)throw new InvalidOperationException("Lightweight metric observation differs from common runner ledger.");metricParityChecks++;}
            recorder.ObservePerformance(new StrategySnapshot{InstanceId=d.InstanceId,StrategyType=d.StrategyType,TradableSymbol=universe.Tradable.Symbol,StrategyRunGeneration=runner.StrategyRunGeneration,UpdatedAt=e.Timestamp,Position=position.NetQuantity,RealizedPnl=position.RealizedPnl,UnrealizedPnl=position.UnrealizedPnl,NetPnl=position.NetPnl,CompletedTrades=position.CompletedTrades});
        };
        source.ReplayAsync().GetAwaiter().GetResult();
        host.Pulse(end);host.StopAll("回測結束");var snapshot=runner.GetSnapshot();recorder.Record(snapshot);
        recorder.FinalizeManifest(manifest);recorder.FinalizeSummary(end);
        using var summary=JsonDocument.Parse(File.ReadAllText(Path.Combine(output,"run-summary.json")));
        if(summary.RootElement.GetProperty("completedTrades").GetInt32()!=snapshot.CompletedTrades || summary.RootElement.GetProperty("netPnl").GetDecimal()!=snapshot.NetPnl ||
            (snapshot.WinRate.HasValue && Math.Abs(summary.RootElement.GetProperty("winRate").GetDecimal()-snapshot.WinRate.Value)>0.000001m))throw new InvalidOperationException("Recorder summary differs from authoritative ledger.");
        var result=new{group,parameters,start,end,count,metricMarks,metricParityChecks,decisions,intents,fills,reasons,failures,snapshot.Position,snapshot.CompletedTrades,snapshot.NetPnl,snapshot.RealizedPnl,snapshot.UnrealizedPnl,snapshot.WinRate,snapshot.AverageWinningPoints,snapshot.AverageLosingPoints,snapshot.UserDefinedProfitFactor,status=snapshot.Position==0?"IncompleteSingleDay":"IncompleteUnclosedPosition"};
        File.WriteAllText(Path.Combine(output,"comparison.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(result));return 0;
    }
}
