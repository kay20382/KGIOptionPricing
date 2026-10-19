using System;
using System.Collections.Generic;

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
    public int StopMarketRequestedQuantity { get; init; }
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
    public KGIOptionPricing.OrderManagement.Core.TimeInForce TimeInForce { get; init; }
    public decimal LimitPrice { get; init; }
    public LiveOrderOwnerKey Key { get; init; } = new(string.Empty, 0, string.Empty);
    public string Account { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public string CycleId { get; init; } = string.Empty;
    public string OcoGroupId { get; init; } = string.Empty;
    public string PairId { get; init; } = string.Empty;
    public bool IsRestingPair { get; init; }
    public PaperOrderRole Role { get; init; }
    public int Quantity { get; init; }
    public StrategySubmissionState SubmissionState { get; init; } = StrategySubmissionState.Prepared;
    public int CumulativeFilledQuantity { get; init; }
    public string Reason { get; init; } = string.Empty;
    public bool PriorProcessReconciled { get; init; }
}

public sealed record FillAcceptance(bool Accepted, int AcceptedQuantity);

public sealed record LiveStrategyRetirement(
    string StrategyInstanceId,
    long StrategyRunGeneration,
    string Account,
    string Symbol,
    DateTimeOffset CapturedAt,
    int Position,
    decimal RealizedPnl,
    decimal UnrealizedPnl,
    decimal Costs,
    int QuarantinedOrders,
    bool PnlVerified);

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
    public IReadOnlyList<RestingPairSnapshot> RestingPairs { get; init; } = Array.Empty<RestingPairSnapshot>();
    public IReadOnlyList<LiveStrategyCheckpoint> StrategyCheckpoints { get; init; } = Array.Empty<LiveStrategyCheckpoint>();
    public IReadOnlyList<LiveOrderOwnership> Routes { get; init; } = Array.Empty<LiveOrderOwnership>();
    public IReadOnlyList<LiveStrategyOcoGroupState> OcoGroups { get; init; } = Array.Empty<LiveStrategyOcoGroupState>();
    public IReadOnlyDictionary<BrokerOrderAlias, LiveOrderOwnerKey> Aliases { get; init; } =
        new Dictionary<BrokerOrderAlias, LiveOrderOwnerKey>();
    public IReadOnlyList<QuarantinedBrokerEvent> QuarantinedEvents { get; init; } = Array.Empty<QuarantinedBrokerEvent>();
    public IReadOnlyList<LiveStrategyRetirement> Retirements { get; init; } = Array.Empty<LiveStrategyRetirement>();
}

public sealed record LiveStrategyCheckpoint(string StrategyInstanceId,long StrategyRunGeneration,int SchemaVersion,
    string StrategyType,string ParameterHash,System.Text.Json.JsonElement State,DateTimeOffset SavedAt);
