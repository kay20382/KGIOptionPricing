using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.ExceptionServices;
using KGIOptionPricing.Strategies.MeanReversionScalper;
using KGIOptionPricing.Strategies.NikkeiMicrostructureAlpha;
using KGIOptionPricing.Strategies.OrderBookTrend;
using KGIOptionPricing.Strategies.PeriodicQueueCycle;
using KGIOptionPricing.Strategies.WaveletScalper;
using KGIOptionPricing.StrategyFramework.Execution;
using KGIOptionPricing.StrategyFramework.MarketData;
using KGIOptionPricing.StrategyFramework.Storage;

namespace KGIOptionPricing.StrategyFramework;

public static class InputArchiveBoundaryCapture
{
    public static InputArchiveBoundary Begin(ArchiveWriterSnapshot snapshot, DateTimeOffset startedAt)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new InputArchiveBoundary
        {
            AbsolutePath = Path.GetFullPath(snapshot.EventFilePath),
            AppendOnly = true,
            StartedAt = startedAt,
            Files = new[]
            {
                new InputArchiveBoundaryFile(Path.GetFileName(snapshot.EventFilePath), snapshot.Length,
                    snapshot.LastWriteTimeUtc, snapshot.Sha256)
            }
        };
    }

    public static InputArchiveIdentity Complete(
        InputArchiveBoundary boundary,
        ArchiveWriterSnapshot endSnapshot,
        DateTimeOffset endedAt)
    {
        ArgumentNullException.ThrowIfNull(boundary);
        ArgumentNullException.ThrowIfNull(endSnapshot);
        try
        {
            InputArchiveBoundaryFile? start = boundary.Files.SingleOrDefault();
            if (start == null || !string.Equals(Path.GetFullPath(endSnapshot.EventFilePath), boundary.AbsolutePath,
                    StringComparison.OrdinalIgnoreCase) || endSnapshot.Length < start.Length)
                return MutatedIdentity(boundary.AbsolutePath, "ArchiveBoundaryMismatch");
            if (start.Length > 0)
            {
                string prefix = HashRawRange(boundary.AbsolutePath, 0, start.Length);
                if (!string.Equals(prefix, start.Sha256, StringComparison.Ordinal))
                    return MutatedIdentity(boundary.AbsolutePath, "ArchivePrefixMutated");
            }
            if (endSnapshot.Length > 0 && !string.Equals(
                    HashRawRange(boundary.AbsolutePath, 0, endSnapshot.Length),
                    endSnapshot.Sha256, StringComparison.Ordinal))
                return MutatedIdentity(boundary.AbsolutePath, "ArchiveEndBoundaryMutated");
            long size = endSnapshot.Length - start.Length;
            if (size == 0)
                return new InputArchiveIdentity
                {
                    AbsolutePath = boundary.AbsolutePath, SizeBytes = 0, Sha256 = null,
                    MissingFields = new[] { "ArchiveFiles", "EmptyArchive", "FirstEventAt", "LastEventAt" }
                };
            var range = new InputArchiveFileRange(start.RelativePath, start.Length, endSnapshot.Length,
                endSnapshot.LastWriteTimeUtc);
            return new InputArchiveIdentity
            {
                AbsolutePath = boundary.AbsolutePath, SizeBytes = size,
                LastWriteTimeUtc = endSnapshot.LastWriteTimeUtc,
                Sha256 = HashRanges(boundary.AbsolutePath, new[] { range }), Files = new[] { range },
                MissingFields = new[] { "FirstEventAt", "LastEventAt" }
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return MutatedIdentity(boundary.AbsolutePath, "ArchiveReadRace");
        }
    }

    private static InputArchiveIdentity MutatedIdentity(string path, string missing) => new()
    {
        AbsolutePath = path, Sha256 = null, MutationReasonCode = "InputMutatedDuringRun",
        MissingFields = new[] { missing, "FirstEventAt", "LastEventAt" }
    };

    public static InputArchiveBoundary Begin(string path, bool appendOnly, DateTimeOffset startedAt)
    {
        string absolute = Path.GetFullPath(path);
        return new InputArchiveBoundary
        {
            AbsolutePath = absolute,
            AppendOnly = appendOnly,
            StartedAt = startedAt,
            Files = Enumerate(absolute).Select(item => new InputArchiveBoundaryFile(
                item.RelativePath, item.Length, item.LastWriteTimeUtc,
                HashRanges(absolute, new[]
                {
                    new InputArchiveFileRange(item.RelativePath, 0, item.Length, item.LastWriteTimeUtc)
                }))).ToArray()
        };
    }

    public static InputArchiveIdentity Complete(InputArchiveBoundary boundary, DateTimeOffset endedAt)
    {
        ArgumentNullException.ThrowIfNull(boundary);
        try
        {
            Dictionary<string, InputArchiveBoundaryFile> initial = boundary.Files.ToDictionary(
                file => file.RelativePath, StringComparer.Ordinal);
            (string RelativePath, long Length, DateTimeOffset LastWriteTimeUtc)[] current =
                Enumerate(boundary.AbsolutePath).ToArray();
            var mutation = new List<string>();
            var ranges = new List<InputArchiveFileRange>();
            if (boundary.AppendOnly)
            {
                foreach (var file in current)
                {
                    long start = initial.TryGetValue(file.RelativePath, out InputArchiveBoundaryFile? before)
                        ? before.Length : 0L;
                    if (file.Length < start) mutation.Add(file.RelativePath);
                    else if (before != null && before.Length > 0)
                    {
                        string prefixHash = HashRanges(boundary.AbsolutePath, new[]
                        {
                            new InputArchiveFileRange(file.RelativePath, 0, before.Length, file.LastWriteTimeUtc)
                        });
                        if (!string.Equals(prefixHash, before.Sha256, StringComparison.Ordinal))
                            mutation.Add(file.RelativePath);
                    }
                    ranges.Add(new InputArchiveFileRange(file.RelativePath, Math.Min(start, file.Length),
                        file.Length, file.LastWriteTimeUtc));
                }
                foreach (string removed in initial.Keys.Except(current.Select(file => file.RelativePath), StringComparer.Ordinal))
                    mutation.Add(removed);
            }
            else
            {
                foreach (var file in current)
                {
                    if (!initial.TryGetValue(file.RelativePath, out InputArchiveBoundaryFile? before) ||
                        before.Length != file.Length)
                        mutation.Add(file.RelativePath);
                    ranges.Add(new InputArchiveFileRange(file.RelativePath, 0, file.Length, file.LastWriteTimeUtc));
                }
                foreach (string removed in initial.Keys.Except(current.Select(file => file.RelativePath), StringComparer.Ordinal))
                    mutation.Add(removed);
            }

            long capturedSize = ranges.Sum(range => Math.Max(0L, range.EndOffset - range.StartOffset));
            string? hash = capturedSize > 0 ? HashRanges(boundary.AbsolutePath, ranges) : null;
            if (!boundary.AppendOnly && mutation.Count == 0)
            {
                foreach (InputArchiveBoundaryFile before in boundary.Files)
                {
                    InputArchiveFileRange range = ranges.Single(item => item.RelativePath == before.RelativePath);
                    string currentFileHash = HashRanges(boundary.AbsolutePath, new[] { range });
                    if (!string.Equals(currentFileHash, before.Sha256, StringComparison.Ordinal))
                        mutation.Add(before.RelativePath);
                }
            }
            var missing = new List<string> { "FirstEventAt", "LastEventAt" };
            if (current.Length == 0) missing.Add("ArchiveFiles");
            if (capturedSize == 0) missing.Add("EmptyArchive");
            return new InputArchiveIdentity
            {
                AbsolutePath = boundary.AbsolutePath,
                SizeBytes = capturedSize,
                LastWriteTimeUtc = ranges.Count == 0 ? null : ranges.Max(range => range.LastWriteTimeUtc),
                Sha256 = mutation.Count == 0 ? hash : null,
                Files = ranges,
                MutationReasonCode = mutation.Count == 0 ? null : "InputMutatedDuringRun",
                MissingFields = missing
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new InputArchiveIdentity
            {
                AbsolutePath = boundary.AbsolutePath,
                Sha256 = null,
                MutationReasonCode = "InputMutatedDuringRun",
                MissingFields = new[] { "ArchiveReadRace", "FirstEventAt", "LastEventAt" }
            };
        }
    }

    public static bool Verify(InputArchiveIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (string.IsNullOrWhiteSpace(identity.Sha256) || identity.Files.Count == 0) return false;
        try
        {
            return string.Equals(HashRanges(identity.AbsolutePath, identity.Files), identity.Sha256,
                StringComparison.Ordinal);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static IEnumerable<(string RelativePath, long Length, DateTimeOffset LastWriteTimeUtc)> Enumerate(
        string absolutePath)
    {
        if (File.Exists(absolutePath))
        {
            var info = new FileInfo(absolutePath);
            yield return (info.Name, info.Length, info.LastWriteTimeUtc);
            yield break;
        }
        if (!Directory.Exists(absolutePath)) yield break;
        foreach (string file in Directory.GetFiles(absolutePath, "*", SearchOption.AllDirectories)
                     .OrderBy(item => Path.GetRelativePath(absolutePath, item), StringComparer.Ordinal))
        {
            var info = new FileInfo(file);
            yield return (Path.GetRelativePath(absolutePath, file).Replace('\\', '/'),
                info.Length, info.LastWriteTimeUtc);
        }
    }

    private static string HashRanges(string absolutePath, IEnumerable<InputArchiveFileRange> ranges)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (InputArchiveFileRange range in ranges.OrderBy(item => item.RelativePath, StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes($"{range.RelativePath}\n{range.StartOffset}\n{range.EndOffset}\n"));
            string file = File.Exists(absolutePath)
                ? absolutePath
                : Path.Combine(absolutePath, range.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            stream.Position = range.StartOffset;
            long remaining = range.EndOffset - range.StartOffset;
            byte[] buffer = new byte[81920];
            while (remaining > 0)
            {
                int read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                if (read == 0) throw new EndOfStreamException($"Archive range truncated: {range.RelativePath}");
                hash.AppendData(buffer, 0, read);
                remaining -= read;
            }
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static string HashRawRange(string path, long start, long end)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        stream.Position = start;
        long remaining = end - start;
        byte[] buffer = new byte[81920];
        while (remaining > 0)
        {
            int read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (read == 0) throw new EndOfStreamException("Archive prefix truncated.");
            hash.AppendData(buffer, 0, read);
            remaining -= read;
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}

public sealed class StrategyRunRecorderSet : IDisposable
{
    private readonly object _sync = new();
    private readonly Dictionary<string, SimulationRunRecorder> _recorders =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Func<DateTimeOffset, RunManifest>> _manifestFinalizers =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _reservations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<Action<SimulationRunRecorder>>> _pendingRecords =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _failedInstances = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<StrategyObservationDiagnostic>> _emergencyDiagnostics =
        new(StringComparer.OrdinalIgnoreCase);
    private bool _disposing;
    private bool _disposed;

    public event Action<string, Exception>? FatalObservationFailure;

    public void Add(string instanceId, SimulationRunRecorder recorder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentNullException.ThrowIfNull(recorder);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_reservations.Contains(instanceId) || !_recorders.TryAdd(instanceId, recorder))
                throw new InvalidOperationException($"Recorder 已存在：{instanceId}");
        }
    }

    public bool TryReserve(string instanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_recorders.ContainsKey(instanceId) || !_reservations.Add(instanceId)) return false;
            _pendingRecords[instanceId] = new List<Action<SimulationRunRecorder>>();
            return true;
        }
    }

    public void CommitReservation(string instanceId, SimulationRunRecorder recorder,
        Func<DateTimeOffset, RunManifest>? manifestFinalizer = null)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_reservations.Remove(instanceId))
                throw new InvalidOperationException($"Recorder reservation 不存在：{instanceId}");
            if (!_recorders.TryAdd(instanceId, recorder))
                throw new InvalidOperationException($"Recorder 已存在：{instanceId}");
            try
            {
                if (manifestFinalizer != null) _manifestFinalizers[instanceId] = manifestFinalizer;
                if (_pendingRecords.Remove(instanceId, out List<Action<SimulationRunRecorder>>? pending))
                    foreach (Action<SimulationRunRecorder> record in pending) record(recorder);
            }
            catch
            {
                _recorders.Remove(instanceId);
                _manifestFinalizers.Remove(instanceId);
                _pendingRecords.Remove(instanceId);
                throw;
            }
        }
    }

    public void ReleaseReservation(string instanceId)
    {
        lock (_sync)
        {
            _reservations.Remove(instanceId);
            _pendingRecords.Remove(instanceId);
        }
    }

    public void Record(StrategySnapshot value) => Route(value.InstanceId, "Snapshot", recorder => recorder.Record(value));
    public void Record(ExecutionEvent value) => Route(value.StrategyInstanceId, "Execution", recorder => recorder.Record(value));
    public void Record(OrderLifecycleEvent value) => Route(value.StrategyInstanceId, "OrderLifecycle", recorder => recorder.Record(value));
    public void Record(StrategyDecisionObservation value) => Route(value.StrategyInstanceId, "Decision", recorder => recorder.Record(value));

    public IReadOnlyList<StrategyObservationDiagnostic> GetPendingDiagnostics(string instanceId)
    {
        lock (_sync)
            return _emergencyDiagnostics.TryGetValue(instanceId, out List<StrategyObservationDiagnostic>? items)
                ? items.ToArray() : Array.Empty<StrategyObservationDiagnostic>();
    }

    private void Route(string instanceId, string kind, Action<SimulationRunRecorder> write)
    {
        Exception? failure = null;
        lock (_sync)
        {
            if (_failedInstances.Contains(instanceId))
            {
                AddEmergencyDiagnostic(instanceId, kind, "RecorderUnavailable", "Observation retained in memory");
                return;
            }
            if (_recorders.TryGetValue(instanceId, out SimulationRunRecorder? recorder))
            {
                try { write(recorder); }
                catch (Exception exception)
                {
                    _failedInstances.Add(instanceId);
                    AddEmergencyDiagnostic(instanceId, kind, "FatalObservationFailure", exception.Message);
                    failure = exception;
                }
            }
            else if (_pendingRecords.TryGetValue(instanceId, out List<Action<SimulationRunRecorder>>? pending))
                pending.Add(write);
        }
        if (failure != null)
        {
            FatalObservationFailure?.Invoke(instanceId, failure);
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private void AddEmergencyDiagnostic(string instanceId, string kind, string reasonCode, string detail)
    {
        if (!_emergencyDiagnostics.TryGetValue(instanceId, out List<StrategyObservationDiagnostic>? items))
            _emergencyDiagnostics[instanceId] = items = new List<StrategyObservationDiagnostic>();
        items.Add(new StrategyObservationDiagnostic(DateTimeOffset.UtcNow, kind, reasonCode, detail));
    }

    public void RemoveAndFinalize(string instanceId, DateTimeOffset endedAt) =>
        RemoveCore(instanceId, endedAt, deleteDirectory: false);

    public void Discard(string instanceId, bool deleteDirectory) =>
        RemoveCore(instanceId, null, deleteDirectory);

    private void RemoveCore(string instanceId, DateTimeOffset? endedAt, bool deleteDirectory)
    {
        SimulationRunRecorder? recorder;
        Func<DateTimeOffset, RunManifest>? manifestFinalizer;
        lock (_sync)
        {
            if (!_recorders.TryGetValue(instanceId, out recorder)) return;
            _manifestFinalizers.TryGetValue(instanceId, out manifestFinalizer);
        }
        List<Exception>? errors = null;
        if (endedAt.HasValue && manifestFinalizer != null && !recorder.ManifestFinalized)
            try { recorder.FinalizeManifest(manifestFinalizer(endedAt.Value)); }
            catch (Exception exception) { (errors ??= new()).Add(exception); }
        if (endedAt.HasValue && errors is null && !recorder.SummaryFinalized)
            try { recorder.FinalizeSummary(endedAt.Value); } catch (Exception exception) { (errors ??= new()).Add(exception); }
        if (errors is null && !recorder.IsDisposed)
            try { recorder.Dispose(); } catch (Exception exception) { (errors ??= new()).Add(exception); }
        if (deleteDirectory && errors is null)
            try { if (Directory.Exists(recorder.DirectoryPath)) Directory.Delete(recorder.DirectoryPath, true); }
            catch (Exception exception) { (errors ??= new()).Add(exception); }
        if (errors is { Count: > 0 }) throw new AggregateException("Recorder finalize/dispose 失敗。", errors);
        lock (_sync)
        {
            if (_recorders.TryGetValue(instanceId, out SimulationRunRecorder? current) && ReferenceEquals(current, recorder))
            {
                _recorders.Remove(instanceId);
                _manifestFinalizers.Remove(instanceId);
                _failedInstances.Remove(instanceId);
                _emergencyDiagnostics.Remove(instanceId);
            }
        }
    }

    public void Dispose()
    {
        string[] instanceIds;
        lock (_sync)
        {
            if (_disposed) return;
            if (_disposing) throw new InvalidOperationException("Recorder set 正在釋放。");
            _disposing = true;
            instanceIds = _recorders.Keys.ToArray();
        }
        List<Exception>? errors = null;
        foreach (string instanceId in instanceIds)
        {
            try { RemoveAndFinalize(instanceId, DateTimeOffset.UtcNow); }
            catch (Exception exception) { (errors ??= new()).Add(exception); }
        }
        lock (_sync)
        {
            _disposing = false;
            if (errors is null)
            {
                _disposed = true;
                _reservations.Clear();
                _pendingRecords.Clear();
            }
        }
        if (errors is { Count: > 0 }) throw new AggregateException("一或多個 recorder 無法完整釋放。", errors);
    }
}

public sealed class StrategyInstanceOperationGate : IDisposable
{
    private readonly object _sync = new();
    private readonly Dictionary<string, (int OwnerThreadId, int Depth)> _active =
        new(StringComparer.OrdinalIgnoreCase);
    private bool _closing;
    private bool _disposed;

    public IDisposable Enter(string instanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        lock (_sync)
        {
            int threadId = Environment.CurrentManagedThreadId;
            if (_active.TryGetValue(instanceId, out var owned) && owned.OwnerThreadId == threadId)
            {
                _active[instanceId] = (threadId, owned.Depth + 1);
                return new Lease(this, instanceId);
            }
            while ((_closing || _active.ContainsKey(instanceId)) && !_disposed) Monitor.Wait(_sync);
            ObjectDisposedException.ThrowIf(_disposed, this);
            _active.Add(instanceId, (threadId, 1));
            return new Lease(this, instanceId);
        }
    }

    public void BeginDispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            int threadId = Environment.CurrentManagedThreadId;
            if (_active.Values.Any(owner => owner.OwnerThreadId == threadId))
                throw new InvalidOperationException("Operation lease owner 不可同步 Dispose 自己的 service。");
            _closing = true;
            Monitor.PulseAll(_sync);
            while (_active.Count > 0) Monitor.Wait(_sync);
        }
    }

    public void CompleteDispose()
    {
        lock (_sync)
        {
            _disposed = true;
            _closing = false;
            Monitor.PulseAll(_sync);
        }
    }

    public void ReopenAfterFailedDispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _closing = false;
            Monitor.PulseAll(_sync);
        }
    }

    public void Dispose()
    {
        BeginDispose();
        CompleteDispose();
    }

    private void Exit(string instanceId)
    {
        lock (_sync)
        {
            if (_active.TryGetValue(instanceId, out var owned))
            {
                if (owned.Depth <= 1) _active.Remove(instanceId);
                else _active[instanceId] = (owned.OwnerThreadId, owned.Depth - 1);
            }
            Monitor.PulseAll(_sync);
        }
    }

    private sealed class Lease : IDisposable
    {
        private StrategyInstanceOperationGate? _owner;
        private readonly string _instanceId;
        internal Lease(StrategyInstanceOperationGate owner, string instanceId) =>
            (_owner, _instanceId) = (owner, instanceId);
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Exit(_instanceId);
    }
}

public sealed class StrategySimulationService : IDisposable
{
    private sealed record ParameterResolution(
        IReadOnlyDictionary<string, string> Raw,
        IReadOnlyDictionary<string, string> Effective,
        string? UnavailableReason);
    private readonly StrategyRegistry _registry = new();
    private readonly StrategyProductResolver _resolver = new();
    private readonly StrategyHost _liveHost;
    private readonly MarketDataArchiveWriter _liveArchive;
    private ArchiveWriterSnapshot? _shutdownArchiveSnapshot;
    private StrategyHeartbeatTimer _heartbeat;
    private readonly object _disposeSync = new();
    private DisposeAttempt? _disposeAttempt;
    private readonly StrategyRunRecorderSet _liveRecorders = new();
    private readonly StrategyInstanceOperationGate _operationGate = new();
    private readonly string _root;
    private bool _disposed;

    public event Action<StrategySnapshot>? SnapshotChanged;
    public event Action<ExecutionEvent>? ExecutionOccurred;
    public string LiveArchivePath { get; }
    public IReadOnlyList<IStrategyFactory> Factories => _registry.Factories;
    public static IReadOnlyList<IStrategyFactory> DefaultFactories => CreateDefaultFactories();

    internal static IReadOnlyList<IStrategyFactory> CreateDefaultFactories() => new IStrategyFactory[]
    {
        new MeanReversionScalperFactory(),
        new PeriodicQueueCycleFactory(),
        new Strategies.TradeFlowTrend.TradeFlowTrendFactory(),
        new Strategies.BuySellPower.BuySellPowerFactory(),
        new Strategies.PowerReversal.PowerReversalFactory(),
        new Strategies.MultiFactorMeanR17.MultiFactorMeanR17Factory(),
        new Strategies.NikkeiRlDirection.NikkeiRlFactory()
    };

    public StrategySimulationService(KGIQuoteService quote, string rootDirectory)
    {
        _root = Path.GetFullPath(rootDirectory);
        string archiveRunId = $"live-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        _liveArchive = new MarketDataArchiveWriter(Path.Combine(_root, "MarketDataArchive"), archiveRunId);
        var source = new RecordingMarketEventSource(new KgiStrategyMarketDataSource(quote), _liveArchive);
        LiveArchivePath = _liveArchive.DirectoryPath;
        _liveHost = new StrategyHost(source);
        foreach (IStrategyFactory factory in CreateDefaultFactories()) _registry.Register(factory);
        _liveHost.SnapshotChanged += OnSnapshot;
        _liveHost.ExecutionOccurred += OnExecution;
        _liveHost.OrderLifecycleOccurred += _liveRecorders.Record;
        _liveHost.DecisionObserved += _liveRecorders.Record;
        _liveRecorders.FatalObservationFailure += OnFatalObservationFailure;
        _heartbeat = new StrategyHeartbeatTimer(_liveHost.Pulse);
    }

    public StrategyInstanceRunner AddLive(StrategyDefinition definition, decimal commissionPerContract)
    {
        Strategies.PowerReversal.PowerReversalParameters.EnsureCommission(definition,commissionPerContract);
        Strategies.MultiFactorMeanR17.R17Parameters.EnsureCommission(definition,commissionPerContract);
        using IDisposable operation = _operationGate.Enter(definition.InstanceId);
        if (!_liveRecorders.TryReserve(definition.InstanceId))
            throw new InvalidOperationException($"策略實例識別碼已存在：{definition.InstanceId}");
        SimulationRunRecorder? recorder = null;
        string? output = null;
        try
        {
            ResolvedStrategyUniverse universe = _resolver.Resolve(definition.Universe);
            ISimulationStrategy strategy = _registry.Get(definition.StrategyType).Create(definition);
            DateTimeOffset startedAt = DateTimeOffset.UtcNow;
            InputArchiveBoundary inputBoundary = InputArchiveBoundaryCapture.Begin(
                _liveArchive.CaptureBarrier(), startedAt);
            string runId = $"live-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
            bool rl = definition.StrategyType == Strategies.NikkeiRlDirection.NikkeiRlFactory.TypeName;
            if (rl && commissionPerContract != 36) throw new ArgumentException("凍結日經RL費用須為每邊36JPY");
            output = Path.Combine(rl ? Strategies.NikkeiRlDirection.NikkeiRlParameters.SummaryRoot : _root, "StrategyRuns", runId);
            recorder = new SimulationRunRecorder(output, summaryOnly: true);
            ParameterResolution parameters = ResolveParameters(strategy.GetSnapshot(), definition);
            RunManifest prepared = CreateManifest(runId, StrategyRunMode.LiveShadow,
                definition, parameters, universe, commissionPerContract, LiveArchivePath, startedAt, null,
                inputIdentity: null);
            recorder.PrepareManifest(prepared);
            _liveRecorders.CommitReservation(definition.InstanceId, recorder, endedAt =>
                CreateManifest(runId, StrategyRunMode.LiveShadow, definition, parameters, universe,
                    commissionPerContract, LiveArchivePath, startedAt, endedAt,
                    InputArchiveBoundaryCapture.Complete(inputBoundary, CaptureLiveEndBarrier(), endedAt)));
            recorder = null;
            StrategyInstanceRunner runner = _liveHost.Add(strategy, universe, commissionPerContract);
            _liveRecorders.Record(runner.GetSnapshot());
            return runner;
        }
        catch (Exception addError)
        {
            var errors = new List<Exception> { addError };
            bool retainedRunner = false;
            try { _liveHost.Remove(definition.InstanceId); }
            catch (Exception cleanupError) { errors.Add(cleanupError); }
            try
            {
                retainedRunner = _liveHost.GetSnapshots().Any(snapshot =>
                    string.Equals(snapshot.InstanceId, definition.InstanceId, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ownershipError)
            {
                // If ownership cannot be proven absent, retain the recorder so a
                // fail-closed runner can continue routing late fills/lifecycle.
                retainedRunner = true;
                errors.Add(ownershipError);
            }
            if (!retainedRunner)
            {
                try
                {
                    if (recorder != null)
                    {
                        recorder.Dispose();
                        if (output != null && Directory.Exists(output)) Directory.Delete(output, true);
                    }
                    else _liveRecorders.Discard(definition.InstanceId, deleteDirectory: true);
                }
                catch (Exception recorderError) { errors.Add(recorderError); }
            }
            if (errors.Count == 1) ExceptionDispatchInfo.Capture(addError).Throw();
            throw new AggregateException("策略新增失敗，cleanup 亦發生錯誤。", errors);
        }
        finally { _liveRecorders.ReleaseReservation(definition.InstanceId); }
    }

    public bool Stop(string instanceId) => _liveHost.RequestClose(instanceId,"使用者關閉") || _liveHost.Stop(instanceId);
    public bool Pause(string instanceId) => _liveHost.Pause(instanceId);
    public bool Resume(string instanceId) => _liveHost.Resume(instanceId);
    public bool Remove(string instanceId)
    {
        using IDisposable operation = _operationGate.Enter(instanceId);
        bool removed = false;
        Exception? hostError = null;
        try { removed = _liveHost.Remove(instanceId); }
        catch (Exception exception) { hostError = exception; }
        bool retained = HostContains(instanceId);
        Exception? recorderError = null;
        if (!retained)
        {
            try { _liveRecorders.RemoveAndFinalize(instanceId, DateTimeOffset.UtcNow); }
            catch (Exception exception) { recorderError = exception; }
        }
        if (hostError != null || recorderError != null)
            throw new AggregateException("策略移除不完整。",
                new[] { hostError, recorderError }.Where(error => error != null).Cast<Exception>());
        return removed;
    }
    public void StopAll() => _liveHost.StopAll();
    public IReadOnlyList<StrategySnapshot> GetSnapshots() => _liveHost.GetSnapshots();

    public async Task<string> RunBacktestAsync(
        string eventFilePath,
        StrategyDefinition definition,
        decimal commissionPerContract,
        double speed = 0d,
        CancellationToken cancellationToken = default,
        HistoricalReplayControl? replayControl = null)
    {
        Strategies.PowerReversal.PowerReversalParameters.EnsureCommission(definition,commissionPerContract);
        Strategies.MultiFactorMeanR17.R17Parameters.EnsureCommission(definition,commissionPerContract);
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        InputArchiveBoundary inputBoundary = InputArchiveBoundaryCapture.Begin(
            eventFilePath, appendOnly: false, startedAt);
        ResolvedStrategyUniverse universe = ResolveArchiveUniverse(eventFilePath, definition.Universe);
        string runId = $"backtest-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        bool rl = definition.StrategyType == Strategies.NikkeiRlDirection.NikkeiRlFactory.TypeName;
        if (rl && commissionPerContract != 36) throw new ArgumentException("凍結日經RL費用須為每邊36JPY");
        string output = Path.Combine(rl ? Strategies.NikkeiRlDirection.NikkeiRlParameters.SummaryRoot : _root, "Backtests", runId);
        using var recorder = new SimulationRunRecorder(output, summaryOnly: true);
        using var source = new ArchiveReplayMarketDataSource(eventFilePath);
        var replayClock = new ReplayStrategyClock();
        using var host = new StrategyHost(source, replayClock);
        host.SnapshotChanged += snapshot => { recorder.Record(snapshot); SnapshotChanged?.Invoke(snapshot); };
        host.ExecutionOccurred += execution => { recorder.Record(execution); ExecutionOccurred?.Invoke(execution); };
        host.OrderLifecycleOccurred += recorder.Record;
        host.DecisionObserved += recorder.Record;
        ISimulationStrategy strategy = _registry.Get(definition.StrategyType).Create(definition);
        if (strategy is MeanReversionScalperStrategy)
        {
            MeanReversionSessionSchedule schedule = MeanReversionSessionSchedule.Parse(definition.Parameters);
            DateTimeOffset? previousEventTime = null;
            source.BeforeMarketEvent += eventTime =>
            {
                if (previousEventTime.HasValue)
                    foreach (DateTimeOffset stop in schedule.StopsBetween(previousEventTime.Value, eventTime))
                        host.Pulse(stop);
                previousEventTime = eventTime;
            };
        }
        if (strategy is IStrategyScheduledPulseSource scheduled)
        {
            DateTimeOffset? previous = null;
            source.BeforeMarketEvent += at =>
            {
                if (previous.HasValue && at >= previous.Value)
                    foreach (var pulse in scheduled.GetPulseTimes(previous.Value, at)) host.Pulse(pulse);
                previous = at;
            };
        }
        ParameterResolution parameters = ResolveParameters(strategy.GetSnapshot(), definition);
        recorder.PrepareManifest(CreateManifest(runId, StrategyRunMode.HistoricalReplay,
            definition, parameters, universe, commissionPerContract, eventFilePath, startedAt, null,
            inputIdentity: null));
        StrategyInstanceRunner runner = host.Add(strategy, universe, commissionPerContract,
            strategy is MeanReversionScalperStrategy
                ? new CrossPricePaperExecutionService(commissionPerContract, replayClock, fillMarketOnBook: true)
                : null);
        await source.ReplayAsync(speed, cancellationToken, replayControl);
        host.StopAll("歷史重播完成");
        DateTimeOffset endedAt = DateTimeOffset.UtcNow;
        InputArchiveIdentity finalInput = InputArchiveBoundaryCapture.Complete(inputBoundary, endedAt);
        recorder.FinalizeManifest(CreateManifest(runId, StrategyRunMode.HistoricalReplay,
            definition, parameters, universe, commissionPerContract, eventFilePath, startedAt, endedAt,
            finalInput));
        recorder.FinalizeSummary(endedAt);
        if (finalInput.MutationReasonCode != null)
            throw new InvalidDataException(finalInput.MutationReasonCode);
        return output;
    }

    private void OnSnapshot(StrategySnapshot snapshot)
    {
        _liveRecorders.Record(snapshot);
        SnapshotChanged?.Invoke(snapshot);
    }

    private void OnExecution(ExecutionEvent execution)
    {
        _liveRecorders.Record(execution);
        ExecutionOccurred?.Invoke(execution);
    }

    private void OnFatalObservationFailure(string instanceId, Exception exception)
    {
        _liveHost.MarkObservationFault(instanceId, exception);
    }

    private ArchiveWriterSnapshot CaptureLiveEndBarrier() =>
        _shutdownArchiveSnapshot ?? _liveArchive.CaptureBarrier();

    private bool HostContains(string instanceId)
    {
        try
        {
            return _liveHost.GetSnapshots().Any(snapshot =>
                string.Equals(snapshot.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase));
        }
        catch (ObjectDisposedException) { return false; }
    }

    private static RunManifest CreateManifest(
        string runId,
        StrategyRunMode mode,
        StrategyDefinition definition,
        ParameterResolution parameterResolution,
        ResolvedStrategyUniverse universe,
        decimal commissionPerContract,
        string inputPath,
        DateTimeOffset startedAt,
        DateTimeOffset? endedAt,
        InputArchiveIdentity? inputIdentity)
    {
        var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> parameter in parameterResolution.Effective)
            parameters[parameter.Key] = parameter.Value;
        var raw = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> parameter in parameterResolution.Raw)
            raw[parameter.Key] = parameter.Value;
        string normalized = parameters.Count > 0 ? JsonSerializer.Serialize(parameters) : string.Empty;
        string parameterHash = parameters.Count > 0
            ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))) : string.Empty;
        string absoluteInput = Path.GetFullPath(inputPath);
        var missing = new List<string>();
        InputArchiveIdentity? input = inputIdentity;
        if (input != null) missing.AddRange(input.MissingFields);
        else missing.Add("InputArchiveFinalizedAtRunEnd");
        if (parameterResolution.UnavailableReason != null) missing.Add("EffectiveParameters");

        return new RunManifest
        {
            RunId = runId,
            StrategyType = definition.StrategyType,
            InstanceId = definition.InstanceId,
            CodeVersion = typeof(StrategySimulationService).Assembly.GetName().Version?.ToString(),
            FrameworkVersion = typeof(StrategyHost).Assembly.GetName().Version?.ToString() ?? string.Empty,
            Mode = mode,
            StartedAt = startedAt,
            EndedAt = endedAt,
            RawParameters = raw,
            EffectiveParameters = parameters,
            EffectiveParametersUnavailableReason = parameterResolution.UnavailableReason,
            NormalizedParameters = normalized,
            ParameterSha256 = parameterHash,
            InputArchive = input,
            Universe = universe.AllProducts.Select(product => new RunProductIdentity(
                product.ProductId, product.Symbol, product.ContractMonth,
                product.TickSize, product.ContractMultiplier)).ToArray(),
            CommissionPerContract = commissionPerContract,
            ExecutionAssumptions = new Dictionary<string, string>
            {
                ["fillModel"] = definition.StrategyType == Strategies.TradeFlowTrend.TradeFlowTrendFactory.TypeName
                    ? "ROD strict-cross; IOC strict contra-book, displayed size consumed once, limit fill price; retry waits fresh book"
                    : mode == StrategyRunMode.HistoricalReplay &&
                    definition.StrategyType == MeanReversionScalperFactory.TypeName
                        ? "strict-cross limit/stop; market on next valid contra book or trade"
                        : mode == StrategyRunMode.HistoricalReplay ? "strict-cross" : "paper-strict-cross",
                ["latencyModel"] = definition.StrategyType == Strategies.NikkeiRlDirection.NikkeiRlFactory.TypeName ? "1ms minimum simulated action delay; release on subsequent eligible trade; whole-process latency not guaranteed" : "not-configured",
                ["slippageModel"] = definition.StrategyType == Strategies.NikkeiRlDirection.NikkeiRlFactory.TypeName ? "0 extra ticks; strict-cross limit, no queue model" : "not-configured"
            },
            MissingProvenanceFields = missing.Concat(new[] { "GitCommit", "LatencyModel", "SlippageModel" }).ToArray()
        };
    }

    private static InputArchiveIdentity BuildInputIdentity(string absoluteInput)
    {
        string[] missingRange = { "FirstEventAt", "LastEventAt" };
        if (File.Exists(absoluteInput))
        {
            var info = new FileInfo(absoluteInput);
            using FileStream stream = File.OpenRead(absoluteInput);
            return new InputArchiveIdentity
            {
                AbsolutePath = absoluteInput,
                SizeBytes = info.Length,
                LastWriteTimeUtc = info.LastWriteTimeUtc,
                Sha256 = Convert.ToHexString(SHA256.HashData(stream)),
                MissingFields = missingRange
            };
        }
        if (Directory.Exists(absoluteInput))
        {
            string[] files = Directory.GetFiles(absoluteInput, "*", SearchOption.AllDirectories)
                .OrderBy(path => Path.GetRelativePath(absoluteInput, path), StringComparer.Ordinal).ToArray();
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long size = 0;
            DateTimeOffset? latest = null;
            foreach (string file in files)
            {
                string relative = Path.GetRelativePath(absoluteInput, file).Replace('\\', '/');
                hash.AppendData(Encoding.UTF8.GetBytes(relative + "\n"));
                using FileStream stream = File.OpenRead(file);
                byte[] buffer = new byte[81920];
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0) hash.AppendData(buffer, 0, read);
                var info = new FileInfo(file);
                size += info.Length;
                DateTimeOffset write = info.LastWriteTimeUtc;
                if (!latest.HasValue || write > latest.Value) latest = write;
            }
            var absent = new List<string>(missingRange);
            if (files.Length == 0) absent.Add("ArchiveFiles");
            return new InputArchiveIdentity
            {
                AbsolutePath = absoluteInput,
                SizeBytes = size,
                LastWriteTimeUtc = latest,
                Sha256 = Convert.ToHexString(hash.GetHashAndReset()),
                MissingFields = absent
            };
        }
        return new InputArchiveIdentity
        {
            AbsolutePath = absoluteInput,
            MissingFields = new[] { "SizeBytes", "LastWriteTimeUtc", "Sha256", "FirstEventAt", "LastEventAt" }
        };
    }

    private static ParameterResolution ResolveParameters(
        StrategySnapshot snapshot,
        StrategyDefinition definition)
    {
        var raw = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> parameter in definition.Parameters)
            raw[parameter.Key] = parameter.Value;
        if (definition.StrategyType == Strategies.NikkeiRlDirection.NikkeiRlFactory.TypeName)
            return new ParameterResolution(raw, Strategies.NikkeiRlDirection.NikkeiRlParameters.Parse(definition.Parameters).ToDictionary(), null);
        if (definition.StrategyType == Strategies.TradeFlowTrend.TradeFlowTrendFactory.TypeName)
            return new ParameterResolution(raw, Strategies.TradeFlowTrend.TradeFlowParameters.Parse(definition.Parameters).ToDictionary(), null);
        if (definition.StrategyType == Strategies.BuySellPower.BuySellPowerFactory.TypeName)
            // Indicator-only parameters are per product; trading strategy uses one shared configuration.
        {
            var power = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (string product in Strategies.BuySellPower.BuySellPowerStrategy.Products)
                foreach (var pair in Strategies.BuySellPower.PowerParameters.Parse(definition.Parameters, product).ToDictionary(product + "."))
                    power[pair.Key] = pair.Value;
            return new ParameterResolution(raw, power, null);
        }
        if (definition.StrategyType == Strategies.PowerReversal.PowerReversalFactory.TypeName)
            return new ParameterResolution(raw, Strategies.PowerReversal.PowerReversalParameters.Parse(definition.Parameters).ToDictionary(), null);
        if (definition.StrategyType == Strategies.MultiFactorMeanR17.MultiFactorMeanR17Factory.TypeName)
            return new ParameterResolution(raw, Strategies.MultiFactorMeanR17.R17Parameters.Parse(definition.Parameters).ToDictionary(), null);
        if (!string.Equals(definition.StrategyType, PeriodicQueueCycleFactory.TypeName,
                StringComparison.Ordinal))
            return new ParameterResolution(raw, new Dictionary<string, string>(),
                "TypedEffectiveParameterResolverNotImplemented");
        var effective = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (string item in snapshot.ParameterSummary.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = item.IndexOf('=');
            if (separator > 0) effective[item[..separator]] = item[(separator + 1)..];
        }
        return effective.Count > 0
            ? new ParameterResolution(raw, effective, null)
            : new ParameterResolution(raw, effective, "ResolvedParameterSummaryUnavailable");
    }

    private ResolvedStrategyUniverse ResolveArchiveUniverse(string eventFilePath, StrategyUniverse requested)
    {
        var symbols = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in File.ReadLines(eventFilePath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (MarketDataArchiveCodec.TryDeserialize(line, out MarketEvent marketEvent) && !string.IsNullOrWhiteSpace(marketEvent.ProductId))
                symbols.TryAdd(marketEvent.ProductId, marketEvent.Symbol);
            if (symbols.ContainsKey(requested.Tradable.ProductId) &&
                requested.References.All(x => symbols.ContainsKey(x.ProductId))) break;
        }

        ResolvedProduct Resolve(ProductSelection selection)
        {
            if (!symbols.TryGetValue(selection.ProductId, out string? symbol))
                throw new InvalidDataException($"歷史檔缺少商品 {selection.ProductId}。");
            decimal tick = StrategyProductSpecifications.ResolveTickSize(selection.ProductId);
            decimal multiplier = StrategyProductSpecifications.ResolveContractMultiplier(selection.ProductId);
            ContractInfo? current = SymbolRepository.Instance.GetHotMonthContract(selection.ProductId);
            multiplier = StrategyProductSpecifications.ResolveContractMultiplier(selection.ProductId, current?.ContractValue ?? 0m);
            return new ResolvedProduct
            {
                ProductId = selection.ProductId,
                Symbol = symbol,
                ContractMonth = "ARCHIVE",
                TickSize = tick,
                TickType = current?.TickType ?? PriceTick.getTickType(selection.ProductId),
                ContractMultiplier = multiplier
            };
        }

        return new ResolvedStrategyUniverse
        {
            Tradable = Resolve(requested.Tradable),
            References = requested.References.Select(Resolve).ToArray()
        };
    }

    public void Dispose()
    {
        DisposeAttempt attempt;
        bool owner;
        lock (_disposeSync)
        {
            if (_disposed) return;
            if (_disposeAttempt != null)
            {
                if (_disposeAttempt.OwnerThreadId == Environment.CurrentManagedThreadId) throw new InvalidOperationException("服務不可在自己的 callback 中同步 Dispose。");
                attempt = _disposeAttempt;
                owner = false;
            }
            else
            {
                attempt = _disposeAttempt = new DisposeAttempt(Environment.CurrentManagedThreadId);
                owner = true;
                Monitor.PulseAll(_disposeSync);
            }
        }
        if (!owner) { attempt.Completed.Wait(); if (attempt.Error != null) ExceptionDispatchInfo.Capture(attempt.Error).Throw(); return; }
        bool timerDrained = false;
        bool recordersDisposed = false;
        try
        {
            _operationGate.BeginDispose();
            _shutdownArchiveSnapshot ??= _liveArchive.CaptureBarrier();
            _heartbeat.Dispose();
            timerDrained = true;
            Exception? hostError = null;
            try { _liveHost.Dispose(); }
            catch (Exception exception) { hostError = exception; }

            if (_liveHost.IsDisposed)
            {
                var errors = new List<Exception>();
                if (hostError != null) errors.Add(hostError);
                // Host is terminal: detach every route before finalizing each
                // recorder, and keep cleaning even when one output fails.
                _liveHost.SnapshotChanged -= OnSnapshot;
                _liveHost.ExecutionOccurred -= OnExecution;
                _liveHost.OrderLifecycleOccurred -= _liveRecorders.Record;
                _liveHost.DecisionObserved -= _liveRecorders.Record;
                _liveRecorders.FatalObservationFailure -= OnFatalObservationFailure;
                try { _liveRecorders.Dispose(); recordersDisposed = true; }
                catch (Exception exception) { errors.Add(exception); }
                if (errors.Count > 0) throw new AggregateException("策略服務釋放不完整。", errors);
                _disposed = true;
                _operationGate.CompleteDispose();
            }
            else if (hostError != null)
            {
                // A fail-closed runner is still owned by the host. Keep event
                // routes and recorders alive so late fills remain observable.
                ExceptionDispatchInfo.Capture(hostError).Throw();
            }
            else throw new InvalidOperationException("StrategyHost Dispose 返回後仍未進入已釋放狀態。");
        }
        catch (Exception exception)
        {
            if (timerDrained && !_liveHost.IsDisposed) _heartbeat = new StrategyHeartbeatTimer(_liveHost.Pulse);
            if (!_liveHost.IsDisposed) _shutdownArchiveSnapshot = null;
            if (_liveHost.IsDisposed && recordersDisposed) _disposed = true;
            if (!_disposed) _operationGate.ReopenAfterFailedDispose();
            else _operationGate.CompleteDispose();
            attempt.Error = exception;
            throw;
        }
        finally
        {
            lock (_disposeSync) { if (ReferenceEquals(_disposeAttempt, attempt)) _disposeAttempt = null; }
            attempt.Completed.Set();
        }
    }

    private sealed class DisposeAttempt
    {
        internal DisposeAttempt(int ownerThreadId) => OwnerThreadId = ownerThreadId;
        internal int OwnerThreadId { get; }
        internal ManualResetEventSlim Completed { get; } = new(false);
        internal Exception? Error { get; set; }
    }

    private sealed class ReplayStrategyClock : IEventDrivenStrategyClock
    {
        public DateTimeOffset Now { get; private set; }
        public void Advance(DateTimeOffset timestamp) => Now = timestamp;
    }
}
