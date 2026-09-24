using KGIOptionPricing.OrderManagement.Core;
using System.Collections.Concurrent;
using KGIOptionPricing.Strategies.MeanReversionScalper;
using KGIOptionPricing.Strategies.MiniLeadMicro;
using KGIOptionPricing.Strategies.NikkeiMicrostructureAlpha;
using KGIOptionPricing.Strategies.OrderBookTrend;
using KGIOptionPricing.Strategies.PeriodicQueueCycle;
using KGIOptionPricing.Strategies.WaveletScalper;
using KGIOptionPricing.StrategyFramework.Execution;
using KGIOptionPricing.StrategyFramework.MarketData;
using KGIOptionPricing.StrategyFramework.Performance;
using KGIOptionPricing.StrategyFramework.Recovery;
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
    private readonly LiveOrderOwnershipStore? _routeStore;
    private readonly LiveStrategyOrderRouter? _orderRouter;
    private readonly Func<string, IReadOnlyList<LiveStrategyOcoGroupState>> _getOcoStates;
    private readonly Func<bool> _isLoggedIn;
    private readonly Func<string> _account;
    private readonly Func<StrategyDefinition, ResolvedStrategyUniverse> _resolve;
    private readonly Func<StrategyDefinition, ISimulationStrategy> _createStrategy;
    private readonly Func<StrategyDefinition, ResolvedStrategyUniverse, decimal, KgiLiveStrategyExecutionService> _createExecution;
    private readonly Func<StrategyDefinition, ResolvedStrategyUniverse, decimal, long, KgiLiveStrategyExecutionService>? _createExecutionWithGeneration;
    private readonly Action? _afterHostAddFailure;
    private readonly ConcurrentDictionary<string, KgiLiveStrategyExecutionService> _executions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, decimal> _dailyLossLimits = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _riskTriggered = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, PendingRisk> _pendingRisk = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _riskProcessing = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _activeGenerations = new(StringComparer.OrdinalIgnoreCase);
    private long _generationSequence;
    private readonly ConcurrentDictionary<string, byte> _pendingClose = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _manualCloseReconciliationInProgress = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _manualCloseObservationPending = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _closeFlattenAuthorized = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, FlattenAttempt> _flattenPending = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _closeRetryNeeded = new(StringComparer.OrdinalIgnoreCase);
    private long _flattenToken;
    private readonly ConcurrentDictionary<string, AddReservation> _addReservations = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, StrategySnapshot> _deferredAddSnapshots = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;
    private readonly AccountPositionLedger _accountLedger = new();
    private LiveRecoveryState _recoveryState = LiveRecoveryState.RecoveredPaused;
    private string _recoveryReason = "Fresh process has no persisted strategy routes; explicit Resume is required.";

    public event Action<StrategySnapshot>? SnapshotChanged;
    public event Action<ExecutionEvent>? ExecutionOccurred;
    public string Account => _account();
    public bool IsLoggedIn => _isLoggedIn();
    public static IReadOnlyList<IStrategyFactory> DefaultFactories => CreateDefaultFactories();

    internal static IReadOnlyList<IStrategyFactory> CreateDefaultFactories() => new IStrategyFactory[]
    {
        new MeanReversionScalperFactory(),
        new WaveletScalperFactory(),
        new NikkeiMicrostructureAlphaFactory(),
        new MiniLeadMicroFactory(),
        new OrderBookTrendFactory(),
        new PeriodicQueueCycleFactory()
    };

    public static bool TryValidateResearchStrategy(StrategyDefinition definition, out string? reasonCode)
    {
        ArgumentNullException.ThrowIfNull(definition);
        bool isResearchStrategy = string.Equals(definition.StrategyType, MiniLeadMicroFactory.TypeName, StringComparison.OrdinalIgnoreCase);
        if (!isResearchStrategy)
        {
            reasonCode = null;
            return true;
        }
        if (definition.Parameters.TryGetValue("ResearchValidated", out string? raw) &&
            bool.TryParse(raw, out bool validated) && validated)
        {
            reasonCode = null;
            return true;
        }
        reasonCode = "ResearchValidationRequired";
        return false;
    }

    public LiveStrategyService(KGIQuoteService quote, OrderManager orders, KGITradeService trade)
    {
        _orders = orders;
        _isLoggedIn = () => trade.IsLoggedIn;
        _account = () => trade.Account;
        string routeAccountKey = string.Join("_", new[] { trade.BrokerID, trade.Account }
            .Select(value => string.Concat((value ?? string.Empty).Select(character =>
                Path.GetInvalidFileNameChars().Contains(character) ? '_' : character))));
        string routeDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KGIOptionPricing", "Strategy_Routes", routeAccountKey);
        _routeStore = new LiveOrderOwnershipStore(routeDirectory);
        _orderRouter = new LiveStrategyOrderRouter(new OrderManagerLiveStrategyOrderGateway(orders, trade), _routeStore);
        _getOcoStates = instanceId => _routeStore.Replay().OcoGroups
            .Where(state => string.Equals(state.Key.StrategyInstanceId, instanceId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(state => state.UpdatedAt).ToArray();
        LiveOwnershipSnapshot startupJournal = _routeStore.Replay();
        _generationSequence = startupJournal.Routes.Select(route => route.Key.StrategyRunGeneration)
            .Concat(startupJournal.Checkpoints.Select(checkpoint => checkpoint.StrategyRunGeneration))
            .DefaultIfEmpty(0L).Max();
        bool hasUnresolvedStartupOco = startupJournal.OcoGroups.Any(state => state.Phase != LiveStrategyOcoPhase.Completed);
        _recoveryState = startupJournal.IsQuarantined ? LiveRecoveryState.Quarantined :
            startupJournal.Routes.Count > 0 || startupJournal.QuarantinedEvents.Count > 0 || hasUnresolvedStartupOco
                ? LiveRecoveryState.Recovering : LiveRecoveryState.RecoveredPaused;
        _recoveryReason = startupJournal.QuarantineReason.Length > 0 ? startupJournal.QuarantineReason :
            hasUnresolvedStartupOco
                ? $"{startupJournal.OcoGroups.Count(state => state.Phase != LiveStrategyOcoPhase.Completed)} persisted OCO group(s) are unsettled; explicit broker reconciliation is required before resume."
                : _recoveryState == LiveRecoveryState.Recovering ? "Persisted strategy orders/checkpoints require broker snapshot recovery." : _recoveryReason;
        _resolve = definition => _resolver.Resolve(definition.Universe);
        _createStrategy = definition => _registry.Get(definition.StrategyType).Create(definition);
        _createExecution = (definition, universe, commission) =>
        {
            ContractInfo contract = SymbolRepository.Instance.GetHotMonthContract(definition.Universe.Tradable.ProductId)
                ?? throw new InvalidOperationException("找不到 225MC 熱門月合約，禁止實盤啟動。");
            return new KgiLiveStrategyExecutionService(
                _orderRouter.Register(definition.InstanceId, _activeGenerations.GetValueOrDefault(definition.InstanceId, 1L)),
                definition.InstanceId, universe.Tradable, contract, commission);
        };
        _createExecutionWithGeneration = (definition, universe, commission, runGeneration) =>
        {
            ContractInfo contract = SymbolRepository.Instance.GetHotMonthContract(definition.Universe.Tradable.ProductId)
                ?? throw new InvalidOperationException("找不到 225MC 熱門月合約，禁止實盤啟動。");
            return new KgiLiveStrategyExecutionService(
                _orderRouter.Register(definition.InstanceId, runGeneration), definition.InstanceId,
                universe.Tradable, contract, commission);
        };
        _afterHostAddFailure = null;
        foreach (IStrategyFactory factory in CreateDefaultFactories()) _registry.Register(factory);
        _host = new StrategyHost(new KgiStrategyMarketDataSource(quote));
        _host.SnapshotChanged += OnSnapshot;
        _host.ExecutionOccurred += OnExecutionCheckpoint;
        _host.OrderLifecycleOccurred += OnLifecycleCheckpoint;
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
        Action? afterHostAddFailure = null,
        Func<string, IReadOnlyList<LiveStrategyOcoGroupState>>? getOcoStates = null)
    {
        _orders = null!;
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _isLoggedIn = isLoggedIn ?? throw new ArgumentNullException(nameof(isLoggedIn));
        _account = account ?? throw new ArgumentNullException(nameof(account));
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        _createStrategy = createStrategy ?? throw new ArgumentNullException(nameof(createStrategy));
        _createExecution = createExecution ?? throw new ArgumentNullException(nameof(createExecution));
        _getOcoStates = getOcoStates ?? (_ => Array.Empty<LiveStrategyOcoGroupState>());
        _createExecutionWithGeneration = null;
        _afterHostAddFailure = afterHostAddFailure;
        _host.SnapshotChanged += OnSnapshot;
        _host.ExecutionOccurred += OnExecutionCheckpoint;
        _host.OrderLifecycleOccurred += OnLifecycleCheckpoint;
        _heartbeat = enableHeartbeat ? new StrategyHeartbeatTimer(Pulse) : null;
    }

    public void Add(StrategyDefinition definition, decimal commissionPerContract, decimal dailyLossLimit)
    {
        if (_recoveryState != LiveRecoveryState.RecoveredPaused)
            throw new InvalidOperationException($"策略回補尚未完成或需人工對帳，禁止新增實盤策略。[{_recoveryState}: {_recoveryReason}]");
        if (!_isLoggedIn()) throw new InvalidOperationException("交易服務尚未登入，禁止啟動實盤策略。");
        if (!TryValidateResearchStrategy(definition, out string? researchReason))
            throw new InvalidOperationException($"{definition.StrategyType} 尚屬研究策略；未明確標記已通過樣本外、滑價、延遲與模擬盤門檻，禁止實盤啟動。[{researchReason}]");
        if (!string.Equals(definition.Universe.Tradable.ProductId, "225MC", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("實盤策略第一版僅允許交易商品 225MC。");
        var reservation = new AddReservation(Environment.CurrentManagedThreadId, Interlocked.Increment(ref _generationSequence));
        bool added = false;
        bool retainedAfterFailedAdd = false;
        if (!_addReservations.TryAdd(definition.InstanceId, reservation))
            throw new InvalidOperationException($"實盤策略實例正在加入：{definition.InstanceId}");
        try
        {
            if (_executions.ContainsKey(definition.InstanceId) ||
                _host.GetSnapshots().Any(x => string.Equals(x.InstanceId, definition.InstanceId, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"實盤策略實例已存在：{definition.InstanceId}");

            ResolvedStrategyUniverse universe = _resolve(definition);
            var execution = _createExecutionWithGeneration?.Invoke(definition, universe, commissionPerContract, reservation.Generation)
                ?? _createExecution(definition, universe, commissionPerContract);
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
                _host.Add(_createStrategy(definition), universe, commissionPerContract, execution, reservation.Generation);
                PersistRecoveryCheckpoints();
                added = true;
            }
            catch (Exception addError)
            {
                Exception? hookError = null;
                try { _afterHostAddFailure?.Invoke(); }
                catch (Exception exception) { hookError = exception; }
                // Host retaining the runner is the single owner of this executor.
                // Never race an independent cleanup against its ledger/routes.
                retainedAfterFailedAdd = _host.GetSnapshots().Any(x =>
                    string.Equals(x.InstanceId, definition.InstanceId, StringComparison.OrdinalIgnoreCase));
                if (retainedAfterFailedAdd)
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
            if ((added || retainedAfterFailedAdd) &&
                _deferredAddSnapshots.TryRemove(definition.InstanceId, out StrategySnapshot? snapshot))
                PublishSnapshot(snapshot);
            else if (!added)
                _deferredAddSnapshots.TryRemove(definition.InstanceId, out _);
            TryProcessPendingRisk(definition.InstanceId, reservation.Generation);
        }
    }

    public bool Pause(string instanceId)
    {
        WaitForAdd(instanceId);
        StrategySnapshot? snapshot = Find(instanceId);
        return snapshot != null && _host.Pause(instanceId);
    }

    public bool Resume(string instanceId)
    {
        WaitForAdd(instanceId);
        if (Find(instanceId) is null) return false;
        if (HasUnresolvedOcoState(instanceId))
        {
            _recoveryState = LiveRecoveryState.Recovering;
            _recoveryReason = "Persisted OCO state is not terminal; reconcile broker orders and fills before resuming.";
            foreach (StrategySnapshot snapshot in _host.GetSnapshots()) PublishSnapshot(snapshot);
            return false;
        }
        if (_recoveryState != LiveRecoveryState.RecoveredPaused) return false;
        return _isLoggedIn() && _host.Resume(instanceId);
    }

    public LiveRecoveryState RecoveryState => _recoveryState;
    public string RecoveryReason => _recoveryReason;

    /// <summary>Applies an explicit broker snapshot; never guesses ownership from product or net position.</summary>
    public LiveRecoveryResult Recover(BrokerRecoverySnapshot brokerSnapshot)
    {
        if (_routeStore is null) throw new InvalidOperationException("Recovery journal is unavailable in this service instance.");
        _recoveryState = LiveRecoveryState.Recovering;
        var coordinator = new LiveStrategyRecoveryCoordinator(_routeStore, _accountLedger, _host.Ledger);
        string account = _account();
        PositionSnapshot[] strategyPositions = brokerSnapshot.Positions
            .SelectMany(position => _host.Ledger.GetAllPositions(position.Symbol)).ToArray();
        LiveRecoveryResult result = coordinator.Recover(account, brokerSnapshot, strategyPositions);
        LiveStrategyOcoGroupState[] unresolvedOco = _routeStore.Replay().OcoGroups
            .Where(state => state.Phase != LiveStrategyOcoPhase.Completed).ToArray();
        if (unresolvedOco.Length > 0)
        {
            result = result with
            {
                State = LiveRecoveryState.Recovering,
                Reason = $"{unresolvedOco.Length} persisted OCO group(s) remain unsettled; broker orders/fills require explicit reconciliation before resume."
            };
        }
        if (result.State == LiveRecoveryState.RecoveredPaused && _routeStore.Replay().Routes.Count > 0)
        {
            result = result with
            {
                State = LiveRecoveryState.Recovering,
                Reason = "Broker orders/positions reconciled, but persisted strategy instances and checkpoints have not yet been restored; automatic trading remains locked."
            };
        }
        _recoveryState = result.State;
        _recoveryReason = result.Reason;
        foreach (StrategySnapshot snapshot in _host.GetSnapshots()) PublishSnapshot(snapshot);
        return result;
    }

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

    public bool ReconcileExternallyCancelledOrders(string instanceId, out string message)
    {
        LiveStrategyCloseStatus status = GetCloseStatus(instanceId);
        if (!status.Exists)
        {
            message = "找不到實盤策略實例。";
            return false;
        }
        if (!status.ClosePending)
        {
            message = "請先使用「關閉／平倉」停止策略訊號並進入關閉流程，再執行人工核對。";
            return false;
        }
        if (status.ManualReconciliationRequired)
        {
            message = "偵測到成交數量協定異常；不得以人工刪單同步取代券商成交／部位核對。";
            return false;
        }
        if (!_executions.TryGetValue(instanceId, out KgiLiveStrategyExecutionService? execution))
        {
            message = "找不到實盤執行器，無法同步人工刪單。";
            return false;
        }

        if (!_manualCloseReconciliationInProgress.TryAdd(instanceId, 0))
        {
            message = "人工刪單同步正在進行，請等待本次核對完成。";
            return false;
        }
        try
        {
            int reconciled;
            try { reconciled = execution.ReconcileExternallyCancelledOrders(); }
            catch (BrokerReconciliationRequiredException exception)
            {
                message = exception.Message;
                return false;
            }
            if (reconciled == 0)
            {
                message = "本機已無待同步的市場委託；請重新確認券商委託與實際部位。";
                return false;
            }

            _manualCloseObservationPending.TryAdd(instanceId, 0);
            LiveStrategyCloseStatus after = GetCloseStatus(instanceId);
            message = after.Position != 0
                ? $"已依人工核對同步 {reconciled} 筆刪單；仍有部位 {after.Position} 口，先進入 late-fill 觀察，完成後才會送出平倉單。"
                : after.QuarantinedBrokerOrders != 0
                    ? $"已依人工核對同步 {reconciled} 筆刪單；需等待 late-fill 觀察期完成後才可關閉。"
                    : "已同步人工刪單，策略已完成安全關閉。";
            return true;
        }
        finally { _manualCloseReconciliationInProgress.TryRemove(instanceId, out _); }
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
            _manualCloseReconciliationInProgress.TryRemove(instanceId, out _);
            _manualCloseObservationPending.TryRemove(instanceId, out _);
            _closeFlattenAuthorized.TryRemove(instanceId, out _);
            _flattenPending.TryRemove(instanceId, out _);
        }
        return removed;
    }

    public IReadOnlyList<StrategySnapshot> GetSnapshots() => _host.GetSnapshots();

    public IReadOnlyList<LiveStrategyOcoGroupState> GetOcoStates(string instanceId) => _getOcoStates(instanceId);

    public bool HasUnresolvedOcoState(string instanceId) => GetOcoStates(instanceId)
        .Any(state => state.Phase != LiveStrategyOcoPhase.Completed);

    public void Pulse(DateTimeOffset now)
    {
        _host.Pulse(now);
        foreach (string instanceId in _manualCloseObservationPending.Keys.ToArray())
        {
            if (_executions.TryGetValue(instanceId, out KgiLiveStrategyExecutionService? execution) &&
                execution.ManualReconciliationObservationCount == 0)
            {
                _manualCloseObservationPending.TryRemove(instanceId, out _);
                TryFinalizeClose(instanceId);
            }
        }
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
        // Strategy.Start publishes snapshots while Add owns a lifecycle lease.
        // Subscribers such as the live grid are not allowed to query a partial
        // instance, so retain only the latest initial state until Add completes.
        if (_addReservations.ContainsKey(snapshot.InstanceId))
        {
            _deferredAddSnapshots[snapshot.InstanceId] = snapshot;
            return;
        }
        PublishSnapshot(snapshot);
    }

    private void PublishSnapshot(StrategySnapshot snapshot)
    {
        snapshot = snapshot with
        {
            StrategyRunGeneration = _activeGenerations.GetValueOrDefault(snapshot.InstanceId),
            RecoveryState = _recoveryState.ToString(),
            RecoveryReason = _recoveryReason,
            OcoStatusSummary = string.Join(" | ", GetOcoStates(snapshot.InstanceId).Take(4).Select(state =>
                $"g{state.Key.StrategyRunGeneration}/{state.Key.CycleId}:{state.Phase}" +
                (state.PendingCancelClientOrderId.Length > 0 ? $" cancel={state.PendingCancelClientOrderId}" : string.Empty) +
                (state.StopMarketClientOrderId.Length > 0 ? $" stop={state.StopMarketClientOrderId}" : string.Empty) +
                $" fill={state.DeliveredFilledQuantity}/{state.BrokerFinalCumulativeFilledQuantity?.ToString() ?? "?"}" +
                (state.Reason.Length > 0 ? $" {state.Reason}" : string.Empty)))
        };
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

    private void OnExecutionCheckpoint(ExecutionEvent execution)
    {
        ExecutionOccurred?.Invoke(execution);
        PersistRecoveryCheckpoints();
    }

    private void OnLifecycleCheckpoint(OrderLifecycleEvent lifecycle) => PersistRecoveryCheckpoints();

    private void PersistRecoveryCheckpoints()
    {
        if (_routeStore is null || _recoveryState == LiveRecoveryState.Quarantined) return;
        try
        {
            foreach (StrategyRecoveryCheckpoint checkpoint in _host.CaptureRecoveryCheckpoints())
                _routeStore.AppendCheckpoint(checkpoint);
        }
        catch (Exception exception)
        {
            _recoveryState = LiveRecoveryState.Quarantined;
            _recoveryReason = $"Strategy recovery checkpoint persistence failed: {exception.Message}";
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
        if (_manualCloseReconciliationInProgress.ContainsKey(instanceId)) return false;
        if (_manualCloseObservationPending.ContainsKey(instanceId))
        {
            if (execution.ManualReconciliationObservationCount > 0) return false;
            _manualCloseObservationPending.TryRemove(instanceId, out _);
        }
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
        StrategySnapshot? current = null;
        try { current = Find(instanceId); }
        catch (ObjectDisposedException) { /* Host disposal can raise a final executor state callback. */ }
        if (current != null) PublishSnapshot(current);
        if (_pendingClose.ContainsKey(instanceId) && execution.ActiveBrokerOrderCount == 0 &&
            execution.ManualReconciliationObservationCount == 0)
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
            _orderRouter?.Dispose();
            _routeStore?.Dispose();
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
