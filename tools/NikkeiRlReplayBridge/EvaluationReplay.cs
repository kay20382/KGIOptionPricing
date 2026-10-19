using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NikkeiRl;

// Evaluation-only segmentation. Raw caches/ledger remain unchanged; never synthesize exits.
public static class EvaluationReplay
{
    public static string Hash(string path){using var file=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(file));}
    public static List<ReplayFrame[]> Split(ReplayFrame[] frames,int decisionIntervalSeconds=5)
    {
        var result=new List<ReplayFrame[]>();var current=new List<ReplayFrame>();
        DateTimeOffset? recovery=null;bool interrupted=false;
        for(int i=0;i<frames.Length;i++)
        {
            var f=frames[i];
            bool discontinuity=i>0&&(Math.Abs((f.At-frames[i-1].At).TotalSeconds-decisionIntervalSeconds)>.001||f.Symbol!=frames[i-1].Symbol);
            if(f.DataGap||discontinuity)
            {
                if(current.Count>0){result.Add(current.ToArray());current.Clear();}
                interrupted=true;recovery=f.DataGap?null:f.At;
                continue;
            }
            if(interrupted)
            {
                recovery??=f.At;
                if((f.At-recovery.Value).TotalSeconds<60)continue;
                interrupted=false;
            }
            current.Add(f);
        }
        if(current.Count>0)result.Add(current.ToArray());return result;
    }
    public static void Prepare(string cache,string directory)
    {
        Directory.CreateDirectory(directory);
        var indexPath=Path.Combine(directory,"segment-index.json");if(File.Exists(indexPath))throw new IOException("Immutable evaluation index exists");
        var parent=JsonSerializer.Deserialize<CacheManifest>(File.ReadAllText(cache+".manifest.json"),ReplayCache.Json)!;
        if(Hash(cache)!=parent.CacheSha256||Hash(parent.InputPath)!=parent.InputSha256||parent.BadRows!=0||parent.OutOfOrder!=0||parent.Dropped!=0)
            throw new InvalidDataException("Source/cache quality failed");
        ReplayCache.ValidateSchemaReadiness(parent);
        if(parent.Schema!=ReplayCache.SchemaForReadiness("nikkei-rl-cache-v4",parent.ResearchReadinessProfile))throw new InvalidDataException("Paper cache required");
        var frames=ReplayCache.Read(cache);
        if(frames.Length<2||frames.Any(f=>f.PaperExtras.Length!=8||f.PaperExtras.Any(x=>!double.IsFinite(x))))throw new InvalidDataException("Invalid paper frames");
        var segments=new List<object>();int number=0;
        foreach(var part in Split(frames,parent.DecisionIntervalSeconds))
        {
            number++;string id=$"{frames[0].At:yyyyMMdd}-segment{number:00}";
            bool excluded=part.Length<2||!part.Any(f=>f.Valid);
            string? path=null;
            if(!excluded)
            {
                path=Path.GetFullPath(Path.Combine(directory,id+".br"));
                using(var output=new FileStream(path,FileMode.CreateNew))using(var compressed=new BrotliStream(output,CompressionLevel.Fastest))using(var writer=new StreamWriter(compressed))
                    foreach(var f in part)writer.WriteLine(JsonSerializer.Serialize(f,ReplayCache.Json));
                var manifest=parent with{Schema=ReplayCache.SchemaForReadiness("nikkei-rl-cache-v5-evaluation",parent.ResearchReadinessProfile),Usage="EvaluationOnly",CacheSha256=Hash(path),ParentCacheSha256=parent.CacheSha256,Frames=part.Length,ValidFrames=part.Count(f=>f.Valid),DataGapFrames=0,RetainedFrom=part[0].At,LegacyParityVerified=false};
                File.WriteAllText(path+".manifest.json",JsonSerializer.Serialize(manifest,ReplayCache.Json));
                ReplayCache.ValidateManifest(manifest,Hash(path),true);
            }
            segments.Add(new{id,path,start=part[0].At,end=part[^1].At,frames=part.Length,validFrames=part.Count(f=>f.Valid),excluded,reason=excluded?"TooShortOrNoHealthyFrames":"",cacheSha256=path==null?null:Hash(path)});
        }
        var at=frames[0].At;var sessionStart=new DateTimeOffset(at.Year,at.Month,at.Day,7,45,0,at.Offset);var sessionEnd=sessionStart.AddHours(7);
        bool fullDayCoverage=frames[0].At<=sessionStart.AddSeconds(65)&&frames[^1].At>=sessionEnd.AddSeconds(-5)&&parent.DataGapFrames==0&&segments.Count==1;
        File.WriteAllText(indexPath,JsonSerializer.Serialize(new{date=at.ToString("yyyyMMdd"),parentCache=Path.GetFullPath(cache),parentCacheSha256=parent.CacheSha256,parent.InputPath,parent.InputSha256,parent.Rows,parent.BadRows,parent.OutOfOrder,parent.Dropped,parent.DataGapFrames,first=frames[0].At,last=frames[^1].At,fullDayCoverage,recoveryWarmupSeconds=60,segments},ReplayCache.Json));
        Console.WriteLine(indexPath);
    }
    public static RlConfig ApplyRiskOverrides(RlConfig trained,string riskFile)
    {
        var risk=JsonNode.Parse(File.ReadAllText(riskFile)) as JsonObject??throw new InvalidDataException("Risk configuration must be an object");
        string[] allowed=["stopMode","fixedStopPoints","minStopPoints","maxStopPoints","stopVolatilitySeconds","stopVolatilityMultiplier","extraCostTicksPerSide"];
        if(risk.Any(p=>!allowed.Contains(p.Key))||!risk.ContainsKey("maxStopPoints")||!risk.ContainsKey("stopMode"))throw new InvalidDataException("Risk overrides cannot alter model/trading/data contract");
        var merged=JsonSerializer.SerializeToNode(trained,ReplayCache.Json)!;
        foreach(var item in risk)merged[item.Key]=item.Value?.DeepClone();
        var result=merged.Deserialize<RlConfig>(ReplayCache.Json)!;
        if(result.StopMode is not ("fixed" or "adaptive-volatility")||result.MaxStopPoints is null||result.MaxStopPoints.Value<trained.TickSize||result.MaxStopPoints.Value>150||result.MaxStopPoints.Value%trained.TickSize!=0||result.ExtraCostTicksPerSide is not (0 or 1 or 2))throw new InvalidDataException("Risk experiment requires a tick-aligned cap up to150points and0/1/2 extra cost ticks");
        return result;
    }
    public static RlConfig ApplyEntryOverrides(RlConfig trained,string entryFile)
    {
        var entry=JsonNode.Parse(File.ReadAllText(entryFile)) as JsonObject??throw new InvalidDataException("Entry configuration must be an object");
        string[] allowed=["entryPriceMode","extraCostTicksPerSide"];
        if(entry.Any(p=>!allowed.Contains(p.Key))||!entry.ContainsKey("entryPriceMode"))throw new InvalidDataException("Entry overrides cannot alter risk/cost/data contract");
        var merged=JsonSerializer.SerializeToNode(trained,ReplayCache.Json)!;
        foreach(var item in entry)merged[item.Key]=item.Value?.DeepClone();
        var result=merged.Deserialize<RlConfig>(ReplayCache.Json)!;
        if(result.EntryPriceMode is not ("same-side" or "opposite-touch")||result.ExtraCostTicksPerSide is not (0 or 1 or 2))throw new InvalidDataException("Entry sensitivity requires supported strict-cross quote mode and0/1/2 cost ticks");
        return result;
    }
    public static void Evaluate(string model,string manifest,string dataset,string directory,string? riskFile=null,bool summaryOnly=false,string? entryFile=null)
    {
        if(riskFile!=null&&entryFile!=null)throw new InvalidDataException("Separate risk and entry experiments required");
        if(Directory.Exists(directory))throw new IOException("Immutable evaluation output exists");Directory.CreateDirectory(directory);
        using var policy=new RlPolicyPredictor(model,manifest);
        using var modelManifest=JsonDocument.Parse(File.ReadAllText(manifest));
        var config=modelManifest.RootElement.GetProperty("environmentConfig").Deserialize<RlConfig>(ReplayCache.Json)! with{AllowIncompleteEvaluation=true};
        if(config.DecisionLatencyMs!=1||config.CooldownSeconds!=10||config.Commission!=36||config.ExtraCostTicksPerSide!=0||config.Multiplier!=10||config.TickSize!=5||config.StopTicks!=6||config.MaxHoldingSeconds!=(policy.FeatureProfile is "mini-micro-power" or "mini-micro-research"?0:60)||config.FeatureProfile!=policy.FeatureProfile||config.ObservationSize!=policy.ObservationSize)
            throw new InvalidDataException("Frozen model environment differs from required parameters");
        var trainingConfig=config;
        if(entryFile!=null)
        {
            if(policy.FeatureProfile!="mini-micro-research")throw new InvalidDataException("Entry experiments require the60input research profile");
            config=ApplyEntryOverrides(config,entryFile);
        }
        if(riskFile!=null)
        {
            if(policy.FeatureProfile is not ("mini-micro-power" or "mini-micro-research"))throw new InvalidDataException("New stop experiments use the power profiles only");
            config=ApplyRiskOverrides(config,riskFile);
        }
        var episodes=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(dataset),ReplayCache.Json)!;
        var results=new List<object>();var times=new List<double>();
        foreach(var (id,path) in episodes.OrderBy(p=>p.Key))
        {
            var cacheManifest=JsonSerializer.Deserialize<CacheManifest>(File.ReadAllText(path+".manifest.json"),ReplayCache.Json)!;
            ReplayCache.ValidateManifest(cacheManifest,Hash(path),true);
            ReplayCache.ValidateInterval(cacheManifest,config);
            var frames=ReplayCache.Read(path);var env=new RlReplayEnvironment(frames,config);var state=env.Reset();
            long stepId=0;decimal deltas=0,rewards=0;var fills=new List<KGIOptionPricing.StrategyFramework.ExecutionEvent>();
            var compact=new CompactEpisodeMetrics();
            var proposed=new int[5];var reasons=new Dictionary<string,int>();int eligibleFlat=0,entrySignals=0;
            string? feedbackPath=summaryOnly?null:Path.Combine(directory,id+".feedback.jsonl.gz");
            using(var output=summaryOnly?null:new FileStream(feedbackPath!,FileMode.CreateNew))using(var compressed=summaryOnly?null:new GZipStream(output!,CompressionLevel.Fastest))using(var writer=summaryOnly?null:new StreamWriter(compressed!))
            {
                while(!state.Terminated&&!state.Truncated)
                {
                    var before=state;var prediction=policy.Predict(before.Observation,before.Mask);times.Add(prediction.InferenceMs);
                    proposed[prediction.Action]++;
                    if(before.Position==0&&before.Mask[1]){eligibleFlat++;if(prediction.Action is 1 or 2)entrySignals++;}
                    state=env.Step(prediction.Action);deltas+=state.DeltaEquity;rewards+=state.Reward;compact.Observe(before,state);
                    if(!summaryOnly)fills.AddRange(state.Fills);
                    if(state.OverrideReason!="")reasons[state.OverrideReason]=reasons.GetValueOrDefault(state.OverrideReason)+1;
                    writer?.WriteLine(JsonSerializer.Serialize(new{episode=id,stepId,observation=before.Observation,mask=before.Mask,proposedAction=prediction.Action,inferenceMs=prediction.InferenceMs,next=state},ReplayCache.Json));stepId++;
                }
            }
            if(Math.Abs(deltas-state.NetPnl)>0.00001m||Math.Abs(rewards-state.TotalReward)>0.00001m||Math.Abs(state.CompletedGrossPnl-state.CompletedCosts-state.CompletedNetPnl)>0.00001m||Math.Abs(state.NetPnl-state.ExcludedOpenNetPnl-state.CompletedNetPnl)>0.00001m)
                throw new InvalidDataException("Authoritative ledger/reward reconciliation failed");
            compact.Validate(state,config.Multiplier);
            if(summaryOnly)results.Add(new{id,date=frames[0].At.ToString("yyyyMMdd"),path,cacheSha256=cacheManifest.CacheSha256,
                start=frames[0].At,end=state.At,steps=stepId,status=state.Status,position=state.Position,
                trades=state.CompletedTrades,wins=compact.Wins,losses=compact.Losses,breakeven=compact.Breakeven,
                grossPnlJPY=state.CompletedGrossPnl,netPnlJPY=state.CompletedNetPnl,costsJPY=state.CompletedCosts,
                winningNetJPY=compact.WinningNetJPY,losingNetJPY=compact.LosingNetJPY,
                winRatePercent=state.WinRate,averageWinJPY=state.AverageWinningPoints*config.Multiplier,averageLossJPY=state.AverageLosingPoints*config.Multiplier,
                excludedOpenPositions=state.Position==0?0:1,excludedEntryCostsJPY=state.OpenPositionCosts,excludedUnrealizedJPY=state.UnrealizedPnl,
                excludedOpenNetPnlJPY=state.ExcludedOpenNetPnl,rawNetPnlJPY=state.NetPnl,maxDrawdownJPY=state.MaxDrawdown,
                funnel=new{eligibleFlat,entrySignals,proposedActionCounts=proposed,overrides=reasons},sharedLedgerReconciled=true});
            else results.Add(new{id,date=frames[0].At.ToString("yyyyMMdd"),path,cacheSha256=cacheManifest.CacheSha256,start=frames[0].At,end=state.At,steps=stepId,state,fills,feedbackPath,feedbackSha256=Hash(feedbackPath!),sumDeltaEquity=deltas,sumReward=rewards});
            Console.WriteLine($"{Path.GetFileName(model)} {id} {state.Status} trades={state.CompletedTrades} completedNet={state.CompletedNetPnl}");
        }
        times.Sort();double Percentile(double q)=>times.Count==0?0:times[(int)Math.Ceiling(q*(times.Count-1))];
        var timing=new{count=times.Count,p50Ms=Percentile(.5),p95Ms=Percentile(.95),p99Ms=Percentile(.99),maxMs=times.Count==0?0:times[^1],countAbove1Ms=times.Count(t=>t>1),scope="Actor only; feature extraction, scheduling, broker/network excluded. Replay uses fixed 1 ms action latency."};
        File.WriteAllText(Path.Combine(directory,"evaluation.json"),JsonSerializer.Serialize(new{model=Path.GetFullPath(model),manifest=Path.GetFullPath(manifest),modelSha256=Hash(model),manifestSha256=Hash(manifest),datasetSha256=Hash(dataset),trainingConfig,config,riskOverridePath=riskFile==null?null:Path.GetFullPath(riskFile),riskOverrideSha256=riskFile==null?null:Hash(riskFile),entryOverridePath=entryFile==null?null:Path.GetFullPath(entryFile),entryOverrideSha256=entryFile==null?null:Hash(entryFile),purpose=entryFile!=null?"EntrySensitivityPreviouslyInspectedNotRetrained":riskFile==null?"FrozenBaseline":"RiskSensitivityPreviouslyInspectedNotRetrained",summaryOnly,sharedLedgerReconciled=true,timing,results},ReplayCache.Json));
    }
}
