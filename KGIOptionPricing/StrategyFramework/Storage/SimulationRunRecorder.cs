using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

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
    private readonly HashSet<string> _lifecycleTransitions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LifecycleTimeline> _lifecycleTimelines = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _primaryReasons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _secondaryReasons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (long Evaluated, long Passed, long Failed)> _thresholdFunnel = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _dataQualityCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _cycleEntrySides = new(StringComparer.Ordinal);
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
    private StrategySnapshot? _latestSnapshot;
    private RunManifest? _manifest;

    public string DirectoryPath { get; }

    public SimulationRunRecorder(string directoryPath, TimeSpan? decisionHeartbeatInterval = null)
    {
        DirectoryPath = Path.GetFullPath(directoryPath);
        Directory.CreateDirectory(DirectoryPath);
        _decisionHeartbeatInterval = decisionHeartbeatInterval ?? TimeSpan.FromSeconds(5);
        if (_decisionHeartbeatInterval < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(decisionHeartbeatInterval));
        _events = Writer("strategy-events.jsonl");
        _decisions = Writer("strategy-decisions.jsonl");
        _orderLifecycle = Writer("order-lifecycle.jsonl");
        _experiments = Writer("parameter-experiments.jsonl");
        _trades = Writer("executions.csv");
        _trades.WriteLine("timestamp,sequence,marketSequence,instanceId,cycleId,generation,clientOrderId,orderId,symbol,side,role,price,quantity,commission,triggerMarketPrice");
    }

    public void RecordManifest(RunManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_manifestWritten || File.Exists(Path.Combine(DirectoryPath, "run-manifest.json")))
                throw new InvalidOperationException("run-manifest.json 每次執行只能寫入一次。");
            File.WriteAllText(Path.Combine(DirectoryPath, "run-manifest.json"),
                JsonSerializer.Serialize(manifest, _json), new UTF8Encoding(false));
            _manifestWritten = true;
            _manifest = manifest;
        }
    }

    public void Record(StrategySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_sync)
        {
            if (_disposed) return;
            _events.WriteLine(JsonSerializer.Serialize(snapshot, _json));
            _latestSnapshot = snapshot;
            _hasSnapshot = true;
            _peakNetPnl = Math.Max(_peakNetPnl, snapshot.NetPnl);
            _maximumDrawdown = Math.Max(_maximumDrawdown, _peakNetPnl - snapshot.NetPnl);
        }
    }

    public void Record(StrategyDecisionObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        lock (_sync)
        {
            if (_disposed) return;
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
            if (observation.Decision == StrategyDecision.Warmup) _warmupObservations++;
            string candidateKey = string.Join("\u001f", observation.StrategyInstanceId, observation.CandidateId);
            if (!string.IsNullOrWhiteSpace(observation.CandidateId) && _candidates.Add(candidateKey))
                _candidateSignals++;
            if (observation.Decision == StrategyDecision.Accept && _acceptedCandidates.Add(candidateKey))
                _acceptedSignals++;
            Increment(_primaryReasons, observation.PrimaryReasonCode);
            foreach (string reason in observation.SecondaryReasonCodes) Increment(_secondaryReasons, reason);
            foreach (ThresholdObservation threshold in observation.Thresholds)
            {
                (long evaluated, long passed, long failed) = _thresholdFunnel.TryGetValue(threshold.Name, out var count)
                    ? count : default;
                _thresholdFunnel[threshold.Name] = (evaluated + 1,
                    passed + (threshold.Passed ? 1 : 0), failed + (threshold.Passed ? 0 : 1));
            }
            foreach (string flag in observation.DataQualityFlags) Increment(_dataQualityCounts, flag);
            if (observation.PrimaryReasonCode == "OneLegTimeout") _oneLegTimeouts++;
        }
    }

    public void Record(OrderLifecycleEvent orderEvent)
    {
        ArgumentNullException.ThrowIfNull(orderEvent);
        lock (_sync)
        {
            if (_disposed) return;
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
            if (generation <= 0) unavailable["generation"] = "OrderGenerationNotObserved";
            if (orderEvent.TriggerMarketSequence <= 0)
                unavailable["triggerMarketSequence"] = "TriggerMarketSequenceNotObserved";
            var observation = new OrderLifecycleObservation
            {
                Timestamp = orderEvent.Timestamp,
                StrategyInstanceId = orderEvent.StrategyInstanceId,
                CycleId = cycleId,
                ClientOrderId = orderEvent.ClientOrderId,
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
                RemainingQuantity = orderEvent.RemainingQuantity,
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
                observation.CycleId, observation.Symbol, observation.Side, observation.Role,
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
            if (_disposed) return;
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

    public void Record(ParameterExperimentRecord experiment)
    {
        ArgumentNullException.ThrowIfNull(experiment);
        lock (_sync)
        {
            if (_disposed) return;
            _experiments.WriteLine(JsonSerializer.Serialize(experiment, _json));
        }
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
                ["winRate"] = "CompletedTradeDetailsNotObserved",
                ["averageWinningPoints"] = "CompletedTradeDetailsNotObserved",
                ["averageLosingPoints"] = "CompletedTradeDetailsNotObserved",
                ["userDefinedProfitFactor"] = "CompletedTradeDetailsNotObserved",
                ["mfePoints"] = "ExecutableBookPathNotObserved",
                ["maePoints"] = "ExecutableBookPathNotObserved",
                ["holdingSeconds"] = "TradePairTimestampsNotObserved"
            };
            if (!_hasSnapshot)
            {
                unavailable["grossPnl"] = "PerformanceSnapshotNotObserved";
                unavailable["netPnl"] = "PerformanceSnapshotNotObserved";
                unavailable["maximumDrawdown"] = "EquityCurveNotObserved";
            }
            if (_fills == 0) unavailable["executionCosts"] = "ExecutionsNotObserved";
            decimal? fillRate = _submittedOrders == 0 ? null : (decimal)_fills / _submittedOrders;
            decimal? cancellationRate = _submittedOrders == 0 ? null : (decimal)_cancelledOrders / _submittedOrders;
            if (!fillRate.HasValue) unavailable["fillRate"] = "SubmittedOrdersNotObserved";
            if (!cancellationRate.HasValue) unavailable["cancellationRate"] = "SubmittedOrdersNotObserved";
            decimal? stressBase = _hasSnapshot ? _latestSnapshot?.NetPnl : null;
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
                CompletedTrades = _latestSnapshot?.CompletedTrades ?? 0,
                BothLegFilledCycles = _cycleEntrySides.Values.LongCount(sides => sides.Count >= 2),
                OneLegFilledCycles = _cycleEntrySides.Values.LongCount(sides => sides.Count == 1),
                StopLossExits = _stopLossExits,
                OneLegTimeouts = _oneLegTimeouts,
                FillRate = fillRate,
                CancellationRate = cancellationRate,
                GrossPnl = _hasSnapshot ? _latestSnapshot?.RealizedPnl : null,
                ExecutionCosts = _fills > 0 ? _totalCommission : null,
                NetPnl = _hasSnapshot ? _latestSnapshot?.NetPnl : null,
                MaximumDrawdown = _hasSnapshot ? _maximumDrawdown : null,
                MfePoints = null,
                MaePoints = null,
                HoldingSeconds = null,
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
