using KGIOptionPricing.OrderManagement.Core;
using KGIOptionPricing.StrategyFramework.Execution;
using KGIOptionPricing.StrategyFramework.Performance;

namespace KGIOptionPricing.StrategyFramework.Recovery;

public interface ILiveBrokerRecoverySource
{
    BrokerRecoverySnapshot Capture(string account);
}

public enum LiveRecoveryState { Recovering, RecoveredPaused, ReconciliationRequired, Quarantined }

public sealed record BrokerExecutionRow(string Account, string Symbol, string ExecutionId,
    int CumulativeQuantity, decimal Price, BuySell Side, BrokerOrderAlias Alias, DateTimeOffset Timestamp);
public sealed record BrokerRecoverySnapshot(IReadOnlyList<OrderInfo> Orders,
    IReadOnlyList<BrokerExecutionRow> Executions, IReadOnlyList<BrokerPositionRow> Positions,
    DateTimeOffset CapturedAt);
public sealed record LiveRecoveryResult(LiveRecoveryState State, string Reason,
    IReadOnlyList<AccountProductSnapshot> Reconciliations,
    IReadOnlyList<LiveOrderOwnerKey> MatchedOwners);

/// <summary>Fail-closed recovery matching: only persisted broker aliases establish ownership.</summary>
public sealed class LiveStrategyRecoveryCoordinator
{
    private readonly ILiveOrderOwnershipStore _store;
    private readonly IAccountPositionLedger _accountLedger;
    private readonly PaperPortfolioLedger _strategyLedger;

    public LiveStrategyRecoveryCoordinator(ILiveOrderOwnershipStore store, IAccountPositionLedger accountLedger,
        PaperPortfolioLedger strategyLedger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _accountLedger = accountLedger ?? throw new ArgumentNullException(nameof(accountLedger));
        _strategyLedger = strategyLedger ?? throw new ArgumentNullException(nameof(strategyLedger));
    }

    public LiveRecoveryResult Recover(string account, BrokerRecoverySnapshot broker,
        IReadOnlyList<PositionSnapshot> strategyPositions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(account);
        ArgumentNullException.ThrowIfNull(broker);
        ArgumentNullException.ThrowIfNull(strategyPositions);
        LiveOwnershipSnapshot journal = _store.Replay();
        if (journal.IsQuarantined)
            return new(LiveRecoveryState.Quarantined, journal.QuarantineReason, Array.Empty<AccountProductSnapshot>(), Array.Empty<LiveOrderOwnerKey>());
        if (journal.QuarantinedEvents.Count > 0)
            return new(LiveRecoveryState.Quarantined, "Ownership journal contains unresolved broker events.", Array.Empty<AccountProductSnapshot>(), Array.Empty<LiveOrderOwnerKey>());

        var matched = new HashSet<LiveOrderOwnerKey>();
        foreach (BrokerExecutionRow execution in broker.Executions)
        {
            if (!string.Equals(execution.Account, account, StringComparison.OrdinalIgnoreCase) ||
                !journal.Aliases.TryGetValue(execution.Alias, out LiveOrderOwnerKey? owner))
                return new(LiveRecoveryState.Quarantined, $"Execution {execution.ExecutionId} has no unique persisted owner alias.", Array.Empty<AccountProductSnapshot>(), matched.ToArray());
            LiveOrderOwnership? route = journal.Routes.SingleOrDefault(x => x.Key == owner);
            if (route is null || !string.Equals(route.Symbol, execution.Symbol, StringComparison.OrdinalIgnoreCase))
                return new(LiveRecoveryState.Quarantined, $"Execution {execution.ExecutionId} conflicts with immutable route identity.", Array.Empty<AccountProductSnapshot>(), matched.ToArray());
            FillAcceptance fill = _store.TryAcceptFill(owner, execution.ExecutionId, execution.CumulativeQuantity);
            if (fill.Accepted)
            {
                _strategyLedger.ApplyExecution(new ExecutionEvent
                {
                    StrategyInstanceId = owner.StrategyInstanceId,
                    StrategyRunGeneration = owner.StrategyRunGeneration,
                    ClientOrderId = owner.ClientOrderId,
                    CycleId = route.CycleId,
                    Symbol = route.Symbol,
                    ExecutionId = execution.ExecutionId,
                    Side = execution.Side,
                    Role = route.Role,
                    Quantity = fill.AcceptedQuantity,
                    Price = execution.Price,
                    Timestamp = execution.Timestamp
                });
                matched.Add(owner);
            }
        }

        _accountLedger.ReplaceBrokerSnapshot(account, broker.Positions);
        PositionSnapshot[] rebuiltPositions = strategyPositions.Concat(
                broker.Positions.SelectMany(position => _strategyLedger.GetAllPositions(position.Symbol)))
            .GroupBy(position => (position.StrategyInstanceId, position.StrategyRunGeneration, position.Symbol))
            .Select(group => group.OrderByDescending(position => position.NetQuantity != 0).First())
            .ToArray();
        string[] symbols = broker.Positions.Select(x => x.Symbol).Concat(rebuiltPositions.Select(x => x.Symbol))
            .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        AccountProductSnapshot[] reconciliations = symbols.Select(symbol =>
            _accountLedger.Reconcile(account, symbol, rebuiltPositions)).ToArray();
        if (reconciliations.Any(x => x.DifferenceQuantity != 0))
            return new(LiveRecoveryState.ReconciliationRequired, "Broker/account net and strategy plus manual ledgers differ; new orders remain disabled.", reconciliations, matched.ToArray());
        return new(LiveRecoveryState.RecoveredPaused, "Recovery matched by durable broker aliases; explicit Resume is required.", reconciliations, matched.ToArray());
    }
}
