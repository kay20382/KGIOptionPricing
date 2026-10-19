using System.Text.Json;
using KGIOptionPricing.StrategyFramework;

namespace NikkeiRl;

public sealed record CounterfactualOutcome(string Status,bool Completed,RlState State,ExecutionEvent[] Fills);
public sealed record ExecutableExample(DateTimeOffset At,double[] Observation,int Action,int? ProxyAction,CounterfactualOutcome Long,CounterfactualOutcome Short);

public static class ExecutableLabels
{
    public static ExecutableExample? Build(ReplayFrame[] frames,int index,RlConfig config,int horizonSeconds=30)
    {
        if(horizonSeconds is not (15 or 30)||horizonSeconds%config.DecisionIntervalSeconds!=0)throw new ArgumentException("Unsupported prediction horizon/cadence");
        var at=frames[index].At;
        var window=frames.Skip(index).TakeWhile(f=>(f.At-at).TotalSeconds<=90).ToArray();
        if(window.Length<2)return null;
        if(window.Any(f=>f.DataGap)||window.Zip(window.Skip(1)).Any(p=>Math.Abs((p.Second.At-p.First.At).TotalSeconds-config.DecisionIntervalSeconds)>.001))throw new InvalidDataException("Counterfactual gap");
        var initial=new RlReplayEnvironment(window,config).Reset();
        if(!initial.Mask[1]||!initial.Mask[2])return null;
        CounterfactualOutcome Outcome(int entry)
        {
            var env=new RlReplayEnvironment(window,config);var state=env.Reset();var fills=new List<ExecutionEvent>();
            int action=entry;
            while(!state.Terminated&&!state.Truncated)
            {
                state=env.Step(action);fills.AddRange(state.Fills);
                if(state.CompletedTrades==1&&state.Position==0)return new("Completed",true,state,fills.ToArray());
                if(state.CompletedTrades==0&&state.Position==0&&!state.PendingEntry&&(state.At-at).TotalSeconds>=15)return new("Unfilled",false,state,fills.ToArray());
                action=(state.At-at).TotalSeconds>=horizonSeconds&&state.Mask[3]?3:0;
            }
            return new("Censored",false,state,fills.ToArray());
        }
        var longResult=Outcome(1);var shortResult=Outcome(2);
        if(longResult.Status=="Censored"||shortResult.Status=="Censored")return null;
        decimal longNet=longResult.State.CompletedNetPnl,shortNet=shortResult.State.CompletedNetPnl;
        int best=longNet<=0&&shortNet<=0?0:longNet>=shortNet?1:2;
        var future=window.FirstOrDefault(f=>f.At==at.AddSeconds(horizonSeconds));
        var targetWindow=window.TakeWhile(f=>f.At<=at.AddSeconds(horizonSeconds)).ToArray();
        int feature=horizonSeconds==15?1:2;
        int? proxy=future!=null&&targetWindow.Length==horizonSeconds/config.DecisionIntervalSeconds+1&&targetWindow.All(f=>f.Valid)?future.Features[feature]>2?1:future.Features[feature]<-2?2:0:null;
        return new(at,initial.Observation,best,proxy,longResult,shortResult);
    }
    public static string[] ValidateEpisodes(IEnumerable<string> episodes)
    {
        var ids=episodes.ToArray();
        if(ids.Length==0||ids.Distinct().Count()!=ids.Length||ids.Any(id=>id.Length<12||!System.DateTime.TryParseExact(id[..8],"yyyyMMdd",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out _)||string.CompareOrdinal(id[..8],"20260928")<0||string.CompareOrdinal(id[..8],"20261005")>0||!id.EndsWith("-day",StringComparison.Ordinal)))
            throw new ArgumentException("Executable training dates must be unique and within September28 through October5; development dates forbidden");
        return ids;
    }
    public static void Generate(string datasetPath,string outputPath,string[]? trainingEpisodes=null,int decisionIntervalSeconds=5,int horizonSeconds=30,string featureProfile="three-product")
    {
        if(File.Exists(outputPath)||File.Exists(outputPath+".manifest.json"))throw new IOException("Immutable executable labels already exist");
        var dataset=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(datasetPath),ReplayCache.Json)!;
        var required=ValidateEpisodes(trainingEpisodes??new[]{"20260928-postgap-day","20260929-day","20261005-postgap-day"});
        bool research=featureProfile=="mini-micro-research";bool power=featureProfile is "mini-micro-power" or "mini-micro-research";
        var config=new RlConfig{RewardCostWeight=1,ObservationSize=research?60:40,DecisionIntervalSeconds=decisionIntervalSeconds,FeatureProfile=featureProfile,DirectionDrivenHolding=power,MaxHoldingSeconds=power?0:60,StopMode=research?"fixed":"legacy-fixed",FixedStopPoints=research?150:20,MaxStopPoints=research?150:null};var sources=new List<object>();
        long examples=0,matched=0,censored=0,flatEligible=0;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        using(var writer=new StreamWriter(new FileStream(outputPath,FileMode.CreateNew)))
        {
            foreach(var id in required)
            {
                string path=dataset[id];var manifest=JsonSerializer.Deserialize<CacheManifest>(File.ReadAllText(path+".manifest.json"),ReplayCache.Json)!;
                ReplayCache.ValidateManifest(manifest,EvaluationReplay.Hash(path));
                ReplayCache.ValidateInterval(manifest,config);
                if(EvaluationReplay.Hash(manifest.InputPath)!=manifest.InputSha256)throw new InvalidDataException("Counterfactual source hash changed");
                var frames=ReplayCache.Read(path);long count=0;
                if(frames.Any(f=>f.At.ToString("yyyyMMdd")!=id[..8]))throw new InvalidDataException("Training episode/date mismatch");
                for(int i=0;i<frames.Length-1;i++)
                {
                    if(!frames[i].Valid||(new DateTimeOffset(frames[i].At.Date.AddHours(14.75),frames[i].At.Offset)-frames[i].At).TotalSeconds<=310)continue;
                    flatEligible++;var row=Build(frames,i,config,horizonSeconds);if(row==null){censored++;continue;}
                    writer.WriteLine(JsonSerializer.Serialize(new{episode=id,example=row},ReplayCache.Json));examples++;count++;if(row.ProxyAction.HasValue)matched++;
                }
                sources.Add(new{episode=id,path,manifest.CacheSha256,manifest.InputPath,manifest.InputSha256,frames=frames.Length,examples=count});
                Console.WriteLine($"{id}: {count} executable examples");
            }
        }
        File.WriteAllText(outputPath+".manifest.json",JsonSerializer.Serialize(new{schema="nikkei-executable-labels-v1",trainingEpisodes=required,labelSha256=EvaluationReplay.Hash(outputPath),config,horizonSeconds,maxReplaySeconds=90,flatEligible,examples,matchedProxySamples=matched,censored,sources,usage="TrainingOnlyCounterfactual",deploymentEligible=false},ReplayCache.Json));
    }
}
