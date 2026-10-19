using System.Globalization;
using KGIOptionPricing.Strategies.BuySellPower;
using KGIOptionPricing.StrategyFramework;
namespace KGIOptionPricing.Strategies.PowerReversal;

public sealed class PowerReversalParameters
{
    private static readonly (string Key, decimal Default, decimal Min, decimal Max, bool Integer)[] Schema = {
        ("PowerWindowSeconds",30,1,3600,true),("ShortAverageSeconds",60,1,86400,true),("LongAverageSeconds",180,1,86400,true),
        ("ReversalPoints",30,0.01m,10000,false),("MovementAveragePeriods",20,1,10000,true),
        ("PowerThreshold",20,0,100,false),("BaisThreshold",10,0,200,false),("ConfirmSeconds",5,1,300,true),
        ("RequireShortAverageDirection",1,0,1,true),
        ("AllowRiskRewardPullback",0,0,1,true),
        ("SignalMode",0,0,1,true),("StopTicks",6,1,1000,true),("TargetTicks",16,1,1000,true),("CooldownSeconds",180,1,3600,true),
        ("RequireOppositeMovement",0,0,1,true),
        ("RequireAlignedMovement",0,0,1,true),
        ("PriceRecoveryTicks",0,0,1000,true),
        ("MinJointPower",0,0,100,false),("MaxPowerDifference",200,0,200,false),("MinShortAveragePower",0,0,100,false),
        ("AdaptiveExitMode",0,0,1,true),("StopMovementFraction",0.5m,0.05m,3,false),("TargetMovementFraction",1,0.05m,5,false),
        ("AdaptiveMinStopTicks",2,1,1000,true),("AdaptiveMaxStopTicks",12,1,1000,true),("AdaptiveMaxTargetTicks",40,1,2000,true),
        ("AdaptiveForceMode",0,0,1,true),("AdaptiveAgreementMode",0,0,1,true),("AdaptiveHoldingMode",0,0,1,true),
        ("AdaptiveLookbackSeconds",300,60,3600,true),("AdaptiveMinSamples",120,10,3600,true),("AdaptiveForcePercentile",70,50,95,false),
        ("AdaptiveHoldingFraction",0.5m,0.1m,3,false),("AdaptiveMinHoldingSeconds",60,1,86400,true),
        ("MovementRatio",0.8m,0,10,false),("MinMovementSamples",5,1,10000,true),
        ("MinQuantity",100,1,10000000,true),("MinTradeCount",5,1,100000,true),
        ("MaxDataAgeSeconds",2,0.1m,60,false),("MaxSkewSeconds",1,0,60,false),("MaxSpreadTicks",2,1,100,false),
        ("MinRewardRisk",1.5m,0.1m,20,false),("StopBufferTicks",1,1,100,true),("SlippageTicks",2,0,100,true),
        ("CommissionPerSide",36,0,100000,false),("MaxHoldingSeconds",180,1,86400,true),
        ("ExitConfirmSeconds",5,1,300,true),("EntryTimeoutSeconds",10,1,300,true),("CloseBeforeSeconds",60,1,3600,true),
        ("PowerExitMode",0,0,2,true),("ExitWeakPowerThreshold",10,-100,100,false),
        ("TrailingExitMode",0,0,1,true),("TrailingActivateRisk",1,0.1m,5,false),("TrailingDistanceRisk",0.5m,0.1m,3,false),
        ("EntryExecutionMode",0,0,1,true),("WeightedMidMode",0,0,1,true)
    };
    private readonly Dictionary<string,decimal> _values=new(StringComparer.OrdinalIgnoreCase);
    public decimal this[string key] => _values[key];
    public PowerParameters Power => new((int)this["PowerWindowSeconds"],(int)this["ShortAverageSeconds"],(int)this["LongAverageSeconds"],this["ReversalPoints"],(int)this["MovementAveragePeriods"]);
    public int ConfirmSeconds => (int)this["ConfirmSeconds"];
    public decimal MinRewardRisk => this["MinRewardRisk"];
    /// <summary>Original entry profile with user-selected 1R/0.5R exit from the 20261008 comparison.</summary>
    public static PowerReversalParameters BestCandidateProfile() => Parse(new Dictionary<string,string>
    {
        ["PowerWindowSeconds"]="10",
        ["ShortAverageSeconds"]="10",
        ["LongAverageSeconds"]="60",
        ["ReversalPoints"]="30",
        ["MovementAveragePeriods"]="20",
        ["PowerThreshold"]="10",
        ["BaisThreshold"]="3",
        ["ConfirmSeconds"]="1",
        ["RequireShortAverageDirection"]="1",
        ["AllowRiskRewardPullback"]="1",
        ["SignalMode"]="1",
        ["StopTicks"]="8",
        ["TargetTicks"]="16",
        ["CooldownSeconds"]="20",
        ["RequireOppositeMovement"]="0",
        ["MovementRatio"]="0.6",
        ["MinMovementSamples"]="5",
        ["MinQuantity"]="30",
        ["MinTradeCount"]="5",
        ["MaxDataAgeSeconds"]="2",
        ["MaxSkewSeconds"]="1",
        ["MaxSpreadTicks"]="2",
        ["MinRewardRisk"]="1.5",
        ["StopBufferTicks"]="1",
        ["SlippageTicks"]="2",
        ["CommissionPerSide"]="36",
        ["MaxHoldingSeconds"]="300",
        ["ExitConfirmSeconds"]="30",
        ["EntryTimeoutSeconds"]="10",
        ["CloseBeforeSeconds"]="60",
        ["RequireAlignedMovement"]="1",
        ["PriceRecoveryTicks"]="0",
        ["MinJointPower"]="40",
        ["MaxPowerDifference"]="20",
        ["MinShortAveragePower"]="10",
        ["AdaptiveExitMode"]="1",
        ["StopMovementFraction"]="0.5",
        ["TargetMovementFraction"]="1.25",
        ["AdaptiveMinStopTicks"]="2",
        ["AdaptiveMaxStopTicks"]="12",
        ["AdaptiveMaxTargetTicks"]="40",
        ["AdaptiveForceMode"]="1",
        ["AdaptiveAgreementMode"]="0",
        ["AdaptiveHoldingMode"]="0",
        ["AdaptiveLookbackSeconds"]="300",
        ["AdaptiveMinSamples"]="120",
        ["AdaptiveForcePercentile"]="70",
        ["AdaptiveHoldingFraction"]="0.5",
        ["AdaptiveMinHoldingSeconds"]="60",
        ["PowerExitMode"]="0",
        ["ExitWeakPowerThreshold"]="10",
        ["TrailingExitMode"]="1",
        ["TrailingActivateRisk"]="1",
        ["TrailingDistanceRisk"]="0.5",
        ["EntryExecutionMode"]="1",
        ["WeightedMidMode"]="0",
    });
    public static PowerReversalParameters ResearchProfile() => Parse(new Dictionary<string,string>
    {
        ["MinQuantity"]="30",["PowerThreshold"]="10",["BaisThreshold"]="5",["ConfirmSeconds"]="2",
        ["MovementRatio"]="0.6",["RequireShortAverageDirection"]="0",["AllowRiskRewardPullback"]="1"
    });
    public static PowerReversalParameters FrequencyProfile()
    {
        var values=ResearchProfile().ToDictionary();
        foreach(var pair in new Dictionary<string,string>{["SignalMode"]="1",["StopTicks"]="6",["TargetTicks"]="16",["CooldownSeconds"]="300",
            ["PowerWindowSeconds"]="10",["ShortAverageSeconds"]="10",["LongAverageSeconds"]="60",["PowerThreshold"]="5",["BaisThreshold"]="1",["ConfirmSeconds"]="1"})values[pair.Key]=pair.Value;
        return Parse(values);
    }
    public static PowerReversalParameters FilteredProfile()
    {
        var values=FrequencyProfile().ToDictionary();values["RequireOppositeMovement"]="1";values["CooldownSeconds"]="360";
        return Parse(values);
    }
    public static PowerReversalParameters Parse(IReadOnlyDictionary<string,string> input)
    {
        var p=new PowerReversalParameters();
        foreach(var item in Schema) {
            decimal value=item.Default;
            if(input.TryGetValue(item.Key,out var text) && !decimal.TryParse(text,NumberStyles.Number,CultureInfo.InvariantCulture,out value)) throw new ArgumentException(item.Key+" 格式無效");
            if(value<item.Min || value>item.Max || item.Integer && value!=decimal.Truncate(value)) throw new ArgumentOutOfRangeException(item.Key);
            p._values[item.Key]=value;
        }
        p.Power.Validate();
        if(p["EntryExecutionMode"]==1 && p["SignalMode"]!=1)throw new ArgumentException("加權中價混合進場需連續力道模式");
        if((p["AdaptiveForceMode"]==1 || p["AdaptiveAgreementMode"]==1 || p["AdaptiveHoldingMode"]==1) && p["SignalMode"]!=1)throw new ArgumentException("自適應參數需連續力道模式");
        if(p["AdaptiveMinSamples"]>p["AdaptiveLookbackSeconds"])throw new ArgumentException("自適應樣本數不可超過秒數視窗");
        if(p["AdaptiveHoldingMode"]==1 && p["AdaptiveMinHoldingSeconds"]>p["MaxHoldingSeconds"])throw new ArgumentException("自適應持倉下限不可超過上限");
        if(p["AdaptiveExitMode"]==1 && p["SignalMode"]!=1)throw new ArgumentException("自適應出場需連續力道模式");
        if(p["AdaptiveMinStopTicks"]>p["AdaptiveMaxStopTicks"])throw new ArgumentException("自適應停損下限不可超過上限");
        if(p["MinMovementSamples"]>p["MovementAveragePeriods"]) throw new ArgumentException("最少樣本數不可大於平均期數");
        if(p["MaxSkewSeconds"]>p["MaxDataAgeSeconds"]) throw new ArgumentException("商品時間差不可大於資料有效秒數");
        if(p["RequireOppositeMovement"]==1 && p["RequireAlignedMovement"]==1) throw new ArgumentException("同向行情與反向行情濾網不可同時啟用");
        if(p["PriceRecoveryTicks"]>0 && (p["SignalMode"]!=1 || p["RequireOppositeMovement"]!=1)) throw new ArgumentException("價格回復濾網需使用連續力道模式與反向行情濾網");
        return p;
    }
    public Dictionary<string,string> ToDictionary() => _values.ToDictionary(x=>x.Key,x=>x.Value.ToString(CultureInfo.InvariantCulture),StringComparer.OrdinalIgnoreCase);
    public static void EnsureCommission(StrategyDefinition definition,decimal actual)
    {if(definition.StrategyType==PowerReversalFactory.TypeName && Parse(definition.Parameters)["CommissionPerSide"]!=actual)throw new ArgumentException("訊號費用與執行帳本費用不同，請同步設定 CommissionPerSide。");}
    public Dictionary<string,string> IndicatorParameters() => BuySellPowerStrategy.Products.SelectMany(p=>Power.ToDictionary(p+".")).ToDictionary(x=>x.Key,x=>x.Value);
}

/// <summary>Continuous pre-reversal arming; the price confirmation is checked by the strategy.</summary>
public sealed class ReversalSignalGate(PowerReversalParameters parameters)
{
    public AdaptiveSignalThresholds? Adaptive {get;set;}
    public decimal PowerThreshold=>Adaptive?.Power??parameters["PowerThreshold"];
    public decimal JointThreshold=>Adaptive?.Joint??parameters["MinJointPower"];
    public decimal DifferenceThreshold=>Adaptive?.Difference??parameters["MaxPowerDifference"];
    private DateTimeOffset? _since;
    private int _direction;
    public void Reset() { _since=null;_direction=0; }
    public decimal ElapsedSeconds(DateTimeOffset at)=>_since.HasValue?(decimal)(at-_since.Value).TotalSeconds:0;
    public bool PowerMatches(PowerSnapshot s,int direction) => s.IsWarm && s.BuyPower*direction>=PowerThreshold &&
        (parameters["RequireShortAverageDirection"]==0 || s.ShortAverage*direction>0) && s.BaisPower*direction>=parameters["BaisThreshold"];
    public static decimal? RecoveryTicks(PowerSnapshot s,int direction,decimal? price,decimal tickSize)=>s.CurrentMovement!=null && price.HasValue && tickSize>0
        ?(price.Value-s.CurrentMovement.ExtremePrice)*direction/tickSize:null;
    public static decimal? PowerDifference(PowerSnapshot mini,PowerSnapshot micro)=>mini.BuyPower.HasValue && micro.BuyPower.HasValue?Math.Abs(mini.BuyPower.Value-micro.BuyPower.Value):null;
    public bool ConsensusMatches(PowerSnapshot mini,PowerSnapshot micro,int direction) =>
        (JointThreshold==0 || (mini.BuyPower+micro.BuyPower)*direction/2>=JointThreshold) &&
        (DifferenceThreshold==200 || PowerDifference(mini,micro)<=DifferenceThreshold) &&
        (parameters["MinShortAveragePower"]==0 || mini.ShortAverage*direction>=parameters["MinShortAveragePower"] && micro.ShortAverage*direction>=parameters["MinShortAveragePower"]);
    public int Update(DateTimeOffset at, PowerSnapshot mini, PowerSnapshot micro, bool healthy, decimal? microPrice=null, decimal tickSize=0)
    {
        if(parameters["SignalMode"]==1)
        {
            int signal=micro.BuyPower>0?1:micro.BuyPower<0?-1:0;
            bool movement=parameters["RequireOppositeMovement"]==0 || micro.CurrentMovement?.Direction==(signal>0?"下跌":"上漲");
            movement &= parameters["RequireAlignedMovement"]==0 || micro.CurrentMovement?.Direction==(signal>0?"上漲":"下跌");
            movement &= parameters["PriceRecoveryTicks"]==0 || RecoveryTicks(micro,signal,microPrice,tickSize)>=parameters["PriceRecoveryTicks"];
            if(!healthy || signal==0 || !movement || !PowerMatches(mini,signal) || !PowerMatches(micro,signal) || !ConsensusMatches(mini,micro,signal)){Reset();return 0;}
            if(_direction!=signal || _since==null){_direction=signal;_since=at;}
            return at>=_since.Value.AddSeconds(parameters.ConfirmSeconds)?signal:0;
        }
        int direction=micro.CurrentMovement?.Direction=="下跌"?1:micro.CurrentMovement?.Direction=="上漲"?-1:0;
        decimal? average=direction>0?micro.AverageDownPoints:micro.AverageUpPoints;
        bool samples=micro.UpSamples>=parameters["MinMovementSamples"] && micro.DownSamples>=parameters["MinMovementSamples"];
        bool match=healthy && direction!=0 && samples && average>0 && micro.CurrentMovement!.Points>=average*parameters["MovementRatio"] &&
            PowerMatches(mini,direction) && PowerMatches(micro,direction) && ConsensusMatches(mini,micro,direction);
        if(!match) {Reset();return 0;}
        if(_direction!=direction || _since==null) {_direction=direction;_since=at;}
        return at>=_since.Value.AddSeconds(parameters.ConfirmSeconds)?direction:0;
    }
}
