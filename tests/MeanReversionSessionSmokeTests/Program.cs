using KGIOptionPricing.Strategies.MeanReversionScalper;

if (args.Length != 1 || args[0] != "schedule")
    throw new ArgumentException("Usage: MeanReversionSessionSmokeTests schedule");

var schedule = MeanReversionSessionSchedule.Parse(new Dictionary<string, string>
{
    ["DayStartTime"] = "08:00",
    ["DayStopTime"] = "14:40",
    ["NightStartTime"] = "16:10",
    ["NightStopTime"] = "04:55"
});

MeanReversionSessionWindow day = schedule.Resolve(Taipei(2026, 9, 24, 7, 50))!;
Check(day.TradingStart == Taipei(2026, 9, 24, 8, 0), "The day strategy start must be configurable independently from market open.");
Check(day.MarketOpen == Taipei(2026, 9, 24, 7, 45), "The day market-open anchor must not move with strategy start.");
Check(day.Stop == Taipei(2026, 9, 24, 14, 40), "The configured day stop must be exact.");

MeanReversionSessionWindow earlyNight = schedule.Resolve(Taipei(2026, 9, 24, 23, 59))!;
MeanReversionSessionWindow lateNight = schedule.Resolve(Taipei(2026, 9, 25, 4, 54))!;
Check(earlyNight.SessionId == lateNight.SessionId, "Overnight timestamps must resolve to one session.");
Check(lateNight.MarketOpen == Taipei(2026, 9, 24, 16, 0), "Overnight market-open must be on the previous date.");
Check(lateNight.Stop == Taipei(2026, 9, 25, 4, 55), "Overnight stop must occur on the following date.");
Check(schedule.Resolve(Taipei(2026, 9, 25, 5, 0)) is null, "The night session ends at 05:00, exclusive.");

DateTimeOffset[] stops = schedule.StopsBetween(Taipei(2026, 9, 24, 14, 39), Taipei(2026, 9, 25, 4, 55)).ToArray();
Check(stops.SequenceEqual(new[] { Taipei(2026, 9, 24, 14, 40), Taipei(2026, 9, 25, 4, 55) }),
    "Stop enumeration must contain both boundaries in chronological order.");
Check(schedule.StopsBetween(Taipei(2026, 9, 24, 14, 40), Taipei(2026, 9, 24, 14, 41)).Count == 0,
    "The lower stop-enumeration boundary is exclusive.");

MeanReversionSessionSchedule defaults = MeanReversionSessionSchedule.Parse(new Dictionary<string, string>());
Check(defaults.Resolve(Taipei(2026, 9, 24, 7, 45))!.TradingStart == Taipei(2026, 9, 24, 7, 45),
    "The default day start must be 07:45 Taipei time.");
Check(defaults.Resolve(Taipei(2026, 9, 24, 16, 0))!.Stop == Taipei(2026, 9, 25, 4, 55),
    "The default night stop must be 04:55 on the following date.");

Reject(new Dictionary<string, string> { ["DayStartTime"] = "07:30" });
Reject(new Dictionary<string, string> { ["DayStartTime"] = "15:00" });
Reject(new Dictionary<string, string> { ["DayStartTime"] = "8:00" });
Reject(new Dictionary<string, string> { ["NightStartTime"] = "04:00" });
Reject(new Dictionary<string, string> { ["NightStopTime"] = "15:00" });

Console.WriteLine("PASS schedule");

static DateTimeOffset Taipei(int year, int month, int day, int hour, int minute) =>
    new(year, month, day, hour, minute, 0, TimeSpan.FromHours(8));

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

static void Reject(IReadOnlyDictionary<string, string> values)
{
    try { _ = MeanReversionSessionSchedule.Parse(values); }
    catch (ArgumentException) { return; }
    throw new Exception("Invalid session input was accepted.");
}
