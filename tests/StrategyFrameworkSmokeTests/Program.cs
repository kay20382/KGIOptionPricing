using KGIOptionPricing;
using KGIOptionPricing.OrderManagement.Core;
using KGIOptionPricing.Strategies.MeanReversionScalper;
using KGIOptionPricing.Strategies.OrderBookTrend;
using KGIOptionPricing.Strategies.WaveletScalper;
using KGIOptionPricing.StrategyFramework;
using KGIOptionPricing.StrategyFramework.Execution;
using KGIOptionPricing.StrategyFramework.Performance;
using KGIOptionPricing.StrategyFramework.Storage;
using System.Reflection;
using System.Windows.Forms;

// Strict crossing: a touch must not fill, a pass through must fill.
Assert(StrategyProductSpecifications.ResolveTickSize("225MC") == 5m &&
       StrategyProductSpecifications.ResolveContractMultiplier("225MC") == 10m &&
       StrategyProductSpecifications.ResolveContractMultiplier("225MC", 10m) == 10m &&
       StrategyProductSpecifications.ResolveContractMultiplier("225MC", 12m) == 12m,
    "策略商品規格應優先使用已由 SymbolRepository 正規化的合約乘數");

var correlated = new PaperOrderRequest
{
    ClientOrderId = "cycle-1-buy-g1", StrategyInstanceId = "managed", Symbol = "MC",
    Side = BuySell.Buy, Type = PaperOrderType.BrokerLimit,
    Role = PaperOrderRole.EntryLong, Price = 100m, Quantity = 1
};
var lifecycle = new OrderLifecycleEvent
{
    StrategyInstanceId = "managed", ClientOrderId = correlated.ClientOrderId,
    OrderId = "SIM-managed-1", PreviousState = OrderLifecycleState.Submitted,
    State = OrderLifecycleState.Working, Price = 100m, Quantity = 1,
    FilledQuantity = 0, RemainingQuantity = 1
};
var correlatedOrder = new PaperOrder
{
    OrderId = "SIM-managed-1", ClientOrderId = correlated.ClientOrderId,
    StrategyInstanceId = "managed", Symbol = "MC", Side = BuySell.Buy,
    Type = PaperOrderType.BrokerLimit, Role = PaperOrderRole.EntryLong,
    Price = 100m, Quantity = 1, State = PaperOrderState.Working
};
var correlatedExecution = new ExecutionEvent
{
    StrategyInstanceId = "managed", ClientOrderId = correlated.ClientOrderId,
    OrderId = "SIM-managed-1", Symbol = "MC", Side = BuySell.Buy,
    Role = PaperOrderRole.EntryLong, Price = 100m, Quantity = 1
};
Assert(lifecycle.ClientOrderId == "cycle-1-buy-g1" &&
       lifecycle.PreviousState == OrderLifecycleState.Submitted &&
       lifecycle.State == OrderLifecycleState.Working && lifecycle.Price == 100m &&
       lifecycle.Quantity == 1 && lifecycle.RemainingQuantity == 1,
    "單張委託生命週期必須保留前後狀態、價格、數量與策略用戶端識別");

var managedBroker = new CrossPricePaperExecutionService();
var managedProbe = new ManagedProbeStrategy("managed-route");
using (var managedRunner = new StrategyInstanceRunner(
    managedProbe,
    new ResolvedStrategyUniverse { Tradable = Product("225MC", "MGR", 5m, 10m) },
    new PaperPortfolioLedger(), new SystemStrategyClock(), 0m, managedBroker))
{
    managedProbe.Request("buy-g1", BuySell.Buy, 100m);
    Assert(managedBroker.GetWorkingOrders("managed-route").Single().ClientOrderId == "buy-g1",
        "紙上執行器保存工作委託時不得遺失 ClientOrderId");
    managedProbe.Cancel("buy-g1");
    Assert(managedProbe.Lifecycle.Select(x => x.State).SequenceEqual(new[]
        {
            OrderLifecycleState.Submitted, OrderLifecycleState.Working,
            OrderLifecycleState.CancelPending, OrderLifecycleState.Cancelled
        }),
        "共用執行器必須依序回報單張送出、工作、刪單中與已刪除");
    Assert(managedProbe.Lifecycle.All(x => x.StrategyInstanceId == "managed-route" &&
        x.ClientOrderId == "buy-g1"), "生命週期不得遺失策略 instance 或 ClientOrderId");
    int lifecycleCount = managedProbe.Lifecycle.Count;
    Assert(!managedBroker.CancelByClientOrderId("managed-route", "buy-g1") &&
           !managedBroker.CancelByClientOrderId("managed-route", "missing") &&
           managedProbe.Lifecycle.Count == lifecycleCount,
        "不存在或已終止的 ClientOrderId 不得偽造撤單確認");
}

var managedFillBroker = new CrossPricePaperExecutionService();
var managedFillProbe = new ManagedProbeStrategy("managed-fill");
using (var managedFillRunner = new StrategyInstanceRunner(
    managedFillProbe,
    new ResolvedStrategyUniverse { Tradable = Product("225MC", "MGF", 5m, 10m) },
    new PaperPortfolioLedger(), new SystemStrategyClock(), 0m, managedFillBroker))
{
    managedFillProbe.Request("buy-fill-g1", BuySell.Buy, 100m);
    managedFillRunner.OnMarketEvent(Trade("MGF", "225MC", 100m, 1, DateTimeOffset.UtcNow));
    Assert(managedFillProbe.Executions.Count == 0, "BrokerLimit 只觸價時不應成交");
    managedFillRunner.OnMarketEvent(Trade("MGF", "225MC", 95m, 2, DateTimeOffset.UtcNow.AddSeconds(1)));
    Assert(managedFillProbe.Lifecycle.Select(x => x.State).SequenceEqual(new[]
        { OrderLifecycleState.Submitted, OrderLifecycleState.Working, OrderLifecycleState.Filled }),
        "BrokerLimit 穿價後必須回報完整成交生命週期");
    Assert(managedFillProbe.Executions.Count == 1 &&
           managedFillProbe.Executions[0].ClientOrderId == "buy-fill-g1",
        "BrokerLimit 成交必須保留 ClientOrderId");
}

var sharedManagedBroker = new CrossPricePaperExecutionService();
var firstManagedProbe = new ManagedProbeStrategy("managed-first", "MG1");
var secondManagedProbe = new ManagedProbeStrategy("managed-second", "MG2");
using (var firstManagedRunner = new StrategyInstanceRunner(
    firstManagedProbe,
    new ResolvedStrategyUniverse { Tradable = Product("225MC", "MG1", 5m, 10m) },
    new PaperPortfolioLedger(), new SystemStrategyClock(), 0m, sharedManagedBroker))
using (var secondManagedRunner = new StrategyInstanceRunner(
    secondManagedProbe,
    new ResolvedStrategyUniverse { Tradable = Product("225MC", "MG2", 5m, 10m) },
    new PaperPortfolioLedger(), new SystemStrategyClock(), 0m, sharedManagedBroker))
{
    firstManagedProbe.Request("same-client-id", BuySell.Buy, 100m);
    secondManagedProbe.Request("same-client-id", BuySell.Buy, 100m);
    firstManagedProbe.Cancel("same-client-id");
    Assert(firstManagedProbe.Lifecycle.Last().State == OrderLifecycleState.Cancelled &&
           secondManagedProbe.Lifecycle.Select(x => x.State).SequenceEqual(new[]
               { OrderLifecycleState.Submitted, OrderLifecycleState.Working }),
        "單張撤單必須以 StrategyInstanceId 與 ClientOrderId 精確隔離");
    firstManagedProbe.Request("first-fill", BuySell.Buy, 100m);
    sharedManagedBroker.OnMarketEvent(Trade("MG1", "225MC", 95m, 3, DateTimeOffset.UtcNow));
    Assert(firstManagedProbe.Executions.Count == 1 && secondManagedProbe.Executions.Count == 0,
        "共用執行器的成交事件只能路由至擁有該委託的策略 instance");
}
Assert(correlatedOrder.ClientOrderId == "cycle-1-buy-g1" &&
       correlatedExecution.ClientOrderId == "cycle-1-buy-g1",
    "工作委託與成交事件必須保留策略用戶端識別");

var broker = new CrossPricePaperExecutionService(2m);
var brokerFills = new List<ExecutionEvent>();
broker.Filled += brokerFills.Add;
broker.Submit(new PaperOrderRequest
{
    StrategyInstanceId = "broker",
    Symbol = "MC",
    Side = BuySell.Buy,
    Type = PaperOrderType.Limit,
    Role = PaperOrderRole.EntryLong,
    Price = 100m,
    Quantity = 1
}, DateTimeOffset.UtcNow);
broker.OnMarketEvent(Trade("MC", "225MC", 100m, 1, DateTimeOffset.UtcNow));
Assert(brokerFills.Count == 0, "觸價不應成交");
broker.OnMarketEvent(Trade("MC", "225MC", 95m, 2, DateTimeOffset.UtcNow));
Assert(brokerFills.Count == 1 && brokerFills[0].Price == 100m && brokerFills[0].Commission == 2m,
    "穿價應按委託價成交並計費");

// Shared ledger: strategies read, runner writes.
var ledger = new PaperPortfolioLedger();
var product = Product("225MC", "MC", 5m, 10m);
ledger.RegisterProduct("ledger", product);
ledger.ApplyExecution(new ExecutionEvent { StrategyInstanceId = "ledger", Symbol = "MC", Side = BuySell.Buy, Price = 100m, Quantity = 1, Commission = 2m });
ledger.ApplyExecution(new ExecutionEvent { StrategyInstanceId = "ledger", Symbol = "MC", Side = BuySell.Sell, Price = 120m, Quantity = 1, Commission = 2m });
PositionSnapshot ledgerPosition = ledger.GetPosition("ledger", "MC");
Assert(ledgerPosition.NetQuantity == 0 && ledgerPosition.RealizedPnl == 200m && ledgerPosition.Costs == 4m && ledgerPosition.NetPnl == 196m,
    "共用帳本損益計算錯誤");

// Causal Haar-MODWT scalper: a negative D1 deviation with bid-side OBI must request a paper market buy.
var waveletDefinition = new StrategyDefinition
{
    StrategyType = WaveletScalperFactory.TypeName,
    InstanceId = "WS-test",
    Universe = new StrategyUniverse { Tradable = new ProductSelection("225MC", "WS") },
    Parameters = new Dictionary<string, string>
    {
        ["WaveletWindowTicks"] = "8", ["EntrySigma"] = "0.5", ["ObiThreshold"] = "0.6",
        ["StopTicks"] = "3", ["EnergyMultiplier"] = "3", ["MaximumSpreadTicks"] = "1",
        ["FreshnessSeconds"] = "2", ["MaxHoldingSeconds"] = "10", ["CooldownSeconds"] = "0"
    }
};
var wavelet = new WaveletScalperFactory().Create(waveletDefinition);
var waveletOrders = new List<PaperOrderRequest>();
wavelet.OrderRequested += waveletOrders.Add;
wavelet.Start(new StrategyRuntimeContext(
    new ResolvedStrategyUniverse { Tradable = Product("225MC", "WS", 5m, 10m) },
    new SystemStrategyClock(), new PaperPortfolioLedger()));
DateTimeOffset waveletTime = new(2026, 9, 16, 0, 0, 0, TimeSpan.Zero);
foreach (decimal price in new[] { 100m, 105m, 100m, 105m, 100m, 105m, 100m, 95m, 90m })
{
    wavelet.OnMarketEvent(new MarketEvent
    {
        Kind = MarketEventKind.Trade, ProductId = "225MC", Symbol = "WS", LastPrice = price,
        Timestamp = waveletTime, BidPrices = new[] { price - 5m }, AskPrices = new[] { price },
        BidQuantities = new[] { 20 }, AskQuantities = new[] { 1 }
    });
    waveletTime = waveletTime.AddSeconds(1);
}
Assert(waveletOrders.Count == 1 && waveletOrders[0].Type == PaperOrderType.Market &&
       waveletOrders[0].Role == PaperOrderRole.EntryLong && waveletOrders[0].Side == BuySell.Buy,
    $"MODWT 負向偏離且 OBI 偏多時未建立模擬做多訊號；count={waveletOrders.Count}，reason={wavelet.GetSnapshot().Reason}");

// Full strategy path with a tradable and two required reference products.
var source = new FakeSource();
using var host = new StrategyHost(source);
var definition = new StrategyDefinition
{
    StrategyType = MeanReversionScalperFactory.TypeName,
    InstanceId = "MR-test",
    Universe = new StrategyUniverse
    {
        Tradable = new ProductSelection("225MC", "MC"),
        References = new[] { new ProductSelection("225", "L"), new ProductSelection("225M", "M") }
    },
    Parameters = new Dictionary<string, string>
    {
        ["HorizonSeconds"] = "10",
        ["F"] = "0.75",
        ["K"] = "0.87",
        ["MinimumRangeTicks"] = "1",
        ["MaximumSpreadTicks"] = "4",
        ["FreshnessSeconds"] = "30",
        ["CooldownSeconds"] = "0"
    }
};
var universe = new ResolvedStrategyUniverse
{
    Tradable = product,
    References = new[] { Product("225", "L", 10m, 1000m), Product("225M", "M", 5m, 100m) }
};
var strategy = new MeanReversionScalperFactory().Create(definition);
var snapshots = new List<StrategySnapshot>();
var executions = new List<ExecutionEvent>();
host.SnapshotChanged += snapshots.Add;
host.ExecutionOccurred += executions.Add;
host.Add(strategy, universe, 1m);
Assert(host.Pause("MR-test") && host.GetSnapshots().Single().State == StrategyLifecycleState.Suspended,
    "策略實例應可暫停");
Assert(host.Resume("MR-test") && host.GetSnapshots().Single().State == StrategyLifecycleState.WarmingUp,
    "暫停的策略實例應可繼續");

DateTimeOffset t0 = new(2026, 9, 16, 0, 0, 0, TimeSpan.Zero);
source.Emit(Book("L", "225", 49990m, 50010m, 1, t0));
source.Emit(Book("M", "225M", 109m, 111m, 2, t0));
source.Emit(Book("MC", "225MC", 109m, 111m, 3, t0));
source.Emit(Trade("MC", "225MC", 100m, 4, t0, 109m, 111m));
source.Emit(Book("L", "225", 49990m, 50010m, 5, t0.AddSeconds(10)));
source.Emit(Book("M", "225M", 109m, 111m, 6, t0.AddSeconds(10)));
source.Emit(Trade("MC", "225MC", 120m, 7, t0.AddSeconds(10), 109m, 111m));
Assert(snapshots.Last().State == StrategyLifecycleState.Running && snapshots.Last().BuyEntry == 100m && snapshots.Last().SellEntry == 120m,
    "暖機完成後未建立預期雙邊模擬掛單");
source.Emit(Trade("MC", "225MC", 100m, 8, t0.AddSeconds(11), 99m, 101m));
Assert(executions.Count == 0, "買單只觸價時不應成交");
source.Emit(Trade("MC", "225MC", 95m, 9, t0.AddSeconds(12), 94m, 96m));
Assert(executions.Count == 1 && executions[0].Role == PaperOrderRole.EntryLong, "穿越買單後應建立多單");
// A 5-point bid/ask spread has a half-tick midpoint (97.5), but PnL must use the 100 trade.
source.Emit(Trade("MC", "225MC", 100m, 10, t0.AddSeconds(12.5), 95m, 100m));
StrategySnapshot tradeMarked = host.GetSnapshots().Single();
Assert(tradeMarked.UnrealizedPnl == 0m && tradeMarked.NetPnl == -1m,
    "未實現損益必須以最新成交價評價，不可使用中價");
source.Emit(Trade("MC", "225MC", 125m, 11, t0.AddSeconds(13), 124m, 126m));
Assert(executions.Count == 2 && executions[1].Role == PaperOrderRole.TakeProfit, "穿越停利價後應平倉");
StrategySnapshot completed = host.GetSnapshots().Single();
Assert(completed.Position == 0 && completed.CompletedTrades == 1 && completed.RealizedPnl == 200m && completed.NetPnl == 198m,
    "完整策略循環的部位或損益錯誤");
Assert(completed.ParameterSummary.Contains("H=10s", StringComparison.Ordinal) && completed.CommissionPerContract == 1m,
    "策略管理快照必須揭示實例實際參數與每口費用");

// A pending bracket must retain its original range.  If the rolling range contracts
// before an entry fills, the stop must still remain on the adverse side of that entry.
var frozenBracketDefinition = new StrategyDefinition
{
    StrategyType = MeanReversionScalperFactory.TypeName,
    InstanceId = "MR-frozen-bracket",
    Universe = new StrategyUniverse { Tradable = new ProductSelection("225MC", "FZ") },
    Parameters = new Dictionary<string, string>
    {
        ["HorizonSeconds"] = "10", ["F"] = "0.75", ["K"] = "1.35",
        ["MinimumRangeTicks"] = "1", ["MaximumSpreadTicks"] = "1", ["FreshnessSeconds"] = "30"
    }
};
var frozenBracket = new MeanReversionScalperFactory().Create(frozenBracketDefinition);
var frozenOrders = new List<PaperOrderRequest>();
frozenBracket.OrderRequested += frozenOrders.Add;
frozenBracket.Start(new StrategyRuntimeContext(
    new ResolvedStrategyUniverse { Tradable = Product("225MC", "FZ", 5m, 10m) },
    new SystemStrategyClock(), new PaperPortfolioLedger()));
DateTimeOffset frozenTime = new(2026, 9, 16, 1, 0, 0, TimeSpan.Zero);
frozenBracket.OnMarketEvent(Trade("FZ", "225MC", 100m, 1, frozenTime, 95m, 100m));
frozenBracket.OnMarketEvent(Trade("FZ", "225MC", 120m, 2, frozenTime.AddSeconds(10), 115m, 120m));
decimal frozenShortEntry = frozenOrders.Single(x => x.Role == PaperOrderRole.EntryShort).Price;
frozenBracket.OnMarketEvent(Trade("FZ", "225MC", 100m, 3, frozenTime.AddSeconds(21), 95m, 100m));
frozenBracket.OnExecution(new ExecutionEvent
{
    StrategyInstanceId = "MR-frozen-bracket", Symbol = "FZ", Side = BuySell.Sell,
    Role = PaperOrderRole.EntryShort, Price = frozenShortEntry, Timestamp = frozenTime.AddSeconds(22)
});
PaperOrderRequest frozenStop = frozenOrders.Last(x => x.Role == PaperOrderRole.StopLoss);
Assert(frozenStop.Price > frozenShortEntry,
    "空單的停損價不得因滾動 R 縮小而落在進場價下方");

// MODWT fixed price-stop remains a StopLoss only for an adverse executable price.
var waveletStop = new WaveletScalperFactory().Create(waveletDefinition with { InstanceId = "WS-stop" });
var waveletStopOrders = new List<PaperOrderRequest>();
var waveletStopLedger = new PaperPortfolioLedger();
waveletStopLedger.RegisterProduct("WS-stop", Product("225MC", "WSS", 5m, 10m));
waveletStop.OrderRequested += waveletStopOrders.Add;
waveletStop.Start(new StrategyRuntimeContext(
    new ResolvedStrategyUniverse { Tradable = Product("225MC", "WSS", 5m, 10m) },
    new SystemStrategyClock(), waveletStopLedger));
waveletStopLedger.ApplyExecution(new ExecutionEvent
{
    StrategyInstanceId = "WS-stop", Symbol = "WSS", Side = BuySell.Buy,
    Role = PaperOrderRole.EntryLong, Price = 100m, Quantity = 1, Timestamp = waveletTime
});
waveletStop.OnExecution(new ExecutionEvent
{
    StrategyInstanceId = "WS-stop", Symbol = "WSS", Side = BuySell.Buy,
    Role = PaperOrderRole.EntryLong, Price = 100m, Timestamp = waveletTime
});
waveletStop.OnMarketEvent(Trade("WSS", "225MC", 80m, 1, waveletTime.AddSeconds(1), 75m, 80m));
Assert(waveletStopOrders.Single().Role == PaperOrderRole.StopLoss,
    "MODWT 價格跌破固定停損時必須標示為 StopLoss");

// OSE five-level trend strategy: all five features, confirmation, valid-tick
// marketable limit entry, and a fixed adverse protective stop.
var trendParameters = new Dictionary<string, string>
{
    ["MomentumEvents"] = "2", ["MomentumEntryTicks"] = "1", ["ObiEntry"] = "0.1",
    ["OfiWindow"] = "2", ["OfiZEntry"] = "0.1", ["OfiMean"] = "0", ["OfiStd"] = "1",
    ["TiTrades"] = "2", ["TiEntry"] = "0.1", ["MicropriceEntryTicks"] = "0.1",
    ["ConfirmEvents"] = "2", ["ConfirmPasses"] = "2", ["MaximumSpreadTicks"] = "1",
    ["MinimumWeightedDepth"] = "1", ["VolatilityWindow"] = "2", ["VolatilityLowTicks"] = "0",
    ["VolatilityHighTicks"] = "100", ["ExpectedMfeTicks"] = "5", ["MinimumExpectedMoveTicks"] = "4.5",
    ["MaxParticipationRate"] = "0.1", ["MaxContracts"] = "1", ["StopLossTicks"] = "4",
    ["PreCloseMinutes"] = "10"
};
var trendDefinition = new StrategyDefinition
{
    StrategyType = OrderBookTrendFactory.TypeName, InstanceId = "OBT-long",
    Universe = new StrategyUniverse { Tradable = new ProductSelection("225MC", "OBT") },
    Parameters = trendParameters
};
var trend = new OrderBookTrendFactory().Create(trendDefinition);
var trendOrders = new List<PaperOrderRequest>();
trend.OrderRequested += trendOrders.Add;
trend.Start(new StrategyRuntimeContext(
    new ResolvedStrategyUniverse { Tradable = Product("225MC", "OBT", 5m, 10m) },
    new SystemStrategyClock(), new PaperPortfolioLedger()));
DateTimeOffset trendTime = new(2026, 9, 16, 0, 0, 0, TimeSpan.Zero); // 09:00 JST
for (int i = 0; i < 4; i++)
    trend.OnMarketEvent(FiveLevelTrade("OBT", 100m + 5m * i, 105m + 5m * i, 50 + i * 5, 15 - i, i + 1, trendTime.AddMilliseconds(i)));
PaperOrderRequest trendEntry = trendOrders.Single(x => x.Role == PaperOrderRole.EntryLong);
Assert(trendEntry.Type == PaperOrderType.Limit && trendEntry.Price == 120m && trendEntry.Quantity == 1,
    $"五檔順勢多方訊號未送出預期可成交限價；orders={trendOrders.Count}, reason={trend.GetSnapshot().Reason}");
trend.OnExecution(new ExecutionEvent
{
    StrategyInstanceId = "OBT-long", Symbol = "OBT", Side = BuySell.Buy,
    Role = PaperOrderRole.EntryLong, Price = 120m, Quantity = 1, Timestamp = trendTime.AddSeconds(1)
});
PaperOrderRequest trendStop = trendOrders.Last(x => x.Role == PaperOrderRole.StopLoss);
Assert(trendStop.Type == PaperOrderType.Stop && trendStop.Price == 100m,
    "五檔順勢多單成交後應建立 4 tick 固定不利停損");
Assert(trend.GetSnapshot().ParameterSummary.Contains("MFE(cal)=5", StringComparison.Ordinal) &&
       trend.GetSnapshot().ReferenceSummary.Contains("接收端", StringComparison.Ordinal),
    "五檔順勢快照應揭露校準 MFE 與目前時間戳限制");

var shortTrend = new OrderBookTrendFactory().Create(trendDefinition with { InstanceId = "OBT-short" });
var shortOrders = new List<PaperOrderRequest>();
shortTrend.OrderRequested += shortOrders.Add;
shortTrend.Start(new StrategyRuntimeContext(
    new ResolvedStrategyUniverse { Tradable = Product("225MC", "OBT", 5m, 10m) },
    new SystemStrategyClock(), new PaperPortfolioLedger()));
for (int i = 0; i < 4; i++)
    shortTrend.OnMarketEvent(FiveLevelTrade("OBT", 115m - 5m * i, 120m - 5m * i, 15 - i, 50 + i * 5, i + 1, trendTime.AddMilliseconds(i), sellAggressor: true));
Assert(shortOrders.Single().Role == PaperOrderRole.EntryShort && shortOrders.Single().Price == 100m,
    $"五檔順勢空方對稱訊號未送出；orders={shortOrders.Count}, reason={shortTrend.GetSnapshot().Reason}");

var invalidTrend = new OrderBookTrendFactory().Create(trendDefinition with { InstanceId = "OBT-invalid" });
var invalidOrders = new List<PaperOrderRequest>();
invalidTrend.OrderRequested += invalidOrders.Add;
invalidTrend.Start(new StrategyRuntimeContext(
    new ResolvedStrategyUniverse { Tradable = Product("225MC", "OBT", 5m, 10m) },
    new SystemStrategyClock(), new PaperPortfolioLedger()));
invalidTrend.OnMarketEvent(Book("OBT", "225MC", 100m, 105m, 1, trendTime));
Assert(invalidOrders.Count == 0 && invalidTrend.GetSnapshot().Reason.Contains("完整五檔", StringComparison.Ordinal),
    "缺少完整五檔時不得產生順勢訊號");

// Archive is readable by the common replay source.
string archiveRoot = Path.Combine(Path.GetTempPath(), "KgiStrategyArchiveTests", Guid.NewGuid().ToString("N"));
string eventPath;
using (var writer = new MarketDataArchiveWriter(archiveRoot, "run"))
{
    writer.Record(Trade("MC", "225MC", 101m, 1, t0));
    writer.Record(Book("MC", "225MC", 100m, 102m, 2, t0));
    eventPath = writer.EventFilePath;
}
var replayed = new List<MarketEvent>();
using (var replay = new ArchiveReplayMarketDataSource(eventPath))
{
    replay.MarketEventReceived += replayed.Add;
    await replay.ReplayAsync();
}
Assert(replayed.Count == 2 && replayed[0].Kind == MarketEventKind.Trade && replayed[1].Kind == MarketEventKind.Book,
    "歷史行情封存或讀取失敗");
Directory.Delete(archiveRoot, true);

string legacyLine = "{\"Kind\":\"trade\",\"Time\":\"2026-09-16T08:00:00+08:00\",\"Sequence\":7,\"Product\":\"225MC\",\"Symbol\":\"MC-Z26\",\"Bid\":[49995],\"Ask\":[50000],\"BidQty\":[2],\"AskQty\":[3],\"Last\":49995,\"TradeQty\":1}";
Assert(MarketDataArchiveCodec.TryDeserialize(legacyLine, out MarketEvent legacy) &&
       legacy.ProductId == "225MC" && legacy.Kind == MarketEventKind.Trade && legacy.BestBid == 49995m,
    "既有 NikkeiResearch 格式無法讀取");

// UI regression: NumericUpDown defaults to Maximum=100, so Value=900 must be assigned only after its range.
Exception? uiConstructionError = null;
var uiThread = new Thread(() =>
{
    try
    {
        MethodInfo numberFactory = typeof(StrategyCenterForm).GetMethod(
            "Number", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(nameof(StrategyCenterForm), "Number");
        using var number = (NumericUpDown)(numberFactory.Invoke(null, new object[] { 900m, 10m, 86400m, 0 })
            ?? throw new InvalidOperationException("Number helper returned null"));
        Assert(number.Value == 900m && number.Minimum == 10m && number.Maximum == 86400m,
            "策略中心數值欄位的預設值或範圍錯誤");
    }
    catch (Exception ex)
    {
        uiConstructionError = ex is TargetInvocationException { InnerException: not null } ? ex.InnerException : ex;
    }
});
uiThread.SetApartmentState(ApartmentState.STA);
uiThread.Start();
uiThread.Join();
if (uiConstructionError != null)
    throw new InvalidOperationException("StrategyCenterForm 數值欄位建構失敗", uiConstructionError);

Console.WriteLine("Strategy framework smoke tests passed.");

if (args.Length > 0)
    await RunActualMarketReplay(args[0]);

static ResolvedProduct Product(string productId, string symbol, decimal tick, decimal multiplier) => new()
{ ProductId = productId, Symbol = symbol, TickSize = tick, ContractMultiplier = multiplier, TickType = TickTypeEnum.tt1 };

static MarketEvent Trade(string symbol, string product, decimal last, long sequence, DateTimeOffset time, decimal bid = 0m, decimal ask = 0m) => new()
{
    Symbol = symbol, ProductId = product, LastPrice = last, Sequence = sequence, Timestamp = time,
    Kind = MarketEventKind.Trade, TradeQuantity = 1,
    BidPrices = bid > 0 ? new[] { bid } : Array.Empty<decimal>(), BidQuantities = bid > 0 ? new[] { 1 } : Array.Empty<int>(),
    AskPrices = ask > 0 ? new[] { ask } : Array.Empty<decimal>(), AskQuantities = ask > 0 ? new[] { 1 } : Array.Empty<int>()
};

static MarketEvent Book(string symbol, string product, decimal bid, decimal ask, long sequence, DateTimeOffset time) => new()
{
    Symbol = symbol, ProductId = product, Sequence = sequence, Timestamp = time, Kind = MarketEventKind.Book,
    BidPrices = new[] { bid }, BidQuantities = new[] { 10 }, AskPrices = new[] { ask }, AskQuantities = new[] { 10 }
};

static MarketEvent FiveLevelTrade(string symbol, decimal bid, decimal ask, int bidQuantity, int askQuantity,
    long sequence, DateTimeOffset time, bool sellAggressor = false) => new()
{
    Symbol = symbol, ProductId = "225MC", Sequence = sequence, Timestamp = time, Kind = MarketEventKind.Trade,
    LastPrice = sellAggressor ? bid : ask, TradeQuantity = 1,
    BidPrices = Enumerable.Range(0, 5).Select(i => bid - 5m * i).ToArray(),
    AskPrices = Enumerable.Range(0, 5).Select(i => ask + 5m * i).ToArray(),
    BidQuantities = Enumerable.Repeat(bidQuantity, 5).ToArray(),
    AskQuantities = Enumerable.Repeat(askQuantity, 5).ToArray()
};

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static async Task RunActualMarketReplay(string path)
{
    var symbols = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (string line in File.ReadLines(path))
    {
        if (MarketDataArchiveCodec.TryDeserialize(line, out MarketEvent item)) symbols.TryAdd(item.ProductId, item.Symbol);
        if (new[] { "225MC", "225", "225M" }.All(symbols.ContainsKey)) break;
    }
    Assert(new[] { "225MC", "225", "225M" }.All(symbols.ContainsKey), "實際行情檔缺少必要商品");

    using var replay = new ArchiveReplayMarketDataSource(path);
    using var actualHost = new StrategyHost(replay);
    var actualDefinition = new StrategyDefinition
    {
        StrategyType = MeanReversionScalperFactory.TypeName,
        InstanceId = "actual-market-replay",
        Universe = new StrategyUniverse
        {
            Tradable = new ProductSelection("225MC", symbols["225MC"]),
            References = new[] { new ProductSelection("225", symbols["225"]), new ProductSelection("225M", symbols["225M"]) }
        },
        Parameters = new Dictionary<string, string>
        {
            ["HorizonSeconds"] = "900", ["F"] = "0.75", ["K"] = "0.87",
            ["MinimumRangeTicks"] = "4", ["MaximumSpreadTicks"] = "10", ["FreshnessSeconds"] = "5"
        }
    };
    var actualUniverse = new ResolvedStrategyUniverse
    {
        Tradable = Product("225MC", symbols["225MC"], 5m, 10m),
        References = new[] { Product("225", symbols["225"], 10m, 1000m), Product("225M", symbols["225M"], 5m, 100m) }
    };
    long eventCount = 0;
    long executionCount = 0;
    replay.MarketEventReceived += _ => eventCount++;
    actualHost.ExecutionOccurred += _ => executionCount++;
    actualHost.Add(new MeanReversionScalperFactory().Create(actualDefinition), actualUniverse, 36m);
    await replay.ReplayAsync();
    StrategySnapshot result = actualHost.GetSnapshots().Single();
    Console.WriteLine($"Actual market replay passed: events={eventCount}, executions={executionCount}, state={result.State}, reason={result.Reason}, range={result.Range}, trades={result.CompletedTrades}, netPnl={result.NetPnl}");
}

sealed class FakeSource : IMarketEventSource
{
    public event Action<MarketEvent>? MarketEventReceived;
    public void Subscribe(IEnumerable<ResolvedProduct> products) { }
    public void Emit(MarketEvent marketEvent) => MarketEventReceived?.Invoke(marketEvent);
    public void Dispose() { }
}

sealed class ManagedProbeStrategy : ISimulationStrategy, IManagedOrderStrategy
{
    private readonly string _symbol;

    public ManagedProbeStrategy(string instanceId, string? symbol = null)
    {
        InstanceId = instanceId;
        _symbol = symbol ?? (instanceId == "managed-fill" ? "MGF" : "MGR");
    }

    public string StrategyType => "ManagedProbe";
    public string InstanceId { get; }
    public StrategyLifecycleState State { get; private set; } = StrategyLifecycleState.Created;
    public List<OrderLifecycleEvent> Lifecycle { get; } = new();
    public List<ExecutionEvent> Executions { get; } = new();

    public event Action<PaperOrderRequest>? OrderRequested;
    public event Action? CancelAllRequested;
    public event Action<StrategySnapshot>? SnapshotChanged;
    public event Action<string>? CancelOrderRequested;

    public void Request(string clientOrderId, BuySell side, decimal price) => OrderRequested?.Invoke(new PaperOrderRequest
    {
        ClientOrderId = clientOrderId,
        StrategyInstanceId = InstanceId,
        Symbol = _symbol,
        Side = side,
        Type = PaperOrderType.BrokerLimit,
        Role = side == BuySell.Buy ? PaperOrderRole.EntryLong : PaperOrderRole.EntryShort,
        Price = price,
        Quantity = 1
    });

    public void Cancel(string clientOrderId) => CancelOrderRequested?.Invoke(clientOrderId);
    public void OnOrderLifecycle(OrderLifecycleEvent orderEvent) => Lifecycle.Add(orderEvent);
    public void Start(StrategyRuntimeContext context)
    {
        State = StrategyLifecycleState.Ready;
        SnapshotChanged?.Invoke(GetSnapshot());
    }
    public void OnMarketEvent(MarketEvent marketEvent) { }
    public void OnExecution(ExecutionEvent execution) => Executions.Add(execution);
    public void Pause(string reason) => State = StrategyLifecycleState.Suspended;
    public void Resume() => State = StrategyLifecycleState.Ready;
    public void Stop(string reason)
    {
        CancelAllRequested?.Invoke();
        State = StrategyLifecycleState.Stopped;
    }
    public StrategySnapshot GetSnapshot() => new()
    {
        StrategyType = StrategyType,
        InstanceId = InstanceId,
        State = State,
        Reason = "probe",
        UpdatedAt = DateTimeOffset.UtcNow
    };
}
