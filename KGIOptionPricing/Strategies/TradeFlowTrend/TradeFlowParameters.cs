using System.Globalization;
using System.Reflection;

namespace KGIOptionPricing.Strategies.TradeFlowTrend;

public sealed record TradeFlowParameters
{
    public int Quantity { get; init; } = 1;
    public MovingAverageKind AverageKind { get; init; } = MovingAverageKind.Sma;
    public int FastSeconds { get; init; } = 30;
    public int SlowSeconds { get; init; } = 120;
    public int History30 { get; init; } = 60;
    public int History60 { get; init; } = 30;
    public int History180 { get; init; } = 10;
    public int EntryWindowSeconds { get; init; } = 30;
    public int ExitWindowSeconds { get; init; } = 30;
    public decimal EntryDirectionThreshold { get; init; }
    public decimal EntryAverageMargin { get; init; }
    public decimal ExitDirectionThreshold { get; init; }
    public decimal StopTicks { get; init; } = 6;
    public decimal ProfitTicks { get; init; } = 50;
    public decimal SarActivationTicks { get; init; } = 8;
    public int SarBarSeconds { get; init; } = 5;
    public decimal SarStart { get; init; } = .02m;
    public decimal SarStep { get; init; } = .02m;
    public decimal SarMax { get; init; } = .20m;
    public decimal SpreadSwitchTicks { get; init; } = 2;
    public decimal IocOffsetTicks { get; init; } = 10;
    public decimal RodOffsetTicks { get; init; } = 1;
    public bool DayEnabled { get; init; } = true;
    public bool NightEnabled { get; init; } = true;
    public TimeOnly DayStartTime { get; init; } = new(7,50);
    public TimeOnly DayStopTime { get; init; } = new(14,25);
    public TimeOnly NightStartTime { get; init; } = new(16,10);
    public TimeOnly NightStopTime { get; init; } = new(4,40);
    public int MaxBookAgeMs { get; init; } = 2000;
    public int MaxCrossBookSkewMs { get; init; } = 2000;
    public int MaxTradeAgeSeconds { get; init; } = 5;
    public int SarMaxAgeBars { get; init; } = 2;
    public bool RequireFullHistory { get; init; } = true;

    private static readonly PropertyInfo[] Fields = typeof(TradeFlowParameters).GetProperties();
    public IReadOnlyDictionary<string,string> ToDictionary() => Fields.ToDictionary(x => x.Name,
        x => x.GetValue(this) is TimeOnly time ? time.ToString("HH:mm", CultureInfo.InvariantCulture) :
            Convert.ToString(x.GetValue(this), CultureInfo.InvariantCulture)!, StringComparer.OrdinalIgnoreCase);

    public static TradeFlowParameters Parse(IReadOnlyDictionary<string,string> values)
    {
        var p = new TradeFlowParameters();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, raw) in values)
        {
            var field = Fields.SingleOrDefault(x => x.Name.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (field is null || !seen.Add(key)) throw new ArgumentException($"未知或重複參數 {key}");
            try
            {
                object value = field.PropertyType == typeof(TimeOnly) ? TimeOnly.ParseExact(raw, "HH:mm", CultureInfo.InvariantCulture) :
                    field.PropertyType.IsEnum ? Enum.Parse(field.PropertyType, raw, true) :
                    Convert.ChangeType(raw, field.PropertyType, CultureInfo.InvariantCulture);
                field.SetValue(p, value);
            }
            catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
            { throw new ArgumentException($"無效參數 {key}: {raw}", ex); }
        }
        p.Validate(); return p;
    }

    public void Validate()
    {
        static void Range(decimal value, decimal min, decimal max, string key)
        { if (value < min || value > max) throw new ArgumentOutOfRangeException(key); }
        Range(Quantity,1,100,nameof(Quantity));
        Range(FastSeconds,1,3600,nameof(FastSeconds)); Range(SlowSeconds,1,3600,nameof(SlowSeconds));
        if (FastSeconds >= SlowSeconds || !Enum.IsDefined(AverageKind)) throw new ArgumentException("均線種類或週期無效");
        foreach (var n in new[] { History30,History60,History180 }) Range(n,1,3600,"History");
        if (EntryWindowSeconds is not (30 or 60 or 180) || ExitWindowSeconds is not (30 or 60 or 180)) throw new ArgumentException("統計窗格無效");
        Range(EntryDirectionThreshold,0,100,nameof(EntryDirectionThreshold)); Range(EntryAverageMargin,0,200,nameof(EntryAverageMargin));
        Range(ExitDirectionThreshold,0,100,nameof(ExitDirectionThreshold));
        foreach (var n in new[] {StopTicks,ProfitTicks,SarActivationTicks}) Range(n,1,10000,"Ticks");
        Range(SarBarSeconds,1,300,nameof(SarBarSeconds));
        if (SarStart <= 0 || SarStep <= 0 || SarStart > SarMax || SarStep > SarMax || SarMax > 1) throw new ArgumentException("SAR AF 無效");
        if (RodOffsetTicks <= 0 || RodOffsetTicks >= SpreadSwitchTicks || IocOffsetTicks < SpreadSwitchTicks || IocOffsetTicks > 10000)
            throw new ArgumentException("委託偏移與價差界線無效");
        if (DayStartTime < new TimeOnly(7,45) || DayStopTime > new TimeOnly(14,45) || DayStartTime >= DayStopTime ||
            NightStartTime < new TimeOnly(16,0) || NightStopTime > new TimeOnly(5,0) || NightStartTime <= NightStopTime)
            throw new ArgumentException("日夜盤時間無效");
        Range(MaxBookAgeMs,100,60000,nameof(MaxBookAgeMs)); Range(MaxCrossBookSkewMs,100,60000,nameof(MaxCrossBookSkewMs));
        Range(MaxTradeAgeSeconds,1,60,nameof(MaxTradeAgeSeconds)); Range(SarMaxAgeBars,1,20,nameof(SarMaxAgeBars));
    }
}
