using KGIOptionPricing.StrategyFramework;
using KGIOptionPricing.StrategyFramework.Recovery;
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
