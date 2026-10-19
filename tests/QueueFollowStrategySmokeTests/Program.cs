using KGIOptionPricing;
using KGIOptionPricing.OrderManagement.Core;
using KGIOptionPricing.Strategies;

var ticks = new WholeNumberTicks();
var planner = new QueueFollowPricePlanner(ticks);

QueueFollowRequest buy = Request("planner-buy", BuySell.Buy, 1, 110m);
QueueFollowDecision hold = planner.Decide(buy, Book(100m, 1, 102m, 3), 100m, 1, 1);
Assert(hold.Kind == QueueFollowDecisionKind.Hold, "前方量未超過剩餘口數時不應追價");

QueueFollowDecision improve = planner.Decide(buy, Book(100m, 8, 103m, 3), 100m, 1, 8);
Assert(improve.Kind == QueueFollowDecisionKind.Modify && improve.TargetPrice == 101m,
    "買單同價前方量過大時應改善一個跳動點");

QueueFollowRequest followRequest = Request("follow-best", BuySell.Buy, 1, 110m, a: 10, b: 6, c: 5);
var layeredBook = new OrderBookSnapshot
{
    Symbol = "JPX.225MC-202609",
    BidPrices = new[] { 101m, 99m },
    BidQuantities = new[] { 5, 10 },
    AskPrices = new[] { 103m },
    AskQuantities = new[] { 3 }
};
QueueFollowDecision followBest = planner.Decide(followRequest, layeredBook, 99m, 1, 10);
Assert(followBest.Kind == QueueFollowDecisionKind.Modify && followBest.TargetPrice == 101m,
    "前方量超過 B 時應改掛到累計量不超過 C 的價位");

var initialBook = new OrderBookSnapshot
{
    Symbol = "JPX.225MC-202609",
    BidPrices = new[] { 100m, 99m, 98m },
    BidQuantities = new[] { 4, 5, 8 },
    AskPrices = new[] { 102m },
    AskQuantities = new[] { 3 }
};
decimal initialTarget = planner.SelectPriceForAheadTarget(Request("initial-a", BuySell.Buy, 1, 110m, a: 10), initialBook, 10);
Assert(initialTarget == 98m, "首次掛單應選擇第 A 口所在的價位");

var exactSellThird = new OrderBookSnapshot
{
    Symbol = "TMFI6",
    BidPrices = new[] { 46442m },
    BidQuantities = new[] { 1 },
    AskPrices = new[] { 46443m, 46444m, 46445m, 46446m },
    AskQuantities = new[] { 3, 7, 10, 6 }
};
decimal sellThirdTarget = planner.SelectPriceForAheadTarget(
    Request("sell-third", BuySell.Sell, 1, 41458m, a: 20, b: 30, c: 20), exactSellThird, 20);
Assert(sellThirdTarget == 46445m, "賣一 3、賣二 7、賣三 10 且 A=20 時應掛在賣三");

QueueFollowRequest cappedBuy = Request("capped", BuySell.Buy, 1, 100.4m);
QueueFollowDecision capped = planner.Decide(cappedBuy, Book(100m, 9, 102m, 3), 100m, 1, 9);
Assert(capped.Kind == QueueFollowDecisionKind.Hold && capped.TargetPrice == 0m,
    "買單價格界線不得因跳動點取整而被突破");

QueueFollowRequest sell = Request("planner-sell", BuySell.Sell, 1, 90m);
QueueFollowDecision sellImprove = planner.Decide(sell, Book(98m, 3, 100m, 8), 100m, 1, 8);
Assert(sellImprove.Kind == QueueFollowDecisionKind.Modify && sellImprove.TargetPrice == 99m,
    "賣單同價前方量過大時應向下改善一個跳動點");

// 自動追價以改價優先；只有收到明確改價失敗，才刪單並於取消確認後重掛。
var market = new FakeMarket();
var orders = new FakeOrders();
var history = new MemoryHistory();
using var engine = new QueueFollowStrategyEngine(market, orders, ticks, history);
market.Current = Book(100m, 10, 103m, 2);
engine.Start(Request("engine", BuySell.Buy, 1, 110m));
Assert(orders.Placed.Count == 1 && orders.Modified.Count == 0, "啟動時應只送出一筆新單");

orders.EmitAccepted("QFS_engine", 100m, 1);
Assert(orders.Modified.SequenceEqual(new[] { 101m }) && orders.Cancelled.Count == 0,
    "前方量超過 B 時應優先送出改價");

market.Publish(Book(102m, 20, 104m, 2));
Assert(orders.Modified.Count == 1 && orders.Cancelled.Count == 0,
    "改價尚未確認時不得因行情變動重複送出操作");

orders.EmitAccepted("QFS_engine", 101m, 1);
Assert(orders.Modified.Count == 2 && orders.Modified[1] == 103m,
    "改價確認後才可依最新行情送出下一次改價");

orders.EmitModifyFailed("QFS_engine", 101m, 1, "改價失敗");
Assert(orders.Cancelled.SequenceEqual(new[] { "QFS_engine" }) && orders.Placed.Count == 1,
    "明確收到改價失敗後，才可退回刪單重掛流程");
orders.EmitCancelled("QFS_engine", 101m);
Assert(orders.Placed.Count == 2 &&
       orders.Placed[1].OrderId == "QFS_engine_R1" &&
       (decimal)orders.Placed[1].Price == 103m,
    "改價失敗後的刪單確認完成，才可依最新行情及 C 重新下單");
Assert(history.Entries.Any(x => x.EventType == QueueFollowHistoryEventType.MarketChanged) &&
       history.Entries.Any(x => x.EventType == QueueFollowHistoryEventType.Decision) &&
       history.Entries.Any(x => x.EventType == QueueFollowHistoryEventType.OrderCommand && x.Action == "Modify") &&
       history.Entries.Any(x => x.EventType == QueueFollowHistoryEventType.Error && x.Action == "ModifyFailedFallback") &&
       history.Entries.Any(x => x.EventType == QueueFollowHistoryEventType.OrderCommand && x.Action == "CancelForReprice") &&
       history.Entries.Any(x => x.EventType == QueueFollowHistoryEventType.OrderCommand && x.Action == "PlaceAfterCancel") &&
       history.Entries.Any(x => x.EventType == QueueFollowHistoryEventType.OrderReport),
    "策略歷程未完整包含行情、改價、失敗降級、刪單、新單與回報");
QueueFollowHistoryEntry started = history.Entries.First(x => x.EventType == QueueFollowHistoryEventType.StrategyStarted);
Assert(started.InitialTargetAheadQuantity == 10 && started.RepriceAheadThreshold == 1 && started.RepriceTargetAheadQuantity == 0,
    "策略歷程未保留 A/B/C 設定");

// 使用者在改價回報尚未抵達時停止策略，必須直接以原始委託識別送刪單，不能永遠卡在 PendingModify。
var pendingMarket = new FakeMarket { Current = Book(100m, 10, 103m, 2) };
var pendingOrders = new FakeOrders();
using (var pendingEngine = new QueueFollowStrategyEngine(pendingMarket, pendingOrders, ticks, new MemoryHistory()))
{
    pendingEngine.Start(Request("pending-stop", BuySell.Buy, 1, 110m, b: 100));
    pendingOrders.EmitAccepted("QFS_pending-stop", 100m, 1);
    pendingOrders.EmitModifyPending("QFS_pending-stop", 100m, 1);
    pendingEngine.Stop("pending-stop");
    Assert(pendingOrders.Cancelled.SequenceEqual(new[] { "QFS_pending-stop" }) &&
           pendingEngine.GetSnapshots().Single().State == QueueFollowState.PendingCancel,
        "PendingModify 中停止策略時應立即送出刪單並轉為 PendingCancel");
}

// 賣單掛在 1005，只剩自己的 1 口，1004 空檔而賣一在 1003：必須先刪單，
// 等刪單確認後才以新的委託識別碼重下賣一 1003。
var gapMarket = new FakeMarket();
var gapOrders = new FakeOrders();
var gapHistory = new MemoryHistory();
using (var gapEngine = new QueueFollowStrategyEngine(gapMarket, gapOrders, ticks, gapHistory))
{
    gapMarket.Current = new OrderBookSnapshot
    {
        Symbol = "JPX.225MC-202609",
        BidPrices = new[] { 1002m },
        BidQuantities = new[] { 3 },
        AskPrices = new[] { 1003m, 1005m },
        AskQuantities = new[] { 3, 5 }
    };
    gapEngine.Start(Request("gap", BuySell.Sell, 1, 900m, a: 8, b: 30, c: 20));
    Assert(gapOrders.Placed.Count == 1 && (decimal)gapOrders.Placed[0].Price == 1005m,
        "測試前提錯誤：初始委託應掛在 1005");

    gapOrders.EmitAccepted("QFS_gap", 1005m, 1);
    gapMarket.Publish(new OrderBookSnapshot
    {
        Symbol = "JPX.225MC-202609",
        BidPrices = new[] { 1002m },
        BidQuantities = new[] { 3 },
        AskPrices = new[] { 1003m, 1005m },
        AskQuantities = new[] { 3, 1 }
    });
    Assert(gapOrders.Cancelled.SequenceEqual(new[] { "QFS_gap" }) && gapOrders.Placed.Count == 1,
        "只剩自己且下一檔空白時，應先刪舊單且不得提前送新單");

    gapOrders.EmitCancelled("QFS_gap", 1005m);
    Assert(gapOrders.Placed.Count == 2 &&
           gapOrders.Placed[1].OrderId == "QFS_gap_R1" &&
           (decimal)gapOrders.Placed[1].Price == 1003m &&
           gapOrders.Placed[1].Quantity == 1,
        "刪單確認後應以新委託識別碼把剩餘 1 口重下第一檔 1003");
    Assert(gapHistory.Entries.Any(x => x.Action == "CancelForFirstReentry") &&
           gapHistory.Entries.Any(x => x.Action == "PlaceAtFirst"),
        "重掛第一檔歷程應包含刪單及新單指令");
}

string historyDirectory = Path.Combine(Path.GetTempPath(), "KGIQueueFollowHistoryTests", Guid.NewGuid().ToString("N"));
using (var fileHistory = new JsonLineQueueFollowHistorySink(historyDirectory))
{
    fileHistory.Record(new QueueFollowHistoryEntry
    {
        Sequence = 1,
        EventType = QueueFollowHistoryEventType.MarketChanged,
        StrategyId = "file-test",
        Symbol = "JPX.225MC-202609",
        BidPrices = new[] { 100m },
        BidQuantities = new[] { 5 },
        AskPrices = new[] { 101m },
        AskQuantities = new[] { 6 }
    });
}
string historyFile = Directory.GetFiles(historyDirectory, "queue-follow-*.jsonl").Single();
string historyJson = File.ReadAllText(historyFile);
Assert(historyJson.Contains("\"eventType\":\"MarketChanged\"") && historyJson.Contains("\"bidPrices\":[100]"),
    "JSONL 歷程檔未正確寫入行情內容");
Directory.Delete(historyDirectory, recursive: true);

Console.WriteLine("QueueFollowStrategy smoke tests passed.");

static QueueFollowRequest Request(string id, BuySell side, int quantity, decimal limit, int a = 10, int b = 1, int c = 0) => new()
{
    StrategyId = id,
    Instrument = new InstrumentKey
    {
        Market = MarketType.ForeignFuture,
        Exchange = "JPX",
        Symbol = "225MC",
        ContractMonth = "202609"
    },
    QuoteSymbol = "JPX.225MC-202609",
    ProductId = "225MC",
    TickType = TickTypeEnum.tt1,
    Side = side,
    Quantity = quantity,
    InitialTargetAheadQuantity = a,
    RepriceAheadThreshold = b,
    RepriceTargetAheadQuantity = c,
    PriceLimit = limit,
    BrokerId = "F004000",
    Account = "TESTACCOUNT"
};

static OrderBookSnapshot Book(decimal bid, int bidQty, decimal ask, int askQty) => new()
{
    Symbol = "JPX.225MC-202609",
    BidPrices = new[] { bid },
    BidQuantities = new[] { bidQty },
    AskPrices = new[] { ask },
    AskQuantities = new[] { askQty },
    LastPrice = (bid + ask) / 2m
};

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class WholeNumberTicks : IQueueFollowTickProvider
{
    public decimal Next(decimal price, TickTypeEnum tickType, bool upward) => upward ? Math.Floor(price) + 1m : Math.Ceiling(price) - 1m;
    public decimal Regular(decimal price, TickTypeEnum tickType, bool upward) => upward ? Math.Ceiling(price) : Math.Floor(price);
}

sealed class MemoryHistory : IQueueFollowHistorySink
{
    public List<QueueFollowHistoryEntry> Entries { get; } = new();
    public void Record(QueueFollowHistoryEntry entry) => Entries.Add(entry);
}

sealed class FakeMarket : IQueueFollowMarketDataSource
{
    public event Action<OrderBookSnapshot>? MarketChanged;
    public OrderBookSnapshot? Current { get; set; }
    public void Subscribe(string symbol) { }
    public bool TryGetSnapshot(string symbol, out OrderBookSnapshot snapshot)
    {
        snapshot = Current ?? new OrderBookSnapshot();
        return Current != null;
    }
    public void Publish(OrderBookSnapshot snapshot)
    {
        Current = snapshot;
        MarketChanged?.Invoke(snapshot);
    }
}

sealed class FakeOrders : IQueueFollowOrderService
{
    public bool IsReady => true;
    public event Action<OrderInfo>? OrderChanged;
    public event Action<OrderInfo, int, double>? OrderFilled;
    public event Action<OrderInfo, string>? OrderFailed;
    public event Action<OrderInfo, string>? ModifyFailed;
    public event Action<OrderInfo, string>? CancelFailed;
    public List<OrderRequest> Placed { get; } = new();
    public List<decimal> Modified { get; } = new();
    public List<string> Cancelled { get; } = new();
    public void Place(OrderRequest request) => Placed.Add(request);
    public void Modify(string orderId, double price, int quantity) => Modified.Add((decimal)price);
    public void Cancel(string orderId) => Cancelled.Add(orderId);
    public void EmitAccepted(string orderId, decimal price, int leaves) => OrderChanged?.Invoke(new OrderInfo
    {
        OrderId = orderId,
        OrderPrice = (double)price,
        OrderQty = leaves,
        LeavesQty = leaves,
        Status = OrderStatus.Accepted
    });
    public void EmitModifyPending(string orderId, decimal price, int leaves) => OrderChanged?.Invoke(new OrderInfo
    {
        OrderId = orderId,
        OrderPrice = (double)price,
        OrderQty = leaves,
        LeavesQty = leaves,
        Status = OrderStatus.ModifyPending
    });
    public void EmitModifyFailed(string orderId, decimal price, int leaves, string reason)
    {
        var order = new OrderInfo
        {
            OrderId = orderId,
            OrderPrice = (double)price,
            OrderQty = leaves,
            LeavesQty = leaves,
            Status = OrderStatus.Accepted
        };
        ModifyFailed?.Invoke(order, reason);
    }
    public void EmitCancelFailed(string orderId, decimal price, int leaves, string reason)
    {
        var order = new OrderInfo
        {
            OrderId = orderId,
            OrderPrice = (double)price,
            OrderQty = leaves,
            LeavesQty = leaves,
            Status = OrderStatus.Accepted
        };
        CancelFailed?.Invoke(order, reason);
    }
    public void EmitCancelled(string orderId, decimal price) => OrderChanged?.Invoke(new OrderInfo
    {
        OrderId = orderId,
        OrderPrice = (double)price,
        OrderQty = 1,
        LeavesQty = 0,
        FilledQty = 0,
        Status = OrderStatus.Cancelled
    });
}
