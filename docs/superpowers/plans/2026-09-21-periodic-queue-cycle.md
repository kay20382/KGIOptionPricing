# Periodic Queue Cycle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the `PeriodicQueueCycle` strategy so simulation, historical replay, and opt-in live execution can run two independent queue-follow completion orders with measured direction, frozen price boundaries, fixed stop loss, one-leg timeout, cooldowns, and reproducible research outputs.

**Architecture:** Extend the shared strategy framework with correlated per-order lifecycle events and a live `BrokerLimit` order type; do not reuse the direct-broker `QueueFollowStrategyEngine` as a parallel engine. Implement direction, health, queue pricing, and cycle coordination as focused pure components under `Strategies/PeriodicQueueCycle`, then connect the strategy to the shared host, ledger, recorder, simulation service, live service, and two strategy-center tabs.

**Tech Stack:** C# 14 / .NET 10 Windows Forms, existing KGI `OrderManager` and `KGITradeService`, JSONL/CSV shared run artifacts, Python 3 standard library analysis, console smoke-test projects.

**Spec:** `docs/superpowers/specs/2026-09-21-periodic-queue-cycle-design.md`

## Global Constraints

- Stable strategy type is `PeriodicQueueCycle`; UI display name is `週期雙邊完成單`.
- `225MC` is the only live tradable product, with `TickSize=5`, contract multiplier `10`, default commission JPY `36` per fill, and live quantity fixed at one.
- The buy and sell completion orders are independent and simultaneous; they are not OCO, and one fill must not automatically cancel the other.
- Strategy code may consume only `MarketEvent`, `StrategyRuntimeContext`, shared order contracts, and `IStrategyPerformanceView`; it must not call `OrderManager`, `KGITradeService`, or broker APIs.
- Simulation preserves strict crossing: touching an order price does not fill, crossing fills at the requested price.
- All order prices use the resolved `TickSize`; working completion-order boundaries are frozen at cycle creation and never trail the market.
- Fixed adverse-price exits use `PaperOrderRole.StopLoss`; time, data-health, administrative, and safe-close exits use `PaperOrderRole.Flatten`.
- Live execution remains opt-in and requires login, `225MC`, one lot, daily-loss control, explicit confirmation, and `ResearchValidated=true`; tests must never transmit a real broker order.
- Existing dirty-worktree changes belong to the user; stage and commit only files named by the current task.

## Review Focus

- Duplicate or out-of-order Book events in the same second must not extend the direction window or overwrite a newer sample; Task 4 adds the deterministic sampling test.
- A fill arriving after cancel request but before cancel confirmation must update the ledger and suppress an obsolete replacement order; Task 6 adds the cancel/fill race test.
- A half-tick cycle midpoint must produce valid, inward-rounded buy/sell boundaries that are never exceeded; Task 5 adds the boundary-rounding test.
- Loss of healthy quotes while one leg is filled must cancel the working leg and flatten after the recovery timeout without classifying the exit as `StopLoss`; Task 7 adds the stale-book risk test.
- Application close while a strategy order is cancel-pending must remain blocked until broker orders and position are both zero; Task 9 adds the shutdown-gate test.

---

### Task 1: Add correlated managed-order contracts

**Files:**
- Modify: `KGIOptionPricing/StrategyFramework/StrategyModels.cs`
- Modify: `KGIOptionPricing/StrategyFramework/StrategyInterfaces.cs`
- Modify: `tests/StrategyFrameworkSmokeTests/Program.cs`

**Interfaces:**
- Consumes: existing `PaperOrderRequest`, `PaperOrder`, `ExecutionEvent`, `ISimulationStrategy`, and `IStrategyExecutionService`.
- Produces: `PaperOrderType.BrokerLimit`, `OrderLifecycleState`, `OrderLifecycleEvent`, `IManagedOrderStrategy`, and `IManagedStrategyExecutionService.CancelByClientOrderId(string, string)`.

- [ ] **Step 1: Write the failing contract test**

Append a compile-time and value-preservation test to `tests/StrategyFrameworkSmokeTests/Program.cs`:

```csharp
var correlated = new PaperOrderRequest
{
    ClientOrderId = "cycle-1-buy-g1", StrategyInstanceId = "managed", Symbol = "MC",
    Side = BuySell.Buy, Type = PaperOrderType.BrokerLimit,
    Role = PaperOrderRole.EntryLong, Price = 100m, Quantity = 1
};
var lifecycle = new OrderLifecycleEvent
{
    StrategyInstanceId = "managed", ClientOrderId = correlated.ClientOrderId,
    OrderId = "SIM-managed-1", State = OrderLifecycleState.Working,
    FilledQuantity = 0, RemainingQuantity = 1
};
Assert(lifecycle.ClientOrderId == "cycle-1-buy-g1" && lifecycle.RemainingQuantity == 1,
    "單張委託生命週期必須保留策略用戶端識別與剩餘數量");
```

- [ ] **Step 2: Run the smoke test and verify it fails to compile**

Run: `dotnet run --project tests/StrategyFrameworkSmokeTests/StrategyFrameworkSmokeTests.csproj`

Expected: FAIL because `BrokerLimit`, `ClientOrderId`, and `OrderLifecycleEvent` do not exist.

- [ ] **Step 3: Add the shared types**

Add the following public contracts, keeping default empty IDs for existing callers:

```csharp
public enum PaperOrderType { Limit, Stop, Market, BrokerLimit }
public enum OrderLifecycleState
{
    Submitted, Working, CancelPending, PartiallyFilled, Filled, Cancelled, Rejected
}

public sealed record OrderLifecycleEvent
{
    public DateTimeOffset Timestamp { get; init; }
    public string StrategyInstanceId { get; init; } = string.Empty;
    public string ClientOrderId { get; init; } = string.Empty;
    public string OrderId { get; init; } = string.Empty;
    public string BrokerOrderId { get; init; } = string.Empty;
    public OrderLifecycleState State { get; init; }
    public int FilledQuantity { get; init; }
    public int RemainingQuantity { get; init; }
    public string ReasonCode { get; init; } = string.Empty;
}
```

Add `ClientOrderId` to `PaperOrderRequest`, `PaperOrder`, and `ExecutionEvent`. Add optional interfaces rather than changing every existing strategy:

```csharp
public interface IManagedOrderStrategy
{
    event Action<string>? CancelOrderRequested;
    void OnOrderLifecycle(OrderLifecycleEvent orderEvent);
}

public interface IManagedStrategyExecutionService : IStrategyExecutionService
{
    event Action<OrderLifecycleEvent>? OrderLifecycleChanged;
    bool CancelByClientOrderId(string strategyInstanceId, string clientOrderId);
}
```

- [ ] **Step 4: Run the framework smoke test**

Run: `dotnet run --project tests/StrategyFrameworkSmokeTests/StrategyFrameworkSmokeTests.csproj`

Expected: PASS, including the new contract assertion and all existing strict-crossing tests.

- [ ] **Step 5: Commit the contract slice**

```powershell
git add KGIOptionPricing/StrategyFramework/StrategyModels.cs KGIOptionPricing/StrategyFramework/StrategyInterfaces.cs tests/StrategyFrameworkSmokeTests/Program.cs
git commit -m "feat: add managed strategy order contracts"
```

### Task 2: Emit and route order lifecycle events in paper execution

**Files:**
- Modify: `KGIOptionPricing/StrategyFramework/Execution/CrossPricePaperExecutionService.cs`
- Modify: `KGIOptionPricing/StrategyFramework/StrategyHost.cs`
- Modify: `tests/StrategyFrameworkSmokeTests/Program.cs`

**Interfaces:**
- Consumes: `IManagedOrderStrategy`, `IManagedStrategyExecutionService`, and `OrderLifecycleEvent` from Task 1.
- Produces: deterministic `Submitted`, `Working`, `CancelPending`, `Cancelled`, and `Filled` events routed back to the owning strategy.

- [ ] **Step 1: Add a managed probe strategy test**

Create a small `ManagedProbeStrategy` at the bottom of `tests/StrategyFrameworkSmokeTests/Program.cs` that implements both strategy interfaces, captures lifecycle events, and exposes `Request` and `Cancel(clientOrderId)` methods. Test this sequence:

```csharp
var managedBroker = new CrossPricePaperExecutionService();
var managedProbe = new ManagedProbeStrategy("managed-route");
using var managedRunner = new StrategyInstanceRunner(
    managedProbe, new ResolvedStrategyUniverse { Tradable = Product("225MC", "MGR", 5m, 10m) },
    new PaperPortfolioLedger(), new SystemStrategyClock(), 0m, managedBroker);
managedProbe.Request("buy-g1", BuySell.Buy, 100m);
managedProbe.Cancel("buy-g1");
Assert(managedProbe.Lifecycle.Select(x => x.State).SequenceEqual(new[]
    { OrderLifecycleState.Submitted, OrderLifecycleState.Working,
      OrderLifecycleState.CancelPending, OrderLifecycleState.Cancelled }),
    "共用執行器必須依序回報單張送出、工作、刪單中與已刪除");
```

- [ ] **Step 2: Run the test and verify the missing lifecycle failure**

Run: `dotnet run --project tests/StrategyFrameworkSmokeTests/StrategyFrameworkSmokeTests.csproj`

Expected: FAIL because the paper broker and runner do not publish or route managed lifecycle events.

- [ ] **Step 3: Implement paper lifecycle and client-ID cancellation**

Make `CrossPricePaperExecutionService` implement `IManagedStrategyExecutionService`. Preserve `ClientOrderId` on stored orders and fills. Emit lifecycle events outside `_sync` locks. `CancelByClientOrderId` must find exactly one working order belonging to the supplied instance, emit `CancelPending`, update state, then emit `Cancelled`; missing or already-terminal IDs return `false` without fabricating confirmation.

Treat `BrokerLimit` exactly like `Limit` in strict-crossing simulation:

```csharp
PaperOrderType.Limit or PaperOrderType.BrokerLimit when order.Side == BuySell.Buy
    => marketEvent.LastPrice < order.Price,
PaperOrderType.Limit or PaperOrderType.BrokerLimit
    => marketEvent.LastPrice > order.Price,
```

- [ ] **Step 4: Route lifecycle through `StrategyInstanceRunner`**

When both optional interfaces are present, wire them in the runner constructor and unwire them in `Dispose`:

```csharp
if (_strategy is IManagedOrderStrategy managedStrategy &&
    _broker is IManagedStrategyExecutionService managedBroker)
{
    managedStrategy.CancelOrderRequested += clientOrderId =>
        managedBroker.CancelByClientOrderId(_strategy.InstanceId, clientOrderId);
    managedBroker.OrderLifecycleChanged += managedStrategy.OnOrderLifecycle;
}
```

Use named private handlers instead of anonymous handlers in the final implementation so unsubscription is exact.

- [ ] **Step 5: Run framework tests and commit**

Run: `dotnet run --project tests/StrategyFrameworkSmokeTests/StrategyFrameworkSmokeTests.csproj`

Expected: PASS with the exact lifecycle sequence and existing strategy behavior unchanged.

```powershell
git add KGIOptionPricing/StrategyFramework/Execution/CrossPricePaperExecutionService.cs KGIOptionPricing/StrategyFramework/StrategyHost.cs tests/StrategyFrameworkSmokeTests/Program.cs
git commit -m "feat: route managed paper order lifecycle"
```

### Task 3: Support real working limit orders in the live adapter

**Files:**
- Create: `KGIOptionPricing/StrategyFramework/Execution/LiveStrategyOrderMapper.cs`
- Modify: `KGIOptionPricing/StrategyFramework/Execution/KgiLiveStrategyExecutionService.cs`
- Modify: `tests/OrderManagementSmokeTests/Program.cs`

**Interfaces:**
- Consumes: `PaperOrderType.BrokerLimit`, correlated order models, `OrderRequest`, and existing `OrderManager` callbacks.
- Produces: `LiveStrategyOrderMapper.CreateBrokerLimit(PaperOrderRequest, InstrumentKey, string brokerId, string account, string realOrderId)` and live managed-order lifecycle/cancellation behavior.

- [ ] **Step 1: Add pure mapping tests before touching the broker adapter**

Add tests that create a 225MC buy and sell `PaperOrderRequest` and assert the mapped request uses the requested limit price, `TimeInForce.ROD`, `PositionEffect.Auto`, quantity one, account, broker ID, and a nonempty real order ID:

```csharp
OrderRequest mapped = LiveStrategyOrderMapper.CreateBrokerLimit(
    request, instrument, "BRK", "ACC", "REAL-1");
Assert(mapped.Price == 100d && mapped.TimeInForce == TimeInForce.ROD &&
       mapped.Quantity == 1 && mapped.OrderId == "REAL-1",
    "BrokerLimit 必須成為一口 ROD 限價單，不得轉成市價 IOC");
```

- [ ] **Step 2: Run the order-management smoke test and verify it fails**

Run: `dotnet run --project tests/OrderManagementSmokeTests/OrderManagementSmokeTests.csproj`

Expected: FAIL because `LiveStrategyOrderMapper` does not exist.

- [ ] **Step 3: Implement the pure mapper**

Implement `CreateBrokerLimit` with validation for `Quantity == 1`, positive price, and nonempty account/broker/order IDs. It returns an `OrderRequest` with `Price=(double)request.Price`, never `0d`.

- [ ] **Step 4: Add `BrokerLimit` handling to the live service**

Make `KgiLiveStrategyExecutionService` implement `IManagedStrategyExecutionService`. For `BrokerLimit`, immediately create and track a real broker order and call `OrderManager.PlaceOrder` with the mapper; do not add it to `_conditions`, do not clear the other completion order, and do not pass through the local OCO trigger logic. Preserve the existing local-condition behavior for `Limit` and `Stop` so existing strategies do not change.

Map broker callbacks to shared events:

- accepted/valid → `Working`;
- cancel request → `CancelPending`;
- cancelled → `Cancelled`;
- rejected/new failure → `Rejected` plus existing `Failed` notification;
- partial fill → `PartiallyFilled` with accurate quantities;
- complete fill → `Filled` plus the existing `ExecutionEvent`.

Implement `CancelByClientOrderId` by locating the owned real order, emitting `CancelPending`, and asking `OrderManager` to cancel that real ID. Do not remove it until a terminal broker callback arrives.

- [ ] **Step 5: Verify mapping and compile the full application**

Run:

```powershell
dotnet run --project tests/OrderManagementSmokeTests/OrderManagementSmokeTests.csproj
dotnet build KGIOptionPricing.sln -c Debug --no-restore
```

Expected: smoke test PASS; Debug build succeeds; no real order is sent because the tests invoke only the pure mapper.

- [ ] **Step 6: Commit the live adapter slice**

```powershell
git add KGIOptionPricing/StrategyFramework/Execution/LiveStrategyOrderMapper.cs KGIOptionPricing/StrategyFramework/Execution/KgiLiveStrategyExecutionService.cs tests/OrderManagementSmokeTests/Program.cs
git commit -m "feat: support managed live broker limit orders"
```

### Task 4: Implement parameter parsing, healthy sampling, and direction calculation

**Files:**
- Create: `KGIOptionPricing/Strategies/PeriodicQueueCycle/PeriodicQueueCycleParameters.cs`
- Create: `KGIOptionPricing/Strategies/PeriodicQueueCycle/DirectionWindow.cs`
- Create: `tests/PeriodicQueueCycleSmokeTests/PeriodicQueueCycleSmokeTests.csproj`
- Create: `tests/PeriodicQueueCycleSmokeTests/Program.cs`

**Interfaces:**
- Consumes: `MarketEvent`, resolved `TickSize`, and the parameter dictionary.
- Produces: `PeriodicQueueCycleParameters.Parse(IReadOnlyDictionary<string,string>)`, `DirectionWindow.TryAdd(MarketEvent, DateTimeOffset)`, and `DirectionAssessment Evaluate(DateTimeOffset, decimal tickSize, PeriodicQueueCycleParameters)`.

- [ ] **Step 1: Create the smoke-test project and failing parameter tests**

The test project targets `net10.0-windows` and references `KGIOptionPricing.csproj`. Tests assert invariant parsing, defaults, safety ranges, `ObiConfirmSeconds <= DirectionWindowSeconds`, A/C/B relationships, and a complete `ParameterSummary`.

```csharp
PeriodicQueueCycleParameters p = PeriodicQueueCycleParameters.Parse(new Dictionary<string,string>());
Assert(p.DirectionWindowSeconds == 15 && p.StopLossTicks == 6m &&
       p.MaxOneLegSeconds == 30 && !p.ResearchValidated,
    "週期策略預設參數不符設計規格");
AssertThrows<ArgumentException>(() => PeriodicQueueCycleParameters.Parse(
    new Dictionary<string,string> { ["TrendRepriceThresholdB"] = "10", ["TrendRepriceTargetC"] = "10" }));
```

- [ ] **Step 2: Run and confirm the missing-type failure**

Run: `dotnet run --project tests/PeriodicQueueCycleSmokeTests/PeriodicQueueCycleSmokeTests.csproj`

Expected: FAIL because the strategy parameter and direction types do not exist.

- [ ] **Step 3: Implement complete parameter parsing**

Create immutable properties for every parameter in spec section 10 plus the six A/B/C values. Parse decimals with `InvariantCulture`, booleans with `bool.TryParse`, enforce all safety ranges, and expose a deterministic summary containing every effective value.

Pin these exact defaults in both code and tests:

```text
DirectionWindowSeconds=15
MinMoveTicks=2
MinTrendEfficiency=0.40
MinDepthObi=0.10
ObiConfirmSeconds=3
DirectionReverseSeconds=3
MinSideDepth=827
MinNearDepth=165
MinDepthRatio=0.35
MaxSpreadTicks=2
MaxVolatilityTicks=5
QuoteFreshnessSeconds=2
MaxChaseTicks=2
StopLossTicks=6
MaxOneLegSeconds=30
CycleCooldownSeconds=45
StopLossCooldownSeconds=60
ShockCooldownSeconds=45
MarketDataRecoverySeconds=2
CommissionPerContract=36
ResearchValidated=false
TrendInitialAheadA=22
TrendRepriceThresholdB=49
TrendRepriceTargetC=22
OppositeInitialAheadA=240
OppositeRepriceThresholdB=312
OppositeRepriceTargetC=22
```

Define the direction result contracts in `DirectionWindow.cs` so later tasks use the same names:

```csharp
public enum CycleDirection { Neutral, Up, Down }
public sealed record ThresholdResult(string Name, decimal Observed, decimal Required, bool Passed);
public sealed record DirectionAssessment(
    CycleDirection Direction,
    decimal NetMoveTicks,
    decimal PathTicks,
    decimal TrendEfficiency,
    decimal AverageObi,
    decimal SpreadTicks,
    decimal VolatilityTicks,
    string PrimaryReasonCode,
    IReadOnlyList<string> OtherFailedReasonCodes,
    IReadOnlyList<ThresholdResult> Thresholds);
```

- [ ] **Step 4: Add failing direction and health tests**

Generate ten-level Books with controlled midpoint and quantities. Pin these behaviors:

- 15 healthy one-second samples, +2 ticks, efficiency 1.0, and three-second OBI above +0.10 → `Up`;
- symmetric negative path → `Down`;
- OBI disagreement or efficiency below 0.40 → `Neutral`;
- stale, crossed, fewer-than-ten-level, shallow, imbalanced, wide-spread, or over-volatile Book → a stable rejection code;
- a later event in a sampled second replaces the earlier event, while an older/out-of-order event does not overwrite it.

```csharp
Assert(window.SampleCount == 15 && assessment.Direction == CycleDirection.Up,
    $"預期向上，實際 {assessment.Direction}/{assessment.PrimaryReasonCode}");
Assert(window.LatestSequence == newestSequence,
    "同秒或亂序事件不得以舊序號覆寫較新的健康樣本");
```

- [ ] **Step 5: Implement `DirectionWindow` as a pure deterministic component**

Store at most one valid sample per UTC second, keyed by second and guarded by sequence/timestamp. Calculate midpoint, net ticks, path ticks, efficiency, reciprocal-level weighted OBI, depth totals, ratio, spread, volatility, and quote age. Return a `DirectionAssessment` containing direction, every feature, each threshold result, primary reason, and other failed reason codes.

- [ ] **Step 6: Run tests and commit**

Run: `dotnet run --project tests/PeriodicQueueCycleSmokeTests/PeriodicQueueCycleSmokeTests.csproj`

Expected: PASS for parameters, health gates, direction symmetry, and deterministic sampling.

```powershell
git add KGIOptionPricing/Strategies/PeriodicQueueCycle/PeriodicQueueCycleParameters.cs KGIOptionPricing/Strategies/PeriodicQueueCycle/DirectionWindow.cs tests/PeriodicQueueCycleSmokeTests
git commit -m "feat: calculate periodic queue direction"
```

### Task 5: Implement queue pricing with frozen boundaries

**Files:**
- Create: `KGIOptionPricing/Strategies/PeriodicQueueCycle/QueueCyclePricingPolicy.cs`
- Modify: `tests/PeriodicQueueCycleSmokeTests/Program.cs`

**Interfaces:**
- Consumes: `MarketEvent` ten-level arrays, side-specific A/B/C, `CycleAnchorPrice`, `TickSize`, and `MaxChaseTicks`.
- Produces: `QueuePriceDecision SelectInitial(BuySell, MarketEvent, int targetAhead, FrozenPriceBounds)` and `QueuePriceDecision EvaluateReprice(BuySell, MarketEvent, decimal currentPrice, int samePriceAhead, int thresholdB, int targetC, FrozenPriceBounds)`.

- [ ] **Step 1: Add failing A/B/C and boundary tests**

Cover buy/sell symmetry, cumulative quantity selection, C reprice only when total ahead exceeds B, no less-aggressive reprice, and frozen bounds. Include the half-tick review case:

```csharp
FrozenPriceBounds bounds = QueueCyclePricingPolicy.CreateBounds(102.5m, 5m, 2m);
Assert(bounds.BuyLimit == 110m && bounds.SellLimit == 95m,
    $"半 Tick 中價邊界取整錯誤：{bounds.BuyLimit}/{bounds.SellLimit}");
Assert(policy.SelectInitial(BuySell.Buy, book, 22, bounds).Price <= bounds.BuyLimit,
    "買單首次掛單不得突破凍結上界");
```

- [ ] **Step 2: Run and confirm failure**

Run: `dotnet run --project tests/PeriodicQueueCycleSmokeTests/PeriodicQueueCycleSmokeTests.csproj`

Expected: FAIL because `QueueCyclePricingPolicy` is missing.

- [ ] **Step 3: Implement the pure policy**

Port the validated cumulative-ahead behavior from `QueueFollowPricePlanner` without depending on `OrderManager`, `InstrumentKey`, or UI. `CreateBounds` must align the buy cap down and sell floor up to valid ticks. `EvaluateReprice` returns `Hold/PriceBoundaryReached` when a candidate equals the current price, is less aggressive, or would cross its frozen bound.

Use these result contracts:

```csharp
public enum QueuePriceAction { Hold, Place, Reprice }
public sealed record FrozenPriceBounds(decimal Anchor, decimal TickSize, decimal BuyLimit, decimal SellLimit);
public sealed record QueuePriceDecision(
    QueuePriceAction Action, decimal Price, int SamePriceAheadQuantity,
    int BetterPriceQuantity, string ReasonCode);
```

- [ ] **Step 4: Run tests and commit**

Run: `dotnet run --project tests/PeriodicQueueCycleSmokeTests/PeriodicQueueCycleSmokeTests.csproj`

Expected: PASS for all A/B/C and boundary paths.

```powershell
git add KGIOptionPricing/Strategies/PeriodicQueueCycle/QueueCyclePricingPolicy.cs tests/PeriodicQueueCycleSmokeTests/Program.cs
git commit -m "feat: add bounded queue cycle pricing"
```

### Task 6: Build the dual completion-order cycle state machine

**Files:**
- Create: `KGIOptionPricing/Strategies/PeriodicQueueCycle/QueueCycleOrderCoordinator.cs`
- Create: `KGIOptionPricing/Strategies/PeriodicQueueCycle/PeriodicQueueCycleStrategy.cs`
- Create: `KGIOptionPricing/Strategies/PeriodicQueueCycle/PeriodicQueueCycleFactory.cs`
- Modify: `tests/PeriodicQueueCycleSmokeTests/Program.cs`

**Interfaces:**
- Consumes: Tasks 1–5 contracts, `StrategyRuntimeContext`, shared position view, and lifecycle/execution callbacks.
- Produces: `PeriodicQueueCycleFactory.TypeName`, an `ISimulationStrategy` + `IManagedOrderStrategy`, cycle IDs, generation-specific client order IDs, and simultaneous `BrokerLimit` requests.

- [ ] **Step 1: Add failing warmup and pair-submission tests**

Feed a complete upward window and assert exactly two requests are emitted in the same market callback:

```csharp
Assert(orders.Count == 2 && orders.Any(x => x.Side == BuySell.Buy) &&
       orders.Any(x => x.Side == BuySell.Sell),
    "方向成立時必須同時建立買賣兩張完成單");
Assert(orders.Single(x => x.Side == BuySell.Buy).ClientOrderId.Contains("buy-g1") &&
       orders.Single(x => x.Side == BuySell.Sell).ClientOrderId.Contains("sell-g1"),
    "雙邊完成單必須有不同且可追蹤的世代識別");
```

Assert upward direction maps trend A/B/C to buy and opposite A/B/C to sell; downward direction maps them oppositely. Assert `ParameterSummary` contains every effective value.

- [ ] **Step 2: Run and verify the missing strategy failure**

Run: `dotnet run --project tests/PeriodicQueueCycleSmokeTests/PeriodicQueueCycleSmokeTests.csproj`

Expected: FAIL because the factory, strategy, and coordinator are missing.

- [ ] **Step 3: Implement factory and strategy lifecycle**

Use `Created → WarmingUp → Ready → Running` public lifecycle states and a separate internal `QueueCyclePhase`. Only the factory parses definitions. On accepted direction, freeze cycle ID, direction features, anchor, boundaries, and A/B/C mapping, then emit two `BrokerLimit` requests. Quantity is one.

- [ ] **Step 4: Implement targeted reprice and non-OCO fills**

The coordinator tracks each side independently. When B is exceeded, emit `CancelOrderRequested(oldClientOrderId)` and enter cancel-pending. Only `Cancelled` for that exact generation may create the replacement. A fill on one side leaves the other side working.

Add the cancel/fill race test:

```csharp
strategy.OnOrderLifecycle(CancelPending(oldBuy));
strategy.OnExecution(Fill(oldBuy, BuySell.Buy, 105m));
strategy.OnOrderLifecycle(Cancelled(oldBuy));
Assert(orders.Count(x => x.ClientOrderId.Contains("buy-g2")) == 0,
    "撤單競速期間已成交的舊世代不得再建立替代買單");
```

Define `CancelPending(string)` and `Cancelled(string)` as local test helpers returning `OrderLifecycleEvent`, and `Fill(string, BuySell, decimal)` as a local helper returning `ExecutionEvent`; each helper sets instance, client ID, timestamp, quantity one, and the corresponding state/side/price.

- [ ] **Step 5: Add and implement pre-fill direction reversal**

Feed three consecutive seconds satisfying the full opposite direction while both orders remain unfilled. Assert both client IDs are targeted for cancellation, reason is `SignalReversedBeforeFill`, and no `StopLoss` or `Flatten` request is created.

- [ ] **Step 6: Run tests and commit**

Run: `dotnet run --project tests/PeriodicQueueCycleSmokeTests/PeriodicQueueCycleSmokeTests.csproj`

Expected: PASS for warmup, mapping, simultaneous non-OCO work, targeted replacement, race handling, and signal reversal.

```powershell
git add KGIOptionPricing/Strategies/PeriodicQueueCycle/QueueCycleOrderCoordinator.cs KGIOptionPricing/Strategies/PeriodicQueueCycle/PeriodicQueueCycleStrategy.cs KGIOptionPricing/Strategies/PeriodicQueueCycle/PeriodicQueueCycleFactory.cs tests/PeriodicQueueCycleSmokeTests/Program.cs
git commit -m "feat: add periodic dual queue cycle"
```

### Task 7: Add stop loss, one-leg timeout, data recovery, and cooldowns

**Files:**
- Modify: `KGIOptionPricing/Strategies/PeriodicQueueCycle/PeriodicQueueCycleStrategy.cs`
- Modify: `KGIOptionPricing/Strategies/PeriodicQueueCycle/QueueCycleOrderCoordinator.cs`
- Modify: `tests/PeriodicQueueCycleSmokeTests/Program.cs`

**Interfaces:**
- Consumes: shared `PositionSnapshot`, executable BestBid/BestAsk, `StopLossTicks`, `MaxOneLegSeconds`, and three cooldown parameters.
- Produces: correct `StopLoss` versus `Flatten` exits and zero-orders/zero-position completion gates.

- [ ] **Step 1: Add failing long/short fixed-stop tests**

After one side fills, update the fake ledger and feed an adverse healthy Book. For a long, `(AveragePrice-BestBid)/TickSize >= 6`; for a short, `(BestAsk-AveragePrice)/TickSize >= 6`. Assert the remaining completion order is cancelled first and the later exit request has role `StopLoss`.

- [ ] **Step 2: Add failing timeout and stale-book tests**

Use an event-driven fake clock. At 29 seconds no timeout occurs; at 30 seconds the working opposite leg is cancelled and the subsequent exit is `Flatten` with `OneLegTimeout`. For quote loss, assert no price stop is calculated from invalid data, then after `MarketDataRecoverySeconds=2` assert a `Flatten` request with `MarketDataRecoveryTimeout`.

```csharp
Assert(exit.Role == PaperOrderRole.Flatten &&
       strategy.GetSnapshot().Reason.Contains("MarketDataRecoveryTimeout", StringComparison.Ordinal),
    "單邊持倉失去健康行情時必須中性平倉，不得誤標固定停損");
```

- [ ] **Step 3: Implement risk ordering**

Freeze stop price from the ledger average fill. When a risk exit triggers: stop repricing, request targeted cancellation, wait for terminal lifecycle state, recompute net position, then emit exactly one exit request. A cancel/fill race may reduce or eliminate the net position; never submit an exit for zero position.

- [ ] **Step 4: Implement cycle completion and cooldown reset**

Both completion fills plus ledger net zero enter `CycleCooldownSeconds=45`; stop loss enters `StopLossCooldownSeconds=60`; health exit enters `ShockCooldownSeconds=45`. A cooldown ending clears the old direction window and returns to `WarmingUp`; it must not immediately reuse the previous 15-second signal.

- [ ] **Step 5: Test pause and stop protection**

Assert pausing while flat cancels entries, while pausing with a position still processes Book, lifecycle, and exit fills. Assert stop cannot report `Stopped` until working orders and position are zero.

- [ ] **Step 6: Run tests and commit**

Run: `dotnet run --project tests/PeriodicQueueCycleSmokeTests/PeriodicQueueCycleSmokeTests.csproj`

Expected: PASS for long/short stops, exact timeout boundary, data recovery, cooldown reset, pause, and safe stop.

```powershell
git add KGIOptionPricing/Strategies/PeriodicQueueCycle/PeriodicQueueCycleStrategy.cs KGIOptionPricing/Strategies/PeriodicQueueCycle/QueueCycleOrderCoordinator.cs tests/PeriodicQueueCycleSmokeTests/Program.cs
git commit -m "feat: protect periodic queue cycle exposure"
```

### Task 8: Extend shared observability artifacts

**Files:**
- Create: `KGIOptionPricing/StrategyFramework/Storage/StrategyObservationModels.cs`
- Modify: `KGIOptionPricing/StrategyFramework/Storage/SimulationRunRecorder.cs`
- Modify: `KGIOptionPricing/StrategyFramework/StrategyHost.cs`
- Modify: `KGIOptionPricing/StrategyFramework/StrategySimulationService.cs`
- Modify: `tests/StrategyFrameworkSmokeTests/Program.cs`

**Interfaces:**
- Consumes: strategy decisions, lifecycle events, executions, snapshots, run definition, resolved universe, and input archive identity.
- Produces: `run-manifest.json`, `strategy-decisions.jsonl`, `order-lifecycle.jsonl`, enhanced `executions.csv`, `run-summary.json`, and appendable `parameter-experiments.jsonl`.

- [ ] **Step 1: Add failing recorder artifact tests**

Record one accepted direction, two submitted orders, one cancellation, two fills, and one completed trade in a temporary directory. Assert all six artifact names exist, `executions.csv` includes `clientOrderId`, and each decision records observed value, threshold, pass/fail, primary reason, secondary reasons, quote age, spread, and cycle ID.

- [ ] **Step 2: Run and verify artifact failures**

Run: `dotnet run --project tests/StrategyFrameworkSmokeTests/StrategyFrameworkSmokeTests.csproj`

Expected: FAIL because the recorder currently writes only `strategy-events.jsonl` and `executions.csv`.

- [ ] **Step 3: Add observation models and recorder methods**

Define `StrategyDecisionObservation`, `ThresholdObservation`, `RunManifest`, `RunSummary`, and `ParameterExperimentRecord`. Add typed recorder methods rather than strategy-specific JSON writes. Preserve raw market archives and rate-limit unchanged decision heartbeats. Use JSON string enums and `InvariantCulture` CSV formatting.

- [ ] **Step 4: Route common lifecycle and decision events**

Expose host events for `OrderLifecycleOccurred` and `DecisionObserved`; subscribe the recorder in live-shadow and backtest paths. Write the manifest once after universe resolution, finalize summary on replay completion/disposal, and represent unavailable metrics as JSON `null` plus stable reason codes.

- [ ] **Step 5: Run recorder tests and commit**

Run: `dotnet run --project tests/StrategyFrameworkSmokeTests/StrategyFrameworkSmokeTests.csproj`

Expected: PASS; repeated identical decisions are compressed while state/reason changes remain present.

```powershell
git add KGIOptionPricing/StrategyFramework/Storage/StrategyObservationModels.cs KGIOptionPricing/StrategyFramework/Storage/SimulationRunRecorder.cs KGIOptionPricing/StrategyFramework/StrategyHost.cs KGIOptionPricing/StrategyFramework/StrategySimulationService.cs tests/StrategyFrameworkSmokeTests/Program.cs
git commit -m "feat: record reproducible strategy lifecycle artifacts"
```

### Task 9: Register simulation/live strategy and enforce live gates

**Files:**
- Modify: `KGIOptionPricing/StrategyFramework/StrategySimulationService.cs`
- Modify: `KGIOptionPricing/StrategyFramework/LiveStrategyService.cs`
- Modify: `tests/PeriodicQueueCycleSmokeTests/Program.cs`

**Interfaces:**
- Consumes: `PeriodicQueueCycleFactory`, existing product resolver, live login/whitelist/daily-loss controls, and managed live execution.
- Produces: factory availability in both services and a strategy-specific research-validation gate.

- [ ] **Step 1: Add registration and validation tests**

Assert both registries expose display name `週期雙邊完成單`. Extract the research gate to an internal pure method callable by the smoke-test assembly and assert missing/false `ResearchValidated` is rejected while true passes only for `PeriodicQueueCycle`.

- [ ] **Step 2: Run and confirm failure**

Run: `dotnet run --project tests/PeriodicQueueCycleSmokeTests/PeriodicQueueCycleSmokeTests.csproj`

Expected: FAIL because the factory is not registered and no live gate exists for it.

- [ ] **Step 3: Register and gate the strategy**

Add `new PeriodicQueueCycleFactory()` to both registries. Replace the Mini-only inline validation with a named policy that requires `ResearchValidated=true` for both research strategies without weakening login, `225MC`, hot-month, one-lot, or daily-loss checks.

- [ ] **Step 4: Add the shutdown review-focus test**

With a fake managed execution service, put one order in `CancelPending` and position zero. Assert the close status remains pending and shutdown warnings remain nonempty; only a later `Cancelled` event and zero broker count permit `Stopped`.

- [ ] **Step 5: Run tests and commit**

Run:

```powershell
dotnet run --project tests/PeriodicQueueCycleSmokeTests/PeriodicQueueCycleSmokeTests.csproj
dotnet run --project tests/StrategyFrameworkSmokeTests/StrategyFrameworkSmokeTests.csproj
```

Expected: PASS for registration, research validation, and safe-close gating.

```powershell
git add KGIOptionPricing/StrategyFramework/StrategySimulationService.cs KGIOptionPricing/StrategyFramework/LiveStrategyService.cs tests/PeriodicQueueCycleSmokeTests/Program.cs
git commit -m "feat: register periodic queue cycle services"
```

### Task 10: Add dedicated simulation and live UI tabs

**Files:**
- Modify: `KGIOptionPricing/StrategyCenterForm.cs`
- Modify: `KGIOptionPricing/LiveStrategyCenterForm.cs`
- Create: `KGIOptionPricing/PeriodicQueueCycleControls.cs`
- Modify: `tests/PeriodicQueueCycleSmokeTests/Program.cs`

**Interfaces:**
- Consumes: service factory lists, `StrategyDefinition`, snapshots, executions, and all strategy parameters.
- Produces: fixed-width `週期雙邊完成單` tabs in simulation and red live centers.

- [ ] **Step 1: Add form-construction smoke tests**

Instantiate both forms on an STA thread without showing them. Recursively locate a tab named `週期雙邊完成單`; assert the first actionable control is the start button, NumericUpDown controls have the required ranges before assigned defaults, all requested parameters are present, and the live tab has confirmation, daily loss, and `ResearchValidated` controls.

- [ ] **Step 2: Run and verify missing-tab failure**

Run: `dotnet run --project tests/PeriodicQueueCycleSmokeTests/PeriodicQueueCycleSmokeTests.csproj`

Expected: FAIL because neither form contains the dedicated tab.

- [ ] **Step 3: Build shared fixed-width parameter controls**

`PeriodicQueueCycleControls` creates labeled controls and returns a complete invariant parameter dictionary. Set `Minimum`, `Maximum`, and `DecimalPlaces` before `Value`. Keep simulation and live definitions identical except for live confirmation/risk controls.

- [ ] **Step 4: Add both tabs and monitoring grids**

Show lifecycle state/reason, direction features, frozen anchor/bounds, each side's client/order ID, generation, price, ahead quantity, filled/remaining quantity, cooldown, and common PnL metrics. Execution grids show time, instance, symbol, side, role, actual price, quantity, trigger, fee, sequence, and client ID. Display unavailable performance fields as `--`.

- [ ] **Step 5: Run UI smoke tests and commit**

Run: `dotnet run --project tests/PeriodicQueueCycleSmokeTests/PeriodicQueueCycleSmokeTests.csproj`

Expected: PASS without opening a window or contacting the broker.

```powershell
git add KGIOptionPricing/PeriodicQueueCycleControls.cs KGIOptionPricing/StrategyCenterForm.cs KGIOptionPricing/LiveStrategyCenterForm.cs tests/PeriodicQueueCycleSmokeTests/Program.cs
git commit -m "feat: add periodic queue cycle tabs"
```

### Task 11: Add the strategy-specific Python analysis entry

**Files:**
- Create: `KGIOptionPricing/Strategies/PeriodicQueueCycle/analysis/analyze_periodic_queue_cycle.py`
- Create: `KGIOptionPricing/Strategies/PeriodicQueueCycle/analysis/test_analyze_periodic_queue_cycle.py`

**Interfaces:**
- Consumes: shared manifest, decisions, order lifecycle, executions, summary, and parameter experiment artifacts.
- Produces: CLI modes `logic`, `parameters`, `all`; `analysis-report.json`; Traditional Chinese `analysis-report.md`; and `parameter-ranking.csv` when two or more valid candidates exist.

- [ ] **Step 1: Write failing Python tests with temporary fixtures**

Use only `unittest`, `tempfile`, `json`, `csv`, and `pathlib`. Cover complete input, missing/corrupt files, wrong strategy type/version, incompatible manifests, overlapping data splits, insufficient samples, boundary candidates, high-profit candidates that fail drawdown/data-quality guards, and deterministic output.

```python
def test_failed_guardrail_cannot_rank_first(self):
    report = run_analysis(self.runs, self.output, mode="parameters")
    self.assertNotEqual(report["parameterRanking"][0]["experimentId"], "high-profit-high-drawdown")

def test_repeat_run_is_deterministic(self):
    first = normalized_report(run_analysis(self.runs, self.output1, mode="all"))
    second = normalized_report(run_analysis(self.runs, self.output2, mode="all"))
    self.assertEqual(first, second)
```

- [ ] **Step 2: Run and verify tests fail**

Run: `python KGIOptionPricing/Strategies/PeriodicQueueCycle/analysis/test_analyze_periodic_queue_cycle.py`

Expected: FAIL because the analyzer module is missing.

- [ ] **Step 3: Implement validation and logic diagnostics**

Validate schema, strategy type, parameter hash, archive hash, product mapping, fees, execution/latency/slippage assumptions, and nonoverlapping splits. Produce signal funnel, reason counts, direction feature distributions, order/fill/cancel/timeout latency, exit roles, MFE/MAE, hold times, and separate data defects from strategy filters.

- [ ] **Step 4: Implement guarded parameter ranking**

Compare only compatible experiments; apply minimum completed trades, data quality, maximum drawdown, net-after-cost, and 0/1/2 adverse-tick slippage guards before ranking. Analyze the specified stop, timeout, window, OBI, and chase grids; report stable ranges rather than only one best point.

- [ ] **Step 5: Implement Traditional Chinese and JSON outputs**

The Markdown sections are exactly `觀察事實`, `分析推論`, `邏輯修改建議`, `參數建議`, and `仍需驗證`. Every recommendation includes run/experiment IDs and supporting reason/threshold/metric. Invalid or insufficient evidence returns a nonzero CLI exit and does not emit a misleading ranking.

- [ ] **Step 6: Run tests and commit**

Run: `python KGIOptionPricing/Strategies/PeriodicQueueCycle/analysis/test_analyze_periodic_queue_cycle.py`

Expected: PASS with identical normalized reports across repeated runs.

```powershell
git add KGIOptionPricing/Strategies/PeriodicQueueCycle/analysis
git commit -m "feat: analyze periodic queue cycle runs"
```

### Task 12: End-to-end replay, regression, and build verification

**Files:**
- Modify: `tests/PeriodicQueueCycleSmokeTests/Program.cs`
- Create from test execution: `KGIOptionPricing/Research/Analysis/periodic-queue-cycle-baseline/analysis-report.json`
- Create from test execution: `KGIOptionPricing/Research/Analysis/periodic-queue-cycle-baseline/analysis-report.md`

**Interfaces:**
- Consumes: the complete strategy, existing NikkeiResearch archive, replay service, shared artifacts, and analyzer.
- Produces: deterministic end-to-end evidence and clean Debug/Release builds without broker transmission.

- [ ] **Step 1: Add a synthetic deterministic replay fixture**

Generate a short archive in the smoke test containing warmup, upward acceptance, two independent working orders, one fill, a completed opposite fill, cooldown, a one-leg timeout cycle, and a fixed-stop cycle. Assert exact cycle, role, order-lifecycle, and completed-trade counts.

- [ ] **Step 2: Run the focused C# and Python suites**

Run:

```powershell
dotnet run --project tests/PeriodicQueueCycleSmokeTests/PeriodicQueueCycleSmokeTests.csproj
dotnet run --project tests/StrategyFrameworkSmokeTests/StrategyFrameworkSmokeTests.csproj
dotnet run --project tests/QueueFollowStrategySmokeTests/QueueFollowStrategySmokeTests.csproj
dotnet run --project tests/OrderManagementSmokeTests/OrderManagementSmokeTests.csproj
python KGIOptionPricing/Strategies/PeriodicQueueCycle/analysis/test_analyze_periodic_queue_cycle.py
```

Expected: every suite prints its pass message; existing queue-follow behavior remains unchanged.

- [ ] **Step 3: Run a research-only replay against healthy NikkeiResearch data**

Run the strategy with `ResearchValidated=false`, write a fresh run directory, then invoke:

```powershell
python KGIOptionPricing/Strategies/PeriodicQueueCycle/analysis/analyze_periodic_queue_cycle.py --runs KGIOptionPricing/Research/StrategyRuns --output KGIOptionPricing/Research/Analysis/periodic-queue-cycle-baseline --mode all
```

Expected: reports clearly label provisional evidence, list data coverage and missing evidence, and do not alter C# defaults or start live execution.

- [ ] **Step 4: Build Debug and Release**

Run:

```powershell
dotnet build KGIOptionPricing.sln -c Debug --no-restore
dotnet build KGIOptionPricing.sln -c Release --no-restore
```

Expected: both builds succeed; report existing warnings separately from any warning introduced by this work.

- [ ] **Step 5: Inspect the final diff and safety invariants**

Run:

```powershell
git diff --check
git status --short
rg -n "OrderManager|KGITradeService" KGIOptionPricing/Strategies/PeriodicQueueCycle
```

Expected: `git diff --check` is clean; the strategy folder contains no direct broker references; unrelated dirty files remain unstaged.

- [ ] **Step 6: Commit end-to-end tests and verified research outputs**

```powershell
git add tests/PeriodicQueueCycleSmokeTests/Program.cs KGIOptionPricing/Research/Analysis/periodic-queue-cycle-baseline
git commit -m "test: verify periodic queue cycle end to end"
```

Do not add generated `bin`, `obj`, temporary archives, credentials, or unrelated workspace changes.
