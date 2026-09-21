using KGIOptionPricing.OrderManagement.Core;

namespace KGIOptionPricing.StrategyFramework.Execution;

/// <summary>
/// Routes strategy orders to the existing KGI OrderManager. Legacy Limit and
/// Stop requests remain local OCO conditions. BrokerLimit requests are sent
/// immediately as independent working ROD limit orders.
/// </summary>
public sealed class KgiLiveStrategyExecutionService : IManagedStrategyExecutionService, IDisposable
{
    private sealed class TrackedRealOrder
    {
        public required PaperOrder Order { get; init; }
        public decimal Trigger { get; init; }
        public OrderLifecycleState? LifecycleState { get; set; }
        public int FilledQuantity { get; set; }
        public int RemainingQuantity { get; set; }
    }

    private readonly object _sync = new();
    private readonly OrderManager _orders;
    private readonly KGITradeService _trade;
    private readonly string _instanceId;
    private readonly string _quoteSymbol;
    private readonly InstrumentKey _instrument;
    private readonly decimal _commission;
    private readonly Dictionary<string, PaperOrder> _conditions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TrackedRealOrder> _realOrders = new(StringComparer.OrdinalIgnoreCase);
    private long _localSequence;
    private long _executionSequence;
    private bool _disposed;

    public event Action<ExecutionEvent>? Filled;
    public event Action<string>? Failed;
    public event Action<OrderLifecycleEvent>? OrderLifecycleChanged;
    public event Action? StateChanged;
    public int ActiveBrokerOrderCount { get { lock (_sync) return _realOrders.Count; } }
    public int LocalConditionCount { get { lock (_sync) return _conditions.Count; } }

    public KgiLiveStrategyExecutionService(
        OrderManager orders,
        KGITradeService trade,
        string instanceId,
        ResolvedProduct product,
        ContractInfo contract,
        decimal commissionPerContract)
    {
        _orders = orders;
        _trade = trade;
        _instanceId = instanceId;
        _quoteSymbol = product.Symbol;
        _commission = Math.Max(0m, commissionPerContract);
        _instrument = ToInstrument(contract);
        _orders.OnOrderFilled += OnOrderFilled;
        _orders.OnOrderFailed += OnOrderFailed;
        _orders.OnCancelFailed += OnCancelFailed;
        _orders.OnOrderStateChanged += OnOrderChanged;
    }

    public PaperOrder Submit(PaperOrderRequest request, DateTimeOffset now)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(KgiLiveStrategyExecutionService));
        if (!_trade.IsLoggedIn) throw new InvalidOperationException("交易服務尚未登入，禁止實盤送單。");
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
        string? realId;
        lock (_sync)
        {
            if (_conditions.Remove(orderId))
            {
                StateChanged?.Invoke();
                return true;
            }
            realId = _realOrders.FirstOrDefault(x => x.Value.Order.OrderId == orderId).Key;
        }
        return !string.IsNullOrEmpty(realId) && RequestCancel(realId);
    }

    public bool CancelByClientOrderId(string strategyInstanceId, string clientOrderId)
    {
        if (_disposed ||
            !string.Equals(strategyInstanceId, _instanceId, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(clientOrderId)) return false;

        string? realId;
        lock (_sync)
        {
            string[] matches = _realOrders
                .Where(x => string.Equals(x.Value.Order.ClientOrderId, clientOrderId, StringComparison.OrdinalIgnoreCase))
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
        int count;
        lock (_sync)
        {
            count = _conditions.Count + _realOrders.Count;
            _conditions.Clear();
            realIds = _realOrders.Keys.ToArray();
        }
        foreach (string realId in realIds) RequestCancel(realId);
        StateChanged?.Invoke();
        return count;
    }

    public IReadOnlyList<PaperOrder> GetWorkingOrders(string strategyInstanceId)
    {
        if (!string.Equals(strategyInstanceId, _instanceId, StringComparison.OrdinalIgnoreCase)) return Array.Empty<PaperOrder>();
        lock (_sync) return _conditions.Values.Concat(_realOrders.Values.Select(x => x.Order)).ToArray();
    }

    public void OnMarketEvent(MarketEvent marketEvent)
    {
        if (_disposed || marketEvent.Kind != MarketEventKind.Trade || marketEvent.LastPrice <= 0m ||
            !string.Equals(marketEvent.Symbol, _quoteSymbol, StringComparison.OrdinalIgnoreCase)) return;

        PaperOrder? triggered;
        lock (_sync)
        {
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
            CreatedAt = DateTimeOffset.Now
        };
        SendMarket(order, 0m);
    }

    private void SendBrokerLimit(PaperOrderRequest request, PaperOrder order, DateTimeOffset now)
    {
        var tracked = new TrackedRealOrder
        {
            Order = order,
            Trigger = request.Price,
            RemainingQuantity = order.Quantity
        };
        lock (_sync) _realOrders[order.OrderId] = tracked;
        Transition(order.OrderId, OrderLifecycleState.Submitted, null, "Submitted", now);
        StateChanged?.Invoke();

        try
        {
            OrderRequest brokerRequest = LiveStrategyOrderMapper.CreateBrokerLimit(
                request, _instrument, _trade.BrokerID, _trade.Account, order.OrderId);
            _orders.PlaceOrder(brokerRequest);
        }
        catch (Exception ex)
        {
            Transition(order.OrderId, OrderLifecycleState.Rejected, null, "SubmitException", DateTimeOffset.Now, terminal: true);
            Failed?.Invoke("實盤限價單送單例外：" + ex.GetBaseException().Message);
        }
    }

    private void SendMarket(PaperOrder condition, decimal trigger)
    {
        if (!_trade.IsLoggedIn)
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
            _orders.PlaceOrder(new OrderRequest
            {
                OrderId = realId,
                Instrument = _instrument,
                BuySell = condition.Side,
                Price = 0d,
                Quantity = 1,
                TimeInForce = TimeInForce.IOC,
                PositionEffect = PositionEffect.Auto,
                BrokerId = _trade.BrokerID,
                Account = _trade.Account
            });
        }
        catch (Exception ex)
        {
            lock (_sync) _realOrders.Remove(realId);
            Failed?.Invoke("實盤送單例外：" + ex.GetBaseException().Message);
        }
    }

    private bool RequestCancel(string realId)
    {
        TrackedRealOrder tracked;
        lock (_sync)
        {
            if (!_realOrders.TryGetValue(realId, out tracked!)) return false;
            if (tracked.LifecycleState is OrderLifecycleState.CancelPending or
                OrderLifecycleState.Cancelled or OrderLifecycleState.Filled or OrderLifecycleState.Rejected)
                return false;
        }

        if (!string.IsNullOrWhiteSpace(tracked.Order.ClientOrderId))
            Transition(realId, OrderLifecycleState.CancelPending, null, "CancelRequested", DateTimeOffset.Now);
        _orders.CancelOrder(realId);
        return true;
    }

    private void OnOrderFilled(OrderInfo brokerOrder, int quantity, double price)
    {
        TrackedRealOrder tracked;
        lock (_sync)
            if (!_realOrders.TryGetValue(brokerOrder.OrderId, out tracked!)) return;

        int filled = Math.Max(tracked.FilledQuantity, brokerOrder.FilledQty);
        int remaining = brokerOrder.LeavesQty >= 0
            ? brokerOrder.LeavesQty
            : Math.Max(0, tracked.Order.Quantity - filled);
        bool terminal = brokerOrder.Status == OrderStatus.Filled || (filled >= tracked.Order.Quantity && remaining == 0);

        if (!string.IsNullOrWhiteSpace(tracked.Order.ClientOrderId))
        {
            Transition(
                brokerOrder.OrderId,
                terminal ? OrderLifecycleState.Filled : OrderLifecycleState.PartiallyFilled,
                brokerOrder,
                terminal ? "Filled" : "PartiallyFilled",
                DateTimeOffset.Now,
                terminal);
        }
        else if (terminal)
        {
            lock (_sync) _realOrders.Remove(brokerOrder.OrderId);
        }

        Filled?.Invoke(new ExecutionEvent
        {
            Sequence = Interlocked.Increment(ref _executionSequence),
            Timestamp = DateTimeOffset.Now,
            StrategyInstanceId = _instanceId,
            ClientOrderId = tracked.Order.ClientOrderId,
            OrderId = brokerOrder.OrderId,
            Symbol = _quoteSymbol,
            Side = tracked.Order.Side,
            Role = tracked.Order.Role,
            Price = (decimal)price,
            Quantity = quantity,
            Commission = _commission * quantity,
            TriggerMarketPrice = tracked.Trigger,
            MarketSequence = 0
        });
        StateChanged?.Invoke();
    }

    private void OnOrderFailed(OrderInfo brokerOrder, string reason)
    {
        TrackedRealOrder? tracked;
        lock (_sync) _realOrders.TryGetValue(brokerOrder.OrderId, out tracked);
        if (tracked == null) return;

        if (!string.IsNullOrWhiteSpace(tracked.Order.ClientOrderId))
            Transition(brokerOrder.OrderId, OrderLifecycleState.Rejected, brokerOrder, "Rejected", DateTimeOffset.Now, terminal: true);
        else
            lock (_sync) _realOrders.Remove(brokerOrder.OrderId);
        Failed?.Invoke($"實盤委託失敗：{reason}");
        StateChanged?.Invoke();
    }

    private void OnCancelFailed(OrderInfo brokerOrder, string reason)
    {
        lock (_sync)
            if (!_realOrders.ContainsKey(brokerOrder.OrderId)) return;
        Failed?.Invoke($"實盤刪單失敗：{reason}");
    }

    private void OnOrderChanged(OrderInfo brokerOrder)
    {
        TrackedRealOrder? tracked;
        lock (_sync) _realOrders.TryGetValue(brokerOrder.OrderId, out tracked);
        if (tracked == null) return;
        if (string.IsNullOrWhiteSpace(tracked.Order.ClientOrderId))
        {
            if (brokerOrder.Status is OrderStatus.Filled or OrderStatus.Cancelled or OrderStatus.Rejected)
            {
                lock (_sync) _realOrders.Remove(brokerOrder.OrderId);
                StateChanged?.Invoke();
            }
            return;
        }

        switch (brokerOrder.Status)
        {
            case OrderStatus.Accepted:
                Transition(brokerOrder.OrderId, OrderLifecycleState.Working, brokerOrder, "Working", DateTimeOffset.Now);
                break;
            case OrderStatus.CancelPending:
                Transition(brokerOrder.OrderId, OrderLifecycleState.CancelPending, brokerOrder, "CancelPending", DateTimeOffset.Now);
                break;
            case OrderStatus.PartiallyFilled:
                Transition(brokerOrder.OrderId, OrderLifecycleState.PartiallyFilled, brokerOrder, "PartiallyFilled", DateTimeOffset.Now);
                break;
            case OrderStatus.Filled:
                Transition(brokerOrder.OrderId, OrderLifecycleState.Filled, brokerOrder, "Filled", DateTimeOffset.Now, terminal: true);
                break;
            case OrderStatus.Cancelled:
                Transition(brokerOrder.OrderId, OrderLifecycleState.Cancelled, brokerOrder, "Cancelled", DateTimeOffset.Now, terminal: true);
                break;
            case OrderStatus.Rejected:
                Transition(brokerOrder.OrderId, OrderLifecycleState.Rejected, brokerOrder, "Rejected", DateTimeOffset.Now, terminal: true);
                Failed?.Invoke(string.IsNullOrWhiteSpace(brokerOrder.Message)
                    ? "實盤委託遭券商拒絕。"
                    : $"實盤委託失敗：{brokerOrder.Message}");
                break;
        }
    }

    private void Transition(
        string realId,
        OrderLifecycleState state,
        OrderInfo? brokerOrder,
        string reasonCode,
        DateTimeOffset timestamp,
        bool terminal = false)
    {
        OrderLifecycleEvent? lifecycle = null;
        lock (_sync)
        {
            if (!_realOrders.TryGetValue(realId, out TrackedRealOrder? tracked)) return;

            int filled = brokerOrder == null ? tracked.FilledQuantity : Math.Max(tracked.FilledQuantity, brokerOrder.FilledQty);
            int remaining = brokerOrder?.LeavesQty >= 0
                ? brokerOrder.LeavesQty
                : Math.Max(0, tracked.Order.Quantity - filled);
            if (tracked.LifecycleState == state &&
                tracked.FilledQuantity == filled && tracked.RemainingQuantity == remaining) return;

            OrderLifecycleState previous = tracked.LifecycleState ?? OrderLifecycleState.Submitted;
            tracked.LifecycleState = state;
            tracked.FilledQuantity = filled;
            tracked.RemainingQuantity = remaining;
            lifecycle = new OrderLifecycleEvent
            {
                Timestamp = timestamp,
                StrategyInstanceId = _instanceId,
                ClientOrderId = tracked.Order.ClientOrderId,
                OrderId = tracked.Order.OrderId,
                BrokerOrderId = BrokerOrderId(brokerOrder),
                PreviousState = previous,
                State = state,
                Price = tracked.Order.Price,
                Quantity = tracked.Order.Quantity,
                FilledQuantity = filled,
                RemainingQuantity = remaining,
                ReasonCode = reasonCode
            };
            if (terminal) _realOrders.Remove(realId);
        }

        OrderLifecycleChanged?.Invoke(lifecycle);
        StateChanged?.Invoke();
    }

    private static string BrokerOrderId(OrderInfo? order)
    {
        if (order == null) return string.Empty;
        if (!string.IsNullOrWhiteSpace(order.Identity.ExternalOrderId)) return order.Identity.ExternalOrderId;
        if (!string.IsNullOrWhiteSpace(order.Identity.OrdNo)) return order.Identity.OrdNo;
        return order.Identity.Cnt;
    }

    private static InstrumentKey ToInstrument(ContractInfo contract)
    {
        bool foreign = !string.IsNullOrWhiteSpace(contract.ExchangeNm) &&
                       !contract.ExchangeNm.Equals("TIMEX", StringComparison.OrdinalIgnoreCase) &&
                       !contract.ExchangeNm.Equals("TAIFEX", StringComparison.OrdinalIgnoreCase);
        bool option = contract.CPF is CPFEnum.Call or CPFEnum.Put || (contract.ComType != '\0' && contract.ComType != 'F');
        return new InstrumentKey
        {
            Market = foreign ? (option ? MarketType.ForeignOption : MarketType.ForeignFuture) :
                (option ? MarketType.DomesticOption : MarketType.DomesticFuture),
            Exchange = contract.ExchangeNm,
            Symbol = foreign && !string.IsNullOrWhiteSpace(contract.ProductId) ? contract.ProductId : contract.Symbol,
            ContractMonth = !string.IsNullOrWhiteSpace(contract.ComYM2) ? contract.ComYM2 :
                !string.IsNullOrWhiteSpace(contract.ComYM) ? contract.ComYM : contract.Month
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelAll(_instanceId);
        _orders.OnOrderFilled -= OnOrderFilled;
        _orders.OnOrderFailed -= OnOrderFailed;
        _orders.OnCancelFailed -= OnCancelFailed;
        _orders.OnOrderStateChanged -= OnOrderChanged;
    }
}
