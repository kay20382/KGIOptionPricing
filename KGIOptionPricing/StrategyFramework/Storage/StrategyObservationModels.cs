using System;
using System.Collections.Generic;

namespace KGIOptionPricing.StrategyFramework;

public enum StrategyRunMode { LiveShadow, Simulation, HistoricalReplay }
public enum StrategyDecision { Warmup, Reject, Accept, Hold, Exit, Halt }
public enum ParameterExperimentStatus { Valid, Rejected, Incomplete }

public sealed record ThresholdObservation(
    string Name,
    decimal? ObservedValue,
    decimal? ThresholdValue,
    string Unit,
    bool Passed);

public sealed record StrategyDecisionObservation
{
    public long Sequence { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public string StrategyType { get; init; } = string.Empty;
    public string StrategyInstanceId { get; init; } = string.Empty;
    public string CandidateId { get; init; } = string.Empty;
    public string CycleId { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public StrategyLifecycleState State { get; init; }
    public StrategyDecision Decision { get; init; }
    public IReadOnlyDictionary<string, decimal?> Features { get; init; } =
        new Dictionary<string, decimal?>();
    public IReadOnlyList<ThresholdObservation> Thresholds { get; init; } =
        Array.Empty<ThresholdObservation>();
    public string PrimaryReasonCode { get; init; } = string.Empty;
    public IReadOnlyList<string> SecondaryReasonCodes { get; init; } = Array.Empty<string>();
    public decimal? QuoteAgeSeconds { get; init; }
    public decimal? CrossProductReceiveSkewSeconds { get; init; }
    public decimal? SpreadTicks { get; init; }
    public bool? RequiredProductsPresent { get; init; }
    public IReadOnlyList<string> DataQualityFlags { get; init; } = Array.Empty<string>();
    public string? UnavailableReasonCode { get; init; }
}

public interface IStrategyDecisionSource
{
    event Action<StrategyDecisionObservation>? DecisionObserved;
}

public sealed record InputArchiveIdentity
{
    public string AbsolutePath { get; init; } = string.Empty;
    public long? SizeBytes { get; init; }
    public DateTimeOffset? LastWriteTimeUtc { get; init; }
    public string? Sha256 { get; init; }
    public DateTimeOffset? FirstEventAt { get; init; }
    public DateTimeOffset? LastEventAt { get; init; }
    public IReadOnlyList<string> MissingFields { get; init; } = Array.Empty<string>();
}

public sealed record RunProductIdentity(
    string ProductId,
    string Symbol,
    string ContractMonth,
    decimal TickSize,
    decimal ContractMultiplier);

public sealed record RunManifest
{
    public string RunId { get; init; } = string.Empty;
    public string StrategyType { get; init; } = string.Empty;
    public string InstanceId { get; init; } = string.Empty;
    public string? CodeVersion { get; init; }
    public StrategyRunMode Mode { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? EndedAt { get; init; }
    public IReadOnlyDictionary<string, string> EffectiveParameters { get; init; } =
        new Dictionary<string, string>();
    public string NormalizedParameters { get; init; } = string.Empty;
    public string ParameterSha256 { get; init; } = string.Empty;
    public InputArchiveIdentity? InputArchive { get; init; }
    public IReadOnlyList<RunProductIdentity> Universe { get; init; } = Array.Empty<RunProductIdentity>();
    public decimal? CommissionPerContract { get; init; }
    public IReadOnlyDictionary<string, string> ExecutionAssumptions { get; init; } =
        new Dictionary<string, string>();
    public string FrameworkVersion { get; init; } = string.Empty;
    public IReadOnlyList<string> MissingProvenanceFields { get; init; } = Array.Empty<string>();
}

public sealed record OrderLifecycleObservation
{
    public DateTimeOffset Timestamp { get; init; }
    public string StrategyInstanceId { get; init; } = string.Empty;
    public string CycleId { get; init; } = string.Empty;
    public string ClientOrderId { get; init; } = string.Empty;
    public string OrderId { get; init; } = string.Empty;
    public string BrokerOrderId { get; init; } = string.Empty;
    public OrderLifecycleState PreviousState { get; init; }
    public OrderLifecycleState State { get; init; }
    public decimal Price { get; init; }
    public int Quantity { get; init; }
    public int FilledQuantity { get; init; }
    public int RemainingQuantity { get; init; }
    public string ReasonCode { get; init; } = string.Empty;
}

public sealed record RunSummary
{
    public DateTimeOffset FinalizedAt { get; init; }
    public long? MarketEvents { get; init; }
    public long WarmupObservations { get; init; }
    public long CandidateSignals { get; init; }
    public long AcceptedSignals { get; init; }
    public long SubmittedOrders { get; init; }
    public long TimedOutOrders { get; init; }
    public long CancelledOrders { get; init; }
    public long RejectedOrders { get; init; }
    public long Fills { get; init; }
    public long CompletedTrades { get; init; }
    public decimal? GrossPnl { get; init; }
    public decimal? ExecutionCosts { get; init; }
    public decimal? ModelSlippage { get; init; }
    public decimal? NetPnl { get; init; }
    public decimal? WinRate { get; init; }
    public decimal? AverageWinningPoints { get; init; }
    public decimal? AverageLosingPoints { get; init; }
    public decimal? UserDefinedProfitFactor { get; init; }
    public decimal? MaximumDrawdown { get; init; }
    public IReadOnlyDictionary<string, long> PrimaryReasonCounts { get; init; } =
        new Dictionary<string, long>();
    public IReadOnlyDictionary<string, long> SecondaryReasonCounts { get; init; } =
        new Dictionary<string, long>();
    public IReadOnlyDictionary<string, string> UnavailableReasons { get; init; } =
        new Dictionary<string, string>();
}

public sealed record ParameterExperimentRecord
{
    public string ExperimentId { get; init; } = string.Empty;
    public string BaselineRunId { get; init; } = string.Empty;
    public string CandidateRunId { get; init; } = string.Empty;
    public string StrategyType { get; init; } = string.Empty;
    public string? CodeVersion { get; init; }
    public IReadOnlyDictionary<string, string> CandidateParameters { get; init; } =
        new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> ChangedParameters { get; init; } =
        new Dictionary<string, string>();
    public string SearchMethod { get; init; } = string.Empty;
    public int? RandomSeed { get; init; }
    public int? CandidateOrdinal { get; init; }
    public string? InputArchiveSha256 { get; init; }
    public string? DataSplitId { get; init; }
    public IReadOnlyList<string> Objectives { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Guardrails { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, decimal?> Results { get; init; } =
        new Dictionary<string, decimal?>();
    public ParameterExperimentStatus Status { get; init; }
    public string? ReasonCode { get; init; }
}
