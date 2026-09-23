using KGIOptionPricing.StrategyFramework;
using KGIOptionPricing.StrategyFramework.Recovery;
using KGIOptionPricing.StrategyFramework.Execution;
using KGIOptionPricing.OrderManagement.Core;
using KGIOptionPricing.StrategyFramework.Performance;
using KGIOptionPricing.Strategies.MeanReversionScalper;
using KGIOptionPricing.Strategies.WaveletScalper;
using KGIOptionPricing.Strategies.NikkeiMicrostructureAlpha;

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

PaperOrderRequest request = new()
{
    StrategyInstanceId = "MR-A",
    StrategyRunGeneration = 17,
    Generation = 3,
    ClientOrderId = "entry-1",
    CycleId = "cycle-1",
    Symbol = "225MC-Z26",
    Side = BuySell.Buy,
    Type = PaperOrderType.Limit,
    Role = PaperOrderRole.EntryLong,
    Price = 66550m,
    Quantity = 1
};

PaperOrder order = new()
{
    StrategyInstanceId = request.StrategyInstanceId,
    StrategyRunGeneration = request.StrategyRunGeneration,
    Generation = request.Generation,
    ClientOrderId = request.ClientOrderId,
    CycleId = request.CycleId
};
ExecutionEvent execution = new()
{
    StrategyInstanceId = request.StrategyInstanceId,
    StrategyRunGeneration = request.StrategyRunGeneration,
    Generation = request.Generation,
    ClientOrderId = request.ClientOrderId,
    CycleId = request.CycleId
};
OrderLifecycleEvent lifecycle = new()
{
    StrategyInstanceId = request.StrategyInstanceId,
    StrategyRunGeneration = request.StrategyRunGeneration,
    Generation = request.Generation,
    ClientOrderId = request.ClientOrderId,
    CycleId = request.CycleId
};

Assert(request.StrategyRunGeneration == 17 && order.StrategyRunGeneration == 17 &&
       execution.StrategyRunGeneration == 17 && lifecycle.StrategyRunGeneration == 17,
    "Strategy run generation did not survive order and event models");
Assert(request.Generation == 3 && order.Generation == 3 && execution.Generation == 3 && lifecycle.Generation == 3,
    "Order reprice generation was overwritten by strategy run generation");

var participant = new FakeRecoveryParticipant();
var checkpoint = new StrategyRecoveryCheckpoint(
    participant.StrategyType, participant.StrategyInstanceId, 17, "v1", DateTimeOffset.UtcNow, "{}");
StrategyRecoveryCheckpointValidator.EnsureCompatible(checkpoint, participant, 17);
try
{
    StrategyRecoveryCheckpointValidator.EnsureCompatible(checkpoint with { Schema = "v0" }, participant, 17);
    throw new InvalidOperationException("Mismatched recovery schema was accepted");
}
catch (InvalidOperationException exception) when (exception.Message.Contains("schema", StringComparison.OrdinalIgnoreCase))
{
}

var capture = new CapturingExecutionService();
var strategy = new RequestOnStartStrategy();
using (var runner = new StrategyInstanceRunner(
    strategy,
    new ResolvedStrategyUniverse { Tradable = new ResolvedProduct { ProductId = "225MC", Symbol = "225MC-Z26" } },
    new PaperPortfolioLedger(), new SystemStrategyClock(), 0m, capture, strategyRunGeneration: 17))
{
    Assert(capture.Submitted?.StrategyInstanceId == strategy.InstanceId &&
           capture.Submitted.StrategyRunGeneration == 17,
        "Runner did not stamp the owning instance and run generation onto strategy requests");
}

Console.WriteLine("PASS Task 1 strategy identity and checkpoint schema");

string storeDirectory = Path.Combine(Path.GetTempPath(), "kgi-live-route-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(storeDirectory);
try
{
    var store = new LiveOrderOwnershipStore(storeDirectory);
    LiveOrderOwnerKey ownerA = new("MR-A", 4, "entry-1");
    LiveOrderOwnerKey ownerB = new("MR-B", 4, "entry-1");
    store.Prepare(new LiveOrderOwnership { Key = ownerA, Account = "ACC", Symbol = "225MC-Z26", Quantity = 1 });
    store.Prepare(new LiveOrderOwnership { Key = ownerB, Account = "ACC", Symbol = "225MC-Z26", Quantity = 1 });
    try
    {
        store.Prepare(new LiveOrderOwnership { Key = ownerA, Account = "ACC", Symbol = "225MC-Z26", Quantity = 1 });
        throw new InvalidOperationException("duplicate owner key was accepted");
    }
    catch (InvalidOperationException exception) when (exception.Message.Contains("owner", StringComparison.OrdinalIgnoreCase)) { }
    store.BindAlias(ownerA, new BrokerOrderAlias("ACC", "20260923", "OrdNo", "ORD-1"));
    try
    {
        store.BindAlias(ownerB, new BrokerOrderAlias("ACC", "20260923", "OrdNo", "ORD-1"));
        throw new InvalidOperationException("duplicate broker alias was accepted");
    }
    catch (InvalidOperationException exception) when (exception.Message.Contains("alias", StringComparison.OrdinalIgnoreCase)) { }
    Assert(store.TryAcceptFill(ownerA, "EXEC-1", 1).Accepted, "new exec id was not accepted");
    Assert(!store.TryAcceptFill(ownerA, "EXEC-1", 1).Accepted, "duplicate exec id was accepted twice");
    Assert(store.TryAcceptFill(ownerB, null, 1).Accepted, "new cumulative fill was not accepted");
    Assert(!store.TryAcceptFill(ownerB, null, 1).Accepted, "unchanged cumulative watermark was accepted twice");
    store.MarkSubmissionUnknown(ownerA, "gateway timeout");
    store.Dispose();
    File.AppendAllText(Path.Combine(storeDirectory, "ownership.jsonl"), "{\"Sequence\":");
    using var restarted = new LiveOrderOwnershipStore(storeDirectory);
    LiveOrderOwnership restoredUnknown = restarted.LoadOpenRoutes("ACC", "225MC-Z26").Single(x => x.Key == ownerA);
    Assert(restoredUnknown.SubmissionState == StrategySubmissionState.Unknown, "submission unknown did not survive restart");
    Assert(restarted.LoadOpenRoutes("ACC", "225MC-Z26").Count == 2, "valid routes were lost after truncated tail recovery");

    string corruptDirectory = Path.Combine(Path.GetTempPath(), "kgi-live-route-corrupt-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(corruptDirectory);
    try
    {
        using (var clean = new LiveOrderOwnershipStore(corruptDirectory))
            clean.Prepare(new LiveOrderOwnership { Key = ownerA, Account = "ACC", Symbol = "225MC-Z26", Quantity = 1 });
        File.AppendAllText(Path.Combine(corruptDirectory, "ownership.jsonl"), "{\"Sequence\":2,\"broken\":true}\n");
        using var corrupt = new LiveOrderOwnershipStore(corruptDirectory);
        Assert(corrupt.Replay().IsQuarantined, "complete corrupted journal record did not quarantine ownership data");
    }
    finally
    {
        Directory.Delete(corruptDirectory, recursive: true);
    }
}
finally
{
    Directory.Delete(storeDirectory, recursive: true);
}
Console.WriteLine("PASS Task 2 ownership journal persistence, idempotency and corruption handling");

string routerDirectory = Path.Combine(Path.GetTempPath(), "kgi-live-router-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(routerDirectory);
try
{
    using var routeStore = new LiveOrderOwnershipStore(routerDirectory);
    var rawGateway = new CountingLiveGateway();
    using var router = new LiveStrategyOrderRouter(rawGateway, routeStore);
    ILiveStrategyOrderGateway facadeA = router.Register("ROUTER-A", 8);
    ILiveStrategyOrderGateway facadeB = router.Register("ROUTER-B", 8);
    Assert(rawGateway.OrderStateSubscriberCount == 1 && rawGateway.OrderFilledSubscriberCount == 1 &&
           rawGateway.OrderFailedSubscriberCount == 1 && rawGateway.CancelFailedSubscriberCount == 1,
        "multiple strategy facades installed multiple broker event subscribers");

    int fillsA = 0;
    int fillsB = 0;
    facadeA.OrderFilled += (_, _, _) => fillsA++;
    facadeB.OrderFilled += (_, _, _) => fillsB++;
    bool routeWasDurableAtGatewayBoundary = false;
    rawGateway.OnPlaceOrder = request => routeWasDurableAtGatewayBoundary = routeStore.Replay().Routes.Any(route =>
        route.Key.ClientOrderId == request.StrategyRoute?.ClientOrderId);
    var owner = new StrategyOrderRouteMetadata("ROUTER-A", 8, "same-client", "cycle-a", "225MC-Z26", "EntryLong");
    facadeA.PlaceOrder(new OrderRequest
    {
        OrderId = "LOCAL-A",
        Account = "ACC",
        BrokerId = "KGI",
        Quantity = 1,
        StrategyRoute = owner
    });
    Assert(rawGateway.PlaceOrderCalls == 1, "central router did not submit exactly once");
    Assert(routeWasDurableAtGatewayBoundary, "gateway was called before ownership had been durably journaled");
    Assert(routeStore.Replay().Routes.Single().Key == new LiveOrderOwnerKey("ROUTER-A", 8, "same-client"),
        "ownership was not durable before broker submission");
    rawGateway.RaiseFilled(new OrderInfo
    {
        OrderId = "LOCAL-A",
        Account = "ACC",
        Identity = new ExternalOrderIdentity { OrdNo = "BRK-A", ExecID = "EXEC-A" },
        FilledQty = 1,
        StrategyRoute = owner
    });
    rawGateway.RaiseFilled(new OrderInfo
    {
        OrderId = "LOCAL-A",
        Account = "ACC",
        Identity = new ExternalOrderIdentity { OrdNo = "BRK-A", ExecID = "EXEC-A" },
        FilledQty = 1,
        StrategyRoute = owner
    });
    Assert(fillsA == 1 && fillsB == 0, "broker fill was not delivered only to its precise owner facade");

    rawGateway.ThrowOnPlace = true;
    var unknownOwner = new StrategyOrderRouteMetadata("ROUTER-B", 8, "timeout-client", "cycle-b", "225MC-Z26", "EntryShort");
    try
    {
        facadeB.PlaceOrder(new OrderRequest
        {
            OrderId = "LOCAL-B", Account = "ACC", BrokerId = "KGI", Quantity = 1, StrategyRoute = unknownOwner
        });
        throw new InvalidOperationException("gateway timeout was hidden");
    }
    catch (BrokerReconciliationRequiredException) { }
    Assert(routeStore.LoadOpenRoutes("ACC", "225MC-Z26").Single(x => x.Key.StrategyInstanceId == "ROUTER-B").SubmissionState == StrategySubmissionState.Unknown,
        "uncertain gateway response did not retain the owner in SubmissionUnknown");

    routeStore.BindAlias(new LiveOrderOwnerKey("ROUTER-B", 8, "timeout-client"),
        new BrokerOrderAlias("ACC", "20260923", "RequestId", "REQUEST-B"));
    rawGateway.RaiseState(new OrderInfo
    {
        OrderId = "LOCAL-A",
        Account = "ACC",
        Instrument = new InstrumentKey { Symbol = "225MC-Z26" },
        Identity = new ExternalOrderIdentity { RequestId = "REQUEST-B" }
    });
    Assert(router.GetQuarantine().Any(item => item.EventType == "OrderStateChanged"),
        "broker event matching two distinct owners was not quarantined");
    try
    {
        facadeA.PlaceOrder(new OrderRequest
        {
            OrderId = "LOCAL-A2", Account = "ACC", Quantity = 1,
            StrategyRoute = new StrategyOrderRouteMetadata("ROUTER-A", 8, "another-client", "cycle-a2", "225MC-Z26", "EntryLong")
        });
        throw new InvalidOperationException("quarantined account/product accepted a new order");
    }
    catch (BrokerReconciliationRequiredException) { }

    var closedStore = new LiveOrderOwnershipStore(Path.Combine(routerDirectory, "closed"));
    closedStore.Dispose();
    var closedGateway = new CountingLiveGateway();
    using (var closedRouter = new LiveStrategyOrderRouter(closedGateway, closedStore))
    {
        ILiveStrategyOrderGateway closedFacade = closedRouter.Register("CLOSED", 9);
        try
        {
            closedFacade.PlaceOrder(new OrderRequest
            {
                OrderId = "LOCAL-C", Account = "ACC", Quantity = 1,
                StrategyRoute = new StrategyOrderRouteMetadata("CLOSED", 9, "client-c", "cycle-c", "225MC-Z26", "EntryLong")
            });
            throw new InvalidOperationException("journal failure did not block broker submission");
        }
        catch (ObjectDisposedException) { }
    }
    Assert(closedGateway.PlaceOrderCalls == 0, "broker order escaped after ownership persistence failed");
}
finally
{
    Directory.Delete(routerDirectory, recursive: true);
}
Console.WriteLine("PASS Task 3 single-subscription exact route dispatch");

var strategyLedger = new PaperPortfolioLedger();
strategyLedger.ApplyExecution(new ExecutionEvent { StrategyInstanceId = "A", StrategyRunGeneration = 1, ExecutionId = "A-1", Symbol = "225MC-Z26", Side = BuySell.Buy, Price = 100m, Quantity = 1, Commission = 2m });
strategyLedger.ApplyExecution(new ExecutionEvent { StrategyInstanceId = "B", StrategyRunGeneration = 1, ExecutionId = "B-1", Symbol = "225MC-Z26", Side = BuySell.Sell, Price = 100m, Quantity = 1, Commission = 2m });
strategyLedger.ApplyExecution(new ExecutionEvent { StrategyInstanceId = "A", StrategyRunGeneration = 1, ExecutionId = "A-1", Symbol = "225MC-Z26", Side = BuySell.Buy, Price = 100m, Quantity = 1, Commission = 2m });
Assert(strategyLedger.GetPosition("A", 1, "225MC-Z26").NetQuantity == 1 &&
       strategyLedger.GetPosition("B", 1, "225MC-Z26").NetQuantity == -1 &&
       strategyLedger.GetStrategyNetQuantity("225MC-Z26") == 0 &&
       strategyLedger.GetPosition("A", 1, "225MC-Z26").Costs == 2m,
    "opposite strategies were netted together or duplicate fill counted twice");
var accountLedger = new AccountPositionLedger();
accountLedger.ReplaceBrokerSnapshot("ACC", new[] { new BrokerPositionRow("ACC", "225MC-Z26", 1) });
accountLedger.ApplyManualExternalFill(new AccountFill("ACC", "225MC-Z26", "MANUAL-1", BuySell.Buy, 1));
AccountProductSnapshot reconciled = accountLedger.Reconcile("ACC", "225MC-Z26", strategyLedger.GetAllPositions("225MC-Z26"));
Assert(reconciled.StrategyNetQuantity == 0 && reconciled.ManualExternalNetQuantity == 1 &&
       reconciled.BrokerNetQuantity == 1 && reconciled.DifferenceQuantity == 0,
    "manual external fill was not isolated from strategy buckets or reconciliation difference");
Assert(AccountPositionEffectResolver.Resolve(1, BuySell.Sell, 1) == PositionEffect.Close &&
       AccountPositionEffectResolver.Resolve(1, BuySell.Buy, 1) == PositionEffect.Open,
    "broker open/close effect did not use account net position independently of strategy ownership");
try
{
    AccountPositionEffectResolver.Resolve(1, BuySell.Sell, 2);
    throw new InvalidOperationException("mixed close/open order was not required to split");
}
catch (InvalidOperationException exception) when (exception.Message.Contains("拆", StringComparison.Ordinal)) { }
OrderRequest mappedStrategyOrder = LiveStrategyOrderMapper.CreateBrokerLimit(
    request with { Type = PaperOrderType.BrokerLimit }, new InstrumentKey { Symbol = "225MC-Z26" }, "KGI", "ACC", "real-1", accountNetQuantity: 1);
Assert(mappedStrategyOrder.PositionEffect == PositionEffect.Open &&
       mappedStrategyOrder.StrategyRoute?.StrategyInstanceId == "MR-A",
    "account position effect resolution changed or discarded immutable strategy ownership");
var ledgerCheckpoint = strategyLedger.Capture();
var restoredLedger = new PaperPortfolioLedger();
restoredLedger.Restore(ledgerCheckpoint);
Assert(restoredLedger.GetPosition("A", 1, "225MC-Z26").NetQuantity == 1,
    "strategy ledger checkpoint did not restore the exact run generation position");
Console.WriteLine("PASS Task 4 strategy/account ledger separation and execution idempotency");

var orderBookA = new ManagedStrategyOrderBook("A", 11);
var orderBookB = new ManagedStrategyOrderBook("B", 11);
string clientA = orderBookA.NextClientOrderId("EntryLong", "cycle-1");
string clientB = orderBookB.NextClientOrderId("EntryLong", "cycle-1");
Assert(clientA != clientB, "different strategy instances generated a colliding client order id");
PaperOrderRequest managedRequestA = new()
{
    StrategyInstanceId = "A", StrategyRunGeneration = 11, ClientOrderId = clientA,
    CycleId = "cycle-1", Generation = 3, Symbol = "225MC-Z26", Quantity = 1
};
orderBookA.Track(managedRequestA);
orderBookA.Apply(new OrderLifecycleEvent
{
    StrategyInstanceId = "A", StrategyRunGeneration = 11, ClientOrderId = clientA,
    CycleId = "cycle-1", Generation = 3, State = OrderLifecycleState.Cancelled
});
Assert(orderBookA.ActiveClientOrderIds.Count == 0, "cancel ack left an order active");
Assert(orderBookA.Apply(new OrderLifecycleEvent
{
    StrategyInstanceId = "A", StrategyRunGeneration = 11, ClientOrderId = clientA,
    CycleId = "cycle-1", Generation = 3, State = OrderLifecycleState.Filled, FilledQuantity = 1
}), "late fill after cancel ack lost its original order route");
ManagedOrderBookCheckpoint managedCheckpoint = orderBookA.Capture();
var restoredOrderBook = new ManagedStrategyOrderBook("A", 11);
restoredOrderBook.Restore(managedCheckpoint);
Assert(restoredOrderBook.ActiveClientOrderIds.Count == 0 &&
       restoredOrderBook.TryGet(clientA, out PaperOrderRequest? restoredRequest) &&
       restoredRequest?.Generation == 3,
    "managed order checkpoint did not preserve order generation and terminal lifecycle");
Assert(restoredOrderBook.NextClientOrderId("EntryLong", "cycle-1") != clientA,
    "restored managed order book reused an earlier client order id");
try
{
    new ManagedStrategyOrderBook("A", 12).Restore(managedCheckpoint);
    throw new InvalidOperationException("managed order checkpoint accepted another run generation");
}
catch (InvalidOperationException exception) when (exception.Message.Contains("generation", StringComparison.OrdinalIgnoreCase)) { }
try
{
    StrategyLiveCapabilityGate.EnsureLiveCapable(strategy);
    throw new InvalidOperationException("legacy strategy unexpectedly passed the live capability gate");
}
catch (InvalidOperationException exception) when (exception.Message.Contains(nameof(IManagedOrderStrategy), StringComparison.Ordinal)) { }
Console.WriteLine("PASS Task 5 managed order lifecycle and checkpoint helper");

var sharedUniverse = new ResolvedStrategyUniverse
{
    Tradable = new ResolvedProduct { ProductId = "225MC", Symbol = "225MC-Z26", TickSize = 5m, ContractMultiplier = 10m },
    References = new[]
    {
        new ResolvedProduct { ProductId = "225", Symbol = "225-Z26", TickSize = 5m, ContractMultiplier = 10m },
        new ResolvedProduct { ProductId = "225M", Symbol = "225M-Z26", TickSize = 5m, ContractMultiplier = 10m }
    }
};
ISimulationStrategy[] firstMigratedStrategies =
{
    new MeanReversionScalperFactory().Create(new StrategyDefinition { StrategyType = MeanReversionScalperFactory.TypeName, InstanceId = "MR-managed" }),
    new WaveletScalperFactory().Create(new StrategyDefinition { StrategyType = WaveletScalperFactory.TypeName, InstanceId = "MODWT-managed" }),
    new NikkeiMicrostructureAlphaFactory().Create(new StrategyDefinition { StrategyType = NikkeiMicrostructureAlphaFactory.TypeName, InstanceId = "NIKKEI-managed" })
};
foreach (ISimulationStrategy migrated in firstMigratedStrategies)
{
    StrategyLiveCapabilityGate.EnsureLiveCapable(migrated);
    migrated.Start(new StrategyRuntimeContext(sharedUniverse, new SystemStrategyClock(), new PaperPortfolioLedger(), 17));
    migrated.OnMarketEvent(new MarketEvent
    {
        Sequence = 1, Kind = MarketEventKind.Trade, ProductId = "225MC", Symbol = "225MC-Z26",
        LastPrice = 100m, Timestamp = DateTimeOffset.UtcNow.AddSeconds(-1),
        BidPrices = new[] { 95m }, AskPrices = new[] { 100m }, BidQuantities = new[] { 10 }, AskQuantities = new[] { 10 }
    });
    IStrategyRecoveryParticipant recoveryParticipant = (IStrategyRecoveryParticipant)migrated;
    StrategyRecoveryCheckpoint saved = recoveryParticipant.CaptureRecoveryCheckpoint();
    recoveryParticipant.RestoreRecoveryCheckpoint(saved);
    Assert(saved.StrategyRunGeneration == 17 && !string.IsNullOrWhiteSpace(saved.PayloadJson) &&
           migrated.GetSnapshot().State == StrategyLifecycleState.WarmingUp &&
           recoveryParticipant.CaptureRecoveryCheckpoint().PayloadJson == saved.PayloadJson,
        $"{migrated.StrategyType} checkpoint did not preserve its run identity and payload");
    IStrategyFactory factory = migrated.StrategyType switch
    {
        MeanReversionScalperFactory.TypeName => new MeanReversionScalperFactory(),
        WaveletScalperFactory.TypeName => new WaveletScalperFactory(),
        NikkeiMicrostructureAlphaFactory.TypeName => new NikkeiMicrostructureAlphaFactory(),
        _ => throw new InvalidOperationException("Unexpected migrated strategy factory.")
    };
    ISimulationStrategy restoredBeforeStart = factory.Create(new StrategyDefinition
    {
        StrategyType = migrated.StrategyType,
        InstanceId = migrated.InstanceId
    });
    ((IStrategyRecoveryParticipant)restoredBeforeStart).RestoreRecoveryCheckpoint(saved);
    restoredBeforeStart.Start(new StrategyRuntimeContext(sharedUniverse, new SystemStrategyClock(), new PaperPortfolioLedger(), 17));
    Assert(restoredBeforeStart.GetSnapshot().State == migrated.GetSnapshot().State &&
           ((IStrategyRecoveryParticipant)restoredBeforeStart).CaptureRecoveryCheckpoint().PayloadJson == saved.PayloadJson,
        $"{migrated.StrategyType} did not restore safely before Start without replaying entry logic");
}
Console.WriteLine("PASS Task 6 MR, MODWT and Nikkei use uniform managed-order/recovery contracts");

sealed class FakeRecoveryParticipant : IStrategyRecoveryParticipant
{
    public string StrategyType => "Fake";
    public string StrategyInstanceId => "MR-A";
    public string RecoverySchema => "v1";
    public StrategyRecoveryCheckpoint CaptureRecoveryCheckpoint() => new(StrategyType, StrategyInstanceId, 17, RecoverySchema, DateTimeOffset.UtcNow, "{}");
    public void RestoreRecoveryCheckpoint(StrategyRecoveryCheckpoint checkpoint) { }
}

sealed class RequestOnStartStrategy : ISimulationStrategy
{
    public string StrategyType => "Fake";
    public string InstanceId => "runner-owner";
    public StrategyLifecycleState State => StrategyLifecycleState.Ready;
    public event Action<PaperOrderRequest>? OrderRequested;
    public event Action? CancelAllRequested;
    public event Action<StrategySnapshot>? SnapshotChanged;
    public void Start(StrategyRuntimeContext context) => OrderRequested?.Invoke(new PaperOrderRequest
    {
        StrategyInstanceId = "forged-instance",
        StrategyRunGeneration = 999,
        ClientOrderId = "entry-1",
        CycleId = "cycle-1",
        Symbol = "225MC-Z26",
        Side = BuySell.Buy,
        Type = PaperOrderType.Limit,
        Role = PaperOrderRole.EntryLong,
        Price = 100m,
        Quantity = 1
    });
    public void OnMarketEvent(MarketEvent marketEvent) { }
    public void OnExecution(ExecutionEvent execution) { }
    public void Pause(string reason) { }
    public void Resume() { }
    public void Stop(string reason) { }
    public StrategySnapshot GetSnapshot() => new() { StrategyType = StrategyType, InstanceId = InstanceId };
}

sealed class CapturingExecutionService : IStrategyExecutionService
{
    public PaperOrderRequest? Submitted { get; private set; }
    public event Action<ExecutionEvent>? Filled;
    public event Action<string>? Failed;
    public PaperOrder Submit(PaperOrderRequest request, DateTimeOffset now)
    {
        Submitted = request;
        return new PaperOrder { OrderId = "TEST", StrategyInstanceId = request.StrategyInstanceId,
            StrategyRunGeneration = request.StrategyRunGeneration, ClientOrderId = request.ClientOrderId,
            CycleId = request.CycleId, Generation = request.Generation, Symbol = request.Symbol,
            Side = request.Side, Type = request.Type, Role = request.Role, Price = request.Price,
            Quantity = request.Quantity, State = PaperOrderState.Working, CreatedAt = now };
    }
    public bool Cancel(string orderId) => true;
    public int CancelAll(string strategyInstanceId) => 0;
    public IReadOnlyList<PaperOrder> GetWorkingOrders(string strategyInstanceId) => Array.Empty<PaperOrder>();
    public void OnMarketEvent(MarketEvent marketEvent) { }
}

sealed class CountingLiveGateway : ILiveStrategyOrderGateway
{
    private Action<OrderInfo>? _orderStateChanged;
    private Action<OrderInfo, int, double>? _orderFilled;
    private Action<OrderInfo, string>? _orderFailed;
    private Action<OrderInfo, string>? _cancelFailed;
    public bool IsLoggedIn => true;
    public string BrokerId => "KGI";
    public string Account => "ACC";
    public int OrderStateSubscriberCount { get; private set; }
    public int OrderFilledSubscriberCount { get; private set; }
    public int OrderFailedSubscriberCount { get; private set; }
    public int CancelFailedSubscriberCount { get; private set; }
    public int PlaceOrderCalls { get; private set; }
    public bool ThrowOnPlace { get; set; }
    public Action<OrderRequest>? OnPlaceOrder { get; set; }
    public event Action<OrderInfo>? OrderStateChanged
    {
        add { OrderStateSubscriberCount++; _orderStateChanged += value; }
        remove { OrderStateSubscriberCount--; _orderStateChanged -= value; }
    }
    public event Action<OrderInfo, int, double>? OrderFilled
    {
        add { OrderFilledSubscriberCount++; _orderFilled += value; }
        remove { OrderFilledSubscriberCount--; _orderFilled -= value; }
    }
    public event Action<OrderInfo, string>? OrderFailed
    {
        add { OrderFailedSubscriberCount++; _orderFailed += value; }
        remove { OrderFailedSubscriberCount--; _orderFailed -= value; }
    }
    public event Action<OrderInfo, string>? CancelFailed
    {
        add { CancelFailedSubscriberCount++; _cancelFailed += value; }
        remove { CancelFailedSubscriberCount--; _cancelFailed -= value; }
    }
    public void PlaceOrder(OrderRequest request)
    {
        PlaceOrderCalls++;
        OnPlaceOrder?.Invoke(request);
        if (ThrowOnPlace) throw new IOException("uncertain gateway response");
        _orderStateChanged?.Invoke(new OrderInfo
        {
            OrderId = request.OrderId,
            Account = request.Account,
            StrategyRoute = request.StrategyRoute,
            Status = OrderStatus.Submitted
        });
    }
    public void CancelOrder(string orderId) { }
    public void RaiseFilled(OrderInfo order) => _orderFilled?.Invoke(order, 1, 100d);
    public void RaiseState(OrderInfo order) => _orderStateChanged?.Invoke(order);
}
