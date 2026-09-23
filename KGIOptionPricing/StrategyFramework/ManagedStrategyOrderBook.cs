using System;
using System.Collections.Generic;
using System.Linq;
using KGIOptionPricing.StrategyFramework.Recovery;

namespace KGIOptionPricing.StrategyFramework;

public sealed record ManagedOrderRecord(PaperOrderRequest Request, OrderLifecycleEvent? LastLifecycle);
public sealed record ManagedOrderBookCheckpoint(string StrategyInstanceId, long StrategyRunGeneration,
    long NextClientSequence, IReadOnlyList<ManagedOrderRecord> Orders);

/// <summary>Common exact-client-id lifecycle index shared by managed live strategies.</summary>
public sealed class ManagedStrategyOrderBook
{
    private readonly object _sync = new();
    private readonly Dictionary<string, ManagedOrderRecord> _orders = new(StringComparer.Ordinal);
    private long _nextSequence;

    public ManagedStrategyOrderBook(string strategyInstanceId, long strategyRunGeneration)
    {
        if (string.IsNullOrWhiteSpace(strategyInstanceId)) throw new ArgumentException("StrategyInstanceId 不得空白。", nameof(strategyInstanceId));
        if (strategyRunGeneration <= 0) throw new ArgumentOutOfRangeException(nameof(strategyRunGeneration));
        StrategyInstanceId = strategyInstanceId;
        StrategyRunGeneration = strategyRunGeneration;
    }

    public string StrategyInstanceId { get; }
    public long StrategyRunGeneration { get; }

    public string NextClientOrderId(string role, string cycleId)
    {
        if (string.IsNullOrWhiteSpace(role) || string.IsNullOrWhiteSpace(cycleId))
            throw new ArgumentException("Role 與 CycleId 不得空白。");
        lock (_sync)
            return $"{StrategyInstanceId}:run-{StrategyRunGeneration}:{Uri.EscapeDataString(cycleId)}:{Uri.EscapeDataString(role)}:{++_nextSequence}";
    }

    public void Track(PaperOrderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ClientOrderId) || string.IsNullOrWhiteSpace(request.CycleId) ||
            !string.Equals(request.StrategyInstanceId, StrategyInstanceId, StringComparison.Ordinal) ||
            request.StrategyRunGeneration != StrategyRunGeneration || request.Quantity <= 0)
            throw new InvalidOperationException("Managed order identity/quantity does not match this strategy run.");
        lock (_sync)
        {
            if (!_orders.TryAdd(request.ClientOrderId, new ManagedOrderRecord(request, null)))
                throw new InvalidOperationException($"ClientOrderId already tracked: {request.ClientOrderId}");
        }
    }

    public bool Apply(OrderLifecycleEvent lifecycle)
    {
        ArgumentNullException.ThrowIfNull(lifecycle);
        if (!string.Equals(lifecycle.StrategyInstanceId, StrategyInstanceId, StringComparison.Ordinal) ||
            lifecycle.StrategyRunGeneration != StrategyRunGeneration || string.IsNullOrWhiteSpace(lifecycle.ClientOrderId))
            return false;
        lock (_sync)
        {
            if (!_orders.TryGetValue(lifecycle.ClientOrderId, out ManagedOrderRecord? record)) return false;
            PaperOrderRequest request = record.Request;
            if (lifecycle.CycleId != request.CycleId ||
                (lifecycle.Generation != 0 && lifecycle.Generation != request.Generation)) return false;
            if (record.LastLifecycle?.State is OrderLifecycleState.Filled or OrderLifecycleState.Rejected) return false;
            if (record.LastLifecycle?.State == OrderLifecycleState.Cancelled && lifecycle.State != OrderLifecycleState.Filled)
                return false;
            _orders[lifecycle.ClientOrderId] = record with { LastLifecycle = lifecycle };
            return true;
        }
    }

    public IReadOnlyList<string> ActiveClientOrderIds
    {
        get
        {
            lock (_sync) return _orders.Values
                .Where(x => x.LastLifecycle?.State is not (OrderLifecycleState.Filled or OrderLifecycleState.Cancelled or OrderLifecycleState.Rejected))
                .Select(x => x.Request.ClientOrderId).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        }
    }

    public bool TryGet(string clientOrderId, out PaperOrderRequest? request)
    {
        lock (_sync)
        {
            if (_orders.TryGetValue(clientOrderId, out ManagedOrderRecord? record)) { request = record.Request; return true; }
            request = null; return false;
        }
    }

    public bool HasActiveRole(PaperOrderRole role)
    {
        lock (_sync) return _orders.Values.Any(x => x.Request.Role == role &&
            x.LastLifecycle?.State is not (OrderLifecycleState.Filled or OrderLifecycleState.Cancelled or OrderLifecycleState.Rejected));
    }

    public ManagedOrderBookCheckpoint Capture()
    {
        lock (_sync) return new ManagedOrderBookCheckpoint(StrategyInstanceId, StrategyRunGeneration,
            _nextSequence, _orders.Values.ToArray());
    }

    public void Restore(ManagedOrderBookCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (!string.Equals(checkpoint.StrategyInstanceId, StrategyInstanceId, StringComparison.Ordinal) ||
            checkpoint.StrategyRunGeneration != StrategyRunGeneration)
            throw new InvalidOperationException("Managed order checkpoint identity or generation is incompatible.");
        lock (_sync)
        {
            _orders.Clear();
            foreach (ManagedOrderRecord record in checkpoint.Orders)
            {
                if (!string.Equals(record.Request.StrategyInstanceId, StrategyInstanceId, StringComparison.Ordinal) ||
                    record.Request.StrategyRunGeneration != StrategyRunGeneration ||
                    !_orders.TryAdd(record.Request.ClientOrderId, record))
                    throw new InvalidOperationException("Managed order checkpoint contains mismatched or duplicate ownership.");
            }
            _nextSequence = Math.Max(checkpoint.NextClientSequence, checkpoint.Orders.Count);
        }
    }
}

public static class StrategyLiveCapabilityGate
{
    public static void EnsureLiveCapable(ISimulationStrategy strategy)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        var missing = new List<string>();
        if (strategy is not IManagedOrderStrategy) missing.Add(nameof(IManagedOrderStrategy));
        if (strategy is not IManagedOrderSubmissionObserver) missing.Add(nameof(IManagedOrderSubmissionObserver));
        if (strategy is not IStrategyRecoveryParticipant) missing.Add(nameof(IStrategyRecoveryParticipant));
        if (missing.Count > 0)
            throw new InvalidOperationException($"{strategy.StrategyType} is not live-capable; missing: {string.Join(", ", missing)}.");
    }
}
