using KGIOptionPricing.StrategyFramework;
using KGIOptionPricing.StrategyFramework.Recovery;
using KGIOptionPricing.StrategyFramework.Execution;
using KGIOptionPricing.OrderManagement.Core;
using KGIOptionPricing.StrategyFramework.Performance;

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
