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
using KGIOptionPricing.Strategies.MiniLeadMicro;
using KGIOptionPricing.Strategies.NikkeiMicrostructureAlpha;
using KGIOptionPricing.Strategies.OrderBookTrend;
using KGIOptionPricing.Strategies.PeriodicQueueCycle;
using KGIOptionPricing.Strategies.WaveletScalper;
using KGIOptionPricing.StrategyFramework.MarketData;
using KGIOptionPricing.StrategyFramework.Storage;

namespace KGIOptionPricing.StrategyFramework;

public static class InputArchiveBoundaryCapture
{
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

        string hash = HashRanges(boundary.AbsolutePath, ranges);
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
        return new InputArchiveIdentity
        {
            AbsolutePath = boundary.AbsolutePath,
            SizeBytes = ranges.Sum(range => Math.Max(0L, range.EndOffset - range.StartOffset)),
            LastWriteTimeUtc = ranges.Count == 0 ? null : ranges.Max(range => range.LastWriteTimeUtc),
            Sha256 = hash,
            Files = ranges,
            MutationReasonCode = mutation.Count == 0 ? null : "InputMutatedDuringRun",
            MissingFields = new[] { "FirstEventAt", "LastEventAt" }
        };
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

    public void Record(StrategySnapshot value) => Route(value.InstanceId, recorder => recorder.Record(value));
    public void Record(ExecutionEvent value) => Route(value.StrategyInstanceId, recorder => recorder.Record(value));
    public void Record(OrderLifecycleEvent value) => Route(value.StrategyInstanceId, recorder => recorder.Record(value));
    public void Record(StrategyDecisionObservation value) => Route(value.StrategyInstanceId, recorder => recorder.Record(value));

    private void Route(string instanceId, Action<SimulationRunRecorder> write)
    {
        Exception? failure = null;
        lock (_sync)
        {
            if (_failedInstances.Contains(instanceId)) return;
            if (_recorders.TryGetValue(instanceId, out SimulationRunRecorder? recorder))
            {
                try { write(recorder); }
                catch (Exception exception)
                {
                    _failedInstances.Add(instanceId);
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
            if (!_recorders.Remove(instanceId, out recorder)) return;
            _manifestFinalizers.Remove(instanceId, out manifestFinalizer);
            _failedInstances.Remove(instanceId);
        }
        List<Exception>? errors = null;
        if (endedAt.HasValue && manifestFinalizer != null)
            try { recorder.FinalizeManifest(manifestFinalizer(endedAt.Value)); }
            catch (Exception exception) { (errors ??= new()).Add(exception); }
        if (endedAt.HasValue)
            try { recorder.FinalizeSummary(endedAt.Value); } catch (Exception exception) { (errors ??= new()).Add(exception); }
        try { recorder.Dispose(); } catch (Exception exception) { (errors ??= new()).Add(exception); }
        if (deleteDirectory)
            try { if (Directory.Exists(recorder.DirectoryPath)) Directory.Delete(recorder.DirectoryPath, true); }
            catch (Exception exception) { (errors ??= new()).Add(exception); }
        if (errors is { Count: > 0 }) throw new AggregateException("Recorder finalize/dispose 失敗。", errors);
    }

    public void Dispose()
    {
        KeyValuePair<string, SimulationRunRecorder>[] entries;
        Dictionary<string, Func<DateTimeOffset, RunManifest>> finalizers;
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            entries = _recorders.ToArray();
            finalizers = new Dictionary<string, Func<DateTimeOffset, RunManifest>>(_manifestFinalizers,
                StringComparer.OrdinalIgnoreCase);
            _recorders.Clear();
            _manifestFinalizers.Clear();
            _reservations.Clear();
            _pendingRecords.Clear();
            _failedInstances.Clear();
        }
        List<Exception>? errors = null;
        foreach (KeyValuePair<string, SimulationRunRecorder> entry in entries)
        {
            DateTimeOffset endedAt = DateTimeOffset.UtcNow;
            if (finalizers.TryGetValue(entry.Key, out Func<DateTimeOffset, RunManifest>? finalizer))
                try { entry.Value.FinalizeManifest(finalizer(endedAt)); } catch (Exception exception) { (errors ??= new()).Add(exception); }
            try { entry.Value.FinalizeSummary(endedAt); } catch (Exception exception) { (errors ??= new()).Add(exception); }
            try { entry.Value.Dispose(); } catch (Exception exception) { (errors ??= new()).Add(exception); }
        }
        if (errors is { Count: > 0 }) throw new AggregateException("一或多個 recorder 無法完整釋放。", errors);
    }
}

public sealed class StrategyInstanceOperationGate : IDisposable
{
    private readonly object _sync = new();
    private readonly HashSet<string> _active = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public IDisposable Enter(string instanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        lock (_sync)
        {
            while (_active.Contains(instanceId) && !_disposed) Monitor.Wait(_sync);
            ObjectDisposedException.ThrowIf(_disposed, this);
            _active.Add(instanceId);
            return new Lease(this, instanceId);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            Monitor.PulseAll(_sync);
            while (_active.Count > 0) Monitor.Wait(_sync);
        }
    }

    private void Exit(string instanceId)
    {
        lock (_sync)
        {
            _active.Remove(instanceId);
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

    public StrategySimulationService(KGIQuoteService quote, string rootDirectory)
    {
        _root = Path.GetFullPath(rootDirectory);
        string archiveRunId = $"live-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        var archive = new MarketDataArchiveWriter(Path.Combine(_root, "MarketDataArchive"), archiveRunId);
        var source = new RecordingMarketEventSource(new KgiStrategyMarketDataSource(quote), archive);
        LiveArchivePath = archive.DirectoryPath;
        _liveHost = new StrategyHost(source);
        _registry.Register(new MeanReversionScalperFactory());
        _registry.Register(new WaveletScalperFactory());
        _registry.Register(new NikkeiMicrostructureAlphaFactory());
        _registry.Register(new MiniLeadMicroFactory());
        _registry.Register(new OrderBookTrendFactory());
        _registry.Register(new PeriodicQueueCycleFactory());
        _liveHost.SnapshotChanged += OnSnapshot;
        _liveHost.ExecutionOccurred += OnExecution;
        _liveHost.OrderLifecycleOccurred += _liveRecorders.Record;
        _liveHost.DecisionObserved += _liveRecorders.Record;
        _liveRecorders.FatalObservationFailure += OnFatalObservationFailure;
        _heartbeat = new StrategyHeartbeatTimer(_liveHost.Pulse);
    }

    public StrategyInstanceRunner AddLive(StrategyDefinition definition, decimal commissionPerContract)
    {
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
                LiveArchivePath, appendOnly: true, startedAt);
            string runId = $"live-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
            output = Path.Combine(_root, "StrategyRuns", runId);
            recorder = new SimulationRunRecorder(output);
            ParameterResolution parameters = ResolveParameters(strategy.GetSnapshot(), definition);
            RunManifest prepared = CreateManifest(runId, StrategyRunMode.LiveShadow,
                definition, parameters, universe, commissionPerContract, LiveArchivePath, startedAt, null,
                inputIdentity: null);
            recorder.PrepareManifest(prepared);
            _liveRecorders.CommitReservation(definition.InstanceId, recorder, endedAt =>
                CreateManifest(runId, StrategyRunMode.LiveShadow, definition, parameters, universe,
                    commissionPerContract, LiveArchivePath, startedAt, endedAt,
                    InputArchiveBoundaryCapture.Complete(inputBoundary, endedAt)));
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

    public bool Stop(string instanceId) => _liveHost.Stop(instanceId);
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
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        InputArchiveBoundary inputBoundary = InputArchiveBoundaryCapture.Begin(
            eventFilePath, appendOnly: false, startedAt);
        ResolvedStrategyUniverse universe = ResolveArchiveUniverse(eventFilePath, definition.Universe);
        string runId = $"backtest-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        string output = Path.Combine(_root, "Backtests", runId);
        using var recorder = new SimulationRunRecorder(output);
        using var source = new ArchiveReplayMarketDataSource(eventFilePath);
        using var host = new StrategyHost(source, new ReplayStrategyClock());
        host.SnapshotChanged += snapshot => { recorder.Record(snapshot); SnapshotChanged?.Invoke(snapshot); };
        host.ExecutionOccurred += execution => { recorder.Record(execution); ExecutionOccurred?.Invoke(execution); };
        host.OrderLifecycleOccurred += recorder.Record;
        host.DecisionObserved += recorder.Record;
        ISimulationStrategy strategy = _registry.Get(definition.StrategyType).Create(definition);
        ParameterResolution parameters = ResolveParameters(strategy.GetSnapshot(), definition);
        recorder.PrepareManifest(CreateManifest(runId, StrategyRunMode.HistoricalReplay,
            definition, parameters, universe, commissionPerContract, eventFilePath, startedAt, null,
            inputIdentity: null));
        StrategyInstanceRunner runner = host.Add(strategy, universe, commissionPerContract);
        await source.ReplayAsync(speed, cancellationToken);
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
        _liveHost.Stop(instanceId, "FatalObservationFailure");
    }

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
                ["fillModel"] = mode == StrategyRunMode.HistoricalReplay ? "strict-cross" : "paper-strict-cross",
                ["latencyModel"] = "not-configured",
                ["slippageModel"] = "not-configured"
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
        _operationGate.Dispose();
        bool timerDrained = false;
        try
        {
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
                try { _liveRecorders.Dispose(); }
                catch (Exception exception) { errors.Add(exception); }
                _disposed = true;
                if (errors.Count > 0) throw new AggregateException("策略服務釋放不完整。", errors);
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
            if (_liveHost.IsDisposed) _disposed = true;
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
