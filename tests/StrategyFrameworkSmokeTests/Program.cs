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
using System.Text.Json;
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

var observationSource = new FakeSource();
using (var observationHost = new StrategyHost(observationSource))
{
    var observationProbe = new ManagedProbeStrategy("managed-observation", "MGO");
    var observationBroker = new CrossPricePaperExecutionService();
    var routedLifecycle = new List<OrderLifecycleEvent>();
    var routedDecisions = new List<StrategyDecisionObservation>();
    observationHost.OrderLifecycleOccurred += routedLifecycle.Add;
    observationHost.DecisionObserved += routedDecisions.Add;
    observationHost.Add(observationProbe,
        new ResolvedStrategyUniverse { Tradable = Product("225MC", "MGO", 5m, 10m) },
        0m, observationBroker);
    observationProbe.Observe(new StrategyDecisionObservation
    {
        Timestamp = DateTimeOffset.UtcNow, StrategyInstanceId = "another-instance",
        StrategyType = observationProbe.StrategyType,
        Decision = StrategyDecision.Accept, PrimaryReasonCode = "WrongOwner"
    });
    observationProbe.Observe(new StrategyDecisionObservation
    {
        Timestamp = DateTimeOffset.UtcNow, StrategyInstanceId = observationProbe.InstanceId,
        StrategyType = "AnotherStrategy",
        Decision = StrategyDecision.Accept, PrimaryReasonCode = "WrongType"
    });
    observationProbe.Observe(new StrategyDecisionObservation
    {
        Timestamp = DateTimeOffset.UtcNow, StrategyInstanceId = observationProbe.InstanceId,
        StrategyType = observationProbe.StrategyType,
        Decision = StrategyDecision.Accept, PrimaryReasonCode = "SignalAccepted"
    });
    observationProbe.Request("observation-buy-g1", BuySell.Buy, 100m);
    Assert(routedDecisions.Count == 1 && routedLifecycle.Select(x => x.State).SequenceEqual(new[]
        { OrderLifecycleState.Submitted, OrderLifecycleState.Working }),
        "StrategyHost 必須公開決策與 managed 委託生命週期供共用 recorder 路由");
    observationHost.Remove(observationProbe.InstanceId);
    observationProbe.Observe(new StrategyDecisionObservation
    {
        Timestamp = DateTimeOffset.UtcNow, StrategyInstanceId = observationProbe.InstanceId,
        StrategyType = observationProbe.StrategyType,
        Decision = StrategyDecision.Hold, PrimaryReasonCode = "DisposedRunner"
    });
    Assert(routedDecisions.Count == 1,
        "Host 必須拒絕不同 instance/type 的決策，並在 runner Dispose 後停止轉發");
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
var safeCleanupHost = new StrategyHost(new FakeSource());
var safeCleanupExecution = new DisposeProbeExecutionService();
AssertThrows<AggregateException>(() => safeCleanupHost.Add(
        new ThrowingStartAndStopStrategy("start-stop-throws-safe"),
        new ResolvedStrategyUniverse { Tradable = Product("225MC", "TSS", 5m, 10m) },
        executionService: safeCleanupExecution),
    "Start與Stop錯誤都必須回報，但broker安全Dispose後不得retained runner");
Assert(safeCleanupExecution.DisposeCalls == 1 && safeCleanupHost.GetSnapshots().Count == 0,
    "broker已安全關閉時Stop錯誤不得留下IsDisposed runner ownership");
safeCleanupHost.Dispose();

var unsafeCleanupHost = new StrategyHost(new FakeSource());
var unsafeCleanupExecution = new DisposeProbeExecutionService { FailDispose = true };
AssertThrows<AggregateException>(() => unsafeCleanupHost.Add(
        new ThrowingStartAndStopStrategy("start-stop-throws-unsafe"),
        new ResolvedStrategyUniverse { Tradable = Product("225MC", "TSU", 5m, 10m) },
        executionService: unsafeCleanupExecution),
    "broker無法安全Dispose時必須回報Start cleanup failure");
Assert(unsafeCleanupHost.GetSnapshots().Single().InstanceId == "start-stop-throws-unsafe",
    "broker仍有風險時runner必須retained供後續retry");
unsafeCleanupExecution.FailDispose = false;
AssertThrows<InvalidOperationException>(() => unsafeCleanupHost.Remove("start-stop-throws-unsafe"),
    "後續安全關閉仍須向caller回報strategy.Stop診斷");
Assert(unsafeCleanupHost.GetSnapshots().Count == 0,
    "broker retry安全Dispose後，即使Stop報錯也必須移除runner ownership");
unsafeCleanupHost.Dispose();
using (var startSnapshotHost = new StrategyHost(new FakeSource()))
{
    int healthySubscriberCalls = 0;
    startSnapshotHost.SnapshotChanged += _ => throw new InvalidOperationException("start snapshot subscriber failure");
    startSnapshotHost.SnapshotChanged += _ => healthySubscriberCalls++;
    StrategyInstanceRunner runner = startSnapshotHost.Add(
        new ManagedProbeStrategy("start-snapshot-isolated", "TXS"),
        new ResolvedStrategyUniverse { Tradable = Product("225MC", "TXS", 5m, 10m) });
    Assert(runner.InstanceId == "start-snapshot-isolated" && healthySubscriberCalls >= 2 &&
           startSnapshotHost.GetSnapshots().Count == 1,
        "Start 內與 initial snapshot 都必須逐 subscriber 隔離，例外不得讓 Add 失敗");
}
var selfDisposeSource = new TransactionalSource();
using (var selfDisposeHost = new StrategyHost(selfDisposeSource))
{
    selfDisposeSource.Host = selfDisposeHost;
    Exception? snapshotSelfDisposeFailure = null;
    selfDisposeHost.SnapshotChanged += _ =>
    {
        try { selfDisposeHost.Dispose(); }
        catch (Exception exception) { snapshotSelfDisposeFailure = exception; }
    };
    selfDisposeHost.SnapshotChanged += _ => throw new InvalidOperationException("subscriber failure");
    StrategyInstanceRunner added = selfDisposeHost.Add(
        new HeartbeatProbeStrategy("self-dispose-add"),
        new ResolvedStrategyUniverse { Tradable = Product("225MC", "TXD", 5m, 10m) });
    Assert(selfDisposeSource.SelfDisposeFailure is InvalidOperationException &&
           snapshotSelfDisposeFailure is InvalidOperationException &&
           selfDisposeHost.GetSnapshots().Count == 1 && added.InstanceId == "self-dispose-add",
        "Add owner thread self-dispose 必須 fail closed，初始 snapshot subscriber 例外不得回滾成功 Add");
}
using (var publishBarrierHost = new StrategyHost(new FakeSource()))
{
    using var initialPublishEntered = new ManualResetEventSlim(false);
    using var releaseInitialPublish = new ManualResetEventSlim(false);
    publishBarrierHost.SnapshotChanged += _ =>
    {
        initialPublishEntered.Set();
        releaseInitialPublish.Wait();
    };
    Task<StrategyInstanceRunner> addDuringPublish = Task.Factory.StartNew(
        () => publishBarrierHost.Add(
            new HeartbeatProbeStrategy("publish-barrier"),
            new ResolvedStrategyUniverse { Tradable = Product("225MC", "TXB", 5m, 10m) }),
        CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    Assert(initialPublishEntered.Wait(TimeSpan.FromSeconds(5)), "測試前置：初始 snapshot subscriber 必須進入 barrier");
    Task disposeDuringPublish = Task.Factory.StartNew(
        publishBarrierHost.Dispose, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    bool disposedBeforePublishReleased = disposeDuringPublish.Wait(TimeSpan.FromMilliseconds(100));
    releaseInitialPublish.Set();
    Assert(addDuringPublish.Wait(TimeSpan.FromSeconds(5)) && disposeDuringPublish.Wait(TimeSpan.FromSeconds(5)) &&
           !disposedBeforePublishReleased,
        "外部 Dispose 必須等待 initial snapshot 發布與 add lease 完整結束");
}

using (var perInstanceSource = new FakeSource())
using (var perInstanceHost = new StrategyHost(perInstanceSource, new MutableStrategyClock(heartbeatTime)))
{
    var activeA = new ManagedHeartbeatProbeStrategy("active-a", "PIA");
    perInstanceHost.Add(activeA,
        new ResolvedStrategyUniverse { Tradable = Product("225MC", "PIA", 5m, 10m) });
    using var slowEntered = new ManualResetEventSlim(false);
    using var releaseSlow = new ManualResetEventSlim(false);
    var slowB = new BlockingStartStrategy("slow-b", slowEntered, releaseSlow);
    Task<StrategyInstanceRunner> slowAdd = Task.Factory.StartNew(
        () => perInstanceHost.Add(slowB,
            new ResolvedStrategyUniverse { Tradable = Product("225MC", "PIB", 5m, 10m) }),
        TaskCreationOptions.LongRunning);
    Assert(slowEntered.Wait(TimeSpan.FromSeconds(5)), "B Start barrier 未進入");
    perInstanceSource.Emit(Book("PIA", "225MC", 100m, 105m, 1, heartbeatTime.AddSeconds(1)));
    perInstanceHost.Pulse(heartbeatTime.AddSeconds(1));
    Assert(activeA.MarketCount == 1 && activeA.HeartbeatCount == 1 &&
           perInstanceHost.Stop("active-a", "A emergency stop"),
        "B 慢 Start 不得阻塞既有 A 的 market/heartbeat/targeted stop");
    AssertThrows<InvalidOperationException>(() => perInstanceHost.Add(
            new HeartbeatProbeStrategy("slow-b"),
            new ResolvedStrategyUniverse { Tradable = Product("225MC", "PIB", 5m, 10m) }),
        "同 instanceId Host.Add 必須在第一個 Start 完成前原子拒絕");
    Assert(perInstanceHost.GetSnapshots().Count == 1,
        "GetSnapshots 不得等待或暴露 partial B runner");
    AssertThrows<InvalidOperationException>(() => perInstanceHost.StopAll("administrative stop"),
        "pending Add 存在時 StopAll 必須 fail closed，不得假裝全部停止");
    releaseSlow.Set();
    Assert(slowAdd.Wait(TimeSpan.FromSeconds(5)) && perInstanceHost.GetSnapshots().Count == 2,
        "B Start 完成後才可公開 runner");
    perInstanceHost.StopAll("administrative stop retry");
    Assert(perInstanceHost.GetSnapshots().All(x => x.State == StrategyLifecycleState.Stopped),
        "pending Add 完成後重試 StopAll 才可線性化地停止全部 runner");
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
Assert(ledgerPosition.NetQuantity == 0 && ledgerPosition.RealizedPnl == 200m && ledgerPosition.Costs == 4m &&
       ledgerPosition.NetPnl == 196m && ledgerPosition.CompletedTrades == 1,
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

// A full archive queue must apply backpressure rather than discard raw market events.
string archiveBackpressureRoot = Path.Combine(Path.GetTempPath(), "KgiStrategyArchiveBackpressure", Guid.NewGuid().ToString("N"));
try
{
    using var enteredArchiveWrite = new ManualResetEventSlim(false);
    using var releaseArchiveWrite = new ManualResetEventSlim(false);
    using var archiveWriter = new MarketDataArchiveWriter(
        archiveBackpressureRoot,
        "lossless",
        capacity: 1,
        beforeWrite: () =>
        {
            enteredArchiveWrite.Set();
            releaseArchiveWrite.Wait();
        });
    archiveWriter.Record(Trade("LOSSLESS", "LOSSLESS", 100m, 1, t0));
    Assert(enteredArchiveWrite.Wait(TimeSpan.FromSeconds(5)), "Writer must begin the first blocked archive write.");
    archiveWriter.Record(Trade("LOSSLESS", "LOSSLESS", 101m, 1, t0.AddSeconds(1)));
    Task thirdRecord = Task.Run(() => archiveWriter.Record(Trade("LOSSLESS", "LOSSLESS", 102m, 1, t0.AddSeconds(2))));
    Assert(!thirdRecord.Wait(TimeSpan.FromMilliseconds(100)), "A full archive queue must backpressure the producer instead of dropping the third event.");
    releaseArchiveWrite.Set();
    Assert(thirdRecord.Wait(TimeSpan.FromSeconds(5)), "Backpressured archive record must complete after the writer drains.");
    ArchiveWriterSnapshot losslessSnapshot = archiveWriter.CaptureBarrier();
    using var losslessStream = new FileStream(losslessSnapshot.EventFilePath, FileMode.Open, FileAccess.Read,
        FileShare.ReadWrite | FileShare.Delete);
    using var losslessReader = new StreamReader(losslessStream);
    int archivedEvents = 0;
    while (losslessReader.ReadLine() != null) archivedEvents++;
    Assert(archivedEvents == 3, "Lossless archive must persist every queued market event.");
}
finally
{
    if (Directory.Exists(archiveBackpressureRoot)) Directory.Delete(archiveBackpressureRoot, true);
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

// Shared observability must retain enough correlated evidence to reproduce a run.
string observationDirectory = Path.Combine(Path.GetTempPath(), "kgi-observation-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(observationDirectory);
try
{
    DateTimeOffset observedAt = new(2026, 9, 22, 1, 2, 3, TimeSpan.Zero);
    using (var recorder = new SimulationRunRecorder(observationDirectory, TimeSpan.FromSeconds(30)))
    {
        recorder.RecordManifest(new RunManifest
        {
            RunId = "run-observation-1", StrategyType = "PeriodicQueueCycle", InstanceId = "observe-1",
            Mode = StrategyRunMode.HistoricalReplay, StartedAt = observedAt,
            EffectiveParameters = new Dictionary<string, string> { ["MinMoveTicks"] = "2" },
            NormalizedParameters = "MinMoveTicks=2", ParameterSha256 = "PARAM-HASH",
            InputArchive = new InputArchiveIdentity
            {
                AbsolutePath = @"E:\data\events.jsonl", SizeBytes = 123, LastWriteTimeUtc = observedAt,
                Sha256 = "ARCHIVE-HASH"
            },
            Universe = new[] { new RunProductIdentity("225MC", "MCM6", "202606", 5m, 10m) },
            ExecutionAssumptions = new Dictionary<string, string> { ["fillModel"] = "strict-cross" }
        });

        var accepted = new StrategyDecisionObservation
        {
            Sequence = 101, Timestamp = observedAt, StrategyType = "PeriodicQueueCycle",
            StrategyInstanceId = "observe-1", CandidateId = "candidate-1", CycleId = "cycle-1",
            IsCandidate = true,
            Symbol = "MCM6", Direction = "Up", State = StrategyLifecycleState.Ready,
            Decision = StrategyDecision.Accept, PrimaryReasonCode = "SignalAccepted",
            SecondaryReasonCodes = new[] { "AllHealthGatesPassed" }, QuoteAgeSeconds = 0.25m,
            SpreadTicks = 1m,
            Thresholds = new[]
            {
                new ThresholdObservation("MinMoveTicks", 3m, 2m, "ticks", true),
                new ThresholdObservation("MinDepthObi", 0.14m, 0.10m, "ratio", true)
            }
        };
        recorder.Record(accepted with
        {
            Sequence = 100, Timestamp = observedAt.AddSeconds(-1), CandidateId = "warmup-heartbeat",
            CycleId = string.Empty, IsCandidate = false, Decision = StrategyDecision.Warmup,
            PrimaryReasonCode = "WarmupIncomplete"
        });
        recorder.Record(accepted);
        recorder.Record(accepted with { Sequence = 102, Timestamp = observedAt.AddSeconds(1) });
        recorder.Record(accepted with
        {
            Sequence = 102, Timestamp = observedAt.AddSeconds(1).AddMilliseconds(1), QuoteAgeSeconds = 0.5m
        });
        recorder.Record(accepted with
        {
            Sequence = 103, Timestamp = observedAt.AddSeconds(2), State = StrategyLifecycleState.Running,
            Decision = StrategyDecision.Hold, PrimaryReasonCode = "WorkingBoth"
        });
        recorder.Record(accepted with { Sequence = 104, Timestamp = observedAt.AddSeconds(31) });
        var timeoutDecision = accepted with
        {
            Sequence = 105, Timestamp = observedAt.AddSeconds(40), Decision = StrategyDecision.Exit,
            State = StrategyLifecycleState.Running, PrimaryReasonCode = "OneLegTimeout",
            Thresholds = Array.Empty<ThresholdObservation>()
        };
        recorder.Record(timeoutDecision);
        recorder.Record(timeoutDecision with { Sequence = 106, Timestamp = observedAt.AddSeconds(80) });
        recorder.Record(accepted with
        {
            Sequence = 107, Timestamp = observedAt.AddSeconds(90), Decision = StrategyDecision.Reject,
            PrimaryReasonCode = "DirectionThresholdFailed",
            Thresholds = new[] { new ThresholdObservation("MinMoveTicks", 1m, 2m, "ticks", false) }
        });

        recorder.Record(Lifecycle("observe-1-cycle-1-buy-g1", "SIM-1", OrderLifecycleState.Submitted,
            OrderLifecycleState.Submitted, 100m, observedAt.AddMilliseconds(10)));
        recorder.Record(Lifecycle("observe-1-cycle-1-buy-g1", "SIM-1", OrderLifecycleState.Submitted,
            OrderLifecycleState.Submitted, 100m, observedAt.AddMilliseconds(15)));
        recorder.Record(Lifecycle("observe-1-cycle-1-sell-g1", "SIM-2", OrderLifecycleState.Submitted,
            OrderLifecycleState.Submitted, 110m, observedAt.AddMilliseconds(20)));
        recorder.Record(Lifecycle("observe-1-cycle-1-sell-g1", "SIM-2", OrderLifecycleState.Working,
            OrderLifecycleState.Cancelled, 110m, observedAt.AddSeconds(1)));
        recorder.Record(ObservedExecution(1, "cycle-1-buy-g1", "SIM-1", BuySell.Buy,
            PaperOrderRole.EntryLong, 100m, 95m, 201, observedAt.AddSeconds(2)));
        recorder.Record(ObservedExecution(2, "cycle-1-sell-g2", "SIM-3", BuySell.Sell,
            PaperOrderRole.EntryShort, 110m, 115m, 202, observedAt.AddSeconds(3)));
        recorder.Record(new StrategySnapshot
        {
            StrategyType = "PeriodicQueueCycle", InstanceId = "observe-1", State = StrategyLifecycleState.Ready,
            Reason = "CycleCompleted", RealizedPnl = 100m, NetPnl = 28m, CompletedTrades = 2,
            UpdatedAt = observedAt.AddSeconds(3)
        });
        AssertThrows<ArgumentException>(() => recorder.Record(new ParameterExperimentRecord
        {
            ExperimentId = "invalid-empty", Status = ParameterExperimentStatus.Valid
        }), "Valid parameter experiment 缺少可重現必要欄位時必須拒絕");
        AssertThrows<ArgumentException>(() => recorder.Record(new ParameterExperimentRecord
        {
            ExperimentId = "rejected-empty", Status = ParameterExperimentStatus.Rejected
        }), "Rejected parameter experiment 仍須保留完整 identity、結果與穩定淘汰原因");
        AssertThrows<ArgumentException>(() => recorder.Record(new ParameterExperimentRecord
        {
            ExperimentId = "incomplete-empty", Status = ParameterExperimentStatus.Incomplete
        }), "Incomplete parameter experiment 仍須保留完整 identity、結果與穩定缺失原因");
        var validExperiment = new ParameterExperimentRecord
        {
            ExperimentId = "experiment-1", BaselineRunId = "baseline-1", CandidateRunId = "run-observation-1",
            StrategyType = "PeriodicQueueCycle", CodeVersion = "test-version",
            SearchMethod = "manual", Status = ParameterExperimentStatus.Valid,
            CandidateParameters = new Dictionary<string, string> { ["MinMoveTicks"] = "2" },
            ChangedParameters = new Dictionary<string, string> { ["MinMoveTicks"] = "2" },
            ParameterDefinitions = new Dictionary<string, ParameterSearchDefinition>
            {
                ["MinMoveTicks"] = new("decimal", "ticks", 0.5m, 50m, 1m, 3m, 0.5m)
            },
            ProductMapping = new Dictionary<string, string> { ["225MC"] = "MCM6" },
            ExecutionAssumptions = new Dictionary<string, string> { ["fillModel"] = "strict-cross" },
            InputArchiveSha256 = "ARCHIVE-HASH", DataSplitId = "out-of-sample-1", PrimaryObjective = "netPnl",
            SecondaryObjectives = new[] { "drawdown" },
            GuardrailValues = new Dictionary<string, decimal?> { ["minimumCompletedTrades"] = 1m },
            Results = new Dictionary<string, decimal?> { ["completedTrades"] = 1m },
            AdverseSlippageStress = new Dictionary<int, decimal?> { [0] = 28m, [1] = -72m, [2] = -172m }
        };
        recorder.Record(validExperiment);
        AssertThrows<ArgumentException>(() => recorder.Record(validExperiment with
        {
            ExperimentId = "undefined-change",
            ChangedParameters = new Dictionary<string, string> { ["UnknownParameter"] = "2" }
        }), "changed parameters 必須是 candidate 子集，且每個 key 都有 typed definition");
        AssertThrows<ArgumentException>(() => recorder.Record(validExperiment with
        {
            ExperimentId = "off-step", CandidateParameters = new Dictionary<string, string> { ["MinMoveTicks"] = "1.1" },
            ChangedParameters = new Dictionary<string, string> { ["MinMoveTicks"] = "1.1" }
        }), "候選數值必須符合 type、allowed/search range 與 step");
        AssertThrows<ArgumentException>(() => recorder.Record(validExperiment with
        {
            ExperimentId = "grid-no-ordinal", SearchMethod = "grid", CandidateOrdinal = null
        }), "grid 搜尋必須記錄 candidate ordinal");
        AssertThrows<ArgumentException>(() => recorder.Record(validExperiment with
        {
            ExperimentId = "random-no-seed", SearchMethod = "random", RandomSeed = null
        }), "random 搜尋必須記錄 random seed");
        AssertThrows<ArgumentException>(() => recorder.Record(validExperiment with
        {
            ExperimentId = "valid-null-result",
            Results = new Dictionary<string, decimal?> { ["completedTrades"] = null }
        }), "Valid experiment 的 results/guardrails 必須是完整有限值，不得用 null 假裝已驗證");
        recorder.Record(validExperiment with
        {
            ExperimentId = "incomplete-with-provenance", Status = ParameterExperimentStatus.Incomplete,
            ReasonCode = "ReplayInterrupted", Results = new Dictionary<string, decimal?>(),
            AdverseSlippageStress = new Dictionary<int, decimal?>(),
            UnavailableReasons = new Dictionary<string, string>
            {
                ["Results"] = "ReplayInterrupted", ["AdverseSlippageStress"] = "ReplayInterrupted"
            }
        });
        recorder.FinalizeSummary(observedAt.AddSeconds(4));
    }

    string[] expectedArtifacts =
    {
        "run-manifest.json", "strategy-decisions.jsonl", "order-lifecycle.jsonl", "executions.csv",
        "run-summary.json", "parameter-experiments.jsonl"
    };
    Assert(expectedArtifacts.All(name => File.Exists(Path.Combine(observationDirectory, name))),
        "共用 recorder 必須產生六項可重現策略產物");
    string executionsCsv = File.ReadAllText(Path.Combine(observationDirectory, "executions.csv"));
    Assert(executionsCsv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length == 3 &&
           executionsCsv.StartsWith("timestamp,sequence,marketSequence,instanceId,cycleId,generation,clientOrderId,orderId", StringComparison.Ordinal) &&
           executionsCsv.Contains("\"cycle-1-buy-g1\"", StringComparison.Ordinal),
        "executions.csv 必須以 InvariantCulture 保留兩筆成交及 clientOrderId");
    string[] decisionLines = File.ReadAllLines(Path.Combine(observationDirectory, "strategy-decisions.jsonl"));
    Assert(decisionLines.Length == 8,
        "完全相同的決策只在心跳間隔內壓縮；跨間隔心跳可留存");
    using (JsonDocument decisionDocument = JsonDocument.Parse(
        decisionLines.First(line => line.Contains("\"decision\":\"Accept\"", StringComparison.Ordinal))))
    {
        JsonElement decision = decisionDocument.RootElement;
        JsonElement threshold = decision.GetProperty("thresholds")[0];
        Assert(decision.GetProperty("decision").GetString() == "Accept" &&
               decision.GetProperty("cycleId").GetString() == "cycle-1" &&
               decision.GetProperty("primaryReasonCode").GetString() == "SignalAccepted" &&
               decision.GetProperty("secondaryReasonCodes").GetArrayLength() == 1 &&
               decision.GetProperty("quoteAgeSeconds").GetDecimal() == 0.25m &&
               decision.GetProperty("spreadTicks").GetDecimal() == 1m &&
               threshold.GetProperty("observedValue").GetDecimal() == 3m &&
               threshold.GetProperty("thresholdValue").GetDecimal() == 2m &&
               threshold.GetProperty("passed").GetBoolean(),
            "決策證據必須包含觀察值、門檻、通過結果、原因、報價年齡、價差及週期識別");
    }
    using (JsonDocument lifecycleDocument = JsonDocument.Parse(
        File.ReadLines(Path.Combine(observationDirectory, "order-lifecycle.jsonl")).First()))
        Assert(lifecycleDocument.RootElement.GetProperty("cycleId").GetString() == "cycle-1" &&
               lifecycleDocument.RootElement.GetProperty("generation").GetInt32() == 1 &&
               lifecycleDocument.RootElement.GetProperty("symbol").GetString() == "MCM6" &&
               lifecycleDocument.RootElement.GetProperty("role").GetString() == "EntryLong" &&
               lifecycleDocument.RootElement.GetProperty("submittedAt").ValueKind == JsonValueKind.String,
            "訂單生命週期必須以 typed metadata 關聯週期、商品、角色、世代與階段時間");
    Assert(File.ReadLines(Path.Combine(observationDirectory, "order-lifecycle.jsonl")).Count() == 3,
        "同一委託重送相同生命週期轉換不得重複落盤");
    using (JsonDocument summaryDocument = JsonDocument.Parse(
        File.ReadAllText(Path.Combine(observationDirectory, "run-summary.json"))))
    {
        JsonElement summary = summaryDocument.RootElement;
        Assert(summary.GetProperty("candidateSignals").GetInt64() == 1 &&
               summary.GetProperty("acceptedSignals").GetInt64() == 1 &&
               summary.GetProperty("submittedOrders").GetInt64() == 2 &&
               summary.GetProperty("cancelledOrders").GetInt64() == 1 &&
               summary.GetProperty("fills").GetInt64() == 2 &&
               summary.GetProperty("completedTrades").GetInt64() == 2 &&
               summary.GetProperty("oneLegTimeouts").GetInt64() == 1 &&
               summary.GetProperty("grossPnl").GetDecimal() == 100m &&
               summary.GetProperty("executionCosts").GetDecimal() == 72m &&
               summary.GetProperty("netPnl").GetDecimal() == 28m &&
               summary.GetProperty("winRate").GetDecimal() == 100m &&
               summary.GetProperty("averageWinningPoints").GetDecimal() == 2.8m &&
               summary.GetProperty("averageLosingPoints").ValueKind == JsonValueKind.Null &&
               summary.GetProperty("holdingSeconds").GetDecimal() == 1m &&
               summary.GetProperty("fillRate").GetDecimal() == 1m &&
               summary.GetProperty("cancellationRate").GetDecimal() == 0.5m &&
               summary.GetProperty("adverseSlippageStress").GetProperty("1").GetDecimal() == -72m &&
               summary.GetProperty("primaryReasonCounts").GetProperty("SignalAccepted").GetInt64() == 1 &&
               summary.GetProperty("thresholdFunnel").GetProperty("MinMoveTicks").GetProperty("evaluated").GetInt64() == 1 &&
               summary.GetProperty("thresholdFunnel").GetProperty("MinMoveTicks").GetProperty("passed").GetInt64() == 0 &&
               summary.GetProperty("thresholdFunnel").GetProperty("MinMoveTicks").GetProperty("failed").GetInt64() == 1 &&
               summary.GetProperty("mfePoints").ValueKind == JsonValueKind.Null &&
               summary.GetProperty("marketEvents").ValueKind == JsonValueKind.Null &&
               summary.GetProperty("unavailableReasons").GetProperty("mfePoints").GetString() == "NoMarkToMarketPath" &&
               summary.GetProperty("unavailableReasons").GetProperty("marketEvents").GetString() == "MarketEventsNotObserved" &&
               !summary.GetProperty("unavailableReasons").TryGetProperty("grossPnl", out _) &&
               !summary.GetProperty("unavailableReasons").TryGetProperty("netPnl", out _) &&
               !summary.GetProperty("unavailableReasons").TryGetProperty("executionCosts", out _),
            "run summary 必須彙總漏斗與交易，無法取得的指標使用 null 及穩定原因碼");
    }
}
finally
{
    Directory.Delete(observationDirectory, recursive: true);
}

string deferredManifestDirectory = Path.Combine(Path.GetTempPath(), "kgi-deferred-manifest-" + Guid.NewGuid().ToString("N"));
try
{
    DateTimeOffset started = new(2026, 9, 22, 3, 0, 0, TimeSpan.Zero);
    DateTimeOffset ended = started.AddMinutes(1);
    using var deferredRecorder = new SimulationRunRecorder(deferredManifestDirectory);
    var deferred = new RunManifest
    {
        RunId = "live-final", StrategyType = "PeriodicQueueCycle", InstanceId = "live-final",
        Mode = StrategyRunMode.LiveShadow, StartedAt = started,
        RawParameters = new Dictionary<string, string> { ["MinMoveTicks"] = "2" },
        EffectiveParameters = new Dictionary<string, string> { ["MinMoveTicks"] = "2" }
    };
    deferredRecorder.PrepareManifest(deferred);
    Assert(!File.Exists(Path.Combine(deferredManifestDirectory, "run-manifest.json")),
        "live archive 尚在寫入時不得把 partial identity 寫成權威 manifest");
    deferredRecorder.FinalizeManifest(deferred with { EndedAt = ended });
    deferredRecorder.Record(new OrderLifecycleEvent
    {
        Timestamp = ended, StrategyInstanceId = "live-final", ClientOrderId = "cancel-unrouted",
        PreviousState = OrderLifecycleState.Working, State = OrderLifecycleState.CancelPending,
        ReasonCode = "CancelRequestNotRouted"
    });
    deferredRecorder.Dispose();
    using JsonDocument finalizedManifest = JsonDocument.Parse(
        File.ReadAllText(Path.Combine(deferredManifestDirectory, "run-manifest.json")));
    Assert(finalizedManifest.RootElement.GetProperty("endedAt").GetDateTimeOffset() == ended,
        "live manifest 必須在 finalize 後原子寫入正確 EndedAt");
    using JsonDocument unknownLifecycle = JsonDocument.Parse(
        File.ReadLines(Path.Combine(deferredManifestDirectory, "order-lifecycle.jsonl")).Single());
    Assert(unknownLifecycle.RootElement.GetProperty("side").ValueKind == JsonValueKind.Null &&
           unknownLifecycle.RootElement.GetProperty("role").ValueKind == JsonValueKind.Null &&
           unknownLifecycle.RootElement.GetProperty("unavailableReasons").GetProperty("side").GetString() == "OrderSideNotObserved",
        "CancelRequestNotRouted 缺少 metadata 時 Side/Role 必須是 null，不得假裝 Buy/EntryLong");
}
finally
{
    if (Directory.Exists(deferredManifestDirectory)) Directory.Delete(deferredManifestDirectory, recursive: true);
}

string boundaryDirectory = Path.Combine(Path.GetTempPath(), "kgi-archive-boundary-" + Guid.NewGuid().ToString("N"));
try
{
    Directory.CreateDirectory(boundaryDirectory);
    string liveFile = Path.Combine(boundaryDirectory, "events.jsonl");
    File.WriteAllText(liveFile, "before\n");
    DateTimeOffset boundaryStarted = new(2026, 9, 22, 4, 0, 0, TimeSpan.Zero);
    InputArchiveBoundary liveBoundary = InputArchiveBoundaryCapture.Begin(
        boundaryDirectory, appendOnly: true, boundaryStarted);
    File.AppendAllText(liveFile, "run-a\n");
    InputArchiveIdentity liveIdentity = InputArchiveBoundaryCapture.Complete(
        liveBoundary, boundaryStarted.AddMinutes(1));
    string rangeHash = liveIdentity.Sha256!;
    long capturedEnd = liveIdentity.Files.Single().EndOffset;
    File.AppendAllText(liveFile, "run-b\n");
    Assert(InputArchiveBoundaryCapture.Verify(liveIdentity) && liveIdentity.Sha256 == rangeHash &&
           liveIdentity.Files.Single().StartOffset == "before\n".Length &&
           liveIdentity.Files.Single().EndOffset == capturedEnd,
        "live archive identity 必須固定 start/end byte range；其他 instance 後續 append 不得改變既有 run hash");

    string replayFile = Path.Combine(boundaryDirectory, "replay.jsonl");
    File.WriteAllText(replayFile, "immutable\n");
    InputArchiveBoundary replayBoundary = InputArchiveBoundaryCapture.Begin(
        replayFile, appendOnly: false, boundaryStarted);
    File.AppendAllText(replayFile, "mutated\n");
    InputArchiveIdentity replayIdentity = InputArchiveBoundaryCapture.Complete(
        replayBoundary, boundaryStarted.AddMinutes(1));
    Assert(replayIdentity.MutationReasonCode == "InputMutatedDuringRun",
        "backtest 輸入在執行中變動必須標記 InputMutatedDuringRun，不得悄悄採用結束時 hash");

    string sameLengthFile = Path.Combine(boundaryDirectory, "same-length.jsonl");
    File.WriteAllText(sameLengthFile, "prefix-A");
    InputArchiveBoundary sameLengthBoundary = InputArchiveBoundaryCapture.Begin(
        sameLengthFile, appendOnly: true, boundaryStarted);
    File.WriteAllText(sameLengthFile, "prefix-B");
    File.AppendAllText(sameLengthFile, "run-data");
    InputArchiveIdentity sameLengthIdentity = InputArchiveBoundaryCapture.Complete(
        sameLengthBoundary, boundaryStarted.AddMinutes(1));
    Assert(sameLengthIdentity.MutationReasonCode == "InputMutatedDuringRun",
        "live append archive 的既有 prefix 即使長度相同被改寫，也必須判定輸入突變");

    string emptyDirectory = Path.Combine(boundaryDirectory, "empty");
    Directory.CreateDirectory(emptyDirectory);
    InputArchiveIdentity emptyIdentity = InputArchiveBoundaryCapture.Complete(
        InputArchiveBoundaryCapture.Begin(emptyDirectory, appendOnly: true, boundaryStarted),
        boundaryStarted.AddMinutes(1));
    Assert(emptyIdentity.Sha256 is null &&
           emptyIdentity.MissingFields.Contains("ArchiveFiles") &&
           emptyIdentity.MissingFields.Contains("EmptyArchive"),
        "空 archive 必須明確 unavailable，不得產生看似有效的空集合 hash");

    string deletedFile = Path.Combine(boundaryDirectory, "deleted.jsonl");
    File.WriteAllText(deletedFile, "will-delete");
    InputArchiveBoundary deletedBoundary = InputArchiveBoundaryCapture.Begin(
        deletedFile, appendOnly: false, boundaryStarted);
    File.Delete(deletedFile);
    InputArchiveIdentity deletedIdentity = InputArchiveBoundaryCapture.Complete(
        deletedBoundary, boundaryStarted.AddMinutes(1));
    Assert(deletedIdentity.MutationReasonCode == "InputMutatedDuringRun" && deletedIdentity.Sha256 is null,
        "archive replacement/deletion race 必須回傳穩定 mutation/unavailable，不可拋例外毀掉 run");
}
finally
{
    if (Directory.Exists(boundaryDirectory)) Directory.Delete(boundaryDirectory, recursive: true);
}

string archiveBarrierRoot = Path.Combine(Path.GetTempPath(), "kgi-archive-barrier-" + Guid.NewGuid().ToString("N"));
try
{
    using var archiveWriter = new MarketDataArchiveWriter(archiveBarrierRoot, "barrier-run");
    archiveWriter.Record(Book("BAR", "BAR", 99m, 101m, 1, DateTimeOffset.UtcNow));
    ArchiveWriterSnapshot startSnapshot = archiveWriter.CaptureBarrier();
    InputArchiveBoundary barrierBoundary = InputArchiveBoundaryCapture.Begin(startSnapshot, DateTimeOffset.UtcNow);
    archiveWriter.Record(Book("BAR", "BAR", 100m, 102m, 2, DateTimeOffset.UtcNow.AddSeconds(1)));
    ArchiveWriterSnapshot endSnapshot = archiveWriter.CaptureBarrier();
    InputArchiveIdentity barrierIdentity = InputArchiveBoundaryCapture.Complete(
        barrierBoundary, endSnapshot, DateTimeOffset.UtcNow.AddSeconds(1));
    string barrierHash = barrierIdentity.Sha256!;
    archiveWriter.Record(Book("BAR", "BAR", 101m, 103m, 3, DateTimeOffset.UtcNow.AddSeconds(2)));
    archiveWriter.CaptureBarrier();
    Assert(startSnapshot.Length < endSnapshot.Length && barrierIdentity.SizeBytes > 0 &&
           barrierIdentity.Files.Single().StartOffset == startSnapshot.Length &&
           barrierIdentity.Files.Single().EndOffset == endSnapshot.Length &&
           InputArchiveBoundaryCapture.Verify(barrierIdentity) && barrierIdentity.Sha256 == barrierHash,
        "writer channel barrier 必須 flush marker 前事件並固定 byte range；ended 後事件不得改變既有 run identity");
}
finally
{
    if (Directory.Exists(archiveBarrierRoot)) Directory.Delete(archiveBarrierRoot, recursive: true);
}

string failingRecorderDirectory = Path.Combine(Path.GetTempPath(), "kgi-recorder-dispose-" + Guid.NewGuid().ToString("N"));
try
{
    var failingRecorder = new SimulationRunRecorder(failingRecorderDirectory);
    Directory.CreateDirectory(Path.Combine(failingRecorderDirectory, "run-summary.json"));
    bool finalizeFailed = false;
    try { failingRecorder.Dispose(); }
    catch (AggregateException) { finalizeFailed = true; }
    Assert(finalizeFailed, "summary 無法寫入時 Dispose 必須回報錯誤");
    failingRecorder.Dispose();
    File.Delete(Path.Combine(failingRecorderDirectory, "strategy-events.jsonl"));
    Assert(!File.Exists(Path.Combine(failingRecorderDirectory, "strategy-events.jsonl")),
        "summary finalize 失敗不得略過 stream cleanup，重複 Dispose 必須幂等");
}
finally
{
    if (Directory.Exists(failingRecorderDirectory)) Directory.Delete(failingRecorderDirectory, recursive: true);
}

string constructorFailureDirectory = Path.Combine(Path.GetTempPath(), "kgi-recorder-constructor-" + Guid.NewGuid().ToString("N"));
try
{
    Directory.CreateDirectory(constructorFailureDirectory);
    Directory.CreateDirectory(Path.Combine(constructorFailureDirectory, "order-lifecycle.jsonl"));
    AssertThrows<UnauthorizedAccessException>(() => new SimulationRunRecorder(constructorFailureDirectory),
        "第三個 artifact 建立失敗時 constructor 必須回報失敗");
    Directory.Delete(Path.Combine(constructorFailureDirectory, "order-lifecycle.jsonl"));
    Assert(!File.Exists(Path.Combine(constructorFailureDirectory, "strategy-events.jsonl")) &&
           !File.Exists(Path.Combine(constructorFailureDirectory, "strategy-decisions.jsonl")),
        "constructor 中途失敗必須關閉先前 stream 並清除本次 partial outputs");
}
finally
{
    if (Directory.Exists(constructorFailureDirectory)) Directory.Delete(constructorFailureDirectory, recursive: true);
}

string retryFinalizeDirectory = Path.Combine(Path.GetTempPath(), "kgi-recorder-finalize-retry-" + Guid.NewGuid().ToString("N"));
try
{
    using var retrySet = new StrategyRunRecorderSet();
    Assert(retrySet.TryReserve("retry-finalize"), "finalize retry fixture 必須取得 reservation");
    var retryRecorder = new SimulationRunRecorder(retryFinalizeDirectory);
    retryRecorder.PrepareManifest(new RunManifest
    {
        RunId = "retry-finalize", StrategyType = "Probe", InstanceId = "retry-finalize",
        Mode = StrategyRunMode.LiveShadow, StartedAt = DateTimeOffset.UtcNow
    });
    int finalizeAttempts = 0;
    retrySet.CommitReservation("retry-finalize", retryRecorder, endedAt =>
    {
        if (++finalizeAttempts == 1) throw new IOException("transient finalizer failure");
        return new RunManifest
        {
            RunId = "retry-finalize", StrategyType = "Probe", InstanceId = "retry-finalize",
            Mode = StrategyRunMode.LiveShadow, StartedAt = endedAt.AddMinutes(-1), EndedAt = endedAt
        };
    });
    AssertThrows<AggregateException>(() => retrySet.RemoveAndFinalize("retry-finalize", DateTimeOffset.UtcNow),
        "第一次 manifest finalize 失敗必須保留 closing recorder 供重試");
    retrySet.RemoveAndFinalize("retry-finalize", DateTimeOffset.UtcNow.AddSeconds(1));
    Assert(finalizeAttempts == 2 && File.Exists(Path.Combine(retryFinalizeDirectory, "run-manifest.json")),
        "同 instance Remove 重試必須完成原 recorder，不可在首次失敗時不可逆移除 ownership");
}
finally
{
    if (Directory.Exists(retryFinalizeDirectory)) Directory.Delete(retryFinalizeDirectory, recursive: true);
}

string recorderSetRoot = Path.Combine(Path.GetTempPath(), "kgi-recorder-set-" + Guid.NewGuid().ToString("N"));
string recorderOnePath = Path.Combine(recorderSetRoot, "one");
string recorderTwoPath = Path.Combine(recorderSetRoot, "two");
try
{
    using var recorderSet = new StrategyRunRecorderSet();
    recorderSet.Add("instance-one", new SimulationRunRecorder(recorderOnePath));
    recorderSet.Add("instance-two", new SimulationRunRecorder(recorderTwoPath));
    recorderSet.Record(new StrategySnapshot
    {
        InstanceId = "instance-one", StrategyType = "First", Reason = "only-one", UpdatedAt = DateTimeOffset.UtcNow
    });
    recorderSet.Record(new StrategySnapshot
    {
        InstanceId = "instance-two", StrategyType = "Second", Reason = "only-two", UpdatedAt = DateTimeOffset.UtcNow
    });
    recorderSet.Record(ObservedExecution(11, "instance-one-cycle-1-buy-g1", "ONE", BuySell.Buy,
        PaperOrderRole.EntryLong, 100m, 95m, 301, DateTimeOffset.UtcNow) with { StrategyInstanceId = "instance-one" });
    recorderSet.Record(ObservedExecution(12, "instance-two-cycle-1-buy-g1", "TWO", BuySell.Buy,
        PaperOrderRole.EntryLong, 200m, 195m, 302, DateTimeOffset.UtcNow) with { StrategyInstanceId = "instance-two" });
    recorderSet.RemoveAndFinalize("instance-one", DateTimeOffset.UtcNow);
    recorderSet.RemoveAndFinalize("instance-two", DateTimeOffset.UtcNow);
    string oneEvents = File.ReadAllText(Path.Combine(recorderOnePath, "strategy-events.jsonl"));
    string twoEvents = File.ReadAllText(Path.Combine(recorderTwoPath, "strategy-events.jsonl"));
    string oneExecutions = File.ReadAllText(Path.Combine(recorderOnePath, "executions.csv"));
    string twoExecutions = File.ReadAllText(Path.Combine(recorderTwoPath, "executions.csv"));
    Assert(oneEvents.Contains("only-one", StringComparison.Ordinal) && !oneEvents.Contains("only-two", StringComparison.Ordinal) &&
           twoEvents.Contains("only-two", StringComparison.Ordinal) && !twoEvents.Contains("only-one", StringComparison.Ordinal) &&
           oneExecutions.Contains("\"ONE\"", StringComparison.Ordinal) && !oneExecutions.Contains("\"TWO\"", StringComparison.Ordinal) &&
           twoExecutions.Contains("\"TWO\"", StringComparison.Ordinal) && !twoExecutions.Contains("\"ONE\"", StringComparison.Ordinal),
        "live-shadow recorder registry 必須依 StrategyInstanceId 完全隔離 artifact");
}
finally
{
    if (Directory.Exists(recorderSetRoot)) Directory.Delete(recorderSetRoot, recursive: true);
}

using (var reservationSet = new StrategyRunRecorderSet())
{
    using var startReservationRace = new ManualResetEventSlim(false);
    Task<bool>[] reservations = Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
    {
        startReservationRace.Wait();
        return reservationSet.TryReserve("same-live-instance");
    })).ToArray();
    startReservationRace.Set();
    Task.WaitAll(reservations);
    Assert(reservations.Count(task => task.Result) == 1,
        "live instance ownership 必須在建立 recorder 目錄／stream 前原子 reservation");
    string reservedOutput = Path.Combine(Path.GetTempPath(), "kgi-reserved-buffer-" + Guid.NewGuid().ToString("N"));
    try
    {
        reservationSet.Record(new StrategySnapshot
        {
            InstanceId = "same-live-instance", StrategyType = "Reserved",
            Reason = "start-buffered", UpdatedAt = DateTimeOffset.UtcNow
        });
        reservationSet.CommitReservation("same-live-instance", new SimulationRunRecorder(reservedOutput));
        reservationSet.ReleaseReservation("same-live-instance");
        reservationSet.Record(new StrategySnapshot
        {
            InstanceId = "same-live-instance", StrategyType = "Reserved",
            Reason = "retained-after-add-failure", UpdatedAt = DateTimeOffset.UtcNow.AddSeconds(1)
        });
        reservationSet.RemoveAndFinalize("same-live-instance", DateTimeOffset.UtcNow);
        string reservedEvents = File.ReadAllText(Path.Combine(reservedOutput, "strategy-events.jsonl"));
        Assert(reservedEvents.Contains("start-buffered", StringComparison.Ordinal) &&
               reservedEvents.Contains("retained-after-add-failure", StringComparison.Ordinal),
            "Host.Add 前事件必須緩衝；若 Add 失敗但 runner retained，finally 不得清掉已 commit recorder");
    }
    finally
    {
        if (Directory.Exists(reservedOutput)) Directory.Delete(reservedOutput, recursive: true);
    }
}

using (var operationGate = new StrategyInstanceOperationGate())
{
    using IDisposable addLease = operationGate.Enter("linearized-instance");
    using IDisposable nestedLease = operationGate.Enter("linearized-instance");
    AssertThrows<InvalidOperationException>(() => operationGate.BeginDispose(),
        "owner callback 內 Dispose 必須立即失敗，不得等待自己的 lease 形成死鎖");
    using var removeAttempted = new ManualResetEventSlim(false);
    using var removeEntered = new ManualResetEventSlim(false);
    Task removeTask = Task.Run(() =>
    {
        removeAttempted.Set();
        using IDisposable removeLease = operationGate.Enter("linearized-instance");
        removeEntered.Set();
    });
    Assert(removeAttempted.Wait(TimeSpan.FromSeconds(2)), "Remove race 測試必須確實抵達同 instance lease");
    Assert(!removeEntered.Wait(TimeSpan.FromMilliseconds(100)),
        "同 instance Remove 必須等待 Add recorder-first lifecycle 完整結束");
    Task disposeWaiter = Task.Run(() =>
    {
        operationGate.BeginDispose();
        operationGate.ReopenAfterFailedDispose();
    });
    Assert(!disposeWaiter.Wait(TimeSpan.FromMilliseconds(100)),
        "外部 Dispose 必須等待既有 Add lease 安全結束");
    nestedLease.Dispose();
    addLease.Dispose();
    Assert(removeEntered.Wait(TimeSpan.FromSeconds(2)),
        "Add lease 結束後 Remove 必須取得 ownership，避免 orphan recorder");
    removeTask.GetAwaiter().GetResult();
    Assert(disposeWaiter.Wait(TimeSpan.FromSeconds(2)),
        "host/recorder dispose 失敗後 gate 必須可 reopen 供 Remove/Get/retry 使用");
    using IDisposable retryLease = operationGate.Enter("linearized-instance");
}

var disposedOperationGate = new StrategyInstanceOperationGate();
disposedOperationGate.Dispose();
AssertThrows<ObjectDisposedException>(() => disposedOperationGate.Enter("after-dispose"),
    "service dispose 後不可再開始 Add/Remove lifecycle");

string observationFailurePath = Path.Combine(Path.GetTempPath(), "kgi-observation-failure-" + Guid.NewGuid().ToString("N"));
try
{
    using var failureSet = new StrategyRunRecorderSet();
    var brokenRecorder = new SimulationRunRecorder(observationFailurePath);
    failureSet.Add("fatal-observation", brokenRecorder);
    string? fatalInstance = null;
    failureSet.FatalObservationFailure += (instanceId, _) => fatalInstance = instanceId;
    brokenRecorder.Dispose();
    AssertThrows<ObjectDisposedException>(() => failureSet.Record(new StrategySnapshot
    {
        InstanceId = "fatal-observation", StrategyType = "Probe", Reason = "must-fail-closed",
        UpdatedAt = DateTimeOffset.UtcNow
    }), "artifact write 失敗必須顯式回報，不能讓 runner 在未記錄狀態繼續");
    Assert(fatalInstance == "fatal-observation",
        "recorder write failure 必須攜帶 instance correlation 觸發 fail-closed");
}
finally
{
    if (Directory.Exists(observationFailurePath)) Directory.Delete(observationFailurePath, recursive: true);
}

string hostObservationFailurePath = Path.Combine(Path.GetTempPath(), "kgi-host-observation-failure-" + Guid.NewGuid().ToString("N"));
try
{
    var fatalSource = new FakeSource();
    using var fatalHost = new StrategyHost(fatalSource, new SystemStrategyClock());
    using var fatalSet = new StrategyRunRecorderSet();
    var fatalRecorder = new SimulationRunRecorder(hostObservationFailurePath);
    fatalSet.Add("host-fatal-observation", fatalRecorder);
    fatalRecorder.Dispose();
    fatalHost.SnapshotChanged += fatalSet.Record;
    fatalHost.ExecutionOccurred += fatalSet.Record;
    fatalHost.OrderLifecycleOccurred += fatalSet.Record;
    fatalHost.DecisionObserved += fatalSet.Record;
    fatalSet.FatalObservationFailure += (instanceId, exception) =>
        fatalHost.MarkObservationFault(instanceId, exception);
    var fatalStrategy = new ManagedProbeStrategy("host-fatal-observation", "HFO");
    var fatalBroker = new ConcurrentCallbackExecutionService();
    StrategyInstanceRunner fatalRunner = fatalHost.Add(fatalStrategy, new ResolvedStrategyUniverse
    {
        Tradable = Product("HFO", "HFO", 1m, 1m)
    }, 1m, fatalBroker);
    Assert(fatalRunner.GetSnapshot().State == StrategyLifecycleState.Faulted &&
           fatalRunner.GetSnapshot().Reason == "FatalObservationFailure",
        "Host.Add 同步 artifact failure 必須在返回前將 runner fail-close，不可返回 Running/Ready");
    fatalStrategy.Request("blocked-after-observation-fault", BuySell.Buy, 100m);
    fatalSource.Emit(Book("HFO", "HFO", 99m, 101m, 1, DateTimeOffset.UtcNow));
    Assert(fatalStrategy.MarketEvents.Count == 0,
        "observation fault 後必須阻止 OnMarket/Pulse/新 order，且不可觸發自動 cancel/flatten");
    fatalBroker.RaiseFill(new ExecutionEvent
    {
        Sequence = 1, MarketSequence = 1, Timestamp = DateTimeOffset.UtcNow,
        StrategyInstanceId = fatalStrategy.InstanceId, ClientOrderId = "late-fill", OrderId = "late-order",
        Symbol = "HFO", Side = BuySell.Buy, Role = PaperOrderRole.EntryLong,
        Price = 100m, Quantity = 1, Commission = 1m
    });
    Assert(fatalStrategy.Executions.Count == 1 &&
           fatalSet.GetPendingDiagnostics(fatalStrategy.InstanceId).Any(item => item.Kind == "Execution"),
        "fail-close 後 late fill/lifecycle 仍須送入 runner，並保留於公開 emergency diagnostic buffer");
}
finally
{
    if (Directory.Exists(hostObservationFailurePath)) Directory.Delete(hostObservationFailurePath, recursive: true);
}

string excursionArtifactPath = Path.Combine(Path.GetTempPath(), "kgi-excursion-artifact-" + Guid.NewGuid().ToString("N"));
try
{
    DateTimeOffset excursionStart = new(2026, 9, 22, 6, 0, 0, TimeSpan.Zero);
    using (var recorder = new SimulationRunRecorder(excursionArtifactPath))
    {
        recorder.RecordManifest(new RunManifest
        {
            RunId = "excursions", StrategyType = "ExcursionProbe", InstanceId = "excursion-probe",
            Mode = StrategyRunMode.HistoricalReplay, StartedAt = excursionStart,
            Universe = new[]
            {
                new RunProductIdentity("A", "A", "202612", 1m, 10m),
                new RunProductIdentity("B", "B", "202612", 1m, 10m),
                new RunProductIdentity("C", "C", "202612", 1m, 10m),
                new RunProductIdentity("D", "D", "202612", 1m, 10m)
            }
        });
        long sequence = 0;
        void Fill(string symbol, string cycle, BuySell side, decimal price, int quantity, decimal commission, int seconds)
        {
            recorder.Record(new ExecutionEvent
            {
                Sequence = ++sequence, MarketSequence = sequence, Timestamp = excursionStart.AddSeconds(seconds),
                StrategyInstanceId = "excursion-probe", ClientOrderId = $"excursion-probe-{cycle}-{side}-{sequence}-g1",
                OrderId = $"E-{sequence}", Symbol = symbol, Side = side,
                Role = side == BuySell.Buy ? PaperOrderRole.EntryLong : PaperOrderRole.EntryShort,
                Price = price, Quantity = quantity, Commission = commission, CycleId = cycle, Generation = 1
            });
        }

        Fill("A", "cycle-1", BuySell.Buy, 100m, 2, 2m, 0);
        Fill("A", "cycle-1", BuySell.Sell, 110m, 1, 1m, 1);
        Fill("A", "cycle-1", BuySell.Sell, 120m, 1, 1m, 2);
        Fill("B", "cycle-1", BuySell.Sell, 200m, 1, 1m, 0);
        Fill("B", "cycle-1", BuySell.Buy, 190m, 1, 1m, 1);
        Fill("C", "cycle-1", BuySell.Buy, 100m, 1, 1m, 0);
        Fill("C", "cycle-1", BuySell.Sell, 110m, 2, 2m, 1);
        Fill("C", "cycle-2", BuySell.Buy, 105m, 1, 1m, 2);
        Fill("D", "cycle-1", BuySell.Buy, 100m, 1, 1m, 0);
        Fill("D", "cycle-2", BuySell.Sell, 110m, 1, 1m, 1);
        foreach (string symbol in new[] { "A", "B", "C", "D" })
            recorder.Record(new StrategySnapshot
            {
                StrategyType = "ExcursionProbe", InstanceId = "excursion-probe", TradableSymbol = symbol,
                State = StrategyLifecycleState.Ready, Reason = "Flat", CompletedTrades = symbol == "C" ? 2 : 1,
                UpdatedAt = excursionStart.AddSeconds(3)
            });
        recorder.FinalizeSummary(excursionStart.AddSeconds(4));
    }
    using JsonDocument excursionSummary = JsonDocument.Parse(
        File.ReadAllText(Path.Combine(excursionArtifactPath, "run-summary.json")));
    Assert(excursionSummary.RootElement.GetProperty("completedTrades").GetInt64() == 5 &&
           excursionSummary.RootElement.GetProperty("averageWinningPoints").GetDecimal() == 9.8m,
        "roundtrip 必須以 instance+symbol 的 flat→nonzero→flat excursion 計一次；部分平倉、跨 cycle 與跨 flat fill 必須和 ledger 一致");
}
finally
{
    if (Directory.Exists(excursionArtifactPath)) Directory.Delete(excursionArtifactPath, recursive: true);
}

string integratedArtifactPath = Path.Combine(Path.GetTempPath(), "kgi-integrated-artifact-" + Guid.NewGuid().ToString("N"));
try
{
    var integratedClock = new MutableStrategyClock(new DateTimeOffset(2026, 9, 22, 2, 0, 0, TimeSpan.Zero));
    var integratedSource = new FakeSource();
    var integratedBroker = new CrossPricePaperExecutionService(1m, integratedClock);
    var integratedStrategy = new ArtifactCycleStrategy("artifact-cycle", "ART");
    using (var integratedRecorder = new SimulationRunRecorder(integratedArtifactPath))
    using (var integratedHost = new StrategyHost(integratedSource, integratedClock))
    {
        integratedRecorder.RecordManifest(new RunManifest
        {
            RunId = "integrated", StrategyType = integratedStrategy.StrategyType,
            InstanceId = integratedStrategy.InstanceId, Mode = StrategyRunMode.HistoricalReplay,
            StartedAt = integratedClock.Now,
            Universe = new[] { new RunProductIdentity("225MC", "ART", "202612", 5m, 10m) }
        });
        integratedHost.SnapshotChanged += integratedRecorder.Record;
        integratedHost.ExecutionOccurred += integratedRecorder.Record;
        integratedHost.OrderLifecycleOccurred += integratedRecorder.Record;
        integratedHost.DecisionObserved += integratedRecorder.Record;
        integratedHost.Add(integratedStrategy,
            new ResolvedStrategyUniverse { Tradable = Product("225MC", "ART", 5m, 10m) },
            1m, integratedBroker);
        integratedStrategy.CancelOriginalSellAndSubmitReplacement();
        integratedClock.Advance(integratedClock.Now.AddSeconds(1));
        integratedSource.Emit(Trade("ART", "225MC", 95m, 401, integratedClock.Now));
        integratedClock.Advance(integratedClock.Now.AddSeconds(1));
        integratedSource.Emit(Trade("ART", "225MC", 115m, 402, integratedClock.Now));
        Assert(integratedHost.Ledger.GetPosition(integratedStrategy.InstanceId, "ART").CompletedTrades == 1,
            "Host 共用 ledger 必須權威記錄 nonzero→flat round trip");
        integratedRecorder.FinalizeSummary(integratedClock.Now);
    }
    string[] lifecycleLines = File.ReadAllLines(Path.Combine(integratedArtifactPath, "order-lifecycle.jsonl"));
    string[] executionLines = File.ReadAllLines(Path.Combine(integratedArtifactPath, "executions.csv"));
    string executionText = string.Join('\n', executionLines);
    string[] filledIds = lifecycleLines.Select(line => JsonDocument.Parse(line))
        .Where(document => document.RootElement.GetProperty("state").GetString() == "Filled")
        .Select(document => document.RootElement.GetProperty("clientOrderId").GetString()!)
        .ToArray();
    Assert(lifecycleLines.Count(line => line.Contains("\"state\":\"Submitted\"", StringComparison.Ordinal)) == 3 &&
           lifecycleLines.Count(line => line.Contains("\"state\":\"Cancelled\"", StringComparison.Ordinal)) == 1 &&
           filledIds.Length == 2 && executionLines.Length == 3 &&
           filledIds.All(id => executionText.Contains($"\"{id}\"", StringComparison.Ordinal)),
        "Host+paper executor 整合產物必須把 submit/cancel/fill 與 executions 以同一 ClientOrderId 關聯");
    using JsonDocument integratedSummary = JsonDocument.Parse(
        File.ReadAllText(Path.Combine(integratedArtifactPath, "run-summary.json")));
    Assert(integratedSummary.RootElement.GetProperty("completedTrades").GetInt64() == 1,
        "Host+paper executor 完整買賣週期必須在摘要形成一筆 completed trade");
}
finally
{
    if (Directory.Exists(integratedArtifactPath)) Directory.Delete(integratedArtifactPath, recursive: true);
}

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

static OrderLifecycleEvent Lifecycle(string clientOrderId, string orderId, OrderLifecycleState previous,
    OrderLifecycleState state, decimal price, DateTimeOffset timestamp) => new()
{
    Timestamp = timestamp, StrategyInstanceId = "observe-1", ClientOrderId = clientOrderId, OrderId = orderId,
    Symbol = "MCM6", Side = clientOrderId.Contains("-buy-", StringComparison.Ordinal) ? BuySell.Buy : BuySell.Sell,
    Role = clientOrderId.Contains("-buy-", StringComparison.Ordinal) ? PaperOrderRole.EntryLong : PaperOrderRole.EntryShort,
    CycleId = "cycle-1", Generation = 1,
    PreviousState = previous, State = state, Price = price, Quantity = 1,
    FilledQuantity = state == OrderLifecycleState.Filled ? 1 : 0,
    RemainingQuantity = state == OrderLifecycleState.Filled ? 0 : 1,
    ReasonCode = state.ToString()
};

static ExecutionEvent ObservedExecution(long sequence, string clientOrderId, string orderId, BuySell side,
    PaperOrderRole role, decimal price, decimal triggerPrice, long marketSequence, DateTimeOffset timestamp) => new()
{
    Sequence = sequence, Timestamp = timestamp, MarketSequence = marketSequence, StrategyInstanceId = "observe-1",
    ClientOrderId = clientOrderId, OrderId = orderId, Symbol = "MCM6", Side = side, Role = role,
    Price = price, Quantity = 1, Commission = 36m, TriggerMarketPrice = triggerPrice
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

sealed class ArtifactCycleStrategy : ISimulationStrategy, IManagedOrderStrategy, IStrategyDecisionSource
{
    private readonly string _symbol;
    private int _fills;
    public ArtifactCycleStrategy(string instanceId, string symbol) { InstanceId = instanceId; _symbol = symbol; }
    public string StrategyType => "ArtifactCycle";
    public string InstanceId { get; }
    public StrategyLifecycleState State { get; private set; } = StrategyLifecycleState.Created;
    public event Action<PaperOrderRequest>? OrderRequested;
    public event Action? CancelAllRequested { add { } remove { } }
    public event Action<StrategySnapshot>? SnapshotChanged;
    public event Action<string>? CancelOrderRequested;
    public event Action<StrategyDecisionObservation>? DecisionObserved;
    public void Start(StrategyRuntimeContext context)
    {
        State = StrategyLifecycleState.Running;
        DecisionObserved?.Invoke(new StrategyDecisionObservation
        {
            Timestamp = context.Clock.Now, StrategyType = StrategyType, StrategyInstanceId = InstanceId,
            CandidateId = "candidate-1", CycleId = "cycle-1", Symbol = _symbol,
            Direction = "Up", State = State, Decision = StrategyDecision.Accept,
            PrimaryReasonCode = "SignalAccepted",
            Thresholds = new[] { new ThresholdObservation("MinMoveTicks", 3m, 2m, "ticks", true) }
        });
        OrderRequested?.Invoke(Order("buy-g1", BuySell.Buy, PaperOrderRole.EntryLong, 100m, 1));
        OrderRequested?.Invoke(Order("sell-g1", BuySell.Sell, PaperOrderRole.EntryShort, 110m, 1));
        Publish();
    }
    public void CancelOriginalSellAndSubmitReplacement()
    {
        CancelOrderRequested?.Invoke($"{InstanceId}-cycle-1-sell-g1");
        OrderRequested?.Invoke(Order("sell-g2", BuySell.Sell, PaperOrderRole.EntryShort, 110m, 2));
    }
    private PaperOrderRequest Order(string suffix, BuySell side, PaperOrderRole role, decimal price, int generation) => new()
    {
        ClientOrderId = $"{InstanceId}-cycle-1-{suffix}", CycleId = "cycle-1", Generation = generation,
        StrategyInstanceId = InstanceId, Symbol = _symbol, Side = side, Type = PaperOrderType.BrokerLimit,
        Role = role, Price = price, Quantity = 1
    };
    public void OnExecution(ExecutionEvent execution) { _fills++; Publish(); }
    public void OnOrderLifecycle(OrderLifecycleEvent orderEvent) { }
    public void OnMarketEvent(MarketEvent marketEvent) { }
    public void Pause(string reason) => State = StrategyLifecycleState.Suspended;
    public void Resume() => State = StrategyLifecycleState.Running;
    public void Stop(string reason) { State = StrategyLifecycleState.Stopped; Publish(); }
    public StrategySnapshot GetSnapshot() => new()
    {
        StrategyType = StrategyType, InstanceId = InstanceId, State = State,
        Reason = _fills >= 2 ? "CycleCompleted" : "Working",
        RealizedPnl = _fills >= 2 ? 10m : 0m, NetPnl = _fills >= 2 ? 8m : 0m,
        UpdatedAt = DateTimeOffset.UtcNow
    };
    private void Publish() => SnapshotChanged?.Invoke(GetSnapshot());
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

sealed class ThrowingStartAndStopStrategy : ISimulationStrategy
{
    public ThrowingStartAndStopStrategy(string instanceId) => InstanceId = instanceId;
    public string StrategyType => "ThrowingStartAndStop";
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
    public void Stop(string reason) => throw new InvalidOperationException("stop failed");
    public StrategySnapshot GetSnapshot() => new() { StrategyType = StrategyType, InstanceId = InstanceId };
}

sealed class DisposeProbeExecutionService : IStrategyExecutionService, IDisposable
{
    public bool FailDispose { get; set; }
    public int DisposeCalls { get; private set; }
    public event Action<ExecutionEvent>? Filled { add { } remove { } }
    public event Action<string>? Failed { add { } remove { } }
    public void Dispose()
    {
        DisposeCalls++;
        if (FailDispose) throw new InvalidOperationException("broker dispose failed");
    }
    public PaperOrder Submit(PaperOrderRequest request, DateTimeOffset now) => throw new NotSupportedException();
    public bool Cancel(string orderId) => false;
    public int CancelAll(string strategyInstanceId) => 0;
    public IReadOnlyList<PaperOrder> GetWorkingOrders(string strategyInstanceId) => Array.Empty<PaperOrder>();
    public void OnMarketEvent(MarketEvent marketEvent) { }
}

sealed class ManagedProbeStrategy : ISimulationStrategy, IManagedOrderStrategy, IStrategyDecisionSource
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
    public event Action<StrategyDecisionObservation>? DecisionObserved;

    public void Observe(StrategyDecisionObservation observation) => DecisionObserved?.Invoke(observation);

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

sealed class ManagedHeartbeatProbeStrategy : ISimulationStrategy, IStrategyHeartbeat
{
    public ManagedHeartbeatProbeStrategy(string instanceId, string symbol) { InstanceId = instanceId; Symbol = symbol; }
    public string StrategyType => "ManagedHeartbeatProbe";
    public string InstanceId { get; }
    public string Symbol { get; }
    public StrategyLifecycleState State { get; private set; } = StrategyLifecycleState.Created;
    public int MarketCount { get; private set; }
    public int HeartbeatCount { get; private set; }
    public event Action<PaperOrderRequest>? OrderRequested;
    public event Action? CancelAllRequested;
    public event Action<StrategySnapshot>? SnapshotChanged;
    public void Start(StrategyRuntimeContext context) => State = StrategyLifecycleState.Ready;
    public void OnMarketEvent(MarketEvent marketEvent) => MarketCount++;
    public void OnHeartbeat(DateTimeOffset now) => HeartbeatCount++;
    public void OnExecution(ExecutionEvent execution) { }
    public void Pause(string reason) => State = StrategyLifecycleState.Suspended;
    public void Resume() => State = StrategyLifecycleState.Ready;
    public void Stop(string reason) => State = StrategyLifecycleState.Stopped;
    public StrategySnapshot GetSnapshot() => new() { StrategyType = StrategyType, InstanceId = InstanceId, TradableSymbol = Symbol, State = State };
}

sealed class BlockingStartStrategy : ISimulationStrategy
{
    private readonly ManualResetEventSlim _entered;
    private readonly ManualResetEventSlim _release;
    public BlockingStartStrategy(string instanceId, ManualResetEventSlim entered, ManualResetEventSlim release)
    { InstanceId = instanceId; _entered = entered; _release = release; }
    public string StrategyType => "BlockingStart";
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
    public void Stop(string reason) => State = StrategyLifecycleState.Stopped;
    public StrategySnapshot GetSnapshot() => new() { StrategyType = StrategyType, InstanceId = InstanceId, State = State };
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
