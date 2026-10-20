using KGIOptionPricing.OrderManagement.Core;
using System.Collections.Concurrent;
using KGIOptionPricing.Strategies.MeanReversionScalper;
using KGIOptionPricing.Strategies.NikkeiMicrostructureAlpha;
using KGIOptionPricing.Strategies.OrderBookTrend;
using KGIOptionPricing.Strategies.PeriodicQueueCycle;
using KGIOptionPricing.Strategies.WaveletScalper;
using KGIOptionPricing.StrategyFramework.Execution;
using KGIOptionPricing.StrategyFramework.MarketData;
using KGIOptionPricing.StrategyFramework.Performance;
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
    bool ClosePending,
    bool OperatorCloseRequested = false,
    string CloseReason = "");

public sealed class LiveStrategyService : IDisposable
{
    private static readonly log4net.ILog Log = log4net.LogManager.GetLogger(typeof(LiveStrategyService));
    private readonly ConcurrentDictionary<string,(Storage.SimulationRunRecorder Recorder,RunManifest Manifest)> _flowRecorders = new(StringComparer.OrdinalIgnoreCase);
    private string? _liveArchivePath;
    private readonly ConcurrentDictionary<string,byte> _powerRecordingFinalization=new();
    private readonly ConcurrentDictionary<string,Strategies.PowerReversal.PowerReversalStrategy> _powerRecordingStrategies=new();
    private readonly ConcurrentDictionary<string,Strategies.MultiFactorMeanR17.MultiFactorMeanR17Strategy> _r17RecordingStrategies=new();
    private readonly ConcurrentDictionary<string,Strategies.NikkeiRlDirection.NikkeiRlStrategy> _rlRecordingStrategies=new();
    private readonly ConcurrentDictionary<string,string> _powerRecordingErrors=new();
    private void RecordLive(string id,Action<Storage.SimulationRunRecorder> write)
    {
        if(_powerRecordingErrors.ContainsKey(id))return;
        if(!_flowRecorders.TryGetValue(id,out var recording))return;
        try{write(recording.Recorder);}
        catch(Exception error){
            if(_powerRecordingStrategies.TryGetValue(id,out var strategy))RecordPowerFailure(id,strategy,error);
            else if(_r17RecordingStrategies.TryGetValue(id,out var r17))RecordR17Failure(id,r17,error);
            else if(_rlRecordingStrategies.TryGetValue(id,out var rl)){
                if(_powerRecordingErrors.TryAdd(id,error.Message)){Log.Error("日經RL執行紀錄保存異常："+id,error);rl.NotifyRecordingFailure(error);}
            }else throw;
        }
    }
    private void RecordPowerFailure(string id,Strategies.PowerReversal.PowerReversalStrategy strategy,Exception error)
    {
        if(!_powerRecordingErrors.TryAdd(id,error.Message))return;
        Log.Error("PowerReversal 執行紀錄保存異常："+id,error);
        strategy.NotifyRecordingFailure(error);
    }
    private void RecordR17Failure(string id,Strategies.MultiFactorMeanR17.MultiFactorMeanR17Strategy strategy,Exception error)
    {
        if(!_powerRecordingErrors.TryAdd(id,error.Message))return;
        Log.Error("多因子均價回歸R17執行紀錄保存異常："+id,error);
        strategy.NotifyRecordingFailure(error);
    }
    private void BeginFlowRecording(ISimulationStrategy strategy, StrategyDefinition definition, ResolvedStrategyUniverse universe, decimal commission, decimal loss)
    {
        if(strategy is not IStrategyDecisionSource)return;
        IReadOnlyDictionary<string,string> effective;
        if(strategy is Strategies.TradeFlowTrend.TradeFlowTrendStrategy)effective=Strategies.TradeFlowTrend.TradeFlowParameters.Parse(definition.Parameters).ToDictionary();
        else if(strategy is Strategies.PowerReversal.PowerReversalStrategy)effective=Strategies.PowerReversal.PowerReversalParameters.Parse(definition.Parameters).ToDictionary();
        else if(strategy is Strategies.MultiFactorMeanR17.MultiFactorMeanR17Strategy)effective=Strategies.MultiFactorMeanR17.R17Parameters.Parse(definition.Parameters).ToDictionary();
        else if(strategy is Strategies.NikkeiRlDirection.NikkeiRlStrategy)effective=Strategies.NikkeiRlDirection.NikkeiRlParameters.Parse(definition.Parameters).ToDictionary();
        else return;
        var parameters=new SortedDictionary<string,string>(StringComparer.Ordinal);
        foreach(var pair in effective)parameters.Add(pair.Key,pair.Value);
        string normalized=System.Text.Json.JsonSerializer.Serialize(parameters);
        string runId=$"live-flow-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        var recorder=new Storage.SimulationRunRecorder(Path.Combine(AppContext.BaseDirectory,"StrategyLiveRuns",runId),summaryOnly:true);
        var manifest=new RunManifest{RunId=runId,StrategyType=definition.StrategyType,InstanceId=definition.InstanceId,
            StartedAt=DateTimeOffset.UtcNow,Mode=StrategyRunMode.LiveTrading,RawParameters=definition.Parameters,EffectiveParameters=parameters,
            NormalizedParameters=normalized,ParameterSha256=strategy is Strategies.TradeFlowTrend.TradeFlowTrendStrategy ? Strategies.TradeFlowTrend.TradeFlowParameterStore.Hash(Strategies.TradeFlowTrend.TradeFlowParameters.Parse(definition.Parameters)) : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalized))),CommissionPerContract=commission,
            Universe=universe.AllProducts.Select(x=>new RunProductIdentity(x.ProductId,x.Symbol,x.ContractMonth,x.TickSize,x.ContractMultiplier)).ToArray(),
            InputArchive=_liveArchivePath==null?null:new InputArchiveIdentity{AbsolutePath=_liveArchivePath,MissingFields=new[]{"Sha256","ExecutionBoundary"}},
            ExecutionAssumptions=new Dictionary<string,string>{{"fillModel",strategy is Strategies.PowerReversal.PowerReversalStrategy or Strategies.NikkeiRlDirection.NikkeiRlStrategy ? "local strict-cross entry; broker market IOC" : "broker reported limit IOC/ROD"},{"dailyLossLimit",loss.ToString(System.Globalization.CultureInfo.InvariantCulture)},{"latencyModel","broker reports; unavailable timestamps remain missing"}},
            MissingProvenanceFields=new[]{"InputArchiveFinalFingerprint","ExchangeSequence","BrokerFullWorkingOrderQueryCompletion"}};
        recorder.PrepareManifest(manifest);
        if(!_flowRecorders.TryAdd(definition.InstanceId,(recorder,manifest))){recorder.Dispose();throw new InvalidOperationException("Live recorder already exists");}
        if(strategy is Strategies.PowerReversal.PowerReversalStrategy power)_powerRecordingStrategies[definition.InstanceId]=power;
        if(strategy is Strategies.MultiFactorMeanR17.MultiFactorMeanR17Strategy r17)_r17RecordingStrategies[definition.InstanceId]=r17;
        if(strategy is Strategies.NikkeiRlDirection.NikkeiRlStrategy rl)_rlRecordingStrategies[definition.InstanceId]=rl;
        ((IStrategyDecisionSource)strategy).DecisionObserved+=observation=>RecordLive(definition.InstanceId,r=>r.Record(observation));
    }
    private void FinalizeFlowRecording(string id)
    {
        if(!_flowRecorders.TryRemove(id,out var value))return;
        try{var now=DateTimeOffset.UtcNow;value.Recorder.FinalizeManifest(value.Manifest with{EndedAt=now});value.Recorder.FinalizeSummary(now);}
        catch(Exception error){
            if(_powerRecordingStrategies.TryGetValue(id,out var strategy))RecordPowerFailure(id,strategy,error);
            else if(_r17RecordingStrategies.TryGetValue(id,out var r17))RecordR17Failure(id,r17,error);
            else if(_rlRecordingStrategies.TryGetValue(id,out var rl)){
                if(_powerRecordingErrors.TryAdd(id,error.Message)){Log.Error("日經RL執行紀錄完成保存異常："+id,error);rl.NotifyRecordingFailure(error);}
            }else throw;
        }
        finally{value.Recorder.Dispose();}
    }
    private static StrategyExecutionCapabilities FlowCapabilities(StrategyDefinition definition) =>
        definition.StrategyType == Strategies.TradeFlowTrend.TradeFlowTrendFactory.TypeName
            ? new(Strategies.TradeFlowTrend.TradeFlowParameters.Parse(definition.Parameters).Quantity, true)
            : definition.StrategyType == MeanReversionScalperFactory.TypeName
                ? new(AllowPairedRestingLive: true)
                : new();
    private readonly StrategyRegistry _registry = new();
    private readonly StrategyProductResolver _resolver = new();
    private readonly StrategyHost _host;
    private StrategyHeartbeatTimer? _heartbeat;
    private readonly object _disposeSync = new();
    private DisposeAttempt? _disposeAttempt;
    private readonly OrderManager _orders;
    private readonly LiveOrderOwnershipRegistry? _routeRegistry;
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
    private long _generationSequence = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private readonly ConcurrentDictionary<string, byte> _pendingClose = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _closeReasons = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _operatorClose = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _operatorDelete = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _manualCloseReconciliationInProgress = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _manualCloseObservationPending = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _closeFlattenAuthorized = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, FlattenAttempt> _flattenPending = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _closeRetryNeeded = new(StringComparer.OrdinalIgnoreCase);
    private long _flattenToken;
    private readonly ConcurrentDictionary<string, AddReservation> _addReservations = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, StrategySnapshot> _deferredAddSnapshots = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public event Action<StrategySnapshot>? SnapshotChanged;
    public event Action<ExecutionEvent>? ExecutionOccurred;
    public event Action<string>? StrategyRetired;
    public string Account => _account();
    public bool IsLoggedIn => _isLoggedIn();
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

    public static bool TryValidateResearchStrategy(StrategyDefinition definition, out string? reasonCode)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.StrategyType == Strategies.NikkeiRlDirection.NikkeiRlFactory.TypeName)
        {
            if(!Strategies.NikkeiRlDirection.NikkeiRlParameters.Parse(definition.Parameters).LiveTradingEnabled)
            {reasonCode="NikkeiRlLiveOptInRequired";return false;}
        }
        reasonCode = null;
        return true;
    }

    public LiveStrategyService(KGIQuoteService quote, OrderManager orders, KGITradeService trade)
    {
        _orders = orders;
        _isLoggedIn = () => trade.IsLoggedIn;
        _account = () => trade.Account;
        // User-managed broker reconciliation: every application start is a fresh session,
        // including after an abnormal exit. Never restore prior-process strategy state.
        _routeRegistry = new LiveOrderOwnershipRegistry(sessionOnly: true);
        _orderRouter = new LiveStrategyOrderRouter(new OrderManagerLiveStrategyOrderGateway(orders, trade), _routeRegistry);
        _getOcoStates = instanceId => _routeRegistry.Snapshot().OcoGroups
            .Where(state => string.Equals(state.Key.StrategyInstanceId, instanceId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(state => state.UpdatedAt).ToArray();
        _resolve = definition => _resolver.Resolve(definition.Universe);
        _createStrategy = definition => _registry.Get(definition.StrategyType).Create(definition);
        _createExecution = (definition, universe, commission) =>
        {
            ContractInfo contract = SymbolRepository.Instance.GetHotMonthContract(definition.Universe.Tradable.ProductId)
                ?? throw new InvalidOperationException("找不到 225MC 熱門月合約，禁止實盤啟動。");
            return new KgiLiveStrategyExecutionService(
                _orderRouter.Register(definition.InstanceId, _activeGenerations.GetValueOrDefault(definition.InstanceId, 1L)),
                definition.InstanceId, universe.Tradable, contract, commission, capabilities: FlowCapabilities(definition),
                strategyNetPosition: generation => _host.Ledger.GetPosition(
                    definition.InstanceId, generation, universe.Tradable.Symbol).NetQuantity,
                strategyRunGeneration: _activeGenerations.GetValueOrDefault(definition.InstanceId, 1L));
        };
        _createExecutionWithGeneration = (definition, universe, commission, runGeneration) =>
        {
            ContractInfo contract = SymbolRepository.Instance.GetHotMonthContract(definition.Universe.Tradable.ProductId)
                ?? throw new InvalidOperationException("找不到 225MC 熱門月合約，禁止實盤啟動。");
            return new KgiLiveStrategyExecutionService(
                _orderRouter.Register(definition.InstanceId, runGeneration), definition.InstanceId,
                universe.Tradable, contract, commission, capabilities: FlowCapabilities(definition),
                strategyNetPosition: generation => _host.Ledger.GetPosition(
                    definition.InstanceId, generation, universe.Tradable.Symbol).NetQuantity,
                strategyRunGeneration: runGeneration);
        };
        _afterHostAddFailure = null;
        foreach (IStrategyFactory factory in CreateDefaultFactories()) _registry.Register(factory);
        var archive = new Storage.MarketDataArchiveWriter(Path.Combine(AppContext.BaseDirectory,"StrategyLiveArchive"),"live-"+Guid.NewGuid().ToString("N"),enabled:false);
        _liveArchivePath = archive.Enabled?archive.EventFilePath:null;
        _host = new StrategyHost(new Storage.RecordingMarketEventSource(new KgiStrategyMarketDataSource(quote),archive));
        _host.SnapshotChanged += OnSnapshot;
        _host.ExecutionOccurred += OnExecution;
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
        Func<string, IReadOnlyList<LiveStrategyOcoGroupState>>? getOcoStates = null,
        LiveOrderOwnershipRegistry? routeRegistry = null)
    {
        _orders = null!;
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _routeRegistry = routeRegistry;
        _isLoggedIn = isLoggedIn ?? throw new ArgumentNullException(nameof(isLoggedIn));
        _account = account ?? throw new ArgumentNullException(nameof(account));
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        _createStrategy = createStrategy ?? throw new ArgumentNullException(nameof(createStrategy));
        _createExecution = createExecution ?? throw new ArgumentNullException(nameof(createExecution));
        _getOcoStates = getOcoStates ?? (_ => Array.Empty<LiveStrategyOcoGroupState>());
        _createExecutionWithGeneration = null;
        _afterHostAddFailure = afterHostAddFailure;
        _host.SnapshotChanged += OnSnapshot;
        _host.ExecutionOccurred += OnExecution;
        _heartbeat = enableHeartbeat ? new StrategyHeartbeatTimer(Pulse) : null;
    }

    public void Add(StrategyDefinition definition, decimal commissionPerContract, decimal dailyLossLimit)
    {
        Strategies.PowerReversal.PowerReversalParameters.EnsureCommission(definition,commissionPerContract);
        Strategies.MultiFactorMeanR17.R17Parameters.EnsureCommission(definition,commissionPerContract);
        if (!_isLoggedIn()) throw new InvalidOperationException("交易服務尚未登入，禁止啟動實盤策略。");
        if (!TryValidateResearchStrategy(definition, out string? researchReason))
            throw new InvalidOperationException($"{definition.StrategyType} 尚未明確啟用實盤交易，禁止啟動。[{researchReason}]");
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
            execution.Failed += reason => QueueRiskStop(definition.InstanceId, reservation.Generation, reason, "ExecutionFailure");
            execution.StateChanged += () => OnExecutionStateChanged(definition.InstanceId, execution);
            execution.OrderLifecycleChanged += state => { RecordLive(definition.InstanceId,r=>r.Record(state)); OnExecutionLifecycleChanged(definition.InstanceId, state); };
            if (!_executions.TryAdd(definition.InstanceId, execution))
            {
                execution.Dispose();
                throw new InvalidOperationException($"實盤策略實例已存在：{definition.InstanceId}");
            }
            _dailyLossLimits[definition.InstanceId] = Math.Max(1m, dailyLossLimit);
            _activeGenerations[definition.InstanceId] = reservation.Generation;
            try
            {
                var strategy = _createStrategy(definition);
                if(strategy is Strategies.TradeFlowTrend.TradeFlowTrendStrategy flow && _routeRegistry != null)
                {
                    if(_routeRegistry.Snapshot().StrategyCheckpoints.Any(c => c.StrategyInstanceId==definition.InstanceId))
                        throw new InvalidOperationException("此實例有歷史 checkpoint；需核对券商工作單、成交與部位，禁止直接重啟新世代。");
                    flow.CheckpointChanged += _routeRegistry.UpdateStrategyCheckpoint;
                }
                BeginFlowRecording(strategy,definition,universe,commissionPerContract,dailyLossLimit);
                _host.Add(strategy, universe, commissionPerContract, execution, reservation.Generation);
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
        return _isLoggedIn() && _host.Resume(instanceId);
    }

    public bool EmergencyStop(string instanceId, string reason = "使用者停止實盤策略")
        => EmergencyStopCore(instanceId, reason, "EmergencyStop");

    private bool EmergencyStopCore(string instanceId, string reason, string source)
    {
        WaitForAdd(instanceId);
        StrategySnapshot? snapshot = Find(instanceId);
        if (snapshot == null) return false;
        if (_operatorClose.ContainsKey(instanceId)) return RequestOperatorClose(instanceId, out _);
        if (_pendingClose.TryAdd(instanceId, 0)) LogCloseRequested(instanceId, source, reason, snapshot);
        _closeReasons.TryAdd(instanceId, reason);
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
            _pendingClose.ContainsKey(instanceId),
            _operatorClose.ContainsKey(instanceId),
            _closeReasons.GetValueOrDefault(instanceId, string.Empty));
    }

    public bool RequestClose(string instanceId, bool flattenPosition, out string message)
    {
        if (_operatorClose.ContainsKey(instanceId)) return RequestOperatorClose(instanceId, out message);
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

        if (_pendingClose.TryAdd(instanceId, 0))
            LogCloseRequested(instanceId, "UserClose", "使用者要求關閉策略", Find(instanceId));
        _closeReasons.TryAdd(instanceId, "使用者要求關閉策略");
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

    public bool RequestOperatorClose(string instanceId, out string message)
    {
        LiveStrategyCloseStatus status = GetCloseStatus(instanceId);
        if (!status.Exists || !_executions.TryGetValue(instanceId, out KgiLiveStrategyExecutionService? execution))
        {
            message = "找不到實盤策略實例或執行器。";
            return false;
        }

        // The executor gate serializes this mode transition with broker
        // placements. An order that already crossed the boundary stays active
        // until its own terminal broker report arrives.
        execution.BeginOperatorClose();
        _operatorClose.TryAdd(instanceId, 0);
        _closeFlattenAuthorized.TryRemove(instanceId, out _);
        if (_pendingClose.TryAdd(instanceId, 0))
            LogCloseRequested(instanceId, "OperatorClose", "使用者要求關閉策略（人工對帳）", Find(instanceId));
        _closeReasons.TryAdd(instanceId, "使用者要求關閉策略（人工對帳）");
        _host.Pause(instanceId);
        if (execution.ProtocolViolationCount == 0) execution.CancelAll(instanceId);
        bool closed = TryFinalizeOperatorClose(instanceId);
        LiveStrategyCloseStatus after = GetCloseStatus(instanceId);
        message = closed
            ? $"已確認該策略市場委託與本機條件為 0，策略已停止；帳本部位 {after.Position} 口待人工對帳，不自動平倉。"
            : after.ManualReconciliationRequired
                ? "券商回報協定異常，委託狀態待人工核對；不可視為已撤單。"
                : $"已停止新單並要求撤銷所屬委託；市場有效委託 {after.ActiveBrokerOrders} 筆，本機條件 {after.LocalConditions} 筆，帳本部位 {after.Position} 口（不自動平倉）。";
        return closed;
    }

    public bool RequestOperatorDelete(string instanceId, out string message)
    {
        if (!GetCloseStatus(instanceId).Exists)
        {
            message = "找不到實盤策略實例。";
            return false;
        }
        _operatorDelete.TryAdd(instanceId, 0);
        if (!RequestOperatorClose(instanceId, out message))
        {
            message += " 撤單確認後將自動從活動列表移除；不會平倉。";
            return false;
        }
        return RetireOperatorClosed(instanceId, out message);
    }

    public IReadOnlyList<string> GetPriorProcessSymbols() => _routeRegistry?.Snapshot().Routes.Where(x =>
        string.Equals(x.Account, Account, StringComparison.OrdinalIgnoreCase) &&
        x.SubmissionState == StrategySubmissionState.Unknown && !x.PriorProcessReconciled &&
        x.Reason == LiveOrderOwnershipRegistry.PriorProcessReason)
        .Select(x => x.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray()
        ?? Array.Empty<string>();

    public IReadOnlyList<LiveOrderOwnerKey> GetPriorProcessOrders(string instanceId)
    {
        StrategySnapshot? snapshot = Find(instanceId);
        return snapshot == null ? Array.Empty<LiveOrderOwnerKey>() : GetPriorProcessOrdersForSymbol(snapshot.TradableSymbol);
    }

    public IReadOnlyList<LiveOrderOwnerKey> GetPriorProcessOrdersForSymbol(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol) || _routeRegistry == null) return Array.Empty<LiveOrderOwnerKey>();
        return _routeRegistry.Snapshot().Routes.Where(x =>
            string.Equals(x.Account, Account, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Symbol, symbol, StringComparison.OrdinalIgnoreCase) &&
            x.SubmissionState == StrategySubmissionState.Unknown && !x.PriorProcessReconciled &&
            x.Reason == LiveOrderOwnershipRegistry.PriorProcessReason).Select(x => x.Key).ToArray();
    }

    public bool ConfirmPriorProcessOrders(string instanceId, IReadOnlyList<LiveOrderOwnerKey> expectedOwners, out string message)
    {
        StrategySnapshot? snapshot = Find(instanceId);
        if (snapshot == null)
        {
            message = "找不到策略或歸屬紀錄。";
            return false;
        }
        return ConfirmPriorProcessOrdersForSymbol(snapshot.TradableSymbol, expectedOwners, out message);
    }

    public bool ConfirmPriorProcessOrdersForSymbol(string symbol, IReadOnlyList<LiveOrderOwnerKey> expectedOwners, out string message)
    {
        if (string.IsNullOrWhiteSpace(symbol) || string.IsNullOrWhiteSpace(Account) || _routeRegistry == null)
        {
            message = "找不到帳號、合約或歸屬紀錄。";
            return false;
        }
        foreach (StrategySnapshot sameProduct in _host.GetSnapshots().Where(x =>
                     string.Equals(x.TradableSymbol, symbol, StringComparison.OrdinalIgnoreCase)))
        {
            LiveStrategyCloseStatus status = GetCloseStatus(sameProduct.InstanceId);
            if (sameProduct.State != StrategyLifecycleState.Stopped || status.Position != 0 ||
                status.ActiveBrokerOrders != 0 || status.LocalConditions != 0 ||
                status.QuarantinedBrokerOrders != 0 || status.ManualReconciliationRequired)
            {
                message = "請先關閉同商品策略，確認目前策略部位、有效委託及待回報均為零，再核對舊程序紀錄。";
                return false;
            }
        }
        if (expectedOwners.Any(x => _activeGenerations.TryGetValue(x.StrategyInstanceId, out long generation) &&
                                    generation == x.StrategyRunGeneration))
        {
            message = "核對清單包含本次執行的策略，不能以舊程序核對解除。";
            return false;
        }
        try { _routeRegistry.ConfirmPriorProcessOrders(Account, symbol, expectedOwners); }
        catch (Exception exception)
        {
            message = exception.GetBaseException().Message;
            return false;
        }
        message = $"已記錄人工核對 {expectedOwners.Count} 筆舊委託；原歸屬與成交紀錄保留。可手動啟動新策略。";
        return true;
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
            message = "請先使用「關閉策略（撤單）」停止策略訊號並進入關閉流程，再執行人工核對。";
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
                ? _operatorClose.ContainsKey(instanceId)
                    ? $"已依人工核對同步 {reconciled} 筆刪單；帳本部位 {after.Position} 口待對帳，不會自動平倉。"
                    : $"已依人工核對同步 {reconciled} 筆刪單；仍有部位 {after.Position} 口，先進入 late-fill 觀察，完成後才會送出平倉單。"
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
            _closeReasons.TryRemove(instanceId, out _);
            _manualCloseReconciliationInProgress.TryRemove(instanceId, out _);
            _manualCloseObservationPending.TryRemove(instanceId, out _);
            _closeFlattenAuthorized.TryRemove(instanceId, out _);
            _flattenPending.TryRemove(instanceId, out _);
        }
        return removed;
    }

    public bool RetireOperatorClosed(string instanceId, out string message)
    {
        WaitForAdd(instanceId);
        StrategySnapshot? snapshot = Find(instanceId);
        if (snapshot == null || snapshot.State != StrategyLifecycleState.Stopped ||
            !_operatorClose.ContainsKey(instanceId) || !_executions.TryGetValue(instanceId, out KgiLiveStrategyExecutionService? execution))
        {
            message = "策略未經使用者關閉；禁止刪除。";
            return false;
        }
        if (execution.ActiveBrokerOrderCount != 0 || execution.LocalConditionCount != 0 || execution.ProtocolViolationCount != 0)
        {
            message = "仍有有效委託、本機條件或回報異常；禁止刪除。";
            return false;
        }
        long generation = _activeGenerations.GetValueOrDefault(instanceId, snapshot.StrategyRunGeneration);
        PositionSnapshot? position = _host.Ledger.GetStrategyPositions(instanceId)
            .SingleOrDefault(x => x.StrategyRunGeneration == generation &&
                (string.IsNullOrWhiteSpace(snapshot.TradableSymbol) ||
                 string.Equals(x.Symbol, snapshot.TradableSymbol, StringComparison.OrdinalIgnoreCase)));
        string symbol = position?.Symbol ?? snapshot.TradableSymbol;
        try
        {
            if (_routeRegistry != null && !_routeRegistry.Snapshot().Retirements.Any(x =>
                    string.Equals(x.StrategyInstanceId, instanceId, StringComparison.OrdinalIgnoreCase) &&
                    x.StrategyRunGeneration == generation))
                _routeRegistry.AppendRetirement(new LiveStrategyRetirement(
                    instanceId, generation, Account, symbol,
                    DateTimeOffset.UtcNow, position?.NetQuantity ?? snapshot.Position,
                    position?.RealizedPnl ?? snapshot.RealizedPnl,
                    position?.UnrealizedPnl ?? snapshot.UnrealizedPnl, position?.Costs ?? 0m,
                    execution.QuarantinedBrokerOrderCount, snapshot.Position == 0));
            if (!_host.RemoveRetired(instanceId))
            {
                message = "活動執行器尚未釋放；請稍後重試。";
                return false;
            }
        }
        catch (Exception exception)
        {
            message = "策略退休失敗，活動實例保留：" + exception.GetBaseException().Message;
            return false;
        }
        _executions.TryRemove(instanceId, out _);
        _dailyLossLimits.TryRemove(instanceId, out _);
        _riskTriggered.TryRemove(instanceId, out _);
        _activeGenerations.TryRemove(instanceId, out _);
        _pendingRisk.TryRemove(instanceId, out _);
        _pendingClose.TryRemove(instanceId, out _);
        _closeReasons.TryRemove(instanceId, out _);
        _operatorClose.TryRemove(instanceId, out _);
        _operatorDelete.TryRemove(instanceId, out _);
        _closeRetryNeeded.TryRemove(instanceId, out _);
        _closeFlattenAuthorized.TryRemove(instanceId, out _);
        _flattenPending.TryRemove(instanceId, out _);
        message = snapshot.Position == 0
            ? "策略已從活動列表移除。"
            : $"策略已從活動列表移除；最後帳本部位 {snapshot.Position} 口待人工對帳。";
        StrategyRetired?.Invoke(instanceId);
        return true;
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
        foreach (string instanceId in _operatorDelete.Keys.ToArray())
        {
            StrategySnapshot? snapshot = Find(instanceId);
            if (snapshot?.State == StrategyLifecycleState.Stopped)
                RetireOperatorClosed(instanceId, out _);
        }
        foreach (string instanceId in _pendingRisk.Keys.ToArray())
            if (_activeGenerations.TryGetValue(instanceId, out long generation)) TryProcessPendingRisk(instanceId, generation);
        foreach(string id in _powerRecordingFinalization.Keys)
            if(!_executions.TryGetValue(id,out var execution) || !execution.HasInFlightCallbacks)
            {FinalizeFlowRecording(id);_powerRecordingFinalization.TryRemove(id,out _);}
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

    // State/fill/risk callbacks can already own this instance's runner lock.
    // An all-instance query here introduces an A->B / B->A lock inversion.
    private StrategySnapshot? Find(string instanceId) => _host.GetSnapshot(instanceId);

    private void OnSnapshot(StrategySnapshot snapshot)
    {
        RecordLive(snapshot.InstanceId,r=>r.Record(snapshot));
        if(snapshot.State==StrategyLifecycleState.Stopped)
        {
            if(snapshot.StrategyType is "PowerReversal" or "NikkeiRlDirection" or "MultiFactorMeanR17" && _executions.TryGetValue(snapshot.InstanceId,out var execution) && execution.HasInFlightCallbacks)
                _powerRecordingFinalization.TryAdd(snapshot.InstanceId,0);
            else FinalizeFlowRecording(snapshot.InstanceId);
        }
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
        string pairedStatus = _routeRegistry == null ? string.Empty : string.Join(" | ",
            _routeRegistry.Snapshot().RestingPairs
                .Where(pair => string.Equals(pair.Key.StrategyInstanceId, snapshot.InstanceId,
                    StringComparison.OrdinalIgnoreCase))
                .TakeLast(4).Select(pair =>
                    $"PAIR g{pair.Key.StrategyRunGeneration}/{pair.Key.CycleId}:{pair.Phase}" +
                    $" buy={pair.Buy?.ClientOrderId ?? "--"} sell={pair.Sell?.ClientOrderId ?? "--"}" +
                    $" fill={pair.Buy?.DeliveredQuantity ?? 0}/{pair.Sell?.DeliveredQuantity ?? 0}" +
                    (pair.SettledOppositeCumulative.HasValue ?
                        $" final={pair.SettledOppositeCumulative}" : string.Empty) +
                    (pair.StopMarketClientOrderId.Length > 0 ? $" stop={pair.StopMarketClientOrderId}" : string.Empty)));
        snapshot = snapshot with
        {
            StrategyRunGeneration = _activeGenerations.GetValueOrDefault(snapshot.InstanceId),
            OcoStatusSummary = string.Join(" | ", GetOcoStates(snapshot.InstanceId).Take(4).Select(state =>
                $"g{state.Key.StrategyRunGeneration}/{state.Key.CycleId}:{state.Phase}" +
                (state.PendingCancelClientOrderId.Length > 0 ? $" cancel={state.PendingCancelClientOrderId}" : string.Empty) +
                (state.StopMarketClientOrderId.Length > 0 ? $" stop={state.StopMarketClientOrderId}" : string.Empty) +
                $" fill={state.DeliveredFilledQuantity}/{state.BrokerFinalCumulativeFilledQuantity?.ToString() ?? "?"}" +
                (state.Reason.Length > 0 ? $" {state.Reason}" : string.Empty))) +
                (pairedStatus.Length > 0 ? " | " + pairedStatus : string.Empty)
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
            if (generation != 0) QueueRiskStop(snapshot.InstanceId, generation, $"觸發單日最大虧損 ¥{limit}", "DailyLoss");
        }
    }

    private void OnExecution(ExecutionEvent execution)
    {
        Log.Info($"FillReceived instance={execution.StrategyInstanceId} runGeneration={execution.StrategyRunGeneration} " +
            $"cycle={execution.CycleId} clientOrderId={execution.ClientOrderId} orderId={execution.OrderId} " +
            $"executionId={execution.ExecutionId} side={execution.Side} role={execution.Role} " +
            $"quantity={execution.Quantity} price={execution.Price} timestamp={execution.Timestamp:O}");
        RecordLive(execution.StrategyInstanceId,r=>r.Record(execution));
        ExecutionOccurred?.Invoke(execution);
    }

    private void WaitForAdd(string instanceId)
    {
        if (!_addReservations.TryGetValue(instanceId, out AddReservation? reservation)) return;
        if (reservation.OwnerThreadId == Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Live Add/Start callback 不可同步操作自己的 partial instance。");
        reservation.Completed.Wait();
    }

    private void LogCloseRequested(string instanceId, string source, string reason, StrategySnapshot? snapshot)
    {
        _executions.TryGetValue(instanceId, out KgiLiveStrategyExecutionService? execution);
        string account;
        try { account = _account(); }
        catch { account = "unavailable"; }
        Log.Warn($"CloseRequested source={source} account={LogValue(account)} instance={instanceId} " +
            $"runGeneration={_activeGenerations.GetValueOrDefault(instanceId)} " +
            $"strategy={snapshot?.StrategyType ?? "unknown"} symbol={snapshot?.TradableSymbol ?? "unknown"} " +
            $"state={snapshot?.State.ToString() ?? "unknown"} reason={LogValue(reason)} " +
            $"position={snapshot?.Position ?? 0} completedTrades={snapshot?.CompletedTrades ?? 0} " +
            $"netPnl={snapshot?.NetPnl ?? 0m} activeBrokerOrders={execution?.ActiveBrokerOrderCount ?? 0} " +
            $"quarantinedBrokerOrders={execution?.QuarantinedBrokerOrderCount ?? 0} " +
            $"protocolViolations={execution?.ProtocolViolationCount ?? 0} " +
            $"localConditions={execution?.LocalConditionCount ?? 0}");
    }

    private static string LogValue(string? value) =>
        (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');

    private void QueueRiskStop(string instanceId, long generation, string reason, string source)
    {
        Log.Warn($"RiskStopQueued source={source} instance={instanceId} runGeneration={generation} " +
            $"reason={LogValue(reason)}");
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
            if (_host.RequestRiskExit(instanceId, pending.Reason) ||
                EmergencyStopCore(instanceId, pending.Reason, "AutomaticRisk"))
            {
                _riskTriggered.TryAdd(instanceId, 0);
                _pendingRisk.TryRemove(instanceId, out _);
            }
        }
        catch (Exception exception)
        {
            Log.Error($"RiskStopDispatchFailed instance={instanceId} runGeneration={generation}", exception);
        }
        finally { _riskProcessing.TryRemove(instanceId, out _); }
    }

    private bool TryFinalizeClose(string instanceId)
    {
        if (_operatorClose.ContainsKey(instanceId)) return TryFinalizeOperatorClose(instanceId);
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
            if(_host.RequestRiskExit(instanceId,"使用者關閉與平倉")) return false;
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
        _closeReasons.TryRemove(instanceId, out _);
        _closeFlattenAuthorized.TryRemove(instanceId, out _);
        _flattenPending.TryRemove(instanceId, out _);
        return _host.Stop(instanceId, "市場無委託且部位歸零，實盤策略已關閉");
    }

    private bool TryFinalizeOperatorClose(string instanceId)
    {
        if (!_pendingClose.ContainsKey(instanceId) ||
            !_executions.TryGetValue(instanceId, out KgiLiveStrategyExecutionService? execution)) return false;
        if (execution.ProtocolViolationCount > 0 || execution.ActiveBrokerOrderCount > 0 ||
            execution.LocalConditionCount > 0) return false;
        StrategySnapshot? snapshot = Find(instanceId);
        if (snapshot == null) return false;
        bool stopped = _host.OperatorStop(instanceId, snapshot.Position == 0
            ? "市場委託已撤銷，使用者關閉策略"
            : $"市場委託已撤銷；帳本部位 {snapshot.Position} 口待人工對帳，使用者關閉策略");
        if (stopped)
        {
            _pendingClose.TryRemove(instanceId, out _);
            _closeReasons.TryRemove(instanceId, out _);
        }
        return stopped;
    }

    private void OnExecutionStateChanged(string instanceId, KgiLiveStrategyExecutionService execution)
    {
        StrategySnapshot? current = null;
        try { current = Find(instanceId); }
        catch (ObjectDisposedException) { /* Host disposal can raise a final executor state callback. */ }
        if (current != null) PublishSnapshot(current);
        if (_pendingClose.ContainsKey(instanceId) && execution.ActiveBrokerOrderCount == 0 &&
            (_operatorClose.ContainsKey(instanceId) || execution.ManualReconciliationObservationCount == 0))
            _closeRetryNeeded.TryAdd(instanceId, 0);
    }

    private void OnExecutionLifecycleChanged(string instanceId, OrderLifecycleEvent state)
    {
        Log.Info($"OrderLifecycle instance={instanceId} runGeneration={state.StrategyRunGeneration} " +
            $"cycle={state.CycleId} clientOrderId={state.ClientOrderId} orderId={state.OrderId} " +
            $"brokerOrderId={state.BrokerOrderId} role={state.Role} side={state.Side} " +
            $"previousState={state.PreviousState} state={state.State} filled={state.FilledQuantity} " +
            $"remaining={state.RemainingQuantity} reasonCode={state.ReasonCode} " +
            $"reasonDetail={LogValue(state.ReasonDetail)} timestamp={state.Timestamp:O}");
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
            foreach(var id in _flowRecorders.Keys)FinalizeFlowRecording(id);
            _orderRouter?.Dispose();
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
