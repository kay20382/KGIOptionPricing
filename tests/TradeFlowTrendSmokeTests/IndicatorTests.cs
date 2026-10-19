using KGIOptionPricing.Strategies.TradeFlowTrend;
using static TradeFlowTrendSmokeTests.TestSupport;
namespace TradeFlowTrendSmokeTests;
internal static class IndicatorTests
{
    public static void Run()
    {
        var t=At("2026-09-25T08:00:00+08:00"); var ma=new SampledPriceTrend(MovingAverageKind.Sma,2,3);
        ma.Sample(t,100);ma.Sample(t.AddSeconds(1),110);var v=ma.Sample(t.AddSeconds(2),120);
        Check(v.Fast==115 && v.Slow==110 && v.Ready,"sample weights");
        Check(!ma.Sample(t.AddSeconds(4),130).Ready,"missing second resets");
        var ema=new SampledPriceTrend(MovingAverageKind.Ema,2,3);ema.Sample(t,100);ema.Sample(t.AddSeconds(1),110);ema.Sample(t.AddSeconds(2),120);
        Check(ema.Sample(t.AddSeconds(3),130).Slow==120,"EMA initialized by SMA");
        var bars=new TradeBarBuilder(5);bars.Add(t.AddMilliseconds(1),100,1);bars.Add(t.AddMilliseconds(2),120,1);bars.Add(t.AddMilliseconds(3),90,1);bars.Add(t.AddSeconds(4),110,1);
        var bar=bars.Advance(t.AddSeconds(5)).Single();Check(bar.High==120 && bar.Low==90 && bar.Volume==4,"real OHLC");
        Check(bars.Advance(t.AddSeconds(10)).Count==0,"no fabricated bars");
        var sar=new ParabolicSarTracker(.02m,.02m,.20m);
        sar.Add(new(t,t.AddSeconds(5),100,105,95,100,1));
        var a=sar.Add(new(t.AddSeconds(5),t.AddSeconds(10),100,110,99,108,1));
        var b=sar.Add(new(t.AddSeconds(10),t.AddSeconds(15),108,115,106,114,1));
        Check(a!.Value==95 && a.Acceleration==.02m && b!.Value==95 && b.Acceleration==.04m && b.Extreme==115,"SAR fixture");
        var c=sar.Add(new(t.AddSeconds(15),t.AddSeconds(20),110,112,80,85,1));
        Check(c is {IsLong:false,Value:115,Acceleration:.02m},"SAR reverse");
        using var fixtures=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"sar-fixtures.json")));
        foreach(var fixture in fixtures.RootElement.EnumerateArray())
        {
            var tracker=new ParabolicSarTracker(.02m,.02m,.20m);int i=0;
            foreach(var input in fixture.GetProperty("bars").EnumerateArray())
            {
                decimal Num(System.Text.Json.JsonElement x)=>decimal.Parse(x.GetString()!,System.Globalization.CultureInfo.InvariantCulture);
                var actual=tracker.Add(new(t.AddSeconds(i*5),t.AddSeconds((i+1)*5),Num(input[2]),Num(input[0]),Num(input[1]),Num(input[2]),1));
                var expected=fixture.GetProperty("expected")[i++];
                if(expected.ValueKind==System.Text.Json.JsonValueKind.Null){Check(actual==null,"equal close initialization waits");continue;}
                Check(actual!=null && actual.Value==Num(expected.GetProperty("value")) && actual.IsLong==expected.GetProperty("isLong").GetBoolean() && actual.Extreme==Num(expected.GetProperty("extreme")) && actual.Acceleration==Num(expected.GetProperty("acceleration")),"independent Python SAR vector "+fixture.GetProperty("name")+" bar "+i);
            }
        }
    }
}
