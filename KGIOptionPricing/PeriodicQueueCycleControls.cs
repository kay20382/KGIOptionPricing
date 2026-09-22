using System.Globalization;
using KGIOptionPricing.Strategies.PeriodicQueueCycle;
using KGIOptionPricing.StrategyFramework;

namespace KGIOptionPricing;

/// <summary>Shared fixed-width parameter editor for the periodic independent two-sided queue strategy.</summary>
public sealed class PeriodicQueueCycleControls : FlowLayoutPanel
{
    private readonly Dictionary<string, NumericUpDown> _numbers = new(StringComparer.Ordinal);
    public TextBox InstanceId { get; } = InputText("PQC-225MC", 132);
    public TextBox ProductId { get; } = InputText("225MC", 72);
    public NumericUpDown Commission { get; } = Number(36m, 0m, 1_000_000m, 2);
    public CheckBox ResearchValidated { get; } = new()
    {
        Name = "ResearchValidated", Text = "已完成研究驗證", AutoSize = false, Width = 150, Height = 28,
        ForeColor = Color.DarkRed
    };

    public PeriodicQueueCycleControls(bool live)
    {
        Name = "PeriodicQueueCycleControls";
        Dock = DockStyle.Top;
        Height = live ? 210 : 182;
        AutoScroll = true;
        WrapContents = true;
        FlowDirection = FlowDirection.LeftToRight;
        Add("實例", InstanceId);
        Add("交易商品", ProductId);
        AddNumber("DirectionWindowSeconds", "方向窗秒", 15, 1, 300);
        AddNumber("MinMoveTicks", "最小移動 ticks", 2, 0, 100);
        AddNumber("MinTrendEfficiency", "趨勢效率", .40m, 0, 1, 2);
        AddNumber("MinDepthObi", "十檔 OBI", .10m, -1, 1, 2);
        AddNumber("ObiConfirmSeconds", "OBI 確認秒", 3, 1, 60);
        AddNumber("DirectionReverseSeconds", "反轉確認秒", 3, 1, 60);
        AddNumber("MinSideDepth", "單邊十檔量", 827, 1, 1_000_000);
        AddNumber("MinNearDepth", "近檔量", 165, 1, 1_000_000);
        AddNumber("MinDepthRatio", "深度比", .35m, .01m, 1m, 2);
        AddNumber("MaxSpreadTicks", "最大價差 ticks", 2, 1, 100);
        AddNumber("MaxVolatilityTicks", "最大波動 ticks", 5, 0, 100);
        AddNumber("QuoteFreshnessSeconds", "行情新鮮秒", 2, .1m, 60m, 1);
        AddNumber("MaxChaseTicks", "最大追價 ticks", 2, 0, 100);
        AddNumber("StopLossTicks", "固定停損 ticks", 6, 1, 100);
        AddNumber("MaxOneLegSeconds", "單邊成交逾時秒", 30, 1, 600);
        AddNumber("CycleCooldownSeconds", "一般冷卻秒", 45, 0, 3600);
        AddNumber("StopLossCooldownSeconds", "停損冷卻秒", 60, 0, 3600);
        AddNumber("ShockCooldownSeconds", "震盪冷卻秒", 45, 0, 3600);
        AddNumber("MarketDataRecoverySeconds", "資料恢復秒", 2, 0, 60);
        AddNumber("TrendInitialAheadA", "順勢 A", 22, 0, 1_000_000);
        AddNumber("TrendRepriceThresholdB", "順勢 B", 49, 0, 1_000_000);
        AddNumber("TrendRepriceTargetC", "順勢 C", 22, 0, 1_000_000);
        AddNumber("OppositeInitialAheadA", "反向 A", 240, 0, 1_000_000);
        AddNumber("OppositeRepriceThresholdB", "反向 B", 312, 0, 1_000_000);
        AddNumber("OppositeRepriceTargetC", "反向 C", 22, 0, 1_000_000);
        Add("每口每次費", Commission);
        if (live) Add("研究驗證", ResearchValidated);
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
        return values;
    }

    private void AddNumber(string key, string label, decimal value, decimal minimum, decimal maximum, int decimals = 0)
    {
        NumericUpDown control = Number(value, minimum, maximum, decimals);
        control.Name = key;
        _numbers.Add(key, control);
        Add(label, control);
    }

    private void Add(string label, Control control)
    {
        var caption = new Label { Text = label, AutoSize = false, Width = 94, Height = 28, TextAlign = ContentAlignment.MiddleRight };
        Controls.Add(caption);
        Controls.Add(control);
    }

    private static TextBox InputText(string value, int width) => new() { Text = value, Width = width, Height = 28, Margin = new Padding(3) };
    private static NumericUpDown Number(decimal value, decimal minimum, decimal maximum, int decimals = 0) => new()
    {
        Minimum = minimum, Maximum = maximum, DecimalPlaces = decimals, Increment = decimals == 0 ? 1 : .01m,
        Value = value, Width = 82, Height = 28, Margin = new Padding(3)
    };
}
