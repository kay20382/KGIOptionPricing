using System.Globalization;
using KGIOptionPricing.Strategies.PeriodicQueueCycle;
using KGIOptionPricing.StrategyFramework;

namespace KGIOptionPricing;

/// <summary>Shared fixed-width parameter editor for the periodic independent two-sided queue strategy.</summary>
public sealed partial class PeriodicQueueCycleControls : UserControl
{
    private readonly Dictionary<string, NumericUpDown> _numbers = new(StringComparer.Ordinal);
    public TextBox InstanceId => instanceIdInput;
    public TextBox ProductId => productIdInput;
    public NumericUpDown Commission => commissionInput;
    public CheckBox ResearchValidated => researchValidatedCheck;

    public PeriodicQueueCycleControls()
    {
        InitializeComponent();
        _numbers.Add("DirectionWindowSeconds", directionWindowSecondsInput);
        _numbers.Add("MinMoveTicks", minMoveTicksInput);
        _numbers.Add("MinTrendEfficiency", minTrendEfficiencyInput);
        _numbers.Add("MinDepthObi", minDepthObiInput);
        _numbers.Add("ObiConfirmSeconds", obiConfirmSecondsInput);
        _numbers.Add("DirectionReverseSeconds", directionReverseSecondsInput);
        _numbers.Add("MinSideDepth", minSideDepthInput);
        _numbers.Add("MinNearDepth", minNearDepthInput);
        _numbers.Add("MinDepthRatio", minDepthRatioInput);
        _numbers.Add("MaxSpreadTicks", maxSpreadTicksInput);
        _numbers.Add("MaxVolatilityTicks", maxVolatilityTicksInput);
        _numbers.Add("QuoteFreshnessSeconds", quoteFreshnessSecondsInput);
        _numbers.Add("StopLossTicks", stopLossTicksInput);
        _numbers.Add("MaxOneLegSeconds", maxOneLegSecondsInput);
        _numbers.Add("CycleCooldownSeconds", cycleCooldownSecondsInput);
        _numbers.Add("StopLossCooldownSeconds", stopLossCooldownSecondsInput);
        _numbers.Add("ShockCooldownSeconds", shockCooldownSecondsInput);
        _numbers.Add("MarketDataRecoverySeconds", marketDataRecoverySecondsInput);
        _numbers.Add("TrendInitialAheadA", trendInitialAheadAInput);
        _numbers.Add("TrendRepriceThresholdB", trendRepriceThresholdBInput);
        _numbers.Add("TrendRepriceTargetC", trendRepriceTargetCInput);
        _numbers.Add("OppositeInitialAheadA", oppositeInitialAheadAInput);
        _numbers.Add("OppositeRepriceThresholdB", oppositeRepriceThresholdBInput);
        _numbers.Add("OppositeRepriceTargetC", oppositeRepriceTargetCInput);
    }

    public PeriodicQueueCycleControls(bool live) : this()
    {
        Height = 230;
        researchValidatedLabel.Visible = live;
        researchValidatedCheck.Visible = live;
        if (live) researchValidatedCheck.Text = "研究驗證（紀錄用）";
    }

    public StrategyDefinition CreateDefinition() => new()
    {
        StrategyType = PeriodicQueueCycleFactory.TypeName,
        InstanceId = InstanceId.Text.Trim(),
        Universe = new StrategyUniverse { Tradable = new ProductSelection(ProductId.Text.Trim()) },
        Parameters = Parameters()
    };

    public IReadOnlyDictionary<string, string> Parameters()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string key, NumericUpDown control) in _numbers)
            values[key] = control.Value.ToString(CultureInfo.InvariantCulture);
        values["CommissionPerContract"] = Commission.Value.ToString(CultureInfo.InvariantCulture);
        values["ResearchValidated"] = ResearchValidated.Checked.ToString(CultureInfo.InvariantCulture);
        values["DaySessionEnabled"] = daySessionCheck.Checked.ToString(CultureInfo.InvariantCulture);
        values["DaySessionStart"] = daySessionStartInput.Text.Trim();
        values["DaySessionEnd"] = daySessionEndInput.Text.Trim();
        values["NightSessionEnabled"] = nightSessionCheck.Checked.ToString(CultureInfo.InvariantCulture);
        values["NightSessionStart"] = nightSessionStartInput.Text.Trim();
        values["NightSessionEnd"] = nightSessionEndInput.Text.Trim();
        return values;
    }

}
