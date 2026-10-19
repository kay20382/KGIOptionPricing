using KGIOptionPricing.OrderManagement.Core;

namespace KGIOptionPricing.StrategyFramework.Execution;

public readonly record struct RestingPairKey(
    string StrategyInstanceId, long StrategyRunGeneration, string CycleId, string PairId);

public enum RestingPairPhase
{
    Submitting, Working, Held, CancelPending, AwaitingFillSettlement,
    StopIntentSent, Settled, Quarantined
}

public enum RestingPairActionKind { CancelExactOpposite, SubmitResidualMarketStop, Quarantine }

public sealed record RestingPairAction(RestingPairActionKind Kind, string ClientOrderId,
    string BrokerOrderId, int Quantity, string Reason);

public sealed record RestingPairDecision(RestingPairPhase Phase, PaperOrderRole? EffectiveRole,
    IReadOnlyList<RestingPairAction> Actions);

public sealed record RestingPairLegSnapshot(string ClientOrderId, string BrokerOrderId,
    BuySell Side, PaperOrderRole OriginalRole, int Quantity, int DeliveredQuantity);

public sealed record RestingPairExecutionKey(string ClientOrderId, string ExecutionId);

public sealed record RestingPairSnapshot
{
    public RestingPairKey Key { get; init; }
    public RestingPairPhase Phase { get; init; }
    public RestingPairLegSnapshot? Buy { get; init; }
    public RestingPairLegSnapshot? Sell { get; init; }
    public string FirstFilledClientOrderId { get; init; } = string.Empty;
    public long? LastBrokerSequence { get; init; }
    public IReadOnlyList<RestingPairExecutionKey> ExecutionKeys { get; init; } =
        Array.Empty<RestingPairExecutionKey>();
    public int? SettledOppositeCumulative { get; init; }
    public string StopMarketClientOrderId { get; init; } = string.Empty;
    public int StopMarketQuantity { get; init; }
}

/// <summary>
/// Owner-scoped transitions for a pair of original resting entry orders. The caller persists
/// state and performs broker operations; this class never sends or cancels an order itself.
/// </summary>
public sealed class ManagedRestingPairCoordinator
{
    private sealed record Leg(string ClientOrderId, string BrokerOrderId, BuySell Side,
        PaperOrderRole OriginalRole, int Quantity)
    {
        public int DeliveredQuantity { get; set; }
    }

    private sealed class PairState
    {
        public Leg? Buy { get; set; }
        public Leg? Sell { get; set; }
        public Leg? FirstFilled { get; set; }
        public long? LastBrokerSequence { get; set; }
        public HashSet<(string ClientOrderId, string ExecutionId)> ExecutionIds { get; } = new();
        public int? SettledOppositeCumulative { get; set; }
        public string StopMarketClientOrderId { get; set; } = string.Empty;
        public int StopMarketQuantity { get; set; }
        public RestingPairPhase Phase { get; set; } = RestingPairPhase.Submitting;
    }

    private readonly object _sync = new();
    private readonly Dictionary<RestingPairKey, PairState> _states = new();

    public RestingPairSnapshot Snapshot(RestingPairKey key)
    {
        ValidateKey(key);
        lock (_sync)
        {
            if (!_states.TryGetValue(key, out PairState? state))
                throw new KeyNotFoundException("Paired cycle does not exist.");
            return new RestingPairSnapshot
            {
                Key = key,
                Phase = state.Phase,
                Buy = Copy(state.Buy),
                Sell = Copy(state.Sell),
                FirstFilledClientOrderId = state.FirstFilled?.ClientOrderId ?? string.Empty,
                LastBrokerSequence = state.LastBrokerSequence,
                ExecutionKeys = state.ExecutionIds
                    .OrderBy(item => item.ClientOrderId, StringComparer.Ordinal)
                    .ThenBy(item => item.ExecutionId, StringComparer.Ordinal)
                    .Select(item => new RestingPairExecutionKey(item.ClientOrderId, item.ExecutionId))
                    .ToArray(),
                SettledOppositeCumulative = state.SettledOppositeCumulative,
                StopMarketClientOrderId = state.StopMarketClientOrderId,
                StopMarketQuantity = state.StopMarketQuantity
            };
        }
    }

    public IReadOnlyList<RestingPairSnapshot> GetSnapshots(string strategyInstanceId,
        long strategyRunGeneration)
    {
        lock (_sync)
        {
            return _states.Keys.Where(key =>
                string.Equals(key.StrategyInstanceId, strategyInstanceId, StringComparison.OrdinalIgnoreCase) &&
                key.StrategyRunGeneration == strategyRunGeneration)
                .Select(Snapshot).ToArray();
        }
    }

    public void Restore(RestingPairSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ValidateKey(snapshot.Key);
        if (snapshot.Buy is { Side: not BuySell.Buy } or { OriginalRole: not PaperOrderRole.EntryLong } ||
            snapshot.Sell is { Side: not BuySell.Sell } or { OriginalRole: not PaperOrderRole.EntryShort } ||
            snapshot.Buy?.ClientOrderId == snapshot.Sell?.ClientOrderId && snapshot.Buy != null ||
            snapshot.Phase == RestingPairPhase.StopIntentSent &&
                (string.IsNullOrWhiteSpace(snapshot.StopMarketClientOrderId) || snapshot.StopMarketQuantity <= 0))
            throw new InvalidDataException("Paired cycle snapshot is inconsistent.");
        lock (_sync)
        {
            if (_states.ContainsKey(snapshot.Key))
                throw new InvalidOperationException("Paired cycle has already been restored.");
            var state = new PairState
            {
                Buy = RestoreLeg(snapshot.Buy),
                Sell = RestoreLeg(snapshot.Sell),
                Phase = snapshot.Phase,
                LastBrokerSequence = snapshot.LastBrokerSequence,
                SettledOppositeCumulative = snapshot.SettledOppositeCumulative,
                StopMarketClientOrderId = snapshot.StopMarketClientOrderId,
                StopMarketQuantity = snapshot.StopMarketQuantity
            };
            state.FirstFilled = snapshot.FirstFilledClientOrderId == state.Buy?.ClientOrderId
                ? state.Buy : snapshot.FirstFilledClientOrderId == state.Sell?.ClientOrderId
                    ? state.Sell : null;
            if (!string.IsNullOrWhiteSpace(snapshot.FirstFilledClientOrderId) && state.FirstFilled == null)
                throw new InvalidDataException("Paired cycle first fill does not match either original order.");
            foreach (RestingPairExecutionKey execution in snapshot.ExecutionKeys)
                state.ExecutionIds.Add((execution.ClientOrderId, execution.ExecutionId));
            _states.Add(snapshot.Key, state);
        }
    }

    private static RestingPairLegSnapshot? Copy(Leg? leg) => leg == null ? null :
        new RestingPairLegSnapshot(leg.ClientOrderId, leg.BrokerOrderId,
            leg.Side, leg.OriginalRole, leg.Quantity, leg.DeliveredQuantity);

    private static Leg? RestoreLeg(RestingPairLegSnapshot? snapshot) => snapshot == null ? null :
        new Leg(snapshot.ClientOrderId, snapshot.BrokerOrderId, snapshot.Side,
            snapshot.OriginalRole, snapshot.Quantity) { DeliveredQuantity = snapshot.DeliveredQuantity };

    public RestingPairDecision RegisterLeg(RestingPairKey key, string clientOrderId,
        string brokerOrderId, BuySell side, PaperOrderRole originalRole, int quantity = 1)
    {
        ValidateKey(key);
        if (string.IsNullOrWhiteSpace(clientOrderId) || string.IsNullOrWhiteSpace(brokerOrderId) ||
            quantity <= 0 || originalRole != (side == BuySell.Buy ? PaperOrderRole.EntryLong : PaperOrderRole.EntryShort))
            throw new ArgumentException("A paired leg requires an exact broker/client identity, side, entry role and quantity.");
        lock (_sync)
        {
            PairState state = GetOrCreate(key);
            if (state.Phase == RestingPairPhase.Quarantined)
                return Decision(state);
            Leg? prior = side == BuySell.Buy ? state.Buy : state.Sell;
            if (prior != null)
                return prior.ClientOrderId == clientOrderId && prior.BrokerOrderId == brokerOrderId &&
                    prior.Quantity == quantity ? Decision(state) : Quarantine(state, "Conflicting paired leg identity.");
            if (state.FirstFilled != null)
                return Quarantine(state, "Opposite leg submitted after first fill; incomplete pair.");
            var leg = new Leg(clientOrderId, brokerOrderId, side, originalRole, quantity);
            if (side == BuySell.Buy) state.Buy = leg;
            else state.Sell = leg;
            state.Phase = state.Buy != null && state.Sell != null
                ? RestingPairPhase.Working : RestingPairPhase.Submitting;
            return Decision(state);
        }
    }

    public RestingPairDecision RecordFill(RestingPairKey key, string clientOrderId,
        string brokerOrderId, string executionId, int cumulativeQuantity, int fillQuantity,
        long? brokerSequence)
    {
        ValidateKey(key);
        lock (_sync)
        {
            if (!_states.TryGetValue(key, out PairState? state))
                return new RestingPairDecision(RestingPairPhase.Quarantined, null,
                    [new RestingPairAction(RestingPairActionKind.Quarantine, clientOrderId, brokerOrderId, 0,
                        "No registered paired cycle owns this fill.")]);
            if (state.Phase == RestingPairPhase.Quarantined)
                return Decision(state);
            if (!string.IsNullOrWhiteSpace(executionId) &&
                state.ExecutionIds.Contains((clientOrderId, executionId)))
                return Decision(state);
            if (state.Phase is RestingPairPhase.StopIntentSent or RestingPairPhase.Settled)
                return Quarantine(state, "New limit fill arrived after the pair was settled for flatten.");
            Leg? leg = FindLeg(state, clientOrderId, brokerOrderId);
            if (leg == null || string.IsNullOrWhiteSpace(executionId) || fillQuantity <= 0 ||
                cumulativeQuantity != leg.DeliveredQuantity + fillQuantity ||
                cumulativeQuantity > leg.Quantity)
                return Quarantine(state, "Fill identity or cumulative quantity does not match its original order.");
            if (state.LastBrokerSequence.HasValue &&
                (!brokerSequence.HasValue || brokerSequence.Value <= state.LastBrokerSequence.Value))
                return Quarantine(state, "Cross-leg fill order cannot be proven.");
            if (state.FirstFilled != null && leg != state.FirstFilled &&
                cumulativeQuantity > state.FirstFilled.DeliveredQuantity)
                return Quarantine(state, "Opposite fill exceeds the first leg's owned quantity.");

            state.ExecutionIds.Add((clientOrderId, executionId));
            leg.DeliveredQuantity = cumulativeQuantity;
            state.LastBrokerSequence = brokerSequence;
            if (state.FirstFilled == null)
                state.FirstFilled = leg;
            if (state.Phase is RestingPairPhase.Working or RestingPairPhase.Submitting)
                state.Phase = RestingPairPhase.Held;
            PaperOrderRole effectiveRole = leg == state.FirstFilled
                ? leg.OriginalRole : PaperOrderRole.TakeProfit;
            return Decision(state, effectiveRole);
        }
    }

    public RestingPairDecision RequestFlatten(RestingPairKey key, string reason)
    {
        ValidateKey(key);
        lock (_sync)
        {
            if (!_states.TryGetValue(key, out PairState? state) || state.FirstFilled == null)
                return new RestingPairDecision(RestingPairPhase.Quarantined, null,
                    [new RestingPairAction(RestingPairActionKind.Quarantine, string.Empty, string.Empty, 0,
                        "No filled leg exists for flatten.")]);
            if (state.Phase is RestingPairPhase.CancelPending or RestingPairPhase.AwaitingFillSettlement or
                RestingPairPhase.StopIntentSent or RestingPairPhase.Settled or RestingPairPhase.Quarantined)
                return Decision(state);
            Leg? opposite = state.FirstFilled == state.Buy ? state.Sell : state.Buy;
            if (opposite == null)
                return Quarantine(state, "Opposite paired order is missing.");
            state.Phase = RestingPairPhase.CancelPending;
            return Decision(state, null, [new RestingPairAction(
                RestingPairActionKind.CancelExactOpposite, opposite.ClientOrderId,
                opposite.BrokerOrderId, 0, reason)]);
        }
    }

    public RestingPairDecision ConfirmTerminal(RestingPairKey key, string clientOrderId,
        string brokerOrderId, int? finalCumulativeQuantity, int deliveredFillQuantity,
        int remainingStrategyQuantity, bool hasFinalWatermark = true)
    {
        ValidateKey(key);
        lock (_sync)
        {
            if (!_states.TryGetValue(key, out PairState? state) || state.FirstFilled == null)
                return new RestingPairDecision(RestingPairPhase.Quarantined, null,
                    [new RestingPairAction(RestingPairActionKind.Quarantine, clientOrderId, brokerOrderId, 0,
                        "Unknown paired terminal report.")]);
            if (state.Phase == RestingPairPhase.Quarantined)
                return Decision(state);
            if (state.Phase is RestingPairPhase.StopIntentSent or RestingPairPhase.Settled)
                return state.SettledOppositeCumulative == finalCumulativeQuantity &&
                    finalCumulativeQuantity == deliveredFillQuantity
                    ? Decision(state)
                    : Quarantine(state, "Terminal report changed after stop intent was released.");
            Leg? opposite = state.FirstFilled == state.Buy ? state.Sell : state.Buy;
            if (state.Phase != RestingPairPhase.CancelPending || opposite == null ||
                opposite.ClientOrderId != clientOrderId || opposite.BrokerOrderId != brokerOrderId ||
                !hasFinalWatermark || !finalCumulativeQuantity.HasValue ||
                finalCumulativeQuantity.Value != deliveredFillQuantity ||
                deliveredFillQuantity != opposite.DeliveredQuantity || remainingStrategyQuantity < 0 ||
                remainingStrategyQuantity != state.FirstFilled.DeliveredQuantity - opposite.DeliveredQuantity)
                return Quarantine(state, "Cancel terminal, delivered fills and strategy position do not reconcile.");

            state.SettledOppositeCumulative = finalCumulativeQuantity;
            state.Phase = remainingStrategyQuantity == 0
                ? RestingPairPhase.Settled : RestingPairPhase.StopIntentSent;
            if (remainingStrategyQuantity == 0)
                return Decision(state);
            state.StopMarketClientOrderId = "stop-" + key.StrategyInstanceId + "-" +
                key.StrategyRunGeneration + "-" + key.CycleId;
            state.StopMarketQuantity = remainingStrategyQuantity;
            return Decision(state, null, [new RestingPairAction(
                RestingPairActionKind.SubmitResidualMarketStop,
                state.StopMarketClientOrderId,
                string.Empty, remainingStrategyQuantity, "Opposite order settled before residual flatten.")]);
        }
    }

    public RestingPairDecision CancelRejected(RestingPairKey key, string clientOrderId,
        string brokerOrderId)
    {
        ValidateKey(key);
        lock (_sync)
        {
            if (!_states.TryGetValue(key, out PairState? state))
                return new RestingPairDecision(RestingPairPhase.Quarantined, null,
                    [new RestingPairAction(RestingPairActionKind.Quarantine, clientOrderId, brokerOrderId,
                        0, "Unknown paired cancel rejection.")]);
            if (state.Phase == RestingPairPhase.Quarantined)
                return Decision(state);
            Leg? opposite = state.FirstFilled == state.Buy ? state.Sell : state.Buy;
            if (state.Phase != RestingPairPhase.CancelPending || opposite == null ||
                opposite.ClientOrderId != clientOrderId || opposite.BrokerOrderId != brokerOrderId)
                return Quarantine(state, "Cancel rejection identity does not match the pending opposite order.");
            return Quarantine(state, "Opposite order cancellation was rejected; market stop is unsafe.");
        }
    }

    public RestingPairDecision SubmissionFailed(RestingPairKey key, string clientOrderId,
        string brokerOrderId)
    {
        ValidateKey(key);
        lock (_sync)
        {
            if (!_states.TryGetValue(key, out PairState? state))
                return new RestingPairDecision(RestingPairPhase.Quarantined, null,
                    [new RestingPairAction(RestingPairActionKind.Quarantine, clientOrderId,
                        brokerOrderId, 0, "Failed paired leg is not registered.")]);
            if (state.Phase == RestingPairPhase.Quarantined)
                return Decision(state);
            Leg? failed = FindLeg(state, clientOrderId, brokerOrderId);
            Leg? other = failed == state.Buy ? state.Sell : state.Buy;
            state.Phase = RestingPairPhase.Quarantined;
            if (failed == null || other == null || state.FirstFilled != null)
                return Decision(state, null,
                    [new RestingPairAction(RestingPairActionKind.Quarantine,
                        clientOrderId, brokerOrderId, 0, "Incomplete paired submission needs position reconciliation.")]);
            return Decision(state, null,
                [new RestingPairAction(RestingPairActionKind.CancelExactOpposite,
                    other.ClientOrderId, other.BrokerOrderId, 0,
                    "One leg rejected; cancel the only known accepted sibling."),
                 new RestingPairAction(RestingPairActionKind.Quarantine,
                    clientOrderId, brokerOrderId, 0, "Paired submission failed.")]);
        }
    }

    public RestingPairDecision ConfirmMarketResult(RestingPairKey key, string clientOrderId,
        bool? accepted, int? finalCumulativeQuantity)
    {
        ValidateKey(key);
        lock (_sync)
        {
            if (!_states.TryGetValue(key, out PairState? state))
                return new RestingPairDecision(RestingPairPhase.Quarantined, null,
                    [new RestingPairAction(RestingPairActionKind.Quarantine, clientOrderId,
                        string.Empty, 0, "Unknown market-stop result.")]);
            if (state.Phase == RestingPairPhase.Quarantined)
                return Decision(state);
            if (state.Phase == RestingPairPhase.Settled &&
                state.StopMarketClientOrderId == clientOrderId && accepted == true &&
                finalCumulativeQuantity == state.StopMarketQuantity)
                return Decision(state);
            if (state.Phase != RestingPairPhase.StopIntentSent ||
                state.StopMarketClientOrderId != clientOrderId || accepted != true ||
                finalCumulativeQuantity != state.StopMarketQuantity)
                return Quarantine(state, "Market-stop result is unknown, rejected or not fully settled.");
            state.Phase = RestingPairPhase.Settled;
            return Decision(state);
        }
    }

    private static Leg? FindLeg(PairState state, string clientOrderId, string brokerOrderId) =>
        new[] { state.Buy, state.Sell }.FirstOrDefault(leg => leg != null &&
            leg.ClientOrderId == clientOrderId && leg.BrokerOrderId == brokerOrderId);

    private PairState GetOrCreate(RestingPairKey key)
    {
        if (!_states.TryGetValue(key, out PairState? state))
            _states[key] = state = new PairState();
        return state;
    }

    private static RestingPairDecision Decision(PairState state, PaperOrderRole? role = null,
        IReadOnlyList<RestingPairAction>? actions = null) =>
        new(state.Phase, role, actions ?? Array.Empty<RestingPairAction>());

    private static RestingPairDecision Quarantine(PairState state, string reason)
    {
        state.Phase = RestingPairPhase.Quarantined;
        return Decision(state, null,
            [new RestingPairAction(RestingPairActionKind.Quarantine, string.Empty, string.Empty, 0, reason)]);
    }

    private static void ValidateKey(RestingPairKey key)
    {
        if (string.IsNullOrWhiteSpace(key.StrategyInstanceId) || key.StrategyRunGeneration <= 0 ||
            string.IsNullOrWhiteSpace(key.CycleId) || string.IsNullOrWhiteSpace(key.PairId))
            throw new ArgumentException("A paired cycle requires owner, generation, cycle and pair IDs.", nameof(key));
    }
}
