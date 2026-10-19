using KGIOptionPricing.Strategies.TradeFlowTrend;
using static TradeFlowTrendSmokeTests.TestSupport;
namespace TradeFlowTrendSmokeTests;
internal static class ParameterTests
{
    public static void Run()
    {
        var p = TradeFlowParameters.Parse(new Dictionary<string,string>());
        Check(p.Quantity == 1 && p.StopTicks == 6 && p.ProfitTicks == 50, "risk defaults");
        Check(p.History30 == 60 && p.History60 == 30 && p.History180 == 10, "history");
        Check(p.DayStopTime == new TimeOnly(14,25) && p.NightStopTime == new TimeOnly(4,40), "session");
        foreach (var pair in new[] { ("FastSeconds","120"), ("Quantity","-1"), ("RodOffsetTicks","2"), ("SarMax","0.01"), ("NightStartTime","14:00"), ("Typo","1"), ("Quantity","abc"), ("Quantity","9999999999999999999999") })
            Reject(() => TradeFlowParameters.Parse(new Dictionary<string,string> { [pair.Item1]=pair.Item2 }));
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        try { System.Globalization.CultureInfo.CurrentCulture = new("fr-FR"); Check(TradeFlowParameters.Parse(p.ToDictionary()) == p, "culture independent roundtrip"); }
        finally { System.Globalization.CultureInfo.CurrentCulture = culture; }
    }
}
