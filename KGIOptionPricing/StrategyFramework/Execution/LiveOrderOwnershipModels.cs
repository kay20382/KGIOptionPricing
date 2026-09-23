using System;
using System.Collections.Generic;
using KGIOptionPricing.StrategyFramework.Recovery;

namespace KGIOptionPricing.StrategyFramework.Execution;

public sealed record LiveOrderOwnerKey(string StrategyInstanceId, long StrategyRunGeneration, string ClientOrderId);

public sealed record BrokerOrderAlias(string Account, string TradingDay, string Kind, string Value);

public enum StrategySubmissionState { Prepared, Working, Unknown, Filled, Cancelled, Rejected }

public sealed record LiveOrderOwnership
{
    public LiveOrderOwnerKey Key { get; init; } = new(string.Empty, 0, string.Empty);
    public string Account { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public string CycleId { get; init; } = string.Empty;
    public PaperOrderRole Role { get; init; }
    public int Quantity { get; init; }
    public StrategySubmissionState SubmissionState { get; init; } = StrategySubmissionState.Prepared;
    public int CumulativeFilledQuantity { get; init; }
    public string Reason { get; init; } = string.Empty;
}

public sealed record FillAcceptance(bool Accepted, int AcceptedQuantity);

public sealed record LiveOwnershipSnapshot
{
    public IReadOnlyList<LiveOrderOwnership> Routes { get; init; } = Array.Empty<LiveOrderOwnership>();
    public IReadOnlyDictionary<BrokerOrderAlias, LiveOrderOwnerKey> Aliases { get; init; } =
        new Dictionary<BrokerOrderAlias, LiveOrderOwnerKey>();
    public IReadOnlyList<StrategyRecoveryCheckpoint> Checkpoints { get; init; } = Array.Empty<StrategyRecoveryCheckpoint>();
    public bool IsQuarantined { get; init; }
    public string QuarantineReason { get; init; } = string.Empty;
}

public sealed class LiveOwnershipJournalException : InvalidOperationException
{
    public LiveOwnershipJournalException(string message) : base(message) { }
    public LiveOwnershipJournalException(string message, Exception innerException) : base(message, innerException) { }
}
