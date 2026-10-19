using KGIOptionPricing.Strategies.TradeFlowTrend;
using static TradeFlowTrendSmokeTests.TestSupport;
namespace TradeFlowTrendSmokeTests;
internal static class SessionTests
{
    public static void Run()
    {
        var s=new TradeFlowSessionSchedule(new());var a=s.Resolve(At("2026-09-25T23:59:59+08:00"))!;var b=s.Resolve(At("2026-09-26T00:00:01+08:00"))!;
        Check(a.SessionId==b.SessionId && b.Stop==At("2026-09-26T04:40:00+08:00"),"midnight");
        Check(s.StopsBetween(At("2026-09-25T14:24:59+08:00"),At("2026-09-25T14:30:00+08:00")).Single()==At("2026-09-25T14:25:00+08:00"),"heartbeat stop");
        Check(s.Resolve(At("2026-09-26T08:00:00+08:00")) is { SessionId:"2026-09-26/D" },"calendar day is not checked");
    }
}
