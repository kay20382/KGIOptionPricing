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

public sealed class StrategySimulationService : IDisposable
{
    private readonly StrategyRegistry _registry = new();
    private readonly StrategyProductResolver _resolver = new();
    private readonly StrategyHost _liveHost;
    private StrategyHeartbeatTimer _heartbeat;
    private readonly object _disposeSync = new();
    private DisposeAttempt? _disposeAttempt;
    private readonly SimulationRunRecorder _liveRecorder;
    private readonly string _root;
    private readonly string _liveRunId;
    private bool _liveManifestWritten;
    private bool _disposed;

    public event Action<StrategySnapshot>? SnapshotChanged;
    public event Action<ExecutionEvent>? ExecutionOccurred;
    public string LiveArchivePath { get; }
    public IReadOnlyList<IStrategyFactory> Factories => _registry.Factories;

    public StrategySimulationService(KGIQuoteService quote, string rootDirectory)
    {
        _root = Path.GetFullPath(rootDirectory);
        _liveRunId = $"live-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        var archive = new MarketDataArchiveWriter(Path.Combine(_root, "MarketDataArchive"), _liveRunId);
        var source = new RecordingMarketEventSource(new KgiStrategyMarketDataSource(quote), archive);
        LiveArchivePath = archive.DirectoryPath;
        _liveRecorder = new SimulationRunRecorder(Path.Combine(_root, "StrategyRuns", _liveRunId));
        _liveHost = new StrategyHost(source);
        _registry.Register(new MeanReversionScalperFactory());
        _registry.Register(new WaveletScalperFactory());
        _registry.Register(new NikkeiMicrostructureAlphaFactory());
        _registry.Register(new MiniLeadMicroFactory());
        _registry.Register(new OrderBookTrendFactory());
        _liveHost.SnapshotChanged += OnSnapshot;
        _liveHost.ExecutionOccurred += OnExecution;
        _liveHost.OrderLifecycleOccurred += _liveRecorder.Record;
        _liveHost.DecisionObserved += _liveRecorder.Record;
        _heartbeat = new StrategyHeartbeatTimer(_liveHost.Pulse);
    }

    public StrategyInstanceRunner AddLive(StrategyDefinition definition, decimal commissionPerContract)
    {
        EnsureUnique(definition.InstanceId, _liveHost.GetSnapshots());
        ResolvedStrategyUniverse universe = _resolver.Resolve(definition.Universe);
        lock (_disposeSync)
        {
            if (!_liveManifestWritten)
            {
                _liveRecorder.RecordManifest(CreateManifest(_liveRunId, StrategyRunMode.LiveShadow,
                    definition, universe, commissionPerContract, LiveArchivePath));
                _liveManifestWritten = true;
            }
        }
        ISimulationStrategy strategy = _registry.Get(definition.StrategyType).Create(definition);
        return _liveHost.Add(strategy, universe, commissionPerContract);
    }

    public bool Stop(string instanceId) => _liveHost.Stop(instanceId);
    public bool Pause(string instanceId) => _liveHost.Pause(instanceId);
    public bool Resume(string instanceId) => _liveHost.Resume(instanceId);
    public bool Remove(string instanceId) => _liveHost.Remove(instanceId);
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
        recorder.RecordManifest(CreateManifest(runId, StrategyRunMode.HistoricalReplay,
            definition, universe, commissionPerContract, eventFilePath));
        host.Add(_registry.Get(definition.StrategyType).Create(definition), universe, commissionPerContract);
        await source.ReplayAsync(speed, cancellationToken);
        host.StopAll("歷史重播完成");
        recorder.FinalizeSummary(DateTimeOffset.UtcNow);
        return output;
    }

    private void OnSnapshot(StrategySnapshot snapshot)
    {
        _liveRecorder.Record(snapshot);
        SnapshotChanged?.Invoke(snapshot);
    }

    private void OnExecution(ExecutionEvent execution)
    {
        _liveRecorder.Record(execution);
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
        ResolvedStrategyUniverse universe,
        decimal commissionPerContract,
        string inputPath)
    {
        var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> parameter in definition.Parameters)
            parameters[parameter.Key] = parameter.Value;
        string normalized = JsonSerializer.Serialize(parameters);
        string parameterHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        string absoluteInput = Path.GetFullPath(inputPath);
        InputArchiveIdentity input;
        var missing = new List<string>();
        if (File.Exists(absoluteInput))
        {
            var info = new FileInfo(absoluteInput);
            using FileStream stream = File.OpenRead(absoluteInput);
            input = new InputArchiveIdentity
            {
                AbsolutePath = absoluteInput,
                SizeBytes = info.Length,
                LastWriteTimeUtc = info.LastWriteTimeUtc,
                Sha256 = Convert.ToHexString(SHA256.HashData(stream)),
                MissingFields = new[] { "FirstEventAt", "LastEventAt" }
            };
            missing.AddRange(input.MissingFields);
        }
        else
        {
            input = new InputArchiveIdentity
            {
                AbsolutePath = absoluteInput,
                MissingFields = new[] { "SizeBytes", "LastWriteTimeUtc", "Sha256", "FirstEventAt", "LastEventAt" }
            };
            missing.AddRange(input.MissingFields);
        }

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
            _liveHost.Dispose();
            _liveHost.SnapshotChanged -= OnSnapshot;
            _liveHost.ExecutionOccurred -= OnExecution;
            _liveHost.OrderLifecycleOccurred -= _liveRecorder.Record;
            _liveHost.DecisionObserved -= _liveRecorder.Record;
            _liveRecorder.FinalizeSummary(DateTimeOffset.UtcNow);
            _liveRecorder.Dispose();
            _disposed = true;
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
