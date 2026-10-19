using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace NikkeiRl;

public sealed record RlPolicyPrediction(int Action,double[] Probabilities,double InferenceMs);

/// <summary>Reusable Windows x64 C# actor. One warm session per instance; fixed schema and hash.</summary>
public sealed class RlPolicyPredictor:IDisposable
{
    private readonly InferenceSession session;
    public double ColdLoadMs {get;}
    public int ObservationSize {get;}
    public string Schema {get;}
    public string FeatureProfile {get;}
    public string ResearchReadinessProfile {get;}
    public RlPolicyPredictor(string modelPath,string manifestPath)
    {
        var sw=Stopwatch.StartNew();
        using var manifest=JsonDocument.Parse(File.ReadAllText(manifestPath));var root=manifest.RootElement;
        ObservationSize=root.GetProperty("observationSize").GetInt32();Schema=root.GetProperty("schema").GetString()!;
        ValidateReadinessContract(root);
        ResearchReadinessProfile=root.TryGetProperty("environmentConfig",out var env)&&env.TryGetProperty("researchReadinessProfile",out var readiness)?readiness.GetString()!:"all-research";
        bool research=Schema is "nikkei-rl-research-v1" or "nikkei-rl-research-pruned-flow-v1";
        bool power=Schema=="nikkei-rl-power-v1"||research;FeatureProfile=research?"mini-micro-research":power?"mini-micro-power":"three-product";
        string[] actions=power?["Wait","LongSignal","ShortSignal","Flatten","CancelEntry"]:["Wait","EnterLong","EnterShort","Flatten","CancelEntry"];
        if(power)
        {
            string[] market=["return5s","return15s","return30s","return60s","spread","obi1","obi5","bookAgeSeconds","logVolume5s","uptickVolumeRatio5s"];
            string[] powerNames=["buyPower30s","shortPower60s","longPower180s","biasPower","movementDirection"];
            string[] state=["position","holdingSecondsDiv60","unrealizedTicks","netPnlTicks","drawdownTicks","cooldownSecondsDiv30","pendingEntry","pendingExit","validData","hoursToSessionClose"];
            var expected=new[]{"225MC","225M"}.SelectMany(p=>market.Select(n=>p+"_"+n)).Concat(new[]{"225MC","225M"}.SelectMany(p=>powerNames.Select(n=>p+"_"+n))).Concat(state);
            if(research)expected=expected.Concat(ResearchFeatureAccumulator.Names);
            var config=root.GetProperty("environmentConfig");
            if(!root.GetProperty("featureNames").EnumerateArray().Select(x=>x.GetString()).SequenceEqual(expected)||config.GetProperty("featureProfile").GetString()!=FeatureProfile||!config.GetProperty("directionDrivenHolding").GetBoolean()||config.GetProperty("maxHoldingSeconds").GetInt32()!=0)throw new ArgumentException("Invalid power feature/holding schema");
        }
        using var file=File.OpenRead(modelPath);var actual=Convert.ToHexString(SHA256.HashData(file));
        if(root.GetProperty("modelSha256").GetString()!=actual || !((Schema=="nikkei-rl-v1"&&ObservationSize==40)||(Schema=="nikkei-rl-v2"&&ObservationSize==50)||(Schema=="nikkei-rl-v3"&&ObservationSize==48)||(Schema=="nikkei-rl-v4"&&ObservationSize==56)||(power&&!research&&ObservationSize==40)||(research&&ObservationSize==60)) || !root.GetProperty("actionNames").EnumerateArray().Select(x=>x.GetString()).SequenceEqual(actions))throw new ArgumentException("Invalid policy schema or SHA-256");
        using var options=new SessionOptions{IntraOpNumThreads=1,InterOpNumThreads=1,GraphOptimizationLevel=GraphOptimizationLevel.ORT_ENABLE_ALL};
        session=new InferenceSession(modelPath,options);
        if(session.InputMetadata.Count!=1||session.OutputMetadata.Count!=1||!session.InputMetadata.ContainsKey("observation") || !session.OutputMetadata.ContainsKey("logits")||session.InputMetadata["observation"].Dimensions.Length!=2||session.InputMetadata["observation"].Dimensions[1]!=ObservationSize||session.OutputMetadata["logits"].Dimensions.Length!=2||session.OutputMetadata["logits"].Dimensions[1]!=5){session.Dispose();throw new ArgumentException("Invalid ONNX signature");}
        ColdLoadMs=sw.Elapsed.TotalMilliseconds;
        // No trading during warm-up; raw observation normalization is embedded in ONNX.
        Predict(new double[ObservationSize],new[]{true,false,false,false,false});
    }

    public static void ValidateReadinessContract(JsonElement root)
    {
        string? schema=root.GetProperty("schema").GetString();
        bool pruned=schema=="nikkei-rl-research-pruned-flow-v1";
        bool hasConfig=root.TryGetProperty("environmentConfig",out var config);
        string? profile=hasConfig&&config.TryGetProperty("researchReadinessProfile",out var value)?value.GetString():"all-research";
        if(profile is not ("all-research" or "exclude-flow")||pruned!=(profile=="exclude-flow"))throw new ArgumentException("Policy schema/readiness mismatch");
        if(!pruned)return;
        if(root.GetProperty("observationSize").GetInt32()!=60||config.GetProperty("featureProfile").GetString()!="mini-micro-research"||config.GetProperty("decisionIntervalSeconds").GetInt32()!=3)
            throw new ArgumentException("Pruned flow requires research60 at three seconds");
        if(!root.TryGetProperty("actorExcludedFeatures",out var mask)||mask.ValueKind!=JsonValueKind.Array)throw new ArgumentException("Pruned flow mask required");
        var indices=mask.EnumerateArray().Select(x=>x.GetInt32()).ToArray();
        if(indices.Any(i=>i<0||i>=60)||indices.Distinct().Count()!=indices.Length||!Enumerable.Range(48,6).All(indices.Contains))
            throw new ArgumentException("All six flow inputs must be excluded with unique raw60 indices");
    }

    public RlPolicyPrediction Predict(double[] observation,bool[] mask)
    {
        var start=Stopwatch.GetTimestamp();
        if(observation.Length!=ObservationSize || observation.Any(x=>!double.IsFinite(x)))throw new ArgumentException("Invalid observation");
        var tensor=new DenseTensor<float>(observation.Select(x=>(float)x).ToArray(),new[]{1,ObservationSize});
        using var values=session.Run(new[]{NamedOnnxValue.CreateFromTensor("observation",tensor)});
        var prediction=SelectAction(values.First(x=>x.Name=="logits").AsTensor<float>().ToArray(),mask);
        return prediction with{InferenceMs=Stopwatch.GetElapsedTime(start).TotalMilliseconds};
    }

    public static RlPolicyPrediction SelectAction(float[] logits,bool[] mask)
    {
        if(logits.Length!=5 || mask.Length!=5 || !mask.Any(x=>x) || logits.Any(x=>!float.IsFinite(x)))throw new ArgumentException("Invalid mask/logits");
        double max=Enumerable.Range(0,5).Where(i=>mask[i]).Max(i=>(double)logits[i]);
        var weights=Enumerable.Range(0,5).Select(i=>mask[i]?Math.Exp(logits[i]-max):0).ToArray();double sum=weights.Sum();
        int action=Enumerable.Range(0,5).Where(i=>mask[i]).OrderByDescending(i=>logits[i]).ThenBy(i=>i).First();
        return new(action,weights.Select(x=>x/sum).ToArray(),0);
    }
    public void Dispose()=>session.Dispose();
}
