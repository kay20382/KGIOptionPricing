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
using KGIOptionPricing.Strategies.WaveletScalper;
using KGIOptionPricing.StrategyFramework.MarketData;
using KGIOptionPricing.StrategyFramework.Storage;

namespace KGIOptionPricing.StrategyFramework;

public sealed class StrategyRunRecorderSet : IDisposable
{
    private readonly object _sync = new();
    private readonly Dictionary<string, SimulationRunRecorder> _recorders =
        new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public void Add(string instanceId, SimulationRunRecorder recorder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentNullException.ThrowIfNull(recorder);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_recorders.TryAdd(instanceId, recorder))
                throw new InvalidOperationException($"Recorder 已存在：{instanceId}");
        }
    }

    public void Record(StrategySnapshot value) { lock (_sync) if (_recorders.TryGetValue(value.InstanceId, out SimulationRunRecorder? recorder)) recorder.Record(value); }
    public void Record(ExecutionEvent value) { lock (_sync) if (_recorders.TryGetValue(value.StrategyInstanceId, out SimulationRunRecorder? recorder)) recorder.Record(value); }
    public void Record(OrderLifecycleEvent value) { lock (_sync) if (_recorders.TryGetValue(value.StrategyInstanceId, out SimulationRunRecorder? recorder)) recorder.Record(value); }
    public void Record(StrategyDecisionObservation value) { lock (_sync) if (_recorders.TryGetValue(value.StrategyInstanceId, out SimulationRunRecorder? recorder)) recorder.Record(value); }

    public void RemoveAndFinalize(string instanceId, DateTimeOffset endedAt) =>
        RemoveCore(instanceId, endedAt, deleteDirectory: false);

    public void Discard(string instanceId, bool deleteDirectory) =>
        RemoveCore(instanceId, null, deleteDirectory);

    private void RemoveCore(string instanceId, DateTimeOffset? endedAt, bool deleteDirectory)
    {
        SimulationRunRecorder? recorder;
        lock (_sync)
        {
            if (!_recorders.Remove(instanceId, out recorder)) return;
        }
        List<Exception>? errors = null;
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
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            entries = _recorders.ToArray();
            _recorders.Clear();
        }
        List<Exception>? errors = null;
        foreach (KeyValuePair<string, SimulationRunRecorder> entry in entries)
        {
            try { entry.Value.FinalizeSummary(DateTimeOffset.UtcNow); } catch (Exception exception) { (errors ??= new()).Add(exception); }
            try { entry.Value.Dispose(); } catch (Exception exception) { (errors ??= new()).Add(exception); }
        }
        if (errors is { Count: > 0 }) throw new AggregateException("一或多個 recorder 無法完整釋放。", errors);
    }
}

public sealed class StrategySimulationService : IDisposable
{
    private readonly StrategyRegistry _registry = new();
    private readonly StrategyProductResolver _resolver = new();
    private readonly StrategyHost _liveHost;
    private StrategyHeartbeatTimer _heartbeat;
    private readonly object _disposeSync = new();
    private DisposeAttempt? _disposeAttempt;
    private readonly StrategyRunRecorderSet _liveRecorders = new();
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
        _liveHost.SnapshotChanged += OnSnapshot;
        _liveHost.ExecutionOccurred += OnExecution;
        _liveHost.OrderLifecycleOccurred += _liveRecorders.Record;
        _liveHost.DecisionObserved += _liveRecorders.Record;
        _heartbeat = new StrategyHeartbeatTimer(_liveHost.Pulse);
    }

    public StrategyInstanceRunner AddLive(StrategyDefinition definition, decimal commissionPerContract)
    {
        EnsureUnique(definition.InstanceId, _liveHost.GetSnapshots());
        ResolvedStrategyUniverse universe = _resolver.Resolve(definition.Universe);
        ISimulationStrategy strategy = _registry.Get(definition.StrategyType).Create(definition);
        string runId = $"live-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        string output = Path.Combine(_root, "StrategyRuns", runId);
        var recorder = new SimulationRunRecorder(output);
        _liveRecorders.Add(definition.InstanceId, recorder);
        try
        {
            StrategyInstanceRunner runner = _liveHost.Add(strategy, universe, commissionPerContract);
            recorder.RecordManifest(CreateManifest(runId, StrategyRunMode.LiveShadow,
                definition, EffectiveParameters(runner.GetSnapshot(), definition), universe,
                commissionPerContract, LiveArchivePath));
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
                try { _liveRecorders.Discard(definition.InstanceId, deleteDirectory: true); }
                catch (Exception recorderError) { errors.Add(recorderError); }
            }
            if (errors.Count == 1) ExceptionDispatchInfo.Capture(addError).Throw();
            throw new AggregateException("策略新增失敗，cleanup 亦發生錯誤。", errors);
        }
    }

    public bool Stop(string instanceId) => _liveHost.Stop(instanceId);
    public bool Pause(string instanceId) => _liveHost.Pause(instanceId);
    public bool Resume(string instanceId) => _liveHost.Resume(instanceId);
    public bool Remove(string instanceId)
    {
        if (!_liveHost.Remove(instanceId)) return false;
        _liveRecorders.RemoveAndFinalize(instanceId, DateTimeOffset.UtcNow);
        return true;
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
        StrategyInstanceRunner runner = host.Add(
            _registry.Get(definition.StrategyType).Create(definition), universe, commissionPerContract);
        recorder.RecordManifest(CreateManifest(runId, StrategyRunMode.HistoricalReplay,
            definition, EffectiveParameters(runner.GetSnapshot(), definition), universe,
            commissionPerContract, eventFilePath));
        await source.ReplayAsync(speed, cancellationToken);
        host.StopAll("歷史重播完成");
        recorder.FinalizeSummary(DateTimeOffset.UtcNow);
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

    private static void EnsureUnique(string instanceId, IReadOnlyList<StrategySnapshot> snapshots)
    {
        if (snapshots.Any(x => string.Equals(x.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"策略實例識別碼已存在：{instanceId}");
    }

    private static RunManifest CreateManifest(
        string runId,
        StrategyRunMode mode,
        StrategyDefinition definition,
        IReadOnlyDictionary<string, string> effectiveParameters,
        ResolvedStrategyUniverse universe,
        decimal commissionPerContract,
        string inputPath)
    {
        var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> parameter in effectiveParameters)
            parameters[parameter.Key] = parameter.Value;
        string normalized = JsonSerializer.Serialize(parameters);
        string parameterHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        string absoluteInput = Path.GetFullPath(inputPath);
        var missing = new List<string>();
        InputArchiveIdentity input = BuildInputIdentity(absoluteInput);
        missing.AddRange(input.MissingFields);

        return new RunManifest
        {
            RunId = runId,
            StrategyType = definition.StrategyType,
            InstanceId = definition.InstanceId,
            CodeVersion = typeof(StrategySimulationService).Assembly.GetName().Version?.ToString(),
            FrameworkVersion = typeof(StrategyHost).Assembly.GetName().Version?.ToString() ?? string.Empty,
            Mode = mode,
            StartedAt = DateTimeOffset.UtcNow,
            EffectiveParameters = parameters,
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

    private static IReadOnlyDictionary<string, string> EffectiveParameters(
        StrategySnapshot snapshot,
        StrategyDefinition definition)
    {
        var effective = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (string item in snapshot.ParameterSummary.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = item.IndexOf('=');
            if (separator > 0) effective[item[..separator]] = item[(separator + 1)..];
        }
        if (effective.Count == 0)
            foreach (KeyValuePair<string, string> parameter in definition.Parameters)
                effective[parameter.Key] = parameter.Value;
        return effective;
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
            else { attempt = _disposeAttempt = new DisposeAttempt(Environment.CurrentManagedThreadId); owner = true; }
        }
        if (!owner) { attempt.Completed.Wait(); if (attempt.Error != null) ExceptionDispatchInfo.Capture(attempt.Error).Throw(); return; }
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
