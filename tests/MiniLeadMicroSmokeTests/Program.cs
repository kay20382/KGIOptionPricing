using KGIOptionPricing.OrderManagement.Core;
using KGIOptionPricing.Strategies.MiniLeadMicro;
using KGIOptionPricing.StrategyFramework;

var definition = Definition("ML-long");
var strategy = new MiniLeadMicroFactory().Create(definition);
Assert(strategy.GetSnapshot().ParameterSummary.Contains("Mode=DelayedResponse", StringComparison.Ordinal), "參數摘要未揭露交易模式");

var source = new FakeSource();
using var host = new StrategyHost(source);
var snapshots = new List<StrategySnapshot>();
var fills = new List<ExecutionEvent>();
host.SnapshotChanged += snapshots.Add;
host.ExecutionOccurred += fills.Add;
host.Add(strategy, Universe(), 36m);
Assert(host.Pause("ML-long") && host.GetSnapshots().Single().State == StrategyLifecycleState.Suspended, "策略應可暫停");
Assert(host.Resume("ML-long") && host.GetSnapshots().Single().State == StrategyLifecycleState.WarmingUp, "策略應可恢復暖機");

DateTimeOffset t = new(2026, 9, 19, 0, 0, 0, TimeSpan.Zero);
source.Emit(Book("MC", "225MC", 100m, 105m, 1, t));
source.Emit(Book("M", "225M", 100m, 105m, 2, t));
source.Emit(Book("L", "225", 95m, 105m, 3, t));
source.Emit(Trade("M", "225M", 105m, 4, t, 100m, 105m));
Assert(host.GetSnapshots().Single().State == StrategyLifecycleState.WarmingUp, "暖機完成前不得進場");

DateTimeOffset signal = t.AddSeconds(1);
source.Emit(Book("MC", "225MC", 100m, 105m, 5, signal));
source.Emit(Book("M", "225M", 120m, 125m, 6, signal));
source.Emit(Trade("M", "225M", 125m, 7, signal, 120m, 125m));
source.Emit(Book("L", "225", 105m, 115m, 8, signal.AddMilliseconds(10)));
Assert(host.GetSnapshots().Single().Reason.Contains("送出買進場", StringComparison.Ordinal), $"三商品確認後應建立多單，實際：{host.GetSnapshots().Single().Reason}");

source.Emit(Trade("MC", "225MC", 100m, 9, signal.AddMilliseconds(20), 100m, 105m));
Assert(fills.Count == 1 && fills[0].Role == PaperOrderRole.EntryLong && fills[0].Price == 105m, "進場應以訊號時 micro 賣一限價並採嚴格穿價成交");
StrategySnapshot opened = host.GetSnapshots().Single();
Assert(opened.Position == 1 && opened.StopPrice == 95m, "多單成交後應建立 2 tick 固定不利停損");

source.Emit(Trade("MC", "225MC", 90m, 10, signal.AddMilliseconds(30), 90m, 95m));
Assert(fills.Count == 2 && fills[1].Role == PaperOrderRole.StopLoss, "固定不利價格必須標示為 StopLoss");
StrategySnapshot stopped = host.GetSnapshots().Single();
Assert(stopped.Position == 0 && stopped.CompletedTrades == 1 && stopped.NetPnl == -172m, $"成本後停損損益應為 -172 日圓，實際 {stopped.NetPnl}");

var shortStrategy = new MiniLeadMicroFactory().Create(Definition("ML-short"));
var shortOrders = new List<PaperOrderRequest>();
shortStrategy.OrderRequested += shortOrders.Add;
shortStrategy.Start(new StrategyRuntimeContext(Universe(), new SystemStrategyClock(), new KGIOptionPricing.StrategyFramework.Performance.PaperPortfolioLedger()));
shortStrategy.OnMarketEvent(Book("MC", "225MC", 100m, 105m, 1, t));
shortStrategy.OnMarketEvent(Book("M", "225M", 120m, 125m, 2, t));
shortStrategy.OnMarketEvent(Book("L", "225", 115m, 125m, 3, t));
shortStrategy.OnMarketEvent(Trade("M", "225M", 120m, 4, t, 120m, 125m));
shortStrategy.OnMarketEvent(Book("MC", "225MC", 100m, 105m, 5, signal));
shortStrategy.OnMarketEvent(Book("M", "225M", 100m, 105m, 6, signal));
shortStrategy.OnMarketEvent(Trade("M", "225M", 100m, 7, signal, 100m, 105m));
shortStrategy.OnMarketEvent(Book("L", "225", 95m, 105m, 8, signal.AddMilliseconds(10)));
Assert(shortOrders.Count == 1 && shortOrders[0].Role == PaperOrderRole.EntryShort && shortOrders[0].Side == BuySell.Sell, $"對稱空方訊號應送出賣單；reason={shortStrategy.GetSnapshot().Reason}");

Console.WriteLine("MiniLeadMicro smoke tests passed.");

static StrategyDefinition Definition(string id) => new()
{
    StrategyType = MiniLeadMicroFactory.TypeName, InstanceId = id,
    Universe = new StrategyUniverse { Tradable = new ProductSelection("225MC", "MC"), References = new[] { new ProductSelection("225M", "M"), new ProductSelection("225", "L") } },
    Parameters = new Dictionary<string, string>
    {
        ["WarmupSeconds"] = "1", ["WindowMs"] = "1000", ["LargeWindowMs"] = "1000", ["LeaderMoveTicks"] = "3",
        ["MinPathEfficiency"] = "0.5", ["MinTradeIntensity"] = "0", ["LargeConfirmMoveTicks"] = "2", ["MinLargePathEfficiency"] = "0.5",
        ["LargeConfirmationTimeoutMs"] = "5000", ["MaxLargeMiniDispersionTicks"] = "5", ["MaxCatchUpRatio"] = "0.5",
        ["MinExpectedMoveTicks"] = "2", ["MinExpectedNetValueYen"] = "0", ["MinWinProbability"] = "0.5",
        ["MaxQuoteAgeMs"] = "5000", ["MaxLargeQuoteAgeMs"] = "5000", ["MaxCrossSymbolSkewMs"] = "5000", ["MaxMicroSpreadTicks"] = "1",
        ["ProfitTargetTicks"] = "5", ["StopLossTicks"] = "2", ["CooldownMs"] = "0", ["CommissionPerSideYen"] = "36"
    }
};

static ResolvedStrategyUniverse Universe() => new()
{
    Tradable = Product("225MC", "MC", 5m, 10m),
    References = new[] { Product("225M", "M", 5m, 100m), Product("225", "L", 10m, 1000m) }
};
static ResolvedProduct Product(string id, string symbol, decimal tick, decimal multiplier) => new() { ProductId = id, Symbol = symbol, TickSize = tick, ContractMultiplier = multiplier };
static MarketEvent Book(string symbol, string product, decimal bid, decimal ask, long sequence, DateTimeOffset time) => new() { Symbol = symbol, ProductId = product, Kind = MarketEventKind.Book, Sequence = sequence, Timestamp = time, BidPrices = new[] { bid }, AskPrices = new[] { ask }, BidQuantities = new[] { 10 }, AskQuantities = new[] { 10 } };
static MarketEvent Trade(string symbol, string product, decimal last, long sequence, DateTimeOffset time, decimal bid, decimal ask) => new() { Symbol = symbol, ProductId = product, Kind = MarketEventKind.Trade, Sequence = sequence, Timestamp = time, LastPrice = last, TradeQuantity = 1, BidPrices = new[] { bid }, AskPrices = new[] { ask }, BidQuantities = new[] { 10 }, AskQuantities = new[] { 10 } };
static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

sealed class FakeSource : IMarketEventSource
{
    public event Action<MarketEvent>? MarketEventReceived;
    public void Subscribe(IEnumerable<ResolvedProduct> products) { }
    public void Emit(MarketEvent item) => MarketEventReceived?.Invoke(item);
    public void Dispose() { }
}
