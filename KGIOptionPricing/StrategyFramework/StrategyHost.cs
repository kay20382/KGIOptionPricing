using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
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
    private bool _disposing;
    private int _disposeOwnerThreadId;
    private int _disposeAttempt;
    private int _failedDisposeAttempt;
    private Exception? _lastDisposeException;
    private bool _paused;

    public string InstanceId => _strategy.InstanceId;
    internal bool IsDisposed { get { lock (_sync) return _disposed; } }
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
            if (_disposed || _disposing || !UsesSymbol(marketEvent.Symbol)) return;
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

    public void Pulse(DateTimeOffset now)
    {
        lock (_sync)
        {
            if (_disposed || _disposing || _strategy is not IStrategyHeartbeat heartbeat) return;
            if (_clock is IEventDrivenStrategyClock eventClock) eventClock.Advance(now);
            if (_paused)
            {
                PositionSnapshot position = _ledger.GetPosition(_strategy.InstanceId, _universe.Tradable.Symbol);
                if (position.NetQuantity == 0) return;
            }
            heartbeat.OnHeartbeat(now);
        }
    }

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
            if (_disposed) return;
            StopCore(reason);
        }
    }

    private void StopCore(string reason)
    {
        _paused = false;
        bool managed = _managedStrategy is not null && _managedBroker is not null;
        if (!managed)
            _broker.CancelAll(_strategy.InstanceId);
        _strategy.Stop(reason);
        if (managed && _strategy.State == StrategyLifecycleState.Stopped &&
            _broker.GetWorkingOrders(_strategy.InstanceId).Count > 0)
            _broker.CancelAll(_strategy.InstanceId);
    }

    private void OnOrderRequested(PaperOrderRequest request) =>
        _broker.Submit(request, _clock.Now);

    private void OnCancelAllRequested() => _broker.CancelAll(_strategy.InstanceId);

    private void OnStrategySnapshotChanged(StrategySnapshot snapshot) =>
        SnapshotChanged?.Invoke(WithPnl(snapshot));

    private void OnCancelOrderRequested(string clientOrderId)
    {
        if (_managedBroker?.CancelByClientOrderId(_strategy.InstanceId, clientOrderId) != false) return;
        _managedStrategy?.OnOrderLifecycle(new OrderLifecycleEvent
        {
            Timestamp = _clock.Now,
            StrategyInstanceId = _strategy.InstanceId,
            ClientOrderId = clientOrderId,
            PreviousState = OrderLifecycleState.CancelPending,
            State = OrderLifecycleState.CancelPending,
            ReasonCode = "CancelRequestNotRouted"
        });
    }

    private void OnOrderLifecycleChanged(OrderLifecycleEvent orderEvent)
    {
        if (!string.Equals(orderEvent.StrategyInstanceId, _strategy.InstanceId, StringComparison.OrdinalIgnoreCase))
            return;
        lock (_sync)
        {
            if (_disposed) return;
            _managedStrategy?.OnOrderLifecycle(orderEvent);
        }
    }

    private void OnFilled(ExecutionEvent execution)
    {
        StrategySnapshot snapshot;
        lock (_sync)
        {
            if (_disposed || !string.Equals(execution.StrategyInstanceId, _strategy.InstanceId, StringComparison.OrdinalIgnoreCase))
                return;
            // Applying the execution and notifying the strategy are one serialized
            // state transition. Broker callbacks may be concurrent with heartbeats.
            _ledger.ApplyExecution(execution);
            _strategy.OnExecution(execution);
            snapshot = GetSnapshotCore();
        }
        ExecutionOccurred?.Invoke(execution);
        SnapshotChanged?.Invoke(snapshot);
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
        return !_paused || snapshot.State is StrategyLifecycleState.Faulted or StrategyLifecycleState.Stopped
            ? snapshot : snapshot with
        {
            State = StrategyLifecycleState.Suspended,
            Reason = "使用者暫停",
            UpdatedAt = DateTimeOffset.Now
        };
    }

    public void Dispose()
    {
        int attempt;
        lock (_sync)
        {
            if (_disposed) return;
            if (_disposing)
            {
                if (_disposeOwnerThreadId == Environment.CurrentManagedThreadId)
                    throw new InvalidOperationException("Runner 不可在 Dispose callback 中同步再次 Dispose。");
                attempt = _disposeAttempt;
                while (_disposing && attempt == _disposeAttempt) Monitor.Wait(_sync);
                if (_disposed) return;
                if (_failedDisposeAttempt == attempt && _lastDisposeException != null)
                    ExceptionDispatchInfo.Capture(_lastDisposeException).Throw();
            }
            _disposing = true;
            _disposeOwnerThreadId = Environment.CurrentManagedThreadId;
            attempt = ++_disposeAttempt;
        }

        // The live broker is fail-closed. Dispose it before mutating runner
        // state or detaching routes so a failure leaves late fills observable.
        try
        {
            if (_broker is IDisposable disposable) disposable.Dispose();
            Exception? cleanupError = null;
            lock (_sync)
            {
                _strategy.OrderRequested -= OnOrderRequested;
                _strategy.CancelAllRequested -= OnCancelAllRequested;
                _strategy.SnapshotChanged -= OnStrategySnapshotChanged;
                if (_managedStrategy != null) _managedStrategy.CancelOrderRequested -= OnCancelOrderRequested;
                _paused = false;
                try { _strategy.Stop("執行個體已釋放"); }
                catch (Exception exception) { cleanupError = exception; }
                _disposed = true;
                _broker.Filled -= OnFilled;
                if (_managedBroker != null) _managedBroker.OrderLifecycleChanged -= OnOrderLifecycleChanged;
            }
            if (cleanupError != null) ExceptionDispatchInfo.Capture(cleanupError).Throw();
        }
        catch (Exception exception)
        {
            lock (_sync)
            {
                _lastDisposeException = exception;
                _failedDisposeAttempt = attempt;
            }
            throw;
        }
        finally
        {
            lock (_sync)
            {
                _disposing = false;
                _disposeOwnerThreadId = 0;
                Monitor.PulseAll(_sync);
            }
        }
    }
}

public sealed class StrategyHost : IDisposable
{
    private readonly object _sync = new();
    private readonly List<StrategyInstanceRunner> _runners = new();
    private readonly PaperPortfolioLedger _ledger = new();
    private readonly IStrategyClock _clock;
    private readonly IMarketEventSource _source;
    private bool _disposed;
    internal bool IsDisposed { get { lock (_sync) return _disposed; } }

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
            runner = _runners.FirstOrDefault(x => string.Equals(x.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase));
        if (runner == null) return false;
        // Dispose may fail while a live order is still active. Remove only
        // after success so the runner continues routing late fills.
        try { runner.Dispose(); }
        finally
        {
            if (runner.IsDisposed) lock (_sync) _runners.Remove(runner);
        }
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

    public void Pulse(DateTimeOffset now)
    {
        if (_clock is IEventDrivenStrategyClock eventClock) eventClock.Advance(now);
        StrategyInstanceRunner[] runners;
        lock (_sync) runners = _runners.ToArray();
        foreach (StrategyInstanceRunner runner in runners) runner.Pulse(now);
    }

    public void Dispose()
    {
        StrategyInstanceRunner[] runners;
        lock (_sync) runners = _runners.ToArray();
        foreach (StrategyInstanceRunner runner in runners)
        {
            try { runner.Dispose(); }
            finally { if (runner.IsDisposed) lock (_sync) _runners.Remove(runner); }
        }
        lock (_sync) _disposed = true;
        _source.MarketEventReceived -= OnMarketEvent;
        _source.Dispose();
    }
}

internal sealed class StrategyHeartbeatTimer : IDisposable
{
    private readonly Action<DateTimeOffset> _pulse;
    private readonly System.Threading.Timer _timer;
    private int _running;
    private int _disposed;
    private int _callbackThreadId;

    internal StrategyHeartbeatTimer(Action<DateTimeOffset> pulse, TimeSpan? interval = null)
    {
        _pulse = pulse ?? throw new ArgumentNullException(nameof(pulse));
        TimeSpan due = interval ?? TimeSpan.FromMilliseconds(250);
        _timer = new System.Threading.Timer(OnTimer, null, due, due);
    }

    private void OnTimer(object? state)
    {
        if (Volatile.Read(ref _disposed) != 0 || Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            return;
        try
        {
            Volatile.Write(ref _callbackThreadId, Environment.CurrentManagedThreadId);
            _pulse(DateTimeOffset.Now);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Strategy heartbeat failed: {exception}");
        }
        finally
        {
            Volatile.Write(ref _callbackThreadId, 0);
            Volatile.Write(ref _running, 0);
        }
    }

    public void Dispose()
    {
        if (Volatile.Read(ref _callbackThreadId) == Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Heartbeat callback 不可同步釋放自己的 timer；請由外部執行緒重試。");
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        using var drained = new ManualResetEvent(false);
        if (_timer.Dispose(drained)) drained.WaitOne();
    }
}
