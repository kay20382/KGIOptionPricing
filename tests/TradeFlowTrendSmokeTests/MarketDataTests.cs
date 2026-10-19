using KGIOptionPricing.Strategies.TradeFlowTrend;
using KGIOptionPricing.StrategyFramework;
using KGIOptionPricing.StrategyFramework.MarketData;
using static TradeFlowTrendSmokeTests.TestSupport;
namespace TradeFlowTrendSmokeTests;
internal static class MarketDataTests
{
    public static void Run()
    {
        var now = At("2026-09-25T08:00:00+08:00");
        var tracker = new CausalBookTracker();
        var book = new MarketEvent { Symbol="M", Timestamp=now, Sequence=1, Kind=MarketEventKind.Book, BidPrices=[100], AskPrices=[110], ConnectionGeneration=1 };
        tracker.Observe(book);
        var t = tracker.Observe(book with { Kind=MarketEventKind.Trade, Sequence=2, Timestamp=now.AddSeconds(1), LastPrice=110, TradeQuantity=3 });
        var age=TimeSpan.FromSeconds(2);
        Check(TradeFlowClassifier.Classify(t,age)==TradeFlowSide.Up,"at ask");
        Check(TradeFlowClassifier.Classify(t with {LastPrice=100},age)==TradeFlowSide.Down,"at bid");
        Check(TradeFlowClassifier.Classify(t with {LastPrice=105},age)==TradeFlowSide.Zero,"inside");
        Check(TradeFlowClassifier.Classify(t with {Timestamp=now.AddSeconds(3)},age)==TradeFlowSide.Unknown,"stale");
        Check(tracker.Observe(t).ProvenanceWarning!=null,"duplicate");
        Check(tracker.Observe(t with {Sequence=3,ConnectionGeneration=2}).PriorBook==null,"generation reset");
        Check(TradeFlowClassifier.Classify(t with {PriorBook=t.PriorBook! with {Timestamp=now.AddSeconds(2)}},age)==TradeFlowSide.Unknown,"future book");
        Check(TradeFlowClassifier.Classify(t with {PriorBook=t.PriorBook! with {Ask=100}},age)==TradeFlowSide.Unknown,"locked book");
        var json=System.Text.Json.JsonSerializer.Serialize(t,new System.Text.Json.JsonSerializerOptions {PropertyNamingPolicy=System.Text.Json.JsonNamingPolicy.CamelCase});
        Check(KGIOptionPricing.StrategyFramework.Storage.MarketDataArchiveCodec.TryDeserialize(json,out var decoded) && decoded.PriorBook==t.PriorBook,"archive provenance");
    }
}
