# 多策略實盤回報路由與重啟回補 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 同帳戶、同商品的多個實盤策略能獨立接收券商回報、獨立分帳，且程式重啟後可安全回補並維持暫停。

**Architecture:** `LiveStrategyService` 擁有單一 `StrategyExecutionRouter` 與持久化 `StrategyOrderRouteStore`。執行器送單前登錄策略訂單身分；路由器依券商識別碼派送回報；策略帳本分實例計算，帳戶層只對帳與隔離風控。

**Tech Stack:** C#、.NET 10、Windows Forms、現有 `StrategyFramework`、`PaperPortfolioLedger`、`OrderManager`、JSON 與 Console smoke tests。

**Spec:** `docs/superpowers/specs/2026-09-23-multi-strategy-routing-recovery-design.md`

## Global Constraints

- 沿用 `StrategyFramework`、`PaperPortfolioLedger` 與 `OrderManager`；不建立平行策略引擎、損益計算器或商品表。
- 策略只能透過 `PaperOrderRequest` 送出意圖；路由持久化失敗時不得呼叫券商。
- 策略歸屬只可使用 `StrategyInstanceId`、`RunGeneration`、`ClientOrderId` 與券商識別碼映射；不可依商品、時間或帳戶淨部位猜測。
- 策略帳本按 `StrategyInstanceId + Symbol` 分帳；帳戶帳本僅供風控與對帳。
- 重啟後必須先回補，成功後也保持 `Suspended`，不得自動恢復送單。
- 未知券商委託／成交、映射衝突或帳戶淨部位不一致時，商品設為 `Quarantined` 並阻擋新單。
- 不修改 MR、MODWT、PQC 或其他策略的訊號與參數。
- 225MC 維持每點 ¥10、每口每次費用 ¥36，損益使用實際成交價與共用帳本。

## Review Focus

- 路由已保存但券商結果未返回後重啟，不能重複送單；由 Task 2 驗證。
- 同商品反向策略時關閉 A 不得碰到 B 的委託、部位或損益；由 Task 4 驗證。
- 重複、部分與亂序成交不可重複計費或計損益；由 Task 3 驗證。
- 未知券商單或帳戶／策略淨額不符時必須隔離商品；由 Task 5 驗證。
- 回補完成後維持暫停，需人工恢復；由 Task 5 驗證。

---

## File Structure

- `KGIOptionPricing/StrategyFramework/Execution/StrategyOrderRouteModels.cs`：路由鍵、路由列、券商識別碼、回補與隔離模型。
- `KGIOptionPricing/StrategyFramework/Execution/StrategyOrderRouteStore.cs`：原子 JSON 保存、回報水位與重啟載入。
- `KGIOptionPricing/StrategyFramework/Execution/StrategyExecutionRouter.cs`：中央註冊、回報派送、去重與商品對帳。
- `KGIOptionPricing/StrategyFramework/Execution/KgiLiveStrategyExecutionService.cs`：送單前登錄、由路由器接收回報。
- `KGIOptionPricing/StrategyFramework/LiveStrategyService.cs`：共用路由器、回補流程與商品交易閘。
- `KGIOptionPricing/OrderManagement/Core/{OrderInfo,Requests,OrderManager}.cs`：策略路由中繼資料隨訂單保存。
- `KGIOptionPricing/LiveStrategyCenterForm.cs`：回補、對帳、隔離與策略識別欄位。
- `tests/OrderManagementSmokeTests/Program.cs`：整合 smoke tests。

### Task 1: 定義策略訂單身分與訂單中繼資料

**Files:**
- Create: `KGIOptionPricing/StrategyFramework/Execution/StrategyOrderRouteModels.cs`
- Modify: `KGIOptionPricing/OrderManagement/Core/OrderInfo.cs`
- Modify: `KGIOptionPricing/OrderManagement/Core/Requests.cs`
- Test: `tests/OrderManagementSmokeTests/Program.cs`

**Interfaces:**
- Produces `StrategyOrderRouteKey(string StrategyInstanceId, long RunGeneration, string ClientOrderId)`、`StrategyRouteMetadata`、`StrategyOrderRoute`、`StrategyRecoveryState`、`InstrumentRouteStatus`。

- [ ] **Step 1: 寫入 failing test**

```csharp
var key = new StrategyOrderRouteKey("MR-A", 7, "entry-1");
var route = new StrategyOrderRoute { Key = key, Symbol = "225MC-Z26", Role = PaperOrderRole.EntryLong };
var order = new OrderInfo { OrderId = "STRAT-MR-A-1", StrategyRoute = route.ToMetadata() };
Assert(order.StrategyRoute?.StrategyInstanceId == "MR-A", "策略實例未保存");
Assert(order.StrategyRoute?.RunGeneration == 7, "策略世代未保存");
Assert(order.StrategyRoute?.Role == PaperOrderRole.EntryLong, "策略角色未保存");
```

- [ ] **Step 2: 確認 RED**

Run: `dotnet run --project tests/OrderManagementSmokeTests/OrderManagementSmokeTests.csproj -c Release --no-restore`

Expected: 編譯失敗，因路由模型與 `OrderInfo.StrategyRoute` 不存在。

- [ ] **Step 3: 最小實作**

```csharp
public sealed record StrategyOrderRouteKey(string StrategyInstanceId, long RunGeneration, string ClientOrderId);
public sealed record StrategyRouteMetadata
{
    public string StrategyInstanceId { get; init; } = string.Empty;
    public long RunGeneration { get; init; }
    public string ClientOrderId { get; init; } = string.Empty;
    public string CycleId { get; init; } = string.Empty;
    public PaperOrderRole Role { get; init; }
}
```

在 `OrderInfo`、`OrderRequest` 加入可空 `StrategyRouteMetadata? StrategyRoute`。手動／外部單保持 `null`；不得從 `OrderId` 字串反推。

- [ ] **Step 4: 確認 GREEN**

Run: `dotnet run --project tests/OrderManagementSmokeTests/OrderManagementSmokeTests.csproj -c Release --no-restore`

Expected: PASS，既有外部單測試仍通過。

- [ ] **Step 5: Commit**

```bash
git add KGIOptionPricing/StrategyFramework/Execution/StrategyOrderRouteModels.cs KGIOptionPricing/OrderManagement/Core/OrderInfo.cs KGIOptionPricing/OrderManagement/Core/Requests.cs tests/OrderManagementSmokeTests/Program.cs
git commit -m "feat: add persistent strategy order identity"
```

### Task 2: 建立送單前持久化的路由存放區

**Files:**
- Create: `KGIOptionPricing/StrategyFramework/Execution/StrategyOrderRouteStore.cs`
- Modify: `KGIOptionPricing/StrategyFramework/Execution/StrategyOrderRouteModels.cs`
- Test: `tests/OrderManagementSmokeTests/Program.cs`

**Interfaces:**
- Produces `IStrategyOrderRouteStore.Prepare`、`BindBrokerIdentity`、`ApplyFillWatermark`、`MarkSubmissionUnknown`、`LoadOpenRoutes`、`LoadRecoverySnapshot`。

- [ ] **Step 1: 寫入 failing test**

```csharp
using var store = new StrategyOrderRouteStore(tempRouteDirectory);
store.Prepare(new StrategyOrderRoute { Key = key, Symbol = "225MC-Z26", Account = "ACC" });
store.MarkSubmissionUnknown(key, "gateway timeout");
using var restarted = new StrategyOrderRouteStore(tempRouteDirectory);
var restored = restarted.LoadOpenRoutes("ACC", "225MC-Z26").Single();
Assert(restored.SubmissionState == StrategySubmissionState.Unknown, "不確定送單狀態未跨重啟保存");
```

另測試第二次 `Prepare(key)` 必須丟出例外。

- [ ] **Step 2: 確認 RED**

Run: `dotnet run --project tests/OrderManagementSmokeTests/OrderManagementSmokeTests.csproj -c Release --no-restore`

Expected: 編譯失敗，因 `StrategyOrderRouteStore` 與 `StrategySubmissionState` 不存在。

- [ ] **Step 3: 最小實作**

```csharp
public interface IStrategyOrderRouteStore : IDisposable
{
    StrategyOrderRoute Prepare(StrategyOrderRoute route);
    void BindBrokerIdentity(StrategyOrderRouteKey key, ExternalOrderIdentity identity, string brokerOrderId);
    FillWatermarkResult ApplyFillWatermark(StrategyOrderRouteKey key, string fillKey, int cumulativeQuantity);
    void MarkSubmissionUnknown(StrategyOrderRouteKey key, string reason);
    IReadOnlyList<StrategyOrderRoute> LoadOpenRoutes(string account, string symbol);
    StrategyRecoverySnapshot LoadRecoverySnapshot(string account);
}
```

存到獨立 `Strategy_Routes` 目錄，以暫存檔後 `File.Move(..., overwrite: true)` 原子替換。只有成功落盤後 `Prepare` 才可回傳；IO 例外必須阻止送券商。成交水位只有新 `fillKey` 或較高累積量可套用。

- [ ] **Step 4: 確認 GREEN**

Run: `dotnet run --project tests/OrderManagementSmokeTests/OrderManagementSmokeTests.csproj -c Release --no-restore`

Expected: PASS，重啟可讀回資料，重複鍵／成交水位被拒絕。

- [ ] **Step 5: Commit**

```bash
git add KGIOptionPricing/StrategyFramework/Execution/StrategyOrderRouteModels.cs KGIOptionPricing/StrategyFramework/Execution/StrategyOrderRouteStore.cs tests/OrderManagementSmokeTests/Program.cs
git commit -m "feat: persist live strategy order routes"
```

### Task 3: 實作中央回報路由與去重

**Files:**
- Create: `KGIOptionPricing/StrategyFramework/Execution/StrategyExecutionRouter.cs`
- Modify: `KGIOptionPricing/StrategyFramework/Execution/KgiLiveStrategyExecutionService.cs`
- Modify: `KGIOptionPricing/OrderManagement/Core/OrderManager.cs`
- Test: `tests/OrderManagementSmokeTests/Program.cs`

**Interfaces:**
- Produces `StrategyExecutionRouter.RegisterExecutor`、`PrepareSubmission`、`BindBrokerOrder`、`RouteFilled`、`RouteLifecycle` 與 `GetInstrumentStatus`。

- [ ] **Step 1: 寫入 failing test**

```csharp
router.RegisterExecutor("MR-A", executorA);
router.RegisterExecutor("PQC-B", executorB);
var route = router.PrepareSubmission(Request("MR-A", "entry-a", BuySell.Buy));
router.BindBrokerOrder(route.Key, new ExternalOrderIdentity { OrdNo = "ORD-A" }, "ORD-A");
router.RouteFilled(BrokerFill("ORD-A", cumulativeQuantity: 1, dealQuantity: 1, price: 66550));
router.RouteFilled(BrokerFill("ORD-A", cumulativeQuantity: 1, dealQuantity: 1, price: 66550));
Assert(executorA.Fills.Count == 1, "重複成交被重複派送");
Assert(executorB.Fills.Count == 0, "成交派送到非所有者策略");
```

增加部分成交 `1 -> 2` 只派送新增一口的斷言。

- [ ] **Step 2: 確認 RED**

Run: `dotnet run --project tests/OrderManagementSmokeTests/OrderManagementSmokeTests.csproj -c Release --no-restore`

Expected: 編譯失敗，因 `StrategyExecutionRouter` 不存在。

- [ ] **Step 3: 最小實作**

路由器以 `OrdNo`、`Cnt`、`OrgCnt`、`RequestId` 與內部 `OrderId` 做反向索引。零個或多個候選時建立 `QuarantinedBrokerEvent`，不得派送。執行器新增僅供路由器呼叫的入口：

```csharp
internal void ReceiveBrokerFill(OrderInfo order, int deltaQuantity, decimal price, DateTimeOffset timestamp);
internal void ReceiveBrokerLifecycle(OrderInfo order, OrderLifecycleState state, DateTimeOffset timestamp);
```

送單先 `PrepareSubmission`，將路由 metadata 放入 `OrderRequest.StrategyRoute`，再呼叫 gateway；gateway 結果不確定時 `MarkSubmissionUnknown`，同一 `ClientOrderId` 不得釋放或重送。`OrderManager.PlaceOrder` 複製 `request.StrategyRoute` 至 `OrderInfo`。

- [ ] **Step 4: 確認 GREEN**

Run: `dotnet run --project tests/OrderManagementSmokeTests/OrderManagementSmokeTests.csproj -c Release --no-restore`

Expected: PASS，雙策略隔離、部分成交、重複成交與既有 live execution tests 全通過。

- [ ] **Step 5: Commit**

```bash
git add KGIOptionPricing/StrategyFramework/Execution/StrategyExecutionRouter.cs KGIOptionPricing/StrategyFramework/Execution/KgiLiveStrategyExecutionService.cs KGIOptionPricing/OrderManagement/Core/OrderManager.cs tests/OrderManagementSmokeTests/Program.cs
git commit -m "feat: route live broker events by strategy identity"
```

### Task 4: 接入多策略分帳與隔離關閉

**Files:**
- Modify: `KGIOptionPricing/StrategyFramework/LiveStrategyService.cs`
- Modify: `KGIOptionPricing/StrategyFramework/Execution/KgiLiveStrategyExecutionService.cs`
- Test: `tests/OrderManagementSmokeTests/Program.cs`

**Interfaces:**
- Produces `LiveStrategyService.GetInstrumentRouteStatus(string account, string symbol)`；`RequestClose(instanceId, ...)` 只處理該實例路由訂單。

- [ ] **Step 1: 寫入 failing test**

```csharp
live.Add(Definition("MR-A", "225MC"), 36m, 1000m);
live.Add(Definition("PQC-B", "225MC"), 36m, 1000m);
Fill("MR-A", BuySell.Buy, 66550m);
Fill("PQC-B", BuySell.Sell, 66560m);
live.RequestClose("MR-A", flattenPosition: true, out _);
Assert(GetLiveExecution(live, "PQC-B").GetWorkingOrders("PQC-B").Count == 1, "策略 A 誤撤策略 B 單");
Assert(ledger.GetPosition("PQC-B", "225MC-Z26").NetQuantity == -1, "策略 A 改變策略 B 部位");
```

同時斷言兩策略的成本與已實現損益不互相覆寫，即使帳戶商品淨額為零。

- [ ] **Step 2: 確認 RED**

Run: `dotnet run --project tests/OrderManagementSmokeTests/OrderManagementSmokeTests.csproj -c Release --no-restore`

Expected: FAIL，現行服務未提供共享商品路由／帳戶對帳狀態。

- [ ] **Step 3: 最小實作**

`LiveStrategyService` 的兩個建構子建立或注入同一 router；每個 executor 使用唯一 `RunGeneration` 註冊。定義：

```csharp
public sealed record InstrumentRouteStatus(
    string Account, string Symbol, int StrategyNetQuantity, int BrokerNetQuantity,
    StrategyRecoveryState RecoveryState, bool IsQuarantined, string Reason, DateTimeOffset UpdatedAt);
```

使用現有 `PaperPortfolioLedger` 提供策略部位；帳戶層只保存每商品淨額。`RequestClose`、`EmergencyStop`、`Remove`、`CancelAll` 只以 `StrategyInstanceId` 尋找路由。策略角色固定保留，即使券商帳戶語意需要 Open／Close／Auto。

- [ ] **Step 4: 確認 GREEN**

Run: `dotnet run --project tests/OrderManagementSmokeTests/OrderManagementSmokeTests.csproj -c Release --no-restore`

Expected: PASS；A 的關閉不影響 B，既有 safe-close、late-fill、protocol tests 仍通過。

- [ ] **Step 5: Commit**

```bash
git add KGIOptionPricing/StrategyFramework/LiveStrategyService.cs KGIOptionPricing/StrategyFramework/Execution/KgiLiveStrategyExecutionService.cs tests/OrderManagementSmokeTests/Program.cs
git commit -m "feat: isolate concurrent live strategy positions"
```

### Task 5: 實作重啟回補、商品隔離與恢復閘

**Files:**
- Modify: `KGIOptionPricing/StrategyFramework/Execution/StrategyExecutionRouter.cs`
- Modify: `KGIOptionPricing/StrategyFramework/Execution/StrategyOrderRouteStore.cs`
- Modify: `KGIOptionPricing/StrategyFramework/LiveStrategyService.cs`
- Test: `tests/OrderManagementSmokeTests/Program.cs`

**Interfaces:**
- Produces `ILiveBrokerRecoverySource` 與 `LiveStrategyService.Recover()`。

- [ ] **Step 1: 寫入 failing test**

```csharp
var firstRun = CreateLiveWithRouteStore(routeDirectory, gateway);
firstRun.Add(Definition("MR-A", "225MC"), 36m, 1000m);
SubmitAndFillDuringDowntime(firstRun, gateway, "MR-A", "ORD-A", 66550m);
firstRun.Dispose();
var restarted = CreateLiveWithRouteStore(routeDirectory, gateway);
restarted.Recover();
Assert(restarted.Find("MR-A")?.State == StrategyLifecycleState.Suspended, "回補後策略不應自動恢復");
Assert(!restarted.GetInstrumentRouteStatus("ACC", "225MC-Z26").IsQuarantined, "可完整對帳商品被誤隔離");
```

另以券商淨額 `1`、策略淨額 `0` 回補，斷言商品隔離且新單被拒絕。

- [ ] **Step 2: 確認 RED**

Run: `dotnet run --project tests/OrderManagementSmokeTests/OrderManagementSmokeTests.csproj -c Release --no-restore`

Expected: 編譯失敗，因 `Recover` 與 recovery source 不存在。

- [ ] **Step 3: 最小實作**

```csharp
public interface ILiveBrokerRecoverySource
{
    IReadOnlyList<OrderInfo> GetOpenOrders(string account);
    IReadOnlyList<OrderInfo> GetTodayOrders(string account);
    IReadOnlyDictionary<string, int> GetNetPositions(string account);
}
```

`Recover()` 載入開放路由、以券商識別碼對應開放單與當日成交、使用成交水位去重、重建策略帳本、比較各商品策略淨額與券商淨額。成功商品由 `Recovering` 轉為 `Suspended`；未知單、未知成交、路由衝突、成交量回退或淨額差異轉 `Quarantined`。`Add` 與送單前都必須拒絕隔離商品。

- [ ] **Step 4: 確認 GREEN**

Run: `dotnet run --project tests/OrderManagementSmokeTests/OrderManagementSmokeTests.csproj -c Release --no-restore`

Expected: PASS；成功回補保持暫停，不一致時隔離，既有訂單管理測試仍通過。

- [ ] **Step 5: Commit**

```bash
git add KGIOptionPricing/StrategyFramework/Execution/StrategyExecutionRouter.cs KGIOptionPricing/StrategyFramework/Execution/StrategyOrderRouteStore.cs KGIOptionPricing/StrategyFramework/LiveStrategyService.cs tests/OrderManagementSmokeTests/Program.cs
git commit -m "feat: recover and reconcile live strategy routes"
```

### Task 6: 顯示路由／回補狀態並完成驗證

**Files:**
- Modify: `KGIOptionPricing/LiveStrategyCenterForm.cs`
- Modify: `KGIOptionPricing/StrategyFramework/LiveStrategyService.cs`
- Modify: `tests/OrderManagementSmokeTests/Program.cs`
- Modify: `tests/StrategyFrameworkSmokeTests/Program.cs`

**Interfaces:**
- Consumes `InstrumentRouteStatus`、`StrategyRecoveryState`、`StrategySnapshot`。

- [ ] **Step 1: 寫入 failing test**

```csharp
OrderInfo manual = new() { OrderId = "MANUAL-1", StrategyRoute = null };
Assert(!StrategyExecutionRouter.TryResolve(manual, out _), "手動單不可被分配至策略");
var status = live.GetInstrumentRouteStatus("ACC", "225MC-Z26");
Assert(status.RecoveryState == StrategyRecoveryState.Suspended, "未提供回補 UI 狀態來源");
```

- [ ] **Step 2: 確認 RED**

Run: `dotnet run --project tests/OrderManagementSmokeTests/OrderManagementSmokeTests.csproj -c Release --no-restore`

Expected: FAIL，狀態／解析公開入口尚未完整提供。

- [ ] **Step 3: 最小實作**

在 `LiveStrategyCenterForm` 以固定欄寬增加：`實例`、`世代`、`回補狀態`、`策略部位`、`帳戶商品淨額`、`對帳差異`、`隔離原因`、`最後回補`；成交表增加 `角色`、`ClientOrderId`、`BrokerOrderId`、`回補`。不得使用 `AutoSize`。`Recovering`／`Quarantined` 時啟動、繼續鍵顯示阻擋原因且不影響其他策略。

- [ ] **Step 4: 執行完整驗證**

Run:

```bash
dotnet run --project tests/OrderManagementSmokeTests/OrderManagementSmokeTests.csproj -c Release --no-restore
dotnet run --project tests/StrategyFrameworkSmokeTests/StrategyFrameworkSmokeTests.csproj -c Release --no-restore
dotnet run --project tests/PeriodicQueueCycleSmokeTests/PeriodicQueueCycleSmokeTests.csproj -c Release --no-restore
dotnet build KGIOptionPricing.sln -c Release --no-restore
```

Expected: 全數 PASS／成功；任何既有失敗需於最終報告列出。

- [ ] **Step 5: Commit**

```bash
git add KGIOptionPricing/LiveStrategyCenterForm.cs KGIOptionPricing/StrategyFramework/LiveStrategyService.cs tests/OrderManagementSmokeTests/Program.cs tests/StrategyFrameworkSmokeTests/Program.cs
git commit -m "feat: surface live strategy recovery status"
```

## Plan Self-Review

### Spec coverage

- 多策略同商品、精確映射、去重：Tasks 1–4。
- 策略／帳戶雙層分帳與隔離關閉：Task 4。
- 送單前持久化與不確定送單：Tasks 2–3。
- 重啟回補、隔離、維持暫停：Task 5。
- UI 揭露、手動單隔離與完整驗證：Task 6。

### Placeholder scan

本計畫未留下待補項、延後實作項或未具體化的測試步驟。

### Type consistency

所有後續使用的 `StrategyOrderRouteKey`、`StrategyRouteMetadata`、`StrategyOrderRoute`、`IStrategyOrderRouteStore`、`StrategyExecutionRouter`、`InstrumentRouteStatus`、`StrategyRecoveryState` 和 `ILiveBrokerRecoverySource` 都在首次使用的任務定義；`RunGeneration` 使用 `long`。

### Review-focus coverage

五項 Review Focus 分別由 Tasks 2、4、3、5、5 的 failing tests 覆蓋。
