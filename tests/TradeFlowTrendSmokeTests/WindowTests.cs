using KGIOptionPricing.Strategies.TradeFlowTrend;
using static TradeFlowTrendSmokeTests.TestSupport;
namespace TradeFlowTrendSmokeTests;
internal static class WindowTests
{
    public static void Run()
    {
        var s=At("2026-09-25T08:00:00+08:00"); var w=new TradeFlowWindow(30,2,s);
        w.Add(new(s.AddSeconds(1),TradeFlowSide.Up,8)); w.Add(new(s.AddSeconds(2),TradeFlowSide.Down,2));
        w.Advance(s.AddSeconds(30)); Check(w.Snapshot(s.AddSeconds(30)).Real==60 && w.Snapshot(s.AddSeconds(30)).Average==60,"first Ri");
        w.Add(new(s.AddSeconds(31),TradeFlowSide.Down,100)); w.Advance(s.AddSeconds(60));
        var v=w.Snapshot(s.AddSeconds(60)); Check(v.Real==-100 && v.Average==-20 && v.Maximum==60 && v.Minimum==-100 && v.FullHistory,"arithmetic Ri mean");
        w.Advance(s.AddSeconds(90)); Check(w.Snapshot(s.AddSeconds(90)).ValidSamples==1,"null replaces old slot");
        var boundary=new TradeFlowWindow(30,2,s); boundary.Add(new(s.AddSeconds(30),TradeFlowSide.Up,1));
        Check(boundary.Snapshot(s.AddSeconds(30)).Average==null,"boundary in next slot");
        Check(boundary.Snapshot(s.AddSeconds(31)).Real==100,"rolling includes next slot");
        var partial=new TradeFlowWindow(30,2,s.AddSeconds(1)); partial.Add(new(s.AddSeconds(2),TradeFlowSide.Up,1));
        Check(partial.Advance(s.AddSeconds(30)).Single().Partial && partial.Snapshot(s.AddSeconds(30)).Average==null,"exclude partial");
        Reject(()=>partial.Add(new(s.AddSeconds(31),TradeFlowSide.Up,-1)));
        var set=new TradeFlowWindowSet(new(),s); set.Add("225",new(s.AddSeconds(1),TradeFlowSide.Up,1));
        Check(set.Get("225",30,s.AddSeconds(2)).Real==100 && set.Get("225M",30,s.AddSeconds(2)).Real==null,"symbol isolation");
        w.Advance(s.AddDays(30)); Check(w.Snapshot(s.AddDays(30)).ValidSamples==0,"bounded gap history");
    }
}
