using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
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
    private readonly StrategyHeartbeatTimer _heartbeat;
    private readonly SimulationRunRecorder _liveRecorder;
    private readonly string _root;
    private bool _disposed;

    public event Action<StrategySnapshot>? SnapshotChanged;
    public event Action<ExecutionEvent>? ExecutionOccurred;
    public string LiveArchivePath { get; }
    public IReadOnlyList<IStrategyFactory> Factories => _registry.Factories;

    public StrategySimulationService(KGIQuoteService quote, string rootDirectory)
    {
        _root = Path.GetFullPath(rootDirectory);
        string runId = $"live-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        var archive = new MarketDataArchiveWriter(Path.Combine(_root, "MarketDataArchive"), runId);
        var source = new RecordingMarketEventSource(new KgiStrategyMarketDataSource(quote), archive);
        LiveArchivePath = archive.DirectoryPath;
        _liveRecorder = new SimulationRunRecorder(Path.Combine(_root, "StrategyRuns", runId));
        _liveHost = new StrategyHost(source);
        _registry.Register(new MeanReversionScalperFactory());
        _registry.Register(new WaveletScalperFactory());
        _registry.Register(new NikkeiMicrostructureAlphaFactory());
        _registry.Register(new MiniLeadMicroFactory());
        _registry.Register(new OrderBookTrendFactory());
        _liveHost.SnapshotChanged += OnSnapshot;
        _liveHost.ExecutionOccurred += OnExecution;
        _heartbeat = new StrategyHeartbeatTimer(_liveHost.Pulse);
    }

    public StrategyInstanceRunner AddLive(StrategyDefinition definition, decimal commissionPerContract)
    {
        EnsureUnique(definition.InstanceId, _liveHost.GetSnapshots());
        ResolvedStrategyUniverse universe = _resolver.Resolve(definition.Universe);
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
        host.Add(_registry.Get(definition.StrategyType).Create(definition), universe, commissionPerContract);
        await source.ReplayAsync(speed, cancellationToken);
        host.StopAll("歷史重播完成");
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
        if (_disposed) return;
        _disposed = true;
        _heartbeat.Dispose();
        _liveHost.SnapshotChanged -= OnSnapshot;
        _liveHost.ExecutionOccurred -= OnExecution;
        _liveHost.Dispose();
        _liveRecorder.Dispose();
    }

    private sealed class ReplayStrategyClock : IEventDrivenStrategyClock
    {
        public DateTimeOffset Now { get; private set; }
        public void Advance(DateTimeOffset timestamp) => Now = timestamp;
    }
}
