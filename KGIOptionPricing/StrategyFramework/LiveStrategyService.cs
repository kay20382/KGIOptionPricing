using KGIOptionPricing.OrderManagement.Core;
using System.Collections.Concurrent;
using KGIOptionPricing.Strategies.MeanReversionScalper;
using KGIOptionPricing.Strategies.MiniLeadMicro;
using KGIOptionPricing.Strategies.NikkeiMicrostructureAlpha;
using KGIOptionPricing.Strategies.OrderBookTrend;
using KGIOptionPricing.Strategies.WaveletScalper;
using KGIOptionPricing.StrategyFramework.Execution;
using KGIOptionPricing.StrategyFramework.MarketData;

namespace KGIOptionPricing.StrategyFramework;

public sealed record LiveStrategyCloseStatus(
    bool Exists,
    int Position,
    int ActiveBrokerOrders,
    int LocalConditions,
    bool ClosePending);

public sealed class LiveStrategyService : IDisposable
{
    private readonly StrategyRegistry _registry = new();
    private readonly StrategyProductResolver _resolver = new();
    private readonly StrategyHost _host;
    private readonly StrategyHeartbeatTimer _heartbeat;
    private readonly OrderManager _orders;
    private readonly KGITradeService _trade;
    private readonly ConcurrentDictionary<string, KgiLiveStrategyExecutionService> _executions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, decimal> _dailyLossLimits = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _riskTriggered = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _pendingClose = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _closeFlattenAuthorized = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _flattenPending = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public event Action<StrategySnapshot>? SnapshotChanged;
    public event Action<ExecutionEvent>? ExecutionOccurred;
    public string Account => _trade.Account;
    public bool IsLoggedIn => _trade.IsLoggedIn;

    public LiveStrategyService(KGIQuoteService quote, OrderManager orders, KGITradeService trade)
    {
        _orders = orders;
        _trade = trade;
        _registry.Register(new MeanReversionScalperFactory());
        _registry.Register(new WaveletScalperFactory());
        _registry.Register(new NikkeiMicrostructureAlphaFactory());
        _registry.Register(new MiniLeadMicroFactory());
        _registry.Register(new OrderBookTrendFactory());
        _host = new StrategyHost(new KgiStrategyMarketDataSource(quote));
        _host.SnapshotChanged += OnSnapshot;
        _host.ExecutionOccurred += execution => ExecutionOccurred?.Invoke(execution);
        _heartbeat = new StrategyHeartbeatTimer(_host.Pulse);
    }

    public void Add(StrategyDefinition definition, decimal commissionPerContract, decimal dailyLossLimit)
    {
        if (!_trade.IsLoggedIn) throw new InvalidOperationException("交易服務尚未登入，禁止啟動實盤策略。");
        if (string.Equals(definition.StrategyType, MiniLeadMicroFactory.TypeName, StringComparison.OrdinalIgnoreCase) &&
            (!definition.Parameters.TryGetValue("ResearchValidated", out string? validated) ||
             !bool.TryParse(validated, out bool isValidated) || !isValidated))
            throw new InvalidOperationException("Mini 領先 Micro 尚屬研究策略；未明確標記已通過樣本外、滑價、延遲與模擬盤門檻，禁止實盤啟動。");
        if (!string.Equals(definition.Universe.Tradable.ProductId, "225MC", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("實盤策略第一版僅允許交易商品 225MC。");
        if (_host.GetSnapshots().Any(x => string.Equals(x.InstanceId, definition.InstanceId, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"實盤策略實例已存在：{definition.InstanceId}");

        ResolvedStrategyUniverse universe = _resolver.Resolve(definition.Universe);
        ContractInfo contract = SymbolRepository.Instance.GetHotMonthContract(definition.Universe.Tradable.ProductId)
            ?? throw new InvalidOperationException("找不到 225MC 熱門月合約，禁止實盤啟動。");
        var execution = new KgiLiveStrategyExecutionService(
            _orders, _trade, definition.InstanceId, universe.Tradable, contract, commissionPerContract);
        execution.Failed += reason => EmergencyStop(definition.InstanceId, reason);
        execution.StateChanged += () => TryFinalizeClose(definition.InstanceId);
        _executions[definition.InstanceId] = execution;
        _dailyLossLimits[definition.InstanceId] = Math.Max(1m, dailyLossLimit);
        try
        {
            _host.Add(_registry.Get(definition.StrategyType).Create(definition), universe, commissionPerContract, execution);
        }
        catch
        {
            _executions.TryRemove(definition.InstanceId, out _);
            _dailyLossLimits.TryRemove(definition.InstanceId, out _);
            execution.Dispose();
            throw;
        }
    }

    public bool Pause(string instanceId)
    {
        StrategySnapshot? snapshot = Find(instanceId);
        return snapshot != null && _host.Pause(instanceId);
    }

    public bool Resume(string instanceId) => _trade.IsLoggedIn && _host.Resume(instanceId);

    public bool EmergencyStop(string instanceId, string reason = "使用者停止實盤策略")
    {
        StrategySnapshot? snapshot = Find(instanceId);
        if (snapshot == null) return false;
        _pendingClose.TryAdd(instanceId, 0);
        _closeFlattenAuthorized.TryAdd(instanceId, 0);
        _host.Pause(instanceId); // Stop new signals, but keep the runner alive to receive cancel/flatten fills.
        if (_executions.TryGetValue(instanceId, out KgiLiveStrategyExecutionService? execution))
        {
            execution.CancelAll(instanceId);
        }
        TryFinalizeClose(instanceId);
        return true;
    }

    public LiveStrategyCloseStatus GetCloseStatus(string instanceId)
    {
        StrategySnapshot? snapshot = Find(instanceId);
        if (snapshot == null) return new LiveStrategyCloseStatus(false, 0, 0, 0, false);
        _executions.TryGetValue(instanceId, out KgiLiveStrategyExecutionService? execution);
        return new LiveStrategyCloseStatus(
            true,
            snapshot.Position,
            execution?.ActiveBrokerOrderCount ?? 0,
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
            execution.CancelAll(instanceId);
        bool closed = TryFinalizeClose(instanceId);
        LiveStrategyCloseStatus after = GetCloseStatus(instanceId);
        closed = closed || (after.Exists && !after.ClosePending && Find(instanceId)?.State == StrategyLifecycleState.Stopped);
        message = closed
            ? "已確認市場委託為 0、部位為 0，策略已關閉。"
            : after.Position != 0
                ? $"仍有部位 {after.Position} 口，正在等待撤單完成後送出平倉單。"
                : $"已送出刪單，等待市場確認；市場委託 {after.ActiveBrokerOrders} 筆。";
        return closed;
    }

    public bool Remove(string instanceId)
    {
        StrategySnapshot? snapshot = Find(instanceId);
        if (snapshot == null || snapshot.Position != 0) return false;
        if (_executions.TryGetValue(instanceId, out KgiLiveStrategyExecutionService? execution) &&
            execution.GetWorkingOrders(instanceId).Count > 0) return false;
        bool removed = _host.Remove(instanceId);
        if (removed)
        {
            _executions.TryRemove(instanceId, out _);
            _dailyLossLimits.TryRemove(instanceId, out _);
            _riskTriggered.TryRemove(instanceId, out _);
            _pendingClose.TryRemove(instanceId, out _);
            _closeFlattenAuthorized.TryRemove(instanceId, out _);
            _flattenPending.TryRemove(instanceId, out _);
        }
        return removed;
    }

    public IReadOnlyList<StrategySnapshot> GetSnapshots() => _host.GetSnapshots();

    public IReadOnlyList<string> GetShutdownWarnings()
    {
        var warnings = new List<string>();
        foreach (StrategySnapshot snapshot in _host.GetSnapshots())
        {
            LiveStrategyCloseStatus status = GetCloseStatus(snapshot.InstanceId);
            if (status.Position != 0 || status.ActiveBrokerOrders != 0)
                warnings.Add($"{snapshot.InstanceId}：部位 {status.Position} 口／市場委託 {status.ActiveBrokerOrders} 筆");
        }
        return warnings;
    }

    private StrategySnapshot? Find(string instanceId) => _host.GetSnapshots().FirstOrDefault(x =>
        string.Equals(x.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase));

    private void OnSnapshot(StrategySnapshot snapshot)
    {
        SnapshotChanged?.Invoke(snapshot);
        if (_pendingClose.ContainsKey(snapshot.InstanceId)) TryFinalizeClose(snapshot.InstanceId);
        if (_dailyLossLimits.TryGetValue(snapshot.InstanceId, out decimal limit) && snapshot.NetPnl <= -limit &&
            _riskTriggered.TryAdd(snapshot.InstanceId, 0))
            EmergencyStop(snapshot.InstanceId, $"觸發單日最大虧損 ¥{limit}");
    }

    private bool TryFinalizeClose(string instanceId)
    {
        if (!_pendingClose.ContainsKey(instanceId) || !_executions.TryGetValue(instanceId, out KgiLiveStrategyExecutionService? execution))
            return false;
        StrategySnapshot? snapshot = Find(instanceId);
        if (snapshot == null) return false;
        if (execution.ActiveBrokerOrderCount > 0) return false;
        if (snapshot.Position != 0)
        {
            if (!_closeFlattenAuthorized.ContainsKey(instanceId) || _flattenPending.ContainsKey(instanceId)) return false;
            _flattenPending.TryAdd(instanceId, 0);
            execution.Flatten(snapshot.Position);
            return false;
        }
        if (execution.LocalConditionCount > 0) execution.CancelAll(instanceId);
        _pendingClose.TryRemove(instanceId, out _);
        _closeFlattenAuthorized.TryRemove(instanceId, out _);
        _flattenPending.TryRemove(instanceId, out _);
        return _host.Stop(instanceId, "市場無委託且部位歸零，實盤策略已關閉");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _heartbeat.Dispose();
        foreach (StrategySnapshot snapshot in _host.GetSnapshots().ToArray())
            EmergencyStop(snapshot.InstanceId, "程式關閉，實盤策略緊急停止");
        _host.SnapshotChanged -= OnSnapshot;
        _host.Dispose();
    }
}
