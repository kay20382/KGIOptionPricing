using KGIOptionPricing;
using KGIOptionPricing.OrderManagement.Core;
using KGIOptionPricing.Strategies.NikkeiMicrostructureAlpha;
using KGIOptionPricing.StrategyFramework;
using KGIOptionPricing.StrategyFramework.Performance;

var definition = Definition("NA-long");
var ledger = new PaperPortfolioLedger();
ResolvedStrategyUniverse universe = Universe();
foreach (ResolvedProduct product in universe.AllProducts) ledger.RegisterProduct(definition.InstanceId, product);
ISimulationStrategy strategy = new NikkeiMicrostructureAlphaFactory().Create(definition);
var orders = new List<PaperOrderRequest>();
strategy.OrderRequested += orders.Add;
strategy.Start(new StrategyRuntimeContext(universe, new SystemStrategyClock(), ledger));
Assert(strategy.State == StrategyLifecycleState.WarmingUp && strategy.GetSnapshot().Reason.Contains("暖機", StringComparison.Ordinal), "策略啟動後應顯示暖機原因");

DateTimeOffset t0 = new(2026, 9, 19, 0, 0, 0, TimeSpan.Zero);
FeedLongSetup(strategy, t0);
PaperOrderRequest entry = orders.Single();
Assert(entry.Role == PaperOrderRole.EntryLong && entry.Type == PaperOrderType.Limit && entry.Price == 100m && entry.Quantity == 1 &&
       !string.IsNullOrWhiteSpace(entry.ClientOrderId) && !string.IsNullOrWhiteSpace(entry.CycleId) && entry.StrategyRunGeneration == 1 &&
       strategy is IManagedOrderStrategy && strategy is IManagedOrderSubmissionObserver,
    "同向 OBI/AOFI/mini/large 訊號應在買一建立一口被動多單");
Assert(strategy.GetSnapshot().ParameterSummary.Contains("Alpha=0.70/0.65", StringComparison.Ordinal) &&
       strategy.GetSnapshot().ParameterSummary.Contains("Cost=¥72", StringComparison.Ordinal),
    "策略快照必須揭示有效 Alpha 與成本參數");

ledger.ApplyExecution(new ExecutionEvent { StrategyInstanceId = definition.InstanceId, Symbol = "MC", Side = BuySell.Buy, Role = PaperOrderRole.EntryLong, Price = 100m, Quantity = 1, Timestamp = t0.AddSeconds(1) });
strategy.OnExecution(new ExecutionEvent { StrategyInstanceId = definition.InstanceId, Symbol = "MC", Side = BuySell.Buy, Role = PaperOrderRole.EntryLong, Price = 100m, Quantity = 1, Timestamp = t0.AddSeconds(1) });
PaperOrderRequest takeProfit = orders.Single(x => x.Role == PaperOrderRole.TakeProfit);
PaperOrderRequest stop = orders.Single(x => x.Role == PaperOrderRole.StopLoss);
Assert(takeProfit.Price == 130m && takeProfit.Side == BuySell.Sell, "基準多單停利應為六跳且價格合法");
Assert(stop.Price == 90m && stop.Side == BuySell.Sell, "基準多單固定停損應為兩跳且位於進場價不利側");

ledger.ApplyExecution(new ExecutionEvent { StrategyInstanceId = definition.InstanceId, Symbol = "MC", Side = BuySell.Sell, Role = PaperOrderRole.TakeProfit, Price = 130m, Quantity = 1, Commission = 36m, Timestamp = t0.AddSeconds(2) });
strategy.OnExecution(new ExecutionEvent { StrategyInstanceId = definition.InstanceId, Symbol = "MC", Side = BuySell.Sell, Role = PaperOrderRole.TakeProfit, Price = 130m, Quantity = 1, Timestamp = t0.AddSeconds(2) });
Assert(strategy.GetSnapshot().CompletedTrades == 1 && strategy.GetSnapshot().Position == 0, "停利成交後應完成一筆並回到空倉");

var shortDefinition = Definition("NA-short");
var shortLedger = new PaperPortfolioLedger();
foreach (ResolvedProduct product in universe.AllProducts) shortLedger.RegisterProduct(shortDefinition.InstanceId, product);
ISimulationStrategy shortStrategy = new NikkeiMicrostructureAlphaFactory().Create(shortDefinition);
var shortOrders = new List<PaperOrderRequest>();
shortStrategy.OrderRequested += shortOrders.Add;
shortStrategy.Start(new StrategyRuntimeContext(universe, new SystemStrategyClock(), shortLedger));
FeedShortSetup(shortStrategy, t0);
PaperOrderRequest shortEntry = shortOrders.Single();
Assert(shortEntry.Role == PaperOrderRole.EntryShort && shortEntry.Type == PaperOrderType.Limit && shortEntry.Price == 105m &&
       !string.IsNullOrWhiteSpace(shortEntry.ClientOrderId) && !string.IsNullOrWhiteSpace(shortEntry.CycleId) &&
       shortEntry.ClientOrderId != entry.ClientOrderId,
    "同向空方訊號應在賣一建立一口被動空單");
shortLedger.ApplyExecution(new ExecutionEvent { StrategyInstanceId = shortDefinition.InstanceId, Symbol = "MC", Side = BuySell.Sell, Role = PaperOrderRole.EntryShort, Price = 105m, Quantity = 1, Timestamp = t0.AddSeconds(1) });
shortStrategy.OnExecution(new ExecutionEvent { StrategyInstanceId = shortDefinition.InstanceId, Symbol = "MC", Side = BuySell.Sell, Role = PaperOrderRole.EntryShort, Price = 105m, Quantity = 1, Timestamp = t0.AddSeconds(1) });
Assert(shortOrders.Single(x => x.Role == PaperOrderRole.StopLoss).Price == 115m, "空單固定停損必須位於進場價不利側");

var timeoutDefinition = Definition("NA-timeout");
var timeoutLedger = new PaperPortfolioLedger();
foreach (ResolvedProduct product in universe.AllProducts) timeoutLedger.RegisterProduct(timeoutDefinition.InstanceId, product);
ISimulationStrategy timeoutStrategy = new NikkeiMicrostructureAlphaFactory().Create(timeoutDefinition);
var timeoutOrders = new List<PaperOrderRequest>();
int timeoutCancels = 0;
timeoutStrategy.OrderRequested += timeoutOrders.Add;
timeoutStrategy.CancelAllRequested += () => timeoutCancels++;
timeoutStrategy.Start(new StrategyRuntimeContext(universe, new SystemStrategyClock(), timeoutLedger));
FeedLongSetup(timeoutStrategy, t0);
timeoutLedger.ApplyExecution(new ExecutionEvent { StrategyInstanceId = timeoutDefinition.InstanceId, Symbol = "MC", Side = BuySell.Buy, Role = PaperOrderRole.EntryLong, Price = 100m, Quantity = 1, Timestamp = t0.AddSeconds(1) });
timeoutStrategy.OnExecution(new ExecutionEvent { StrategyInstanceId = timeoutDefinition.InstanceId, Symbol = "MC", Side = BuySell.Buy, Role = PaperOrderRole.EntryLong, Price = 100m, Quantity = 1, Timestamp = t0.AddSeconds(1) });
int cancelsBeforePause = timeoutCancels;
timeoutStrategy.Pause("test pause");
Assert(timeoutCancels == cancelsBeforePause && timeoutStrategy.State == StrategyLifecycleState.Suspended,
    "持倉暫停不得撤銷既有停利與固定停損保護");
timeoutStrategy.Resume();
timeoutStrategy.OnMarketEvent(Book("M", "225M", 110m, 115m, 7, t0.AddSeconds(182), 10, 10));
timeoutStrategy.OnMarketEvent(Book("L", "225", 30020m, 30030m, 8, t0.AddSeconds(182), 10, 10));
timeoutStrategy.OnMarketEvent(Trade("MC", 105m, 9, t0.AddSeconds(182), 100m, 105m, 100, 1));
Assert(timeoutOrders.Last().Role == PaperOrderRole.Flatten,
    "180 秒持倉上限必須使用中性 Flatten，不得錯標為 StopLoss");

var clamped = new NikkeiMicrostructureAlphaFactory().Create(Definition("NA-clamp") with
{
    Parameters = new Dictionary<string, string> { ["EntryAlpha"] = "0.01", ["MaximumSpreadTicks"] = "99", ["EntryTimeoutMilliseconds"] = "1" }
});
string clampedSummary = clamped.GetSnapshot().ParameterSummary;
Assert(clampedSummary.Contains("Alpha=0.10/0.65", StringComparison.Ordinal) && clampedSummary.Contains("Spread=2t", StringComparison.Ordinal) && clampedSummary.Contains("Wait=100ms", StringComparison.Ordinal),
    "參數解析應使用安全邊界並在快照揭示實際值");

Console.WriteLine("Nikkei microstructure alpha smoke tests passed.");

static StrategyDefinition Definition(string id) => new()
{
    StrategyType = NikkeiMicrostructureAlphaFactory.TypeName,
    InstanceId = id,
    Universe = new StrategyUniverse
    {
        Tradable = new ProductSelection("225MC", "MC"),
        References = new[] { new ProductSelection("225M", "M"), new ProductSelection("225", "L") }
    },
    Parameters = new Dictionary<string, string>
    {
        ["WarmupSeconds"] = "1", ["FlowWindowSeconds"] = "30", ["VolatilityWindowSeconds"] = "10",
        ["EntryAlpha"] = "0.70", ["ContinuationAlpha"] = "0.65", ["MinimumDepth"] = "1",
        ["MaximumSpreadTicks"] = "1", ["MinimumMoveTicks"] = "5", ["MinimumNetEdge"] = "100",
        ["BaseStopTicks"] = "2", ["BaseTakeProfitTicks"] = "6", ["MinimumRewardRisk"] = "1",
        ["StopSlippageTicks"] = "1", ["EntryTimeoutMilliseconds"] = "1500", ["MaxHoldingSeconds"] = "180",
        ["FreshnessSeconds"] = "5", ["RoundTripCost"] = "72", ["CooldownSeconds"] = "0"
    }
};

static ResolvedStrategyUniverse Universe() => new()
{
    Tradable = Product("225MC", "MC", 5m, 10m),
    References = new[] { Product("225M", "M", 5m, 100m), Product("225", "L", 10m, 1000m) }
};

static void FeedLongSetup(ISimulationStrategy strategy, DateTimeOffset t0)
{
    strategy.OnMarketEvent(Book("M", "225M", 100m, 105m, 1, t0, 10, 10));
    strategy.OnMarketEvent(Book("L", "225", 30000m, 30010m, 2, t0, 10, 10));
    strategy.OnMarketEvent(Trade("MC", 105m, 3, t0, 100m, 105m, 100, 1));
    strategy.OnMarketEvent(Book("M", "225M", 105m, 110m, 4, t0.AddSeconds(1), 10, 10));
    strategy.OnMarketEvent(Book("L", "225", 30010m, 30020m, 5, t0.AddSeconds(1), 10, 10));
    strategy.OnMarketEvent(Trade("MC", 105m, 6, t0.AddSeconds(1), 100m, 105m, 100, 1));
}

static void FeedShortSetup(ISimulationStrategy strategy, DateTimeOffset t0)
{
    strategy.OnMarketEvent(Book("M", "225M", 105m, 110m, 1, t0, 10, 10));
    strategy.OnMarketEvent(Book("L", "225", 30010m, 30020m, 2, t0, 10, 10));
    strategy.OnMarketEvent(Trade("MC", 100m, 3, t0, 100m, 105m, 1, 100));
    strategy.OnMarketEvent(Book("M", "225M", 100m, 105m, 4, t0.AddSeconds(1), 10, 10));
    strategy.OnMarketEvent(Book("L", "225", 30000m, 30010m, 5, t0.AddSeconds(1), 10, 10));
    strategy.OnMarketEvent(Trade("MC", 100m, 6, t0.AddSeconds(1), 100m, 105m, 1, 100));
}

static ResolvedProduct Product(string productId, string symbol, decimal tick, decimal multiplier) => new()
{ ProductId = productId, Symbol = symbol, TickSize = tick, ContractMultiplier = multiplier, TickType = TickTypeEnum.tt1 };

static MarketEvent Book(string symbol, string product, decimal bid, decimal ask, long sequence, DateTimeOffset time, int bidQty, int askQty) => new()
{
    Symbol = symbol, ProductId = product, Sequence = sequence, Timestamp = time, Kind = MarketEventKind.Book,
    BidPrices = new[] { bid }, BidQuantities = new[] { bidQty }, AskPrices = new[] { ask }, AskQuantities = new[] { askQty }
};

static MarketEvent Trade(string symbol, decimal last, long sequence, DateTimeOffset time, decimal bid, decimal ask, int bidQty, int askQty) => new()
{
    Symbol = symbol, ProductId = "225MC", Sequence = sequence, Timestamp = time, Kind = MarketEventKind.Trade,
    LastPrice = last, TradeQuantity = 10,
    BidPrices = new[] { bid }, BidQuantities = new[] { bidQty }, AskPrices = new[] { ask }, AskQuantities = new[] { askQty }
};

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
