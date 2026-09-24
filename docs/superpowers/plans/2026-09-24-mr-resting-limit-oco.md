# MR 實盤限價括號單與停損 OCO Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 將 MR 實盤進場與停利改成交易所限價單，並以可持久化的週期 OCO 協調撤單、成交結算及一次性停損市價平倉。

**Architecture:** 在既有 `StrategyHost → LiveStrategyOrderRouter → KgiLiveStrategyExecutionService` 執行路徑增加共用 OCO 狀態／協調元件，OCO 以策略實例、執行世代及週期識別。MR 提供凍結限價及本機停損觸發意圖；共用執行層精確撤銷互斥委託、等待券商終態成交水位對平，再以策略帳本剩餘部位決定是否送出一次停損市價單。模擬與重播不連線券商，使用相同委託角色與週期語意。

**Tech Stack:** C#、.NET 10 Windows、現有 `StrategyFramework`、`OrderManager`、System.Text.Json JSONL ownership journal、Console smoke tests。

**Spec:** `docs/superpowers/specs/2026-09-24-mr-resting-limit-oco-design.md`

## Global Constraints

- 本設計只改變 MR 的實盤委託執行方式，不修改訊號、價位公式、`F`／`K`／`H` 等參數預設值、每筆一口風控、商品白名單或費用計算。
- 不假設或依賴券商支援原生 OCO；實作以共用路由器協調的本機 OCO 為準。
- 不改 Wavelet、Nikkei Alpha、Mini Lead、Order Book Trend、PQC 或其他策略的委託意圖。
- 所有測試使用 fake gateway 或 paper execution adapter；不得送出真實委託。
- `Cancelled` 生命週期不能抹除可能晚到的成交映射。
- 券商未提供可用終態累積量、終態水位未與已交付成交量對平，或仍有未結算成交時，不可送停損市價單。
- 重啟時禁止自動續送或重送停損市價單；不完整／不確定狀態必須維持 `Quarantined`／`ReconciliationRequired`。
- 工作樹已有大量使用者修改；每個 task 只 stage 該 task 明列的檔案，不清理或覆寫無關內容。

## Review Focus

- 停利撤單終態回報比成交回報先到，且最終累積量大於已入帳量時，不能先送 stop-market；Task 4 加終態水位未對平測試。
- 停利撤單途中全成或部分成交時，stop-market 不可重複或超過策略剩餘部位；Task 4 加完整／部分晚到成交測試。
- 兩側進場一側成交、對側在撤單生效前晚到成交時，兩筆仍須正確入帳且不可建立兩套保護括號；Task 3 加 entry-race 測試。
- 券商終態未提供最終累積成交量或停損委託送單結果不明時，不可以逾時推測並重送；Task 4 加 fail-closed 測試。
- 程式重啟在 `StopPending` 與 stop-market write-ahead 之間發生時，不自動重送，並顯示可操作的恢復狀態；Task 5 加 journal replay 測試。

---

## File Structure

- `KGIOptionPricing/StrategyFramework/StrategyModels.cs`：在 broker 終態生命週期明確標示最終成交水位是否權威可用。
- `KGIOptionPricing/StrategyFramework/Execution/LiveOrderOwnershipModels.cs`：定義週期 OCO key、階段與持久狀態。
- `KGIOptionPricing/StrategyFramework/Execution/LiveOrderOwnershipStore.cs`：以 append-only JSONL 保存／重播 OCO 狀態轉換。
- `KGIOptionPricing/StrategyFramework/Execution/LiveStrategyOrderMapper.cs`：擴充 routed gateway 的 OCO persistence/read API；維持現有 BrokerLimit mapping。
- `KGIOptionPricing/StrategyFramework/Execution/ManagedStrategyOcoCoordinator.cs`：實作確定性的週期 OCO phase transition 與單次動作決策，不直接呼叫券商。
- `KGIOptionPricing/StrategyFramework/Execution/KgiLiveStrategyExecutionService.cs`：套用 OCO 決策、送精確撤單、等待終態水位及提交唯一 stop-market。
- `KGIOptionPricing/Strategies/MeanReversionScalper/MeanReversionScalperStrategy.cs`：用 `BrokerLimit` 發出 MR 進場／停利，entry 成交立即建立本機停損保護，並等待共用 OCO lifecycle 後才建停利單。
- `KGIOptionPricing/StrategyFramework/StrategyModels.cs`、`StrategyFramework/Storage/StrategyObservationModels.cs`、`StrategyFramework/Storage/SimulationRunRecorder.cs`：保存 OCO 可觀測階段、終態水位及動作原因。
- `KGIOptionPricing/LiveStrategyCenterForm.cs`：實盤 MR 狀態列顯示 OCO phase、待撤訂單／原因及停損市價提交狀態，維持固定欄寬。
- `KGIOptionPricing/StrategyFramework/Execution/CrossPricePaperExecutionService.cs`：讓模擬 BrokerLimit 與 MR 週期 OCO 沿用嚴格穿價規則。
- `tests/LiveStrategyRecoverySmokeTests/Program.cs`：測試 OCO ownership state 的 append/replay 與重啟 fail-closed。
- `tests/OrderManagementSmokeTests/Program.cs`：使用 fake gateway 驗證實盤 OCO cancel/fill/watermark/stop-market 次序。
- `tests/StrategyFrameworkSmokeTests/Program.cs`：驗證 MR 凍結限價、停損優先保護及模擬成交。

## Task 1: 持久化 OCO identity 與 phase

**Files:**
- Modify: `KGIOptionPricing/StrategyFramework/StrategyModels.cs`
- Modify: `KGIOptionPricing/StrategyFramework/Execution/LiveOrderOwnershipModels.cs`
- Modify: `KGIOptionPricing/StrategyFramework/Execution/LiveOrderOwnershipStore.cs`
- Test: `tests/LiveStrategyRecoverySmokeTests/Program.cs`

**Interfaces:**

```csharp
public sealed record LiveStrategyOcoKey(
    string StrategyInstanceId,
    long StrategyRunGeneration,
    string CycleId);

public enum LiveStrategyOcoPhase
{
    EntryWorking, EntryCancelPending, PositionProtected, StopPending,
    StopSubmitted, Completed, ReconciliationRequired, Quarantined
}

public sealed record LiveStrategyOcoGroupState
{
    public LiveStrategyOcoKey Key { get; init; } = new(string.Empty, 0, string.Empty);
    public LiveStrategyOcoPhase Phase { get; init; }
    public string EntryLongClientOrderId { get; init; } = string.Empty;
    public string EntryShortClientOrderId { get; init; } = string.Empty;
    public string TakeProfitClientOrderId { get; init; } = string.Empty;
    public string StopClientOrderId { get; init; } = string.Empty;
    public string PendingCancelClientOrderId { get; init; } = string.Empty;
    public string StopMarketClientOrderId { get; init; } = string.Empty;
    public long TriggerMarketSequence { get; init; }
    public int? BrokerFinalCumulativeFilledQuantity { get; init; }
    public int DeliveredFilledQuantity { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public string Reason { get; init; } = string.Empty;
}

// Add to OrderLifecycleEvent:
public bool HasFinalCumulativeFillWatermark { get; init; }

void ILiveOrderOwnershipStore.AppendOcoState(LiveStrategyOcoGroupState state);
// Add to LiveOwnershipSnapshot:
public IReadOnlyList<LiveStrategyOcoGroupState> OcoGroups { get; init; }
    = Array.Empty<LiveStrategyOcoGroupState>();
```

- [x] **Step 1: 寫 failing model/journal assertions**：序列化／反序列化時保留 `HasFinalCumulativeFillWatermark` 的 true 與 false；追加 `EntryCancelPending`、`StopPending`、`StopSubmitted` 三種同一 OCO key 的狀態，重開 store 後逐 key 斷言只呈現最新 phase、所有 client id、trigger sequence 與成交 watermark；不同 instance／generation／cycle 必須分開保存。
- [x] **Step 2: 執行 RED**：執行 `dotnet run --project tests/LiveStrategyRecoverySmokeTests/LiveStrategyRecoverySmokeTests.csproj -c Release --no-restore`。Expected：編譯失敗，因 OCO key/state、append API 與 replay collection 尚不存在。
- [x] **Step 3: 最小實作**：新增上述三個 OCO 模型，並在 `OrderLifecycleEvent` 加明確的 `HasFinalCumulativeFillWatermark` 欄位；在 journal 加入 `OcoState` append/replay case，以完整 OCO key 做最新狀態索引；驗證 key 欄位、phase、時間與非負成交水位，不覆寫既有事件紀錄。
- [x] **Step 4: 執行 GREEN**：重跑同一 Recovery smoke command。Expected：OCO append/replay assertions 及既有 journal corruption、route alias、fill dedupe 測試全 PASS。
- [x] **Step 5: Commit**：只提交本 task 列出的模型、store 與 test 檔，訊息 `feat: persist strategy OCO lifecycle state`。

## Task 2: 將 MR 進場改為 BrokerLimit 並先保護持倉

**Files:**
- Modify: `KGIOptionPricing/Strategies/MeanReversionScalper/MeanReversionScalperStrategy.cs`
- Modify: `KGIOptionPricing/StrategyFramework/StrategyModels.cs` (add explicit opt-in `OcoGroupId`; empty default preserves existing strategy semantics)
- Modify: `KGIOptionPricing/StrategyFramework/Execution/CrossPricePaperExecutionService.cs` (minimum same-cycle MR sibling cancel and paper terminal watermark support moved forward from Task 6)
- Test: `tests/StrategyFrameworkSmokeTests/Program.cs`

**Interfaces:**
- Consumes: Task 1 `OrderLifecycleEvent.HasFinalCumulativeFillWatermark`；策略繼續用既有 `PaperOrderRequest`、`IStrategyPerformanceView` 與自己的 versioned checkpoint。
- Produces: MR 每個新週期發出的 `EntryLong`、`EntryShort` 皆為 `BrokerLimit`；第一次 entry fill 後立即發出對應本機 `Stop` 保護並暫不發 TakeProfit，由共用 OCO 執行層撤銷同 cycle 對側；對側終態帶 `HasFinalCumulativeFillWatermark == true` 且累積量對平後才發 `TakeProfit BrokerLimit`。

- [x] **Step 1: 寫 failing MR request assertions**：在現有 MR smoke section 加 `Assert`，驗證雙邊進場 `Type == BrokerLimit`、CycleId 相同、client id 不同、價格與 frozen `BuyEntry`／`SellEntry` 一致；模擬 first fill 後立刻有固定 `StopLoss/Stop`，策略不自行送 TakeProfit；送入對側 `Cancelled`、`HasFinalCumulativeFillWatermark == true` 且 `FilledQuantity == 0` 的終態後，才有對應凍結價的 `TakeProfit/BrokerLimit`。
- [x] **Step 2: 執行 RED**：執行 `dotnet run --project tests/StrategyFrameworkSmokeTests/StrategyFrameworkSmokeTests.csproj -c Release --no-restore -- --skip-windows-ui-regression`。Expected：目前進場 request type 為 `Limit`，第一筆成交會立即建立停利而非先只建立 stop protection，新增 assertions 失敗。
- [x] **Step 3: 最小實作**：MR entry fill 時保存已成交方向並先安裝 StopLoss，不由策略重複提出 sibling cancel；透過目前 `OnOrderLifecycle` event 與 `_orderBook.Apply` 等候對側 entry 終態，且只在 `HasFinalCumulativeFillWatermark` 為 true、水位對平、策略仍持倉且 `_takeProfitSubmitted == false` 時建立一次 TakeProfit BrokerLimit。將成交方向及 `_takeProfitSubmitted` 加入版本化 checkpoint。若水位未知、策略部位已歸零或方向變成相反，維持受控／暫停、不開新 cycle、不重複建立 bracket。
- [x] **Step 4: 執行 GREEN**：重跑 `dotnet run --project tests/StrategyFrameworkSmokeTests/StrategyFrameworkSmokeTests.csproj -c Release --no-restore -- --skip-windows-ui-regression`。Expected：MR frozen entry、stop-side validity、entry request type、TP deferred ordering 與原有 MR PnL assertions 全 PASS；WinForms STA-only check 另外需在互動式桌面執行。
- [x] **Step 5: Commit**：只提交 MR strategy 與 StrategyFramework test，訊息 `feat: use resting limit entries in mean reversion strategy`。

## Task 3: 共用實盤 OCO 協調與精確撤單

**Files:**
- Create: `KGIOptionPricing/StrategyFramework/Execution/ManagedStrategyOcoCoordinator.cs`
- Modify: `KGIOptionPricing/StrategyFramework/Execution/LiveStrategyOrderMapper.cs`
- Modify: `KGIOptionPricing/StrategyFramework/Execution/LiveStrategyOrderRouter.cs`
- Modify: `KGIOptionPricing/StrategyFramework/Execution/KgiLiveStrategyExecutionService.cs`
- Test: `tests/OrderManagementSmokeTests/Program.cs`

**Interfaces:**
- Consumes: Task 1 store OCO append/replay; Task 2 MR `BrokerLimit` entry/TP 與本機 stop request。
- Produces: coordinator 與 gateway 採以下最小契約；協調器為純狀態轉換器，不直接呼叫券商，所有決策均帶完整 OCO key。

```csharp
public enum ManagedStrategyOcoEventKind
{
    EntrySubmitted, EntryFill, EntryTerminal, TakeProfitSubmitted,
    StopTriggered, CancelAccepted, CancelRejected, StopMarketResult
}

public sealed record ManagedStrategyOcoEvent(
    LiveStrategyOcoKey Key,
    ManagedStrategyOcoEventKind Kind,
    string ClientOrderId,
    string BrokerOrderId,
    PaperOrderRole Role,
    int DeliveredFilledQuantity,
    int? BrokerFinalCumulativeFilledQuantity,
    bool HasFinalCumulativeFillWatermark,
    long TriggerMarketSequence,
    DateTimeOffset EventTime);

public enum ManagedStrategyOcoActionKind
{
    PersistState, CancelBrokerOrder, SubmitStopMarket, NoAction
}

public sealed record ManagedStrategyOcoAction(
    ManagedStrategyOcoActionKind Kind,
    LiveStrategyOcoGroupState State,
    string ClientOrderId,
    int Quantity,
    string Reason);

public IReadOnlyList<ManagedStrategyOcoAction> Apply(ManagedStrategyOcoEvent input);
```

`ILiveStrategyOrderGateway.AppendOcoState(LiveStrategyOcoGroupState state)` 與 `GetOcoStates(string strategyInstanceId, long strategyRunGeneration)` 只允許 owner scope 內讀寫；`RoutedGateway` 必須驗證 owner。`KgiLiveStrategyExecutionService` 只在券商終態生命週期明確提供權威累積量時設定 `HasFinalCumulativeFillWatermark = true`；缺值不得推定為 0。

- [x] **Step 1: 寫 failing fake-gateway assertions**：新增 entry cancel、成交先到/對側後到競態、停損等待停利終態、停損先於 TP 時等待 entry 撤單、以及 routed owner scope assertions。
- [x] **Step 2: 執行 RED**：OrderManagement smoke 在新增協調器前因缺少 `ManagedStrategyOcoCoordinator` 而編譯失敗。
- [x] **Step 3: 最小實作**：新增共用 OCO phase coordinator；在 BrokerLimit 成交／本機 Stop trigger 路徑使用 `(StrategyInstanceId, StrategyRunGeneration, CycleId)` 尋找互斥角色，送出精確 broker cancel 並先持久化 OCO state；把 OCO group owner validation 加在 routed gateway；stop-market 僅由 coordinator 後續 action 送出，不可由行情 callback 直接繞過。
- [x] **Step 4: 執行 GREEN**：主程式 Release build 成功；隔離輸出完整 OrderManagement smoke suite 通過。測試未觸發真實券商委託。
- [x] **Step 5: Commit**：只提交 OCO coordinator、gateway/router/service 與 metadata/model production files，訊息 `feat: coordinate live strategy OCO orders`。OrderManagement test source 保留未暫存，因原本已有大量使用者變更，避免將不相關內容帶進 commit。

## Task 4: 最終成交水位、剩餘部位與 stop-market 冪等

**Files:**
- Modify: `KGIOptionPricing/StrategyFramework/Execution/KgiLiveStrategyExecutionService.cs`
- Modify: `KGIOptionPricing/StrategyFramework/Execution/ManagedStrategyOcoCoordinator.cs`
- Test: `tests/OrderManagementSmokeTests/Program.cs`
- Test: `tests/StrategyFrameworkSmokeTests/Program.cs`

**Interfaces:**
- Consumes: Task 3 OCO phase coordinator and persisted per-cycle state.
- Produces: OCO proceeds only if the final cumulative broker fill quantity equals the quantity already delivered into the strategy ledger; stop-market uses one persisted client id and quantity no greater than the strategy instance's current absolute net position.

- [x] **Step 1: 寫 failing race assertions**：驗證 stop 先於 TP 時等待進場撤單水位、TP 部分成交只送 residual stop-market 數量、重複 trigger／終態不重送，以及相反兩邊都成交時隔離。
- [x] **Step 2: 寫 failing unknown/reject assertions**：驗證撤單缺最終水位、撤單被拒與對側 entry fill 競態皆轉 `ReconciliationRequired` 且不送市價。
- [x] **Step 3: 執行 RED**：新增 stop-market terminal/idempotency assertions 時舊 coordinator 將所有 stop terminal 結果視為待人工處理，缺少可辨識的完全成交完成狀態；TDD 改動後補上持久化唯一 ID、數量及 authoritative terminal gate。
- [x] **Step 4: 最小實作**：以最終水位與已交付成交數核對、凍結 stop client id/數量；journal 寫入成功才送市價；拒單、未知回報及水位不符都 fail closed，不自動重送。
- [x] **Step 5: 執行 GREEN**：OrderManagement smoke suite 通過；StrategyFramework smoke suite 通過（明確略過僅 WinForms STA UI regression）；主程式 Release 建置 0 errors。
- [x] **Step 6: Commit**：只提交 OCO state/coordinator 與 execution service production files，訊息 `fix: gate stop market on settled OCO fills`。Smoke test source 未加入 commit，因該檔有大量既有使用者變更。

## Task 5: OCO restart fail-closed 與策略狀態可觀測性

**Files:**
- Modify: `KGIOptionPricing/StrategyFramework/LiveStrategyService.cs`
- Modify: `KGIOptionPricing/StrategyFramework/StrategyModels.cs`
- Modify: `KGIOptionPricing/StrategyFramework/Storage/StrategyObservationModels.cs`
- Modify: `KGIOptionPricing/StrategyFramework/Storage/SimulationRunRecorder.cs`
- Modify: `KGIOptionPricing/LiveStrategyCenterForm.cs`
- Test: `tests/LiveStrategyRecoverySmokeTests/Program.cs`
- Test: `tests/StrategyFrameworkSmokeTests/Program.cs`

**Interfaces:**
- Consumes: Task 1 `LiveOwnershipSnapshot.OcoGroups`; Tasks 3–4 phase, watermark, stop client id 與不確定原因。
- Produces: 恢復時 OCO phase 暴露於 live snapshot／紀錄／UI；未完成 reconciliation 不允許 Resume，且永不自動續送 stop-market。

- [x] **Step 1: 寫 restart assertions**：新增非終結 OCO 快照可觀察且阻擋 `Resume` 的測試，並保證測試不建立 live executor。
- [x] **Step 2: 寫 observation assertions**：共用 recorder 驗證 OCO group、broker id、trigger sequence、delivered/final fill watermark 與原因皆落入 lifecycle JSON。
- [x] **Step 3: 執行 RED**：未保留獨立行為 RED 執行紀錄；本次接續時先完成 gate 的最後缺口和測試，再執行 GREEN。測試 harness 初次建置只因缺少 `LiveRecoveryState` namespace 而失敗，修正 using 後才是有效 GREEN。
- [x] **Step 4: 最小實作**：啟動與 recover 對任何非 Completed OCO 維持全域 recovery lock；每個 snapshot 顯示 generation/cycle/phase、pending ids、成交水位與 reason；lifecycle 共用 observation 加入 broker 最終累積成交量。
- [x] **Step 5: 執行 GREEN**：隔離輸出 build + direct DLL execution：StrategyFramework smoke PASS（明確略過 unattended WinForms STA-only regression），LiveStrategyRecovery smoke 8 groups PASS；無 broker gateway 呼叫。
- [ ] **Step 6: Commit**：只提交 service/models/recorder/UI 與 OCO lifecycle projection production files，訊息 `feat: expose and recover strategy OCO state safely`。測試檔有既存使用者修改，因此保留新增測試在 working tree、不納入本次 commit。

## Task 6: 模擬與歷史重播一致性及完整驗收

**Files:**
- Modify: `KGIOptionPricing/StrategyFramework/Execution/CrossPricePaperExecutionService.cs`
- Modify: `tests/StrategyFrameworkSmokeTests/Program.cs`
- Modify: `tests/OrderManagementSmokeTests/Program.cs`
- Modify: `tests/LiveStrategyRecoverySmokeTests/Program.cs`

**Interfaces:**
- Consumes: Tasks 1–5 OCO roles, persisted transition model, final-watermark live release rule, MR broker-limit intent.
- Produces: `BrokerLimit` 在模擬／重播沿用嚴格穿價及委託價成交；同週期 entry sibling／TP-stop OCO 只觸發一次、只以當前策略剩餘部位平倉；真實 gateway call count 維持為零。

- [ ] **Step 1: 寫 failing paper assertions**：驗證觸及 `BrokerLimit` 不成交、嚴格穿價依限價成交；MR first fill 後只保留 stop，對側終結後加入 TP；停損觸發時不重複產生 exit execution，其他策略同商品工作單保持有效。
- [ ] **Step 2: 執行 RED**：執行 StrategyFramework smoke command。Expected：新增的 paper bracket transition assertions 失敗。
- [ ] **Step 3: 最小實作**：讓 paper adapter 依 cycle OCO 取消精確互斥單，對紙上終態事件設定 `HasFinalCumulativeFillWatermark = true`（paper adapter 能權威知道自己的完整累積量），確保同一市場事件不重複觸發；成交價仍為限價、保留目前手續費及嚴格穿價慣例。
- [ ] **Step 4: 執行所有 focused tests**：依序執行：
  - `dotnet run --project tests/LiveStrategyRecoverySmokeTests/LiveStrategyRecoverySmokeTests.csproj -c Release --no-restore`
  - `dotnet run --project tests/OrderManagementSmokeTests/OrderManagementSmokeTests.csproj -c Release --no-restore`
  - `dotnet run --project tests/StrategyFrameworkSmokeTests/StrategyFrameworkSmokeTests.csproj -c Release --no-restore -- --skip-windows-ui-regression`

Expected：三個 smoke projects 全 PASS；fake gateway 的 place/cancel 次序 assertions 證明沒有真實送單。
- [ ] **Step 5: Release build**：執行 `dotnet build KGIOptionPricing.sln -c Release --no-restore`。Expected：成功且 0 errors；逐項記錄已有 warnings，不得當作全零警告。
- [ ] **Step 6: Commit**：只提交 paper adapter 與此 task 測試，訊息 `test: verify MR OCO paper replay behavior`。

## Plan Self-Review

### Spec coverage

- 凍結雙邊 BrokerLimit、成交立即取消對側及 stop-first 保護：Task 2–3。
- TakeProfit BrokerLimit 與 local Stop trigger：Task 2。
- StopPending、精確 cancel、終態成交水位、策略 residual、唯一 stop-market：Tasks 3–4。
- owner/cycle isolation、journal/restart fail closed、未知／隔離：Tasks 1、3–5。
- 共用 UI／紀錄可觀測性：Task 5。
- 嚴格穿價模擬、regression、Release build、fake gateway no-real-order proof：Task 6。

### Placeholder scan

計畫沒有 TODO/TBD、泛稱「適當處理」或未定介面；每 task 具體列出檔案、測試 assertion、RED/GREEN 命令、預期結果與 commit 範圍。

### Type consistency

- OCO identity 全程使用 `LiveStrategyOcoKey(StrategyInstanceId, StrategyRunGeneration, CycleId)`。
- journal、gateway 和 execution service 均使用 `LiveStrategyOcoGroupState`；沒有另造第二種 key 或 phase 型別。
- 只有 `BrokerLimit` 被送到交易所；本機保護條件為 `Stop`；唯一停損平倉 intent 為 `Market + StopLoss`。
- final cumulative fill watermark 可為 null；null 不等同於已確認零成交，必須 fail closed。

### Review-focus coverage

Review Focus 五類情況分別由 Tasks 3–5 的晚到／重複成交、full/partial fill、缺 watermark／unknown submission、entry cancel race、restart replay assertions 鎖定。

### Existing recovery dependency

目前原多策略 recovery 計畫中的 broker per-execution restart source／strategy definition reconstruction 尚未完成。此 plan 可持久化並顯示 OCO phase，並要求任何未完全回補的 OCO 保持 `Recovering`／`Quarantined`；除非原 recovery adapter 已完成且通過對帳，否則不得宣稱停損流程支援自動重啟續作或解除人工暫停。
