using System.Security.Cryptography;
using NikkeiRl;
namespace KGIOptionPricing.Strategies.NikkeiRlDirection;

public sealed record NikkeiRlParameters
{
    public const string ModelId="pruned-earlier-seed22";
    public const string ModelSha="D6E19A8A791782C74F1DE2746B39EB231D938C53BDF3BE72EB98DA354AD0AA77";
    public const string ManifestSha="9BD4567F8D60A22715A38D3E80849E15684FB0749B70CEE6EA8B31AA6A4D9DB1";
    public const string ReadinessProfile="exclude-flow";
    public string ModelDirectory {get;init;}=Path.Combine(AppContext.BaseDirectory,"Strategies","NikkeiRlDirection","models",ModelId);
    public bool EnableNightSession {get;init;}=true;
    public bool LiveTradingEnabled {get;init;}
    public int ActionLatencyMs=>LiveTradingEnabled?0:1;
    public static NikkeiRlParameters Parse(IReadOnlyDictionary<string,string> values)
    {
        var p=new NikkeiRlParameters();
        foreach(var pair in values){
            if(pair.Key is "EnableNightSession" or "LiveTradingEnabled"){
                if(!bool.TryParse(pair.Value,out var enabled))throw new ArgumentException($"布林參數無效：{pair.Key}");
                p=pair.Key=="EnableNightSession"?p with{EnableNightSession=enabled}:p with{LiveTradingEnabled=enabled};
            }
        }
        var expected=p.ToDictionary();
        foreach(var pair in values)
        {
            if(pair.Key=="ModelDirectory"){p=p with{ModelDirectory=Path.GetFullPath(pair.Value)};continue;}
            if(pair.Key is "EnableNightSession" or "LiveTradingEnabled")continue;
            if(!expected.TryGetValue(pair.Key,out var value)||pair.Value!=value)throw new ArgumentException($"凍結策略參數不可修改：{pair.Key}");
        }
        return p;
    }
    public Dictionary<string,string> ToDictionary()=>new(StringComparer.OrdinalIgnoreCase){
        ["ModelId"]=ModelId,["ModelDirectory"]=ModelDirectory,["ModelSha256"]=ModelSha,["ManifestSha256"]=ManifestSha,
        ["DecisionIntervalSeconds"]="3",["PredictionHorizonSeconds"]="15",["DecisionLatencyMs"]=ActionLatencyMs.ToString(System.Globalization.CultureInfo.InvariantCulture),["CooldownSeconds"]="10",
        ["MaxHoldingSeconds"]="0",["FixedStopPoints"]="150",["MaxStopPoints"]="150",["CommissionPerSide"]="36",
        ["Quantity"]="1",["EntryPriceMode"]="same-side",["EntryTimeoutSeconds"]="10",["ResearchReadinessProfile"]=ReadinessProfile,
        ["ExtraCostTicksPerSide"]="0",["SummaryOnly"]="true",
        ["EnableNightSession"]=EnableNightSession.ToString().ToLowerInvariant(),["LiveTradingEnabled"]=LiveTradingEnabled.ToString().ToLowerInvariant(),
        ["DaySession"]="07:45-14:45",["NightSession"]="16:00-05:00",["PreCloseSeconds"]="310"};
    public static string SummaryRoot=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"KGIOptionPricing","NikkeiRlPerformance");
}
public interface IRlMainPolicy:IDisposable { RlPolicyPrediction Predict(double[] observation,bool[] mask); }
public sealed class FrozenRlMainPolicy:IRlMainPolicy
{
    readonly RlPolicyPredictor actor;
    public FrozenRlMainPolicy(NikkeiRlParameters p)
    {
        var model=Path.Combine(p.ModelDirectory,"policy-seed22.onnx");var manifest=Path.Combine(p.ModelDirectory,"policy-seed22.manifest.json");
        static string Hash(string path){using var input=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(input));}
        if(Hash(manifest)!=NikkeiRlParameters.ManifestSha||Hash(model)!=NikkeiRlParameters.ModelSha)throw new InvalidDataException("模型或參數指紋不符，停止載入");
        actor=new(model,manifest);
    }
    public RlPolicyPrediction Predict(double[] observation,bool[] mask)=>actor.Predict(observation,mask);
    public void Dispose()=>actor.Dispose();
}
