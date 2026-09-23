using System;
using System.Collections.Generic;
using KGIOptionPricing.StrategyFramework.Recovery;

namespace KGIOptionPricing.StrategyFramework;

public interface IStrategyPerformanceView
{
    PositionSnapshot GetPosition(string strategyInstanceId, string symbol);
}

public interface IStrategyExecutionService
{
    event Action<ExecutionEvent>? Filled;
    event Action<string>? Failed;
    PaperOrder Submit(PaperOrderRequest request, DateTimeOffset now);
    bool Cancel(string orderId);
    int CancelAll(string strategyInstanceId);
    IReadOnlyList<PaperOrder> GetWorkingOrders(string strategyInstanceId);
    void OnMarketEvent(MarketEvent marketEvent);
}

public interface IPaperExecutionService : IStrategyExecutionService { }

public interface IManagedStrategyExecutionService : IStrategyExecutionService
{
    event Action<OrderLifecycleEvent>? OrderLifecycleChanged;
    bool CancelByClientOrderId(string strategyInstanceId, string clientOrderId);
}

public interface ISimulationStrategy
{
    string StrategyType { get; }
    string InstanceId { get; }
    StrategyLifecycleState State { get; }
    event Action<PaperOrderRequest>? OrderRequested;
    event Action? CancelAllRequested;
    event Action<StrategySnapshot>? SnapshotChanged;
    void Start(StrategyRuntimeContext context);
    void OnMarketEvent(MarketEvent marketEvent);
    void OnExecution(ExecutionEvent execution);
    void Pause(string reason);
    void Resume();
    void Stop(string reason);
    StrategySnapshot GetSnapshot();
}

public interface IManagedOrderStrategy
{
    event Action<string>? CancelOrderRequested;
    void OnOrderLifecycle(OrderLifecycleEvent orderEvent);
}

public interface IStrategyRecoveryParticipant
{
    string RecoverySchema { get; }
    StrategyRecoveryCheckpoint CaptureRecoveryCheckpoint();
    void RestoreRecoveryCheckpoint(StrategyRecoveryCheckpoint checkpoint);
}

public interface IStrategyHeartbeat
{
    void OnHeartbeat(DateTimeOffset now);
}

public interface IStrategyFactory
{
    string StrategyType { get; }
    string DisplayName { get; }
    ISimulationStrategy Create(StrategyDefinition definition);
}

public sealed class StrategyRuntimeContext
{
    public ResolvedStrategyUniverse Universe { get; }
    public IStrategyClock Clock { get; }
    public IStrategyPerformanceView Performance { get; }

    public StrategyRuntimeContext(
        ResolvedStrategyUniverse universe,
        IStrategyClock clock,
        IStrategyPerformanceView performance)
    {
        Universe = universe;
        Clock = clock;
        Performance = performance;
    }
}

public interface IMarketEventSource : IDisposable
{
    event Action<MarketEvent>? MarketEventReceived;
    void Subscribe(IEnumerable<ResolvedProduct> products);
}
