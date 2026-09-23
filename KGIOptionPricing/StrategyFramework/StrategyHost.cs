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
    private readonly long _strategyRunGeneration;
    private readonly IManagedOrderStrategy? _managedStrategy;
    private readonly IManagedStrategyExecutionService? _managedBroker;
    private readonly IStrategyDecisionSource? _decisionSource;
    private bool _disposed;
    private bool _disposing;
    private int _disposeOwnerThreadId;
    private int _disposeAttempt;
    private int _failedDisposeAttempt;
    private Exception? _lastDisposeException;
    private bool _paused;
    private bool _observationFaulted;

    public string InstanceId => _strategy.InstanceId;
    public long StrategyRunGeneration => _strategyRunGeneration;
    internal bool IsDisposed { get { lock (_sync) return _disposed; } }
    public event Action<StrategySnapshot>? SnapshotChanged;
    public event Action<ExecutionEvent>? ExecutionOccurred;
    public event Action<OrderLifecycleEvent>? OrderLifecycleOccurred;
    public event Action<StrategyDecisionObservation>? DecisionObserved;

    public StrategyInstanceRunner(
        ISimulationStrategy strategy,
        ResolvedStrategyUniverse universe,
        PaperPortfolioLedger ledger,
        IStrategyClock clock,
        decimal commissionPerContract,
        IStrategyExecutionService? executionService = null,
        bool deferStart = false,
        long? strategyRunGeneration = null)
    {
        _strategy = strategy;
        _universe = universe;
        _ledger = ledger;
        _clock = clock;
        _strategyRunGeneration = strategyRunGeneration ?? 1L;
        if (_strategyRunGeneration <= 0) throw new ArgumentOutOfRangeException(nameof(strategyRunGeneration));
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
        if (_strategy is IStrategyDecisionSource decisionSource)
        {
            _decisionSource = decisionSource;
            _decisionSource.DecisionObserved += OnDecisionObserved;
        }
        if (!deferStart) try
        {
            Initialize();
        }
        catch
        {
            _strategy.OrderRequested -= OnOrderRequested;
            _strategy.CancelAllRequested -= OnCancelAllRequested;
            _strategy.SnapshotChanged -= OnStrategySnapshotChanged;
            _broker.Filled -= OnFilled;
            if (_managedStrategy != null) _managedStrategy.CancelOrderRequested -= OnCancelOrderRequested;
            if (_managedBroker != null) _managedBroker.OrderLifecycleChanged -= OnOrderLifecycleChanged;
            if (_decisionSource != null) _decisionSource.DecisionObserved -= OnDecisionObserved;
            try { if (_broker is IDisposable disposable) disposable.Dispose(); } catch { }
            throw;
        }
    }

    internal void Initialize() => _strategy.Start(new StrategyRuntimeContext(_universe, _clock, _ledger));

    public bool UsesSymbol(string symbol) => _universe.AllProducts.Any(x =>
        string.Equals(x.Symbol, symbol, StringComparison.OrdinalIgnoreCase));

    public void OnMarketEvent(MarketEvent marketEvent)
    {
        lock (_sync)
        {
            if (_disposed || _disposing || _observationFaulted || !UsesSymbol(marketEvent.Symbol)) return;
            // Mark open positions from an actual last-traded price, never a half-tick midpoint.
            if (marketEvent.LastPrice > 0m)
                _ledger.ApplyMark(marketEvent.Symbol, marketEvent.LastPrice);
            try { _broker.OnMarketEvent(marketEvent); }
            catch (BrokerReconciliationRequiredException)
            {
                // Keep routing prices to the paused strategy for visibility;
                // the execution service will reject any resulting auto order.
            }
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
            if (_disposed || _disposing || _observationFaulted || _strategy is not IStrategyHeartbeat heartbeat) return;
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

    private void OnOrderRequested(PaperOrderRequest request)
    {
        lock (_sync)
        {
            if (_observationFaulted || _disposed || _disposing) return;
            PaperOrderRequest ownedRequest = request with
            {
                StrategyInstanceId = _strategy.InstanceId,
                StrategyRunGeneration = _strategyRunGeneration
            };
            try { _broker.Submit(ownedRequest, _clock.Now); }
            catch (BrokerReconciliationRequiredException)
            {
                // The execution service already published the protocol failure.
                // Preserve the strategy so Live can pause it for manual recovery.
            }
        }
    }

    private void OnCancelAllRequested()
    {
        lock (_sync)
        {
            if (_observationFaulted || _disposed || _disposing) return;
            try { _broker.CancelAll(_strategy.InstanceId); }
            catch (BrokerReconciliationRequiredException) { }
        }
    }

    private void OnStrategySnapshotChanged(StrategySnapshot snapshot) =>
        SnapshotChanged?.Invoke(WithPnl(snapshot));

    private void OnCancelOrderRequested(string clientOrderId)
    {
        lock (_sync)
        {
            if (_observationFaulted || _disposed || _disposing) return;
            try
            {
                if (_managedBroker?.CancelByClientOrderId(_strategy.InstanceId, clientOrderId) != false) return;
            }
            catch (BrokerReconciliationRequiredException) { return; }
            var failure = new OrderLifecycleEvent
            {
                Timestamp = _clock.Now,
                StrategyInstanceId = _strategy.InstanceId,
                StrategyRunGeneration = _strategyRunGeneration,
                ClientOrderId = clientOrderId,
                PreviousState = OrderLifecycleState.CancelPending,
                State = OrderLifecycleState.CancelPending,
                ReasonCode = "CancelRequestNotRouted"
            };
            _managedStrategy?.OnOrderLifecycle(failure);
            OrderLifecycleOccurred?.Invoke(failure);
        }
    }

    private void OnOrderLifecycleChanged(OrderLifecycleEvent orderEvent)
    {
        if (!string.Equals(orderEvent.StrategyInstanceId, _strategy.InstanceId, StringComparison.OrdinalIgnoreCase) ||
            (orderEvent.StrategyRunGeneration != 0 && orderEvent.StrategyRunGeneration != _strategyRunGeneration))
            return;
        lock (_sync)
        {
            if (_disposed) return;
            _managedStrategy?.OnOrderLifecycle(orderEvent);
        }
        OrderLifecycleOccurred?.Invoke(orderEvent);
    }

    private void OnDecisionObserved(StrategyDecisionObservation observation)
    {
        if (!string.Equals(observation.StrategyInstanceId, _strategy.InstanceId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(observation.StrategyType, _strategy.StrategyType, StringComparison.OrdinalIgnoreCase))
            return;
        lock (_sync)
        {
            if (_disposed || _disposing) return;
        }
        DecisionObserved?.Invoke(observation);
    }

    private void OnFilled(ExecutionEvent execution)
    {
        StrategySnapshot snapshot;
        lock (_sync)
        {
            if (_disposed || !string.Equals(execution.StrategyInstanceId, _strategy.InstanceId, StringComparison.OrdinalIgnoreCase) ||
                (execution.StrategyRunGeneration != 0 && execution.StrategyRunGeneration != _strategyRunGeneration))
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
            CommissionPerContract = _commissionPerContract,
            CompletedTrades = position.CompletedTrades
        };
    }

    private StrategySnapshot GetSnapshotCore()
    {
        StrategySnapshot snapshot = WithPnl(_strategy.GetSnapshot());
        if (_observationFaulted) return snapshot with
        {
            State = StrategyLifecycleState.Faulted,
            Reason = "FatalObservationFailure",
            UpdatedAt = DateTimeOffset.Now
        };
        return !_paused || snapshot.State is StrategyLifecycleState.Faulted or StrategyLifecycleState.Stopped
            ? snapshot : snapshot with
        {
            State = StrategyLifecycleState.Suspended,
            Reason = "使用者暫停",
            UpdatedAt = DateTimeOffset.Now
        };
    }

    internal void MarkObservationFault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        lock (_sync) _observationFaulted = true;
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
            if (_broker is KgiLiveStrategyExecutionService liveBroker)
                liveBroker.DisposeIfSafe(() =>
                    _ledger.GetPosition(_strategy.InstanceId, _universe.Tradable.Symbol).NetQuantity == 0);
            else if (_broker is IDisposable disposable) disposable.Dispose();
            Exception? cleanupError = null;
            lock (_sync)
            {
                _strategy.OrderRequested -= OnOrderRequested;
                _strategy.CancelAllRequested -= OnCancelAllRequested;
                _strategy.SnapshotChanged -= OnStrategySnapshotChanged;
                if (_managedStrategy != null) _managedStrategy.CancelOrderRequested -= OnCancelOrderRequested;
                if (_decisionSource != null) _decisionSource.DecisionObserved -= OnDecisionObserved;
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
    private bool _disposing;
    private bool _administrativeStop;
    private readonly Dictionary<string, int> _addOwners = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, StrategyInstanceRunner> _startingRunners = new(StringComparer.OrdinalIgnoreCase);
    internal bool IsDisposed { get { lock (_sync) return _disposed; } }

    public event Action<StrategySnapshot>? SnapshotChanged;
    public event Action<ExecutionEvent>? ExecutionOccurred;
    public event Action<OrderLifecycleEvent>? OrderLifecycleOccurred;
    public event Action<StrategyDecisionObservation>? DecisionObserved;
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
        IStrategyExecutionService? executionService = null,
        long? strategyRunGeneration = null)
    {
        lock (_sync)
        {
            if (_disposed || _disposing) throw new ObjectDisposedException(nameof(StrategyHost));
            if (_administrativeStop)
                throw new InvalidOperationException("StopAll 正在執行，暫停新增策略。");
            if (_addOwners.ContainsKey(strategy.InstanceId) ||
                _runners.Any(x => string.Equals(x.InstanceId, strategy.InstanceId, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"策略實例已存在：{strategy.InstanceId}");
            _addOwners.Add(strategy.InstanceId, Environment.CurrentManagedThreadId);
        }
        StrategyInstanceRunner? runner = null;
        bool addSucceeded = false;
        try
        {
            _source.Subscribe(universe.AllProducts);
            runner = new StrategyInstanceRunner(strategy, universe, _ledger, _clock, commissionPerContract,
                executionService, deferStart: true, strategyRunGeneration: strategyRunGeneration);
            lock (_sync) _startingRunners[strategy.InstanceId] = runner;
            runner.SnapshotChanged += PublishSnapshotSafely;
            runner.ExecutionOccurred += execution => ExecutionOccurred?.Invoke(execution);
            runner.OrderLifecycleOccurred += PublishOrderLifecycleSafely;
            runner.DecisionObserved += PublishDecisionSafely;
            try { runner.Initialize(); }
            catch (Exception startError)
            {
                try { runner.Dispose(); _ledger.RemoveFlatInstance(strategy.InstanceId); }
                catch (Exception cleanupError)
                {
                    if (runner.IsDisposed)
                    {
                        _ledger.RemoveFlatInstance(strategy.InstanceId);
                        throw new AggregateException(
                            "策略 Start 失敗；執行資源已安全關閉，但 Stop cleanup 亦回報錯誤。",
                            startError, cleanupError);
                    }
                    lock (_sync) _runners.Add(runner);
                    throw new AggregateException("策略 Start 失敗且仍有活動執行資源；Runner 已保留供安全關閉。", startError, cleanupError);
                }
                throw;
            }
            lock (_sync) _runners.Add(runner);
            addSucceeded = true;
            return runner;
        }
        finally
        {
            if (runner != null && addSucceeded)
            {
                try { PublishSnapshotSafely(runner.GetSnapshot()); }
                catch (ObjectDisposedException) { }
            }
            lock (_sync)
            {
                _addOwners.Remove(strategy.InstanceId);
                _startingRunners.Remove(strategy.InstanceId);
                Monitor.PulseAll(_sync);
            }
        }
    }

    public IReadOnlyList<StrategySnapshot> GetSnapshots()
    {
        StrategyInstanceRunner[] runners = GetRunnersForOperation(throwIfDisposed: true);
        return runners.Select(x => x.GetSnapshot()).ToArray();
    }

    public bool Stop(string instanceId, string reason = "使用者停止")
    {
        StrategyInstanceRunner? runner = FindForOperation(instanceId);
        if (runner == null) return false;
        runner.Stop(reason);
        return true;
    }

    public bool MarkObservationFault(string instanceId, Exception exception)
    {
        StrategyInstanceRunner? runner;
        lock (_sync)
        {
            _startingRunners.TryGetValue(instanceId, out runner);
            runner ??= _runners.FirstOrDefault(candidate =>
                string.Equals(candidate.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase));
        }
        if (runner == null) return false;
        runner.MarkObservationFault(exception);
        return true;
    }

    public bool Pause(string instanceId)
    {
        StrategyInstanceRunner? runner = FindForOperation(instanceId);
        return runner?.Pause() == true;
    }

    public bool Resume(string instanceId)
    {
        StrategyInstanceRunner? runner = FindForOperation(instanceId);
        return runner?.Resume() == true;
    }

    public bool Remove(string instanceId)
    {
        StrategyInstanceRunner? runner = FindForOperation(instanceId);
        if (runner == null) return false;
        // Dispose may fail while a live order is still active. Remove only
        // after success so the runner continues routing late fills.
        try { runner.Dispose(); }
        finally
        {
            if (runner.IsDisposed)
            {
                lock (_sync) _runners.Remove(runner);
                _ledger.RemoveFlatInstance(instanceId);
            }
        }
        return true;
    }

    public void StopAll(string reason = "全部停止")
    {
        StrategyInstanceRunner[] runners;
        lock (_sync)
        {
            if (_disposed || _disposing) return;
            if (_addOwners.Count > 0)
                throw new InvalidOperationException("仍有策略正在 Add/Start，StopAll 未執行；請稍後重試。");
            if (_administrativeStop)
                throw new InvalidOperationException("StopAll 已在執行中。");
            _administrativeStop = true;
            runners = _runners.ToArray();
        }
        try { foreach (StrategyInstanceRunner runner in runners) runner.Stop(reason); }
        finally
        {
            lock (_sync) { _administrativeStop = false; Monitor.PulseAll(_sync); }
        }
    }

    private void OnMarketEvent(MarketEvent marketEvent)
    {
        if (_clock is IEventDrivenStrategyClock eventClock) eventClock.Advance(marketEvent.Timestamp);
        StrategyInstanceRunner[] runners = GetRunnersForOperation(throwIfDisposed: false);
        foreach (StrategyInstanceRunner runner in runners) runner.OnMarketEvent(marketEvent);
    }

    public void Pulse(DateTimeOffset now)
    {
        if (_clock is IEventDrivenStrategyClock eventClock) eventClock.Advance(now);
        StrategyInstanceRunner[] runners = GetRunnersForOperation(throwIfDisposed: false);
        foreach (StrategyInstanceRunner runner in runners) runner.Pulse(now);
    }

    public void Dispose()
    {
        StrategyInstanceRunner[] runners;
        lock (_sync)
        {
            if (_disposed) return;
            if (_addOwners.Values.Contains(Environment.CurrentManagedThreadId))
                throw new InvalidOperationException("Add callback 不可同步 Dispose 同一個 StrategyHost。");
            if (_disposing) throw new InvalidOperationException("StrategyHost 正在釋放。");
            _disposing = true;
            while (_addOwners.Count > 0) Monitor.Wait(_sync);
            runners = _runners.ToArray();
        }
        try
        {
            foreach (StrategyInstanceRunner runner in runners)
            {
                try { runner.Dispose(); }
                finally { if (runner.IsDisposed) lock (_sync) _runners.Remove(runner); }
            }
            lock (_sync) _disposed = true;
            _source.MarketEventReceived -= OnMarketEvent;
            _source.Dispose();
        }
        finally
        {
            lock (_sync) { _disposing = false; Monitor.PulseAll(_sync); }
        }
    }

    private void PublishSnapshotSafely(StrategySnapshot snapshot)
    {
        Delegate[] handlers = SnapshotChanged?.GetInvocationList() ?? Array.Empty<Delegate>();
        foreach (Action<StrategySnapshot> handler in handlers.Cast<Action<StrategySnapshot>>())
        {
            try { handler(snapshot); }
            catch (Exception exception) { Debug.WriteLine($"Initial strategy snapshot subscriber failed: {exception}"); }
        }
    }

    private void PublishOrderLifecycleSafely(OrderLifecycleEvent orderEvent)
    {
        Delegate[] handlers = OrderLifecycleOccurred?.GetInvocationList() ?? Array.Empty<Delegate>();
        foreach (Action<OrderLifecycleEvent> handler in handlers.Cast<Action<OrderLifecycleEvent>>())
        {
            try { handler(orderEvent); }
            catch (Exception exception) { Debug.WriteLine($"Order lifecycle subscriber failed: {exception}"); }
        }
    }

    private void PublishDecisionSafely(StrategyDecisionObservation observation)
    {
        Delegate[] handlers = DecisionObserved?.GetInvocationList() ?? Array.Empty<Delegate>();
        foreach (Action<StrategyDecisionObservation> handler in handlers.Cast<Action<StrategyDecisionObservation>>())
        {
            try { handler(observation); }
            catch (Exception exception) { Debug.WriteLine($"Strategy decision subscriber failed: {exception}"); }
        }
    }

    private StrategyInstanceRunner? FindForOperation(string instanceId) =>
        GetRunnerForTargetedOperation(instanceId);

    private StrategyInstanceRunner? GetRunnerForTargetedOperation(string instanceId)
    {
        lock (_sync)
        {
            while (_addOwners.TryGetValue(instanceId, out int ownerThreadId))
            {
                if (ownerThreadId == Environment.CurrentManagedThreadId)
                    throw new InvalidOperationException("Add/Start callback 不可同步操作自己的 partial runner。");
                Monitor.Wait(_sync);
            }
            if (_disposed || _disposing) return null;
            return _runners.FirstOrDefault(x =>
                string.Equals(x.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase));
        }
    }

    private StrategyInstanceRunner[] GetRunnersForOperation(bool throwIfDisposed)
    {
        lock (_sync)
        {
            if (_disposed || _disposing)
            {
                if (throwIfDisposed) throw new ObjectDisposedException(nameof(StrategyHost));
                return Array.Empty<StrategyInstanceRunner>();
            }
            return _runners.ToArray();
        }
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
