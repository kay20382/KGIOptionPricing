using System;
using System.Collections.Generic;
using System.Linq;
using KGIOptionPricing.OrderManagement.Core;

namespace KGIOptionPricing.StrategyFramework.Execution;

public sealed class LiveStrategyOrderRouter : IDisposable
{
    private sealed record FacadeKey(string InstanceId, long RunGeneration);
    private sealed class FacadeKeyComparer : IEqualityComparer<FacadeKey>
    {
        public bool Equals(FacadeKey? left, FacadeKey? right) => left is not null && right is not null &&
            left.RunGeneration == right.RunGeneration && StringComparer.OrdinalIgnoreCase.Equals(left.InstanceId, right.InstanceId);
        public int GetHashCode(FacadeKey key) => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(key.InstanceId), key.RunGeneration);
    }
    private readonly object _sync = new();
    private readonly ILiveStrategyOrderGateway _gateway;
    private readonly LiveOrderOwnershipRegistry _registry;
    private readonly Func<DateTime> _today;
    private readonly Dictionary<FacadeKey, RoutedGateway> _facades = new(new FacadeKeyComparer());
    private readonly List<QuarantinedBrokerEvent> _quarantine = new();
    private bool _disposed;

    public LiveStrategyOrderRouter(
        ILiveStrategyOrderGateway gateway,
        LiveOrderOwnershipRegistry registry,
        Func<DateTime>? today = null)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _today = today ?? (() => DateTime.Today);
        _gateway.OrderStateChanged += OnOrderStateChanged;
        _gateway.OrderFilled += OnOrderFilled;
        _gateway.OrderFailed += OnOrderFailed;
        _gateway.CancelFailed += OnCancelFailed;
    }

    public ILiveStrategyOrderGateway Register(string strategyInstanceId, long strategyRunGeneration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(strategyInstanceId);
        if (strategyRunGeneration <= 0) throw new ArgumentOutOfRangeException(nameof(strategyRunGeneration));
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var key = new FacadeKey(strategyInstanceId, strategyRunGeneration);
            if (_facades.ContainsKey(key))
                throw new InvalidOperationException($"A live execution facade is already registered for {strategyInstanceId} generation {strategyRunGeneration}.");
            var facade = new RoutedGateway(this, strategyInstanceId, strategyRunGeneration);
            _facades.Add(key, facade);
            return facade;
        }
    }

    public IReadOnlyList<QuarantinedBrokerEvent> GetQuarantine()
    {
        lock (_sync)
        {
            return _quarantine.ToArray();
        }
    }

    private void Submit(RoutedGateway facade, OrderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        StrategyOrderRouteMetadata metadata = request.StrategyRoute
            ?? throw new InvalidOperationException("Live strategy broker submission requires strategy route metadata.");
        if (!string.Equals(metadata.StrategyInstanceId, facade.StrategyInstanceId, StringComparison.OrdinalIgnoreCase) ||
            metadata.StrategyRunGeneration != facade.StrategyRunGeneration || string.IsNullOrWhiteSpace(metadata.ClientOrderId) ||
            string.IsNullOrWhiteSpace(metadata.CycleId) || string.IsNullOrWhiteSpace(metadata.Symbol) ||
            string.IsNullOrWhiteSpace(metadata.OrderRole))
            throw new InvalidOperationException("Live strategy route metadata is incomplete or belongs to another strategy instance.");
        if (!string.IsNullOrEmpty(metadata.OcoGroupId) && !string.Equals(metadata.OcoGroupId, metadata.CycleId, StringComparison.Ordinal))
            throw new InvalidOperationException("An opted-in OCO group must use its exact strategy cycle id.");
        if (!string.Equals(request.Account, _gateway.Account, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Live strategy request account differs from the routed broker account.");
        LiveOwnershipSnapshot current = _registry.Snapshot();
        LiveOrderOwnership? unresolved = current.Routes.FirstOrDefault(route => string.Equals(route.Account, request.Account, StringComparison.OrdinalIgnoreCase) &&
                                        string.Equals(route.Symbol, metadata.Symbol, StringComparison.OrdinalIgnoreCase) &&
                                        route.SubmissionState == StrategySubmissionState.Unknown && !route.PriorProcessReconciled);
        QuarantinedBrokerEvent? blockedEvent = current.QuarantinedEvents.FirstOrDefault(item => string.Equals(item.Account, request.Account, StringComparison.OrdinalIgnoreCase) &&
                                                  string.Equals(item.Symbol, metadata.Symbol, StringComparison.OrdinalIgnoreCase));
        if (unresolved != null || blockedEvent != null)
            throw new BrokerReconciliationRequiredException(
                "Account/product has an unresolved submission or quarantined broker event. " +
                (unresolved != null
                    ? $"原策略={unresolved.Key.StrategyInstanceId}; 原委託={unresolved.Key.ClientOrderId}; {unresolved.Reason}"
                    : $"隔離委託={blockedEvent!.OrderId}; {blockedEvent.Reason}"),
                brokerCallMayHaveStarted: false);

        LiveOrderOwnerKey owner = new(metadata.StrategyInstanceId, metadata.StrategyRunGeneration, metadata.ClientOrderId);
        if (!Enum.TryParse(metadata.OrderRole, ignoreCase: false, out PaperOrderRole role))
            throw new InvalidOperationException($"Unknown strategy order role: {metadata.OrderRole}");
        string? tradingDay = request.ForeignContext?.TradeDate;
        if (string.IsNullOrWhiteSpace(tradingDay)) tradingDay = _today().ToString("yyyyMMdd");
        _registry.Prepare(new LiveOrderOwnership
        {
            Key = owner,
            Account = request.Account,
            Symbol = metadata.Symbol,
            CycleId = metadata.CycleId,
            OcoGroupId = metadata.OcoGroupId,
            PairId = metadata.PairId,
            IsRestingPair = metadata.IsRestingPair,
            Role = role,
            Quantity = request.Quantity,
            TimeInForce = request.TimeInForce,
            LimitPrice = (decimal)request.Price,
            SubmissionState = StrategySubmissionState.Prepared
        });
        _registry.BindAlias(owner, new BrokerOrderAlias(request.Account, tradingDay, "OrderId", request.OrderId));

        try
        {
            _gateway.PlaceOrder(request);
        }
        catch (Exception exception)
        {
            _registry.MarkSubmissionUnknown(owner, exception.GetBaseException().Message);
            throw new BrokerReconciliationRequiredException(
                "Broker submission outcome is unknown; the account/product is blocked pending manual verification: " + exception.GetBaseException().Message);
        }
    }

    private void Cancel(RoutedGateway facade, string orderId)
    {
        LiveOrderOwnerKey? owner = ResolveAlias(facade.Account, "OrderId", orderId);
        if (owner == null || !string.Equals(owner.StrategyInstanceId, facade.StrategyInstanceId, StringComparison.OrdinalIgnoreCase) ||
            owner.StrategyRunGeneration != facade.StrategyRunGeneration)
            throw new InvalidOperationException("A strategy facade may cancel only an order with its own route.");
        _gateway.CancelOrder(orderId);
    }

    private void OnOrderStateChanged(OrderInfo order) => Route(order, "OrderStateChanged", facade => facade.PublishOrderStateChanged(order));
    private void OnOrderFilled(OrderInfo order, int quantity, double price)
    {
        if (!TryGetOwnerFacade(order, "OrderFilled", out LiveOrderOwnerKey? owner, out RoutedGateway? facade)) return;
        LiveOrderOwnership? route = _registry.Snapshot().Routes.SingleOrDefault(candidate => candidate.Key == owner);
        if (route == null || quantity <= 0)
        {
            Quarantine(order, "OrderFilled", "Fill has no matching route or has an invalid quantity.");
            return;
        }
        string? execId = order.Identity.ExecID;
        int cumulativeQuantity = order.FilledQty;
        if (!string.IsNullOrWhiteSpace(execId) && cumulativeQuantity <= route.CumulativeFilledQuantity)
            cumulativeQuantity = route.CumulativeFilledQuantity + quantity;
        if (string.IsNullOrWhiteSpace(execId) && cumulativeQuantity <= route.CumulativeFilledQuantity)
        {
            Quarantine(order, "OrderFilled", "Fill has neither a unique execution id nor a higher cumulative quantity.");
            return;
        }
        FillAcceptance accepted;
        try { accepted = _registry.TryAcceptFill(owner!, execId, cumulativeQuantity); }
        catch (Exception exception)
        {
            Quarantine(order, "OrderFilled", "Fill idempotency state could not be updated: " + exception.GetBaseException().Message);
            return;
        }
        if (!accepted.Accepted) return;
        if (accepted.AcceptedQuantity != quantity)
        {
            Quarantine(order, "OrderFilled", $"Broker fill quantity {quantity} differs from newly accepted cumulative delta {accepted.AcceptedQuantity}.");
            return;
        }
        facade!.PublishOrderFilled(order, quantity, price);
    }
    private void OnOrderFailed(OrderInfo order, string reason) => Route(order, "OrderFailed", facade => facade.PublishOrderFailed(order, reason));
    private void OnCancelFailed(OrderInfo order, string reason) => Route(order, "CancelFailed", facade => facade.PublishCancelFailed(order, reason));

    private void Route(OrderInfo order, string eventType, Action<RoutedGateway> publish)
    {
        if (TryGetOwnerFacade(order, eventType, out _, out RoutedGateway? facade)) publish(facade!);
    }

    private bool TryGetOwnerFacade(OrderInfo order, string eventType, out LiveOrderOwnerKey? owner, out RoutedGateway? facade)
    {
        owner = ResolveOwner(order);
        facade = null;
        if (owner == null)
        {
            if (_registry.IsSessionOnly)
            {
                LiveOwnershipSnapshot current = _registry.Snapshot();
                bool referencesCurrentOwner = order.StrategyRoute is StrategyOrderRouteMetadata metadata &&
                    current.Routes.Any(route => route.Key == new LiveOrderOwnerKey(
                        metadata.StrategyInstanceId, metadata.StrategyRunGeneration, metadata.ClientOrderId));
                // Broker recovery/manual reports remain visible to the account order manager,
                // but cannot be assigned to a new strategy or revive an old-session lock.
                // Conflicting identifiers that reference this session still fail closed.
                if (!referencesCurrentOwner && !FindOwnersByOrder(order, current).Any()) return false;
            }
            Quarantine(order, eventType, "Broker event has no unique strategy owner in this session.");
            return false;
        }
        try { PersistBrokerAliases(order, owner); }
        catch (Exception exception)
        {
            Quarantine(order, eventType, "Broker alias conflicted with its strategy route: " + exception.GetBaseException().Message);
            return false;
        }
        lock (_sync)
            _facades.TryGetValue(new FacadeKey(owner.StrategyInstanceId, owner.StrategyRunGeneration), out facade);
        if (facade != null) return true;
        Quarantine(order, eventType, "The strategy owner has no active strategy facade.");
        return false;
    }

    private LiveOrderOwnerKey? ResolveOwner(OrderInfo order)
    {
        LiveOwnershipSnapshot snapshot = _registry.Snapshot();
        if (order.StrategyRoute is StrategyOrderRouteMetadata metadata)
        {
            LiveOrderOwnerKey direct = new(metadata.StrategyInstanceId, metadata.StrategyRunGeneration, metadata.ClientOrderId);
            LiveOrderOwnership? route = snapshot.Routes.SingleOrDefault(candidate => candidate.Key == direct);
            if (route == null ||
                !string.Equals(route.Symbol, metadata.Symbol, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(route.CycleId, metadata.CycleId, StringComparison.Ordinal) ||
                !string.Equals(route.Role.ToString(), metadata.OrderRole, StringComparison.Ordinal) ||
                !string.Equals(route.OcoGroupId, metadata.OcoGroupId, StringComparison.Ordinal) ||
                !string.Equals(route.PairId, metadata.PairId, StringComparison.Ordinal) ||
                route.IsRestingPair != metadata.IsRestingPair)
                return null;
            LiveOrderOwnerKey[] indexed = FindOwnersByOrder(order, snapshot).Distinct().ToArray();
            return indexed.Length == 0 || indexed.Length == 1 && indexed[0] == direct ? direct : null;
        }

        LiveOrderOwnerKey[] matches = FindOwnersByOrder(order, snapshot).Distinct().ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private IEnumerable<LiveOrderOwnerKey> FindOwnersByOrder(OrderInfo order, LiveOwnershipSnapshot snapshot)
    {
        string account = string.IsNullOrWhiteSpace(order.Account) ? _gateway.Account : order.Account;
        foreach (BrokerOrderAlias alias in GetAliases(order, account))
            foreach (KeyValuePair<BrokerOrderAlias, LiveOrderOwnerKey> entry in snapshot.Aliases)
                if (string.Equals(entry.Key.Account, alias.Account, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(entry.Key.Kind, alias.Kind, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(entry.Key.Value, alias.Value, StringComparison.Ordinal))
                    yield return entry.Value;
    }

    private LiveOrderOwnerKey? ResolveAlias(string account, string kind, string value)
    {
        LiveOwnershipSnapshot snapshot = _registry.Snapshot();
        LiveOrderOwnerKey[] owners = snapshot.Aliases
            .Where(entry => string.Equals(entry.Key.Account, account, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(entry.Key.Kind, kind, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(entry.Key.Value, value, StringComparison.Ordinal))
            .Select(entry => entry.Value).Distinct().ToArray();
        return owners.Length == 1 ? owners[0] : null;
    }

    private void PersistBrokerAliases(OrderInfo order, LiveOrderOwnerKey owner)
    {
        string account = string.IsNullOrWhiteSpace(order.Account) ? _gateway.Account : order.Account;
        string tradingDay = order.SubmitTime == default ? _today().ToString("yyyyMMdd") : order.SubmitTime.ToString("yyyyMMdd");
        foreach (BrokerOrderAlias alias in GetAliases(order, account, tradingDay))
            _registry.BindAlias(owner, alias);
    }

    private static IEnumerable<BrokerOrderAlias> GetAliases(OrderInfo order, string account, string? tradingDay = null)
    {
        string day = tradingDay ?? DateTime.Today.ToString("yyyyMMdd");
        var values = new (string Kind, string Value)[]
        {
            ("OrderId", order.OrderId),
            ("RequestId", order.Identity.RequestId),
            ("LastRequestId", order.Identity.LastRequestId),
            ("Cnt", order.Identity.Cnt),
            ("LastOperationCnt", order.Identity.LastOperationCnt),
            ("OrdNo", order.Identity.OrdNo),
            ("OrgCnt", order.Identity.OrgCnt),
            ("ExternalOrderId", order.Identity.ExternalOrderId)
        };
        return values.Where(value => !string.IsNullOrWhiteSpace(value.Value))
            .Select(value => new BrokerOrderAlias(account, day, value.Kind, value.Value));
    }

    private void Quarantine(OrderInfo? order, string eventType, string reason)
    {
        var item = new QuarantinedBrokerEvent(
            eventType,
            order?.Account ?? _gateway.Account,
            order?.Instrument?.Symbol ?? order?.StrategyRoute?.Symbol ?? string.Empty,
            order?.OrderId ?? string.Empty,
            order?.Identity.RequestId ?? string.Empty,
            order?.Identity.Cnt ?? string.Empty,
            order?.Identity.OrdNo ?? string.Empty,
            order?.StrategyRoute?.ClientOrderId ?? string.Empty,
            DateTimeOffset.Now,
            reason);
        lock (_sync) _quarantine.Add(item);
        _registry.AddQuarantinedEvent(item);
    }

    private void Unregister(RoutedGateway facade)
    {
        lock (_sync)
        {
            var key = new FacadeKey(facade.StrategyInstanceId, facade.StrategyRunGeneration);
            if (_facades.TryGetValue(key, out RoutedGateway? current) && ReferenceEquals(current, facade))
                _facades.Remove(key);
        }
    }

    public void Dispose()
    {
        RoutedGateway[] facades;
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            facades = _facades.Values.ToArray();
            _facades.Clear();
        }
        _gateway.OrderStateChanged -= OnOrderStateChanged;
        _gateway.OrderFilled -= OnOrderFilled;
        _gateway.OrderFailed -= OnOrderFailed;
        _gateway.CancelFailed -= OnCancelFailed;
        foreach (RoutedGateway facade in facades) facade.Detach();
    }

    private sealed class RoutedGateway : ILiveStrategyOrderGateway, IDisposable
    {
        private readonly LiveStrategyOrderRouter _owner;
        private bool _disposed;
        public string StrategyInstanceId { get; }
        public long StrategyRunGeneration { get; }
        public bool IsLoggedIn => _owner._gateway.IsLoggedIn;
        public string BrokerId => _owner._gateway.BrokerId;
        public string Account => _owner._gateway.Account;
        public event Action<OrderInfo>? OrderStateChanged;
        public event Action<OrderInfo, int, double>? OrderFilled;
        public event Action<OrderInfo, string>? OrderFailed;
        public event Action<OrderInfo, string>? CancelFailed;

        public RoutedGateway(LiveStrategyOrderRouter owner, string strategyInstanceId, long strategyRunGeneration)
        {
            _owner = owner;
            StrategyInstanceId = strategyInstanceId;
            StrategyRunGeneration = strategyRunGeneration;
        }

        public void PlaceOrder(OrderRequest request)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _owner.Submit(this, request);
        }

        public void CancelOrder(string orderId)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _owner.Cancel(this, orderId);
        }

        public void AppendOcoState(LiveStrategyOcoGroupState state)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(state);
            if (!string.Equals(state.Key.StrategyInstanceId, StrategyInstanceId, StringComparison.OrdinalIgnoreCase) ||
                state.Key.StrategyRunGeneration != StrategyRunGeneration)
                throw new InvalidOperationException("A strategy facade may update only its own OCO state.");
            _owner._registry.UpdateOcoState(state);
        }

        public IReadOnlyList<LiveStrategyOcoGroupState> GetOcoStates(string strategyInstanceId, long strategyRunGeneration)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!string.Equals(strategyInstanceId, StrategyInstanceId, StringComparison.OrdinalIgnoreCase) ||
                strategyRunGeneration != StrategyRunGeneration)
                throw new InvalidOperationException("A strategy facade may read only its own OCO state.");
            return _owner._registry.Snapshot().OcoGroups.Where(state =>
                string.Equals(state.Key.StrategyInstanceId, StrategyInstanceId, StringComparison.OrdinalIgnoreCase) &&
                state.Key.StrategyRunGeneration == StrategyRunGeneration).ToArray();
        }

        public void AppendRestingPairState(RestingPairSnapshot state)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(state);
            if (!string.Equals(state.Key.StrategyInstanceId, StrategyInstanceId, StringComparison.OrdinalIgnoreCase) ||
                state.Key.StrategyRunGeneration != StrategyRunGeneration)
                throw new InvalidOperationException("A strategy facade may update only its own paired state.");
            _owner._registry.UpdateRestingPairState(state);
        }

        public IReadOnlyList<RestingPairSnapshot> GetRestingPairStates(string strategyInstanceId,
            long strategyRunGeneration)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!string.Equals(strategyInstanceId, StrategyInstanceId, StringComparison.OrdinalIgnoreCase) ||
                strategyRunGeneration != StrategyRunGeneration)
                throw new InvalidOperationException("A strategy facade may read only its own paired state.");
            return _owner._registry.Snapshot().RestingPairs.Where(state =>
                string.Equals(state.Key.StrategyInstanceId, StrategyInstanceId, StringComparison.OrdinalIgnoreCase) &&
                state.Key.StrategyRunGeneration == StrategyRunGeneration).ToArray();
        }

        internal void PublishOrderStateChanged(OrderInfo order) => OrderStateChanged?.Invoke(order);
        internal void PublishOrderFilled(OrderInfo order, int quantity, double price) => OrderFilled?.Invoke(order, quantity, price);
        internal void PublishOrderFailed(OrderInfo order, string reason) => OrderFailed?.Invoke(order, reason);
        internal void PublishCancelFailed(OrderInfo order, string reason) => CancelFailed?.Invoke(order, reason);
        internal void Detach() => _disposed = true;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _owner.Unregister(this);
        }
    }
}
