using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KGIOptionPricing.StrategyFramework.Recovery;

namespace KGIOptionPricing.StrategyFramework.Execution;

public interface ILiveOrderOwnershipStore : IDisposable
{
    LiveOrderOwnership Prepare(LiveOrderOwnership ownership);
    void BindAlias(LiveOrderOwnerKey owner, BrokerOrderAlias alias);
    FillAcceptance TryAcceptFill(LiveOrderOwnerKey owner, string? execId, int cumulativeQuantity);
    void MarkSubmissionUnknown(LiveOrderOwnerKey owner, string reason);
    void AppendCheckpoint(StrategyRecoveryCheckpoint checkpoint);
    void AppendQuarantinedEvent(QuarantinedBrokerEvent quarantinedEvent);
    LiveOwnershipSnapshot Replay();
    IReadOnlyList<LiveOrderOwnership> LoadOpenRoutes(string account, string symbol);
}

public sealed class LiveOrderOwnershipStore : ILiveOrderOwnershipStore
{
    private sealed record JournalEnvelope(long Sequence, string EventType, JsonElement Payload, string Checksum);
    private sealed record AliasPayload(LiveOrderOwnerKey Owner, BrokerOrderAlias Alias);
    private sealed record FillPayload(LiveOrderOwnerKey Owner, string ExecId, int CumulativeQuantity);
    private sealed record WatermarkPayload(LiveOrderOwnerKey Owner, int CumulativeQuantity);
    private sealed record UnknownPayload(LiveOrderOwnerKey Owner, string Reason);

    private readonly object _sync = new();
    private readonly string _journalPath;
    private readonly Dictionary<LiveOrderOwnerKey, LiveOrderOwnership> _routes = new();
    private readonly Dictionary<BrokerOrderAlias, LiveOrderOwnerKey> _aliases = new();
    private readonly Dictionary<LiveOrderOwnerKey, HashSet<string>> _execIds = new();
    private readonly Dictionary<LiveOrderOwnerKey, int> _watermarks = new();
    private readonly Dictionary<(string InstanceId, long RunGeneration), StrategyRecoveryCheckpoint> _checkpoints = new();
    private readonly List<QuarantinedBrokerEvent> _quarantinedEvents = new();
    private long _sequence;
    private bool _disposed;
    private string _quarantineReason = string.Empty;

    public LiveOrderOwnershipStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory.CreateDirectory(directory);
        _journalPath = Path.Combine(directory, "ownership.jsonl");
        ReplayJournal(repairIncompleteTail: true);
    }

    public LiveOrderOwnership Prepare(LiveOrderOwnership ownership)
    {
        ArgumentNullException.ThrowIfNull(ownership);
        lock (_sync)
        {
            ThrowIfUnavailable();
            ValidateOwnership(ownership);
            if (_routes.ContainsKey(ownership.Key))
                throw new InvalidOperationException($"Duplicate strategy owner key: {ownership.Key}");
            Append("Prepare", ownership);
            _routes.Add(ownership.Key, ownership);
            _watermarks.Add(ownership.Key, 0);
            _execIds.Add(ownership.Key, new HashSet<string>(StringComparer.Ordinal));
            return ownership;
        }
    }

    public void BindAlias(LiveOrderOwnerKey owner, BrokerOrderAlias alias)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(alias);
        lock (_sync)
        {
            ThrowIfUnavailable();
            if (!_routes.ContainsKey(owner)) throw new InvalidOperationException("Cannot bind alias to an unknown owner.");
            ValidateAlias(alias);
            if (_aliases.TryGetValue(alias, out LiveOrderOwnerKey? existing))
            {
                if (existing == owner) return;
                throw new InvalidOperationException($"Broker alias is already bound to another owner: {alias}");
            }
            Append("Alias", new AliasPayload(owner, alias));
            _aliases.Add(alias, owner);
        }
    }

    public FillAcceptance TryAcceptFill(LiveOrderOwnerKey owner, string? execId, int cumulativeQuantity)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (cumulativeQuantity < 0) throw new ArgumentOutOfRangeException(nameof(cumulativeQuantity));
        lock (_sync)
        {
            ThrowIfUnavailable();
            if (!_routes.TryGetValue(owner, out LiveOrderOwnership? route))
                throw new InvalidOperationException("Cannot accept a fill for an unknown owner.");
            if (!string.IsNullOrWhiteSpace(execId) && _execIds.TryGetValue(owner, out HashSet<string>? seen) && seen.Contains(execId))
                return new FillAcceptance(false, 0);
            if (cumulativeQuantity > route.Quantity)
                throw new InvalidOperationException("Cumulative fill quantity exceeds the owned order quantity.");
            HashSet<string> ids = _execIds[owner];
            if (!string.IsNullOrWhiteSpace(execId))
            {
                if (ids.Contains(execId)) return new FillAcceptance(false, 0);
                Append("ExecFill", new FillPayload(owner, execId, cumulativeQuantity));
                ids.Add(execId);
                int prior = _watermarks[owner];
                int effective = Math.Max(prior, cumulativeQuantity);
                _watermarks[owner] = effective;
                _routes[owner] = route with { CumulativeFilledQuantity = effective };
                return new FillAcceptance(true, Math.Max(0, effective - prior));
            }

            int previous = _watermarks[owner];
            if (cumulativeQuantity <= previous) return new FillAcceptance(false, 0);
            Append("FillWatermark", new WatermarkPayload(owner, cumulativeQuantity));
            _watermarks[owner] = cumulativeQuantity;
            _routes[owner] = route with { CumulativeFilledQuantity = cumulativeQuantity };
            return new FillAcceptance(true, cumulativeQuantity - previous);
        }
    }

    public void MarkSubmissionUnknown(LiveOrderOwnerKey owner, string reason)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        lock (_sync)
        {
            ThrowIfUnavailable();
            if (!_routes.TryGetValue(owner, out LiveOrderOwnership? route))
                throw new InvalidOperationException("Cannot mark an unknown owner submission.");
            Append("SubmissionUnknown", new UnknownPayload(owner, reason));
            _routes[owner] = route with { SubmissionState = StrategySubmissionState.Unknown, Reason = reason };
        }
    }

    public void AppendCheckpoint(StrategyRecoveryCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        lock (_sync)
        {
            ThrowIfUnavailable();
            if (string.IsNullOrWhiteSpace(checkpoint.StrategyType) ||
                string.IsNullOrWhiteSpace(checkpoint.StrategyInstanceId) ||
                checkpoint.StrategyRunGeneration <= 0 || string.IsNullOrWhiteSpace(checkpoint.Schema))
                throw new ArgumentException("Checkpoint identity and schema are required.", nameof(checkpoint));
            Append("Checkpoint", checkpoint);
            _checkpoints[(checkpoint.StrategyInstanceId, checkpoint.StrategyRunGeneration)] = checkpoint;
        }
    }

    public void AppendQuarantinedEvent(QuarantinedBrokerEvent quarantinedEvent)
    {
        ArgumentNullException.ThrowIfNull(quarantinedEvent);
        lock (_sync)
        {
            ThrowIfUnavailable();
            Append("Quarantine", quarantinedEvent);
            _quarantinedEvents.Add(quarantinedEvent);
        }
    }

    public LiveOwnershipSnapshot Replay()
    {
        lock (_sync)
        {
            return new LiveOwnershipSnapshot
            {
                Routes = _routes.Values.ToArray(),
                Aliases = new Dictionary<BrokerOrderAlias, LiveOrderOwnerKey>(_aliases),
                Checkpoints = _checkpoints.Values.ToArray(),
                QuarantinedEvents = _quarantinedEvents.ToArray(),
                IsQuarantined = _quarantineReason.Length > 0,
                QuarantineReason = _quarantineReason
            };
        }
    }

    public IReadOnlyList<LiveOrderOwnership> LoadOpenRoutes(string account, string symbol)
    {
        lock (_sync)
            return _routes.Values.Where(route =>
                string.Equals(route.Account, account, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(route.Symbol, symbol, StringComparison.OrdinalIgnoreCase) &&
                route.SubmissionState is not (StrategySubmissionState.Filled or StrategySubmissionState.Cancelled or StrategySubmissionState.Rejected))
                .ToArray();
    }

    private void ReplayJournal(bool repairIncompleteTail)
    {
        if (!File.Exists(_journalPath)) return;
        byte[] bytes = File.ReadAllBytes(_journalPath);
        int validLength = Array.LastIndexOf(bytes, (byte)'\n') + 1;
        if (validLength < bytes.Length && repairIncompleteTail)
        {
            using var repair = new FileStream(_journalPath, FileMode.Open, FileAccess.Write, FileShare.Read);
            repair.SetLength(validLength);
            repair.Flush(true);
        }
        string complete = Encoding.UTF8.GetString(bytes, 0, validLength);
        foreach (string line in complete.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                JournalEnvelope envelope = JsonSerializer.Deserialize<JournalEnvelope>(line)
                    ?? throw new JsonException("Empty journal envelope.");
                if (envelope.Sequence != _sequence + 1 ||
                    !string.Equals(envelope.Checksum, ComputeChecksum(envelope.Sequence, envelope.EventType, envelope.Payload), StringComparison.Ordinal))
                    throw new LiveOwnershipJournalException("Ownership journal sequence or checksum mismatch.");
                ApplyEnvelope(envelope);
                _sequence = envelope.Sequence;
            }
            catch (Exception exception) when (exception is JsonException or LiveOwnershipJournalException or InvalidOperationException or ArgumentException)
            {
                _quarantineReason = $"Ownership journal is corrupt at sequence {_sequence + 1}: {exception.Message}";
                return;
            }
        }
    }

    private void ApplyEnvelope(JournalEnvelope envelope)
    {
        switch (envelope.EventType)
        {
            case "Prepare":
            {
                LiveOrderOwnership route = envelope.Payload.Deserialize<LiveOrderOwnership>() ?? throw new JsonException("Missing prepared route.");
                ValidateOwnership(route);
                if (_routes.ContainsKey(route.Key)) throw new LiveOwnershipJournalException("Duplicate immutable owner in journal.");
                _routes.Add(route.Key, route);
                _watermarks.Add(route.Key, route.CumulativeFilledQuantity);
                _execIds.Add(route.Key, new HashSet<string>(StringComparer.Ordinal));
                break;
            }
            case "Alias":
            {
                AliasPayload payload = envelope.Payload.Deserialize<AliasPayload>() ?? throw new JsonException("Missing alias payload.");
                ValidateAlias(payload.Alias);
                if (!_routes.ContainsKey(payload.Owner) || !_aliases.TryAdd(payload.Alias, payload.Owner))
                    throw new LiveOwnershipJournalException("Unknown owner or duplicate alias in journal.");
                break;
            }
            case "ExecFill":
            {
                FillPayload payload = envelope.Payload.Deserialize<FillPayload>() ?? throw new JsonException("Missing execution payload.");
                if (!_routes.TryGetValue(payload.Owner, out LiveOrderOwnership? route) ||
                    !_execIds.TryGetValue(payload.Owner, out HashSet<string>? ids) || !ids.Add(payload.ExecId) ||
                    payload.CumulativeQuantity < 0 || payload.CumulativeQuantity > route.Quantity)
                    throw new LiveOwnershipJournalException("Invalid or duplicate execution id in journal.");
                int effective = Math.Max(_watermarks[payload.Owner], payload.CumulativeQuantity);
                _watermarks[payload.Owner] = effective;
                _routes[payload.Owner] = route with { CumulativeFilledQuantity = effective };
                break;
            }
            case "FillWatermark":
            {
                WatermarkPayload payload = envelope.Payload.Deserialize<WatermarkPayload>() ?? throw new JsonException("Missing fill watermark.");
                if (!_routes.TryGetValue(payload.Owner, out LiveOrderOwnership? route) ||
                    payload.CumulativeQuantity <= _watermarks[payload.Owner] || payload.CumulativeQuantity > route.Quantity)
                    throw new LiveOwnershipJournalException("Invalid cumulative fill watermark in journal.");
                _watermarks[payload.Owner] = payload.CumulativeQuantity;
                _routes[payload.Owner] = route with { CumulativeFilledQuantity = payload.CumulativeQuantity };
                break;
            }
            case "SubmissionUnknown":
            {
                UnknownPayload payload = envelope.Payload.Deserialize<UnknownPayload>() ?? throw new JsonException("Missing unknown submission payload.");
                if (!_routes.TryGetValue(payload.Owner, out LiveOrderOwnership? route))
                    throw new LiveOwnershipJournalException("Unknown owner in submission state event.");
                _routes[payload.Owner] = route with { SubmissionState = StrategySubmissionState.Unknown, Reason = payload.Reason };
                break;
            }
            case "Checkpoint":
            {
                StrategyRecoveryCheckpoint checkpoint = envelope.Payload.Deserialize<StrategyRecoveryCheckpoint>() ?? throw new JsonException("Missing checkpoint.");
                _checkpoints[(checkpoint.StrategyInstanceId, checkpoint.StrategyRunGeneration)] = checkpoint;
                break;
            }
            case "Quarantine":
            {
                QuarantinedBrokerEvent quarantined = envelope.Payload.Deserialize<QuarantinedBrokerEvent>()
                    ?? throw new JsonException("Missing quarantine record.");
                _quarantinedEvents.Add(quarantined);
                break;
            }
            default:
                throw new LiveOwnershipJournalException($"Unknown journal event type: {envelope.EventType}");
        }
    }

    private void Append<T>(string eventType, T payload)
    {
        string payloadJson = JsonSerializer.Serialize(payload);
        using JsonDocument payloadDocument = JsonDocument.Parse(payloadJson);
        JsonElement payloadElement = payloadDocument.RootElement.Clone();
        long sequence = _sequence + 1;
        var envelope = new JournalEnvelope(sequence, eventType, payloadElement, ComputeChecksum(sequence, eventType, payloadElement));
        byte[] line = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope) + "\n");
        using var stream = new FileStream(_journalPath, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
        stream.Write(line);
        stream.Flush(true);
        _sequence = sequence;
    }

    private static string ComputeChecksum(long sequence, string eventType, JsonElement payload)
    {
        byte[] bytes = Encoding.UTF8.GetBytes($"{sequence}\n{eventType}\n{payload.GetRawText()}");
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static void ValidateOwnership(LiveOrderOwnership ownership)
    {
        if (string.IsNullOrWhiteSpace(ownership.Key.StrategyInstanceId) || ownership.Key.StrategyRunGeneration <= 0 ||
            string.IsNullOrWhiteSpace(ownership.Key.ClientOrderId) || string.IsNullOrWhiteSpace(ownership.Account) ||
            string.IsNullOrWhiteSpace(ownership.Symbol) || ownership.Quantity <= 0)
            throw new ArgumentException("Strategy ownership identity, account, symbol and quantity are required.", nameof(ownership));
    }

    private static void ValidateAlias(BrokerOrderAlias alias)
    {
        if (string.IsNullOrWhiteSpace(alias.Account) || string.IsNullOrWhiteSpace(alias.TradingDay) ||
            string.IsNullOrWhiteSpace(alias.Kind) || string.IsNullOrWhiteSpace(alias.Value))
            throw new ArgumentException("Broker alias account, trading day, kind and value are required.", nameof(alias));
    }

    private void ThrowIfUnavailable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_quarantineReason.Length > 0) throw new LiveOwnershipJournalException(_quarantineReason);
    }

    public void Dispose()
    {
        lock (_sync) _disposed = true;
    }
}
