using KGIOptionPricing.StrategyFramework;
using KGIOptionPricing.StrategyFramework.Execution;
using KGIOptionPricing.OrderManagement.Core;

namespace MeanReversionBacktest;

internal static class BacktestSmoke
{
    public static int Run()
    {
        var classifier = new RegimeClassifier();
        DateTimeOffset start = new(2026, 9, 24, 8, 0, 0, TimeSpan.FromHours(8));
        if (classifier.Label(start) != "day|unknown") throw new Exception("Warmup label");
        for (int i = 0; i <= 15; i++)
            classifier.Observe(new MarketEvent { ProductId = "225M", Kind = MarketEventKind.Trade,
                Timestamp = start.AddMinutes(i), LastPrice = 64000 + 10 * i });
        if (classifier.Label(start.AddMinutes(15)) != "day|trend|highvol")
            throw new Exception("Trailing trend/highvol label");
        if (classifier.Label(start.AddHours(1)) != "day|unknown")
            throw new Exception("Stale reference price should be unknown");
        var clock = new FakeClock { Now = start };
        var broker = new CrossPricePaperExecutionService(36m, clock, fillMarketOnBook: true);
        var fills = new List<ExecutionEvent>();
        broker.Filled += fills.Add;
        broker.Submit(new PaperOrderRequest { StrategyInstanceId = "book-test", Symbol = "MC",
            ClientOrderId = "flat-1", Side = BuySell.Buy, Role = PaperOrderRole.Flatten,
            Type = PaperOrderType.Market, Price = 64000m, Quantity = 1 }, start);
        broker.OnMarketEvent(new MarketEvent { Timestamp = start.AddSeconds(1), ProductId = "225MC",
            Symbol = "MC", Kind = MarketEventKind.Book, BidPrices = [63995m], AskPrices = [64005m],
            BidQuantities = [2], AskQuantities = [2] });
        if (fills.Count != 1 || fills[0].Price != 64005m || fills[0].Commission != 36m)
            throw new Exception("Market flatten should cross next valid book at ask with normal fee.");
        broker.Submit(new PaperOrderRequest { StrategyInstanceId = "book-test", Symbol = "MC",
            ClientOrderId = "flat-2", Side = BuySell.Sell, Role = PaperOrderRole.Flatten,
            Type = PaperOrderType.Market, Price = 64000m, Quantity = 1 }, start);
        broker.OnMarketEvent(new MarketEvent { Timestamp = start.AddSeconds(2), ProductId = "225MC",
            Symbol = "MC", Kind = MarketEventKind.Book, BidPrices = [63995m], AskPrices = [64005m],
            BidQuantities = [0], AskQuantities = [2] });
        if (fills.Count != 1) throw new Exception("Empty best bid cannot fill a sell market order.");
        broker.OnMarketEvent(new MarketEvent { Timestamp = start.AddSeconds(3), ProductId = "225MC",
            Symbol = "MC", Kind = MarketEventKind.Book, BidPrices = [63995m], AskPrices = [64005m],
            BidQuantities = [2], AskQuantities = [2] });
        if (fills.Count != 2 || fills[1].Price != 63995m)
            throw new Exception("Sell market order should use the next valid bid.");
        Console.WriteLine("PASS regime classifier");
        return 0;
    }

    private sealed class FakeClock : IStrategyClock { public DateTimeOffset Now { get; set; } }
}
