using System;
using System.Collections.Generic;
using System.Globalization;

namespace KGIOptionPricing.Strategies.PeriodicQueueCycle;

public sealed class PeriodicQueueCycleParameters
{
    private static readonly NumberStyles DecimalStyles = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

    public int DirectionWindowSeconds { get; private init; }
    public decimal MinMoveTicks { get; private init; }
    public decimal MinTrendEfficiency { get; private init; }
    public decimal MinDepthObi { get; private init; }
    public int ObiConfirmSeconds { get; private init; }
    public int DirectionReverseSeconds { get; private init; }
    public int MinSideDepth { get; private init; }
    public int MinNearDepth { get; private init; }
    public decimal MinDepthRatio { get; private init; }
    public decimal MaxSpreadTicks { get; private init; }
    public decimal MaxVolatilityTicks { get; private init; }
    public decimal QuoteFreshnessSeconds { get; private init; }
    public decimal StopLossTicks { get; private init; }
    public int MaxOneLegSeconds { get; private init; }
    public int CycleCooldownSeconds { get; private init; }
    public int StopLossCooldownSeconds { get; private init; }
    public int ShockCooldownSeconds { get; private init; }
    public decimal MarketDataRecoverySeconds { get; private init; }
    public decimal CommissionPerContract { get; private init; }
    public bool ResearchValidated { get; private init; }
    public int TrendInitialAheadA { get; private init; }
    public int TrendRepriceThresholdB { get; private init; }
    public int TrendRepriceTargetC { get; private init; }
    public int OppositeInitialAheadA { get; private init; }
    public int OppositeRepriceThresholdB { get; private init; }
    public int OppositeRepriceTargetC { get; private init; }

    public string ParameterSummary => string.Join(";", new[]
    {
        $"DirectionWindowSeconds={DirectionWindowSeconds}",
        $"MinMoveTicks={General(MinMoveTicks)}",
        $"MinTrendEfficiency={Ratio(MinTrendEfficiency)}",
        $"MinDepthObi={Ratio(MinDepthObi)}",
        $"ObiConfirmSeconds={ObiConfirmSeconds}",
        $"DirectionReverseSeconds={DirectionReverseSeconds}",
        $"MinSideDepth={MinSideDepth}",
        $"MinNearDepth={MinNearDepth}",
        $"MinDepthRatio={Ratio(MinDepthRatio)}",
        $"MaxSpreadTicks={General(MaxSpreadTicks)}",
        $"MaxVolatilityTicks={General(MaxVolatilityTicks)}",
        $"QuoteFreshnessSeconds={General(QuoteFreshnessSeconds)}",
        $"StopLossTicks={General(StopLossTicks)}",
        $"MaxOneLegSeconds={MaxOneLegSeconds}",
        $"CycleCooldownSeconds={CycleCooldownSeconds}",
        $"StopLossCooldownSeconds={StopLossCooldownSeconds}",
        $"ShockCooldownSeconds={ShockCooldownSeconds}",
        $"MarketDataRecoverySeconds={General(MarketDataRecoverySeconds)}",
        $"CommissionPerContract={General(CommissionPerContract)}",
        $"ResearchValidated={ResearchValidated.ToString().ToLowerInvariant()}",
        $"TrendInitialAheadA={TrendInitialAheadA}",
        $"TrendRepriceThresholdB={TrendRepriceThresholdB}",
        $"TrendRepriceTargetC={TrendRepriceTargetC}",
        $"OppositeInitialAheadA={OppositeInitialAheadA}",
        $"OppositeRepriceThresholdB={OppositeRepriceThresholdB}",
        $"OppositeRepriceTargetC={OppositeRepriceTargetC}"
    });

    public static PeriodicQueueCycleParameters Parse(IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var source = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, string> pair in values)
        {
            source[pair.Key] = pair.Value;
        }

        var result = new PeriodicQueueCycleParameters
        {
            DirectionWindowSeconds = ReadInt(source, "DirectionWindowSeconds", 15, 2, 300),
            MinMoveTicks = ReadDecimal(source, "MinMoveTicks", 2m, 0.5m, 50m),
            MinTrendEfficiency = ReadDecimal(source, "MinTrendEfficiency", 0.40m, 0m, 1m),
            MinDepthObi = ReadDecimal(source, "MinDepthObi", 0.10m, 0m, 1m),
            ObiConfirmSeconds = ReadInt(source, "ObiConfirmSeconds", 3, 1, 30),
            DirectionReverseSeconds = ReadInt(source, "DirectionReverseSeconds", 3, 1, 30),
            MinSideDepth = ReadInt(source, "MinSideDepth", 827, 1, 1_000_000),
            MinNearDepth = ReadInt(source, "MinNearDepth", 165, 1, 1_000_000),
            MinDepthRatio = ReadDecimal(source, "MinDepthRatio", 0.35m, 0m, 1m),
            MaxSpreadTicks = ReadDecimal(source, "MaxSpreadTicks", 2m, 1m, 20m),
            MaxVolatilityTicks = ReadDecimal(source, "MaxVolatilityTicks", 5m, 1m, 100m),
            QuoteFreshnessSeconds = ReadDecimal(source, "QuoteFreshnessSeconds", 2m, 0.1m, 30m),
            StopLossTicks = ReadDecimal(source, "StopLossTicks", 6m, 1m, 100m),
            MaxOneLegSeconds = ReadInt(source, "MaxOneLegSeconds", 30, 1, 300),
            CycleCooldownSeconds = ReadInt(source, "CycleCooldownSeconds", 45, 0, 3_600),
            StopLossCooldownSeconds = ReadInt(source, "StopLossCooldownSeconds", 60, 0, 3_600),
            ShockCooldownSeconds = ReadInt(source, "ShockCooldownSeconds", 45, 0, 3_600),
            MarketDataRecoverySeconds = ReadDecimal(source, "MarketDataRecoverySeconds", 2m, 0.1m, 30m),
            CommissionPerContract = ReadNonNegativeDecimal(source, "CommissionPerContract", 36m),
            ResearchValidated = ReadBoolean(source, "ResearchValidated", false),
            TrendInitialAheadA = ReadNonNegativeInt(source, "TrendInitialAheadA", 100),
            TrendRepriceThresholdB = ReadPositiveInt(source, "TrendRepriceThresholdB", 200),
            TrendRepriceTargetC = ReadNonNegativeInt(source, "TrendRepriceTargetC", 100),
            OppositeInitialAheadA = ReadNonNegativeInt(source, "OppositeInitialAheadA", 600),
            OppositeRepriceThresholdB = ReadPositiveInt(source, "OppositeRepriceThresholdB", 700),
            OppositeRepriceTargetC = ReadNonNegativeInt(source, "OppositeRepriceTargetC", 600)
        };

        if (result.ObiConfirmSeconds > result.DirectionWindowSeconds)
        {
            throw new ArgumentException("ObiConfirmSeconds 不得大於 DirectionWindowSeconds。", nameof(values));
        }

        ValidateQueueGroup("Trend", result.TrendRepriceTargetC, result.TrendRepriceThresholdB, values);
        ValidateQueueGroup("Opposite", result.OppositeRepriceTargetC, result.OppositeRepriceThresholdB, values);
        return result;
    }

    private static int ReadInt(IReadOnlyDictionary<string, string> values, string name, int defaultValue, int minimum, int maximum)
    {
        int value = ReadRawInt(values, name, defaultValue);
        if (value < minimum || value > maximum)
        {
            throw new ArgumentException($"{name} 必須介於 {minimum} 與 {maximum}。", nameof(values));
        }

        return value;
    }

    private static int ReadNonNegativeInt(IReadOnlyDictionary<string, string> values, string name, int defaultValue)
    {
        int value = ReadRawInt(values, name, defaultValue);
        if (value < 0) throw new ArgumentException($"{name} 不得小於零。", nameof(values));
        return value;
    }

    private static int ReadPositiveInt(IReadOnlyDictionary<string, string> values, string name, int defaultValue)
    {
        int value = ReadRawInt(values, name, defaultValue);
        if (value <= 0) throw new ArgumentException($"{name} 必須大於零。", nameof(values));
        return value;
    }

    private static int ReadRawInt(IReadOnlyDictionary<string, string> values, string name, int defaultValue)
    {
        if (!values.TryGetValue(name, out string? text)) return defaultValue;
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
        {
            throw new ArgumentException($"{name} 不是有效的整數。", nameof(values));
        }

        return value;
    }

    private static decimal ReadDecimal(IReadOnlyDictionary<string, string> values, string name, decimal defaultValue, decimal minimum, decimal maximum)
    {
        decimal value = ReadRawDecimal(values, name, defaultValue);
        if (value < minimum || value > maximum)
        {
            throw new ArgumentException($"{name} 必須介於 {General(minimum)} 與 {General(maximum)}。", nameof(values));
        }

        return value;
    }

    private static decimal ReadNonNegativeDecimal(IReadOnlyDictionary<string, string> values, string name, decimal defaultValue)
    {
        decimal value = ReadRawDecimal(values, name, defaultValue);
        if (value < 0m) throw new ArgumentException($"{name} 不得小於零。", nameof(values));
        return value;
    }

    private static decimal ReadRawDecimal(IReadOnlyDictionary<string, string> values, string name, decimal defaultValue)
    {
        if (!values.TryGetValue(name, out string? text)) return defaultValue;
        if (!decimal.TryParse(text, DecimalStyles, CultureInfo.InvariantCulture, out decimal value))
        {
            throw new ArgumentException($"{name} 不是有效的 InvariantCulture 數值。", nameof(values));
        }

        return value;
    }

    private static bool ReadBoolean(IReadOnlyDictionary<string, string> values, string name, bool defaultValue)
    {
        if (!values.TryGetValue(name, out string? text)) return defaultValue;
        if (!bool.TryParse(text, out bool value))
        {
            throw new ArgumentException($"{name} 不是有效的布林值。", nameof(values));
        }

        return value;
    }

    private static void ValidateQueueGroup(string name, int targetC, int thresholdB, IReadOnlyDictionary<string, string> values)
    {
        if (targetC >= thresholdB)
        {
            throw new ArgumentException($"{name}RepriceTargetC 必須小於 {name}RepriceThresholdB。", nameof(values));
        }
    }

    private static string General(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);
    private static string Ratio(decimal value) => value.ToString("0.00##########################", CultureInfo.InvariantCulture);
}
