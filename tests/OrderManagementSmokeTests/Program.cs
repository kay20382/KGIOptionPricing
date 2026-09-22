using System.Reflection;
using KGIOptionPricing;
using KGIOptionPricing.OrderManagement.Core;
using KGIOptionPricing.OrderManagement.Gateway;
using KGIOptionPricing.OrderManagement.Storage;
using KGIOptionPricing.StrategyFramework;
using KGIOptionPricing.StrategyFramework.Execution;

string storagePath = Path.Combine(Path.GetTempPath(), "KGIOptionPricing_OrderTests", Guid.NewGuid().ToString("N"));
var gateway = new FakeGateway();
var manager = new OrderManager(gateway, new OrderStorageManager(storagePath));

ForeignOrderContext context = new()
{
    Fcm = "JPXGW",
    ClearingAccount = "KGI Taiwan-JPX",
    TradeDate = "12345678",
    Source = "r",
    Cp = 'F',
    DayTrade = 'Y',
    PositionEffect = 'O',
    TimeInForce = 'R',
    PriceFlag = '2'
};

// 外部 O -> M (ErrMsg 非空但 ErrCode=0) -> C。
gateway.Emit(NewReport("root-1", "root-1", "ord-1", "204", OrderOperationType.New,
    OrderEventType.Accepted, "0", string.Empty, 65000, 1, context));
OrderInfo external = Single(manager.GetActiveOrders(), "外部新單未正確建立");
Assert(external.Source == OrderSource.External, "外部回報來源錯誤");
Assert(external.Identity.OrgCnt == "root-1", "OrgCnt 未保存");

gateway.Emit(NewReport("op-m1", "root-1", "ord-1", "204", OrderOperationType.Modify,
    OrderEventType.Modified, "0", "GWStatus:NEW", 65005, 1, context));
Assert(external.OrderPrice == 65005, "成功改價未更新價格");
Assert(external.LeavesQty == 1, "成功改價後剩餘量錯誤");
gateway.Emit(NewReport("op-m1", "root-1", "ord-1", "204", OrderOperationType.Modify,
    OrderEventType.Modified, "0", "GWStatus:NEW", 65005, 1, context));
Assert(external.ModifyHistory.Count == 1, "重複 3310 被重複處理");

// 錯誤 WEBID 的刪單失敗不得採用 AfterQty=0 或覆蓋原識別資料。
gateway.Emit(NewReport("op-c-fail", "root-1", "ord-1", "203", OrderOperationType.Cancel,
    OrderEventType.CancelFailed, "07", "後檯:查無此委託單", 65005, 0,
    new ForeignOrderContext()));
Assert(external.Status == OrderStatus.Accepted, "刪單失敗後委託未恢復有效狀態");
Assert(external.LeavesQty == 1, "刪單失敗錯誤採用了 AfterQty=0");
Assert(external.Identity.WebId == "204", "失敗回報覆蓋了原 WEBID");
Assert(external.ForeignContext?.Fcm == "JPXGW", "失敗回報覆蓋了原 FCM");
Assert(external.ForeignContext?.Cp == 'F' && external.ForeignContext.DayTrade == 'Y',
    "失敗回報覆蓋了原 CP/DayTrade");

gateway.Emit(NewReport("op-c-bad-org", "wrong-root", "ord-1", "204", OrderOperationType.Cancel,
    OrderEventType.CancelFailed, "12", "後檯:無剩餘口數可取消", 65005, 0, context));
Assert(external.Status == OrderStatus.Accepted && external.LeavesQty == 1,
    "錯誤 OrgCnt 的刪單失敗改變了原委託");

gateway.Emit(NewReport("op-c1", "root-1", "ord-1", "204", OrderOperationType.Cancel,
    OrderEventType.Cancelled, "0", string.Empty, 65005, 0, context));
Assert(external.Status == OrderStatus.Cancelled && external.LeavesQty == 0, "成功刪單狀態錯誤");
gateway.Emit(NewReport("late-m", "root-1", "ord-1", "204", OrderOperationType.Modify,
    OrderEventType.Modified, "0", string.Empty, 65010, 1, context));
Assert(external.Status == OrderStatus.Cancelled && external.LeavesQty == 0,
    "延遲改價回報讓已刪除委託復活");

// 內部 RequestId -> 3302.CNT -> 3310.OrgCnt，並驗證刪改請求沿用原始上手資料。
gateway.NextRequestId = "req-new";
manager.PlaceOrder(new OrderRequest
{
    OrderId = "internal-1",
    Instrument = ForeignInstrument(),
    BuySell = BuySell.Buy,
    Price = 62000,
    Quantity = 1,
    BrokerId = "F004000",
    Account = "9819113"
});
gateway.Emit(new OrderEvent
{
    Identity = new ExternalOrderIdentity { RequestId = "req-new", Cnt = "root-2" },
    EventType = OrderEventType.Ack,
    ErrorCode = "0"
});
Assert(manager.GetActiveOrders().Single(x => x.OrderId == "internal-1").LeavesQty == 1,
    "新單 ACK 不得用預設 AfterQty=0 清除原委託量");
gateway.Emit(NewReport("root-2", "root-2", "ord-2", "204", OrderOperationType.New,
    OrderEventType.Accepted, "0", string.Empty, 62000, 1, context));
OrderInfo internalOrder = manager.GetActiveOrders().Single(x => x.OrderId == "internal-1");
Assert(internalOrder.Source == OrderSource.Internal, "3302/3310 未關聯回內部訂單");

int recoverableModifyFailures = 0;
manager.OnModifyFailed += (_, _) => recoverableModifyFailures++;
gateway.NextRequestId = "req-mod-fail";
manager.ModifyOrder("internal-1", 62005, 1);
gateway.Emit(new OrderEvent
{
    Identity = new ExternalOrderIdentity { RequestId = "req-mod-fail" },
    EventType = OrderEventType.Ack,
    ErrorCode = "99",
    Message = "改價 ACK 失敗"
});
Assert(internalOrder.Status == OrderStatus.Accepted && internalOrder.LeavesQty == 1 && recoverableModifyFailures == 1,
    "錯誤 ACK 應還原原委託並通知可恢復的改價失敗，不得卡在 ModifyPending");

gateway.NextRequestId = "req-mod";
manager.ModifyOrder("internal-1", 62005, 1);
Assert(gateway.LastModify?.Identity.OrgCnt == "root-2", "改價未使用原始 OrgCnt");
Assert(gateway.LastModify?.ForeignContext?.TradeDate == "12345678", "改價未沿用原交易日期");
Assert(gateway.LastModify?.ForeignContext?.Fcm == "JPXGW", "改價未沿用原 FCM");
Assert(gateway.LastModify?.ForeignContext?.Cp == 'F', "改價未沿用原 CP");
Assert(gateway.LastModify?.ForeignContext?.DayTrade == 'Y', "改價未沿用原 DayTrade");

gateway.Emit(new OrderEvent
{
    Identity = new ExternalOrderIdentity { RequestId = "req-mod", Cnt = "op-m2" },
    EventType = OrderEventType.Ack,
    ErrorCode = "0"
});
Assert(internalOrder.LeavesQty == 1 && internalOrder.Status == OrderStatus.ModifyPending,
    "改價 ACK 不得清除原委託量或提前結束等待狀態");
gateway.Emit(NewReport("op-m2", "root-2", "ord-2", "204", OrderOperationType.Modify,
    OrderEventType.Modified, "0", "GWStatus:NEW", 62005, 1, context));
Assert(internalOrder.OrderPrice == 62005, "內部改價成功後價格錯誤");

// 國內 2010 連續改價可能沿用原 CNT 且沒有 RequestId；不同價格不得被誤判成重複回報。
gateway.NextRequestId = "req-mod-2";
manager.ModifyOrder("internal-1", 62010, 1);
gateway.Emit(new OrderEvent
{
    Identity = new ExternalOrderIdentity { RequestId = "req-mod-2", Cnt = "root-2" },
    EventType = OrderEventType.Ack,
    ErrorCode = "0"
});
gateway.Emit(NewReport("root-2", "root-2", "ord-2", "204", OrderOperationType.Modify,
    OrderEventType.Modified, "0", string.Empty, 62010, 1, context));
Assert(internalOrder.OrderPrice == 62010 && internalOrder.ModifyHistory.Count == 2,
    "同一 CNT 的第二次合法改價回報被錯誤去重");
gateway.Emit(NewReport("root-2", "root-2", "ord-2", "204", OrderOperationType.Modify,
    OrderEventType.Modified, "0", string.Empty, 62010, 1, context));
Assert(internalOrder.ModifyHistory.Count == 2, "完全相同的重送回報仍應去重");

gateway.NextRequestId = "req-cancel";
manager.CancelOrder("internal-1");
Assert(gateway.LastCancel?.Identity.OrgCnt == "root-2", "刪單未使用原始 OrgCnt");
Assert(gateway.LastCancel?.Identity.LastOperationCnt == "root-2", "最新操作 CNT 未保存");
Assert(gateway.LastCancel?.ForeignContext?.Cp == 'F', "刪單未沿用原 CP");
Assert(gateway.LastCancel?.ForeignContext?.DayTrade == 'Y', "刪單未沿用原 DayTrade");

Assert(ForeignOrderRules.ResolveCp(ForeignInstrument(), null) == 'F', "外期缺少回報時 CP 未回退為 F");
Assert(ForeignOrderRules.ResolveDayTrade(null) == 'N', "缺少回報時 DayTrade 未回退為 N");

// 驗證使用者指定的格式規則：只要求八碼數字，不驗證真實日曆日期。
Assert(ForeignOrderRules.TryValidateAmend(internalOrder, out _), "12345678 應通過八碼數字驗證");
internalOrder.ForeignContext!.TradeDate = "2026-08-27";
Assert(!ForeignOrderRules.TryValidateAmend(internalOrder, out _), "非八碼交易日期未被攔截");
internalOrder.ForeignContext.TradeDate = "12345678";
string savedFcm = internalOrder.ForeignContext.Fcm;
internalOrder.ForeignContext.Fcm = string.Empty;
Assert(!ForeignOrderRules.TryValidateAmend(internalOrder, out _), "空白上手券商未被攔截");
internalOrder.ForeignContext.Fcm = savedFcm;

// 3310/3311 固定七位小數價格格式。
MethodInfo parser = typeof(KGITradeService).GetMethod("ParseForeignReportPrice", BindingFlags.NonPublic | BindingFlags.Static)
    ?? throw new Exception("找不到外期價格解析器");
double parsed = (double)(parser.Invoke(null, new object?[] { "0065005000000" }) ?? 0d);
Assert(parsed == 65005, "外期固定小數價格解析錯誤");

// 外期 3310 使用商品代碼，行情契約可能使用完整 Symbol；月份與價格都需正確映射到閃電下單欄位。
var jpxContract = new ContractInfo
{
    ExchangeNm = "JPX",
    ProductId = "225MC",
    Symbol = "JPX.225MC-202609",
    ComYM2 = "202609",
    PxDecimal = 1
};
external.Instrument = ForeignInstrument();
external.OrderPrice = 65005.1d;
external.LeavesQty = 1;
external.Status = OrderStatus.Accepted;
var levels = WorkingOrderAggregator.Aggregate(new[] { external }, jpxContract);
Assert(levels.TryGetValue(65005.1m, out WorkingOrderLevel? level) && level.BuyLeaves == 1,
    "外期委託未依交易所、商品、月份或價格位數映射到閃電下單");

// 商品自動訂閱：優先 Hot，沒有 Hot 時選最近的未到期月份，並排除到期合約。
ContractInfo[] subscriptionContracts =
{
    new() { ProductId = "UNKNOWN", Symbol = "EXPIRED", ComType = 'F', Month = "202608", EndDate = "20260831" },
    new() { ProductId = "UNKNOWN", Symbol = "NEAR", ComType = 'F', Month = "202609", EndDate = "20260930" },
    new() { ProductId = "UNKNOWN", Symbol = "HOT", ComType = 'F', Month = "202610", EndDate = "20261031", Hot = true }
};
ContractInfo? hotContract = SymbolRepository.SelectHotMonthContract(subscriptionContracts, new DateTime(2026, 9, 7));
Assert(hotContract?.Symbol == "HOT", "自動訂閱未優先選擇 Hot 合約");

ContractInfo? nearestContract = SymbolRepository.SelectHotMonthContract(
    subscriptionContracts.Select(contract => contract with { Hot = false }),
    new DateTime(2026, 9, 7));
Assert(nearestContract?.Symbol == "NEAR", "無 Hot 標記時未選擇最近的未到期合約");

foreach ((string productId, string expectedNextSymbol) in new[]
{
    ("225", "225-QUARTER"),
    ("225M", "225M-QUARTER"),
    ("225MC", "225MC-QUARTER"),
    ("TPX", "TPX-QUARTER"),
    ("TPXM", "TPXM-QUARTER")
})
{
    ContractInfo[] jpxRollContracts =
    {
        new() { ProductId = productId, Symbol = $"{productId}-NEAR", ComType = 'F', Month = "202609", EndDate = "20260910" },
        new() { ProductId = productId, Symbol = $"{productId}-SERIAL", ComType = 'F', Month = "202610", EndDate = "20261008", Hot = true },
        new() { ProductId = productId, Symbol = $"{productId}-QUARTER", ComType = 'F', Month = "202612", EndDate = "20261210" }
    };
    ContractInfo? beforeRoll = SymbolRepository.SelectHotMonthContract(jpxRollContracts, new DateTime(2026, 9, 8));
    Assert(beforeRoll?.Symbol == $"{productId}-NEAR", $"{productId} 在到期前兩日應維持最近月");
    ContractInfo? onRoll = SymbolRepository.SelectHotMonthContract(jpxRollContracts, new DateTime(2026, 9, 9));
    Assert(onRoll?.Symbol == expectedNextSymbol, $"{productId} 未在到期前一日切換至正確的有效月份");
}

// QuoteCom 國外期貨的 ComType 實際可能回傳 '0'；MF_FUT 載入時的 CPF 分類必須優先。
ContractInfo quoteComForeignFuture = new()
{
    ProductId = "225MC",
    Symbol = "225MC-QUOTE-COM",
    ComType = '0',
    CPF = CPFEnum.Futures,
    Month = "202612",
    EndDate = "20261210"
};
Assert(SymbolRepository.SelectHotMonthContract(new[] { quoteComForeignFuture }, new DateTime(2026, 9, 7))?.Symbol == "225MC-QUOTE-COM",
    "QuoteCom ComType=0 的國外期貨被熱門月篩選器排除");

ContractInfo quoteComNumericTypeFuture = quoteComForeignFuture with
{
    Symbol = "225MC-NUMERIC-TYPE",
    CPF = CPFEnum.None
};
Assert(SymbolRepository.SelectHotMonthContract(new[] { quoteComNumericTypeFuture }, new DateTime(2026, 9, 7))?.Symbol == "225MC-NUMERIC-TYPE",
    "QuoteCom ComType='0' 且 CPF 未設定的國外期貨被熱門月篩選器排除");

ContractInfo foreignOptionMarkedByCpf = quoteComForeignFuture with
{
    Symbol = "225MC-OPTION",
    CPF = CPFEnum.Call
};
Assert(SymbolRepository.SelectHotMonthContract(new[] { foreignOptionMarkedByCpf }, new DateTime(2026, 9, 7)) is null,
    "國外選擇權不應混入熱門月期貨");

ContractInfo[] firstNoticeContracts =
{
    new()
    {
        ProductId = "TU", Symbol = "TU-U26", CPF = CPFEnum.Futures, Month = "202609",
        FirstNoticeDate = "20260908", LastTradeDate = "20260930", ExpiryDate = "20260930"
    },
    new()
    {
        ProductId = "TU", Symbol = "TU-Z26", CPF = CPFEnum.Futures, Month = "202612",
        FirstNoticeDate = "20261130", LastTradeDate = "20261231", ExpiryDate = "20261231"
    }
};
Assert(SymbolRepository.SelectHotMonthContract(firstNoticeContracts, new DateTime(2026, 9, 6))?.Symbol == "TU-U26",
    "FND 前兩日應維持原最近月");
Assert(SymbolRepository.SelectHotMonthContract(firstNoticeContracts, new DateTime(2026, 9, 7))?.Symbol == "TU-Z26",
    "有 FND 時未在第一通知日前一日換月");

ContractInfo[] lastTradeFallbackContracts = firstNoticeContracts
    .Select(contract => contract with { FirstNoticeDate = string.Empty })
    .ToArray();
Assert(SymbolRepository.SelectHotMonthContract(lastTradeFallbackContracts, new DateTime(2026, 9, 29))?.Symbol == "TU-Z26",
    "沒有 FND 時未回退至最後交易日前一日換月");

ContractInfo[] expiryFallbackContracts = lastTradeFallbackContracts
    .Select(contract => contract with { LastTradeDate = string.Empty })
    .ToArray();
Assert(SymbolRepository.SelectHotMonthContract(expiryFallbackContracts, new DateTime(2026, 9, 29))?.Symbol == "TU-Z26",
    "沒有 FND/LTD 時未回退至到期日前一日換月");

// 商品價差：A 買/B 賣時，Bid 是賣出價差（A Bid、B Ask），Ask 是買進價差（A Ask、B Bid）。
var spreadDefinition = new KGIOptionPricing.ProductSpreads.ProductSpreadDefinition
{
    Id = "spread-test",
    ProductName = "測試,價差",
    LegA = new() { ProductID = "A", Direction = KGIOptionPricing.ProductSpreads.SpreadLegDirection.Buy, Quantity = 2 },
    LegB = new() { ProductID = "B", Direction = KGIOptionPricing.ProductSpreads.SpreadLegDirection.Sell, Quantity = 1 },
    ShiftPoint = 0.10m,
    MinimumTick = 0.25m,
    PxDecimal = 2
};
var spreadLegA = new KGIOptionPricing.ProductSpreads.SpreadLegQuote
{
    ProductID = "A", Symbol = "A1", Direction = KGIOptionPricing.ProductSpreads.SpreadLegDirection.Buy,
    Quantity = 2, ContractMultiplier = 10m, PxDecimal = 1,
    LastPrice = 100.5m, BidPrice = 100m, AskPrice = 101m, BidQuantity = 7, AskQuantity = 11
};
var spreadLegB = new KGIOptionPricing.ProductSpreads.SpreadLegQuote
{
    ProductID = "B", Symbol = "B1", Direction = KGIOptionPricing.ProductSpreads.SpreadLegDirection.Sell,
    Quantity = 1, ContractMultiplier = 5m, PxDecimal = 1,
    LastPrice = 50.5m, BidPrice = 50m, AskPrice = 51m, BidQuantity = 8, AskQuantity = 9
};
var spreadQuote = KGIOptionPricing.ProductSpreads.ProductSpreadCalculator.Calculate(
    spreadDefinition, spreadLegA, spreadLegB, new DateTime(2026, 9, 7, 9, 0, 0));
Assert(spreadQuote.BidPrice == 1745.00m, "Spread Bid 或 RoundDown Tick 對齊錯誤");
Assert(spreadQuote.AskPrice == 1770.25m, "Spread Ask 或 RoundUp Tick 對齊錯誤");
Assert(spreadQuote.BidPrice < spreadQuote.AskPrice, "正常兩腳行情不應產生 Bid >= Ask");
Assert(spreadQuote.LastPrice == 1757.50m, "Spread 成交價或最近 Tick 對齊錯誤");
Assert(spreadQuote.BidQuantity == 3, "Spread 第一檔 Bid 量比例錯誤");
Assert(spreadQuote.AskQuantity == 5, "Spread 第一檔 Ask 量比例錯誤");

var snapshot = new KGIOptionPricing.ProductSpreads.ProductSpreadSnapshot
{
    SnapshotTime = new DateTime(2026, 9, 7, 9, 5, 6),
    Quote = spreadQuote
};
string csvLine = KGIOptionPricing.ProductSpreads.ProductSpreadCsvSnapshotStore.FormatLine(spreadDefinition, snapshot);
Assert(csvLine.StartsWith("20260907 09:05:06,\"測試,價差\",1757.50,3,1745.00,1770.25,5,"),
    "Spread CSV 欄位順序、時間或格式錯誤");

var beforeCutover = KGIOptionPricing.ProductSpreads.ProductSpreadCsvSnapshotStore.GetPeriod(new DateTime(2026, 9, 8, 5, 59, 59));
Assert(beforeCutover.FileDate == new DateTime(2026, 9, 7), "隔日 05:59 應歸入前一交易日檔案");
var atCutover = KGIOptionPricing.ProductSpreads.ProductSpreadCsvSnapshotStore.GetPeriod(new DateTime(2026, 9, 8, 6, 0, 0));
Assert(atCutover.FileDate == new DateTime(2026, 9, 8), "06:00 應建立新交易日檔案");
Assert(atCutover.StartLabel == "0600" && atCutover.EndLabel == "0600", "每日快照區間標示錯誤");

DateTime morningStartup = new(2026, 9, 9, 5, 50, 0);
Assert(KGIQuoteService.GetJpxSubscriptionNotBefore(morningStartup) == new DateTime(2026, 9, 9, 7, 20, 0),
    "05:50 排程啟動應延後 JPX 訂閱至 07:20");
Assert(KGIQuoteService.GetJpxSubscriptionNotBefore(new DateTime(2026, 9, 9, 7, 19, 59)) ==
       new DateTime(2026, 9, 9, 7, 20, 0),
    "07:20 前的清晨啟動應延後 JPX 訂閱");
Assert(KGIQuoteService.GetJpxSubscriptionNotBefore(new DateTime(2026, 9, 9, 7, 20, 0)) == null,
    "07:20 啟動不應延後 JPX 訂閱");
Assert(KGIQuoteService.GetJpxSubscriptionNotBefore(new DateTime(2026, 9, 9, 1, 0, 0)) == null,
    "日本夜盤期間啟動不應延後 JPX 訂閱");
Assert(KGIQuoteService.GetJpxSubscriptionNotBefore(
           new DateTime(2026, 9, 9, 5, 50, 0),
           new TimeSpan(7, 45, 0)) == new DateTime(2026, 9, 9, 7, 45, 0),
    "JPX 訂閱時間應採用 config 設定值");
Assert(KGIQuoteService.GetJpxSubscriptionNotBefore(
           new DateTime(2026, 9, 9, 7, 30, 0),
           new TimeSpan(7, 20, 0)) == null,
    "超過 config 訂閱時間後啟動應立即訂閱");

// 外部程序獨占主 CSV 時，不可遺失快照；改寫入同時段的 recovery 檔。
string lockedSnapshotDirectory = Path.Combine(
    Path.GetTempPath(),
    "KGIOptionPricing_SpreadSnapshotTests",
    Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(lockedSnapshotDirectory);
try
{
    var lockedDefinition = spreadDefinition with { SnapshotPath = lockedSnapshotDirectory };
    string lockedPrimaryPath = Path.Combine(
        lockedSnapshotDirectory,
        "20260907_spread-test.csv");
    await using (var externalLock = new FileStream(
        lockedPrimaryPath,
        FileMode.OpenOrCreate,
        FileAccess.ReadWrite,
        FileShare.Read))
    {
        using var snapshotStore = new KGIOptionPricing.ProductSpreads.ProductSpreadCsvSnapshotStore(
            lockedSnapshotDirectory);
        await snapshotStore.SaveAsync(lockedDefinition, snapshot);
    }

    string recoveryPath = Path.Combine(
        lockedSnapshotDirectory,
        $"20260907_spread-test_recovery_{Environment.ProcessId}.csv");
    Assert(File.Exists(recoveryPath), "主快照檔被獨占時沒有建立 recovery 檔");
    string recoveryText = await File.ReadAllTextAsync(recoveryPath);
    Assert(recoveryText.Contains(csvLine, StringComparison.Ordinal), "recovery 檔沒有保存原快照內容");
}
finally
{
    Directory.Delete(lockedSnapshotDirectory, recursive: true);
}

// BrokerLimit 的純映射不得碰觸 OrderManager；這裡只驗證送入券商前的委託內容。
InstrumentKey liveInstrument = ForeignInstrument();
var liveBuy = new PaperOrderRequest
{
    ClientOrderId = "cycle-1-buy-g1",
    StrategyInstanceId = "managed-live",
    Symbol = "225MC",
    Side = BuySell.Buy,
    Type = PaperOrderType.BrokerLimit,
    Role = PaperOrderRole.EntryLong,
    Price = 65000m,
    Quantity = 1
};
OrderRequest mappedBuy = LiveStrategyOrderMapper.CreateBrokerLimit(
    liveBuy, liveInstrument, "BRK", "ACC", "REAL-1");
Assert(mappedBuy.Instrument == liveInstrument && mappedBuy.BuySell == BuySell.Buy &&
       mappedBuy.Price == 65000d && mappedBuy.TimeInForce == TimeInForce.ROD &&
       mappedBuy.PositionEffect == PositionEffect.Auto && mappedBuy.Quantity == 1 &&
       mappedBuy.BrokerId == "BRK" && mappedBuy.Account == "ACC" && mappedBuy.OrderId == "REAL-1",
    "BrokerLimit 買單必須成為一口、正價格、Auto 的 ROD 限價單並保留券商識別");

OrderRequest mappedSell = LiveStrategyOrderMapper.CreateBrokerLimit(
    liveBuy with { ClientOrderId = "cycle-1-sell-g1", Side = BuySell.Sell, Price = 65005m },
    liveInstrument, "BRK", "ACC", "REAL-2");
Assert(mappedSell.BuySell == BuySell.Sell && mappedSell.Price == 65005d &&
       mappedSell.TimeInForce == TimeInForce.ROD && mappedSell.Quantity == 1 &&
       mappedSell.OrderId == "REAL-2",
    "BrokerLimit 賣單必須保留方向、限價與真實委託識別");

Assert(LiveStrategyOrderMapper.CreateBrokerLimit(
        liveBuy with { Quantity = 2 }, liveInstrument, "BRK", "ACC", "REAL-Q").Quantity == 2,
    "BrokerLimit mapper 必須保留正數口數；策略本身仍可限制固定一口");
AssertThrows<ArgumentOutOfRangeException>(() => LiveStrategyOrderMapper.CreateBrokerLimit(
    liveBuy with { Price = 0m }, liveInstrument, "BRK", "ACC", "REAL-P"),
    "BrokerLimit 必須拒絕非正價格");
AssertThrows<ArgumentException>(() => LiveStrategyOrderMapper.CreateBrokerLimit(
    liveBuy, liveInstrument, "", "ACC", "REAL-B"),
    "BrokerLimit 必須拒絕空白券商代碼");
AssertThrows<ArgumentException>(() => LiveStrategyOrderMapper.CreateBrokerLimit(
    liveBuy, liveInstrument, "BRK", "", "REAL-A"),
    "BrokerLimit 必須拒絕空白帳號");
AssertThrows<ArgumentException>(() => LiveStrategyOrderMapper.CreateBrokerLimit(
    liveBuy, liveInstrument, "BRK", "ACC", ""),
    "BrokerLimit 必須拒絕空白真實委託識別");
AssertThrows<ArgumentException>(() => LiveStrategyOrderMapper.CreateBrokerLimit(
    liveBuy with { Type = PaperOrderType.Limit }, liveInstrument, "BRK", "ACC", "REAL-T"),
    "live mapper 必須拒絕非 BrokerLimit 委託");
AssertThrows<ArgumentException>(() => LiveStrategyOrderMapper.CreateBrokerLimit(
    liveBuy with { ClientOrderId = "" }, liveInstrument, "BRK", "ACC", "REAL-C"),
    "BrokerLimit 必須拒絕空白 ClientOrderId");
AssertThrows<ArgumentException>(() => LiveStrategyOrderMapper.CreateBrokerLimit(
    liveBuy with { StrategyInstanceId = "" }, liveInstrument, "BRK", "ACC", "REAL-I"),
    "BrokerLimit 必須拒絕空白 StrategyInstanceId");
AssertThrows<ArgumentException>(() => LiveStrategyOrderMapper.CreateBrokerLimit(
    liveBuy with { Symbol = "" }, liveInstrument, "BRK", "ACC", "REAL-S"),
    "BrokerLimit 必須拒絕空白商品代碼");

InstrumentKey mappedFuture = LiveStrategyOrderMapper.ToInstrument(new ContractInfo
{
    ExchangeNm = "JPX",
    ProductId = "225MC",
    Symbol = "JPX.225MC-202609",
    ComYM2 = "202609",
    CPF = CPFEnum.Futures,
    ComType = '0'
});
Assert(mappedFuture.Market == MarketType.ForeignFuture && mappedFuture.Symbol == "225MC",
    "CPF=Futures、ComType='0' 的國外契約必須走 ForeignFuture/MF_FUT，不得誤判 ForeignOption");

var liveGateway = new FakeLiveStrategyOrderGateway();
var managedLifecycles = new List<OrderLifecycleEvent>();
var managedExecutions = new List<ExecutionEvent>();
var managedEventOrder = new List<string>();
var managedFailures = new List<string>();
using (var liveService = new KgiLiveStrategyExecutionService(
    liveGateway,
    "managed-live",
    new ResolvedProduct { ProductId = "225MC", Symbol = "225MC", TickSize = 5m, ContractMultiplier = 10m },
    new ContractInfo
    {
        ExchangeNm = "JPX", ProductId = "225MC", Symbol = "JPX.225MC-202609",
        ComYM2 = "202609", CPF = CPFEnum.Futures, ComType = '0'
    },
    36m))
{
    liveService.OrderLifecycleChanged += lifecycle =>
    {
        managedLifecycles.Add(lifecycle);
        managedEventOrder.Add("lifecycle:" + lifecycle.State);
        _ = liveService.GetWorkingOrders("managed-live");
    };
    liveService.Filled += execution =>
    {
        managedExecutions.Add(execution);
        managedEventOrder.Add("execution");
        _ = liveService.GetWorkingOrders("managed-live");
    };
    liveService.Failed += failure =>
    {
        managedFailures.Add(failure);
        _ = liveService.GetWorkingOrders("managed-live");
    };
    liveService.StateChanged += () => _ = liveService.GetWorkingOrders("managed-live");

    PaperOrder managedBuy = liveService.Submit(liveBuy, DateTimeOffset.UtcNow);
    PaperOrder managedSell = liveService.Submit(
        liveBuy with
        {
            ClientOrderId = "cycle-1-sell-g1",
            Side = BuySell.Sell,
            Role = PaperOrderRole.EntryShort,
            Price = 65005m
        },
        DateTimeOffset.UtcNow);
    Assert(liveService.LocalConditionCount == 0 && liveService.ActiveBrokerOrderCount == 2 &&
           liveService.GetWorkingOrders("managed-live").Select(x => x.ClientOrderId)
               .OrderBy(x => x).SequenceEqual(new[] { "cycle-1-buy-g1", "cycle-1-sell-g1" }),
        "兩張 BrokerLimit 必須立即獨立工作，不得進入 local OCO 或清除另一張");
    liveService.OnMarketEvent(new MarketEvent
    {
        Timestamp = DateTimeOffset.UtcNow, Symbol = "225MC", Kind = MarketEventKind.Trade, LastPrice = 66000m
    });
    Assert(liveService.ActiveBrokerOrderCount == 2,
        "BrokerLimit 不得被 local OCO 行情觸發流程清除");
    Assert(!liveService.CancelByClientOrderId("other-instance", "cycle-1-buy-g1") &&
           !liveService.CancelByClientOrderId("managed-live", "missing-client") &&
           liveService.GetWorkingOrders("other-instance").Count == 0,
        "精準刪單與工作委託查詢必須隔離策略 instance/client");

    OrderLifecycleEvent buyWorking = managedLifecycles.Last(x =>
        x.ClientOrderId == "cycle-1-buy-g1" && x.State == OrderLifecycleState.Working);
    Assert(!string.IsNullOrWhiteSpace(buyWorking.BrokerOrderId),
        "Working 回報必須保存券商委託識別");
    Assert(liveService.CancelByClientOrderId("managed-live", "cycle-1-buy-g1") &&
           liveService.ActiveBrokerOrderCount == 2,
        "CancelPending 期間仍必須視為有效 broker order");
    OrderLifecycleEvent buyCancelPending = managedLifecycles.Last(x =>
        x.ClientOrderId == "cycle-1-buy-g1" && x.State == OrderLifecycleState.CancelPending);
    Assert(buyCancelPending.BrokerOrderId == buyWorking.BrokerOrderId,
        "CancelPending 必須延續先前保存的 BrokerOrderId");
    liveGateway.EmitState(managedBuy.OrderId, OrderStatus.Cancelled, 0, 0);
    OrderLifecycleEvent buyCancelled = managedLifecycles.Last(x =>
        x.ClientOrderId == "cycle-1-buy-g1" && x.State == OrderLifecycleState.Cancelled);
    Assert(buyCancelled.BrokerOrderId == buyWorking.BrokerOrderId &&
           liveService.ActiveBrokerOrderCount == 1 &&
           liveService.GetWorkingOrders("managed-live").All(x => x.ClientOrderId != "cycle-1-buy-g1"),
        "Cancelled 終結後不得列為工作單，但 tombstone 必須保留券商識別");

    managedEventOrder.Clear();
    int executionsBeforeLateBuy = managedExecutions.Count;
    liveGateway.EmitFill(managedBuy.OrderId, cumulativeQuantity: 1, remainingQuantity: 0,
        OrderStatus.Filled, dealQuantity: 1, dealPrice: 65000d);
    liveGateway.EmitFill(managedBuy.OrderId, cumulativeQuantity: 1, remainingQuantity: 0,
        OrderStatus.Filled, dealQuantity: 1, dealPrice: 65000d);
    Assert(managedExecutions.Count == executionsBeforeLateBuy + 1,
        "Cancelled 後 late fill 必須入帳一次，重複成交 callback 不得重複發送 ExecutionEvent");
    Assert(managedEventOrder.Take(2).SequenceEqual(new[] { "execution", "lifecycle:Filled" }),
        "成交必須先送入共用 ledger，再發布 lifecycle Filled");

    liveGateway.EmitState(managedSell.OrderId, OrderStatus.Rejected, 0, 1, "exchange rejected");
    Assert(liveService.ActiveBrokerOrderCount == 0 && managedFailures.Count > 0,
        "Rejected 必須退出工作集合並保留失敗通知");
    managedEventOrder.Clear();
    int executionsBeforeLateSell = managedExecutions.Count;
    liveGateway.EmitFill(managedSell.OrderId, cumulativeQuantity: 1, remainingQuantity: 0,
        OrderStatus.Filled, dealQuantity: 1, dealPrice: 65005d);
    Assert(managedExecutions.Count == executionsBeforeLateSell + 1 &&
           managedEventOrder.Take(2).SequenceEqual(new[] { "execution", "lifecycle:Filled" }),
        "Rejected 後 late fill 仍必須入帳，且 lifecycle Filled 不得早於 ledger");
    Assert(liveService.ActiveBrokerOrderCount == 0,
        "兩張managed orders完整late fill後不得殘留active broker order");
}

var partialGateway = new FakeLiveStrategyOrderGateway();
var partialLifecycle = new List<OrderLifecycleEvent>();
var partialExecutions = new List<ExecutionEvent>();
var partialService = new KgiLiveStrategyExecutionService(
    partialGateway,
    "partial-live",
    new ResolvedProduct { ProductId = "225MC", Symbol = "225MC", TickSize = 5m, ContractMultiplier = 10m },
    new ContractInfo
    {
        ExchangeNm = "JPX", ProductId = "225MC", Symbol = "JPX.225MC-202609",
        ComYM2 = "202609", CPF = CPFEnum.Futures, ComType = '0'
    },
    36m);
partialService.OrderLifecycleChanged += partialLifecycle.Add;
partialService.Filled += partialExecutions.Add;
PaperOrder partialOrder = partialService.Submit(
    liveBuy with { StrategyInstanceId = "partial-live", ClientOrderId = "partial-g1", Quantity = 2 },
    DateTimeOffset.UtcNow);
partialGateway.EmitFill(partialOrder.OrderId, cumulativeQuantity: 1, remainingQuantity: 1,
    OrderStatus.PartiallyFilled, dealQuantity: 1, dealPrice: 65000d);
Assert(partialLifecycle.Last().State == OrderLifecycleState.PartiallyFilled &&
       partialLifecycle.Last().FilledQuantity == 1 && partialLifecycle.Last().RemainingQuantity == 1 &&
       partialExecutions.Count == 1 && partialService.ActiveBrokerOrderCount == 1,
    "部分成交必須回報準確累計／剩餘量且繼續列為工作單");
partialGateway.EmitState(partialOrder.OrderId, OrderStatus.Cancelled, 1, 0);
AssertThrows<InvalidOperationException>(partialService.Dispose,
    "部分成交後 Cancelled 仍須 quarantine，禁止拆 callback");
partialGateway.EmitFill(partialOrder.OrderId, cumulativeQuantity: 2, remainingQuantity: 0,
    OrderStatus.Filled, dealQuantity: 1, dealPrice: 65000d);
partialService.Dispose();
Assert(partialService.ActiveBrokerOrderCount == 0 && partialService.LocalConditionCount == 0,
    "委託終結後 Dispose 必須清除 tombstone 與本地條件");
int partialLifecycleCountAfterDispose = partialLifecycle.Count;
partialGateway.EmitState(partialOrder.OrderId, OrderStatus.Rejected, 1, 0);
Assert(partialLifecycle.Count == partialLifecycleCountAfterDispose,
    "Dispose 後必須解除 gateway callback 訂閱");

var reportedFillGateway = new FakeLiveStrategyOrderGateway();
var reportedFillService = CreateLiveService(reportedFillGateway, "reported-fill-live");
var reportedExecutions = new List<ExecutionEvent>();
reportedFillService.Filled += reportedExecutions.Add;
PaperOrder reportedFillOrder = reportedFillService.Submit(
    liveBuy with { StrategyInstanceId = "reported-fill-live", ClientOrderId = "reported-fill-g1" },
    DateTimeOffset.UtcNow);
reportedFillGateway.EmitState(reportedFillOrder.OrderId, OrderStatus.Filled, 1, 0);
Assert(reportedFillService.ActiveBrokerOrderCount == 0 &&
       reportedFillService.QuarantinedBrokerOrderCount == 1 && reportedExecutions.Count == 0,
    "terminal Filled report 的 quantity 尚未由 Fill callback 交付 ledger 時仍須 quarantine");
AssertThrows<InvalidOperationException>(reportedFillService.Dispose,
    "Filled lifecycle 不得取代 Fill callback ledger delivery");
reportedFillGateway.EmitFill(reportedFillOrder.OrderId, 1, 0, OrderStatus.Filled, 1, 65000d);
Assert(reportedExecutions.Count == 1 && reportedFillService.QuarantinedBrokerOrderCount == 0,
    "完整 Fill callback delivered quantity 一致後才算 settled");
reportedFillService.Dispose();

// Broker cumulative quantities are untrusted. A one-lot order reporting two
// fills may deliver at most one lot to the ledger and is never auto-settled.
var overfillGateway = new FakeLiveStrategyOrderGateway();
var overfillService = CreateLiveService(overfillGateway, "overfill-live");
var overfillExecutions = new List<ExecutionEvent>();
var overfillFailures = new List<string>();
overfillService.Filled += overfillExecutions.Add;
overfillService.Failed += overfillFailures.Add;
PaperOrder overfillOrder = overfillService.Submit(
    liveBuy with { StrategyInstanceId = "overfill-live", ClientOrderId = "overfill-g1" },
    DateTimeOffset.UtcNow);
overfillGateway.EmitFill(overfillOrder.OrderId, 2, 0, OrderStatus.Filled, 2, 65000d);
overfillGateway.EmitFill(overfillOrder.OrderId, 2, 0, OrderStatus.Filled, 2, 65000d);
overfillGateway.EmitFill(overfillOrder.OrderId, 1, 0, OrderStatus.Filled, 1, 65000d);
Assert(overfillExecutions.Sum(x => x.Quantity) == 1 && overfillService.ProtocolViolationCount == 1 &&
       overfillService.QuarantinedBrokerOrderCount == 1 && overfillFailures.Count == 1,
    "overfill/duplicate/out-of-order cumulative 最多入帳委託量一次，並須保留protocol violation quarantine");
AssertThrows<InvalidOperationException>(overfillService.Dispose,
    "overfill protocol violation 不得因 delivered quantity 達標而自動settle");

// Dispose 必須 fail closed：有 active broker order 時不可清除追蹤或解除 callback。
var disposeGateway = new FakeLiveStrategyOrderGateway();
var disposeLifecycle = new List<OrderLifecycleEvent>();
var disposeExecutions = new List<ExecutionEvent>();
var disposeService = CreateLiveService(disposeGateway, "dispose-live");
disposeService.OrderLifecycleChanged += disposeLifecycle.Add;
disposeService.Filled += disposeExecutions.Add;
PaperOrder disposeOrder = disposeService.Submit(
    liveBuy with { StrategyInstanceId = "dispose-live", ClientOrderId = "dispose-g1" },
    DateTimeOffset.UtcNow);
AssertThrows<InvalidOperationException>(() => disposeService.Dispose(),
    "尚有 active BrokerLimit 時 Dispose 必須 fail closed");
Assert(disposeService.ActiveBrokerOrderCount == 1 &&
       disposeService.GetWorkingOrders("dispose-live").Count == 1,
    "Dispose 失敗後不得清除 active count 或工作單");
disposeGateway.EmitFill(disposeOrder.OrderId, 1, 0, OrderStatus.Filled, 1, 65000d);
Assert(disposeExecutions.Count == 1 && disposeLifecycle.Last().State == OrderLifecycleState.Filled &&
       disposeService.ActiveBrokerOrderCount == 0,
    "Dispose 失敗後仍須接收 late fill，並在成交後歸零 active count");
disposeService.Dispose();

// Terminal state is not safe to dispose until its execution/lifecycle callbacks are delivered.
using var fillReadyToPublish = new ManualResetEventSlim(false);
using var releaseFillPublish = new ManualResetEventSlim(false);
var drainGateway = new FakeLiveStrategyOrderGateway();
var drainExecutions = new List<ExecutionEvent>();
var drainService = CreateLiveService(drainGateway, "drain-live", beforeFilledPublished: () =>
{
    fillReadyToPublish.Set();
    releaseFillPublish.Wait();
});
drainService.Filled += drainExecutions.Add;
PaperOrder drainOrder = drainService.Submit(
    liveBuy with { StrategyInstanceId = "drain-live", ClientOrderId = "drain-g1" }, DateTimeOffset.UtcNow);
Task emitBlockedFill = Task.Factory.StartNew(
    () => drainGateway.EmitFill(drainOrder.OrderId, 1, 0, OrderStatus.Filled, 1, 65000d),
    CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
Assert(fillReadyToPublish.Wait(TimeSpan.FromSeconds(5)), "測試前置：fill callback 必須停在發布 Filled 前");
AssertThrows<InvalidOperationException>(drainService.Dispose,
    "terminal-but-undelivered callback inflight 時 Dispose 必須立即 fail closed");
Assert(drainExecutions.Count == 0, "阻塞期間 execution 尚不得被誤判為已發布");
releaseFillPublish.Set();
Assert(emitBlockedFill.Wait(TimeSpan.FromSeconds(5)) && drainExecutions.Count == 1,
    "Dispose 失敗後 terminal-but-undelivered callback 必須完整送入 ledger/strategy 路由");
drainService.Dispose();

var operationGateway = new FakeLiveStrategyOrderGateway { BlockPlaceOrder = true };
var operationService = CreateLiveService(operationGateway, "operation-live");
Task<PaperOrder> blockedSubmit = Task.Factory.StartNew(
    () => operationService.Submit(
        liveBuy with { StrategyInstanceId = "operation-live", ClientOrderId = "operation-g1" },
        DateTimeOffset.UtcNow),
    CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
Assert(operationGateway.PlaceOrderEntered.Wait(TimeSpan.FromSeconds(5)),
    "測試前置：Submit 必須持 operation lease 進入 gateway call");
AssertThrows<InvalidOperationException>(operationService.Dispose,
    "gateway call 期間 Dispose 必須立即 fail closed，不得拆 tracking/subscription");
Assert(operationService.ActiveBrokerOrderCount == 1,
    "operation Dispose 失敗後 active tracking 必須保留");
operationGateway.ReleasePlaceOrder.Set();
Assert(blockedSubmit.Wait(TimeSpan.FromSeconds(5)), "release 後 Submit 必須完成");
operationGateway.EmitFill(blockedSubmit.Result.OrderId, 1, 0, OrderStatus.Filled, 1, 65000d);
operationService.Dispose();

var terminalOperationGateway = new FakeLiveStrategyOrderGateway
{
    BlockPlaceOrder = true,
    EmitFillBeforePlaceOrderRelease = true
};
var terminalOperationService = CreateLiveService(terminalOperationGateway, "terminal-operation-live");
Task<PaperOrder> terminalBlockedSubmit = Task.Factory.StartNew(
    () => terminalOperationService.Submit(
        liveBuy with { StrategyInstanceId = "terminal-operation-live", ClientOrderId = "terminal-operation-g1" },
        DateTimeOffset.UtcNow),
    CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
Assert(terminalOperationGateway.PlaceOrderEntered.Wait(TimeSpan.FromSeconds(5)) &&
       terminalOperationService.ActiveBrokerOrderCount == 0,
    "同步 fill 後 active 必須歸零，但 Submit operation 仍卡在 gateway return 前");
AssertThrows<InvalidOperationException>(terminalOperationService.Dispose,
    "active=0/callback=0 但 operation inflight>0 時 Dispose 仍必須 fail closed");
terminalOperationGateway.ReleasePlaceOrder.Set();
Assert(terminalBlockedSubmit.Wait(TimeSpan.FromSeconds(5)), "operation release 後 Submit 必須返回");
terminalOperationService.Dispose();

// StrategyHost.Remove 必須在 broker Dispose 成功後才移除 runner 或解除路由。
var hostGateway = new FakeLiveStrategyOrderGateway();
var hostService = CreateLiveService(hostGateway, "host-live");
var hostSource = new FakeHostMarketSource();
var host = new StrategyHost(hostSource);
{
    var hostStrategy = new HostManagedProbeStrategy("host-live", "225MC");
    host.Add(
        hostStrategy,
        new ResolvedStrategyUniverse
        {
            Tradable = new ResolvedProduct
            {
                ProductId = "225MC", Symbol = "225MC", TickSize = 5m, ContractMultiplier = 10m
            }
        },
        36m,
        hostService);
    PaperOrderRequest hostRequest = liveBuy with
    {
        StrategyInstanceId = "host-live", ClientOrderId = "host-g1"
    };
    hostStrategy.Request(hostRequest);
    PaperOrder hostOrder = hostService.GetWorkingOrders("host-live").Single();
    AssertThrows<InvalidOperationException>(() => host.Remove("host-live"),
        "Host Remove 必須傳遞 active broker 的 fail-closed Dispose");
    Assert(host.GetSnapshots().Any(x => x.InstanceId == "host-live"),
        "Remove 失敗後 runner 必須仍留在 host");
    hostGateway.EmitFill(hostOrder.OrderId, 1, 0, OrderStatus.Filled, 1, 65000d);
    Assert(hostStrategy.Executions.Count == 1 && hostStrategy.Lifecycle.Last().State == OrderLifecycleState.Filled,
        "Remove 失敗後 runner 必須仍接收 late fill 與 lifecycle");
    AssertThrows<InvalidOperationException>(() => host.Remove("host-live"),
        "broker 歸零但 ledger 非零仍不得移除 runner");
    hostService.Flatten(1);
    PaperOrder hostFlatten = hostService.GetWorkingOrders("host-live").Single();
    hostGateway.EmitFill(hostGateway.LastOrderId, 1, 0, OrderStatus.Filled, 1, 64995d);
    Assert(host.Ledger.GetPosition("host-live", "225MC").NetQuantity == 0,
        "Flatten 成交必須先使 ledger 歸零");
    Assert(host.Remove("host-live") && host.GetSnapshots().All(x => x.InstanceId != "host-live"),
        "broker 與 ledger 同時歸零後第二次 Remove 才可成功移除 runner");
}

var retainedGateway = new FakeLiveStrategyOrderGateway();
var retainedService = CreateLiveService(retainedGateway, "retained-start-live");
using (var retainedSource = new FakeHostMarketSource())
using (var retainedHost = new StrategyHost(retainedSource))
{
    var retainedStrategy = new ThrowAfterOrderStartStrategy("retained-start-live", "225MC");
    AssertThrows<AggregateException>(() => retainedHost.Add(
            retainedStrategy,
            new ResolvedStrategyUniverse
            {
                Tradable = new ResolvedProduct { ProductId = "225MC", Symbol = "225MC", TickSize = 5m, ContractMultiplier = 10m }
            },
            36m,
            retainedService),
        "Start 送出 active BrokerLimit 後 throw 時 Add 必須 fail closed 並保留可管理 Runner");
    Assert(retainedHost.GetSnapshots().Any(x => x.InstanceId == "retained-start-live") &&
           retainedService.ActiveBrokerOrderCount == 1,
        "Start-after-order failure 必須保留 runner、active tracking 與 callback routes");
    PaperOrder retainedOrder = retainedService.GetWorkingOrders("retained-start-live").Single();
    AssertThrows<InvalidOperationException>(() => retainedHost.Remove("retained-start-live"),
        "active orphan cleanup 不得偽裝成功");
    retainedGateway.EmitFill(retainedOrder.OrderId, 1, 0, OrderStatus.Filled, 1, 65000d);
    Assert(retainedStrategy.Executions.Count == 1 && retainedHost.Ledger
            .GetPosition("retained-start-live", "225MC").NetQuantity == 1,
        "retained Runner 必須繼續接收 late fill 並更新 ledger/strategy");
    retainedService.Flatten(1);
    PaperOrder retainedFlatten = retainedService.GetWorkingOrders("retained-start-live").Single();
    retainedGateway.EmitFill(retainedGateway.LastOrderId, 1, 0, OrderStatus.Filled, 1, 64995d);
    Assert(retainedHost.Remove("retained-start-live"), "ledger 歸零後 retained runner 才可清理");
}

// Runner's ledger-flat decision and live execution teardown share the same
// callback exclusion lease: a terminal broker state cannot slip a fill between them.
var atomicDisposeGateway = new FakeLiveStrategyOrderGateway();
using var fillBeforeLedger = new ManualResetEventSlim(false);
using var releaseFillToLedger = new ManualResetEventSlim(false);
var atomicDisposeExecution = CreateLiveService(atomicDisposeGateway, "atomic-dispose-live",
    beforeFilledPublished: () => { fillBeforeLedger.Set(); releaseFillToLedger.Wait(); });
using (var atomicDisposeHost = new StrategyHost(new FakeHostMarketSource()))
{
    var atomicDisposeStrategy = new HostManagedProbeStrategy("atomic-dispose-live", "225MC");
    atomicDisposeHost.Add(atomicDisposeStrategy, new ResolvedStrategyUniverse
    {
        Tradable = new ResolvedProduct { ProductId = "225MC", Symbol = "225MC", TickSize = 5m, ContractMultiplier = 10m }
    }, 36m, atomicDisposeExecution);
    atomicDisposeStrategy.Request(liveBuy with
    {
        StrategyInstanceId = "atomic-dispose-live", ClientOrderId = "atomic-dispose-g1"
    });
    PaperOrder atomicDisposeOrder = atomicDisposeExecution.GetWorkingOrders("atomic-dispose-live").Single();
    Task publishFill = Task.Factory.StartNew(() => atomicDisposeGateway.EmitFill(
        atomicDisposeOrder.OrderId, 1, 0, OrderStatus.Filled, 1, 65000d), TaskCreationOptions.LongRunning);
    Assert(fillBeforeLedger.Wait(TimeSpan.FromSeconds(5)), "fill callback 未停在 ledger publish 前");
    AssertThrows<InvalidOperationException>(() => atomicDisposeHost.Remove("atomic-dispose-live"),
        "position check 前已進入的 fill callback 必須阻擋 execution teardown");
    releaseFillToLedger.Set();
    Assert(publishFill.Wait(TimeSpan.FromSeconds(5)) &&
           atomicDisposeHost.Ledger.GetPosition("atomic-dispose-live", "225MC").NetQuantity == 1,
        "release 後 late fill 必須先進原 ledger");
    AssertThrows<InvalidOperationException>(() => atomicDisposeHost.Remove("atomic-dispose-live"),
        "ledger 非零時 atomic safe-dispose 必須 fail closed");
    atomicDisposeExecution.Flatten(1);
    atomicDisposeGateway.EmitFill(atomicDisposeGateway.LastOrderId, 1, 0, OrderStatus.Filled, 1, 64995d);
    Assert(atomicDisposeHost.Remove("atomic-dispose-live"), "ledger 歸零且無 callback 後才可 teardown");
}

// Live service owns a failed Start atomically: the executor must never be
// independently cleaned up while Host still owns the retained runner.
var atomicSource = new FakeHostMarketSource();
var atomicHost = new StrategyHost(atomicSource);
var atomicGateway = new FakeLiveStrategyOrderGateway();
ThrowAfterOrderStartStrategy? atomicStrategy = null;
using var liveCatchEntered = new ManualResetEventSlim(false);
using var releaseLiveCatch = new ManualResetEventSlim(false);
var atomicUniverse = new ResolvedStrategyUniverse
{
    Tradable = new ResolvedProduct { ProductId = "225MC", Symbol = "225MC", TickSize = 5m, ContractMultiplier = 10m }
};
var atomicLive = new LiveStrategyService(
    atomicHost,
    () => true,
    () => "TEST",
    _ => atomicUniverse,
    definition => atomicStrategy = new ThrowAfterOrderStartStrategy(definition.InstanceId, "225MC"),
    (definition, universe, commission) => CreateLiveService(atomicGateway, definition.InstanceId),
    enableHeartbeat: false,
    afterHostAddFailure: () => { liveCatchEntered.Set(); releaseLiveCatch.Wait(); });
var atomicDefinition = new StrategyDefinition
{
    StrategyType = "ThrowAfterOrderStart", InstanceId = "live-retained", Universe = new StrategyUniverse
    {
        Tradable = new ProductSelection("225MC", "225MC"), References = Array.Empty<ProductSelection>()
    }
};
Exception? atomicAddError = null;
Task atomicAdd = Task.Factory.StartNew(() =>
{
    try { atomicLive.Add(atomicDefinition, 36m, 1000m); }
    catch (Exception exception) { atomicAddError = exception; }
}, TaskCreationOptions.LongRunning);
Assert(liveCatchEntered.Wait(TimeSpan.FromSeconds(5)),
    "Host 第一次 cleanup fail 後必須可固定 Live catch race");
string cancelledAtomicOrderId = atomicGateway.LastOrderId;
atomicGateway.EmitState(cancelledAtomicOrderId, OrderStatus.Cancelled, 0, 0);
releaseLiveCatch.Set();
Assert(atomicAdd.Wait(TimeSpan.FromSeconds(5)) && atomicAddError is AggregateException,
    "terminal race 後 Live Add 仍須回報原始 Start/cleanup failure");
LiveStrategyCloseStatus atomicStatus = atomicLive.GetCloseStatus("live-retained");
Assert(atomicStatus.Exists && atomicStatus.ActiveBrokerOrders == 0 && atomicStatus.QuarantinedBrokerOrders == 1,
    "Host cleanup fail 與 Live catch 間 terminal 仍須保留 instance ownership/status");
Assert(atomicLive.EmergencyStop("live-retained"), "retained failure 必須可由 EmergencyStop 管理");
Assert(!atomicLive.RequestClose("live-retained", false, out _),
    "Cancelled quarantine 未 settlement 前不得宣告安全關閉");
AssertThrows<InvalidOperationException>(atomicLive.Dispose,
    "Live Dispose 必須包含 awaiting late-fill settlement 並 fail closed");
atomicGateway.EmitFill(cancelledAtomicOrderId, 1, 0, OrderStatus.Filled, 1, 65000d);
Assert(atomicLive.GetCloseStatus("live-retained").Position == 1,
    "Cancelled 後 late fill 必須回到原 runner ledger");
Assert(!atomicLive.RequestClose("live-retained", true, out _),
    "late fill 後 RequestClose 必須送 Flatten");
atomicGateway.EmitFill(atomicGateway.LastOrderId, 1, 0, OrderStatus.Filled, 1, 64995d);
Assert(atomicLive.RequestClose("live-retained", true, out _),
    "late fill Flatten 成交後才能完成關閉");
atomicLive.Dispose();

// A synchronous fill before Start throws leaves active=0 but a real position;
// cleanup must retain the runner/executor until the public close API flattens it.
var filledSource = new FakeHostMarketSource();
var filledHost = new StrategyHost(filledSource);
var filledGateway = new FakeLiveStrategyOrderGateway { EmitFillBeforePlaceOrderRelease = true };
var filledLive = new LiveStrategyService(
    filledHost, () => true, () => "TEST", _ => atomicUniverse,
    definition => new ThrowAfterOrderStartStrategy(definition.InstanceId, "225MC"),
    (definition, universe, commission) => CreateLiveService(filledGateway, definition.InstanceId),
    enableHeartbeat: false);
var filledDefinition = atomicDefinition with { InstanceId = "live-filled-retained" };
AssertThrows<AggregateException>(() => filledLive.Add(filledDefinition, 36m, 1000m),
    "Start sync fill 後 throw 不得清除非零 ledger runner");
LiveStrategyCloseStatus filledStatus = filledLive.GetCloseStatus("live-filled-retained");
Assert(filledStatus.Exists && filledStatus.ActiveBrokerOrders == 0 && filledStatus.Position == 1,
    "sync fill retained 狀態必須公開可見");
filledGateway.EmitFillBeforePlaceOrderRelease = false;
Assert(!filledLive.RequestClose("live-filled-retained", true, out _),
    "非零部位第一次 RequestClose 應送 Flatten 並等待成交");
PaperOrder flattenOrder = GetLiveExecution(filledLive, "live-filled-retained")
    .GetWorkingOrders("live-filled-retained").Single();
filledGateway.EmitFill(filledGateway.LastOrderId, 1, 0, OrderStatus.Filled, 1, 64995d);
Assert(filledLive.RequestClose("live-filled-retained", true, out _),
    "Flatten fill 後必須可安全完成 retained cleanup");
filledLive.Dispose();

var overfillHost = new StrategyHost(new FakeHostMarketSource());
var overfillLiveGateway = new FakeLiveStrategyOrderGateway();
var overfillLive = new LiveStrategyService(
    overfillHost, () => true, () => "TEST", _ => atomicUniverse,
    definition => new ThrowAfterOrderStartStrategy(definition.InstanceId, "225MC"),
    (definition, universe, commission) => CreateLiveService(overfillLiveGateway, definition.InstanceId),
    enableHeartbeat: false);
AssertThrows<AggregateException>(() => overfillLive.Add(
        atomicDefinition with { InstanceId = "live-overfill" }, 36m, 1000m),
    "active entry 後 Start failure 必須保留供overfill測試");
overfillLiveGateway.EmitFill(overfillLiveGateway.LastOrderId, 2, 0, OrderStatus.Filled, 2, 65000d);
LiveStrategyCloseStatus overfillStatus = overfillLive.GetCloseStatus("live-overfill");
Assert(overfillStatus.Position == 1 && overfillStatus.ProtocolViolations == 1 &&
       overfillStatus.QuarantinedBrokerOrders == 1,
    "close status 必須暴露overfill異常，ledger部位最多一口且不可安全Dispose");
AssertThrows<InvalidOperationException>(overfillLive.Dispose,
    "overfill retained runner 必須fail closed");

// The flatten token is the ownership gate: while the winning caller is still
// inside the gateway, a concurrent close attempt must not submit a second exit.
var parallelFlattenHost = new StrategyHost(new FakeHostMarketSource());
var parallelFlattenGateway = new FakeLiveStrategyOrderGateway { EmitFillBeforePlaceOrderRelease = true };
var parallelFlattenLive = new LiveStrategyService(
    parallelFlattenHost, () => true, () => "TEST", _ => atomicUniverse,
    definition => new ThrowAfterOrderStartStrategy(definition.InstanceId, "225MC"),
    (definition, universe, commission) => CreateLiveService(parallelFlattenGateway, definition.InstanceId),
    enableHeartbeat: false);
var parallelFlattenDefinition = atomicDefinition with { InstanceId = "live-parallel-flatten" };
AssertThrows<AggregateException>(() => parallelFlattenLive.Add(parallelFlattenDefinition, 36m, 1000m),
    "parallel flatten 前置必須保留 sync-fill 部位");
parallelFlattenGateway.EmitFillBeforePlaceOrderRelease = false;
parallelFlattenGateway.BlockPlaceOrder = true;
Task<bool> winningClose = Task.Factory.StartNew(
    () => parallelFlattenLive.RequestClose("live-parallel-flatten", true, out _),
    TaskCreationOptions.LongRunning);
Assert(parallelFlattenGateway.PlaceOrderEntered.Wait(TimeSpan.FromSeconds(2)),
    "第一個 flatten 必須進入 gateway barrier");
Assert(!parallelFlattenLive.RequestClose("live-parallel-flatten", true, out _) &&
       parallelFlattenGateway.PlaceOrderCount == 2,
    "第二個並行 TryFinalize 必須在 flatten token gate 返回且不得送第二張");
parallelFlattenGateway.ReleasePlaceOrder.Set();
Assert(winningClose.Wait(TimeSpan.FromSeconds(2)) && !winningClose.Result &&
       parallelFlattenGateway.PlaceOrderCount == 2,
    "winning close 完成後仍只能有一張 flatten");
parallelFlattenGateway.BlockPlaceOrder = false;
parallelFlattenGateway.EmitFill(parallelFlattenGateway.LastOrderId, 1, 0, OrderStatus.Filled, 1, 64995d);
Assert(parallelFlattenLive.RequestClose("live-parallel-flatten", true, out _),
    "唯一 flatten 成交後應可完成 close");
parallelFlattenLive.Dispose();

using var flattenRegistrationEntered = new ManualResetEventSlim(false);
using var releaseFlattenRegistration = new ManualResetEventSlim(false);
int flattenRegistrationCalls = 0;
var correlatedFlattenHost = new StrategyHost(new FakeHostMarketSource());
var correlatedFlattenGateway = new FakeLiveStrategyOrderGateway { EmitFillBeforePlaceOrderRelease = true };
var correlatedFlattenLive = new LiveStrategyService(
    correlatedFlattenHost, () => true, () => "TEST", _ => atomicUniverse,
    definition => new ThrowAfterOrderStartStrategy(definition.InstanceId, "225MC"),
    (definition, universe, commission) => CreateLiveService(
        correlatedFlattenGateway, definition.InstanceId, beforeFlattenRegistration: () =>
        {
            if (Interlocked.Increment(ref flattenRegistrationCalls) != 1) return;
            flattenRegistrationEntered.Set();
            releaseFlattenRegistration.Wait();
        }),
    enableHeartbeat: false);
AssertThrows<AggregateException>(() => correlatedFlattenLive.Add(
        atomicDefinition with { InstanceId = "live-correlated-flatten" }, 36m, 1000m),
    "correlated flatten 前置必須保留一口部位");
correlatedFlattenGateway.EmitFillBeforePlaceOrderRelease = false;
KgiLiveStrategyExecutionService correlatedExecution = GetLiveExecution(correlatedFlattenLive, "live-correlated-flatten");
Task<bool> correlatedFirst = Task.Factory.StartNew(
    () => correlatedFlattenLive.RequestClose("live-correlated-flatten", true, out _),
    TaskCreationOptions.LongRunning);
Assert(flattenRegistrationEntered.Wait(TimeSpan.FromSeconds(2)),
    "flatten token reserve 後、real order註冊前必須進入測試barrier");
PaperOrder unrelated = correlatedExecution.Submit(
    liveBuy with { StrategyInstanceId = "live-correlated-flatten", ClientOrderId = "unrelated-entry" },
    DateTimeOffset.UtcNow);
correlatedFlattenGateway.EmitState(unrelated.OrderId, OrderStatus.Cancelled, 0, 0);
Assert(!correlatedFlattenLive.RequestClose("live-correlated-flatten", true, out _) &&
       correlatedFlattenGateway.PlaceOrderCount == 2,
    "不相干entry terminal不得清除目前flatten token或送第二張exit");
releaseFlattenRegistration.Set();
Assert(correlatedFirst.Wait(TimeSpan.FromSeconds(2)) && correlatedFlattenGateway.PlaceOrderCount == 3,
    "釋放barrier後只能註冊原本那張flatten");

var offlineFlattenHost = new StrategyHost(new FakeHostMarketSource());
var offlineFlattenGateway = new FakeLiveStrategyOrderGateway { EmitFillBeforePlaceOrderRelease = true };
var offlineFlattenLive = new LiveStrategyService(
    offlineFlattenHost, () => true, () => "TEST", _ => atomicUniverse,
    definition => new ThrowAfterOrderStartStrategy(definition.InstanceId, "225MC"),
    (definition, universe, commission) => CreateLiveService(offlineFlattenGateway, definition.InstanceId),
    enableHeartbeat: false);
AssertThrows<AggregateException>(() => offlineFlattenLive.Add(
        atomicDefinition with { InstanceId = "live-offline-flatten" }, 36m, 1000m),
    "offline flatten 前置必須保留一口部位");
offlineFlattenGateway.EmitFillBeforePlaceOrderRelease = false;
offlineFlattenGateway.IsLoggedIn = false;
Assert(!offlineFlattenLive.RequestClose("live-offline-flatten", true, out _) &&
       offlineFlattenGateway.PlaceOrderCount == 1,
    "gateway離線的單次RequestClose不得送單、遞迴或留下永久token");
offlineFlattenGateway.IsLoggedIn = true;
offlineFlattenLive.Pulse(DateTimeOffset.UtcNow);
Assert(offlineFlattenGateway.PlaceOrderCount == 2,
    "登入後下一個Pulse必須恰好重送一張flatten");

// Flatten terminal failure clears the in-flight guard exactly once. Rejected
// and Cancelled attempts may be retried without concurrent duplicate exits.
var retryFlattenHost = new StrategyHost(new FakeHostMarketSource());
var retryFlattenGateway = new FakeLiveStrategyOrderGateway { EmitFillBeforePlaceOrderRelease = true };
var retryFlattenLive = new LiveStrategyService(
    retryFlattenHost, () => true, () => "TEST", _ => atomicUniverse,
    definition => new ThrowAfterOrderStartStrategy(definition.InstanceId, "225MC"),
    (definition, universe, commission) => CreateLiveService(retryFlattenGateway, definition.InstanceId),
    enableHeartbeat: false);
var retryFlattenDefinition = atomicDefinition with { InstanceId = "live-flatten-retry" };
AssertThrows<AggregateException>(() => retryFlattenLive.Add(retryFlattenDefinition, 36m, 1000m),
    "retry flatten 前置必須保留 sync-fill 部位");
retryFlattenGateway.EmitFillBeforePlaceOrderRelease = false;
retryFlattenGateway.PlaceOutcomes.Enqueue(FakePlaceOutcome.Rejected);
retryFlattenGateway.PlaceOutcomes.Enqueue(FakePlaceOutcome.Cancelled);
retryFlattenGateway.PlaceOutcomes.Enqueue(FakePlaceOutcome.Throw);
retryFlattenGateway.PlaceOutcomes.Enqueue(FakePlaceOutcome.None);
Assert(!retryFlattenLive.RequestClose("live-flatten-retry", true, out _), "第一次 close 應送 flatten");
Assert(retryFlattenGateway.PlaceOrderCount == 2,
    "同步 Rejected callback 不得在同一 RequestClose call stack 遞迴重送");
retryFlattenLive.Pulse(DateTimeOffset.UtcNow);
string cancelledFlatten = retryFlattenGateway.LastOrderId;
Assert(retryFlattenGateway.PlaceOrderCount == 3,
    "下一個 Pulse 才能在同步 Rejected 後重送一次，且同步 Cancelled 不得形成 storm");
retryFlattenLive.Pulse(DateTimeOffset.UtcNow.AddMilliseconds(250));
Assert(retryFlattenGateway.PlaceOrderCount == 4,
    "PlaceOrder throw 後下一個 Pulse 只能重送一次");
retryFlattenLive.Pulse(DateTimeOffset.UtcNow.AddMilliseconds(500));
string successfulFlatten = retryFlattenGateway.LastOrderId;
Assert(retryFlattenGateway.PlaceOrderCount == 5 &&
       GetLiveExecution(retryFlattenLive, "live-flatten-retry").ActiveBrokerOrderCount == 1,
    "再下一個 Pulse 才送第4次 Flatten，並行 gate只能保留一張 active exit");
retryFlattenGateway.EmitFill(successfulFlatten, 1, 0, OrderStatus.Filled, 1, 64995d);
Assert(retryFlattenLive.GetCloseStatus("live-flatten-retry").QuarantinedBrokerOrders >= 2,
    "Rejected/Cancelled 原單都必須保持 quarantine，成功 Flatten 不得猜測其 settlement");
AssertThrows<InvalidOperationException>(retryFlattenLive.Dispose,
    "未收到完整 Fill callback 的 Rejected/Cancelled quarantine 必須阻擋 Dispose");

// A loss snapshot published synchronously during Start is deferred until the
// add lease exits, then risk processing must run exactly once and flatten.
var startRiskHost = new StrategyHost(new FakeHostMarketSource());
var startRiskGateway = new FakeLiveStrategyOrderGateway { EmitFillBeforePlaceOrderRelease = true };
var startRiskLive = new LiveStrategyService(
    startRiskHost, () => true, () => "TEST", _ => atomicUniverse,
    definition => new ThrowAfterOrderStartStrategy(definition.InstanceId, "225MC", publishSnapshotBeforeThrow: true),
    (definition, universe, commission) => new KgiLiveStrategyExecutionService(
        startRiskGateway, definition.InstanceId, universe.Tradable,
        new ContractInfo { ExchangeNm = "JPX", ProductId = "225MC", Symbol = "JPX.225MC-202609", ComYM2 = "202609", CPF = CPFEnum.Futures, ComType = '0' },
        commission),
    enableHeartbeat: false);
var startRiskDefinition = atomicDefinition with { InstanceId = "live-start-risk" };
AssertThrows<AggregateException>(() => startRiskLive.Add(startRiskDefinition, 36m, 1m),
    "Start sync fill 後 throw 仍須回報 Add failure");
Assert(startRiskGateway.PlaceOrderCount == 2 &&
       startRiskLive.GetCloseStatus("live-start-risk").Position == 0,
    "Start lease 內 daily-loss snapshot 必須在 lease 退出後可靠觸發一次 EmergencyStop/Flatten");
Assert(startRiskLive.RequestClose("live-start-risk", true, out _),
    "延後風控 Flatten 歸零後 retained runner 必須可完成 close");
startRiskLive.Dispose();

// Pending risk is scoped to one Add incarnation. A flat, fully-settled failed
// incarnation must not stop a later Add that reuses the same instanceId.
var generationHost = new StrategyHost(new FakeHostMarketSource());
var generationGateway = new FakeLiveStrategyOrderGateway { EmitFillBeforePlaceOrderRelease = true };
int generationCreateCount = 0;
var generationLive = new LiveStrategyService(
    generationHost, () => true, () => "TEST", _ => atomicUniverse,
    definition => Interlocked.Increment(ref generationCreateCount) == 1
        ? new RoundTripLossThrowStrategy(definition.InstanceId, "225MC")
        : new HostManagedProbeStrategy(definition.InstanceId, "225MC"),
    (definition, universe, commission) => new KgiLiveStrategyExecutionService(
        generationGateway, definition.InstanceId, universe.Tradable,
        new ContractInfo { ExchangeNm = "JPX", ProductId = "225MC", Symbol = "JPX.225MC-202609", ComYM2 = "202609", CPF = CPFEnum.Futures, ComType = '0' },
        commission),
    enableHeartbeat: false);
var generationDefinition = atomicDefinition with { InstanceId = "live-generation" };
AssertThrows<InvalidOperationException>(() => generationLive.Add(generationDefinition, 36m, 1m),
    "flat roundtrip loss incarnation 應回報 Start failure並完整清理");
generationGateway.EmitFillBeforePlaceOrderRelease = false;
generationLive.Add(generationDefinition, 36m, 1m);
Assert(generationLive.GetCloseStatus("live-generation").Exists && generationGateway.PlaceOrderCount == 2 &&
       generationLive.GetSnapshots().Single(x => x.InstanceId == "live-generation").State == StrategyLifecycleState.Ready,
    "同ID新generation不得繼承舊loss reason或自動送Flatten");
generationLive.EmergencyStop("live-generation");
generationLive.RequestClose("live-generation", false, out _);
generationLive.Dispose();

// A throwing diagnostic hook cannot bypass ownership cleanup.
var hookHost = new StrategyHost(new FakeHostMarketSource());
var hookGateway = new FakeLiveStrategyOrderGateway();
KgiLiveStrategyExecutionService? hookExecution = null;
var hookLive = new LiveStrategyService(
    hookHost, () => true, () => "TEST", _ => atomicUniverse,
    definition => new ThrowBeforeOrderStartStrategy(definition.InstanceId),
    (definition, universe, commission) => hookExecution = CreateLiveService(hookGateway, definition.InstanceId),
    enableHeartbeat: false,
    afterHostAddFailure: () => throw new InvalidOperationException("hook failed"));
AssertThrows<AggregateException>(() => hookLive.Add(atomicDefinition with { InstanceId = "live-hook-failure" }, 36m, 1000m),
    "afterHostAddFailure hook exception 必須被aggregate但不可跳過cleanup");
Assert(hookHost.GetSnapshots().Count == 0 && hookExecution != null,
    "無 retained runner 的 hook failure 不得留下Host orphan");
AssertThrows<ObjectDisposedException>(() => hookExecution!.Submit(
        liveBuy with { StrategyInstanceId = "live-hook-failure", ClientOrderId = "disposed-hook" }, DateTimeOffset.UtcNow),
    "hook failure 後 executor 必須已安全Dispose");
hookLive.Dispose();

var retainedHookHost = new StrategyHost(new FakeHostMarketSource());
var retainedHookGateway = new FakeLiveStrategyOrderGateway();
var retainedHookLive = new LiveStrategyService(
    retainedHookHost, () => true, () => "TEST", _ => atomicUniverse,
    definition => new ThrowAfterOrderStartStrategy(definition.InstanceId, "225MC"),
    (definition, universe, commission) => CreateLiveService(retainedHookGateway, definition.InstanceId),
    enableHeartbeat: false,
    afterHostAddFailure: () => throw new InvalidOperationException("hook failed retained"));
AggregateException? retainedHookError = null;
try
{
    retainedHookLive.Add(atomicDefinition with { InstanceId = "live-hook-retained" }, 36m, 1000m);
}
catch (AggregateException exception) { retainedHookError = exception; }
string retainedHookDiagnostics = string.Join(" | ", retainedHookError?.Flatten().InnerExceptions.Select(x => x.Message)
    ?? Array.Empty<string>());
Assert(retainedHookError != null && retainedHookDiagnostics.Contains("start failed") &&
       retainedHookDiagnostics.Contains("hook failed retained") &&
       retainedHookDiagnostics.Contains("實盤委託", StringComparison.Ordinal),
    "Start/add cleanup與afterHostAddFailure三類診斷必須完整保留於AggregateException");
Assert(retainedHookLive.GetCloseStatus("live-hook-retained").Exists,
    "aggregate diagnostics不得破壞retained public ownership");
retainedHookGateway.EmitFill(retainedHookGateway.LastOrderId, 1, 0, OrderStatus.Filled, 1, 65000d);
Assert(!retainedHookLive.RequestClose("live-hook-retained", true, out _),
    "retained hook late fill後必須可由public API送flatten");
retainedHookGateway.EmitFill(retainedHookGateway.LastOrderId, 1, 0, OrderStatus.Filled, 1, 64995d);
Assert(retainedHookLive.RequestClose("live-hook-retained", true, out _),
    "retained hook flatten後必須可安全close");
retainedHookLive.Dispose();

// Start is a lifecycle lease. Close APIs wait for it; a duplicate Add is
// rejected before creating a second executor or live path.
var leaseSource = new FakeHostMarketSource();
var leaseHost = new StrategyHost(leaseSource);
var leaseGateway = new FakeLiveStrategyOrderGateway();
using var startEntered = new ManualResetEventSlim(false);
using var releaseStart = new ManualResetEventSlim(false);
using var executionFactoryEntered = new ManualResetEventSlim(false);
using var releaseExecutionFactory = new ManualResetEventSlim(false);
int leaseExecutors = 0;
var leaseLive = new LiveStrategyService(
    leaseHost, () => true, () => "TEST", _ => atomicUniverse,
    definition => definition.InstanceId == "live-add-lease"
        ? new BarrierStartStrategy(definition.InstanceId, "225MC", startEntered, releaseStart)
        : new HostManagedProbeStrategy(definition.InstanceId, "225MC"),
    (definition, universe, commission) =>
    {
        Interlocked.Increment(ref leaseExecutors);
        if (definition.InstanceId == "live-add-lease")
        {
            executionFactoryEntered.Set();
            releaseExecutionFactory.Wait();
        }
        return CreateLiveService(leaseGateway, definition.InstanceId);
    },
    enableHeartbeat: false);
var leaseDefinition = atomicDefinition with { StrategyType = "BarrierStart", InstanceId = "live-add-lease" };
var activeLeaseDefinition = atomicDefinition with { StrategyType = "HostManagedProbe", InstanceId = "live-active-a" };
leaseLive.Add(activeLeaseDefinition, 36m, 1000m);
Task addLease = Task.Factory.StartNew(() => leaseLive.Add(leaseDefinition, 36m, 1000m), TaskCreationOptions.LongRunning);
Assert(executionFactoryEntered.Wait(TimeSpan.FromSeconds(5)), "execution factory barrier 未進入");
AssertThrows<InvalidOperationException>(() => leaseLive.Add(leaseDefinition, 36m, 1000m),
    "reservation 必須在 resolve/factory/Host.Add 前阻擋 concurrent duplicate Add");
Assert(leaseExecutors == 2 && leaseHost.GetSnapshots().Count == 1,
    "factory barrier 期間不得建立第二個 B executor/order 或公開 partial B runner");
Assert(leaseLive.EmergencyStop("live-active-a") &&
       leaseLive.RequestClose("live-active-a", false, out _),
    "B 卡在 execution factory/Start 時既有 A 的 EmergencyStop/RequestClose 不得被全域阻塞");
releaseExecutionFactory.Set();
Assert(startEntered.Wait(TimeSpan.FromSeconds(5)), "Start barrier 未進入");
Task<bool> closeDuringAdd = Task.Factory.StartNew(
    () => leaseLive.EmergencyStop("live-add-lease"), TaskCreationOptions.LongRunning);
Assert(!closeDuringAdd.Wait(TimeSpan.FromMilliseconds(100)),
    "EmergencyStop 不得在 Start lease 結束前先回 true");
releaseStart.Set();
Task.WaitAll(addLease, closeDuringAdd);
Assert(closeDuringAdd.Result && leaseExecutors == 2 && leaseLive.GetSnapshots().Count == 2,
    "Start lease 完成後 close 應生效，且 B 只能存在一條 runner/executor/live path");
leaseLive.RequestClose("live-add-lease", false, out _);
leaseLive.Dispose();

// 非法倒退 callback 不得讓終結單或 CancelPending 回到錯誤狀態。
var raceGateway = new FakeLiveStrategyOrderGateway();
var raceLifecycle = new List<OrderLifecycleEvent>();
var raceFailures = new List<string>();
var raceService = CreateLiveService(raceGateway, "race-live");
raceService.OrderLifecycleChanged += raceLifecycle.Add;
raceService.Failed += raceFailures.Add;
PaperOrder cancelledRace = raceService.Submit(
    liveBuy with { StrategyInstanceId = "race-live", ClientOrderId = "race-cancelled" },
    DateTimeOffset.UtcNow);
raceGateway.EmitState(cancelledRace.OrderId, OrderStatus.Cancelled, 0, 0);
int afterCancelled = raceLifecycle.Count;
int failuresAfterCancelled = raceFailures.Count;
raceGateway.EmitState(cancelledRace.OrderId, OrderStatus.Rejected, 0, 0, "late reject");
Assert(raceLifecycle.Count == afterCancelled && raceFailures.Count == failuresAfterCancelled,
    "Cancelled 後 late reject 不得發布 lifecycle 或 Failed");

PaperOrder filledRace = raceService.Submit(
    liveBuy with { StrategyInstanceId = "race-live", ClientOrderId = "race-filled" },
    DateTimeOffset.UtcNow);
raceGateway.EmitFill(filledRace.OrderId, 1, 0, OrderStatus.Filled, 1, 65000d);
int afterFilled = raceLifecycle.Count;
int failuresAfterFilled = raceFailures.Count;
raceGateway.EmitState(filledRace.OrderId, OrderStatus.Rejected, 1, 0, "late reject");
Assert(raceLifecycle.Count == afterFilled && raceFailures.Count == failuresAfterFilled,
    "Filled 後 late reject 不得發布 lifecycle 或 Failed");

PaperOrder pendingRace = raceService.Submit(
    liveBuy with { StrategyInstanceId = "race-live", ClientOrderId = "race-pending" },
    DateTimeOffset.UtcNow);
Assert(raceService.CancelByClientOrderId("race-live", "race-pending"),
    "測試委託應進入 CancelPending");
int workingBeforeLateAccepted = raceLifecycle.Count(x =>
    x.ClientOrderId == "race-pending" && x.State == OrderLifecycleState.Working);
raceGateway.EmitState(pendingRace.OrderId, OrderStatus.Accepted, 0, 1);
Assert(raceLifecycle.Count(x =>
           x.ClientOrderId == "race-pending" && x.State == OrderLifecycleState.Working) == workingBeforeLateAccepted &&
       raceLifecycle.Last(x => x.ClientOrderId == "race-pending").State == OrderLifecycleState.CancelPending,
    "CancelPending 後 late Accepted 不得倒退回 Working");
raceGateway.EmitState(pendingRace.OrderId, OrderStatus.Cancelled, 0, 0);
raceGateway.EmitState(pendingRace.OrderId, OrderStatus.Rejected, 0, 0, "settled");
AssertThrows<InvalidOperationException>(raceService.Dispose,
    "Rejected/Cancelled terminal report 未由 Fill callback 完整交付前仍須 quarantine");
raceGateway.EmitFill(cancelledRace.OrderId, 1, 0, OrderStatus.Filled, 1, 65000d);
raceGateway.EmitFill(pendingRace.OrderId, 1, 0, OrderStatus.Filled, 1, 65000d);
raceService.Dispose();

// Cancelled tombstone 沒有 definitive settlement 時不可因時間經過而猜測安全。
DateTimeOffset retentionNow = new(2026, 9, 22, 1, 0, 0, TimeSpan.Zero);
var retentionGateway = new FakeLiveStrategyOrderGateway();
var retentionExecutions = new List<ExecutionEvent>();
var retentionService = CreateLiveService(retentionGateway, "retention-live", () => retentionNow);
retentionService.Filled += retentionExecutions.Add;
var expiredOrders = new List<PaperOrder>();
for (int i = 0; i < 25; i++)
{
    PaperOrder item = retentionService.Submit(
        liveBuy with
        {
            StrategyInstanceId = "retention-live", ClientOrderId = $"expired-{i}", Price = 65000m + i * 5m
        },
        retentionNow);
    expiredOrders.Add(item);
    retentionGateway.EmitState(item.OrderId, OrderStatus.Cancelled, 0, 0);
}
Assert(retentionService.ActiveBrokerOrderCount == 0,
    "終結 tombstone 不得計入 active count");
retentionNow = retentionNow.AddMinutes(11);
PaperOrder retainedNew = retentionService.Submit(
    liveBuy with { StrategyInstanceId = "retention-live", ClientOrderId = "retained-new" },
    retentionNow);
retentionGateway.EmitFill(expiredOrders[0].OrderId, 1, 0, OrderStatus.Filled, 1, 65000d);
Assert(retentionExecutions.Count == 1 && retentionService.ActiveBrokerOrderCount == 1 &&
       retentionService.QuarantinedBrokerOrderCount == 24,
    "超過十分鐘仍須保留 Cancelled quarantine，late fill 必須繼續路由");
retentionGateway.EmitState(retainedNew.OrderId, OrderStatus.Cancelled, 0, 0);
AssertThrows<InvalidOperationException>(retentionService.Dispose,
    "無 definitive settlement 的 Cancelled quarantine 必須永久 fail closed");

Console.WriteLine("OrderManagement smoke tests passed.");

static KgiLiveStrategyExecutionService CreateLiveService(
    ILiveStrategyOrderGateway gateway,
    string instanceId,
    Func<DateTimeOffset>? now = null,
    Action? beforeFilledPublished = null,
    Action? beforeFlattenRegistration = null) => new(
        gateway,
        instanceId,
        new ResolvedProduct { ProductId = "225MC", Symbol = "225MC", TickSize = 5m, ContractMultiplier = 10m },
        new ContractInfo
        {
            ExchangeNm = "JPX", ProductId = "225MC", Symbol = "JPX.225MC-202609",
            ComYM2 = "202609", CPF = CPFEnum.Futures, ComType = '0'
        },
        36m,
        now,
        beforeFilledPublished,
        beforeFlattenRegistration);

static KgiLiveStrategyExecutionService GetLiveExecution(LiveStrategyService service, string instanceId)
{
    var field = typeof(LiveStrategyService).GetField("_executions", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Live execution ownership field missing");
    var map = (System.Collections.Concurrent.ConcurrentDictionary<string, KgiLiveStrategyExecutionService>)field.GetValue(service)!;
    return map[instanceId];
}

static InstrumentKey ForeignInstrument() => new()
{
    Market = MarketType.ForeignFuture,
    Exchange = "JPX",
    Symbol = "225MC",
    ContractMonth = "202609"
};

static OrderEvent NewReport(
    string cnt,
    string orgCnt,
    string ordNo,
    string webId,
    OrderOperationType operation,
    OrderEventType eventType,
    string errorCode,
    string message,
    double price,
    int leaves,
    ForeignOrderContext context) => new()
{
    Identity = new ExternalOrderIdentity
    {
        Cnt = cnt,
        LastOperationCnt = cnt,
        OrgCnt = orgCnt,
        OrdNo = ordNo,
        WebId = webId
    },
    ForeignContext = context,
    Instrument = ForeignInstrument(),
    BuySell = BuySell.Buy,
    OrderPrice = price,
    OrderQty = leaves,
    LeavesQty = leaves,
    UpdatedPrice = price,
    UpdatedQty = leaves,
    BrokerId = "F004000",
    Account = "9819113",
    OperationType = operation,
    EventType = eventType,
    ErrorCode = errorCode,
    Message = message,
    EventTime = DateTime.Now
};

static OrderInfo Single(OrderInfo[] orders, string message)
{
    Assert(orders.Length == 1, message);
    return orders[0];
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

static void AssertThrows<TException>(Action action, string message) where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new Exception(message);
}

sealed class FakeGateway : IOrderGateway
{
    public string NextRequestId { get; set; } = "request";
    public ModifyOrderRequest? LastModify { get; private set; }
    public CancelOrderRequest? LastCancel { get; private set; }

    public event Action<OrderEvent>? OrderEventReceived;
    public event Action<FillEvent>? FillEventReceived;
    public event Action<RecoveryEvent>? RecoveryEventReceived;

    public OrderResult NewOrder(OrderRequest request) => new() { IsSuccess = true, RequestId = NextRequestId };
    public OrderResult ModifyOrder(ModifyOrderRequest request)
    {
        LastModify = request;
        return new OrderResult { IsSuccess = true, RequestId = NextRequestId };
    }
    public OrderResult CancelOrder(CancelOrderRequest request)
    {
        LastCancel = request;
        return new OrderResult { IsSuccess = true, RequestId = NextRequestId };
    }

    public void Emit(OrderEvent ev) => OrderEventReceived?.Invoke(ev);
}

sealed class FakeLiveStrategyOrderGateway : ILiveStrategyOrderGateway
{
    private readonly Dictionary<string, OrderInfo> _orders = new(StringComparer.OrdinalIgnoreCase);

    public bool IsLoggedIn { get; set; } = true;
    public string BrokerId { get; set; } = "BRK";
    public string Account { get; set; } = "ACC";
    public bool BlockPlaceOrder { get; set; }
    public bool EmitFillBeforePlaceOrderRelease { get; set; }
    public ManualResetEventSlim PlaceOrderEntered { get; } = new(false);
    public ManualResetEventSlim ReleasePlaceOrder { get; } = new(false);
    public string LastOrderId { get; private set; } = string.Empty;
    public int PlaceOrderCount { get; private set; }
    public Queue<FakePlaceOutcome> PlaceOutcomes { get; } = new();

    public event Action<OrderInfo>? OrderStateChanged;
    public event Action<OrderInfo, int, double>? OrderFilled;
    public event Action<OrderInfo, string>? OrderFailed;
    public event Action<OrderInfo, string>? CancelFailed;

    public void PlaceOrder(OrderRequest request)
    {
        var order = new OrderInfo
        {
            OrderId = request.OrderId,
            Instrument = request.Instrument,
            BuySell = request.BuySell,
            OrderPrice = request.Price,
            OrderQty = request.Quantity,
            LeavesQty = request.Quantity,
            Status = OrderStatus.Accepted,
            BrokerId = request.BrokerId,
            Account = request.Account,
            Identity = new ExternalOrderIdentity { OrdNo = "ORD-" + request.OrderId }
        };
        _orders[request.OrderId] = order;
        LastOrderId = request.OrderId;
        PlaceOrderCount++;
        OrderStateChanged?.Invoke(order);
        FakePlaceOutcome outcome = PlaceOutcomes.Count == 0 ? FakePlaceOutcome.None : PlaceOutcomes.Dequeue();
        if (outcome == FakePlaceOutcome.Throw) throw new InvalidOperationException("synchronous place failure");
        if (outcome is FakePlaceOutcome.Rejected or FakePlaceOutcome.Cancelled)
        {
            order.Status = outcome == FakePlaceOutcome.Rejected ? OrderStatus.Rejected : OrderStatus.Cancelled;
            order.LeavesQty = 0;
            OrderStateChanged?.Invoke(order);
        }
        if (EmitFillBeforePlaceOrderRelease)
        {
            order.Status = OrderStatus.Filled;
            order.FilledQty = request.Quantity;
            order.LeavesQty = 0;
            OrderFilled?.Invoke(order, request.Quantity, request.Price);
        }
        if (BlockPlaceOrder)
        {
            PlaceOrderEntered.Set();
            ReleasePlaceOrder.Wait();
        }
    }

    public void CancelOrder(string orderId)
    {
        if (!_orders.TryGetValue(orderId, out OrderInfo? order)) return;
        order.Status = OrderStatus.CancelPending;
        OrderStateChanged?.Invoke(order);
    }

    public void EmitState(
        string orderId,
        OrderStatus status,
        int cumulativeQuantity,
        int remainingQuantity,
        string message = "")
    {
        OrderInfo order = _orders[orderId];
        order.Status = status;
        order.FilledQty = cumulativeQuantity;
        order.LeavesQty = remainingQuantity;
        order.Message = message;
        OrderStateChanged?.Invoke(order);
    }

    public void EmitFill(
        string orderId,
        int cumulativeQuantity,
        int remainingQuantity,
        OrderStatus status,
        int dealQuantity,
        double dealPrice)
    {
        OrderInfo order = _orders[orderId];
        order.Status = status;
        order.FilledQty = cumulativeQuantity;
        order.LeavesQty = remainingQuantity;
        order.AvgFilledPrice = dealPrice;
        OrderFilled?.Invoke(order, dealQuantity, dealPrice);
        OrderStateChanged?.Invoke(order);
    }

    public void EmitOrderFailed(string orderId, string reason) => OrderFailed?.Invoke(_orders[orderId], reason);
    public void EmitCancelFailed(string orderId, string reason) => CancelFailed?.Invoke(_orders[orderId], reason);
}

enum FakePlaceOutcome { None, Rejected, Cancelled, Throw }

sealed class FakeHostMarketSource : IMarketEventSource
{
    public event Action<MarketEvent>? MarketEventReceived;
    public void Subscribe(IEnumerable<ResolvedProduct> products) { }
    public void Emit(MarketEvent marketEvent) => MarketEventReceived?.Invoke(marketEvent);
    public void Dispose() { }
}

sealed class ThrowAfterOrderStartStrategy : ISimulationStrategy, IManagedOrderStrategy
{
    private readonly string _symbol;
    private readonly bool _publishSnapshotBeforeThrow;
    public ThrowAfterOrderStartStrategy(string instanceId, string symbol, bool publishSnapshotBeforeThrow = false)
    { InstanceId = instanceId; _symbol = symbol; _publishSnapshotBeforeThrow = publishSnapshotBeforeThrow; }
    public string StrategyType => "ThrowAfterOrderStart";
    public string InstanceId { get; }
    public StrategyLifecycleState State { get; private set; } = StrategyLifecycleState.Created;
    public List<ExecutionEvent> Executions { get; } = new();
    public event Action<PaperOrderRequest>? OrderRequested;
    public event Action? CancelAllRequested;
    public event Action<StrategySnapshot>? SnapshotChanged;
    public event Action<string>? CancelOrderRequested { add { } remove { } }
    public void Start(StrategyRuntimeContext context)
    {
        OrderRequested?.Invoke(new PaperOrderRequest
        {
            StrategyInstanceId = InstanceId, ClientOrderId = "start-active-g1", Symbol = _symbol,
            Side = BuySell.Buy, Type = PaperOrderType.BrokerLimit, Role = PaperOrderRole.EntryLong,
            Price = 65000m, Quantity = 1
        });
        if (_publishSnapshotBeforeThrow) SnapshotChanged?.Invoke(GetSnapshot());
        throw new InvalidOperationException("start failed after order");
    }
    public void OnOrderLifecycle(OrderLifecycleEvent orderEvent) { }
    public void OnExecution(ExecutionEvent execution) => Executions.Add(execution);
    public void OnMarketEvent(MarketEvent marketEvent) { }
    public void Pause(string reason) => State = StrategyLifecycleState.Suspended;
    public void Resume() => State = StrategyLifecycleState.Ready;
    public void Stop(string reason) { CancelAllRequested?.Invoke(); State = StrategyLifecycleState.Stopped; }
    public StrategySnapshot GetSnapshot() => new() { StrategyType = StrategyType, InstanceId = InstanceId, State = State };
}

sealed class ThrowBeforeOrderStartStrategy : ISimulationStrategy
{
    public ThrowBeforeOrderStartStrategy(string instanceId) => InstanceId = instanceId;
    public string StrategyType => "ThrowBeforeOrder";
    public string InstanceId { get; }
    public StrategyLifecycleState State => StrategyLifecycleState.Created;
    public event Action<PaperOrderRequest>? OrderRequested;
    public event Action? CancelAllRequested;
    public event Action<StrategySnapshot>? SnapshotChanged;
    public void Start(StrategyRuntimeContext context) => throw new InvalidOperationException("start failed before order");
    public void OnMarketEvent(MarketEvent marketEvent) { }
    public void OnExecution(ExecutionEvent execution) { }
    public void Pause(string reason) { }
    public void Resume() { }
    public void Stop(string reason) { }
    public StrategySnapshot GetSnapshot() => new() { StrategyType = StrategyType, InstanceId = InstanceId, State = State };
}

sealed class RoundTripLossThrowStrategy : ISimulationStrategy
{
    private readonly string _symbol;
    public RoundTripLossThrowStrategy(string instanceId, string symbol) { InstanceId = instanceId; _symbol = symbol; }
    public string StrategyType => "RoundTripLossThrow";
    public string InstanceId { get; }
    public StrategyLifecycleState State { get; private set; } = StrategyLifecycleState.Created;
    public event Action<PaperOrderRequest>? OrderRequested;
    public event Action? CancelAllRequested;
    public event Action<StrategySnapshot>? SnapshotChanged;
    public void Start(StrategyRuntimeContext context)
    {
        OrderRequested?.Invoke(new PaperOrderRequest { StrategyInstanceId = InstanceId, ClientOrderId = "rt-buy", Symbol = _symbol, Side = BuySell.Buy, Type = PaperOrderType.BrokerLimit, Role = PaperOrderRole.EntryLong, Price = 65000m, Quantity = 1 });
        OrderRequested?.Invoke(new PaperOrderRequest { StrategyInstanceId = InstanceId, ClientOrderId = "rt-sell", Symbol = _symbol, Side = BuySell.Sell, Type = PaperOrderType.BrokerLimit, Role = PaperOrderRole.Flatten, Price = 65000m, Quantity = 1 });
        SnapshotChanged?.Invoke(GetSnapshot());
        throw new InvalidOperationException("roundtrip start failed");
    }
    public void OnMarketEvent(MarketEvent marketEvent) { }
    public void OnExecution(ExecutionEvent execution) { }
    public void Pause(string reason) => State = StrategyLifecycleState.Suspended;
    public void Resume() => State = StrategyLifecycleState.Ready;
    public void Stop(string reason) => State = StrategyLifecycleState.Stopped;
    public StrategySnapshot GetSnapshot() => new() { StrategyType = StrategyType, InstanceId = InstanceId, State = State };
}

sealed class BarrierStartStrategy : ISimulationStrategy
{
    private readonly string _symbol;
    private readonly ManualResetEventSlim _entered;
    private readonly ManualResetEventSlim _release;
    public BarrierStartStrategy(string instanceId, string symbol, ManualResetEventSlim entered, ManualResetEventSlim release)
    { InstanceId = instanceId; _symbol = symbol; _entered = entered; _release = release; }
    public string StrategyType => "BarrierStart";
    public string InstanceId { get; }
    public StrategyLifecycleState State { get; private set; } = StrategyLifecycleState.Created;
    public event Action<PaperOrderRequest>? OrderRequested;
    public event Action? CancelAllRequested;
    public event Action<StrategySnapshot>? SnapshotChanged;
    public void Start(StrategyRuntimeContext context) { _entered.Set(); _release.Wait(); State = StrategyLifecycleState.Ready; }
    public void OnMarketEvent(MarketEvent marketEvent) { }
    public void OnExecution(ExecutionEvent execution) { }
    public void Pause(string reason) => State = StrategyLifecycleState.Suspended;
    public void Resume() => State = StrategyLifecycleState.Ready;
    public void Stop(string reason) { CancelAllRequested?.Invoke(); State = StrategyLifecycleState.Stopped; }
    public StrategySnapshot GetSnapshot() => new()
    {
        StrategyType = StrategyType, InstanceId = InstanceId, TradableSymbol = _symbol,
        State = State, UpdatedAt = DateTimeOffset.UtcNow
    };
}

sealed class HostManagedProbeStrategy : ISimulationStrategy, IManagedOrderStrategy
{
    private readonly string _symbol;

    public HostManagedProbeStrategy(string instanceId, string symbol)
    {
        InstanceId = instanceId;
        _symbol = symbol;
    }

    public string StrategyType => "HostManagedProbe";
    public string InstanceId { get; }
    public StrategyLifecycleState State { get; private set; } = StrategyLifecycleState.Created;
    public List<OrderLifecycleEvent> Lifecycle { get; } = new();
    public List<ExecutionEvent> Executions { get; } = new();
    public event Action<PaperOrderRequest>? OrderRequested;
    public event Action? CancelAllRequested;
    public event Action<StrategySnapshot>? SnapshotChanged;
    public event Action<string>? CancelOrderRequested;

    public void Request(PaperOrderRequest request) => OrderRequested?.Invoke(request);
    public void Start(StrategyRuntimeContext context) => State = StrategyLifecycleState.Ready;
    public void OnMarketEvent(MarketEvent marketEvent) { }
    public void OnExecution(ExecutionEvent execution) => Executions.Add(execution);
    public void OnOrderLifecycle(OrderLifecycleEvent orderEvent) => Lifecycle.Add(orderEvent);
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
        TradableSymbol = _symbol,
        State = State,
        Reason = "probe",
        UpdatedAt = DateTimeOffset.UtcNow
    };
}
