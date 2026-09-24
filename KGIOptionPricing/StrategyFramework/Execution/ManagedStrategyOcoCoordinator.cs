namespace KGIOptionPricing.StrategyFramework.Execution;

public enum ManagedStrategyOcoEventKind
{
    EntrySubmitted,
    EntryFill,
    EntryTerminal,
    TakeProfitSubmitted,
    TakeProfitTerminal,
    StopTriggered,
    CancelAccepted,
    CancelRejected,
    StopMarketResult
}

public sealed record ManagedStrategyOcoEvent(
    LiveStrategyOcoKey Key,
    ManagedStrategyOcoEventKind Kind,
    string ClientOrderId,
    string BrokerOrderId,
    PaperOrderRole Role,
    int DeliveredFilledQuantity,
    int? BrokerFinalCumulativeFilledQuantity,
    bool HasFinalCumulativeFillWatermark,
    long TriggerMarketSequence,
    DateTimeOffset EventTime);

public enum ManagedStrategyOcoActionKind { PersistState, CancelBrokerOrder, SubmitStopMarket, NoAction }

public sealed record ManagedStrategyOcoAction(
    ManagedStrategyOcoActionKind Kind,
    LiveStrategyOcoGroupState State,
    string ClientOrderId,
    int Quantity,
    string Reason);

/// <summary>Pure, owner-scoped OCO state transitions. The caller performs durable writes and broker mutations.</summary>
public sealed class ManagedStrategyOcoCoordinator
{
    private readonly object _sync = new();
    private readonly Dictionary<LiveStrategyOcoKey, LiveStrategyOcoGroupState> _states = new();

    public IReadOnlyList<ManagedStrategyOcoAction> Apply(ManagedStrategyOcoEvent input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.Key.StrategyInstanceId) || input.Key.StrategyRunGeneration <= 0 ||
            string.IsNullOrWhiteSpace(input.Key.CycleId) || string.IsNullOrWhiteSpace(input.ClientOrderId))
            throw new ArgumentException("OCO owner, generation, cycle and client order id are required.", nameof(input));
        if (input.DeliveredFilledQuantity < 0 || input.BrokerFinalCumulativeFilledQuantity < 0)
            throw new ArgumentOutOfRangeException(nameof(input));

        lock (_sync)
        {
            _states.TryGetValue(input.Key, out LiveStrategyOcoGroupState? current);
            current ??= new LiveStrategyOcoGroupState { Key = input.Key, Phase = LiveStrategyOcoPhase.EntryWorking };
            LiveStrategyOcoGroupState next = current with { UpdatedAt = input.EventTime };
            var actions = new List<ManagedStrategyOcoAction>();
            switch (input.Kind)
            {
                case ManagedStrategyOcoEventKind.EntrySubmitted:
                    if (!string.IsNullOrWhiteSpace(current.FilledEntryClientOrderId) &&
                        !string.Equals(current.FilledEntryClientOrderId, input.ClientOrderId, StringComparison.Ordinal))
                    {
                        next = next with
                        {
                            Phase = LiveStrategyOcoPhase.EntryCancelPending,
                            PendingCancelClientOrderId = input.ClientOrderId,
                            EntryLongClientOrderId = input.Role == PaperOrderRole.EntryLong ? input.ClientOrderId : current.EntryLongClientOrderId,
                            EntryShortClientOrderId = input.Role == PaperOrderRole.EntryShort ? input.ClientOrderId : current.EntryShortClientOrderId
                        };
                        actions.Add(Action(ManagedStrategyOcoActionKind.CancelBrokerOrder, next, input.ClientOrderId, 0,
                            "Entry submitted after the opposite entry filled."));
                        break;
                    }
                    if (input.Role == PaperOrderRole.EntryLong) next = next with { EntryLongClientOrderId = input.ClientOrderId };
                    else if (input.Role == PaperOrderRole.EntryShort) next = next with { EntryShortClientOrderId = input.ClientOrderId };
                    else return Array.Empty<ManagedStrategyOcoAction>();
                    if (string.IsNullOrWhiteSpace(current.FilledEntryClientOrderId))
                        next = next with { Phase = LiveStrategyOcoPhase.EntryWorking };
                    break;
                case ManagedStrategyOcoEventKind.EntryFill:
                    if (!string.IsNullOrWhiteSpace(current.FilledEntryClientOrderId) &&
                        !string.Equals(current.FilledEntryClientOrderId, input.ClientOrderId, StringComparison.Ordinal))
                    {
                        next = next with { Phase = LiveStrategyOcoPhase.ReconciliationRequired,
                            Reason = "Both opposing entry orders filled; manual position reconciliation required." };
                        break;
                    }
                    next = next with { FilledEntryClientOrderId = input.ClientOrderId };
                    next = next with { DeliveredFilledQuantity = Math.Max(next.DeliveredFilledQuantity, input.DeliveredFilledQuantity) };
                    string sibling = input.Role == PaperOrderRole.EntryLong ? next.EntryShortClientOrderId :
                        input.Role == PaperOrderRole.EntryShort ? next.EntryLongClientOrderId : string.Empty;
                    if (!string.IsNullOrEmpty(sibling) && current.PendingCancelClientOrderId != sibling)
                    {
                        next = next with { Phase = LiveStrategyOcoPhase.EntryCancelPending, PendingCancelClientOrderId = sibling };
                        actions.Add(Action(ManagedStrategyOcoActionKind.CancelBrokerOrder, next, sibling, 0, "Opposite entry filled."));
                    }
                    else next = next with { Phase = LiveStrategyOcoPhase.PositionProtected };
                    break;
                case ManagedStrategyOcoEventKind.EntryTerminal:
                    if (!input.HasFinalCumulativeFillWatermark || input.BrokerFinalCumulativeFilledQuantity is null ||
                        input.BrokerFinalCumulativeFilledQuantity != input.DeliveredFilledQuantity)
                        next = next with { Phase = LiveStrategyOcoPhase.ReconciliationRequired, Reason = "Entry final fill watermark is missing or mismatched." };
                    else if (current.Phase == LiveStrategyOcoPhase.StopPending && current.PendingCancelClientOrderId == input.ClientOrderId &&
                             string.IsNullOrWhiteSpace(current.TakeProfitClientOrderId))
                    {
                        int residual = Math.Max(0, current.StopRequestedQuantity - input.BrokerFinalCumulativeFilledQuantity.Value);
                        next = next with { EntryCancelSettled = true, PendingCancelClientOrderId = string.Empty,
                            BrokerFinalCumulativeFilledQuantity = input.BrokerFinalCumulativeFilledQuantity,
                            Phase = residual > 0 ? LiveStrategyOcoPhase.StopSubmitted : LiveStrategyOcoPhase.Completed,
                            StopMarketClientOrderId = residual > 0 ? StopClientOrderId(input.Key) : string.Empty };
                        if (residual > 0) actions.Add(Action(ManagedStrategyOcoActionKind.SubmitStopMarket, next,
                            next.StopMarketClientOrderId, residual, "Opposite entry cancel settled before stop."));
                    }
                    else if (current.Phase == LiveStrategyOcoPhase.EntryCancelPending && current.PendingCancelClientOrderId == input.ClientOrderId)
                        next = next with { Phase = LiveStrategyOcoPhase.PositionProtected, EntryCancelSettled = true,
                            PendingCancelClientOrderId = string.Empty, BrokerFinalCumulativeFilledQuantity = input.BrokerFinalCumulativeFilledQuantity };
                    break;
                case ManagedStrategyOcoEventKind.TakeProfitSubmitted:
                    if (input.Role != PaperOrderRole.TakeProfit) return Array.Empty<ManagedStrategyOcoAction>();
                    next = next with { TakeProfitClientOrderId = input.ClientOrderId, Phase = LiveStrategyOcoPhase.PositionProtected };
                    break;
                case ManagedStrategyOcoEventKind.TakeProfitTerminal:
                    if (input.Role != PaperOrderRole.TakeProfit || !input.HasFinalCumulativeFillWatermark ||
                        input.BrokerFinalCumulativeFilledQuantity is null ||
                        input.BrokerFinalCumulativeFilledQuantity != input.DeliveredFilledQuantity)
                        next = next with { Phase = LiveStrategyOcoPhase.ReconciliationRequired,
                            Reason = "Take-profit final fill watermark is missing or mismatched." };
                    else
                    {
                        next = next with { TakeProfitTerminal = true,
                            TakeProfitFinalCumulativeFilledQuantity = input.BrokerFinalCumulativeFilledQuantity.Value,
                            Phase = input.BrokerFinalCumulativeFilledQuantity.Value > 0 ? LiveStrategyOcoPhase.Completed : LiveStrategyOcoPhase.PositionProtected };
                    }
                    break;
                case ManagedStrategyOcoEventKind.StopTriggered:
                    if (current.Phase is LiveStrategyOcoPhase.StopPending or LiveStrategyOcoPhase.StopSubmitted or LiveStrategyOcoPhase.Completed)
                        return Array.Empty<ManagedStrategyOcoAction>();
                    if (current.TakeProfitTerminal)
                    {
                        int residual = Math.Max(0, input.DeliveredFilledQuantity - current.TakeProfitFinalCumulativeFilledQuantity);
                        next = next with { StopRequestedQuantity = input.DeliveredFilledQuantity,
                            StopClientOrderId = input.ClientOrderId, TriggerMarketSequence = input.TriggerMarketSequence,
                            Phase = residual > 0 ? LiveStrategyOcoPhase.StopSubmitted : LiveStrategyOcoPhase.Completed,
                            StopMarketClientOrderId = residual > 0 ? "stop-" + input.Key.StrategyInstanceId + "-" + input.Key.StrategyRunGeneration + "-" + input.Key.CycleId : string.Empty };
                        if (residual > 0) actions.Add(Action(ManagedStrategyOcoActionKind.SubmitStopMarket, next,
                            next.StopMarketClientOrderId, residual, "Take-profit is already terminal."));
                    }
                    else if (!string.IsNullOrEmpty(current.TakeProfitClientOrderId))
                    {
                        next = next with { Phase = LiveStrategyOcoPhase.StopPending,
                            PendingCancelClientOrderId = current.TakeProfitClientOrderId,
                            TriggerMarketSequence = input.TriggerMarketSequence,
                            StopRequestedQuantity = input.DeliveredFilledQuantity,
                            StopClientOrderId = input.ClientOrderId };
                        actions.Add(Action(ManagedStrategyOcoActionKind.CancelBrokerOrder, next, current.TakeProfitClientOrderId, 0,
                            "Stop triggered; cancel take-profit and wait for terminal fill watermark."));
                    }
                    else if (!string.IsNullOrWhiteSpace(current.PendingCancelClientOrderId))
                    {
                        next = next with { Phase = LiveStrategyOcoPhase.StopPending,
                            StopRequestedQuantity = input.DeliveredFilledQuantity,
                            StopClientOrderId = input.ClientOrderId,
                            TriggerMarketSequence = input.TriggerMarketSequence };
                    }
                    else if (current.EntryCancelSettled)
                    {
                        int residual = Math.Max(0, input.DeliveredFilledQuantity - current.BrokerFinalCumulativeFilledQuantity.GetValueOrDefault());
                        next = next with { StopRequestedQuantity = input.DeliveredFilledQuantity,
                            StopClientOrderId = input.ClientOrderId, TriggerMarketSequence = input.TriggerMarketSequence,
                            Phase = residual > 0 ? LiveStrategyOcoPhase.StopSubmitted : LiveStrategyOcoPhase.Completed,
                            StopMarketClientOrderId = residual > 0 ? StopClientOrderId(input.Key) : string.Empty };
                        if (residual > 0) actions.Add(Action(ManagedStrategyOcoActionKind.SubmitStopMarket, next,
                            next.StopMarketClientOrderId, residual, "Opposite entry cancel was already settled."));
                    }
                    else next = next with { Phase = LiveStrategyOcoPhase.ReconciliationRequired,
                        Reason = "Stop-market requires confirmed OCO settlement." };
                    break;
                case ManagedStrategyOcoEventKind.CancelAccepted:
                    if (current.Phase != LiveStrategyOcoPhase.StopPending || current.PendingCancelClientOrderId != input.ClientOrderId)
                        return Array.Empty<ManagedStrategyOcoAction>();
                    if (!input.HasFinalCumulativeFillWatermark || input.BrokerFinalCumulativeFilledQuantity is null ||
                        input.BrokerFinalCumulativeFilledQuantity != input.DeliveredFilledQuantity)
                        next = next with { Phase = LiveStrategyOcoPhase.ReconciliationRequired, Reason = "Cancelled order fill watermark is missing or mismatched." };
                    else
                    {
                        int residual = Math.Max(0, current.StopRequestedQuantity - input.BrokerFinalCumulativeFilledQuantity.Value);
                        next = next with { Phase = residual > 0 ? LiveStrategyOcoPhase.StopSubmitted : LiveStrategyOcoPhase.Completed,
                            BrokerFinalCumulativeFilledQuantity = input.BrokerFinalCumulativeFilledQuantity,
                            TakeProfitTerminal = true,
                            TakeProfitFinalCumulativeFilledQuantity = input.BrokerFinalCumulativeFilledQuantity.Value,
                            PendingCancelClientOrderId = string.Empty,
                            StopMarketClientOrderId = residual > 0 ? "stop-" + input.Key.StrategyInstanceId + "-" + input.Key.StrategyRunGeneration + "-" + input.Key.CycleId : string.Empty };
                        if (residual > 0) actions.Add(Action(ManagedStrategyOcoActionKind.SubmitStopMarket, next,
                            next.StopMarketClientOrderId, residual, "OCO cancel settled."));
                    }
                    break;
                case ManagedStrategyOcoEventKind.CancelRejected:
                    next = next with { Phase = LiveStrategyOcoPhase.ReconciliationRequired, Reason = "OCO cancel rejected; manual reconciliation required." };
                    break;
                case ManagedStrategyOcoEventKind.StopMarketResult:
                    next = next with { Phase = LiveStrategyOcoPhase.ReconciliationRequired, Reason = "Stop-market result requires broker reconciliation." };
                    break;
            }
            _states[input.Key] = next;
            actions.Insert(0, Action(ManagedStrategyOcoActionKind.PersistState, next, string.Empty, 0, next.Reason));
            return actions;
        }
    }

    public IReadOnlyList<LiveStrategyOcoGroupState> GetStates(string strategyInstanceId, long strategyRunGeneration)
    {
        lock (_sync) return _states.Values.Where(state =>
            string.Equals(state.Key.StrategyInstanceId, strategyInstanceId, StringComparison.OrdinalIgnoreCase) &&
            state.Key.StrategyRunGeneration == strategyRunGeneration).ToArray();
    }

    private static ManagedStrategyOcoAction Action(ManagedStrategyOcoActionKind kind, LiveStrategyOcoGroupState state,
        string clientOrderId, int quantity, string reason) => new(kind, state, clientOrderId, quantity, reason);

    private static string StopClientOrderId(LiveStrategyOcoKey key) =>
        "stop-" + key.StrategyInstanceId + "-" + key.StrategyRunGeneration + "-" + key.CycleId;
}
