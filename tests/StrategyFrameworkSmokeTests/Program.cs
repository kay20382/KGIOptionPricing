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
using System.Threading;
using System.Threading.Tasks;
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

var pausedFillBroker = new CrossPricePaperExecutionService();
var pausedFillProbe = new ManagedProbeStrategy("managed-paused-fill", "MGP");
using (var pausedFillRunner = new StrategyInstanceRunner(
    pausedFillProbe,
    new ResolvedStrategyUniverse { Tradable = Product("225MC", "MGP", 5m, 10m) },
    new PaperPortfolioLedger(), new SystemStrategyClock(), 0m, pausedFillBroker))
{
    pausedFillProbe.Request("paused-buy-g1", BuySell.Buy, 100m);
    Assert(pausedFillRunner.Pause(), "持有工作單的 managed strategy 應可暫停");
    pausedFillRunner.OnMarketEvent(Trade("MGP", "225MC", 95m, 4, DateTimeOffset.UtcNow));
    Assert(pausedFillProbe.Executions.Count == 1 && pausedFillProbe.MarketEvents.Count == 1,
        "Runner 暫停期間 broker 先成交後，更新後部位非零時仍必須把同一行情交給策略保護路徑");
}

DateTimeOffset lifecycleT0 = new(2026, 9, 22, 0, 0, 0, TimeSpan.Zero);
DateTimeOffset lifecycleT1 = lifecycleT0.AddSeconds(7);
var deterministicLifecycleRuns = new List<OrderLifecycleEvent[]>();
for (int run = 0; run < 2; run++)
{
    var lifecycleClock = new MutableStrategyClock(lifecycleT0);
    var lifecycleBroker = new CrossPricePaperExecutionService(clock: lifecycleClock);
    var lifecycleProbe = new ManagedProbeStrategy("managed-clock", "MGC");
    using var lifecycleRunner = new StrategyInstanceRunner(
        lifecycleProbe,
        new ResolvedStrategyUniverse { Tradable = Product("225MC", "MGC", 5m, 10m) },
        new PaperPortfolioLedger(), lifecycleClock, 0m, lifecycleBroker);
    lifecycleProbe.Request("clock-g1", BuySell.Buy, 100m);
    Assert(lifecycleProbe.Lifecycle.Take(2).All(x => x.Timestamp == lifecycleT0),
        "Submitted 與 Working 必須使用策略 clock 的送單時間");
    lifecycleClock.Advance(lifecycleT1);
    lifecycleProbe.Cancel("clock-g1");
    Assert(lifecycleProbe.Lifecycle.Skip(2).All(x => x.Timestamp == lifecycleT1),
        "CancelPending 與 Cancelled 必須使用策略 clock 的撤單時間");
    deterministicLifecycleRuns.Add(lifecycleProbe.Lifecycle.ToArray());
}
Assert(deterministicLifecycleRuns[0].SequenceEqual(deterministicLifecycleRuns[1]),
    "相同 clock 與委託序列必須產生完全一致的生命週期事件");

DateTimeOffset unroutedAt = new(2026, 9, 22, 0, 30, 0, TimeSpan.Zero);
var unroutedClock = new MutableStrategyClock(unroutedAt);
var unroutedBroker = new TrackingManagedExecutionService();
var unroutedProbe = new ManagedProbeStrategy("managed-unrouted", "MGU");
using (var unroutedRunner = new StrategyInstanceRunner(
    unroutedProbe,
    new ResolvedStrategyUniverse { Tradable = Product("225MC", "MGU", 5m, 10m) },
    new PaperPortfolioLedger(), unroutedClock, 0m, unroutedBroker))
{
    unroutedProbe.Cancel("missing-g1");
    OrderLifecycleEvent failure = unroutedProbe.Lifecycle.Single();
    Assert(failure.StrategyInstanceId == "managed-unrouted" &&
           failure.ClientOrderId == "missing-g1" &&
           failure.Timestamp == unroutedAt &&
           failure.State == OrderLifecycleState.CancelPending &&
           failure.ReasonCode == "CancelRequestNotRouted",
        "managed targeted cancel 無法路由時 Runner 必須明確回送失敗 lifecycle，不得丟棄 bool=false 或偽造 Cancelled");
}

var silentStopBroker = new TrackingManagedExecutionService();
var silentStopProbe = new ManagedProbeStrategy("managed-silent-stop", "MGS", cancelAllOnStop: false);
using (var silentStopRunner = new StrategyInstanceRunner(
    silentStopProbe,
    new ResolvedStrategyUniverse { Tradable = Product("225MC", "MGS", 5m, 10m) },
    new PaperPortfolioLedger(), new SystemStrategyClock(), 0m, silentStopBroker))
{
    silentStopProbe.Request("silent-stop-g1", BuySell.Buy, 100m);
    silentStopRunner.Stop("generic managed stop");
    Assert(silentStopBroker.CancelAllCount == 1 &&
           silentStopBroker.GetWorkingOrders("managed-silent-stop").Count == 0,
        "managed strategy 若宣告 Stopped 卻未自行撤掉工作單，Runner 必須 fail-closed 補做 instance 全撤");
}

var detachedBroker = new TrackingManagedExecutionService();
var detachedProbe = new ManagedProbeStrategy("managed-detached", "MGD");
var detachedRunner = new StrategyInstanceRunner(
    detachedProbe,
    new ResolvedStrategyUniverse { Tradable = Product("225MC", "MGD", 5m, 10m) },
    new PaperPortfolioLedger(), new SystemStrategyClock(), 0m, detachedBroker);
int detachedSnapshots = 0;
detachedRunner.SnapshotChanged += _ => detachedSnapshots++;
detachedRunner.Dispose();
int submitsAfterDispose = detachedBroker.SubmitCount;
int cancelAllAfterDispose = detachedBroker.CancelAllCount;
int snapshotsAfterDispose = detachedSnapshots;
detachedProbe.Request("after-dispose", BuySell.Buy, 100m);
detachedProbe.CancelAll();
detachedProbe.PublishSnapshot();
Assert(detachedBroker.SubmitCount == submitsAfterDispose &&
       detachedBroker.CancelAllCount == cancelAllAfterDispose &&
       detachedSnapshots == snapshotsAfterDispose,
    "Runner 釋放後必須解除策略送單、全撤與快照事件訂閱");
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

var marketBroker = new CrossPricePaperExecutionService();
var marketFills = new List<ExecutionEvent>();
marketBroker.Filled += marketFills.Add;
marketBroker.Submit(new PaperOrderRequest
{
    StrategyInstanceId = "market-price", ClientOrderId = "market-price-1", Symbol = "MC",
    Side = BuySell.Sell, Type = PaperOrderType.Market, Role = PaperOrderRole.Flatten,
    Price = 100m, Quantity = 1
}, DateTimeOffset.UtcNow);
marketBroker.OnMarketEvent(Trade("MC", "225MC", 75m, 3, DateTimeOffset.UtcNow));
Assert(marketFills.Count == 1 && marketFills[0].Price == 75m,
    "模擬 Market 單必須按下一筆實際 Trade 價成交，不得沿用請求參考價");

DateTimeOffset heartbeatTime = new(2026, 9, 23, 0, 0, 0, TimeSpan.Zero);
var heartbeatClock = new MutableStrategyClock(heartbeatTime);
var heartbeatSource = new FakeSource();
using (var heartbeatHost = new StrategyHost(heartbeatSource, heartbeatClock))
{
    var heartbeatProbe = new HeartbeatProbeStrategy("heartbeat-probe");
    heartbeatHost.Add(heartbeatProbe,
        new ResolvedStrategyUniverse { Tradable = Product("225MC", "HBP", 5m, 10m) });
    heartbeatHost.Pulse(heartbeatTime.AddSeconds(1));
    Assert(heartbeatProbe.Heartbeats.SequenceEqual(new[] { heartbeatTime.AddSeconds(1) }) &&
           heartbeatClock.Now == heartbeatTime.AddSeconds(1),
        "StrategyHost.Pulse 必須以事件時鐘驅動 optional heartbeat strategy，測試不依賴 sleep");
}

var throwingSubscribeSource = new TransactionalSource { ThrowOnSubscribe = true };
using (var transactionalHost = new StrategyHost(throwingSubscribeSource))
{
    AssertThrows<InvalidOperationException>(() => transactionalHost.Add(
            new HeartbeatProbeStrategy("subscribe-throws"),
            new ResolvedStrategyUniverse { Tradable = Product("225MC", "TXS", 5m, 10m) }),
        "source.Subscribe 失敗時 Add 必須失敗");
    Assert(transactionalHost.GetSnapshots().Count == 0,
        "source.Subscribe 失敗不得留下 runner");
}
using (var startFailureHost = new StrategyHost(new FakeSource()))
{
    AssertThrows<InvalidOperationException>(() => startFailureHost.Add(
            new ThrowingStartStrategy("start-throws"),
            new ResolvedStrategyUniverse { Tradable = Product("225MC", "TXT", 5m, 10m) }),
        "strategy.Start 失敗時 Add 必須失敗");
    Assert(startFailureHost.GetSnapshots().Count == 0, "Start 失敗不得留下 runner");
}
var selfDisposeSource = new TransactionalSource();
using (var selfDisposeHost = new StrategyHost(selfDisposeSource))
{
    selfDisposeSource.Host = selfDisposeHost;
    selfDisposeHost.SnapshotChanged += _ => throw new InvalidOperationException("subscriber failure");
    StrategyInstanceRunner added = selfDisposeHost.Add(
        new HeartbeatProbeStrategy("self-dispose-add"),
        new ResolvedStrategyUniverse { Tradable = Product("225MC", "TXD", 5m, 10m) });
    Assert(selfDisposeSource.SelfDisposeFailure is InvalidOperationException &&
           selfDisposeHost.GetSnapshots().Count == 1 && added.InstanceId == "self-dispose-add",
        "Add owner thread self-dispose 必須 fail closed，初始 snapshot subscriber 例外不得回滾成功 Add");
}

var serializedClock = new MutableStrategyClock(heartbeatTime);
var serializedLedger = new PaperPortfolioLedger();
var serializedBroker = new ConcurrentCallbackExecutionService();
var serializedProbe = new BlockingHeartbeatManagedStrategy("serialized-heartbeat", serializedLedger, "HBS");
using (var serializedRunner = new StrategyInstanceRunner(
    serializedProbe,
    new ResolvedStrategyUniverse { Tradable = Product("225MC", "HBS", 5m, 10m) },
    serializedLedger,
    serializedClock,
    0m,
    serializedBroker))
{
    Task pulse = Task.Factory.StartNew(
        () => serializedRunner.Pulse(heartbeatTime.AddSeconds(1)),
        CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    Assert(serializedProbe.HeartbeatEntered.Wait(TimeSpan.FromSeconds(5)),
        "測試前置：heartbeat 必須進入阻塞區段");
    Task lifecycleCallback = Task.Factory.StartNew(() => serializedBroker.RaiseLifecycle(new OrderLifecycleEvent
    {
        StrategyInstanceId = serializedProbe.InstanceId, ClientOrderId = "entry-1",
        Timestamp = heartbeatTime.AddSeconds(1), State = OrderLifecycleState.Filled,
        Quantity = 1, FilledQuantity = 1
    }), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    Task fillCallback = Task.Factory.StartNew(() => serializedBroker.RaiseFill(new ExecutionEvent
    {
        StrategyInstanceId = serializedProbe.InstanceId, ClientOrderId = "entry-1",
        Symbol = "HBS", Side = BuySell.Buy, Role = PaperOrderRole.EntryLong,
        Price = 100m, Quantity = 1, Timestamp = heartbeatTime.AddSeconds(1)
    }), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    bool callbackEnteredWhileHeartbeatBlocked =
        serializedProbe.BrokerCallbackEntered.Wait(TimeSpan.FromMilliseconds(100));
    int positionWhileHeartbeatBlocked =
        serializedLedger.GetPosition(serializedProbe.InstanceId, "HBS").NetQuantity;
    serializedProbe.ReleaseHeartbeat.Set();
    Assert(Task.WaitAll(new[] { pulse, lifecycleCallback, fillCallback }, TimeSpan.FromSeconds(5)),
        "heartbeat 釋放後 broker callbacks 必須完成");
    Assert(!callbackEnteredWhileHeartbeatBlocked && positionWhileHeartbeatBlocked == 0,
        "heartbeat 尚未釋放時 broker Filled/lifecycle 不得並行進入策略或先更新帳本");
    Assert(serializedProbe.MaxConcurrentCallbacks == 1 &&
           serializedProbe.LifecycleCount == 1 && serializedProbe.ExecutionCount == 1 &&
           serializedProbe.PositionObservedInsideExecution == 1,
        "Runner 必須序列化 heartbeat/lifecycle/execution，且 ledger apply 必須先於 strategy execution callback");
}

Type heartbeatTimerType = typeof(StrategyHost).Assembly.GetType(
    "KGIOptionPricing.StrategyFramework.StrategyHeartbeatTimer", throwOnError: true)!;
using var timerCallbackEntered = new ManualResetEventSlim(false);
using var releaseTimerCallback = new ManualResetEventSlim(false);
Action<DateTimeOffset> blockingPulse = _ =>
{
    timerCallbackEntered.Set();
    releaseTimerCallback.Wait();
};
var heartbeatTimer = (IDisposable)Activator.CreateInstance(
    heartbeatTimerType,
    BindingFlags.Instance | BindingFlags.NonPublic,
    binder: null,
    args: new object?[] { blockingPulse, TimeSpan.FromMilliseconds(1) },
    culture: null)!;
Assert(timerCallbackEntered.Wait(TimeSpan.FromSeconds(5)), "測試前置：timer callback 必須開始執行");
Task disposeHeartbeatTimer = Task.Run(heartbeatTimer.Dispose);
bool timerDisposeReturnedBeforeCallback = disposeHeartbeatTimer.Wait(TimeSpan.FromMilliseconds(100));
releaseTimerCallback.Set();
Assert(disposeHeartbeatTimer.Wait(TimeSpan.FromSeconds(5)),
    "timer callback 排空後 Dispose 必須完成，供 SimulationService 依 timer→host 順序安全釋放");
Assert(!timerDisposeReturnedBeforeCallback,
    "timer callback 尚未結束時 Dispose 不得先返回並讓 host 被釋放");

using var selfDisposeEntered = new ManualResetEventSlim(false);
using var allowSelfDispose = new ManualResetEventSlim(false);
using var selfDisposeFinished = new ManualResetEventSlim(false);
IDisposable? selfDisposingTimer = null;
Exception? selfDisposeFailure = null;
Action<DateTimeOffset> selfDisposingPulse = _ =>
{
    selfDisposeEntered.Set();
    allowSelfDispose.Wait();
    try { selfDisposingTimer!.Dispose(); }
    catch (Exception exception) { selfDisposeFailure = exception; }
    finally { selfDisposeFinished.Set(); }
};
selfDisposingTimer = (IDisposable)Activator.CreateInstance(
    heartbeatTimerType,
    BindingFlags.Instance | BindingFlags.NonPublic,
    binder: null,
    args: new object?[] { selfDisposingPulse, TimeSpan.FromMilliseconds(1) },
    culture: null)!;
Assert(selfDisposeEntered.Wait(TimeSpan.FromSeconds(5)), "測試前置：self-dispose heartbeat 必須開始");
allowSelfDispose.Set();
Assert(selfDisposeFinished.Wait(TimeSpan.FromSeconds(5)) && selfDisposeFailure is InvalidOperationException,
    "heartbeat callback 同步 Dispose 必須 fail closed，不得回傳成功後繼續拆 host");
selfDisposingTimer.Dispose();

var retryBroker = new FirstDisposeFailsExecutionService();
var retryStrategy = new DisposalProbeStrategy("dispose-retry", "DRY");
var retryLedger = new PaperPortfolioLedger();
var retryRunner = new StrategyInstanceRunner(
    retryStrategy,
    new ResolvedStrategyUniverse { Tradable = Product("225MC", "DRY", 5m, 10m) },
    retryLedger, new MutableStrategyClock(heartbeatTime), 0m, retryBroker);
Task firstDispose = Task.Factory.StartNew(retryRunner.Dispose, CancellationToken.None,
    TaskCreationOptions.LongRunning, TaskScheduler.Default);
Assert(retryBroker.DisposeEntered.Wait(TimeSpan.FromSeconds(5)), "測試前置：第一次 broker Dispose 必須阻塞");
Task waitingDispose = Task.Factory.StartNew(retryRunner.Dispose, CancellationToken.None,
    TaskCreationOptions.LongRunning, TaskScheduler.Default);
retryBroker.ReleaseDispose.Set();
try { Task.WaitAll(new[] { firstDispose, waitingDispose }, TimeSpan.FromSeconds(5)); } catch (AggregateException) { }
Assert(firstDispose.IsFaulted && waitingDispose.IsFaulted && retryBroker.DisposeCalls == 1,
    "同一 Dispose attempt 的 owner/waiter 必須收到相同失敗，不得把 waiter 誤認成功");
retryRunner.Pulse(heartbeatTime.AddSeconds(2));
retryBroker.RaiseFill(new ExecutionEvent
{
    StrategyInstanceId = retryStrategy.InstanceId, Symbol = "DRY", Side = BuySell.Buy,
    Role = PaperOrderRole.EntryLong, Price = 100m, Quantity = 1
});
Assert(retryStrategy.HeartbeatCount == 1 && retryStrategy.ExecutionCount == 1 &&
       retryLedger.GetPosition(retryStrategy.InstanceId, "DRY").NetQuantity == 1,
    "Dispose 失敗後必須復原 disposing，heartbeat 與 late fill 路由仍保持有效");
retryBroker.FailDispose = false;
retryRunner.Dispose();

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

static void AssertThrows<TException>(Action action, string message) where TException : Exception
{
    try { action(); }
    catch (TException) { return; }
    throw new InvalidOperationException(message);
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

sealed class TransactionalSource : IMarketEventSource
{
    public bool ThrowOnSubscribe { get; set; }
    public StrategyHost? Host { get; set; }
    public Exception? SelfDisposeFailure { get; private set; }
    public event Action<MarketEvent>? MarketEventReceived { add { } remove { } }
    public void Subscribe(IEnumerable<ResolvedProduct> products)
    {
        if (ThrowOnSubscribe) throw new InvalidOperationException("subscribe failed");
        if (Host != null)
        {
            try { Host.Dispose(); }
            catch (Exception exception) { SelfDisposeFailure = exception; }
        }
    }
    public void Dispose() { }
}

sealed class ThrowingStartStrategy : ISimulationStrategy
{
    public ThrowingStartStrategy(string instanceId) => InstanceId = instanceId;
    public string StrategyType => "ThrowingStart";
    public string InstanceId { get; }
    public StrategyLifecycleState State => StrategyLifecycleState.Created;
    public event Action<PaperOrderRequest>? OrderRequested { add { } remove { } }
    public event Action? CancelAllRequested { add { } remove { } }
    public event Action<StrategySnapshot>? SnapshotChanged { add { } remove { } }
    public void Start(StrategyRuntimeContext context) => throw new InvalidOperationException("start failed");
    public void OnMarketEvent(MarketEvent marketEvent) { }
    public void OnExecution(ExecutionEvent execution) { }
    public void Pause(string reason) { }
    public void Resume() { }
    public void Stop(string reason) { }
    public StrategySnapshot GetSnapshot() => new() { StrategyType = StrategyType, InstanceId = InstanceId };
}

sealed class ManagedProbeStrategy : ISimulationStrategy, IManagedOrderStrategy
{
    private readonly string _symbol;
    private readonly bool _cancelAllOnStop;

    public ManagedProbeStrategy(string instanceId, string? symbol = null, bool cancelAllOnStop = true)
    {
        InstanceId = instanceId;
        _symbol = symbol ?? (instanceId == "managed-fill" ? "MGF" : "MGR");
        _cancelAllOnStop = cancelAllOnStop;
    }

    public string StrategyType => "ManagedProbe";
    public string InstanceId { get; }
    public StrategyLifecycleState State { get; private set; } = StrategyLifecycleState.Created;
    public List<OrderLifecycleEvent> Lifecycle { get; } = new();
    public List<ExecutionEvent> Executions { get; } = new();
    public List<MarketEvent> MarketEvents { get; } = new();

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
    public void CancelAll() => CancelAllRequested?.Invoke();
    public void PublishSnapshot() => SnapshotChanged?.Invoke(GetSnapshot());
    public void OnOrderLifecycle(OrderLifecycleEvent orderEvent) => Lifecycle.Add(orderEvent);
    public void Start(StrategyRuntimeContext context)
    {
        State = StrategyLifecycleState.Ready;
        SnapshotChanged?.Invoke(GetSnapshot());
    }
    public void OnMarketEvent(MarketEvent marketEvent) => MarketEvents.Add(marketEvent);
    public void OnExecution(ExecutionEvent execution) => Executions.Add(execution);
    public void Pause(string reason) => State = StrategyLifecycleState.Suspended;
    public void Resume() => State = StrategyLifecycleState.Ready;
    public void Stop(string reason)
    {
        if (_cancelAllOnStop) CancelAllRequested?.Invoke();
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

sealed class HeartbeatProbeStrategy : ISimulationStrategy, IStrategyHeartbeat
{
    public HeartbeatProbeStrategy(string instanceId) => InstanceId = instanceId;
    public string StrategyType => "HeartbeatProbe";
    public string InstanceId { get; }
    public StrategyLifecycleState State { get; private set; } = StrategyLifecycleState.Created;
    public List<DateTimeOffset> Heartbeats { get; } = new();
    public event Action<PaperOrderRequest>? OrderRequested { add { } remove { } }
    public event Action? CancelAllRequested { add { } remove { } }
    public event Action<StrategySnapshot>? SnapshotChanged { add { } remove { } }
    public void Start(StrategyRuntimeContext context) => State = StrategyLifecycleState.Ready;
    public void OnHeartbeat(DateTimeOffset now) => Heartbeats.Add(now);
    public void OnMarketEvent(MarketEvent marketEvent) { }
    public void OnExecution(ExecutionEvent execution) { }
    public void Pause(string reason) => State = StrategyLifecycleState.Suspended;
    public void Resume() => State = StrategyLifecycleState.Ready;
    public void Stop(string reason) => State = StrategyLifecycleState.Stopped;
    public StrategySnapshot GetSnapshot() => new()
    {
        StrategyType = StrategyType, InstanceId = InstanceId, State = State, UpdatedAt = DateTimeOffset.UtcNow
    };
}

sealed class BlockingHeartbeatManagedStrategy : ISimulationStrategy, IManagedOrderStrategy, IStrategyHeartbeat
{
    private readonly PaperPortfolioLedger _ledger;
    private readonly string _symbol;
    private int _activeCallbacks;

    public BlockingHeartbeatManagedStrategy(string instanceId, PaperPortfolioLedger ledger, string symbol)
    {
        InstanceId = instanceId;
        _ledger = ledger;
        _symbol = symbol;
    }

    public string StrategyType => "BlockingHeartbeatManaged";
    public string InstanceId { get; }
    public StrategyLifecycleState State { get; private set; } = StrategyLifecycleState.Created;
    public ManualResetEventSlim HeartbeatEntered { get; } = new(false);
    public ManualResetEventSlim ReleaseHeartbeat { get; } = new(false);
    public ManualResetEventSlim BrokerCallbackEntered { get; } = new(false);
    public int MaxConcurrentCallbacks { get; private set; }
    public int LifecycleCount { get; private set; }
    public int ExecutionCount { get; private set; }
    public int PositionObservedInsideExecution { get; private set; }
    public event Action<PaperOrderRequest>? OrderRequested { add { } remove { } }
    public event Action? CancelAllRequested { add { } remove { } }
    public event Action<StrategySnapshot>? SnapshotChanged { add { } remove { } }
    public event Action<string>? CancelOrderRequested { add { } remove { } }
    public void Start(StrategyRuntimeContext context) => State = StrategyLifecycleState.Ready;
    public void OnHeartbeat(DateTimeOffset now)
    {
        EnterCallback();
        HeartbeatEntered.Set();
        ReleaseHeartbeat.Wait();
        ExitCallback();
    }
    public void OnOrderLifecycle(OrderLifecycleEvent orderEvent)
    {
        EnterCallback();
        BrokerCallbackEntered.Set();
        LifecycleCount++;
        ExitCallback();
    }
    public void OnExecution(ExecutionEvent execution)
    {
        EnterCallback();
        BrokerCallbackEntered.Set();
        PositionObservedInsideExecution = _ledger.GetPosition(InstanceId, _symbol).NetQuantity;
        ExecutionCount++;
        ExitCallback();
    }
    public void OnMarketEvent(MarketEvent marketEvent) { }
    public void Pause(string reason) => State = StrategyLifecycleState.Suspended;
    public void Resume() => State = StrategyLifecycleState.Ready;
    public void Stop(string reason) => State = StrategyLifecycleState.Stopped;
    public StrategySnapshot GetSnapshot() => new()
    {
        StrategyType = StrategyType, InstanceId = InstanceId, State = State, UpdatedAt = DateTimeOffset.UtcNow
    };

    private void EnterCallback()
    {
        int active = Interlocked.Increment(ref _activeCallbacks);
        MaxConcurrentCallbacks = Math.Max(MaxConcurrentCallbacks, active);
    }
    private void ExitCallback() => Interlocked.Decrement(ref _activeCallbacks);
}

sealed class ConcurrentCallbackExecutionService : IManagedStrategyExecutionService
{
    public event Action<ExecutionEvent>? Filled;
    public event Action<string>? Failed { add { } remove { } }
    public event Action<OrderLifecycleEvent>? OrderLifecycleChanged;
    public void RaiseFill(ExecutionEvent execution) => Filled?.Invoke(execution);
    public void RaiseLifecycle(OrderLifecycleEvent orderEvent) => OrderLifecycleChanged?.Invoke(orderEvent);
    public PaperOrder Submit(PaperOrderRequest request, DateTimeOffset now) => throw new NotSupportedException();
    public bool Cancel(string orderId) => false;
    public int CancelAll(string strategyInstanceId) => 0;
    public bool CancelByClientOrderId(string strategyInstanceId, string clientOrderId) => false;
    public IReadOnlyList<PaperOrder> GetWorkingOrders(string strategyInstanceId) => Array.Empty<PaperOrder>();
    public void OnMarketEvent(MarketEvent marketEvent) { }
}

sealed class DisposalProbeStrategy : ISimulationStrategy, IManagedOrderStrategy, IStrategyHeartbeat
{
    public DisposalProbeStrategy(string instanceId, string symbol) { InstanceId = instanceId; }
    public string StrategyType => "DisposalProbe";
    public string InstanceId { get; }
    public StrategyLifecycleState State { get; private set; } = StrategyLifecycleState.Created;
    public int HeartbeatCount { get; private set; }
    public int ExecutionCount { get; private set; }
    public event Action<PaperOrderRequest>? OrderRequested { add { } remove { } }
    public event Action? CancelAllRequested { add { } remove { } }
    public event Action<StrategySnapshot>? SnapshotChanged { add { } remove { } }
    public event Action<string>? CancelOrderRequested { add { } remove { } }
    public void Start(StrategyRuntimeContext context) => State = StrategyLifecycleState.Ready;
    public void OnHeartbeat(DateTimeOffset now) => HeartbeatCount++;
    public void OnOrderLifecycle(OrderLifecycleEvent orderEvent) { }
    public void OnMarketEvent(MarketEvent marketEvent) { }
    public void OnExecution(ExecutionEvent execution) => ExecutionCount++;
    public void Pause(string reason) => State = StrategyLifecycleState.Suspended;
    public void Resume() => State = StrategyLifecycleState.Ready;
    public void Stop(string reason) => State = StrategyLifecycleState.Stopped;
    public StrategySnapshot GetSnapshot() => new() { StrategyType = StrategyType, InstanceId = InstanceId, State = State };
}

sealed class FirstDisposeFailsExecutionService : IManagedStrategyExecutionService, IDisposable
{
    public ManualResetEventSlim DisposeEntered { get; } = new(false);
    public ManualResetEventSlim ReleaseDispose { get; } = new(false);
    public bool FailDispose { get; set; } = true;
    public int DisposeCalls;
    public event Action<ExecutionEvent>? Filled;
    public event Action<string>? Failed { add { } remove { } }
    public event Action<OrderLifecycleEvent>? OrderLifecycleChanged { add { } remove { } }
    public void RaiseFill(ExecutionEvent execution) => Filled?.Invoke(execution);
    public void Dispose()
    {
        Interlocked.Increment(ref DisposeCalls);
        if (!FailDispose) return;
        DisposeEntered.Set();
        ReleaseDispose.Wait();
        throw new InvalidOperationException("expected dispose failure");
    }
    public PaperOrder Submit(PaperOrderRequest request, DateTimeOffset now) => throw new NotSupportedException();
    public bool Cancel(string orderId) => false;
    public int CancelAll(string strategyInstanceId) => 0;
    public bool CancelByClientOrderId(string strategyInstanceId, string clientOrderId) => false;
    public IReadOnlyList<PaperOrder> GetWorkingOrders(string strategyInstanceId) => Array.Empty<PaperOrder>();
    public void OnMarketEvent(MarketEvent marketEvent) { }
}

sealed class TrackingManagedExecutionService : IManagedStrategyExecutionService
{
    private readonly List<PaperOrder> _working = new();
    public int SubmitCount { get; private set; }
    public int CancelAllCount { get; private set; }

    public event Action<ExecutionEvent>? Filled { add { } remove { } }
    public event Action<string>? Failed { add { } remove { } }
    public event Action<OrderLifecycleEvent>? OrderLifecycleChanged { add { } remove { } }

    public PaperOrder Submit(PaperOrderRequest request, DateTimeOffset now)
    {
        SubmitCount++;
        var order = new PaperOrder
        {
            OrderId = $"TRACK-{SubmitCount}", ClientOrderId = request.ClientOrderId,
            StrategyInstanceId = request.StrategyInstanceId, Symbol = request.Symbol,
            Side = request.Side, Type = request.Type, Role = request.Role,
            Price = request.Price, Quantity = request.Quantity,
            State = PaperOrderState.Working, CreatedAt = now
        };
        _working.Add(order);
        return order;
    }

    public bool Cancel(string orderId) => false;
    public int CancelAll(string strategyInstanceId)
    {
        CancelAllCount++;
        int cancelled = _working.RemoveAll(x =>
            string.Equals(x.StrategyInstanceId, strategyInstanceId, StringComparison.OrdinalIgnoreCase));
        return cancelled;
    }
    public bool CancelByClientOrderId(string strategyInstanceId, string clientOrderId) => false;
    public IReadOnlyList<PaperOrder> GetWorkingOrders(string strategyInstanceId) => _working
        .Where(x => string.Equals(x.StrategyInstanceId, strategyInstanceId, StringComparison.OrdinalIgnoreCase))
        .ToArray();
    public void OnMarketEvent(MarketEvent marketEvent) { }
}

sealed class MutableStrategyClock : IEventDrivenStrategyClock
{
    public MutableStrategyClock(DateTimeOffset now) => Now = now;
    public DateTimeOffset Now { get; private set; }
    public void Advance(DateTimeOffset now) => Now = now;
}
