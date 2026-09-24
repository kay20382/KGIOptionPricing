using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KGIOptionPricing.OrderManagement.Core;

namespace KGIOptionPricing.StrategyFramework.Storage;

public sealed class SimulationRunRecorder : IDisposable
{
    private sealed record DecisionStamp(string Fingerprint, DateTimeOffset WrittenAt);
    private sealed class LifecycleTimeline
    {
        internal DateTimeOffset? SubmittedAt;
        internal DateTimeOffset? AcceptedAt;
        internal DateTimeOffset? FirstFilledAt;
        internal DateTimeOffset? FullyFilledAt;
        internal DateTimeOffset? CancelRequestedAt;
        internal DateTimeOffset? CancelledAt;
        internal DateTimeOffset? RejectedAt;
    }
    private sealed class OpenExecutionLot
    {
        internal required BuySell Side;
        internal required decimal Price;
        internal required int Quantity;
        internal required decimal CommissionPerUnit;
        internal required DateTimeOffset Timestamp;
    }
    private sealed class OpenTradeExcursion
    {
        internal List<OpenExecutionLot> Lots { get; } = new();
        internal int NetQuantity;
        internal int ClosedQuantity;
        internal decimal GrossPnl;
        internal decimal Commission;
        internal required decimal Multiplier;
        internal required DateTimeOffset StartedAt;
    }
    private sealed record CompletedRoundTrip(
        decimal GrossPnl, decimal Commission, decimal NetPnl, decimal NetPoints, decimal HoldingSeconds);

    private readonly object _sync = new();
    private readonly StreamWriter _events;
    private readonly StreamWriter _decisions;
    private readonly StreamWriter _orderLifecycle;
    private readonly StreamWriter _trades;
    private readonly StreamWriter _experiments;
    private readonly TimeSpan _decisionHeartbeatInterval;
    private readonly Dictionary<string, DecisionStamp> _lastDecisions = new(StringComparer.Ordinal);
    private readonly HashSet<string> _candidates = new(StringComparer.Ordinal);
    private readonly HashSet<string> _acceptedCandidates = new(StringComparer.Ordinal);
    private readonly HashSet<string> _warmupCandidates = new(StringComparer.Ordinal);
    private readonly HashSet<string> _primaryReasonFacts = new(StringComparer.Ordinal);
    private readonly HashSet<string> _secondaryReasonFacts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _thresholdFacts = new(StringComparer.Ordinal);
    private readonly HashSet<string> _dataQualityFacts = new(StringComparer.Ordinal);
    private readonly HashSet<string> _timeoutCycles = new(StringComparer.Ordinal);
    private readonly HashSet<string> _lifecycleTransitions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LifecycleTimeline> _lifecycleTimelines = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _primaryReasons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _secondaryReasons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (long Evaluated, long Passed, long Failed)> _thresholdFunnel = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _dataQualityCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _cycleEntrySides = new(StringComparer.Ordinal);
    private readonly Dictionary<string, OpenTradeExcursion> _openExcursions = new(StringComparer.Ordinal);
    private readonly List<CompletedRoundTrip> _completedRoundTrips = new();
    private readonly Dictionary<string, StrategySnapshot> _latestSnapshots = new(StringComparer.Ordinal);
    private readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() }
    };
    private bool _manifestWritten;
    private bool _summaryFinalized;
    private bool _disposed;
    private long _warmupObservations;
    private long _candidateSignals;
    private long _acceptedSignals;
    private long _submittedOrders;
    private long _timedOutOrders;
    private long _cancelledOrders;
    private long _rejectedOrders;
    private long _fills;
    private long _stopLossExits;
    private long _oneLegTimeouts;
    private decimal _totalCommission;
    private long _totalFillQuantity;
    private decimal _peakNetPnl;
    private decimal _maximumDrawdown;
    private bool _hasSnapshot;
    private RunManifest? _manifest;

    public string DirectoryPath { get; }
    internal bool ManifestFinalized { get { lock (_sync) return _manifestWritten; } }
    internal bool SummaryFinalized { get { lock (_sync) return _summaryFinalized; } }
    internal bool IsDisposed { get { lock (_sync) return _disposed; } }

    public SimulationRunRecorder(string directoryPath, TimeSpan? decisionHeartbeatInterval = null)
    {
        DirectoryPath = Path.GetFullPath(directoryPath);
        Directory.CreateDirectory(DirectoryPath);
        _decisionHeartbeatInterval = decisionHeartbeatInterval ?? TimeSpan.FromSeconds(5);
        if (_decisionHeartbeatInterval < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(decisionHeartbeatInterval));
        StreamWriter? events = null, decisions = null, lifecycle = null, experiments = null, trades = null;
        var created = new List<string>();
        StreamWriter Open(string name)
        {
            string path = Path.Combine(DirectoryPath, name);
            bool existed = File.Exists(path);
            StreamWriter writer = Writer(name);
            if (!existed) created.Add(path);
            return writer;
        }
        try
        {
            events = Open("strategy-events.jsonl");
            decisions = Open("strategy-decisions.jsonl");
            lifecycle = Open("order-lifecycle.jsonl");
            experiments = Open("parameter-experiments.jsonl");
            trades = Open("executions.csv");
            trades.WriteLine("timestamp,sequence,marketSequence,instanceId,cycleId,generation,clientOrderId,orderId,symbol,side,role,price,quantity,commission,triggerMarketPrice");
        }
        catch
        {
            foreach (IDisposable? stream in new IDisposable?[] { events, decisions, lifecycle, experiments, trades })
                try { stream?.Dispose(); } catch { }
            foreach (string path in created)
                try { if (File.Exists(path)) File.Delete(path); } catch { }
            throw;
        }
        _events = events!;
        _decisions = decisions!;
        _orderLifecycle = lifecycle!;
        _experiments = experiments!;
        _trades = trades!;
    }

    public void RecordManifest(RunManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_manifestWritten || File.Exists(Path.Combine(DirectoryPath, "run-manifest.json")))
                throw new InvalidOperationException("run-manifest.json 每次執行只能寫入一次。");
            WriteManifestAtomically(manifest);
            _manifestWritten = true;
            _manifest = manifest;
        }
    }

    public void PrepareManifest(RunManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_manifestWritten) throw new InvalidOperationException("Manifest 已完成，不得重新 prepare。");
            _manifest = manifest;
        }
    }

    public void FinalizeManifest(RunManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        lock (_sync)
        {
            ThrowIfDisposed();
            if (!manifest.EndedAt.HasValue)
                throw new ArgumentException("Final manifest 必須含 EndedAt。", nameof(manifest));
            if (_manifestWritten || File.Exists(Path.Combine(DirectoryPath, "run-manifest.json")))
                throw new InvalidOperationException("run-manifest.json 每次執行只能完成一次。");
            WriteManifestAtomically(manifest);
            _manifestWritten = true;
            _manifest = manifest;
        }
    }

    private void WriteManifestAtomically(RunManifest manifest)
    {
        string target = Path.Combine(DirectoryPath, "run-manifest.json");
        string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(manifest, _json), new UTF8Encoding(false));
            File.Move(temporary, target, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public void Record(StrategySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _events.WriteLine(JsonSerializer.Serialize(snapshot, _json));
            string snapshotKey = string.Join("\u001f", snapshot.InstanceId, snapshot.TradableSymbol);
            _latestSnapshots[snapshotKey] = snapshot;
            _hasSnapshot = true;
            decimal aggregateNetPnl = _latestSnapshots.Values.Sum(item => item.NetPnl);
            _peakNetPnl = Math.Max(_peakNetPnl, aggregateNetPnl);
            _maximumDrawdown = Math.Max(_maximumDrawdown, _peakNetPnl - aggregateNetPnl);
        }
    }

    public void Record(StrategyDecisionObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            string key = string.Join("\u001f", observation.StrategyInstanceId,
                observation.CandidateId, observation.CycleId);
            string fingerprint = JsonSerializer.Serialize(
                observation with { Sequence = 0, Timestamp = default }, _json);
            if (_lastDecisions.TryGetValue(key, out DecisionStamp? last) &&
                string.Equals(last.Fingerprint, fingerprint, StringComparison.Ordinal) &&
                observation.Timestamp - last.WrittenAt < _decisionHeartbeatInterval)
                return;

            _decisions.WriteLine(JsonSerializer.Serialize(observation, _json));
            _lastDecisions[key] = new DecisionStamp(fingerprint, observation.Timestamp);
            string factScope = string.Join("\u001f", observation.StrategyInstanceId,
                observation.CandidateId, observation.CycleId);
            if (observation.Decision == StrategyDecision.Warmup && _warmupCandidates.Add(factScope))
                _warmupObservations++;
            string candidateKey = string.Join("\u001f", observation.StrategyInstanceId,
                observation.CandidateId, observation.CycleId);
            if (observation.IsCandidate && !string.IsNullOrWhiteSpace(observation.CandidateId) &&
                _candidates.Add(candidateKey))
                _candidateSignals++;
            if (observation.IsCandidate && observation.Decision == StrategyDecision.Accept &&
                _acceptedCandidates.Add(candidateKey))
                _acceptedSignals++;
            if (_primaryReasonFacts.Add(factScope + "\u001f" + observation.PrimaryReasonCode))
                Increment(_primaryReasons, observation.PrimaryReasonCode);
            foreach (string reason in observation.SecondaryReasonCodes)
                if (_secondaryReasonFacts.Add(factScope + "\u001f" + reason)) Increment(_secondaryReasons, reason);
            foreach (ThresholdObservation threshold in observation.Thresholds)
            {
                if (!observation.IsCandidate) continue;
                string thresholdKey = factScope + "\u001f" + threshold.Name;
                (long evaluated, long passed, long failed) = _thresholdFunnel.TryGetValue(threshold.Name, out var count)
                    ? count : default;
                if (_thresholdFacts.TryGetValue(thresholdKey, out bool previous))
                {
                    if (previous == threshold.Passed) continue;
                    _thresholdFacts[thresholdKey] = threshold.Passed;
                    _thresholdFunnel[threshold.Name] = (evaluated,
                        passed + (threshold.Passed ? 1 : -1), failed + (threshold.Passed ? -1 : 1));
                }
                else
                {
                    _thresholdFacts[thresholdKey] = threshold.Passed;
                    _thresholdFunnel[threshold.Name] = (evaluated + 1,
                        passed + (threshold.Passed ? 1 : 0), failed + (threshold.Passed ? 0 : 1));
                }
            }
            foreach (string flag in observation.DataQualityFlags)
                if (_dataQualityFacts.Add(factScope + "\u001f" + flag)) Increment(_dataQualityCounts, flag);
            if (observation.PrimaryReasonCode == "OneLegTimeout" && _timeoutCycles.Add(factScope))
                _oneLegTimeouts++;
        }
    }

    public void Record(OrderLifecycleEvent orderEvent)
    {
        ArgumentNullException.ThrowIfNull(orderEvent);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            string cycleId = orderEvent.CycleId;
            int generation = orderEvent.Generation;
            if ((cycleId.Length == 0 || generation <= 0) &&
                ManagedOrderCorrelation.TryParse(orderEvent.ClientOrderId, out string parsedCycle, out int parsedGeneration))
            {
                if (cycleId.Length == 0) cycleId = parsedCycle;
                if (generation <= 0) generation = parsedGeneration;
            }
            string timelineKey = string.Join("\u001f", orderEvent.StrategyInstanceId, orderEvent.ClientOrderId);
            if (!_lifecycleTimelines.TryGetValue(timelineKey, out LifecycleTimeline? timeline))
                _lifecycleTimelines[timelineKey] = timeline = new LifecycleTimeline();
            switch (orderEvent.State)
            {
                case OrderLifecycleState.Submitted: timeline.SubmittedAt ??= orderEvent.Timestamp; break;
                case OrderLifecycleState.Working: timeline.AcceptedAt ??= orderEvent.Timestamp; break;
                case OrderLifecycleState.PartiallyFilled: timeline.FirstFilledAt ??= orderEvent.Timestamp; break;
                case OrderLifecycleState.Filled:
                    timeline.FirstFilledAt ??= orderEvent.Timestamp;
                    timeline.FullyFilledAt ??= orderEvent.Timestamp;
                    break;
                case OrderLifecycleState.CancelPending: timeline.CancelRequestedAt ??= orderEvent.Timestamp; break;
                case OrderLifecycleState.Cancelled: timeline.CancelledAt ??= orderEvent.Timestamp; break;
                case OrderLifecycleState.Rejected: timeline.RejectedAt ??= orderEvent.Timestamp; break;
            }
            var unavailable = new Dictionary<string, string>();
            if (!timeline.SubmittedAt.HasValue) unavailable["submittedAt"] = "SubmitTimestampNotObserved";
            if (!timeline.AcceptedAt.HasValue) unavailable["acceptedAt"] = "BrokerAcceptanceNotObserved";
            if (!timeline.FirstFilledAt.HasValue) unavailable["firstFilledAt"] = "FirstFillNotObserved";
            if (!timeline.FullyFilledAt.HasValue) unavailable["fullyFilledAt"] = "FullFillNotObserved";
            if (!timeline.CancelRequestedAt.HasValue) unavailable["cancelRequestedAt"] = "CancelRequestNotObserved";
            if (!timeline.CancelledAt.HasValue) unavailable["cancelledAt"] = "CancelConfirmationNotObserved";
            if (!timeline.RejectedAt.HasValue) unavailable["rejectedAt"] = "RejectionNotObserved";
            if (string.IsNullOrWhiteSpace(cycleId)) unavailable["cycleId"] = "CycleCorrelationNotObserved";
            if (string.IsNullOrWhiteSpace(orderEvent.Symbol)) unavailable["symbol"] = "OrderSymbolNotObserved";
            if (!orderEvent.Side.HasValue) unavailable["side"] = "OrderSideNotObserved";
            if (!orderEvent.Role.HasValue) unavailable["role"] = "OrderRoleNotObserved";
            if (generation <= 0) unavailable["generation"] = "OrderGenerationNotObserved";
            if (orderEvent.TriggerMarketSequence <= 0)
                unavailable["triggerMarketSequence"] = "TriggerMarketSequenceNotObserved";
            var observation = new OrderLifecycleObservation
            {
                Timestamp = orderEvent.Timestamp,
                StrategyInstanceId = orderEvent.StrategyInstanceId,
                CycleId = cycleId,
                ClientOrderId = orderEvent.ClientOrderId,
                OcoGroupId = orderEvent.OcoGroupId,
                OrderId = orderEvent.OrderId,
                BrokerOrderId = orderEvent.BrokerOrderId,
                Symbol = orderEvent.Symbol,
                Side = orderEvent.Side,
                Role = orderEvent.Role,
                Generation = generation,
                TriggerMarketSequence = orderEvent.TriggerMarketSequence,
                PreviousState = orderEvent.PreviousState,
                State = orderEvent.State,
                Price = orderEvent.Price,
                Quantity = orderEvent.Quantity,
                FilledQuantity = orderEvent.FilledQuantity,
                DeliveredFilledQuantity = orderEvent.DeliveredFilledQuantity,
                BrokerFinalCumulativeFilledQuantity = orderEvent.BrokerFinalCumulativeFilledQuantity,
                RemainingQuantity = orderEvent.RemainingQuantity,
                HasFinalCumulativeFillWatermark = orderEvent.HasFinalCumulativeFillWatermark,
                ReasonCode = orderEvent.ReasonCode,
                SubmittedAt = timeline.SubmittedAt,
                AcceptedAt = timeline.AcceptedAt,
                FirstFilledAt = timeline.FirstFilledAt,
                FullyFilledAt = timeline.FullyFilledAt,
                CancelRequestedAt = timeline.CancelRequestedAt,
                CancelledAt = timeline.CancelledAt,
                RejectedAt = timeline.RejectedAt,
                UnavailableReasons = unavailable
            };
            string transitionKey = string.Join("\u001f", observation.StrategyInstanceId,
                observation.ClientOrderId, observation.OrderId, observation.BrokerOrderId,
                observation.PreviousState, observation.State,
                observation.Price.ToString(CultureInfo.InvariantCulture), observation.Quantity,
                observation.FilledQuantity, observation.RemainingQuantity, observation.ReasonCode,
                observation.CycleId, observation.OcoGroupId, observation.Symbol, observation.Side, observation.Role,
                observation.DeliveredFilledQuantity, observation.BrokerFinalCumulativeFilledQuantity,
                observation.HasFinalCumulativeFillWatermark,
                observation.Generation, observation.TriggerMarketSequence);
            if (!_lifecycleTransitions.Add(transitionKey)) return;
            string serialized = JsonSerializer.Serialize(observation, _json);
            _orderLifecycle.WriteLine(serialized);
            if (orderEvent.State == OrderLifecycleState.Submitted) _submittedOrders++;
            if (orderEvent.State == OrderLifecycleState.Cancelled) _cancelledOrders++;
            if (orderEvent.State == OrderLifecycleState.Rejected) _rejectedOrders++;
            if (orderEvent.ReasonCode.Contains("Timeout", StringComparison.OrdinalIgnoreCase)) _timedOutOrders++;
        }
    }

    public void Record(ExecutionEvent execution)
    {
        ArgumentNullException.ThrowIfNull(execution);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            string cycleId = execution.CycleId;
            int generation = execution.Generation;
            if ((cycleId.Length == 0 || generation <= 0) &&
                ManagedOrderCorrelation.TryParse(execution.ClientOrderId, out string parsedCycle, out int parsedGeneration))
            {
                if (cycleId.Length == 0) cycleId = parsedCycle;
                if (generation <= 0) generation = parsedGeneration;
            }
            _trades.WriteLine(string.Join(",", new[]
            {
                Csv(execution.Timestamp.ToString("O", CultureInfo.InvariantCulture)),
                execution.Sequence.ToString(CultureInfo.InvariantCulture),
                execution.MarketSequence.ToString(CultureInfo.InvariantCulture),
                Csv(execution.StrategyInstanceId), Csv(cycleId), generation.ToString(CultureInfo.InvariantCulture),
                Csv(execution.ClientOrderId), Csv(execution.OrderId),
                Csv(execution.Symbol), execution.Side.ToString(), execution.Role.ToString(),
                execution.Price.ToString(CultureInfo.InvariantCulture),
                execution.Quantity.ToString(CultureInfo.InvariantCulture),
                execution.Commission.ToString(CultureInfo.InvariantCulture),
                execution.TriggerMarketPrice.ToString(CultureInfo.InvariantCulture)
            }));
            _fills++;
            _totalCommission += execution.Commission;
            _totalFillQuantity += execution.Quantity;
            CaptureRoundTrips(execution);
            if (execution.Role == PaperOrderRole.StopLoss) _stopLossExits++;
            if (cycleId.Length > 0 && execution.Role is PaperOrderRole.EntryLong or PaperOrderRole.EntryShort)
            {
                string cycleKey = string.Join("\u001f", execution.StrategyInstanceId, cycleId);
                if (!_cycleEntrySides.TryGetValue(cycleKey, out HashSet<string>? sides))
                    _cycleEntrySides[cycleKey] = sides = new HashSet<string>(StringComparer.Ordinal);
                sides.Add(execution.Side.ToString());
            }
        }
    }

    private void CaptureRoundTrips(ExecutionEvent execution)
    {
        if (execution.Quantity <= 0) return;
        string positionKey = string.Join("\u001f", execution.StrategyInstanceId, execution.Symbol);
        int remaining = execution.Quantity;
        decimal commissionPerUnit = execution.Commission / execution.Quantity;
        decimal multiplier = _manifest?.Universe.FirstOrDefault(product =>
            string.Equals(product.Symbol, execution.Symbol, StringComparison.OrdinalIgnoreCase))?.ContractMultiplier ?? 1m;
        if (multiplier <= 0m) multiplier = 1m;
        int executionSign = execution.Side == BuySell.Buy ? 1 : -1;
        while (remaining > 0)
        {
            if (!_openExcursions.TryGetValue(positionKey, out OpenTradeExcursion? excursion))
            {
                excursion = new OpenTradeExcursion { Multiplier = multiplier, StartedAt = execution.Timestamp };
                _openExcursions[positionKey] = excursion;
            }
            int netSign = Math.Sign(excursion.NetQuantity);
            if (netSign == 0 || netSign == executionSign)
            {
                excursion.Lots.Add(new OpenExecutionLot
                {
                    Side = execution.Side, Price = execution.Price, Quantity = remaining,
                    CommissionPerUnit = commissionPerUnit, Timestamp = execution.Timestamp
                });
                excursion.NetQuantity += executionSign * remaining;
                excursion.Commission += commissionPerUnit * remaining;
                break;
            }

            OpenExecutionLot entry = excursion.Lots[0];
            int closed = Math.Min(remaining, entry.Quantity);
            decimal grossPoints = entry.Side == BuySell.Buy
                ? execution.Price - entry.Price
                : entry.Price - execution.Price;
            excursion.GrossPnl += grossPoints * closed * excursion.Multiplier;
            excursion.Commission += commissionPerUnit * closed;
            excursion.ClosedQuantity += closed;
            excursion.NetQuantity += executionSign * closed;
            entry.Quantity -= closed;
            remaining -= closed;
            if (entry.Quantity == 0) excursion.Lots.RemoveAt(0);
            if (excursion.NetQuantity != 0) continue;

            decimal net = excursion.GrossPnl - excursion.Commission;
            decimal netPoints = excursion.ClosedQuantity == 0
                ? 0m : net / (excursion.Multiplier * excursion.ClosedQuantity);
            decimal holding = Math.Max(0m,
                (decimal)(execution.Timestamp - excursion.StartedAt).Ticks / TimeSpan.TicksPerSecond);
            _completedRoundTrips.Add(new CompletedRoundTrip(
                excursion.GrossPnl, excursion.Commission, net, netPoints, holding));
            _openExcursions.Remove(positionKey);
        }
    }

    public void Record(ParameterExperimentRecord experiment)
    {
        ArgumentNullException.ThrowIfNull(experiment);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ValidateExperiment(experiment);
            _experiments.WriteLine(JsonSerializer.Serialize(experiment, _json));
        }
    }

    private static void ValidateExperiment(ParameterExperimentRecord experiment)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(experiment.ExperimentId)) missing.Add("ExperimentId");
        if (string.IsNullOrWhiteSpace(experiment.BaselineRunId)) missing.Add("BaselineRunId");
        if (string.IsNullOrWhiteSpace(experiment.CandidateRunId)) missing.Add("CandidateRunId");
        if (string.IsNullOrWhiteSpace(experiment.StrategyType)) missing.Add("StrategyType");
        if (string.IsNullOrWhiteSpace(experiment.CodeVersion)) missing.Add("CodeVersion");
        if (experiment.CandidateParameters.Count == 0) missing.Add("CandidateParameters");
        if (experiment.ChangedParameters.Count == 0) missing.Add("ChangedParameters");
        if (string.IsNullOrWhiteSpace(experiment.SearchMethod)) missing.Add("SearchMethod");
        if (string.IsNullOrWhiteSpace(experiment.InputArchiveSha256)) missing.Add("InputArchiveSha256");
        if (string.IsNullOrWhiteSpace(experiment.DataSplitId)) missing.Add("DataSplitId");
        if (string.IsNullOrWhiteSpace(experiment.PrimaryObjective) && experiment.Objectives.Count == 0)
            missing.Add("Objectives");
        if (experiment.SecondaryObjectives.Count == 0 && experiment.Objectives.Count < 2)
            missing.Add("SecondaryObjectives");
        if (experiment.GuardrailValues.Count == 0 && experiment.Guardrails.Count == 0) missing.Add("Guardrails");
        bool incompleteStatus = experiment.Status is ParameterExperimentStatus.Rejected or ParameterExperimentStatus.Incomplete;
        if (experiment.Results.Count == 0 &&
            (!incompleteStatus || !experiment.UnavailableReasons.ContainsKey("Results"))) missing.Add("Results");
        if (experiment.ExecutionAssumptions.Count == 0) missing.Add("ExecutionAssumptions");
        if (experiment.ProductMapping.Count == 0) missing.Add("ProductMapping");
        if (!new[] { 0, 1, 2 }.All(key => experiment.AdverseSlippageStress.TryGetValue(key, out decimal? value) &&
                                               value.HasValue) &&
            (!incompleteStatus || !experiment.UnavailableReasons.ContainsKey("AdverseSlippageStress")))
            missing.Add("AdverseSlippageStress[0,1,2]");
        if (experiment.ParameterDefinitions.Count == 0) missing.Add("ParameterDefinitions");
        foreach ((string name, ParameterSearchDefinition definition) in experiment.ParameterDefinitions)
        {
            if (string.IsNullOrWhiteSpace(definition.Type) || string.IsNullOrWhiteSpace(definition.Unit) ||
                !definition.AllowedMinimum.HasValue || !definition.AllowedMaximum.HasValue ||
                !definition.SearchMinimum.HasValue || !definition.SearchMaximum.HasValue ||
                !definition.Step.HasValue || definition.Step <= 0m ||
                definition.AllowedMinimum > definition.AllowedMaximum ||
                definition.SearchMinimum > definition.SearchMaximum ||
                definition.SearchMinimum < definition.AllowedMinimum ||
                definition.SearchMaximum > definition.AllowedMaximum)
                missing.Add($"ParameterDefinitions.{name}");
        }
        foreach ((string name, string value) in experiment.CandidateParameters)
        {
            if (!experiment.ParameterDefinitions.TryGetValue(name, out ParameterSearchDefinition? definition))
            {
                missing.Add($"ParameterDefinitions.{name}");
                continue;
            }
            if (!TryValidateParameterValue(value, definition)) missing.Add($"CandidateParameters.{name}");
        }
        foreach ((string name, string value) in experiment.ChangedParameters)
            if (!experiment.CandidateParameters.TryGetValue(name, out string? candidate) ||
                !string.Equals(candidate, value, StringComparison.Ordinal) ||
                !experiment.ParameterDefinitions.ContainsKey(name))
                missing.Add($"ChangedParameters.{name}");
        if (experiment.SearchMethod.Equals("grid", StringComparison.OrdinalIgnoreCase) &&
            !experiment.CandidateOrdinal.HasValue) missing.Add("CandidateOrdinal");
        if (experiment.SearchMethod.Equals("random", StringComparison.OrdinalIgnoreCase))
        {
            if (!experiment.RandomSeed.HasValue) missing.Add("RandomSeed");
            if (!experiment.CandidateOrdinal.HasValue) missing.Add("CandidateOrdinal");
        }
        if (experiment.Status is ParameterExperimentStatus.Rejected or ParameterExperimentStatus.Incomplete &&
            string.IsNullOrWhiteSpace(experiment.ReasonCode))
            missing.Add("ReasonCode");
        if (experiment.Status == ParameterExperimentStatus.Valid)
        {
            if (experiment.Results.Any(result => !result.Value.HasValue)) missing.Add("ResultsFinite");
            if (experiment.GuardrailValues.Any(guardrail => !guardrail.Value.HasValue))
                missing.Add("GuardrailValuesFinite");
        }
        if (missing.Count > 0)
            throw new ArgumentException($"{experiment.Status} parameter experiment 缺少可重現欄位：" +
                                        string.Join(",", missing),
                nameof(experiment));
    }

    private static bool TryValidateParameterValue(string value, ParameterSearchDefinition definition)
    {
        if (definition.Type.Equals("bool", StringComparison.OrdinalIgnoreCase))
            return bool.TryParse(value, out _);
        if (definition.Type.Equals("string", StringComparison.OrdinalIgnoreCase)) return true;
        if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed))
            return false;
        if (definition.Type.Equals("int", StringComparison.OrdinalIgnoreCase) && parsed != decimal.Truncate(parsed))
            return false;
        if (definition.AllowedMinimum.HasValue && parsed < definition.AllowedMinimum.Value ||
            definition.AllowedMaximum.HasValue && parsed > definition.AllowedMaximum.Value ||
            definition.SearchMinimum.HasValue && parsed < definition.SearchMinimum.Value ||
            definition.SearchMaximum.HasValue && parsed > definition.SearchMaximum.Value)
            return false;
        if (definition.Step is > 0m)
        {
            decimal origin = definition.SearchMinimum ?? definition.AllowedMinimum ?? 0m;
            if ((parsed - origin) % definition.Step.Value != 0m) return false;
        }
        return definition.Type.Equals("decimal", StringComparison.OrdinalIgnoreCase) ||
               definition.Type.Equals("int", StringComparison.OrdinalIgnoreCase);
    }

    public void FinalizeSummary(DateTimeOffset finalizedAt)
    {
        lock (_sync)
        {
            if (_disposed || _summaryFinalized) return;
            var unavailable = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["marketEvents"] = "MarketEventsNotObserved",
                ["modelSlippage"] = "SlippageModelNotConfigured",
                ["mfePoints"] = "NoMarkToMarketPath",
                ["maePoints"] = "NoMarkToMarketPath"
            };
            if (!_hasSnapshot)
            {
                unavailable["grossPnl"] = "PerformanceSnapshotNotObserved";
                unavailable["netPnl"] = "PerformanceSnapshotNotObserved";
                unavailable["maximumDrawdown"] = "EquityCurveNotObserved";
                unavailable["completedTrades"] = "LedgerSnapshotNotObserved";
            }
            if (_fills == 0) unavailable["executionCosts"] = "ExecutionsNotObserved";
            decimal? winRate = null;
            decimal? averageWinning = null;
            decimal? averageLosing = null;
            decimal? userProfitFactor = null;
            decimal? holdingSeconds = null;
            if (_completedRoundTrips.Count == 0)
            {
                unavailable["winRate"] = "NoCompletedTrades";
                unavailable["averageWinningPoints"] = "NoCompletedTrades";
                unavailable["averageLosingPoints"] = "NoCompletedTrades";
                unavailable["userDefinedProfitFactor"] = "NoCompletedTrades";
                unavailable["holdingSeconds"] = "NoCompletedTrades";
            }
            else
            {
                CompletedRoundTrip[] winners = _completedRoundTrips.Where(x => x.NetPoints > 0m).ToArray();
                CompletedRoundTrip[] losers = _completedRoundTrips.Where(x => x.NetPoints < 0m).ToArray();
                winRate = (decimal)winners.Length / _completedRoundTrips.Count * 100m;
                holdingSeconds = _completedRoundTrips.Average(x => x.HoldingSeconds);
                if (winners.Length > 0) averageWinning = winners.Average(x => x.NetPoints);
                else unavailable["averageWinningPoints"] = "NoWinningTrades";
                if (losers.Length > 0) averageLosing = losers.Average(x => x.NetPoints);
                else unavailable["averageLosingPoints"] = "NoLosingTrades";
                if (averageWinning.HasValue && averageLosing is < 0m)
                    userProfitFactor = -averageWinning.Value / averageLosing.Value;
                else unavailable["userDefinedProfitFactor"] = winners.Length == 0
                    ? "NoWinningTrades" : "NoLosingTrades";
            }
            decimal? fillRate = _submittedOrders == 0 ? null : (decimal)_fills / _submittedOrders;
            decimal? cancellationRate = _submittedOrders == 0 ? null : (decimal)_cancelledOrders / _submittedOrders;
            if (!fillRate.HasValue) unavailable["fillRate"] = "SubmittedOrdersNotObserved";
            if (!cancellationRate.HasValue) unavailable["cancellationRate"] = "SubmittedOrdersNotObserved";
            decimal? roundTripGross = _completedRoundTrips.Count > 0
                ? _completedRoundTrips.Sum(x => x.GrossPnl) : null;
            decimal? roundTripCosts = _completedRoundTrips.Count > 0
                ? _completedRoundTrips.Sum(x => x.Commission) : null;
            decimal? roundTripNet = _completedRoundTrips.Count > 0
                ? _completedRoundTrips.Sum(x => x.NetPnl) : null;
            long? ledgerCompletedTrades = _hasSnapshot
                ? _latestSnapshots.Values.Sum(snapshot => (long)snapshot.CompletedTrades) : null;
            decimal? ledgerRealizedPnl = _hasSnapshot
                ? _latestSnapshots.Values.Sum(snapshot => snapshot.RealizedPnl) : null;
            decimal? ledgerNetPnl = _hasSnapshot
                ? _latestSnapshots.Values.Sum(snapshot => snapshot.NetPnl) : null;
            if (roundTripGross.HasValue) unavailable.Remove("grossPnl");
            if (roundTripCosts.HasValue) unavailable.Remove("executionCosts");
            if (roundTripNet.HasValue) unavailable.Remove("netPnl");
            decimal? stressBase = roundTripNet ?? ledgerNetPnl;
            decimal? adversePerTick = null;
            RunProductIdentity? tradable = _manifest?.Universe.FirstOrDefault();
            if (stressBase.HasValue && tradable is not null)
                adversePerTick = tradable.TickSize * tradable.ContractMultiplier * _totalFillQuantity;
            var stress = new Dictionary<int, decimal?>
            {
                [0] = stressBase,
                [1] = adversePerTick.HasValue ? stressBase - adversePerTick : null,
                [2] = adversePerTick.HasValue ? stressBase - 2m * adversePerTick : null
            };
            if (!adversePerTick.HasValue) unavailable["adverseSlippageStress"] = "ProductSpecificationOrExecutionsNotObserved";
            var summary = new RunSummary
            {
                FinalizedAt = finalizedAt,
                MarketEvents = null,
                WarmupObservations = _warmupObservations,
                CandidateSignals = _candidateSignals,
                AcceptedSignals = _acceptedSignals,
                SubmittedOrders = _submittedOrders,
                TimedOutOrders = _timedOutOrders,
                CancelledOrders = _cancelledOrders,
                RejectedOrders = _rejectedOrders,
                Fills = _fills,
                CompletedTrades = ledgerCompletedTrades,
                BothLegFilledCycles = _cycleEntrySides.Values.LongCount(sides => sides.Count >= 2),
                OneLegFilledCycles = _cycleEntrySides.Values.LongCount(sides => sides.Count == 1),
                StopLossExits = _stopLossExits,
                OneLegTimeouts = _oneLegTimeouts,
                FillRate = fillRate,
                CancellationRate = cancellationRate,
                GrossPnl = roundTripGross ?? ledgerRealizedPnl,
                ExecutionCosts = roundTripCosts ?? (_fills > 0 ? _totalCommission : null),
                NetPnl = roundTripNet ?? ledgerNetPnl,
                WinRate = winRate,
                AverageWinningPoints = averageWinning,
                AverageLosingPoints = averageLosing,
                UserDefinedProfitFactor = userProfitFactor,
                MaximumDrawdown = _hasSnapshot ? _maximumDrawdown : null,
                MfePoints = null,
                MaePoints = null,
                HoldingSeconds = holdingSeconds,
                AdverseSlippageStress = stress,
                ThresholdFunnel = _thresholdFunnel.ToDictionary(pair => pair.Key,
                    pair => new ThresholdFunnelCount(pair.Value.Evaluated, pair.Value.Passed, pair.Value.Failed),
                    StringComparer.Ordinal),
                DataQualityCounts = new Dictionary<string, long>(_dataQualityCounts),
                PrimaryReasonCounts = new Dictionary<string, long>(_primaryReasons),
                SecondaryReasonCounts = new Dictionary<string, long>(_secondaryReasons),
                UnavailableReasons = unavailable
            };
            File.WriteAllText(Path.Combine(DirectoryPath, "run-summary.json"),
                JsonSerializer.Serialize(summary, _json), new UTF8Encoding(false));
            _summaryFinalized = true;
        }
    }

    private StreamWriter Writer(string name) => new(new FileStream(Path.Combine(DirectoryPath, name),
        FileMode.Append, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false)) { AutoFlush = true };

    private static string Csv(string text) => '"' + text.Replace("\"", "\"\"") + '"';

    private static void Increment(IDictionary<string, long> counts, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return;
        counts[reason] = counts.TryGetValue(reason, out long count) ? count + 1 : 1;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SimulationRunRecorder));
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            var errors = new List<Exception>();
            if (!_summaryFinalized)
            {
                _disposed = false;
                try { FinalizeSummary(DateTimeOffset.UtcNow); }
                catch (Exception exception) { errors.Add(exception); }
                finally { _disposed = true; }
            }
            foreach (IDisposable stream in new IDisposable[]
                     { _events, _decisions, _orderLifecycle, _trades, _experiments })
            {
                try { stream.Dispose(); }
                catch (Exception exception) { errors.Add(exception); }
            }
            if (errors.Count > 0) throw new AggregateException("Recorder finalize/dispose 失敗。", errors);
        }
    }
}
