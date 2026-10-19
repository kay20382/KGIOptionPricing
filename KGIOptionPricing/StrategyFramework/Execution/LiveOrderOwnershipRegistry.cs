using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KGIOptionPricing.StrategyFramework.Execution;

/// <summary>Tracks broker callbacks by immutable strategy owner; optionally journals every change before applying it.</summary>
public sealed class LiveOrderOwnershipRegistry
{
    private sealed record JournalEntry(long Sequence, string Type, JsonElement Payload, string Checksum);
    private sealed record AliasChange(LiveOrderOwnerKey Owner, BrokerOrderAlias Alias);
    private sealed record FillChange(LiveOrderOwnerKey Owner, string? ExecId, int CumulativeQuantity);
    private sealed record UnknownChange(LiveOrderOwnerKey Owner, string Reason);
    private sealed record PriorProcessConfirmation(LiveOrderOwnerKey[] Owners, DateTimeOffset ConfirmedAt);
    public const string PriorProcessReason = "Prior process ownership requires broker reconciliation";
    public bool IsSessionOnly { get; }

    public void ConfirmPriorProcessOrders(string account, string symbol, IReadOnlyList<LiveOrderOwnerKey> owners)
    {
        lock (_sync)
        {
            LiveOrderOwnerKey[] expected = _routes.Values.Where(x =>
                string.Equals(x.Account, account, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.Symbol, symbol, StringComparison.OrdinalIgnoreCase) &&
                x.SubmissionState == StrategySubmissionState.Unknown && !x.PriorProcessReconciled &&
                x.Reason == PriorProcessReason).Select(x => x.Key).ToArray();
            if (owners.Count == 0 || owners.Distinct().Count() != owners.Count ||
                !expected.ToHashSet().SetEquals(owners) || _quarantinedEvents.Any(x =>
                    string.Equals(x.Account, account, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(x.Symbol, symbol, StringComparison.OrdinalIgnoreCase)) ||
                _routes.Values.Any(x => string.Equals(x.Account, account, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(x.Symbol, symbol, StringComparison.OrdinalIgnoreCase) &&
                    x.SubmissionState == StrategySubmissionState.Unknown && !x.PriorProcessReconciled &&
                    x.Reason != PriorProcessReason))
                throw new InvalidOperationException("核對清單已改變，或仍有本次送單不明／隔離回報；不能解除舊程序阻擋。");
            var confirmation = new PriorProcessConfirmation(owners.ToArray(), DateTimeOffset.UtcNow);
            Append("PriorProcessConfirmed", confirmation);
            foreach (LiveOrderOwnerKey owner in confirmation.Owners)
                _routes[owner] = _routes[owner] with { PriorProcessReconciled = true };
        }
    }
    private readonly object _sync = new();
    private readonly string? _journalPath;
    private long _sequence;
    private readonly Dictionary<LiveOrderOwnerKey, LiveOrderOwnership> _routes = new();
    private readonly Dictionary<BrokerOrderAlias, LiveOrderOwnerKey> _aliases = new();
    private readonly Dictionary<LiveOrderOwnerKey, HashSet<string>> _execIds = new();
    private readonly Dictionary<LiveStrategyOcoKey, LiveStrategyOcoGroupState> _ocoGroups = new();
    private readonly Dictionary<RestingPairKey, RestingPairSnapshot> _restingPairs = new();
    private readonly List<QuarantinedBrokerEvent> _quarantinedEvents = new();
    private readonly List<LiveStrategyRetirement> _retirements = new();
    private readonly Dictionary<(string,long),LiveStrategyCheckpoint> _checkpoints = new();
    public void UpdateStrategyCheckpoint(LiveStrategyCheckpoint checkpoint)
    {
        if (string.IsNullOrWhiteSpace(checkpoint.StrategyInstanceId) || checkpoint.StrategyRunGeneration<=0 || checkpoint.SchemaVersion!=1)
            throw new ArgumentException("Checkpoint identity or schema invalid");
        lock (_sync) { Append("StrategyCheckpoint", checkpoint); _checkpoints[(checkpoint.StrategyInstanceId,checkpoint.StrategyRunGeneration)]=checkpoint; }
    }

    public LiveOrderOwnershipRegistry(string? directory = null, bool sessionOnly = false)
    {
        IsSessionOnly = sessionOnly;
        // Explicit desktop policy: ownership is scoped to this process only.
        // Ignore legacy journals without deleting audit files or touching broker orders.
        if (sessionOnly) return;
        if (directory == null) return;
        if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("Ownership journal directory is required.", nameof(directory));
        Directory.CreateDirectory(directory);
        _journalPath = Path.Combine(directory, "ownership.jsonl");
        Replay();
    }

    public LiveOrderOwnership Prepare(LiveOrderOwnership ownership)
    {
        ArgumentNullException.ThrowIfNull(ownership);
        if (string.IsNullOrWhiteSpace(ownership.Key.StrategyInstanceId) ||
            ownership.Key.StrategyRunGeneration <= 0 || string.IsNullOrWhiteSpace(ownership.Key.ClientOrderId) ||
            string.IsNullOrWhiteSpace(ownership.Account) || string.IsNullOrWhiteSpace(ownership.Symbol) ||
            ownership.Quantity <= 0)
            throw new ArgumentException("Strategy ownership identity, account, symbol and quantity are required.", nameof(ownership));
        lock (_sync)
        {
            if (_routes.ContainsKey(ownership.Key))
                throw new InvalidOperationException($"Duplicate strategy owner key: {ownership.Key}");
            Append("Prepare", ownership);
            _routes.Add(ownership.Key, ownership);
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
            if (!_routes.ContainsKey(owner)) throw new InvalidOperationException("Cannot bind alias to an unknown owner.");
            if (_aliases.TryGetValue(alias, out LiveOrderOwnerKey? existing))
            {
                if (existing == owner) return;
                throw new InvalidOperationException($"Broker alias is already bound to another owner: {alias}");
            }
            Append("Alias", new AliasChange(owner, alias));
            _aliases.Add(alias, owner);
        }
    }

    public FillAcceptance TryAcceptFill(LiveOrderOwnerKey owner, string? execId, int cumulativeQuantity)
    {
        ArgumentNullException.ThrowIfNull(owner);
        lock (_sync)
        {
            if (!_routes.TryGetValue(owner, out LiveOrderOwnership? route))
                throw new InvalidOperationException("Cannot accept a fill for an unknown owner.");
            HashSet<string> seen = _execIds[owner];
            if (!string.IsNullOrWhiteSpace(execId) && seen.Contains(execId)) return new(false, 0);
            if (cumulativeQuantity < 0 || cumulativeQuantity > route.Quantity)
                throw new InvalidOperationException("Cumulative fill quantity exceeds the owned order quantity.");
            int accepted = Math.Max(0, cumulativeQuantity - route.CumulativeFilledQuantity);
            if (accepted == 0 && string.IsNullOrWhiteSpace(execId)) return new(false, 0);
            Append("Fill", new FillChange(owner, execId, cumulativeQuantity));
            if (!string.IsNullOrWhiteSpace(execId)) seen.Add(execId);
            _routes[owner] = route with { CumulativeFilledQuantity = Math.Max(route.CumulativeFilledQuantity, cumulativeQuantity) };
            return new(true, accepted);
        }
    }

    public void MarkSubmissionUnknown(LiveOrderOwnerKey owner, string reason)
    {
        lock (_sync)
        {
            if (!_routes.TryGetValue(owner, out LiveOrderOwnership? route))
                throw new InvalidOperationException("Cannot mark an unknown owner submission.");
            Append("Unknown", new UnknownChange(owner, reason));
            _routes[owner] = route with { SubmissionState = StrategySubmissionState.Unknown, Reason = reason, PriorProcessReconciled = false };
        }
    }

    public void UpdateOcoState(LiveStrategyOcoGroupState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        lock (_sync)
        {
            Append("Oco", state);
            _ocoGroups[state.Key] = state;
        }
    }

    public void UpdateRestingPairState(RestingPairSnapshot state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (string.IsNullOrWhiteSpace(state.Key.StrategyInstanceId) ||
            state.Key.StrategyRunGeneration <= 0 || string.IsNullOrWhiteSpace(state.Key.CycleId) ||
            string.IsNullOrWhiteSpace(state.Key.PairId))
            throw new ArgumentException("Paired state has incomplete owner identity.", nameof(state));
        lock (_sync)
        {
            Append("RestingPair", state);
            _restingPairs[state.Key] = state;
        }
    }

    public void AddQuarantinedEvent(QuarantinedBrokerEvent item)
    {
        ArgumentNullException.ThrowIfNull(item);
        lock (_sync)
        {
            Append("Quarantine", item);
            _quarantinedEvents.Add(item);
        }
    }

    public void AppendRetirement(LiveStrategyRetirement retirement)
    {
        ArgumentNullException.ThrowIfNull(retirement);
        if (string.IsNullOrWhiteSpace(retirement.StrategyInstanceId) ||
            retirement.StrategyRunGeneration <= 0 || string.IsNullOrWhiteSpace(retirement.Account) ||
            string.IsNullOrWhiteSpace(retirement.Symbol))
            throw new ArgumentException("Retirement identity, account and symbol are required.", nameof(retirement));
        lock (_sync)
        {
            if (_retirements.Any(x => string.Equals(x.StrategyInstanceId, retirement.StrategyInstanceId, StringComparison.OrdinalIgnoreCase) &&
                                      x.StrategyRunGeneration == retirement.StrategyRunGeneration))
                throw new InvalidOperationException("Strategy generation was already retired.");
            Append("Retire", retirement);
            _retirements.Add(retirement);
        }
    }

    public LiveOwnershipSnapshot Snapshot()
    {
        lock (_sync)
            return new LiveOwnershipSnapshot
            {
                Routes = _routes.Values.ToArray(),
                StrategyCheckpoints = _checkpoints.Values.ToArray(),
                Aliases = new Dictionary<BrokerOrderAlias, LiveOrderOwnerKey>(_aliases),
                OcoGroups = _ocoGroups.Values.ToArray(),
                RestingPairs = _restingPairs.Values.ToArray(),
                QuarantinedEvents = _quarantinedEvents.ToArray(),
                Retirements = _retirements.ToArray()
            };
    }

    private void Append<T>(string type, T payload)
    {
        if (_journalPath == null) return;
        JsonElement data = JsonSerializer.SerializeToElement(payload);
        long sequence = _sequence + 1;
        var entry = new JournalEntry(sequence, type, data, Checksum(sequence, type, data));
        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entry) + "\n");
        using var stream = new FileStream(_journalPath, FileMode.Append, FileAccess.Write, FileShare.Read,
            4096, FileOptions.WriteThrough);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
        _sequence = sequence;
    }

    private void Replay()
    {
        if (_journalPath == null || !File.Exists(_journalPath)) return;
        byte[] bytes = File.ReadAllBytes(_journalPath);
        if (bytes.Length != 0 && bytes[^1] != (byte)'\n')
            throw new InvalidDataException("Ownership journal has an incomplete final record; manual reconciliation required.");
        foreach (string line in Encoding.UTF8.GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            JournalEntry entry = JsonSerializer.Deserialize<JournalEntry>(line)
                ?? throw new InvalidDataException("Ownership journal contains an empty record.");
            if (entry.Sequence != _sequence + 1 ||
                !string.Equals(entry.Checksum, Checksum(entry.Sequence, entry.Type, entry.Payload), StringComparison.Ordinal))
                throw new InvalidDataException("Ownership journal sequence or checksum mismatch; manual reconciliation required.");
            Apply(entry);
            _sequence = entry.Sequence;
        }
        // Broker state is not reconstructed by journal replay alone. Any
        // unretired owner from a prior process blocks new same-product orders
        // until the broker's working orders and fills are reconciled.
        foreach (LiveOrderOwnerKey key in _routes.Keys.ToArray())
        {
            if (_routes[key].PriorProcessReconciled) continue;
            if (_retirements.Any(x => string.Equals(x.StrategyInstanceId, key.StrategyInstanceId, StringComparison.OrdinalIgnoreCase) &&
                                      x.StrategyRunGeneration == key.StrategyRunGeneration)) continue;
            _routes[key] = _routes[key] with
            {
                SubmissionState = StrategySubmissionState.Unknown,
                Reason = PriorProcessReason
            };
        }
    }

    private void Apply(JournalEntry entry)
    {
        switch (entry.Type)
        {
            case "StrategyCheckpoint":
                var checkpoint = entry.Payload.Deserialize<LiveStrategyCheckpoint>()!;
                if(checkpoint.SchemaVersion!=1) throw new InvalidDataException("Checkpoint schema invalid");
                _checkpoints[(checkpoint.StrategyInstanceId,checkpoint.StrategyRunGeneration)]=checkpoint;
                break;
            case "Prepare":
                var route = entry.Payload.Deserialize<LiveOrderOwnership>() ?? throw new InvalidDataException("Missing route.");
                _routes.Add(route.Key, route);
                _execIds.Add(route.Key, new HashSet<string>(StringComparer.Ordinal));
                break;
            case "Alias":
                var alias = entry.Payload.Deserialize<AliasChange>() ?? throw new InvalidDataException("Missing alias.");
                if (!_routes.ContainsKey(alias.Owner)) throw new InvalidDataException("Alias owner missing.");
                _aliases.Add(alias.Alias, alias.Owner);
                break;
            case "Fill":
                var fill = entry.Payload.Deserialize<FillChange>() ?? throw new InvalidDataException("Missing fill.");
                if (!_routes.TryGetValue(fill.Owner, out LiveOrderOwnership? previous)) throw new InvalidDataException("Fill owner missing.");
                if (!string.IsNullOrWhiteSpace(fill.ExecId)) _execIds[fill.Owner].Add(fill.ExecId);
                _routes[fill.Owner] = previous with { CumulativeFilledQuantity = Math.Max(previous.CumulativeFilledQuantity, fill.CumulativeQuantity) };
                break;
            case "Unknown":
                var unknown = entry.Payload.Deserialize<UnknownChange>() ?? throw new InvalidDataException("Missing unknown submission.");
                if (!_routes.TryGetValue(unknown.Owner, out LiveOrderOwnership? submitted)) throw new InvalidDataException("Unknown submission owner missing.");
                _routes[unknown.Owner] = submitted with { SubmissionState = StrategySubmissionState.Unknown, Reason = unknown.Reason, PriorProcessReconciled = false };
                break;
            case "PriorProcessConfirmed":
                var confirmed = entry.Payload.Deserialize<PriorProcessConfirmation>() ?? throw new InvalidDataException("Missing prior-process confirmation.");
                foreach (LiveOrderOwnerKey owner in confirmed.Owners)
                    _routes[owner] = _routes[owner] with { PriorProcessReconciled = true };
                break;
            case "Oco":
                var state = entry.Payload.Deserialize<LiveStrategyOcoGroupState>() ?? throw new InvalidDataException("Missing OCO state.");
                _ocoGroups[state.Key] = state;
                break;
            case "RestingPair":
                var pair = entry.Payload.Deserialize<RestingPairSnapshot>() ?? throw new InvalidDataException("Missing paired state.");
                _restingPairs[pair.Key] = pair;
                break;
            case "Quarantine":
                _quarantinedEvents.Add(entry.Payload.Deserialize<QuarantinedBrokerEvent>() ?? throw new InvalidDataException("Missing quarantine event."));
                break;
            case "Retire":
                _retirements.Add(entry.Payload.Deserialize<LiveStrategyRetirement>() ?? throw new InvalidDataException("Missing retirement."));
                break;
            default:
                throw new InvalidDataException($"Unknown ownership journal event: {entry.Type}");
        }
    }

    private static string Checksum(long sequence, string type, JsonElement payload)
    {
        byte[] bytes = Encoding.UTF8.GetBytes($"{sequence}|{type}|{JsonSerializer.Serialize(payload)}");
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}
