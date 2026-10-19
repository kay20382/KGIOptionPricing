using System.Globalization;
using KGIOptionPricing.StrategyFramework;
namespace KGIOptionPricing.Strategies.MultiFactorMeanR17;

public sealed class R17Parameters
{
    static readonly Dictionary<string,string> Defaults=new(StringComparer.OrdinalIgnoreCase)
    {
        ["AdaptiveAgreementMode"]="0",
        ["AdaptiveExitMode"]="0",
        ["AdaptiveForceMode"]="0",
        ["AdaptiveForcePercentile"]="70",
        ["AdaptiveHoldingFraction"]="0.5",
        ["AdaptiveHoldingMode"]="0",
        ["AdaptiveLookbackSeconds"]="300",
        ["AdaptiveMaxStopTicks"]="12",
        ["AdaptiveMaxTargetTicks"]="40",
        ["AdaptiveMinHoldingSeconds"]="60",
        ["AdaptiveMinSamples"]="120",
        ["AdaptiveMinStopTicks"]="2",
        ["AllowRiskRewardPullback"]="1",
        ["BaisThreshold"]="3",
        ["CloseBeforeSeconds"]="60",
        ["CommissionPerSide"]="36",
        ["ConfirmSeconds"]="1",
        ["CooldownSeconds"]="20",
        ["EntryExecutionMode"]="1",
        ["EntryTimeoutSeconds"]="10",
        ["ExitConfirmSeconds"]="30",
        ["ExitWeakPowerThreshold"]="10",
        ["LongAverageSeconds"]="60",
        ["MaxDataAgeSeconds"]="2",
        ["MaxHoldingSeconds"]="180",
        ["MaxPowerDifference"]="20",
        ["MaxSkewSeconds"]="1",
        ["MaxSpreadTicks"]="2",
        ["MinJointPower"]="40",
        ["MinMovementSamples"]="5",
        ["MinQuantity"]="30",
        ["MinRewardRisk"]="0.9",
        ["MinShortAveragePower"]="10",
        ["MinTradeCount"]="5",
        ["MovementAveragePeriods"]="20",
        ["MovementRatio"]="0.6",
        ["PowerExitMode"]="0",
        ["PowerThreshold"]="10",
        ["PowerWindowSeconds"]="10",
        ["PriceRecoveryTicks"]="0",
        ["RequireAlignedMovement"]="1",
        ["RequireOppositeMovement"]="0",
        ["RequireShortAverageDirection"]="1",
        ["ResearchAnchorRiskToFill"]="1",
        ["ResearchDecisionFrameMilliseconds"]="250",
        ["ResearchFactorConfirmSeconds"]="0",
        ["ResearchFactorMode"]="3",
        ["ResearchFlowFloor"]="-0.2",
        ["ResearchMinLargePrintRate"]="0.3",
        ["ResearchMinLargeVariancePressure"]="-0.1",
        ["ResearchMinSignedMiniLastReturnTicks"]="-0.5",
        ["ResearchQueueFloor"]="-0.3",
        ["ResearchRequireMomentum"]="0",
        ["ResearchRequireOfi"]="0",
        ["ResearchScoreThreshold"]="0.25",
        ["ResearchUseLargeVariancePressure"]="1",
        ["ResearchUseMiniLastReturnFloor"]="1",
        ["ResearchVolatilityMaxStopTicks"]="30",
        ["ResearchVolatilityMaxTargetTicks"]="60",
        ["ResearchVolatilityMinStopTicks"]="4",
        ["ResearchVolatilityRewardRatio"]="1.5",
        ["ResearchVolatilityRiskMultiplier"]="2",
        ["ReversalPoints"]="30",
        ["ShortAverageSeconds"]="10",
        ["SignalMode"]="1",
        ["SlippageTicks"]="2",
        ["StopBufferTicks"]="1",
        ["StopMovementFraction"]="0.5",
        ["StopTicks"]="30",
        ["TargetMovementFraction"]="1.25",
        ["TargetTicks"]="40",
        ["TrailingActivateRisk"]="0.8",
        ["TrailingDistanceRisk"]="0.5",
        ["TrailingExitMode"]="1",
        ["WeightedMidMode"]="0",
    };
    readonly Dictionary<string,string> _values;
    R17Parameters(Dictionary<string,string> values){_values=values;}
    public decimal this[string key]=>decimal.Parse(_values[key],CultureInfo.InvariantCulture);
    public Dictionary<string,string> ToDictionary()=>new(_values,StringComparer.OrdinalIgnoreCase);
    public static R17Parameters BestCandidateProfile()=>Parse(new Dictionary<string,string>());
    public static bool MatchesFrozenProfile(IReadOnlyDictionary<string,string> values)=>Defaults.All(p=>values.TryGetValue(p.Key,out var v) && decimal.TryParse(v,NumberStyles.Number,CultureInfo.InvariantCulture,out var parsed) && parsed==decimal.Parse(p.Value,CultureInfo.InvariantCulture));
    public static R17Parameters Parse(IReadOnlyDictionary<string,string> input)
    {
        var values=new Dictionary<string,string>(Defaults,StringComparer.OrdinalIgnoreCase);
        foreach(var pair in input){if(!values.ContainsKey(pair.Key) && pair.Key!="HistoryRoot")throw new ArgumentException("R17未知參數："+pair.Key);values[pair.Key]=pair.Value;}
        var baseline=R17BaseParameters.Parse(values);
        foreach(var pair in baseline.ToDictionary())values[pair.Key]=pair.Value;
        decimal P(string k)=>decimal.TryParse(values[k],NumberStyles.Number,CultureInfo.InvariantCulture,out var v)?v:throw new ArgumentException(k+"格式無效");
        void Bound(string k,decimal min,decimal max,bool integer=false){var v=P(k);if(v<min||v>max||(integer&&decimal.Truncate(v)!=v))throw new ArgumentOutOfRangeException(k);values[k]=v.ToString(CultureInfo.InvariantCulture);}
        Bound("ResearchFactorMode",1,8,true);Bound("ResearchScoreThreshold",0.001m,1000);
        Bound("ResearchFlowFloor",-1,1);Bound("ResearchQueueFloor",-1,1);
        Bound("ResearchFactorConfirmSeconds",0,300);
        Bound("ResearchDecisionFrameMilliseconds",100,1000,true);
        Bound("ResearchVolatilityRiskMultiplier",0.01m,10);Bound("ResearchVolatilityRewardRatio",0.01m,10);
        Bound("ResearchVolatilityMinStopTicks",1,30,true);Bound("ResearchVolatilityMaxStopTicks",1,30,true);
        Bound("ResearchVolatilityMaxTargetTicks",1,2000,true);Bound("ResearchMinLargePrintRate",0,100000);
        Bound("ResearchMinLargeVariancePressure",-1,1);Bound("ResearchMinSignedMiniLastReturnTicks",-1000,1000);
        foreach(var k in new[]{"ResearchRequireOfi","ResearchRequireMomentum","ResearchUseLargeVariancePressure","ResearchAnchorRiskToFill","ResearchUseMiniLastReturnFloor"})Bound(k,0,1,true);
        if(P("ResearchVolatilityMinStopTicks")>P("ResearchVolatilityMaxStopTicks"))throw new ArgumentException("停損跳數上下限不一致");
        if(P("SignalMode")!=1 || P("AdaptiveExitMode")!=0)throw new ArgumentException("R17需連續訊號模式與固定波動風險模型");
        _=new MultiFactorSignal(values,new FactorState());
        return new(values);
    }
    public static void EnsureCommission(StrategyDefinition definition,decimal actual)
    {if(definition.StrategyType==MultiFactorMeanR17Factory.TypeName && Parse(definition.Parameters)["CommissionPerSide"]!=actual)throw new ArgumentException("R17訊號費用與執行帳本費用不同。");}
}
