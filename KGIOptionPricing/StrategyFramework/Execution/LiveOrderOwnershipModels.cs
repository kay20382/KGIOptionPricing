using System;
using System.Collections.Generic;
using KGIOptionPricing.StrategyFramework.Recovery;

namespace KGIOptionPricing.StrategyFramework.Execution;

public sealed record LiveOrderOwnerKey(string StrategyInstanceId, long StrategyRunGeneration, string ClientOrderId);

public sealed record LiveStrategyOcoKey(string StrategyInstanceId, long StrategyRunGeneration, string CycleId);

public enum LiveStrategyOcoPhase
{
    EntryWorking,
    EntryCancelPending,
    PositionProtected,
    StopPending,
    StopSubmitted,
    Completed,
    ReconciliationRequired,
    Quarantined
}

public sealed record LiveStrategyOcoGroupState
{
    public LiveStrategyOcoKey Key { get; init; } = new(string.Empty, 0, string.Empty);
    public LiveStrategyOcoPhase Phase { get; init; }
    public string EntryLongClientOrderId { get; init; } = string.Empty;
    public string EntryShortClientOrderId { get; init; } = string.Empty;
    public string FilledEntryClientOrderId { get; init; } = string.Empty;
    public bool EntryCancelSettled { get; init; }
    public string TakeProfitClientOrderId { get; init; } = string.Empty;
    public bool TakeProfitTerminal { get; init; }
    public int TakeProfitFinalCumulativeFilledQuantity { get; init; }
    public string StopClientOrderId { get; init; } = string.Empty;
    public string PendingCancelClientOrderId { get; init; } = string.Empty;
    public string StopMarketClientOrderId { get; init; } = string.Empty;
    public long TriggerMarketSequence { get; init; }
    public int? BrokerFinalCumulativeFilledQuantity { get; init; }
    public int DeliveredFilledQuantity { get; init; }
    public int StopRequestedQuantity { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public string Reason { get; init; } = string.Empty;
}

public sealed record BrokerOrderAlias(string Account, string TradingDay, string Kind, string Value);

public enum StrategySubmissionState { Prepared, Working, Unknown, Filled, Cancelled, Rejected }

public sealed record LiveOrderOwnership
{
    public LiveOrderOwnerKey Key { get; init; } = new(string.Empty, 0, string.Empty);
    public string Account { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public string CycleId { get; init; } = string.Empty;
    public string OcoGroupId { get; init; } = string.Empty;
    public PaperOrderRole Role { get; init; }
    public int Quantity { get; init; }
    public StrategySubmissionState SubmissionState { get; init; } = StrategySubmissionState.Prepared;
    public int CumulativeFilledQuantity { get; init; }
    public string Reason { get; init; } = string.Empty;
}

public sealed record FillAcceptance(bool Accepted, int AcceptedQuantity);

public sealed record QuarantinedBrokerEvent(
    string EventType,
    string Account,
    string Symbol,
    string OrderId,
    string RequestId,
    string Cnt,
    string OrdNo,
    string ClientOrderId,
    DateTimeOffset OccurredAt,
    string Reason);

public sealed record LiveOwnershipSnapshot
{
    public IReadOnlyList<LiveOrderOwnership> Routes { get; init; } = Array.Empty<LiveOrderOwnership>();
    public IReadOnlyList<LiveStrategyOcoGroupState> OcoGroups { get; init; } = Array.Empty<LiveStrategyOcoGroupState>();
    public IReadOnlyDictionary<BrokerOrderAlias, LiveOrderOwnerKey> Aliases { get; init; } =
        new Dictionary<BrokerOrderAlias, LiveOrderOwnerKey>();
    public IReadOnlyList<StrategyRecoveryCheckpoint> Checkpoints { get; init; } = Array.Empty<StrategyRecoveryCheckpoint>();
    public IReadOnlyList<QuarantinedBrokerEvent> QuarantinedEvents { get; init; } = Array.Empty<QuarantinedBrokerEvent>();
    public bool IsQuarantined { get; init; }
    public string QuarantineReason { get; init; } = string.Empty;
}

public sealed class LiveOwnershipJournalException : InvalidOperationException
{
    public LiveOwnershipJournalException(string message) : base(message) { }
    public LiveOwnershipJournalException(string message, Exception innerException) : base(message, innerException) { }
}
