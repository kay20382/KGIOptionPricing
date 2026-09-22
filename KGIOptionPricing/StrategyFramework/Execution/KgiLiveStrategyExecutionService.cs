using KGIOptionPricing.OrderManagement.Core;

namespace KGIOptionPricing.StrategyFramework.Execution;

/// <summary>
/// Routes strategy orders through the shared OrderManager. Legacy Limit and
/// Stop requests remain local OCO conditions. BrokerLimit requests are sent
/// immediately as independent ROD limit orders and retain terminal tombstones
/// so cancel/fill races can still reach the shared ledger.
/// </summary>
public sealed class KgiLiveStrategyExecutionService : IManagedStrategyExecutionService, IDisposable
{
    private const string ProtocolReconciliationMessage =
        "偵測到券商成交量協定異常，必須先人工核對部位；已禁止自動送單、刪單與條件觸發。";
    // Bounds memory while preserving a practical cancel/fill race window.
    // Callbacks arriving more than ten minutes after terminal state are ignored;
    // those extreme delays must be reconciled from broker order/deal reports.
    private static readonly TimeSpan TerminalRetention = TimeSpan.FromMinutes(10);

    private sealed class TrackedRealOrder
    {
        public required PaperOrder Order { get; init; }
        public decimal Trigger { get; init; }
        public OrderLifecycleState? LifecycleState { get; set; }
        public int FilledQuantity { get; set; }
        public int RemainingQuantity { get; set; }
        public int DeliveredFilledQuantity { get; set; }
        public string BrokerOrderId { get; set; } = string.Empty;
        public bool Terminal { get; set; }
        public DateTimeOffset? TerminalAt { get; set; }
        public bool FailurePublished { get; set; }
        public bool AwaitingSettlement { get; set; }
        public bool ProtocolViolation { get; set; }
        public bool ProtocolViolationPublished { get; set; }
        public bool IsManaged => !string.IsNullOrWhiteSpace(Order.ClientOrderId);
    }

    private readonly object _sync = new();
    private readonly ILiveStrategyOrderGateway _gateway;
    private readonly string _instanceId;
    private readonly string _quoteSymbol;
    private readonly InstrumentKey _instrument;
    private readonly decimal _commission;
    private readonly Func<DateTimeOffset> _now;
    private readonly Action? _beforeFilledPublished;
    private readonly Action? _beforeFlattenRegistration;
    private readonly Dictionary<string, PaperOrder> _conditions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TrackedRealOrder> _realOrders = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _ownedClientOrderIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _clientOrderReservations = new(StringComparer.OrdinalIgnoreCase);
    private long _localSequence;
    private long _executionSequence;
    private bool _disposed;
    private bool _disposing;
    private int _inflightCallbacks;
    private readonly Dictionary<int, int> _callbackThreadDepth = new();
    private int _inflightOperations;
    private readonly Dictionary<int, int> _operationThreadDepth = new();
    private bool _protocolViolation;

    public event Action<ExecutionEvent>? Filled;
    public event Action<string>? Failed;
    public event Action<OrderLifecycleEvent>? OrderLifecycleChanged;
    public event Action? StateChanged;
    public int ActiveBrokerOrderCount
    {
        get
        {
            lock (_sync)
            {
                PurgeExpiredTerminalsLocked(_now());
                return _realOrders.Values.Count(x => !x.Terminal);
            }
        }
    }
    public int LocalConditionCount { get { lock (_sync) return _conditions.Count; } }
    public int QuarantinedBrokerOrderCount
    {
        get { lock (_sync) return _realOrders.Values.Count(x => x.Terminal && x.AwaitingSettlement); }
    }
    public int ProtocolViolationCount
    {
        get { lock (_sync) return _realOrders.Values.Count(x => x.ProtocolViolation); }
    }
    internal int ClientOrderRegistryCount
    {
        get { lock (_sync) return _ownedClientOrderIds.Count; }
    }
    internal bool ManualReconciliationRequired
    {
        get { lock (_sync) return _protocolViolation; }
    }

    public KgiLiveStrategyExecutionService(
        OrderManager orders,
        KGITradeService trade,
        string instanceId,
        ResolvedProduct product,
        ContractInfo contract,
        decimal commissionPerContract)
        : this(new OrderManagerLiveStrategyOrderGateway(orders, trade), instanceId, product, contract,
            commissionPerContract, null)
    {
    }

    public KgiLiveStrategyExecutionService(
        ILiveStrategyOrderGateway gateway,
        string instanceId,
        ResolvedProduct product,
        ContractInfo contract,
        decimal commissionPerContract,
        Func<DateTimeOffset>? now = null,
        Action? beforeFilledPublished = null,
        Action? beforeFlattenRegistration = null)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _instanceId = instanceId;
        _quoteSymbol = product.Symbol;
        _commission = Math.Max(0m, commissionPerContract);
        _now = now ?? (() => DateTimeOffset.Now);
        _beforeFilledPublished = beforeFilledPublished;
        _beforeFlattenRegistration = beforeFlattenRegistration;
        _instrument = LiveStrategyOrderMapper.ToInstrument(contract);
        _gateway.OrderFilled += OnOrderFilled;
        _gateway.OrderFailed += OnOrderFailed;
        _gateway.CancelFailed += OnCancelFailed;
        _gateway.OrderStateChanged += OnOrderChanged;
    }

    public PaperOrder Submit(PaperOrderRequest request, DateTimeOffset now)
    {
        EnterOperation();
        try
        {
        lock (_sync)
        {
            PurgeExpiredTerminalsLocked(_now());
            ThrowIfProtocolViolationLocked();
        }
        if (!_gateway.IsLoggedIn) throw new InvalidOperationException("交易服務尚未登入，禁止實盤送單。");
        if (!string.Equals(request.StrategyInstanceId, _instanceId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("策略實例與實盤執行器不一致。");
        if (!string.Equals(request.Symbol, _quoteSymbol, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("策略商品與實盤執行器不一致。");
        if (request.Quantity != 1) throw new InvalidOperationException("實盤策略第一版固定每次 1 口。");

        bool ownsReservation = !string.IsNullOrWhiteSpace(request.ClientOrderId);
        if (ownsReservation && !TryReserveClientOrderIdCore(request.ClientOrderId))
            throw new InvalidOperationException($"ClientOrderId 已存在或已保留：{request.ClientOrderId}");

        bool brokerLimit = request.Type == PaperOrderType.BrokerLimit;
        string orderId = brokerLimit
            ? $"STRAT-{_instanceId}-{Interlocked.Increment(ref _localSequence)}"
            : $"COND-{_instanceId}-{Interlocked.Increment(ref _localSequence)}";
        var order = new PaperOrder
        {
            OrderId = orderId,
            ClientOrderId = request.ClientOrderId,
            StrategyInstanceId = request.StrategyInstanceId,
            Symbol = request.Symbol,
            Side = request.Side,
            Type = request.Type,
            Role = request.Role,
            Price = request.Price,
            Quantity = request.Quantity,
            State = PaperOrderState.Working,
            CreatedAt = now
        };

        try
        {
            if (brokerLimit)
                SendBrokerLimit(request, order, now);
            else if (request.Type == PaperOrderType.Market)
                SendMarket(order, request.Price);
            else
            {
                lock (_sync)
                {
                    _conditions[order.OrderId] = order;
                    CommitClientOrderReservationLocked(order.ClientOrderId);
                }
                StateChanged?.Invoke();
            }
        }
        catch
        {
            if (ownsReservation) AbandonClientOrderReservation(request.ClientOrderId);
            throw;
        }
        return order;
        }
        finally { ExitOperation(); }
    }

    public bool Cancel(string orderId)
    {
        EnterOperation();
        try
        {
        PaperOrder? removedCondition;
        string? realId;
        lock (_sync)
        {
            ThrowIfClosingLocked();
            PurgeExpiredTerminalsLocked(_now());
            ThrowIfProtocolViolationLocked();
            _conditions.Remove(orderId, out removedCondition);
            if (removedCondition != null) RetireClientOrderIdLocked(removedCondition.ClientOrderId);
            realId = removedCondition != null
                ? null
                : _realOrders.FirstOrDefault(x => !x.Value.Terminal && x.Value.Order.OrderId == orderId).Key;
        }
        if (removedCondition != null)
        {
            StateChanged?.Invoke();
            return true;
        }
        return !string.IsNullOrEmpty(realId) && RequestCancel(realId);
        }
        finally { ExitOperation(); }
    }

    public bool CancelByClientOrderId(string strategyInstanceId, string clientOrderId)
    {
        EnterOperation();
        try
        {
        if (!string.Equals(strategyInstanceId, _instanceId, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(clientOrderId)) return false;

        string? realId;
        lock (_sync)
        {
            ThrowIfClosingLocked();
            PurgeExpiredTerminalsLocked(_now());
            ThrowIfProtocolViolationLocked();
            string[] matches = _realOrders
                .Where(x => !x.Value.Terminal &&
                            string.Equals(x.Value.Order.StrategyInstanceId, strategyInstanceId, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(x.Value.Order.ClientOrderId, clientOrderId, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Key)
                .ToArray();
            if (matches.Length != 1) return false;
            realId = matches[0];
        }
        return RequestCancel(realId);
        }
        finally { ExitOperation(); }
    }

    public int CancelAll(string strategyInstanceId)
    {
        EnterOperation();
        try
        {
        if (!string.Equals(strategyInstanceId, _instanceId, StringComparison.OrdinalIgnoreCase)) return 0;
        string[] realIds;
        int conditionCount;
        lock (_sync)
        {
            ThrowIfClosingLocked();
            PurgeExpiredTerminalsLocked(_now());
            ThrowIfProtocolViolationLocked();
            conditionCount = _conditions.Count;
            foreach (PaperOrder condition in _conditions.Values)
                RetireClientOrderIdLocked(condition.ClientOrderId);
            _conditions.Clear();
            realIds = _realOrders.Where(x => !x.Value.Terminal).Select(x => x.Key).ToArray();
        }
        foreach (string realId in realIds) RequestCancel(realId);
        if (conditionCount > 0) StateChanged?.Invoke();
        return conditionCount + realIds.Length;
        }
        finally { ExitOperation(); }
    }

    public IReadOnlyList<PaperOrder> GetWorkingOrders(string strategyInstanceId)
    {
        if (!string.Equals(strategyInstanceId, _instanceId, StringComparison.OrdinalIgnoreCase)) return Array.Empty<PaperOrder>();
        lock (_sync)
        {
            PurgeExpiredTerminalsLocked(_now());
            return _conditions.Values
                .Concat(_realOrders.Values.Where(x => !x.Terminal).Select(x => x.Order))
                .ToArray();
        }
    }

    public void OnMarketEvent(MarketEvent marketEvent)
    {
        EnterOperation();
        try
        {
        if (marketEvent.Kind != MarketEventKind.Trade || marketEvent.LastPrice <= 0m ||
            !string.Equals(marketEvent.Symbol, _quoteSymbol, StringComparison.OrdinalIgnoreCase)) return;

        PaperOrder? triggered;
        lock (_sync)
        {
            ThrowIfClosingLocked();
            ThrowIfProtocolViolationLocked();
            triggered = _conditions.Values.OrderBy(x => x.CreatedAt).FirstOrDefault(order => order.Type switch
            {
                PaperOrderType.Limit when order.Side == BuySell.Buy => marketEvent.LastPrice < order.Price,
                PaperOrderType.Limit => marketEvent.LastPrice > order.Price,
                PaperOrderType.Stop when order.Side == BuySell.Buy => marketEvent.LastPrice > order.Price,
                PaperOrderType.Stop => marketEvent.LastPrice < order.Price,
                _ => false
            });
            if (triggered != null)
            {
                foreach (PaperOrder condition in _conditions.Values)
                {
                    if (!ReferenceEquals(condition, triggered))
                        RetireClientOrderIdLocked(condition.ClientOrderId);
                }
                _conditions.Clear(); // Existing local OCO behavior.
            }
        }
        if (triggered != null) SendMarket(triggered, marketEvent.LastPrice);
        }
        finally { ExitOperation(); }
    }

    public bool TryReserveClientOrderId(string clientOrderId)
    {
        EnterOperation();
        try
        {
            if (string.IsNullOrWhiteSpace(clientOrderId)) return false;
            return TryReserveClientOrderIdCore(clientOrderId);
        }
        finally { ExitOperation(); }
    }

    public void ReleaseClientOrderIdReservation(string clientOrderId)
    {
        EnterOperation();
        try { AbandonClientOrderReservation(clientOrderId); }
        finally { ExitOperation(); }
    }

    private bool TryReserveClientOrderIdCore(string clientOrderId)
    {
        lock (_sync)
        {
            ThrowIfClosingLocked();
            ThrowIfProtocolViolationLocked();
            if (!_ownedClientOrderIds.Add(clientOrderId)) return false;
            _clientOrderReservations.Add(clientOrderId);
            return true;
        }
    }

    private void CommitClientOrderReservationLocked(string clientOrderId)
    {
        if (!string.IsNullOrWhiteSpace(clientOrderId)) _clientOrderReservations.Remove(clientOrderId);
    }

    private void AbandonClientOrderReservation(string clientOrderId)
    {
        if (string.IsNullOrWhiteSpace(clientOrderId)) return;
        lock (_sync)
        {
            if (_clientOrderReservations.Remove(clientOrderId)) _ownedClientOrderIds.Remove(clientOrderId);
        }
    }

    public void Flatten(int netPosition)
    {
        lock (_sync) ThrowIfProtocolViolationLocked();
        if (netPosition == 0) return;
        string clientOrderId = $"flatten-{_instanceId}-{Interlocked.Increment(ref _localSequence)}";
        if (!TryReserveClientOrderId(clientOrderId))
            throw new InvalidOperationException($"ClientOrderId 已存在或已保留：{clientOrderId}");
        try { FlattenReserved(netPosition, clientOrderId); }
        catch
        {
            ReleaseClientOrderIdReservation(clientOrderId);
            throw;
        }
    }

    public void FlattenReserved(int netPosition, string clientOrderId)
    {
        EnterOperation();
        try
        {
        lock (_sync) ThrowIfProtocolViolationLocked();
        if (netPosition == 0) return;
        if (Math.Abs(netPosition) != 1)
            throw new InvalidOperationException("實盤策略平倉硬閘固定每次 1 口；非一口部位必須人工核對。");
        if (string.IsNullOrWhiteSpace(clientOrderId))
            throw new ArgumentException("Flatten ClientOrderId 不得空白。", nameof(clientOrderId));
        lock (_sync)
        {
            ThrowIfProtocolViolationLocked();
            if (!_clientOrderReservations.Contains(clientOrderId))
                throw new InvalidOperationException($"Flatten ClientOrderId 未先取得唯一保留權：{clientOrderId}");
        }
        var order = new PaperOrder
        {
            OrderId = $"FLAT-{_instanceId}-{Interlocked.Increment(ref _localSequence)}",
            ClientOrderId = clientOrderId,
            StrategyInstanceId = _instanceId,
            Symbol = _quoteSymbol,
            Side = netPosition > 0 ? BuySell.Sell : BuySell.Buy,
            Type = PaperOrderType.Market,
            Role = PaperOrderRole.Flatten,
            Price = 1m,
            Quantity = 1,
            State = PaperOrderState.Working,
            CreatedAt = _now()
        };
        try
        {
            _beforeFlattenRegistration?.Invoke();
            SendMarket(order, 0m);
        }
        catch
        {
            AbandonClientOrderReservation(clientOrderId);
            throw;
        }
        }
        finally { ExitOperation(); }
    }

    private void SendBrokerLimit(PaperOrderRequest request, PaperOrder order, DateTimeOffset now)
    {
        OrderRequest brokerRequest = LiveStrategyOrderMapper.CreateBrokerLimit(
            request, _instrument, _gateway.BrokerId, _gateway.Account, order.OrderId);
        lock (_sync)
        {
            PurgeExpiredTerminalsLocked(_now());
            ThrowIfProtocolViolationLocked();
            if (_realOrders.Values.Any(x => string.Equals(
                    x.Order.ClientOrderId, request.ClientOrderId, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"ClientOrderId 已存在：{request.ClientOrderId}");
            _realOrders[order.OrderId] = new TrackedRealOrder
            {
                Order = order,
                Trigger = request.Price,
                RemainingQuantity = order.Quantity,
                AwaitingSettlement = true
            };
            CommitClientOrderReservationLocked(order.ClientOrderId);
        }
        PublishTransition(order.OrderId, OrderLifecycleState.Submitted, null, "Submitted", now);

        try
        {
            lock (_sync) ThrowIfProtocolViolationLocked();
            _gateway.PlaceOrder(brokerRequest);
        }
        catch (Exception ex)
        {
            PublishTransition(order.OrderId, OrderLifecycleState.Rejected, null, "SubmitException", _now());
            PublishFailureOnce(order.OrderId, "實盤限價單送單例外：" + ex.GetBaseException().Message);
        }
    }

    private void SendMarket(PaperOrder condition, decimal trigger)
    {
        if (condition.Quantity != 1)
            throw new InvalidOperationException("實盤策略市價單硬閘固定每次 1 口。");
        if (!_gateway.IsLoggedIn)
            throw new InvalidOperationException("交易服務已離線，實盤市價單未送出。");
        string realId = $"STRAT-{_instanceId}-{Interlocked.Increment(ref _localSequence)}";
        lock (_sync)
        {
            ThrowIfProtocolViolationLocked();
            _realOrders[realId] = new TrackedRealOrder
            {
                Order = condition,
                Trigger = trigger,
                RemainingQuantity = condition.Quantity,
                AwaitingSettlement = true
            };
            CommitClientOrderReservationLocked(condition.ClientOrderId);
        }
        PublishTransition(realId, OrderLifecycleState.Submitted, null, "Submitted", _now());
        try
        {
            lock (_sync) ThrowIfProtocolViolationLocked();
            _gateway.PlaceOrder(new OrderRequest
            {
                OrderId = realId,
                Instrument = _instrument,
                BuySell = condition.Side,
                Price = 0d,
                Quantity = condition.Quantity,
                TimeInForce = TimeInForce.IOC,
                PositionEffect = PositionEffect.Auto,
                BrokerId = _gateway.BrokerId,
                Account = _gateway.Account
            });
        }
        catch (Exception ex)
        {
            PublishTransition(realId, OrderLifecycleState.Rejected, null, "SubmitException", _now());
            PublishFailureOnce(realId, "實盤送單例外：" + ex.GetBaseException().Message);
        }
    }

    private bool RequestCancel(string realId)
    {
        OrderLifecycleEvent? lifecycle = null;
        bool managed;
        lock (_sync)
        {
            PurgeExpiredTerminalsLocked(_now());
            ThrowIfProtocolViolationLocked();
            if (!_realOrders.TryGetValue(realId, out TrackedRealOrder? tracked) || tracked.Terminal ||
                tracked.LifecycleState == OrderLifecycleState.CancelPending) return false;
            managed = tracked.IsManaged;
            if (managed)
                lifecycle = CreateTransitionLocked(tracked, OrderLifecycleState.CancelPending, null,
                    "CancelRequested", _now());
        }
        PublishLifecycle(lifecycle);

        try
        {
            lock (_sync)
            {
                ThrowIfProtocolViolationLocked();
            }
            _gateway.CancelOrder(realId);
            if (!managed) StateChanged?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            Failed?.Invoke("實盤刪單例外：" + ex.GetBaseException().Message);
            return false;
        }
    }

    private void OnOrderFilled(OrderInfo brokerOrder, int quantity, double price)
    {
        if (!TryEnterCallback()) return;
        try
        {
        ExecutionEvent? execution = null;
        OrderLifecycleEvent? lifecycle = null;
        bool legacyTerminal = false;
        string? protocolFailure = null;
        lock (_sync)
        {
            PurgeExpiredTerminalsLocked(_now());
            if (!_realOrders.TryGetValue(brokerOrder.OrderId, out TrackedRealOrder? tracked)) return;
            UpdateBrokerOrderId(tracked, brokerOrder);
            int reportedCumulative = brokerOrder.FilledQty > 0
                ? brokerOrder.FilledQty
                : tracked.DeliveredFilledQuantity + Math.Max(0, quantity);
            if (reportedCumulative > tracked.Order.Quantity)
            {
                tracked.ProtocolViolation = true;
                tracked.AwaitingSettlement = true;
                _protocolViolation = true;
                if (!tracked.ProtocolViolationPublished)
                {
                    tracked.ProtocolViolationPublished = true;
                    protocolFailure = $"成交累計 {reportedCumulative} 超過委託量 {tracked.Order.Quantity}，已限制入帳並隔離等待人工核對。";
                }
            }
            int effectiveCumulative = Math.Clamp(reportedCumulative, 0, tracked.Order.Quantity);
            int delta = Math.Max(0, effectiveCumulative - tracked.DeliveredFilledQuantity);
            if (delta == 0) goto PublishOnly;
            tracked.DeliveredFilledQuantity += delta;
            tracked.FilledQuantity = Math.Max(tracked.FilledQuantity, effectiveCumulative);
            tracked.RemainingQuantity = Math.Max(0, tracked.Order.Quantity - effectiveCumulative);

            execution = new ExecutionEvent
            {
                Sequence = Interlocked.Increment(ref _executionSequence),
                Timestamp = _now(),
                StrategyInstanceId = _instanceId,
                ClientOrderId = tracked.Order.ClientOrderId,
                OrderId = brokerOrder.OrderId,
                Symbol = _quoteSymbol,
                Side = tracked.Order.Side,
                Role = tracked.Order.Role,
                Price = (decimal)price,
                Quantity = delta,
                Commission = _commission * delta,
                TriggerMarketPrice = tracked.Trigger,
                MarketSequence = 0
            };

            bool partial = tracked.RemainingQuantity > 0;
            if (tracked.IsManaged)
                lifecycle = CreateTransitionLocked(
                    tracked,
                    partial ? OrderLifecycleState.PartiallyFilled : OrderLifecycleState.Filled,
                    brokerOrder,
                    partial ? "PartiallyFilled" : "Filled",
                    _now(),
                    allowLateFill: true);
            else if (!partial)
            {
                if (tracked.ProtocolViolation)
                {
                    tracked.Terminal = true;
                    tracked.TerminalAt = _now();
                    tracked.AwaitingSettlement = true;
                }
                else _realOrders.Remove(brokerOrder.OrderId);
                legacyTerminal = true;
            }
        PublishOnly:;
        }

        // Match paper execution ordering: ledger first, then terminal lifecycle.
        if (execution != null)
        {
            _beforeFilledPublished?.Invoke();
            Filled?.Invoke(execution);
        }
        if (lifecycle != null) OrderLifecycleChanged?.Invoke(lifecycle);
        if (protocolFailure != null) Failed?.Invoke(protocolFailure);
        if (execution != null || lifecycle != null || legacyTerminal || protocolFailure != null) StateChanged?.Invoke();
        }
        finally
        {
            ExitCallback();
        }
    }

    private void OnOrderFailed(OrderInfo brokerOrder, string reason)
    {
        if (!TryEnterCallback()) return;
        try
        {
        OrderLifecycleEvent? lifecycle = null;
        string? failure = null;
        bool legacyRemoved = false;
        lock (_sync)
        {
            PurgeExpiredTerminalsLocked(_now());
            if (!_realOrders.TryGetValue(brokerOrder.OrderId, out TrackedRealOrder? tracked)) return;
            if (tracked.Terminal) return;
            UpdateBrokerOrderId(tracked, brokerOrder);
            if (tracked.IsManaged)
                lifecycle = CreateTransitionLocked(tracked, OrderLifecycleState.Rejected, brokerOrder,
                    "Rejected", _now());
            else
            {
                tracked.Terminal = true;
                tracked.TerminalAt = _now();
                tracked.AwaitingSettlement = true;
                legacyRemoved = true;
            }
            if (!tracked.FailurePublished)
            {
                tracked.FailurePublished = true;
                failure = $"實盤委託失敗：{reason}";
            }
        }
        if (lifecycle != null) OrderLifecycleChanged?.Invoke(lifecycle);
        if (failure != null) Failed?.Invoke(failure);
        if (lifecycle != null || legacyRemoved) StateChanged?.Invoke();
        }
        finally { ExitCallback(); }
    }

    private void OnCancelFailed(OrderInfo brokerOrder, string reason)
    {
        if (!TryEnterCallback()) return;
        try
        {
        bool owned;
        OrderLifecycleEvent? lifecycle = null;
        lock (_sync)
        {
            PurgeExpiredTerminalsLocked(_now());
            owned = _realOrders.TryGetValue(brokerOrder.OrderId, out TrackedRealOrder? tracked) && !tracked.Terminal;
            if (owned && tracked!.IsManaged && tracked.LifecycleState == OrderLifecycleState.CancelPending)
                lifecycle = CreateTransitionLocked(tracked, OrderLifecycleState.Working, brokerOrder,
                    "CancelFailed", _now(), allowCancelFailureRecovery: true);
        }
        if (lifecycle != null) OrderLifecycleChanged?.Invoke(lifecycle);
        if (owned) Failed?.Invoke($"實盤刪單失敗：{reason}");
        if (lifecycle != null) StateChanged?.Invoke();
        }
        finally { ExitCallback(); }
    }

    private void OnOrderChanged(OrderInfo brokerOrder)
    {
        if (!TryEnterCallback()) return;
        try
        {
        OrderLifecycleEvent? lifecycle = null;
        string? failure = null;
        bool legacyChanged = false;
        lock (_sync)
        {
            PurgeExpiredTerminalsLocked(_now());
            if (!_realOrders.TryGetValue(brokerOrder.OrderId, out TrackedRealOrder? tracked)) return;
            UpdateBrokerOrderId(tracked, brokerOrder);
            if (brokerOrder.FilledQty > tracked.Order.Quantity)
            {
                tracked.ProtocolViolation = true;
                tracked.AwaitingSettlement = true;
                _protocolViolation = true;
                if (!tracked.ProtocolViolationPublished)
                {
                    tracked.ProtocolViolationPublished = true;
                    failure = $"成交累計 {brokerOrder.FilledQty} 超過委託量 {tracked.Order.Quantity}，已限制入帳並隔離等待人工核對。";
                }
            }
            if (!tracked.IsManaged)
            {
                if (brokerOrder.Status is OrderStatus.Filled or OrderStatus.Cancelled or OrderStatus.Rejected)
                {
                    tracked.Terminal = true;
                    tracked.TerminalAt = _now();
                    tracked.AwaitingSettlement = true;
                    legacyChanged = true;
                }
            }
            else
            {
                // A terminal tombstone advances only in OnOrderFilled, which
                // guarantees the execution reaches the ledger first.
                if (!tracked.Terminal)
                switch (brokerOrder.Status)
                {
                    case OrderStatus.Accepted:
                        lifecycle = CreateTransitionLocked(tracked, OrderLifecycleState.Working, brokerOrder,
                            "Working", _now());
                        break;
                    case OrderStatus.CancelPending:
                        lifecycle = CreateTransitionLocked(tracked, OrderLifecycleState.CancelPending, brokerOrder,
                            "CancelPending", _now());
                        break;
                    case OrderStatus.PartiallyFilled:
                        lifecycle = CreateTransitionLocked(tracked, OrderLifecycleState.PartiallyFilled, brokerOrder,
                            "PartiallyFilled", _now());
                        break;
                    case OrderStatus.Filled:
                        lifecycle = CreateTransitionLocked(tracked, OrderLifecycleState.Filled, brokerOrder,
                            "Filled", _now());
                        break;
                    case OrderStatus.Cancelled:
                        lifecycle = CreateTransitionLocked(tracked, OrderLifecycleState.Cancelled, brokerOrder,
                            "Cancelled", _now());
                        break;
                    case OrderStatus.Rejected:
                        lifecycle = CreateTransitionLocked(tracked, OrderLifecycleState.Rejected, brokerOrder,
                            "Rejected", _now());
                        if (!tracked.FailurePublished)
                        {
                            tracked.FailurePublished = true;
                            string rejectedFailure = string.IsNullOrWhiteSpace(brokerOrder.Message)
                                ? "實盤委託遭券商拒絕。"
                                : $"實盤委託失敗：{brokerOrder.Message}";
                            failure = failure == null ? rejectedFailure : failure + " " + rejectedFailure;
                        }
                        break;
                }
            }
        }
        if (lifecycle != null) OrderLifecycleChanged?.Invoke(lifecycle);
        if (failure != null) Failed?.Invoke(failure);
        if (lifecycle != null || legacyChanged || failure != null) StateChanged?.Invoke();
        }
        finally { ExitCallback(); }
    }

    private void PublishTransition(
        string realId,
        OrderLifecycleState state,
        OrderInfo? brokerOrder,
        string reasonCode,
        DateTimeOffset timestamp)
    {
        OrderLifecycleEvent? lifecycle;
        lock (_sync)
        {
            PurgeExpiredTerminalsLocked(_now());
            if (!_realOrders.TryGetValue(realId, out TrackedRealOrder? tracked)) return;
            lifecycle = CreateTransitionLocked(tracked, state, brokerOrder, reasonCode, timestamp);
        }
        PublishLifecycle(lifecycle);
    }

    private OrderLifecycleEvent? CreateTransitionLocked(
        TrackedRealOrder tracked,
        OrderLifecycleState state,
        OrderInfo? brokerOrder,
        string reasonCode,
        DateTimeOffset timestamp,
        bool allowLateFill = false,
        bool allowCancelFailureRecovery = false)
    {
        UpdateBrokerOrderId(tracked, brokerOrder);
        int reportedFilled = brokerOrder == null ? tracked.FilledQuantity : Math.Max(tracked.FilledQuantity, brokerOrder.FilledQty);
        if (reportedFilled > tracked.Order.Quantity)
        {
            tracked.ProtocolViolation = true;
            tracked.AwaitingSettlement = true;
            _protocolViolation = true;
        }
        int filled = Math.Clamp(reportedFilled, 0, tracked.Order.Quantity);
        int remaining = brokerOrder == null
            ? tracked.RemainingQuantity
            : Math.Max(0, tracked.Order.Quantity - filled);
        bool terminal = state is OrderLifecycleState.Filled or OrderLifecycleState.Cancelled or OrderLifecycleState.Rejected;
        if (!CanTransition(tracked, state, allowLateFill, allowCancelFailureRecovery)) return null;
        if (tracked.LifecycleState == state &&
            tracked.FilledQuantity == filled && tracked.RemainingQuantity == remaining)
        {
            if (state == OrderLifecycleState.Filled &&
                !tracked.ProtocolViolation && tracked.DeliveredFilledQuantity >= tracked.Order.Quantity && remaining == 0)
                tracked.AwaitingSettlement = false;
            if (terminal)
            {
                tracked.Terminal = true;
                tracked.TerminalAt = timestamp;
            }
            return null;
        }

        OrderLifecycleState previous = tracked.LifecycleState ?? OrderLifecycleState.Submitted;
        tracked.LifecycleState = state;
        tracked.FilledQuantity = filled;
        tracked.RemainingQuantity = remaining;
        tracked.Terminal = tracked.Terminal || terminal;
        if (terminal) tracked.TerminalAt = timestamp;
        if (state == OrderLifecycleState.Filled && !tracked.ProtocolViolation &&
            tracked.DeliveredFilledQuantity >= tracked.Order.Quantity && remaining == 0)
            tracked.AwaitingSettlement = false;
        return new OrderLifecycleEvent
        {
            Timestamp = timestamp,
            StrategyInstanceId = _instanceId,
            ClientOrderId = tracked.Order.ClientOrderId,
            OrderId = tracked.Order.OrderId,
            BrokerOrderId = tracked.BrokerOrderId,
            PreviousState = previous,
            State = state,
            Price = tracked.Order.Price,
            Quantity = tracked.Order.Quantity,
            FilledQuantity = filled,
            RemainingQuantity = remaining,
            ReasonCode = reasonCode
        };
    }

    private void PublishLifecycle(OrderLifecycleEvent? lifecycle)
    {
        if (lifecycle == null) return;
        OrderLifecycleChanged?.Invoke(lifecycle);
        StateChanged?.Invoke();
    }

    private void PublishFailureOnce(string realId, string message)
    {
        bool publish;
        lock (_sync)
        {
            PurgeExpiredTerminalsLocked(_now());
            publish = _realOrders.TryGetValue(realId, out TrackedRealOrder? tracked) && !tracked.FailurePublished;
            if (publish) tracked!.FailurePublished = true;
        }
        if (publish) Failed?.Invoke(message);
    }

    private static void UpdateBrokerOrderId(TrackedRealOrder tracked, OrderInfo? order)
    {
        string brokerOrderId = BrokerOrderId(order);
        if (!string.IsNullOrWhiteSpace(brokerOrderId)) tracked.BrokerOrderId = brokerOrderId;
    }

    private static string BrokerOrderId(OrderInfo? order)
    {
        if (order == null) return string.Empty;
        if (!string.IsNullOrWhiteSpace(order.Identity.ExternalOrderId)) return order.Identity.ExternalOrderId;
        if (!string.IsNullOrWhiteSpace(order.Identity.OrdNo)) return order.Identity.OrdNo;
        return order.Identity.Cnt;
    }

    private static bool CanTransition(
        TrackedRealOrder tracked,
        OrderLifecycleState next,
        bool allowLateFill,
        bool allowCancelFailureRecovery)
    {
        if (tracked.Terminal)
            return allowLateFill && next is OrderLifecycleState.PartiallyFilled or OrderLifecycleState.Filled;
        if (!tracked.LifecycleState.HasValue) return next == OrderLifecycleState.Submitted;
        return tracked.LifecycleState.Value switch
        {
            OrderLifecycleState.Submitted => next is OrderLifecycleState.Working or
                OrderLifecycleState.CancelPending or OrderLifecycleState.PartiallyFilled or
                OrderLifecycleState.Filled or OrderLifecycleState.Cancelled or OrderLifecycleState.Rejected,
            OrderLifecycleState.Working => next is OrderLifecycleState.CancelPending or
                OrderLifecycleState.PartiallyFilled or OrderLifecycleState.Filled or
                OrderLifecycleState.Cancelled or OrderLifecycleState.Rejected,
            OrderLifecycleState.CancelPending =>
                (allowCancelFailureRecovery && next == OrderLifecycleState.Working) ||
                next is OrderLifecycleState.PartiallyFilled or OrderLifecycleState.Filled or
                    OrderLifecycleState.Cancelled or OrderLifecycleState.Rejected,
            OrderLifecycleState.PartiallyFilled => next is OrderLifecycleState.CancelPending or
                OrderLifecycleState.PartiallyFilled or OrderLifecycleState.Filled or
                OrderLifecycleState.Cancelled or OrderLifecycleState.Rejected,
            _ => false
        };
    }

    private void PurgeExpiredTerminalsLocked(DateTimeOffset now)
    {
        string[] expired = _realOrders
            .Where(x => x.Value.Terminal && !x.Value.AwaitingSettlement && x.Value.TerminalAt.HasValue &&
                        now - x.Value.TerminalAt.Value > TerminalRetention)
            .Select(x => x.Key)
            .ToArray();
        foreach (string realId in expired)
        {
            if (_realOrders.Remove(realId, out TrackedRealOrder? tracked))
                RetireClientOrderIdLocked(tracked.Order.ClientOrderId);
        }
    }

    private void RetireClientOrderIdLocked(string clientOrderId)
    {
        if (string.IsNullOrWhiteSpace(clientOrderId)) return;
        _clientOrderReservations.Remove(clientOrderId);
        _ownedClientOrderIds.Remove(clientOrderId);
    }

    private void ThrowIfProtocolViolationLocked()
    {
        if (_protocolViolation) throw new InvalidOperationException(ProtocolReconciliationMessage);
    }

    public void Dispose() => DisposeIfSafe(() => true);

    internal void DisposeIfSafe(Func<bool> isLedgerFlat)
    {
        lock (_sync)
        {
            if (_disposed) return;
            if (_disposing)
                throw new InvalidOperationException("實盤執行器正在釋放，請稍後重試。");
            PurgeExpiredTerminalsLocked(_now());
            int active = _realOrders.Values.Count(x => !x.Terminal);
            if (active > 0)
                throw new InvalidOperationException($"仍有 {active} 張實盤委託未終結，禁止釋放執行器。");
            if (_inflightCallbacks > 0 || _inflightOperations > 0)
                throw new InvalidOperationException($"仍有 {_inflightCallbacks} 個 callback／{_inflightOperations} 個操作尚未完成，禁止釋放執行器。");
            if (_clientOrderReservations.Count > 0)
                throw new InvalidOperationException($"仍有 {_clientOrderReservations.Count} 個 ClientOrderId 保留尚未提交，禁止釋放執行器。");
            int quarantined = _realOrders.Values.Count(x => x.Terminal && x.AwaitingSettlement);
            if (quarantined > 0)
                throw new InvalidOperationException($"仍有 {quarantined} 張已撤委託等待 late-fill settlement，禁止釋放執行器。");
            if (!isLedgerFlat())
                throw new InvalidOperationException("策略 ledger 尚未歸零，禁止釋放執行器。");
            _disposing = true;
        }
        _gateway.OrderFilled -= OnOrderFilled;
        _gateway.OrderFailed -= OnOrderFailed;
        _gateway.CancelFailed -= OnCancelFailed;
        _gateway.OrderStateChanged -= OnOrderChanged;
        bool changed;
        lock (_sync)
        {
            _disposed = true;
            changed = _conditions.Count > 0 || _realOrders.Count > 0;
            _conditions.Clear();
            _realOrders.Clear();
            _clientOrderReservations.Clear();
            _ownedClientOrderIds.Clear();
            _disposing = false;
            Monitor.PulseAll(_sync);
        }
        if (changed) StateChanged?.Invoke();
    }

    private bool TryEnterCallback()
    {
        lock (_sync)
        {
            if (_disposed || _disposing) return false;
            _inflightCallbacks++;
            int threadId = Environment.CurrentManagedThreadId;
            _callbackThreadDepth.TryGetValue(threadId, out int depth);
            _callbackThreadDepth[threadId] = depth + 1;
            return true;
        }
    }

    private void ExitCallback()
    {
        lock (_sync)
        {
            int threadId = Environment.CurrentManagedThreadId;
            if (_callbackThreadDepth.TryGetValue(threadId, out int depth))
            {
                if (depth <= 1) _callbackThreadDepth.Remove(threadId);
                else _callbackThreadDepth[threadId] = depth - 1;
            }
            _inflightCallbacks--;
            if (_inflightCallbacks == 0) Monitor.PulseAll(_sync);
        }
    }

    private void ThrowIfClosingLocked()
    {
        if (_disposed || _disposing)
            throw new ObjectDisposedException(nameof(KgiLiveStrategyExecutionService));
    }

    private void EnterOperation()
    {
        lock (_sync)
        {
            ThrowIfClosingLocked();
            _inflightOperations++;
            int threadId = Environment.CurrentManagedThreadId;
            _operationThreadDepth.TryGetValue(threadId, out int depth);
            _operationThreadDepth[threadId] = depth + 1;
        }
    }

    private void ExitOperation()
    {
        lock (_sync)
        {
            int threadId = Environment.CurrentManagedThreadId;
            if (_operationThreadDepth.TryGetValue(threadId, out int depth))
            {
                if (depth <= 1) _operationThreadDepth.Remove(threadId);
                else _operationThreadDepth[threadId] = depth - 1;
            }
            _inflightOperations--;
        }
    }
}
