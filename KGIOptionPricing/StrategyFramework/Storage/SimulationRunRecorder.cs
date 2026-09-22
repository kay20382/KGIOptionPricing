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
    private readonly Dictionary<string, long> _primaryReasons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _secondaryReasons = new(StringComparer.Ordinal);
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
    private StrategySnapshot? _latestSnapshot;

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
        _trades.WriteLine("timestamp,sequence,marketSequence,instanceId,clientOrderId,orderId,symbol,side,role,price,quantity,commission,triggerMarketPrice");
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
        }
    }

    public void Record(OrderLifecycleEvent orderEvent)
    {
        ArgumentNullException.ThrowIfNull(orderEvent);
        lock (_sync)
        {
            if (_disposed) return;
            var observation = new OrderLifecycleObservation
            {
                Timestamp = orderEvent.Timestamp,
                StrategyInstanceId = orderEvent.StrategyInstanceId,
                CycleId = ExtractCycleId(orderEvent.ClientOrderId),
                ClientOrderId = orderEvent.ClientOrderId,
                OrderId = orderEvent.OrderId,
                BrokerOrderId = orderEvent.BrokerOrderId,
                PreviousState = orderEvent.PreviousState,
                State = orderEvent.State,
                Price = orderEvent.Price,
                Quantity = orderEvent.Quantity,
                FilledQuantity = orderEvent.FilledQuantity,
                RemainingQuantity = orderEvent.RemainingQuantity,
                ReasonCode = orderEvent.ReasonCode
            };
            string transitionKey = string.Join("\u001f", observation.StrategyInstanceId,
                observation.ClientOrderId, observation.OrderId, observation.BrokerOrderId,
                observation.PreviousState, observation.State,
                observation.Price.ToString(CultureInfo.InvariantCulture), observation.Quantity,
                observation.FilledQuantity, observation.RemainingQuantity, observation.ReasonCode);
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
            _trades.WriteLine(string.Join(",", new[]
            {
                Csv(execution.Timestamp.ToString("O", CultureInfo.InvariantCulture)),
                execution.Sequence.ToString(CultureInfo.InvariantCulture),
                execution.MarketSequence.ToString(CultureInfo.InvariantCulture),
                Csv(execution.StrategyInstanceId), Csv(execution.ClientOrderId), Csv(execution.OrderId),
                Csv(execution.Symbol), execution.Side.ToString(), execution.Role.ToString(),
                execution.Price.ToString(CultureInfo.InvariantCulture),
                execution.Quantity.ToString(CultureInfo.InvariantCulture),
                execution.Commission.ToString(CultureInfo.InvariantCulture),
                execution.TriggerMarketPrice.ToString(CultureInfo.InvariantCulture)
            }));
            _fills++;
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
                ["grossPnl"] = "GrossPnlNotObserved",
                ["executionCosts"] = "ExecutionCostsNotObserved",
                ["modelSlippage"] = "SlippageModelNotConfigured",
                ["winRate"] = "CompletedTradeDetailsNotObserved",
                ["averageWinningPoints"] = "CompletedTradeDetailsNotObserved",
                ["averageLosingPoints"] = "CompletedTradeDetailsNotObserved",
                ["userDefinedProfitFactor"] = "CompletedTradeDetailsNotObserved",
                ["maximumDrawdown"] = "EquityCurveNotObserved"
            };
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
                NetPnl = _latestSnapshot?.NetPnl,
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

    private static string ExtractCycleId(string clientOrderId)
    {
        if (string.IsNullOrWhiteSpace(clientOrderId)) return string.Empty;
        int suffix = new[] { "-buy-g", "-sell-g", "-exit-" }
            .Select(marker => clientOrderId.IndexOf(marker, StringComparison.OrdinalIgnoreCase))
            .Where(index => index >= 0)
            .DefaultIfEmpty(clientOrderId.Length)
            .Min();
        return clientOrderId[..suffix];
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
            if (!_summaryFinalized) FinalizeSummary(DateTimeOffset.UtcNow);
            _disposed = true;
            _events.Dispose();
            _decisions.Dispose();
            _orderLifecycle.Dispose();
            _trades.Dispose();
            _experiments.Dispose();
        }
    }
}
