using System;
using System.Collections.Generic;
using System.Linq;
using KGIOptionPricing.StrategyFramework.Execution;
using KGIOptionPricing.StrategyFramework.Performance;

namespace KGIOptionPricing.StrategyFramework;

public sealed class StrategyRegistry
{
    private readonly Dictionary<string, IStrategyFactory> _factories = new(StringComparer.OrdinalIgnoreCase);
    public void Register(IStrategyFactory factory) => _factories[factory.StrategyType] = factory;
    public IReadOnlyList<IStrategyFactory> Factories => _factories.Values.OrderBy(x => x.DisplayName).ToArray();
    public IStrategyFactory Get(string strategyType) => _factories.TryGetValue(strategyType, out IStrategyFactory? factory)
        ? factory : throw new KeyNotFoundException($"找不到策略類型：{strategyType}");
}

public sealed class StrategyInstanceRunner : IDisposable
{
    private readonly object _sync = new();
    private readonly ISimulationStrategy _strategy;
    private readonly IStrategyExecutionService _broker;
    private readonly PaperPortfolioLedger _ledger;
    private readonly ResolvedStrategyUniverse _universe;
    private readonly decimal _commissionPerContract;
    private readonly IStrategyClock _clock;
    private readonly IManagedOrderStrategy? _managedStrategy;
    private readonly IManagedStrategyExecutionService? _managedBroker;
    private bool _disposed;
    private bool _paused;

    public string InstanceId => _strategy.InstanceId;
    public event Action<StrategySnapshot>? SnapshotChanged;
    public event Action<ExecutionEvent>? ExecutionOccurred;

    public StrategyInstanceRunner(
        ISimulationStrategy strategy,
        ResolvedStrategyUniverse universe,
        PaperPortfolioLedger ledger,
        IStrategyClock clock,
        decimal commissionPerContract,
        IStrategyExecutionService? executionService = null)
    {
        _strategy = strategy;
        _universe = universe;
        _ledger = ledger;
        _clock = clock;
        _commissionPerContract = Math.Max(0m, commissionPerContract);
        _broker = executionService ?? new CrossPricePaperExecutionService(commissionPerContract, clock);
        foreach (ResolvedProduct product in universe.AllProducts) ledger.RegisterProduct(strategy.InstanceId, product);
        _strategy.OrderRequested += OnOrderRequested;
        _strategy.CancelAllRequested += OnCancelAllRequested;
        _strategy.SnapshotChanged += OnStrategySnapshotChanged;
        _broker.Filled += OnFilled;
        if (_strategy is IManagedOrderStrategy managedStrategy &&
            _broker is IManagedStrategyExecutionService managedBroker)
        {
            _managedStrategy = managedStrategy;
            _managedBroker = managedBroker;
            _managedStrategy.CancelOrderRequested += OnCancelOrderRequested;
            _managedBroker.OrderLifecycleChanged += OnOrderLifecycleChanged;
        }
        _strategy.Start(new StrategyRuntimeContext(universe, clock, ledger));
    }

    public bool UsesSymbol(string symbol) => _universe.AllProducts.Any(x =>
        string.Equals(x.Symbol, symbol, StringComparison.OrdinalIgnoreCase));

    public void OnMarketEvent(MarketEvent marketEvent)
    {
        lock (_sync)
        {
            if (_disposed || !UsesSymbol(marketEvent.Symbol)) return;
            // Mark open positions from an actual last-traded price, never a half-tick midpoint.
            if (marketEvent.LastPrice > 0m)
                _ledger.ApplyMark(marketEvent.Symbol, marketEvent.LastPrice);
            _broker.OnMarketEvent(marketEvent);
            if (_paused)
            {
                // Pausing blocks entries, but an open position still needs market events
                // for trailing, time, session, and data-health exits.
                PositionSnapshot position = _ledger.GetPosition(_strategy.InstanceId, _universe.Tradable.Symbol);
                if (position.NetQuantity != 0) _strategy.OnMarketEvent(marketEvent);
                SnapshotChanged?.Invoke(GetSnapshotCore());
                return;
            }
            _strategy.OnMarketEvent(marketEvent);
        }
    }

    public StrategySnapshot GetSnapshot() { lock (_sync) return GetSnapshotCore(); }

    public bool Pause()
    {
        lock (_sync)
        {
            if (_disposed || _paused || _strategy.State is StrategyLifecycleState.Stopped or StrategyLifecycleState.Faulted)
                return false;
            _paused = true;
            _strategy.Pause("使用者暫停");
            return true;
        }
    }

    public bool Resume()
    {
        lock (_sync)
        {
            if (_disposed || !_paused || _strategy.State is StrategyLifecycleState.Stopped or StrategyLifecycleState.Faulted)
                return false;
            _paused = false;
            _strategy.Resume();
            return true;
        }
    }

    public void Stop(string reason)
    {
        lock (_sync)
        {
            _paused = false;
            _broker.CancelAll(_strategy.InstanceId);
            _strategy.Stop(reason);
        }
    }

    private void OnOrderRequested(PaperOrderRequest request) =>
        _broker.Submit(request, _clock.Now);

    private void OnCancelAllRequested() => _broker.CancelAll(_strategy.InstanceId);

    private void OnStrategySnapshotChanged(StrategySnapshot snapshot) =>
        SnapshotChanged?.Invoke(WithPnl(snapshot));

    private void OnCancelOrderRequested(string clientOrderId) =>
        _managedBroker?.CancelByClientOrderId(_strategy.InstanceId, clientOrderId);

    private void OnOrderLifecycleChanged(OrderLifecycleEvent orderEvent)
    {
        if (string.Equals(orderEvent.StrategyInstanceId, _strategy.InstanceId, StringComparison.OrdinalIgnoreCase))
            _managedStrategy?.OnOrderLifecycle(orderEvent);
    }

    private void OnFilled(ExecutionEvent execution)
    {
        if (!string.Equals(execution.StrategyInstanceId, _strategy.InstanceId, StringComparison.OrdinalIgnoreCase))
            return;
        _ledger.ApplyExecution(execution);
        _strategy.OnExecution(execution);
        ExecutionOccurred?.Invoke(execution);
        SnapshotChanged?.Invoke(GetSnapshot());
    }

    private StrategySnapshot WithPnl(StrategySnapshot snapshot)
    {
        PositionSnapshot position = _ledger.GetPosition(_strategy.InstanceId, _universe.Tradable.Symbol);
        return snapshot with
        {
            Position = position.NetQuantity,
            RealizedPnl = position.RealizedPnl,
            UnrealizedPnl = position.UnrealizedPnl,
            NetPnl = position.NetPnl,
            CommissionPerContract = _commissionPerContract
        };
    }

    private StrategySnapshot GetSnapshotCore()
    {
        StrategySnapshot snapshot = WithPnl(_strategy.GetSnapshot());
        return !_paused ? snapshot : snapshot with
        {
            State = StrategyLifecycleState.Suspended,
            Reason = "使用者暫停",
            UpdatedAt = DateTimeOffset.Now
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop("執行個體已釋放");
        _disposed = true;
        _strategy.OrderRequested -= OnOrderRequested;
        _strategy.CancelAllRequested -= OnCancelAllRequested;
        _strategy.SnapshotChanged -= OnStrategySnapshotChanged;
        _broker.Filled -= OnFilled;
        if (_managedStrategy != null) _managedStrategy.CancelOrderRequested -= OnCancelOrderRequested;
        if (_managedBroker != null) _managedBroker.OrderLifecycleChanged -= OnOrderLifecycleChanged;
        if (_broker is IDisposable disposable) disposable.Dispose();
    }
}

public sealed class StrategyHost : IDisposable
{
    private readonly object _sync = new();
    private readonly List<StrategyInstanceRunner> _runners = new();
    private readonly PaperPortfolioLedger _ledger = new();
    private readonly IStrategyClock _clock;
    private readonly IMarketEventSource _source;

    public event Action<StrategySnapshot>? SnapshotChanged;
    public event Action<ExecutionEvent>? ExecutionOccurred;
    public PaperPortfolioLedger Ledger => _ledger;

    public StrategyHost(IMarketEventSource source, IStrategyClock? clock = null)
    {
        _source = source;
        _clock = clock ?? new SystemStrategyClock();
        _source.MarketEventReceived += OnMarketEvent;
    }

    public StrategyInstanceRunner Add(
        ISimulationStrategy strategy,
        ResolvedStrategyUniverse universe,
        decimal commissionPerContract = 0m,
        IStrategyExecutionService? executionService = null)
    {
        var runner = new StrategyInstanceRunner(strategy, universe, _ledger, _clock, commissionPerContract, executionService);
        runner.SnapshotChanged += snapshot => SnapshotChanged?.Invoke(snapshot);
        runner.ExecutionOccurred += execution => ExecutionOccurred?.Invoke(execution);
        lock (_sync) _runners.Add(runner);
        _source.Subscribe(universe.AllProducts);
        SnapshotChanged?.Invoke(runner.GetSnapshot());
        return runner;
    }

    public IReadOnlyList<StrategySnapshot> GetSnapshots()
    {
        lock (_sync) return _runners.Select(x => x.GetSnapshot()).ToArray();
    }

    public bool Stop(string instanceId, string reason = "使用者停止")
    {
        StrategyInstanceRunner? runner;
        lock (_sync) runner = _runners.FirstOrDefault(x => string.Equals(x.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase));
        if (runner == null) return false;
        runner.Stop(reason);
        return true;
    }

    public bool Pause(string instanceId)
    {
        StrategyInstanceRunner? runner;
        lock (_sync) runner = _runners.FirstOrDefault(x => string.Equals(x.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase));
        return runner?.Pause() == true;
    }

    public bool Resume(string instanceId)
    {
        StrategyInstanceRunner? runner;
        lock (_sync) runner = _runners.FirstOrDefault(x => string.Equals(x.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase));
        return runner?.Resume() == true;
    }

    public bool Remove(string instanceId)
    {
        StrategyInstanceRunner? runner;
        lock (_sync)
        {
            runner = _runners.FirstOrDefault(x => string.Equals(x.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase));
            if (runner != null) _runners.Remove(runner);
        }
        if (runner == null) return false;
        runner.Dispose();
        return true;
    }

    public void StopAll(string reason = "全部停止")
    {
        StrategyInstanceRunner[] runners;
        lock (_sync) runners = _runners.ToArray();
        foreach (StrategyInstanceRunner runner in runners) runner.Stop(reason);
    }

    private void OnMarketEvent(MarketEvent marketEvent)
    {
        if (_clock is IEventDrivenStrategyClock eventClock) eventClock.Advance(marketEvent.Timestamp);
        StrategyInstanceRunner[] runners;
        lock (_sync) runners = _runners.ToArray();
        foreach (StrategyInstanceRunner runner in runners) runner.OnMarketEvent(marketEvent);
    }

    public void Dispose()
    {
        _source.MarketEventReceived -= OnMarketEvent;
        StrategyInstanceRunner[] runners;
        lock (_sync) { runners = _runners.ToArray(); _runners.Clear(); }
        foreach (StrategyInstanceRunner runner in runners) runner.Dispose();
        _source.Dispose();
    }
}
