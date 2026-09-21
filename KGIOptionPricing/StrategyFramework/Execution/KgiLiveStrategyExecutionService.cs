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
    private readonly Dictionary<string, PaperOrder> _conditions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TrackedRealOrder> _realOrders = new(StringComparer.OrdinalIgnoreCase);
    private long _localSequence;
    private long _executionSequence;
    private bool _disposed;
    private bool _disposing;
    private int _inflightCallbacks;
    private readonly Dictionary<int, int> _callbackThreadDepth = new();

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
        Action? beforeFilledPublished = null)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _instanceId = instanceId;
        _quoteSymbol = product.Symbol;
        _commission = Math.Max(0m, commissionPerContract);
        _now = now ?? (() => DateTimeOffset.Now);
        _beforeFilledPublished = beforeFilledPublished;
        _instrument = LiveStrategyOrderMapper.ToInstrument(contract);
        _gateway.OrderFilled += OnOrderFilled;
        _gateway.OrderFailed += OnOrderFailed;
        _gateway.CancelFailed += OnCancelFailed;
        _gateway.OrderStateChanged += OnOrderChanged;
    }

    public PaperOrder Submit(PaperOrderRequest request, DateTimeOffset now)
    {
        lock (_sync)
        {
            ThrowIfClosingLocked();
            PurgeExpiredTerminalsLocked(_now());
        }
        if (!_gateway.IsLoggedIn) throw new InvalidOperationException("交易服務尚未登入，禁止實盤送單。");
        if (!string.Equals(request.StrategyInstanceId, _instanceId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("策略實例與實盤執行器不一致。");
        if (!string.Equals(request.Symbol, _quoteSymbol, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("策略商品與實盤執行器不一致。");
        if (request.Quantity != 1) throw new InvalidOperationException("實盤策略第一版固定每次 1 口。");

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
            Quantity = 1,
            State = PaperOrderState.Working,
            CreatedAt = now
        };

        if (brokerLimit)
            SendBrokerLimit(request, order, now);
        else if (request.Type == PaperOrderType.Market)
            SendMarket(order, request.Price);
        else
        {
            lock (_sync) _conditions[order.OrderId] = order;
            StateChanged?.Invoke();
        }
        return order;
    }

    public bool Cancel(string orderId)
    {
        bool removedCondition;
        string? realId;
        lock (_sync)
        {
            ThrowIfClosingLocked();
            PurgeExpiredTerminalsLocked(_now());
            removedCondition = _conditions.Remove(orderId);
            realId = removedCondition
                ? null
                : _realOrders.FirstOrDefault(x => !x.Value.Terminal && x.Value.Order.OrderId == orderId).Key;
        }
        if (removedCondition)
        {
            StateChanged?.Invoke();
            return true;
        }
        return !string.IsNullOrEmpty(realId) && RequestCancel(realId);
    }

    public bool CancelByClientOrderId(string strategyInstanceId, string clientOrderId)
    {
        if (!string.Equals(strategyInstanceId, _instanceId, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(clientOrderId)) return false;

        string? realId;
        lock (_sync)
        {
            ThrowIfClosingLocked();
            PurgeExpiredTerminalsLocked(_now());
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

    public int CancelAll(string strategyInstanceId)
    {
        if (!string.Equals(strategyInstanceId, _instanceId, StringComparison.OrdinalIgnoreCase)) return 0;
        string[] realIds;
        int conditionCount;
        lock (_sync)
        {
            ThrowIfClosingLocked();
            PurgeExpiredTerminalsLocked(_now());
            conditionCount = _conditions.Count;
            _conditions.Clear();
            realIds = _realOrders.Where(x => !x.Value.Terminal).Select(x => x.Key).ToArray();
        }
        foreach (string realId in realIds) RequestCancel(realId);
        if (conditionCount > 0) StateChanged?.Invoke();
        return conditionCount + realIds.Length;
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
        if (marketEvent.Kind != MarketEventKind.Trade || marketEvent.LastPrice <= 0m ||
            !string.Equals(marketEvent.Symbol, _quoteSymbol, StringComparison.OrdinalIgnoreCase)) return;

        PaperOrder? triggered;
        lock (_sync)
        {
            ThrowIfClosingLocked();
            triggered = _conditions.Values.OrderBy(x => x.CreatedAt).FirstOrDefault(order => order.Type switch
            {
                PaperOrderType.Limit when order.Side == BuySell.Buy => marketEvent.LastPrice < order.Price,
                PaperOrderType.Limit => marketEvent.LastPrice > order.Price,
                PaperOrderType.Stop when order.Side == BuySell.Buy => marketEvent.LastPrice > order.Price,
                PaperOrderType.Stop => marketEvent.LastPrice < order.Price,
                _ => false
            });
            if (triggered != null) _conditions.Clear(); // Existing local OCO behavior.
        }
        if (triggered != null) SendMarket(triggered, marketEvent.LastPrice);
    }

    public void Flatten(int netPosition)
    {
        if (netPosition == 0) return;
        lock (_sync) ThrowIfClosingLocked();
        var order = new PaperOrder
        {
            OrderId = $"FLAT-{_instanceId}-{Interlocked.Increment(ref _localSequence)}",
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
        SendMarket(order, 0m);
    }

    private void SendBrokerLimit(PaperOrderRequest request, PaperOrder order, DateTimeOffset now)
    {
        OrderRequest brokerRequest = LiveStrategyOrderMapper.CreateBrokerLimit(
            request, _instrument, _gateway.BrokerId, _gateway.Account, order.OrderId);
        lock (_sync)
        {
            PurgeExpiredTerminalsLocked(_now());
            if (_realOrders.Values.Any(x => string.Equals(
                    x.Order.ClientOrderId, request.ClientOrderId, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"ClientOrderId 已存在：{request.ClientOrderId}");
            _realOrders[order.OrderId] = new TrackedRealOrder
            {
                Order = order,
                Trigger = request.Price,
                RemainingQuantity = order.Quantity
            };
        }
        PublishTransition(order.OrderId, OrderLifecycleState.Submitted, null, "Submitted", now);

        try
        {
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
        if (!_gateway.IsLoggedIn)
        {
            Failed?.Invoke("交易服務已離線，實盤市價單未送出。");
            return;
        }
        string realId = $"STRAT-{_instanceId}-{Interlocked.Increment(ref _localSequence)}";
        lock (_sync)
        {
            _realOrders[realId] = new TrackedRealOrder
            {
                Order = condition,
                Trigger = trigger,
                RemainingQuantity = condition.Quantity
            };
        }
        StateChanged?.Invoke();
        try
        {
            _gateway.PlaceOrder(new OrderRequest
            {
                OrderId = realId,
                Instrument = _instrument,
                BuySell = condition.Side,
                Price = 0d,
                Quantity = 1,
                TimeInForce = TimeInForce.IOC,
                PositionEffect = PositionEffect.Auto,
                BrokerId = _gateway.BrokerId,
                Account = _gateway.Account
            });
        }
        catch (Exception ex)
        {
            lock (_sync) _realOrders.Remove(realId);
            Failed?.Invoke("實盤送單例外：" + ex.GetBaseException().Message);
            StateChanged?.Invoke();
        }
    }

    private bool RequestCancel(string realId)
    {
        OrderLifecycleEvent? lifecycle = null;
        bool managed;
        lock (_sync)
        {
            PurgeExpiredTerminalsLocked(_now());
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
        lock (_sync)
        {
            PurgeExpiredTerminalsLocked(_now());
            if (!_realOrders.TryGetValue(brokerOrder.OrderId, out TrackedRealOrder? tracked)) return;
            UpdateBrokerOrderId(tracked, brokerOrder);

            int cumulative = brokerOrder.FilledQty > 0
                ? brokerOrder.FilledQty
                : tracked.DeliveredFilledQuantity + Math.Max(0, quantity);
            if (cumulative <= tracked.DeliveredFilledQuantity) return;
            int delta = cumulative - tracked.DeliveredFilledQuantity;
            tracked.DeliveredFilledQuantity = cumulative;
            tracked.FilledQuantity = Math.Max(tracked.FilledQuantity, cumulative);
            tracked.RemainingQuantity = brokerOrder.LeavesQty >= 0
                ? brokerOrder.LeavesQty
                : Math.Max(0, tracked.Order.Quantity - cumulative);

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

            bool partial = brokerOrder.Status == OrderStatus.PartiallyFilled || tracked.RemainingQuantity > 0;
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
                _realOrders.Remove(brokerOrder.OrderId);
                legacyTerminal = true;
            }
        }

        // Match paper execution ordering: ledger first, then terminal lifecycle.
        _beforeFilledPublished?.Invoke();
        Filled?.Invoke(execution);
        if (lifecycle != null) OrderLifecycleChanged?.Invoke(lifecycle);
        if (execution != null || lifecycle != null || legacyTerminal) StateChanged?.Invoke();
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
                legacyRemoved = _realOrders.Remove(brokerOrder.OrderId);
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
            if (!tracked.IsManaged)
            {
                if (brokerOrder.Status is OrderStatus.Filled or OrderStatus.Cancelled or OrderStatus.Rejected)
                    legacyChanged = _realOrders.Remove(brokerOrder.OrderId);
            }
            else
            {
                // A terminal tombstone advances only in OnOrderFilled, which
                // guarantees the execution reaches the ledger first.
                if (tracked.Terminal) return;
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
                            failure = string.IsNullOrWhiteSpace(brokerOrder.Message)
                                ? "實盤委託遭券商拒絕。"
                                : $"實盤委託失敗：{brokerOrder.Message}";
                        }
                        break;
                }
            }
        }
        if (lifecycle != null) OrderLifecycleChanged?.Invoke(lifecycle);
        if (failure != null) Failed?.Invoke(failure);
        if (lifecycle != null || legacyChanged) StateChanged?.Invoke();
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
        int filled = brokerOrder == null ? tracked.FilledQuantity : Math.Max(tracked.FilledQuantity, brokerOrder.FilledQty);
        int remaining = brokerOrder?.LeavesQty >= 0
            ? brokerOrder.LeavesQty
            : tracked.RemainingQuantity;
        bool terminal = state is OrderLifecycleState.Filled or OrderLifecycleState.Cancelled or OrderLifecycleState.Rejected;
        if (!CanTransition(tracked, state, allowLateFill, allowCancelFailureRecovery)) return null;
        if (tracked.LifecycleState == state &&
            tracked.FilledQuantity == filled && tracked.RemainingQuantity == remaining)
        {
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
            .Where(x => x.Value.Terminal && x.Value.TerminalAt.HasValue &&
                        now - x.Value.TerminalAt.Value > TerminalRetention)
            .Select(x => x.Key)
            .ToArray();
        foreach (string realId in expired) _realOrders.Remove(realId);
    }

    public void Dispose()
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
            if (_inflightCallbacks > 0)
                throw new InvalidOperationException($"仍有 {_inflightCallbacks} 個 broker callback 尚未發布完成，禁止釋放執行器。");
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
            _disposing = false;
            Monitor.PulseAll(_sync);
        }
        if (changed) StateChanged?.Invoke();
    }

    private bool TryEnterCallback()
    {
        lock (_sync)
        {
            if (_disposed) return false;
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
}
