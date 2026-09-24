using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KGIOptionPricing.Strategies.MeanReversionScalper;

public sealed record MeanReversionSessionWindow(
    string SessionId,
    DateTimeOffset MarketOpen,
    DateTimeOffset TradingStart,
    DateTimeOffset Stop,
    DateTimeOffset MarketClose);

/// <summary>Local exchange-session anchors and configurable strategy activation times.</summary>
public sealed class MeanReversionSessionSchedule
{
    private static readonly TimeOnly DayMarketOpen = new(7, 45);
    private static readonly TimeOnly DayMarketClose = new(14, 45);
    private static readonly TimeOnly NightMarketOpen = new(16, 0);
    private static readonly TimeOnly NightMarketClose = new(5, 0);
    private static readonly TimeZoneInfo TaipeiZone = TimeZoneInfo.FindSystemTimeZoneById(
        OperatingSystem.IsWindows() ? "Taipei Standard Time" : "Asia/Taipei");

    private MeanReversionSessionSchedule(TimeOnly dayStart, TimeOnly dayStop,
        TimeOnly nightStart, TimeOnly nightStop)
    {
        DayStartTime = dayStart;
        DayStopTime = dayStop;
        NightStartTime = nightStart;
        NightStopTime = nightStop;
    }

    public TimeOnly DayStartTime { get; }
    public TimeOnly DayStopTime { get; }
    public TimeOnly NightStartTime { get; }
    public TimeOnly NightStopTime { get; }

    public static MeanReversionSessionSchedule Parse(IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        TimeOnly dayStart = ReadTime(values, "DayStartTime", "07:45");
        TimeOnly dayStop = ReadTime(values, "DayStopTime", "14:40");
        TimeOnly nightStart = ReadTime(values, "NightStartTime", "16:00");
        TimeOnly nightStop = ReadTime(values, "NightStopTime", "04:55");

        if (dayStart < DayMarketOpen || dayStop <= dayStart || dayStop > DayMarketClose)
            throw new ArgumentException("日盤啟停時間必須位於 07:45–14:45，且停止晚於啟動。", nameof(values));
        if (nightStart < NightMarketOpen)
            throw new ArgumentException("夜盤啟動時間不得早於 16:00。", nameof(values));

        DateOnly sample = new(2026, 1, 1);
        DateTimeOffset nightStartAt = At(sample, nightStart);
        DateTimeOffset nightStopAt = At(nightStop >= NightMarketOpen ? sample : sample.AddDays(1), nightStop);
        if (nightStopAt <= nightStartAt || nightStopAt > At(sample.AddDays(1), NightMarketClose))
            throw new ArgumentException("夜盤停止時間必須晚於啟動且不晚於隔日 05:00。", nameof(values));

        return new MeanReversionSessionSchedule(dayStart, dayStop, nightStart, nightStop);
    }

    public MeanReversionSessionWindow? Resolve(DateTimeOffset timestamp)
    {
        DateOnly date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timestamp, TaipeiZone).Date);
        MeanReversionSessionWindow day = Day(date);
        if (InMarket(timestamp, day)) return day;

        MeanReversionSessionWindow night = Night(date);
        if (InMarket(timestamp, night)) return night;
        night = Night(date.AddDays(-1));
        return InMarket(timestamp, night) ? night : null;
    }

    public IReadOnlyList<DateTimeOffset> StopsBetween(DateTimeOffset exclusiveStart, DateTimeOffset inclusiveEnd)
    {
        if (inclusiveEnd <= exclusiveStart) return Array.Empty<DateTimeOffset>();
        DateOnly first = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(exclusiveStart, TaipeiZone).Date).AddDays(-1);
        DateOnly last = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(inclusiveEnd, TaipeiZone).Date);
        var stops = new List<DateTimeOffset>();
        for (DateOnly date = first; date <= last; date = date.AddDays(1))
        {
            DateTimeOffset dayStop = Day(date).Stop;
            DateTimeOffset nightStop = Night(date).Stop;
            if (dayStop > exclusiveStart && dayStop <= inclusiveEnd) stops.Add(dayStop);
            if (nightStop > exclusiveStart && nightStop <= inclusiveEnd) stops.Add(nightStop);
        }
        return stops.OrderBy(x => x).ToArray();
    }

    private MeanReversionSessionWindow Day(DateOnly date) => new(
        $"D:{date:yyyyMMdd}", At(date, DayMarketOpen), At(date, DayStartTime),
        At(date, DayStopTime), At(date, DayMarketClose));

    private MeanReversionSessionWindow Night(DateOnly date) => new(
        $"N:{date:yyyyMMdd}", At(date, NightMarketOpen), At(date, NightStartTime),
        At(NightStopTime >= NightMarketOpen ? date : date.AddDays(1), NightStopTime),
        At(date.AddDays(1), NightMarketClose));

    private static bool InMarket(DateTimeOffset instant, MeanReversionSessionWindow window) =>
        instant >= window.MarketOpen && instant < window.MarketClose;

    private static DateTimeOffset At(DateOnly date, TimeOnly time)
    {
        DateTime local = date.ToDateTime(time, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TaipeiZone.GetUtcOffset(local));
    }

    private static TimeOnly ReadTime(IReadOnlyDictionary<string, string> values, string key, string fallback)
    {
        string text = values.TryGetValue(key, out string? raw) ? raw : fallback;
        if (!TimeOnly.TryParseExact(text, "HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out TimeOnly value))
            throw new ArgumentException($"{key} 必須為 HH:mm。", key);
        return value;
    }
}
