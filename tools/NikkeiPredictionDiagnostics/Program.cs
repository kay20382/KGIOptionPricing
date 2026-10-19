using NikkeiRl;
using NikkeiPredictionDiagnostics;
using System.Text.Json;
using System.Diagnostics;

if(args.Length==1&&args[0]=="selftest")
{
    var linear=new BaselinePredictor("{\"schema\":\"nikkei-prediction-baseline-v1\",\"kind\":\"ridge\",\"columns\":[0],\"means\":[0],\"scales\":[1],\"coefficients\":[2],\"intercept\":1}");
    if(linear.Predict([3])!=7)throw new Exception("FAIL linear expected7");
    var tree=new BaselinePredictor("{\"schema\":\"nikkei-prediction-baseline-v1\",\"kind\":\"gbr\",\"columns\":[0],\"means\":[0],\"scales\":[1],\"initial\":1,\"learningRate\":0.5,\"trees\":[{\"left\":[1,-1,-1],\"right\":[2,-1,-1],\"feature\":[0,-2,-2],\"threshold\":[2,-2,-2],\"value\":[0,-1,4]}]}");
    if(tree.Predict([3])!=3)throw new Exception("FAIL tree expected3");
    var at=new DateTimeOffset(2026,10,6,14,30,0,TimeSpan.FromHours(8));
    ReplayFrame Frame(DateTimeOffset t,ReplayTrade[] trades)=>new(){At=t,Symbol="225MC-Z26",Bid=1000,Ask=1005,Valid=true,Features=new double[30],ResearchExtras=new double[20],Trades=trades};
    var frames=new[]{Frame(at,[]),Frame(at.AddSeconds(3),[new(at.AddTicks(5000),1,995),new(at.AddMilliseconds(2),2,1000),new(at.AddMilliseconds(3),3,995)])};
    var config=new RlConfig{FeatureProfile="mini-micro-research",ResearchReadinessProfile="exclude-flow",DecisionIntervalSeconds=3,ObservationSize=60,DirectionDrivenHolding=true,MaxHoldingSeconds=0,StopMode="fixed",FixedStopPoints=150,MaxStopPoints=150};
    var env=new RlReplayEnvironment(frames,config);var before=env.Reset();var after=env.Step(1);
    if(after.Fills.Length!=1||after.Fills[0].Timestamp!=at.AddMilliseconds(3)||after.Fills[0].Price!=1000)throw new Exception("FAIL strict-cross/latency");
    var probeMetrics=new CompactEpisodeMetrics();probeMetrics.Observe(before,after);probeMetrics.Validate(after,10);
    if(after.CompletedNetPnl!=0||after.OpenPositionCosts!=36||after.ExcludedOpenNetPnl!=-86)throw new Exception("FAIL symmetric open exclusion");
    Console.WriteLine("PASS4: JSON linear/tree; real strict-cross1ms; shared open exclusion");
    return;
}
if(args.Length!=2||args[0]!="serve")throw new ArgumentException("serve dataset.json");
var dataset=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(args[1]),ReplayCache.Json)!;
RlReplayEnvironment? environment=null;ReplayFrame[]? loaded=null;string? loadedPath=null;
BaselinePredictor? baseline=null;RlPolicyPredictor? actor=null;string mode="wait";
RlState? state=null;RlConfig? activeConfig=null;int index=0,pendingSide=0;object? pendingContext=null;
CompactEpisodeMetrics metrics=new();
string? line;
while((line=Console.ReadLine())!=null)
{
    try
    {
        using var doc=JsonDocument.Parse(line);var request=doc.RootElement;string op=request.GetProperty("op").GetString()!;
        if(op=="close")break;
        if(op=="predict")
        {
            var model=new BaselinePredictor(File.ReadAllText(request.GetProperty("model").GetString()!));
            var values=request.GetProperty("observations").EnumerateArray().Select(v=>model.Predict(v.EnumerateArray().Select(x=>x.GetDouble()).ToArray())).ToArray();
            Console.WriteLine(JsonSerializer.Serialize(new{predictions=values},ReplayCache.Json));continue;
        }
        double? prediction=null,inferenceMs=null;int action=0;var contexts=new List<object>();
        if(op=="reset")
        {
            actor?.Dispose();actor=null;baseline=null;
            string path=dataset[request.GetProperty("episode").GetString()!];
            activeConfig=request.GetProperty("config").Deserialize<RlConfig>(ReplayCache.Json)!;
            if(activeConfig.DecisionLatencyMs!=1||activeConfig.DecisionIntervalSeconds!=3||activeConfig.CooldownSeconds!=10||activeConfig.MaxHoldingSeconds!=0||!activeConfig.DirectionDrivenHolding||activeConfig.StopMode!="fixed"||activeConfig.FixedStopPoints!=150||activeConfig.MaxStopPoints!=150||activeConfig.Commission!=36||activeConfig.TickSize!=5||activeConfig.Multiplier!=10||activeConfig.ExtraCostTicksPerSide is <0 or >2||activeConfig.ExtraCostTicksPerSide%1!=0||activeConfig.ResearchReadinessProfile!="exclude-flow")throw new ArgumentException("Research execution contract changed");
            var manifest=JsonSerializer.Deserialize<CacheManifest>(File.ReadAllText(path+".manifest.json"),ReplayCache.Json)!;
            ReplayCache.ValidateManifest(manifest,EvaluationReplay.Hash(path),true);ReplayCache.ValidateInterval(manifest,activeConfig);
            if(path!=loadedPath){loaded=ReplayCache.Read(path);loadedPath=path;}
            if(loaded!.Zip(loaded!.Skip(1)).Any(p=>p.Second.At<=p.First.At||Math.Abs((p.Second.At-p.First.At).TotalSeconds-3)>.001))throw new InvalidDataException("Non-continuous segment");
            environment=new(loaded!,activeConfig);state=environment.Reset();index=0;metrics=new();pendingContext=null;pendingSide=0;
            mode=request.GetProperty("mode").GetString()!;
            if(mode=="baseline")baseline=new(File.ReadAllText(request.GetProperty("model").GetString()!));
            else if(mode=="actor")
            {
                string model=request.GetProperty("model").GetString()!;actor=new(model,Path.ChangeExtension(model,"manifest.json"));
                if(actor.ResearchReadinessProfile!="exclude-flow")throw new ArgumentException("Actor/cache profile mismatch");
            }
            else if(mode!="wait")throw new ArgumentException("Unknown mode");
        }
        else if(op=="step")
        {
            if(environment==null||state==null||activeConfig==null||loaded==null)throw new InvalidOperationException("Reset required");
            var before=state;var timer=Stopwatch.StartNew();
            if(mode=="actor"){var p=actor!.Predict(state.Observation,state.Mask);action=p.Action;inferenceMs=p.InferenceMs;}
            else if(mode=="baseline")
            {
                prediction=baseline!.Predict(state.Observation);timer.Stop();inferenceMs=timer.Elapsed.TotalMilliseconds;
                int direction=prediction>=2?1:prediction<=-2?-1:0;
                if(state.PendingEntry)action=direction!=pendingSide&&state.Mask[4]?4:0;
                else if(state.Position!=0)action=direction==0&&state.Mask[3]?3:direction>0&&state.Mask[1]?1:direction<0&&state.Mask[2]?2:0;
                else action=direction>0&&state.Mask[1]?1:direction<0&&state.Mask[2]?2:0;
            }
            if(action is 1 or 2&&state.Position==0&&!state.PendingEntry&&state.Mask[action])
            {
                pendingSide=action==1?1:-1;
                pendingContext=new{signalIndex=index,signalAt=state.At,side=pendingSide,predictionTicks=prediction,signalMid=(loaded[index].Bid+loaded[index].Ask)/2};
            }
            state=environment.Step(action);metrics.Observe(before,state);index++;
            foreach(var fill in state.Fills.Where(f=>f.Role is KGIOptionPricing.StrategyFramework.PaperOrderRole.EntryLong or KGIOptionPricing.StrategyFramework.PaperOrderRole.EntryShort))
            {
                if(pendingContext==null)throw new InvalidDataException("Missing entry context");
                contexts.Add(new{fill,pendingContext});pendingContext=null;
            }
            if(state.Position==0&&!state.PendingEntry)pendingContext=null;
        }
        else throw new ArgumentException("Unknown operation");
        object? summary=null;
        if(state!.Terminated||state.Truncated)
        {
            metrics.Validate(state,activeConfig!.Multiplier);
            summary=new{trades=state.CompletedTrades,wins=metrics.Wins,losses=metrics.Losses,
                grossPnlJPY=state.CompletedGrossPnl,netPnlJPY=state.CompletedNetPnl,costsJPY=state.CompletedCosts,
                winningNetJPY=metrics.WinningNetJPY,losingNetJPY=metrics.LosingNetJPY,
                excludedOpenPositions=state.Position==0?0:1,excludedEntryCostsJPY=state.OpenPositionCosts,
                excludedUnrealizedJPY=state.UnrealizedPnl,excludedOpenNetPnlJPY=state.ExcludedOpenNetPnl,rawNetPnlJPY=state.NetPnl,sharedLedgerReconciled=true};
        }
        var frame=loaded![index];
        Console.WriteLine(JsonSerializer.Serialize(new{state,quote=new{frame.At,frame.Bid,frame.Ask,frame.Valid,frame.DataGap},prediction,inferenceMs,action,contexts,summary},ReplayCache.Json));
    }
    catch(Exception e){Console.WriteLine(JsonSerializer.Serialize(new{error=e.ToString()},ReplayCache.Json));}
}
actor?.Dispose();
