using System;
using System.Collections.Generic;

namespace KGIOptionPricing.StrategyFramework;

public enum StrategyRunMode { LiveShadow, Simulation, HistoricalReplay, LiveTrading }
public enum StrategyDecision { Warmup, Reject, Accept, Hold, Exit, Halt }
public enum StrategyObservationKind { Decision, FeatureSample, RiClosed }
public enum ParameterExperimentStatus { Valid, Rejected, Incomplete }

public sealed record ThresholdObservation(
    string Name,
    decimal? ObservedValue,
    decimal? ThresholdValue,
    string Unit,
    bool Passed);

public sealed record StrategyDecisionObservation
{
    public StrategyObservationKind Kind { get; init; }
    public string SampleKey { get; init; } = string.Empty;
    public long Sequence { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public string StrategyType { get; init; } = string.Empty;
    public string StrategyInstanceId { get; init; } = string.Empty;
    public string CandidateId { get; init; } = string.Empty;
    public bool IsCandidate { get; init; }
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
    public IReadOnlyDictionary<string, string> UnavailableReasons { get; init; } =
        new Dictionary<string, string>();
}

public interface IStrategyDecisionSource
{
    event Action<StrategyDecisionObservation>? DecisionObserved;
}

public sealed record StrategyObservationDiagnostic(
    DateTimeOffset Timestamp,
    string Kind,
    string ReasonCode,
    string Detail);

public sealed record InputArchiveIdentity
{
    public string AbsolutePath { get; init; } = string.Empty;
    public long? SizeBytes { get; init; }
    public DateTimeOffset? LastWriteTimeUtc { get; init; }
    public string? Sha256 { get; init; }
    public DateTimeOffset? FirstEventAt { get; init; }
    public DateTimeOffset? LastEventAt { get; init; }
    public IReadOnlyList<InputArchiveFileRange> Files { get; init; } = Array.Empty<InputArchiveFileRange>();
    public string? MutationReasonCode { get; init; }
    public IReadOnlyList<string> MissingFields { get; init; } = Array.Empty<string>();
}

public sealed record InputArchiveFileRange(
    string RelativePath,
    long StartOffset,
    long EndOffset,
    DateTimeOffset LastWriteTimeUtc);

public sealed record InputArchiveBoundaryFile(
    string RelativePath,
    long Length,
    DateTimeOffset LastWriteTimeUtc,
    string Sha256);

public sealed record InputArchiveBoundary
{
    public string AbsolutePath { get; init; } = string.Empty;
    public bool AppendOnly { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public IReadOnlyList<InputArchiveBoundaryFile> Files { get; init; } = Array.Empty<InputArchiveBoundaryFile>();
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
    public IReadOnlyDictionary<string, string> RawParameters { get; init; } =
        new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> EffectiveParameters { get; init; } =
        new Dictionary<string, string>();
    public string? EffectiveParametersUnavailableReason { get; init; }
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
    public KGIOptionPricing.OrderManagement.Core.TimeInForce TimeInForce { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public string StrategyInstanceId { get; init; } = string.Empty;
    public string CycleId { get; init; } = string.Empty;
    public string ClientOrderId { get; init; } = string.Empty;
    public string PairId { get; init; } = string.Empty;
    public string OcoGroupId { get; init; } = string.Empty;
    public string OrderId { get; init; } = string.Empty;
    public string BrokerOrderId { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public KGIOptionPricing.OrderManagement.Core.BuySell? Side { get; init; }
    public PaperOrderRole? Role { get; init; }
    public int Generation { get; init; }
    public long TriggerMarketSequence { get; init; }
    public OrderLifecycleState PreviousState { get; init; }
    public OrderLifecycleState State { get; init; }
    public decimal Price { get; init; }
    public int Quantity { get; init; }
    public int FilledQuantity { get; init; }
    public int DeliveredFilledQuantity { get; init; }
    public int? BrokerFinalCumulativeFilledQuantity { get; init; }
    public int RemainingQuantity { get; init; }
    public bool HasFinalCumulativeFillWatermark { get; init; }
    public string ReasonCode { get; init; } = string.Empty;
    public DateTimeOffset? SubmittedAt { get; init; }
    public DateTimeOffset? AcceptedAt { get; init; }
    public DateTimeOffset? FirstFilledAt { get; init; }
    public DateTimeOffset? FullyFilledAt { get; init; }
    public DateTimeOffset? CancelRequestedAt { get; init; }
    public DateTimeOffset? CancelledAt { get; init; }
    public DateTimeOffset? RejectedAt { get; init; }
    public IReadOnlyDictionary<string, string> UnavailableReasons { get; init; } =
        new Dictionary<string, string>();
}

public static class ManagedOrderCorrelation
{
    public static bool TryParse(string clientOrderId, out string cycleId, out int generation)
    {
        cycleId = string.Empty;
        generation = 0;
        if (string.IsNullOrWhiteSpace(clientOrderId)) return false;
        int marker = clientOrderId.IndexOf("-cycle-", StringComparison.OrdinalIgnoreCase);
        if (marker < 0) return false;
        int numberStart = marker + "-cycle-".Length;
        int numberEnd = numberStart;
        while (numberEnd < clientOrderId.Length && char.IsDigit(clientOrderId[numberEnd])) numberEnd++;
        if (numberEnd == numberStart) return false;
        cycleId = "cycle-" + clientOrderId[numberStart..numberEnd];
        int generationMarker = clientOrderId.LastIndexOf("-g", StringComparison.OrdinalIgnoreCase);
        if (generationMarker >= 0 &&
            int.TryParse(clientOrderId[(generationMarker + 2)..],
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out int parsedGeneration))
            generation = parsedGeneration;
        else
            generation = 1;
        return true;
    }
}

public sealed record RunSummary
{
    public long? DataQualityEvaluations { get; init; }
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
    public long? CompletedTrades { get; init; }
    public long BothLegFilledCycles { get; init; }
    public long OneLegFilledCycles { get; init; }
    public long StopLossExits { get; init; }
    public long OneLegTimeouts { get; init; }
    public decimal? FillRate { get; init; }
    public decimal? CancellationRate { get; init; }
    public decimal? GrossPnl { get; init; }
    public decimal? ExecutionCosts { get; init; }
    public decimal? ModelSlippage { get; init; }
    public decimal? NetPnl { get; init; }
    public decimal? WinRate { get; init; }
    public decimal? AverageWinningPoints { get; init; }
    public decimal? AverageLosingPoints { get; init; }
    public decimal? UserDefinedProfitFactor { get; init; }
    public decimal? MaximumDrawdown { get; init; }
    public decimal? MaximumIntradayLoss { get; init; }
    public decimal? MaximumTradeLoss { get; init; }
    public decimal? MfePoints { get; init; }
    public decimal? MaePoints { get; init; }
    public decimal? HoldingSeconds { get; init; }
    public IReadOnlyDictionary<int, decimal?> AdverseSlippageStress { get; init; } =
        new Dictionary<int, decimal?>();
    public IReadOnlyDictionary<string, ThresholdFunnelCount> ThresholdFunnel { get; init; } =
        new Dictionary<string, ThresholdFunnelCount>();
    public IReadOnlyDictionary<string, long> DataQualityCounts { get; init; } =
        new Dictionary<string, long>();
    public IReadOnlyDictionary<string, long> PrimaryReasonCounts { get; init; } =
        new Dictionary<string, long>();
    public IReadOnlyDictionary<string, long> SecondaryReasonCounts { get; init; } =
        new Dictionary<string, long>();
    public IReadOnlyDictionary<string, string> UnavailableReasons { get; init; } =
        new Dictionary<string, string>();
}

public sealed record ThresholdFunnelCount(long Evaluated, long Passed, long Failed);

public sealed record ParameterSearchDefinition(
    string Type,
    string Unit,
    decimal? AllowedMinimum,
    decimal? AllowedMaximum,
    decimal? SearchMinimum,
    decimal? SearchMaximum,
    decimal? Step);

public sealed record ParameterExperimentRecord
{
    public IReadOnlyList<ParameterDataSplit> DataSplits { get; init; } = Array.Empty<ParameterDataSplit>();
    public IReadOnlyDictionary<string, ParameterSplitResult> SplitResults { get; init; } =
        new Dictionary<string, ParameterSplitResult>();
    public string ExperimentId { get; init; } = string.Empty;
    public string BaselineRunId { get; init; } = string.Empty;
    public string CandidateRunId { get; init; } = string.Empty;
    public string StrategyType { get; init; } = string.Empty;
    public string? CodeVersion { get; init; }
    public IReadOnlyDictionary<string, string> CandidateParameters { get; init; } =
        new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> ChangedParameters { get; init; } =
        new Dictionary<string, string>();
    public IReadOnlyDictionary<string, ParameterSearchDefinition> ParameterDefinitions { get; init; } =
        new Dictionary<string, ParameterSearchDefinition>();
    public IReadOnlyDictionary<string, string> ProductMapping { get; init; } =
        new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> ExecutionAssumptions { get; init; } =
        new Dictionary<string, string>();
    public string SearchMethod { get; init; } = string.Empty;
    public int? RandomSeed { get; init; }
    public int? CandidateOrdinal { get; init; }
    public string? InputArchiveSha256 { get; init; }
    public string? DataSplitId { get; init; }
    public string PrimaryObjective { get; init; } = string.Empty;
    public IReadOnlyList<string> SecondaryObjectives { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Objectives { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Guardrails { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, decimal?> GuardrailValues { get; init; } =
        new Dictionary<string, decimal?>();
    public IReadOnlyDictionary<string, decimal?> Results { get; init; } =
        new Dictionary<string, decimal?>();
    public IReadOnlyDictionary<int, decimal?> AdverseSlippageStress { get; init; } =
        new Dictionary<int, decimal?>();
    public ParameterExperimentStatus Status { get; init; }
    public string? ReasonCode { get; init; }
    public IReadOnlyDictionary<string, string> UnavailableReasons { get; init; } =
        new Dictionary<string, string>();
}

public sealed record ParameterDataSplit(string Kind, DateTimeOffset Start, DateTimeOffset End);
public sealed record ParameterSplitResult(int CompletedTrades, decimal? NetPnl, decimal? MaximumDrawdown);
