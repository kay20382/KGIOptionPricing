# 多策略實盤精確路由、獨立分帳與重啟回補 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 讓六個既有策略及未來策略都能在同帳戶、同商品並行，所有委託與回報依不可變識別精確歸屬，策略部位／損益獨立，且程式重啟後可安全回補並維持人工暫停。

**Architecture:** 所有策略固定走 `StrategyHost → StrategyInstanceRunner → LiveStrategyExecutionFacade → LiveStrategyOrderRouter → OrderManager`。每個帳戶只有一個中央路由器訂閱券商事件；路由器先將 ownership 寫入 append-only journal 並強制落盤，再送券商。`PaperPortfolioLedger` 保存策略虛擬部位，新增的 `AccountPositionLedger` 保存券商商品部位、人工單及對帳差異；兩層帳本不互相覆寫。

**Tech Stack:** C#、.NET 10 Windows、Windows Forms、現有 `StrategyFramework`／`OrderManager`、System.Text.Json JSONL journal、Console smoke tests。

**Spec:** `docs/superpowers/specs/2026-09-23-multi-strategy-live-routing-recovery-design.md`

## Global Constraints

- 必須遵守 `skill-packages/kgi-strategy-development/SKILL.md`、`references/framework-contract.md` 與 `references/integration-checklist.md`；不得建立第二套策略 host、損益計算器或商品規格表。
- 六個既有策略 `MeanReversionScalper`、`WaveletScalper`、`NikkeiMicrostructureAlpha`、`MiniLeadMicro`、`OrderBookTrend`、`PeriodicQueueCycle` 必須使用同一套 managed-order、checkpoint、路由與回補介面；未符合者不得啟用實盤。
- `PaperOrderRequest.Generation` 保留作訂單改價世代；新增 `long StrategyRunGeneration` 表示策略執行世代，不得混用。
- ownership 主鍵固定為 `StrategyInstanceId + StrategyRunGeneration + ClientOrderId`；券商別名包含 `BrokerOrderId`、`OrdNo`、`Cnt`、`OrgCnt`、`RequestId`。不可依商品、成交順序、價格、方向或帳戶淨部位猜測。
- 每個帳戶僅有一個 `LiveStrategyOrderRouter` 訂閱 gateway；個別策略只持有 facade，不得自行訂閱券商、建立 ownership store 或計算另一套損益。
- 送券商前必須 append journal 並 `Flush(true)`；落盤失敗不得送單。不確定券商是否接受的單不得自動重送。
- journal 可忽略最後一筆不完整尾段；中段毀損、重複 ownership、別名衝突或 checkpoint schema 不相容必須隔離，不能靜默修復。
- 成交冪等優先使用 `ExecId`；缺少 `ExecId` 時使用每個 broker order 的累積成交量水位。即時回報與回補重疊只能入帳一次。
- 策略帳本按 `StrategyInstanceId + StrategyRunGeneration + Symbol` 分帳；帳戶帳本只用於券商對帳、整體風控與 `PositionEffect` 判斷。
- 手動／外部單進入 `ManualExternal` bucket；未知或模稜兩可的回報進入 quarantine，絕不可分配給任一策略。
- 重啟先進入 `Recovering` 並阻擋新單；回補及對帳成功後進入 `RecoveredPaused`，仍須使用者明確按「繼續」。
- 關閉、停損、撤單與平倉只能作用於該策略執行世代；不得影響同商品其他策略。帳戶緊急風控是獨立且明確的操作。
- 測試只使用 fake gateway／recovery source，禁止為驗證送出真實委託；保留既有商品白名單、口數上限與實盤安全閘。
- 工作樹已有大量使用者修改；每一 task 只 stage 該 task 明列的檔案，不得清理或覆寫無關內容。

## Review Focus

- write-ahead 已落盤但券商回覆前 crash：重啟不得重送，必須唯一比對或隔離。
- 同商品、相反方向、甚至相同 `ClientOrderId` 的兩個策略：回報、部位、費用與關閉互不混淆。
- 即時／回補重複成交、部分成交、晚到成交與亂序生命週期：只計入一次且回到原策略。
- 中段 journal 毀損、別名衝突、不相容 checkpoint、未知人工單及策略合計與券商淨額不符：阻擋恢復並顯示原因。
- 六個既有策略與未來 factory 都被 capability gate 約束，不可再有 legacy `CancelAll` 或直接 gateway 旁路。

---

## File Structure

- `KGIOptionPricing/StrategyFramework/Recovery/StrategyRecoveryModels.cs`：策略 checkpoint、schema 與恢復狀態。
- `KGIOptionPricing/StrategyFramework/Execution/LiveOrderOwnershipModels.cs`：ownership key、券商別名、journal event、quarantine model。
- `KGIOptionPricing/StrategyFramework/Execution/LiveOrderOwnershipStore.cs`：append-only JSONL、強制落盤、重播與毀損偵測。
- `KGIOptionPricing/StrategyFramework/Execution/LiveStrategyOrderRouter.cs`：單一 gateway 訂閱、精確路由、去重、隔離。
- `KGIOptionPricing/StrategyFramework/Execution/LiveStrategyExecutionFacade.cs`：策略執行世代專屬的送單／改單／撤單入口。
- `KGIOptionPricing/StrategyFramework/Performance/AccountPositionLedger.cs`：帳戶商品帳本、ManualExternal 與對帳差異。
- `KGIOptionPricing/StrategyFramework/Recovery/LiveStrategyRecoveryCoordinator.cs`：啟動回補、checkpoint restore、券商 snapshot 比對。
- `tests/LiveStrategyRecoverySmokeTests/`：中央路由、journal、雙層帳本、六策略 capability 與重啟回補測試。

### Task 1: 建立專用 recovery smoke-test 專案與執行世代模型

**Files:**
- Create: `tests/LiveStrategyRecoverySmokeTests/LiveStrategyRecoverySmokeTests.csproj`
- Create: `tests/LiveStrategyRecoverySmokeTests/Program.cs`
- Modify: `KGIOptionPricing/StrategyFramework/StrategyModels.cs`
- Modify: `KGIOptionPricing/StrategyFramework/StrategyInterfaces.cs`
- Modify: `KGIOptionPricing/StrategyFramework/StrategyHost.cs`
- Create: `KGIOptionPricing/StrategyFramework/Recovery/StrategyRecoveryModels.cs`

**Interfaces:**

```csharp
public interface IStrategyRecoveryParticipant
{
    string RecoverySchema { get; }
    StrategyRecoveryCheckpoint CaptureRecoveryCheckpoint();
    void RestoreRecoveryCheckpoint(StrategyRecoveryCheckpoint checkpoint);
}

public sealed record StrategyRecoveryCheckpoint(
    string StrategyType,
    string StrategyInstanceId,
    long StrategyRunGeneration,
    string Schema,
    DateTimeOffset CapturedAt,
    string PayloadJson);
```

- [ ] **Step 1: 寫 failing test**：在新 `Program.cs` 建立 request/order/execution/lifecycle round-trip，斷言四者的 `StrategyRunGeneration == 17`，且既有 `Generation == 3` 不被覆寫；另建立 fake recovery participant，驗證 schema 不同時 restore 被拒絕。
- [ ] **Step 2: 確認 RED**：執行 `dotnet run --project tests/LiveStrategyRecoverySmokeTests/LiveStrategyRecoverySmokeTests.csproj -c Release`，預期因 `StrategyRunGeneration`、checkpoint 與 recovery interface 不存在而編譯失敗。
- [ ] **Step 3: 最小實作**：將 `long StrategyRunGeneration` 加到 `PaperOrderRequest`、`PaperOrder`、`ExecutionEvent`、`OrderLifecycleEvent`；在 runner 建立不可重用的 run generation，對策略送出的 request 補上 instance/run identity，若策略覆寫成其他 instance 或 generation 則拒絕並記錄 lifecycle fault。加入 checkpoint model 與 schema 驗證 helper。
- [ ] **Step 4: 確認 GREEN**：重跑新專案，預期所有 identity／schema tests PASS。
- [ ] **Step 5: Commit**：stage 本 task 檔案，commit `feat: define strategy run recovery identity`。

### Task 2: 實作不可變 ownership journal

**Files:**
- Create: `KGIOptionPricing/StrategyFramework/Execution/LiveOrderOwnershipModels.cs`
- Create: `KGIOptionPricing/StrategyFramework/Execution/LiveOrderOwnershipStore.cs`
- Modify: `tests/LiveStrategyRecoverySmokeTests/Program.cs`

**Interfaces:**

```csharp
public sealed record LiveOrderOwnerKey(string StrategyInstanceId, long StrategyRunGeneration, string ClientOrderId);

public interface ILiveOrderOwnershipStore : IDisposable
{
    LiveOrderOwnership Prepare(LiveOrderOwnership ownership);
    void BindAlias(LiveOrderOwnerKey owner, BrokerOrderAlias alias);
    FillAcceptance TryAcceptFill(LiveOrderOwnerKey owner, string? execId, int cumulativeQuantity);
    void AppendCheckpoint(StrategyRecoveryCheckpoint checkpoint);
    LiveOwnershipSnapshot Replay();
}
```

- [ ] **Step 1: 寫 failing tests**：涵蓋同一 `ClientOrderId` 但不同 instance 可共存、完全相同 owner key 不可重建、同一 broker alias 不可綁兩個 owner、Prepare 跨 process 重播、末尾截斷 JSON 可忽略、中段 JSON 毀損必須回傳 quarantined、`ExecId` 重複及累積量不增加不得再次接受。
- [ ] **Step 2: 確認 RED**：執行 recovery tests，預期因 ownership models/store 不存在而編譯失敗。
- [ ] **Step 3: 最小實作**：journal 每行為含 `Sequence`、`EventType`、`OccurredAt`、payload 與 checksum 的 JSON；以帳戶專屬 lock 序列化 append，寫入 UTF-8、換行後呼叫 `FileStream.Flush(true)`。Replay 驗證 sequence/checksum/不可變欄位；只允許忽略 EOF 的不完整最後一行。建立 owner、alias、exec-id、fill-watermark、checkpoint 索引。
- [ ] **Step 4: 確認 GREEN**：重跑 recovery tests，預期 durability、corruption、uniqueness、dedupe tests PASS。
- [ ] **Step 5: Commit**：stage 本 task 檔案，commit `feat: persist immutable live order ownership`。

### Task 3: 建立帳戶單一中央路由器與策略 facade

**Files:**
- Create: `KGIOptionPricing/StrategyFramework/Execution/LiveStrategyOrderRouter.cs`
- Create: `KGIOptionPricing/StrategyFramework/Execution/LiveStrategyExecutionFacade.cs`
- Modify: `KGIOptionPricing/StrategyFramework/Execution/LiveStrategyOrderMapper.cs`
- Modify: `KGIOptionPricing/StrategyFramework/Execution/KgiLiveStrategyExecutionService.cs`
- Modify: `KGIOptionPricing/OrderManagement/Core/OrderInfo.cs`
- Modify: `KGIOptionPricing/OrderManagement/Core/Requests.cs`
- Modify: `KGIOptionPricing/OrderManagement/Core/OrderManager.cs`
- Modify: `tests/LiveStrategyRecoverySmokeTests/Program.cs`
- Modify: `tests/OrderManagementSmokeTests/Program.cs`

**Interfaces:**

```csharp
public interface ILiveStrategyOrderRouter
{
    ILiveStrategyExecutionService Register(StrategyExecutionRegistration registration);
    RouteSubmissionResult Submit(PaperOrderRequest request);
    bool Cancel(LiveOrderOwnerKey owner, out string reason);
    IReadOnlyList<QuarantinedBrokerEvent> GetQuarantine();
}
```

- [ ] **Step 1: 寫 failing tests**：fake gateway 計算 event subscriber 數，兩個 facade 註冊後仍各只有一個中央 subscriber；兩個策略同商品且相同 client id，各自綁不同 broker id，成交只能送到 owner；零候選與多候選 alias 都隔離；Prepare 寫入失敗時 gateway `PlaceOrderCalls == 0`；gateway timeout 時 owner 標成 `SubmissionUnknown` 且相同 key 重送被拒絕。
- [ ] **Step 2: 確認 RED**：執行 recovery tests，預期 router/facade 不存在，且既有服務仍各自訂閱 gateway。
- [ ] **Step 3: 最小實作**：router 建構時唯一訂閱 `OrderStateChanged`、`OrderFilled`、`OrderFailed`、`OrderCancelFailed`；所有 callback 進入同一序列化 queue。`Submit` 先驗證完整 identity、journal Prepare，再由 mapper 建立含 strategy metadata 的 `OrderRequest`，送單結果回寫 alias。`OrderManager.PlaceOrder` 將 metadata 原樣複製到 `OrderInfo`。將現有 `KgiLiveStrategyExecutionService` 收斂為相容 adapter 或移除其 gateway subscription，內部委派 facade。
- [ ] **Step 4: 確認 GREEN**：執行 Recovery 與 OrderManagement tests，預期 subscriber、精確路由、未知回報、write-ahead 與既有 live tests 全 PASS。
- [ ] **Step 5: Commit**：只 stage 上列 router、facade、mapper、order metadata 及兩個 test files，commit `feat: centralize live strategy order routing`。

### Task 4: 建立策略虛擬帳本冪等性與帳戶商品帳本

**Files:**
- Modify: `KGIOptionPricing/StrategyFramework/Performance/PaperPortfolioLedger.cs`
- Create: `KGIOptionPricing/StrategyFramework/Performance/AccountPositionLedger.cs`
- Modify: `KGIOptionPricing/StrategyFramework/Execution/LiveStrategyOrderMapper.cs`
- Modify: `tests/LiveStrategyRecoverySmokeTests/Program.cs`
- Modify: `tests/StrategyFrameworkSmokeTests/Program.cs`

**Interfaces:**

```csharp
public sealed record StrategyLedgerKey(string StrategyInstanceId, long StrategyRunGeneration, string Symbol);
public sealed record AccountProductSnapshot(string Account, string Symbol, int BrokerNetQuantity,
    int StrategyNetQuantity, int ManualExternalNetQuantity, int DifferenceQuantity, decimal MarkPrice);

public interface IAccountPositionLedger
{
    void ReplaceBrokerSnapshot(string account, IReadOnlyList<BrokerPositionRow> positions);
    void ApplyManualExternalFill(AccountFill fill);
    AccountProductSnapshot Reconcile(string account, string symbol, IReadOnlyList<PositionSnapshot> strategies);
}
```

- [ ] **Step 1: 寫 failing tests**：A 多 1、B 空 1 時兩個 strategy positions 保留而 account strategy net 為 0；重播相同 execution key 不得重複部位／費用；關閉 A 只將 A 歸零、B 仍為 -1；manual buy 1 進入 ManualExternal 後 difference 為 0；無法識別來源的差額不得硬塞給策略。測試 mapper 依 account ledger 選擇券商 `PositionEffect`，但策略 ownership/role 不變。
- [ ] **Step 2: 確認 RED**：執行 Recovery 與 StrategyFramework tests，預期 generation-aware key、idempotency/account ledger 不存在。
- [ ] **Step 3: 最小實作**：`PaperPortfolioLedger` key 加入 run generation，保存 processed execution ids／水位並提供 capture/restore；`AccountPositionLedger` 僅聚合券商 snapshot、策略合計與 ManualExternal。新增 `AccountPositionEffectResolver`，只決定券商 Open/Close/Auto，不參與策略歸屬。
- [ ] **Step 4: 確認 GREEN**：重跑兩個 test projects，預期雙層帳本、費用、position effect tests PASS，既有 225MC 每點 ¥10／每口每次 ¥36 tests 不退步。
- [ ] **Step 5: Commit**：stage 本 task 檔案，commit `feat: separate strategy and account position ledgers`。

### Task 5: 提供六策略共用 managed-order 與 checkpoint 元件

**Files:**
- Create: `KGIOptionPricing/StrategyFramework/ManagedStrategyOrderBook.cs`
- Modify: `KGIOptionPricing/StrategyFramework/StrategyInterfaces.cs`
- Modify: `KGIOptionPricing/StrategyFramework/StrategyHost.cs`
- Modify: `tests/LiveStrategyRecoverySmokeTests/Program.cs`

**Interfaces:**

```csharp
public sealed class ManagedStrategyOrderBook
{
    public string NextClientOrderId(string role, string cycleId);
    public void Track(PaperOrderRequest request);
    public void Apply(OrderLifecycleEvent lifecycle);
    public IReadOnlyList<string> ActiveClientOrderIds { get; }
    public ManagedOrderBookCheckpoint Capture();
    public void Restore(ManagedOrderBookCheckpoint checkpoint);
}
```

- [ ] **Step 1: 寫 failing tests**：兩個 strategy instances 產生的 ID 不相撞；同 instance 不同 run generation 不共用 active orders；cancel 必須帶精確 client id；晚到 fill 在 cancel ack 後仍能找到原 order；capture/restore 後 active orders、cycle、order generation 一致。
- [ ] **Step 2: 確認 RED**：執行 recovery tests，預期 helper 不存在。
- [ ] **Step 3: 最小實作**：提供所有策略共用的 client-id 生成、active-order 索引、lifecycle state machine 與 JSON checkpoint DTO；runner 在 live registration 時強制策略同時實作 `IManagedOrderStrategy` 與 `IStrategyRecoveryParticipant`，模擬／重播仍走相同 lifecycle，但不需要 broker router。
- [ ] **Step 4: 確認 GREEN**：helper tests PASS；fake legacy strategy 在 simulation 可建立、在 live capability validation 必須被拒絕並說明缺少介面。
- [ ] **Step 5: Commit**：stage 本 task 檔案，commit `feat: standardize managed strategy lifecycle`。

### Task 6: 遷移 MR、MODWT 與 Nikkei Alpha

**Files:**
- Modify: `KGIOptionPricing/Strategies/MeanReversionScalper/MeanReversionScalperStrategy.cs`
- Modify: `KGIOptionPricing/Strategies/WaveletScalper/WaveletScalperStrategy.cs`
- Modify: `KGIOptionPricing/Strategies/NikkeiMicrostructureAlpha/NikkeiMicrostructureAlphaStrategy.cs`
- Modify: `tests/StrategyFrameworkSmokeTests/Program.cs`
- Modify: `tests/NikkeiMicrostructureAlphaSmokeTests/Program.cs`
- Modify: `tests/LiveStrategyRecoverySmokeTests/Program.cs`

- [ ] **Step 1: 寫 failing parameterized test**：從三個 factory 各建立策略，斷言實作兩個 required interfaces；觸發 entry 後 request 含非空 `ClientOrderId`／`CycleId`／`Role`，lifecycle 回報只改對應 order；`StopAsync` 逐筆 cancel active ids，不呼叫無範圍 legacy `CancelAll`；checkpoint round-trip 保存部位保護狀態、冷卻／訊號窗口與 working orders。
- [ ] **Step 2: 確認 RED**：執行三個相關 test projects，預期 legacy strategies 缺介面／identity。
- [ ] **Step 3: 最小實作**：三策略各持有 `ManagedStrategyOrderBook`，所有送單指定 stable cycle/role/client id；實作 `OnOrderLifecycle`；checkpoint payload 使用各自具名 versioned DTO，包含恢復後避免重複進場所需的訊號／冷卻／停損狀態。不得改變訊號公式或參數預設值。
- [ ] **Step 4: 確認 GREEN**：執行 StrategyFramework、NikkeiAlpha、Recovery tests，預期三策略 capability、exact cancel、checkpoint tests PASS。
- [ ] **Step 5: Commit**：stage 三個策略及三個 tests，commit `feat: migrate mr modwt and nikkei strategies to managed routing`。

### Task 7: 遷移 Mini Lead、Order Book Trend 與 PQC

**Files:**
- Modify: `KGIOptionPricing/Strategies/MiniLeadMicro/MiniLeadMicroStrategy.cs`
- Modify: `KGIOptionPricing/Strategies/OrderBookTrend/OrderBookTrendStrategy.cs`
- Modify: `KGIOptionPricing/Strategies/PeriodicQueueCycle/PeriodicQueueCycleStrategy.cs`
- Modify: `KGIOptionPricing/Strategies/PeriodicQueueCycle/QueueCycleOrderCoordinator.cs`
- Modify: `tests/MiniLeadMicroSmokeTests/Program.cs`
- Modify: `tests/OrderBookSmokeTests/Program.cs`
- Modify: `tests/PeriodicQueueCycleSmokeTests/Program.cs`
- Modify: `tests/LiveStrategyRecoverySmokeTests/Program.cs`

- [ ] **Step 1: 寫 failing parameterized test**：三 factory 均滿足 live capabilities；Mini/OBT 不再依 legacy CancelAll；PQC 保留既有完成單改價 `Generation`，同時另帶 `StrategyRunGeneration`；三者 checkpoint round-trip 後不重複掛單，且 stop-loss 的撤單／對手價平倉仍只作用於原 instance。
- [ ] **Step 2: 確認 RED**：執行四個 test projects，預期 Mini/OBT 缺介面，PQC 缺 recovery checkpoint/run generation 分離。
- [ ] **Step 3: 最小實作**：Mini/OBT 使用共用 managed order book 並加入 versioned DTO；PQC 讓 coordinator 的訂單改價世代保留 `int Generation`，runner stamps `long StrategyRunGeneration`，checkpoint 保存方向、cycle、兩邊完成單、單邊成交計時、冷卻及停損階段。不得改變順勢 100/200/100、逆勢 600/700/600 或既有動態移動規則。
- [ ] **Step 4: 確認 GREEN**：執行 Mini、OBT、PQC、Recovery tests，預期 managed lifecycle、checkpoint 與既有策略行為全部 PASS。
- [ ] **Step 5: Commit**：stage 上列檔案，commit `feat: migrate mini orderbook and pqc to managed routing`。

### Task 8: 實作重啟回補 coordinator 並接入 LiveStrategyService

**Files:**
- Create: `KGIOptionPricing/StrategyFramework/Recovery/LiveStrategyRecoveryCoordinator.cs`
- Modify: `KGIOptionPricing/StrategyFramework/LiveStrategyService.cs`
- Modify: `KGIOptionPricing/StrategyFramework/Execution/LiveStrategyOrderRouter.cs`
- Modify: `KGIOptionPricing/KGITradeService.cs`
- Modify: `tests/LiveStrategyRecoverySmokeTests/Program.cs`
- Modify: `tests/OrderManagementSmokeTests/Program.cs`

**Interfaces:**

```csharp
public interface ILiveBrokerRecoverySource
{
    BrokerRecoverySnapshot Capture(string account);
}

public enum LiveRecoveryState { Recovering, RecoveredPaused, ReconciliationRequired, Quarantined }

public sealed record BrokerRecoverySnapshot(
    IReadOnlyList<OrderInfo> Orders,
    IReadOnlyList<BrokerExecutionRow> Executions,
    IReadOnlyList<BrokerPositionRow> Positions,
    DateTimeOffset CapturedAt);
```

- [ ] **Step 1: 寫 failing tests**：涵蓋 (a) write-ahead 後 crash、券商有唯一訂單，回補綁定且不重送；(b) 即時 fill 與 recovery fill 重疊只入帳一次；(c) 晚到 fill 回原 instance；(d) 未知券商單進 ManualExternal 後可對帳，無法判定來源則隔離；(e) strategy sum 與 broker position 不符進 `ReconciliationRequired`；(f) incompatible checkpoint／journal middle corruption 進 `Quarantined`；(g) 成功後所有策略 `RecoveredPaused`，只有明確 `Resume(instanceId)` 才能交易。
- [ ] **Step 2: 確認 RED**：執行 recovery tests，預期 coordinator 與 service state gate 不存在。
- [ ] **Step 3: 最小實作**：service 啟動時先 replay journal/checkpoints，再由 `KGITradeService` adapter 取得當日訂單、成交與持倉 snapshot；coordinator 依 alias 唯一配對、套用 dedupe、restore 六策略 checkpoint、重建雙層帳本並逐商品對帳。`Add`、`Resume`、`Submit` 都查 recovery/product gate；成功僅轉 `RecoveredPaused`。停止策略只撤／平該 instance；另保留明確 account emergency action。
- [ ] **Step 4: 確認 GREEN**：執行 Recovery 與 OrderManagement tests，預期七組 recovery scenarios 與既有 safe-close/late-fill tests PASS。
- [ ] **Step 5: Commit**：stage 本 task 檔案，commit `feat: recover live strategies without automatic resume`。

### Task 9: 加入 UI 可觀測性、能力門檻與完整驗收

**Files:**
- Modify: `KGIOptionPricing/LiveStrategyCenterForm.cs`
- Modify: `KGIOptionPricing/StrategyFramework/LiveStrategyService.cs`
- Modify: `KGIOptionPricing/StrategyFramework/Storage/StrategyObservationModels.cs`
- Modify: `KGIOptionPricing/StrategyFramework/Storage/SimulationRunRecorder.cs`
- Modify: `tests/LiveStrategyRecoverySmokeTests/Program.cs`
- Modify: `tests/StrategyFrameworkSmokeTests/Program.cs`

- [ ] **Step 1: 寫 failing tests**：列舉 `LiveStrategyService.DefaultFactories` 六項，逐一驗證 live capability gate；snapshot/observation 必須包含 instance、run generation、client/broker id、recovery state、account net、strategy net、manual external、difference、quarantine reason；manual row 不得顯示成策略成交。
- [ ] **Step 2: 確認 RED**：執行 Recovery 與 StrategyFramework tests，預期 observation/UI source 欄位不完整。
- [ ] **Step 3: 最小實作**：實盤狀態表增加 `執行世代`、`回補狀態`、`策略部位`、`帳戶淨部位`、`人工／外部`、`差異`、`隔離原因`；成交表增加 `ClientOrderId`、`BrokerOrderId`、`角色`、`回補`。新增對帳／隔離頁籤；`Recovering`、`ReconciliationRequired`、`Quarantined` 禁用啟動／繼續並顯示確切原因。欄寬固定，不使用 `AutoSize`。
- [ ] **Step 4: 執行完整驗收**：依序執行 `LiveStrategyRecoverySmokeTests`、`OrderManagementSmokeTests`、`StrategyFrameworkSmokeTests`、`NikkeiMicrostructureAlphaSmokeTests`、`MiniLeadMicroSmokeTests`、`OrderBookSmokeTests`、`PeriodicQueueCycleSmokeTests`，最後執行 `dotnet build KGIOptionPricing.sln -c Release --no-restore`。預期全部 PASS／成功，且 fake gateway 證明未送真實委託。
- [ ] **Step 5: 人工驗收清單**：在模擬 gateway 啟動兩個同商品反向策略，確認各一列、各自部位／損益；關閉 A 不影響 B；模擬重啟後顯示 `RecoveredPaused` 且不自動下單；注入 unknown fill 時只在隔離頁顯示。
- [ ] **Step 6: Commit**：stage 本 task 檔案，commit `feat: expose strategy recovery and reconciliation state`。

## Plan Self-Review

### Spec coverage

- 所有六策略及未來策略的一致 capability gate：Tasks 5–9。
- 單一中央路由、完整三段識別、不可猜測歸屬：Tasks 1–3。
- append-only write-ahead、強制落盤、尾段／中段毀損語意：Task 2。
- 策略虛擬帳本、帳戶商品帳本、ManualExternal 與 PositionEffect：Task 4。
- late fill、cancel failure、改單、斷線及重啟回補：Tasks 3、5、8。
- checkpoint versioning、回補後保持暫停、人工 Resume：Tasks 1、5–8。
- UI、quarantine、reconciliation 與 full regression：Task 9。

### Placeholder scan

本計畫沒有 `TODO`、`TBD`、「同上」或留待實作者自行決定的核心介面。各 task 均列出檔案、RED、最小實作、GREEN 與 commit 邊界。

### Type consistency

- `Generation` 固定為 `int` 訂單改價世代；`StrategyRunGeneration` 固定為 `long` 策略執行世代。
- `LiveOrderOwnerKey` 在 Task 2 首次定義，Task 3 之後一致使用。
- `StrategyRecoveryCheckpoint`／`IStrategyRecoveryParticipant` 在 Task 1 定義；六策略於 Tasks 6–7 實作。
- `AccountProductSnapshot`／`IAccountPositionLedger` 在 Task 4 定義；recovery 與 UI 只讀同一模型。
- `LiveRecoveryState`／`ILiveBrokerRecoverySource` 在 Task 8 定義，UI 於 Task 9 消費。

### Review-focus coverage

- crash window 與不重送：Tasks 2、3、8。
- 同商品反向策略及 client-id 碰撞隔離：Tasks 2–4、8。
- 重複／部分／亂序／晚到成交：Tasks 2–4、8。
- 毀損、衝突、未知單、checkpoint schema、淨額差異：Tasks 2、4、8、9。
- 六策略無旁路與未來 factory gate：Tasks 5–9。
