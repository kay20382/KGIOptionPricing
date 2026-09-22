using KGIOptionPricing.OrderManagement.Core;
using System.Collections.Concurrent;
using KGIOptionPricing.Strategies.MeanReversionScalper;
using KGIOptionPricing.Strategies.MiniLeadMicro;
using KGIOptionPricing.Strategies.NikkeiMicrostructureAlpha;
using KGIOptionPricing.Strategies.OrderBookTrend;
using KGIOptionPricing.Strategies.WaveletScalper;
using KGIOptionPricing.StrategyFramework.Execution;
using KGIOptionPricing.StrategyFramework.MarketData;
using System.Runtime.ExceptionServices;
using System.Diagnostics;

namespace KGIOptionPricing.StrategyFramework;

public sealed record LiveStrategyCloseStatus(
    bool Exists,
    int Position,
    int ActiveBrokerOrders,
    int QuarantinedBrokerOrders,
    int ProtocolViolations,
    bool ManualReconciliationRequired,
    int LocalConditions,
    bool ClosePending);

public sealed class LiveStrategyService : IDisposable
{
    private readonly StrategyRegistry _registry = new();
    private readonly StrategyProductResolver _resolver = new();
    private readonly StrategyHost _host;
    private StrategyHeartbeatTimer? _heartbeat;
    private readonly object _disposeSync = new();
    private DisposeAttempt? _disposeAttempt;
    private readonly OrderManager _orders;
    private readonly Func<bool> _isLoggedIn;
    private readonly Func<string> _account;
    private readonly Func<StrategyDefinition, ResolvedStrategyUniverse> _resolve;
    private readonly Func<StrategyDefinition, ISimulationStrategy> _createStrategy;
    private readonly Func<StrategyDefinition, ResolvedStrategyUniverse, decimal, KgiLiveStrategyExecutionService> _createExecution;
    private readonly Action? _afterHostAddFailure;
    private readonly ConcurrentDictionary<string, KgiLiveStrategyExecutionService> _executions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, decimal> _dailyLossLimits = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _riskTriggered = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, PendingRisk> _pendingRisk = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _riskProcessing = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _activeGenerations = new(StringComparer.OrdinalIgnoreCase);
    private long _generationSequence;
    private readonly ConcurrentDictionary<string, byte> _pendingClose = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _closeFlattenAuthorized = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, FlattenAttempt> _flattenPending = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _closeRetryNeeded = new(StringComparer.OrdinalIgnoreCase);
    private long _flattenToken;
    private readonly ConcurrentDictionary<string, AddReservation> _addReservations = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public event Action<StrategySnapshot>? SnapshotChanged;
    public event Action<ExecutionEvent>? ExecutionOccurred;
    public string Account => _account();
    public bool IsLoggedIn => _isLoggedIn();

    public LiveStrategyService(KGIQuoteService quote, OrderManager orders, KGITradeService trade)
    {
        _orders = orders;
        _isLoggedIn = () => trade.IsLoggedIn;
        _account = () => trade.Account;
        _resolve = definition => _resolver.Resolve(definition.Universe);
        _createStrategy = definition => _registry.Get(definition.StrategyType).Create(definition);
        _createExecution = (definition, universe, commission) =>
        {
            ContractInfo contract = SymbolRepository.Instance.GetHotMonthContract(definition.Universe.Tradable.ProductId)
                ?? throw new InvalidOperationException("找不到 225MC 熱門月合約，禁止實盤啟動。");
            return new KgiLiveStrategyExecutionService(
                _orders, trade, definition.InstanceId, universe.Tradable, contract, commission);
        };
        _afterHostAddFailure = null;
        _registry.Register(new MeanReversionScalperFactory());
        _registry.Register(new WaveletScalperFactory());
        _registry.Register(new NikkeiMicrostructureAlphaFactory());
        _registry.Register(new MiniLeadMicroFactory());
        _registry.Register(new OrderBookTrendFactory());
        _host = new StrategyHost(new KgiStrategyMarketDataSource(quote));
        _host.SnapshotChanged += OnSnapshot;
        _host.ExecutionOccurred += execution => ExecutionOccurred?.Invoke(execution);
        _heartbeat = new StrategyHeartbeatTimer(Pulse);
    }

    public LiveStrategyService(
        StrategyHost host,
        Func<bool> isLoggedIn,
        Func<string> account,
        Func<StrategyDefinition, ResolvedStrategyUniverse> resolve,
        Func<StrategyDefinition, ISimulationStrategy> createStrategy,
        Func<StrategyDefinition, ResolvedStrategyUniverse, decimal, KgiLiveStrategyExecutionService> createExecution,
        bool enableHeartbeat,
        Action? afterHostAddFailure = null)
    {
        _orders = null!;
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _isLoggedIn = isLoggedIn ?? throw new ArgumentNullException(nameof(isLoggedIn));
        _account = account ?? throw new ArgumentNullException(nameof(account));
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        _createStrategy = createStrategy ?? throw new ArgumentNullException(nameof(createStrategy));
        _createExecution = createExecution ?? throw new ArgumentNullException(nameof(createExecution));
        _afterHostAddFailure = afterHostAddFailure;
        _host.SnapshotChanged += OnSnapshot;
        _host.ExecutionOccurred += execution => ExecutionOccurred?.Invoke(execution);
        _heartbeat = enableHeartbeat ? new StrategyHeartbeatTimer(Pulse) : null;
    }

    public void Add(StrategyDefinition definition, decimal commissionPerContract, decimal dailyLossLimit)
    {
        if (!_isLoggedIn()) throw new InvalidOperationException("交易服務尚未登入，禁止啟動實盤策略。");
        if (string.Equals(definition.StrategyType, MiniLeadMicroFactory.TypeName, StringComparison.OrdinalIgnoreCase) &&
            (!definition.Parameters.TryGetValue("ResearchValidated", out string? validated) ||
             !bool.TryParse(validated, out bool isValidated) || !isValidated))
            throw new InvalidOperationException("Mini 領先 Micro 尚屬研究策略；未明確標記已通過樣本外、滑價、延遲與模擬盤門檻，禁止實盤啟動。");
        if (!string.Equals(definition.Universe.Tradable.ProductId, "225MC", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("實盤策略第一版僅允許交易商品 225MC。");
        var reservation = new AddReservation(Environment.CurrentManagedThreadId, Interlocked.Increment(ref _generationSequence));
        if (!_addReservations.TryAdd(definition.InstanceId, reservation))
            throw new InvalidOperationException($"實盤策略實例正在加入：{definition.InstanceId}");
        try
        {
            if (_executions.ContainsKey(definition.InstanceId) ||
                _host.GetSnapshots().Any(x => string.Equals(x.InstanceId, definition.InstanceId, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"實盤策略實例已存在：{definition.InstanceId}");

            ResolvedStrategyUniverse universe = _resolve(definition);
            var execution = _createExecution(definition, universe, commissionPerContract);
            execution.Failed += reason => QueueRiskStop(definition.InstanceId, reservation.Generation, reason);
            execution.StateChanged += () => OnExecutionStateChanged(definition.InstanceId, execution);
            execution.OrderLifecycleChanged += state => OnExecutionLifecycleChanged(definition.InstanceId, state);
            if (!_executions.TryAdd(definition.InstanceId, execution))
            {
                execution.Dispose();
                throw new InvalidOperationException($"實盤策略實例已存在：{definition.InstanceId}");
            }
            _dailyLossLimits[definition.InstanceId] = Math.Max(1m, dailyLossLimit);
            _activeGenerations[definition.InstanceId] = reservation.Generation;
            try
            {
                _host.Add(_createStrategy(definition), universe, commissionPerContract, execution);
            }
            catch (Exception addError)
            {
                Exception? hookError = null;
                try { _afterHostAddFailure?.Invoke(); }
                catch (Exception exception) { hookError = exception; }
                // Host retaining the runner is the single owner of this executor.
                // Never race an independent cleanup against its ledger/routes.
                bool retainedByHost = _host.GetSnapshots().Any(x =>
                    string.Equals(x.InstanceId, definition.InstanceId, StringComparison.OrdinalIgnoreCase));
                if (retainedByHost)
                {
                    if (hookError != null) throw new AggregateException(addError, hookError);
                    throw;
                }
                try
                {
                    execution.Dispose();
                }
                catch (Exception cleanupError)
                {
                    // Ownership must remain discoverable when an active live order
                    // prevents cleanup; close/retry paths still need this executor.
                    throw hookError == null
                        ? new AggregateException("策略加入失敗，且實盤執行器仍有活動資源。", addError, cleanupError)
                        : new AggregateException("策略加入、診斷 hook 與實盤清理皆失敗。", addError, hookError, cleanupError);
                }
                _executions.TryRemove(definition.InstanceId, out _);
                _dailyLossLimits.TryRemove(definition.InstanceId, out _);
                _activeGenerations.TryRemove(definition.InstanceId, out _);
                if (_pendingRisk.TryGetValue(definition.InstanceId, out PendingRisk? pending) &&
                    pending.Generation == reservation.Generation)
                    _pendingRisk.TryRemove(definition.InstanceId, out _);
                if (hookError != null) throw new AggregateException(addError, hookError);
                throw;
            }
        }
        finally
        {
            _addReservations.TryRemove(definition.InstanceId, out _);
            reservation.Completed.Set();
            TryProcessPendingRisk(definition.InstanceId, reservation.Generation);
        }
    }

    public bool Pause(string instanceId)
    {
        WaitForAdd(instanceId);
        StrategySnapshot? snapshot = Find(instanceId);
        return snapshot != null && _host.Pause(instanceId);
    }

    public bool Resume(string instanceId) { WaitForAdd(instanceId); return _isLoggedIn() && _host.Resume(instanceId); }

    public bool EmergencyStop(string instanceId, string reason = "使用者停止實盤策略")
    {
        WaitForAdd(instanceId);
        StrategySnapshot? snapshot = Find(instanceId);
        if (snapshot == null) return false;
        _pendingClose.TryAdd(instanceId, 0);
        _closeFlattenAuthorized.TryAdd(instanceId, 0);
        _host.Pause(instanceId); // Stop new signals, but keep the runner alive to receive cancel/flatten fills.
        if (_executions.TryGetValue(instanceId, out KgiLiveStrategyExecutionService? execution))
        {
            if (execution.ProtocolViolationCount == 0) execution.CancelAll(instanceId);
        }
        TryFinalizeClose(instanceId);
        return true;
    }

    public LiveStrategyCloseStatus GetCloseStatus(string instanceId)
    {
        WaitForAdd(instanceId);
        StrategySnapshot? snapshot = Find(instanceId);
        if (snapshot == null) return new LiveStrategyCloseStatus(false, 0, 0, 0, 0, false, 0, false);
        _executions.TryGetValue(instanceId, out KgiLiveStrategyExecutionService? execution);
        return new LiveStrategyCloseStatus(
            true,
            snapshot.Position,
            execution?.ActiveBrokerOrderCount ?? 0,
            execution?.QuarantinedBrokerOrderCount ?? 0,
            execution?.ProtocolViolationCount ?? 0,
            (execution?.ProtocolViolationCount ?? 0) > 0,
            execution?.LocalConditionCount ?? 0,
            _pendingClose.ContainsKey(instanceId));
    }

    public bool RequestClose(string instanceId, bool flattenPosition, out string message)
    {
        LiveStrategyCloseStatus status = GetCloseStatus(instanceId);
        if (!status.Exists)
        {
            message = "找不到實盤策略實例。";
            return false;
        }
        if (status.Position != 0 && !flattenPosition)
        {
            message = $"仍有實盤部位 {status.Position} 口，必須確認平倉後才能關閉。";
            return false;
        }

        _pendingClose.TryAdd(instanceId, 0);
        if (flattenPosition || status.ActiveBrokerOrders > 0) _closeFlattenAuthorized.TryAdd(instanceId, 0);
        _host.Pause(instanceId);
        if (_executions.TryGetValue(instanceId, out KgiLiveStrategyExecutionService? execution))
        {
            if (execution.ProtocolViolationCount == 0) execution.CancelAll(instanceId);
        }
        bool closed = TryFinalizeClose(instanceId);
        LiveStrategyCloseStatus after = GetCloseStatus(instanceId);
        closed = closed || (after.Exists && !after.ClosePending && Find(instanceId)?.State == StrategyLifecycleState.Stopped);
        message = closed
            ? "已確認市場委託為 0、部位為 0，策略已關閉。"
            : after.ManualReconciliationRequired
                ? "偵測到成交數量協定異常，已停止自動刪單／平倉；必須人工核對券商委託、成交與實際部位。"
            : after.Position != 0
                ? $"仍有部位 {after.Position} 口，正在等待撤單完成後送出平倉單。"
                : after.QuarantinedBrokerOrders != 0
                    ? $"市場委託與部位已歸零，但仍有 {after.QuarantinedBrokerOrders} 筆 Cancelled 委託等待 late-fill settlement。"
                : $"已送出刪單，等待市場確認；市場委託 {after.ActiveBrokerOrders} 筆。";
        return closed;
    }

    public bool Remove(string instanceId)
    {
        WaitForAdd(instanceId);
        StrategySnapshot? snapshot = Find(instanceId);
        if (snapshot == null || snapshot.Position != 0) return false;
        if (_executions.TryGetValue(instanceId, out KgiLiveStrategyExecutionService? execution) &&
            (execution.GetWorkingOrders(instanceId).Count > 0 || execution.QuarantinedBrokerOrderCount > 0)) return false;
        bool removed = _host.Remove(instanceId);
        if (removed)
        {
            _executions.TryRemove(instanceId, out _);
            _dailyLossLimits.TryRemove(instanceId, out _);
            _riskTriggered.TryRemove(instanceId, out _);
            _activeGenerations.TryRemove(instanceId, out _);
            _pendingRisk.TryRemove(instanceId, out _);
            _pendingClose.TryRemove(instanceId, out _);
            _closeFlattenAuthorized.TryRemove(instanceId, out _);
            _flattenPending.TryRemove(instanceId, out _);
        }
        return removed;
    }

    public IReadOnlyList<StrategySnapshot> GetSnapshots() => _host.GetSnapshots();

    public void Pulse(DateTimeOffset now)
    {
        _host.Pulse(now);
        foreach (string instanceId in _closeRetryNeeded.Keys.ToArray())
        {
            if (_closeRetryNeeded.TryRemove(instanceId, out _)) TryFinalizeClose(instanceId);
        }
        foreach (string instanceId in _pendingRisk.Keys.ToArray())
            if (_activeGenerations.TryGetValue(instanceId, out long generation)) TryProcessPendingRisk(instanceId, generation);
    }

    public IReadOnlyList<string> GetShutdownWarnings()
    {
        var warnings = new List<string>();
        foreach (StrategySnapshot snapshot in _host.GetSnapshots())
        {
            LiveStrategyCloseStatus status = GetCloseStatus(snapshot.InstanceId);
            if (status.Position != 0 || status.ActiveBrokerOrders != 0 || status.QuarantinedBrokerOrders != 0 ||
                status.ManualReconciliationRequired)
                warnings.Add($"{snapshot.InstanceId}：部位 {status.Position} 口／市場委託 {status.ActiveBrokerOrders} 筆／待 late-fill settlement {status.QuarantinedBrokerOrders} 筆／protocol violations {status.ProtocolViolations} 筆（需人工核對券商部位）");
        }
        return warnings;
    }

    private StrategySnapshot? Find(string instanceId) => _host.GetSnapshots().FirstOrDefault(x =>
        string.Equals(x.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase));

    private void OnSnapshot(StrategySnapshot snapshot)
    {
        foreach (Action<StrategySnapshot> handler in (SnapshotChanged?.GetInvocationList() ?? Array.Empty<Delegate>())
                     .Cast<Action<StrategySnapshot>>())
        {
            try { handler(snapshot); }
            catch (Exception exception) { Debug.WriteLine($"Live snapshot subscriber failed: {exception}"); }
        }
        if (_pendingClose.ContainsKey(snapshot.InstanceId)) _closeRetryNeeded.TryAdd(snapshot.InstanceId, 0);
        if (_dailyLossLimits.TryGetValue(snapshot.InstanceId, out decimal limit) && snapshot.NetPnl <= -limit &&
            !_riskTriggered.ContainsKey(snapshot.InstanceId))
        {
            long generation = _addReservations.TryGetValue(snapshot.InstanceId, out AddReservation? reservation)
                ? reservation.Generation
                : _activeGenerations.GetValueOrDefault(snapshot.InstanceId);
            if (generation != 0) QueueRiskStop(snapshot.InstanceId, generation, $"觸發單日最大虧損 ¥{limit}");
        }
    }

    private void WaitForAdd(string instanceId)
    {
        if (!_addReservations.TryGetValue(instanceId, out AddReservation? reservation)) return;
        if (reservation.OwnerThreadId == Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Live Add/Start callback 不可同步操作自己的 partial instance。");
        reservation.Completed.Wait();
    }

    private void QueueRiskStop(string instanceId, long generation, string reason)
    {
        _pendingRisk[instanceId] = new PendingRisk(generation, reason);
        // A failure raised synchronously by a close/flatten submission must not
        // re-enter EmergencyStop and submit another exit on the same call stack.
        if (!_pendingClose.ContainsKey(instanceId)) TryProcessPendingRisk(instanceId, generation);
    }

    private void TryProcessPendingRisk(string instanceId, long generation)
    {
        if (_addReservations.ContainsKey(instanceId) || !_pendingRisk.TryGetValue(instanceId, out PendingRisk? pending) ||
            pending.Generation != generation || !_activeGenerations.TryGetValue(instanceId, out long activeGeneration) ||
            activeGeneration != generation ||
            !_riskProcessing.TryAdd(instanceId, 0)) return;
        try
        {
            if (_pendingClose.ContainsKey(instanceId))
            {
                _riskTriggered.TryAdd(instanceId, 0);
                _pendingRisk.TryRemove(instanceId, out _);
                return;
            }
            if (EmergencyStop(instanceId, pending.Reason))
            {
                _riskTriggered.TryAdd(instanceId, 0);
                _pendingRisk.TryRemove(instanceId, out _);
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Deferred live risk stop failed and remains pending: {exception}");
        }
        finally { _riskProcessing.TryRemove(instanceId, out _); }
    }

    private bool TryFinalizeClose(string instanceId)
    {
        if (!_pendingClose.ContainsKey(instanceId) || !_executions.TryGetValue(instanceId, out KgiLiveStrategyExecutionService? execution))
            return false;
        StrategySnapshot? snapshot = Find(instanceId);
        if (snapshot == null) return false;
        if (execution.ProtocolViolationCount > 0) return false;
        if (execution.ActiveBrokerOrderCount > 0) return false;
        if (snapshot.Position != 0)
        {
            if (!_closeFlattenAuthorized.ContainsKey(instanceId)) return false;
            long token = Interlocked.Increment(ref _flattenToken);
            var attempt = new FlattenAttempt(token, $"flatten-{instanceId}-{token}");
            if (!execution.TryReserveClientOrderId(attempt.ClientOrderId))
            {
                _closeRetryNeeded.TryAdd(instanceId, 0);
                return false;
            }
            if (!_flattenPending.TryAdd(instanceId, attempt))
            {
                execution.ReleaseClientOrderIdReservation(attempt.ClientOrderId);
                return false;
            }
            try { execution.FlattenReserved(snapshot.Position, attempt.ClientOrderId); }
            catch
            {
                TryRemoveFlatten(instanceId, attempt);
                execution.ReleaseClientOrderIdReservation(attempt.ClientOrderId);
                _closeRetryNeeded.TryAdd(instanceId, 0);
                return false;
            }
            return false;
        }
        if (execution.QuarantinedBrokerOrderCount > 0) return false;
        if (execution.LocalConditionCount > 0) execution.CancelAll(instanceId);
        _pendingClose.TryRemove(instanceId, out _);
        _closeFlattenAuthorized.TryRemove(instanceId, out _);
        _flattenPending.TryRemove(instanceId, out _);
        return _host.Stop(instanceId, "市場無委託且部位歸零，實盤策略已關閉");
    }

    private void OnExecutionStateChanged(string instanceId, KgiLiveStrategyExecutionService execution)
    {
        if (_pendingClose.ContainsKey(instanceId) && execution.ActiveBrokerOrderCount == 0)
            _closeRetryNeeded.TryAdd(instanceId, 0);
    }

    private void OnExecutionLifecycleChanged(string instanceId, OrderLifecycleEvent state)
    {
        if (state.State is not (OrderLifecycleState.Filled or OrderLifecycleState.Cancelled or OrderLifecycleState.Rejected) ||
            !_flattenPending.TryGetValue(instanceId, out FlattenAttempt? attempt) ||
            !string.Equals(state.ClientOrderId, attempt.ClientOrderId, StringComparison.OrdinalIgnoreCase)) return;
        if (TryRemoveFlatten(instanceId, attempt)) _closeRetryNeeded.TryAdd(instanceId, 0);
    }

    private bool TryRemoveFlatten(string instanceId, FlattenAttempt attempt) =>
        ((ICollection<KeyValuePair<string, FlattenAttempt>>)_flattenPending)
            .Remove(new KeyValuePair<string, FlattenAttempt>(instanceId, attempt));

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
            foreach (StrategySnapshot snapshot in _host.GetSnapshots().ToArray())
                EmergencyStop(snapshot.InstanceId, "程式關閉，實盤策略緊急停止");
            LiveStrategyCloseStatus[] unsafeStates = _host.GetSnapshots()
                .Select(x => GetCloseStatus(x.InstanceId))
                .Where(x => x.Position != 0 || x.ActiveBrokerOrders != 0 ||
                            x.QuarantinedBrokerOrders != 0 || x.ProtocolViolations != 0 || x.LocalConditions != 0)
                .ToArray();
            if (unsafeStates.Length > 0)
                throw new InvalidOperationException("實盤策略尚有部位、broker 委託或本地條件，禁止釋放服務。");
            _heartbeat?.Dispose();
            timerDrained = true;
            _host.Dispose();
            _host.SnapshotChanged -= OnSnapshot;
            _disposed = true;
        }
        catch (Exception exception)
        {
            if (timerDrained && !_host.IsDisposed && _heartbeat != null) _heartbeat = new StrategyHeartbeatTimer(Pulse);
            if (_host.IsDisposed) _disposed = true;
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

    private sealed class AddReservation
    {
        internal AddReservation(int ownerThreadId, long generation) { OwnerThreadId = ownerThreadId; Generation = generation; }
        internal int OwnerThreadId { get; }
        internal long Generation { get; }
        internal ManualResetEventSlim Completed { get; } = new(false);
    }

    private sealed record PendingRisk(long Generation, string Reason);
    private sealed record FlattenAttempt(long Token, string ClientOrderId);
}
