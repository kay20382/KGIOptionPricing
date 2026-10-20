using System.Security.Cryptography;
using NikkeiRl;
namespace KGIOptionPricing.Strategies.NikkeiRlDirection;

public sealed record NikkeiRlParameters
{
    public const string ModelId="pruned-earlier-seed22";
    public const string ModelSha="D6E19A8A791782C74F1DE2746B39EB231D938C53BDF3BE72EB98DA354AD0AA77";
    public const string ManifestSha="9BD4567F8D60A22715A38D3E80849E15684FB0749B70CEE6EA8B31AA6A4D9DB1";
    public const string ReadinessProfile="exclude-flow";
    public string PolicyVariant {get;init;}="earlier";
    public bool IsGrid=>PolicyVariant!="earlier";
    public string EffectiveModelId=>IsGrid?PolicyVariant:ModelId;
    public string EffectiveModelSha=>PolicyVariant switch {
        "grid15-positive-net"=>"9DA62F6B696BA8155604D948F3A78771AACD8A6D92B91624FCCFB6FCA0BBE327",
        "grid15-q90-research"=>"4356F1C49CF5413E3F1757E1197A6918D27E4C5BF0C22132D24D07FD7E6403DC",
        _=>ModelSha};
    public string ModelDirectory {get;init;}="";
    public string ResolvedModelDirectory=>string.IsNullOrEmpty(ModelDirectory)?Path.Combine(AppContext.BaseDirectory,"Strategies","NikkeiRlDirection","models",EffectiveModelId):ModelDirectory;
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
        if(values.TryGetValue("PolicyVariant",out var variant)){
            if(variant is not ("earlier" or "grid15-positive-net" or "grid15-q90-research"))throw new ArgumentException("未知模型版本");
            p=p with{PolicyVariant=variant};
            if(p.IsGrid&&!values.ContainsKey("EnableNightSession"))p=p with{EnableNightSession=false};
        }
        if(p.IsGrid&&p.EnableNightSession)throw new ArgumentException("Grid15僅驗證日盤，夜盤未開放");
        var expected=p.ToDictionary();
        foreach(var pair in values)
        {
            if(pair.Key=="ModelDirectory"){p=p with{ModelDirectory=Path.GetFullPath(pair.Value)};continue;}
            if(pair.Key is "EnableNightSession" or "LiveTradingEnabled" or "PolicyVariant")continue;
            if(!expected.TryGetValue(pair.Key,out var value)||pair.Value!=value)throw new ArgumentException($"凍結策略參數不可修改：{pair.Key}");
        }
        return p;
    }
    public Dictionary<string,string> ToDictionary()=>new(StringComparer.OrdinalIgnoreCase){
        ["PolicyVariant"]=PolicyVariant,["ModelId"]=EffectiveModelId,["ModelDirectory"]=ResolvedModelDirectory,["ModelSha256"]=EffectiveModelSha,["ManifestSha256"]=IsGrid?EffectiveModelSha:ManifestSha,
        ["DecisionIntervalSeconds"]="3",["PredictionHorizonSeconds"]=IsGrid?"120":"15",["DecisionLatencyMs"]=ActionLatencyMs.ToString(System.Globalization.CultureInfo.InvariantCulture),["CooldownSeconds"]="10",
        ["MaxHoldingSeconds"]="0",["FixedStopPoints"]="150",["MaxStopPoints"]="150",["CommissionPerSide"]="36",
        ["Quantity"]="1",["EntryPriceMode"]="same-side",["EntryTimeoutSeconds"]="10",["ResearchReadinessProfile"]=ReadinessProfile,
        ["ExtraCostTicksPerSide"]="0",["EntryGridSeconds"]=IsGrid?"15":"3",["SelectionThresholdJPY"]=PolicyVariant=="grid15-q90-research"?"-29.203085902874":"0",["SummaryOnly"]="true",
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
        var model=Path.Combine(p.ResolvedModelDirectory,"policy-seed22.onnx");var manifest=Path.Combine(p.ResolvedModelDirectory,"policy-seed22.manifest.json");
        static string Hash(string path){using var input=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(input));}
        if(Hash(manifest)!=NikkeiRlParameters.ManifestSha||Hash(model)!=NikkeiRlParameters.ModelSha)throw new InvalidDataException("模型或參數指紋不符，停止載入");
        actor=new(model,manifest);
    }
    public RlPolicyPrediction Predict(double[] observation,bool[] mask)=>actor.Predict(observation,mask);
    public void Dispose()=>actor.Dispose();
}
