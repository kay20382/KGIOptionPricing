using System;
using System.Collections.Generic;
using System.Linq;
using KGIOptionPricing.OrderManagement.Core;

namespace KGIOptionPricing.StrategyFramework.Execution;

/// <summary>
/// Deterministic paper broker. A touch does not fill; a trade must pass strictly through
/// limit/stop prices. Those fills use the requested price; Market fills use the next trade price.
/// </summary>
public sealed class CrossPricePaperExecutionService : IPaperExecutionService, IManagedStrategyExecutionService
{
    private readonly object _sync = new();
    private readonly Dictionary<string, PaperOrder> _orders = new(StringComparer.OrdinalIgnoreCase);
    private readonly decimal _commissionPerContract;
    private readonly IStrategyClock _clock;
    private long _orderSequence;
    private long _executionSequence;

    public event Action<ExecutionEvent>? Filled;
    public event Action<string>? Failed;
    public event Action<OrderLifecycleEvent>? OrderLifecycleChanged;

    public CrossPricePaperExecutionService(decimal commissionPerContract = 0m, IStrategyClock? clock = null)
    {
        _commissionPerContract = Math.Max(0m, commissionPerContract);
        _clock = clock ?? new SystemStrategyClock();
    }

    public PaperOrder Submit(PaperOrderRequest request, DateTimeOffset now)
    {
        if (request.Price <= 0m) throw new ArgumentOutOfRangeException(nameof(request.Price));
        if (request.Quantity <= 0) throw new ArgumentOutOfRangeException(nameof(request.Quantity));
        var order = new PaperOrder
        {
            OrderId = $"SIM-{request.StrategyInstanceId}-{System.Threading.Interlocked.Increment(ref _orderSequence)}",
            ClientOrderId = request.ClientOrderId,
            CycleId = request.CycleId,
            OcoGroupId = request.OcoGroupId,
            StrategyRunGeneration = request.StrategyRunGeneration,
            Generation = request.Generation,
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
        lock (_sync) _orders[order.OrderId] = order;
        PublishLifecycle(order, OrderLifecycleState.Submitted, OrderLifecycleState.Submitted, now, "Submitted");
        PublishLifecycle(order, OrderLifecycleState.Submitted, OrderLifecycleState.Working, now, "Working");
        return order;
    }

    public bool Cancel(string orderId)
    {
        PaperOrder? cancelled;
        lock (_sync)
        {
            if (!_orders.TryGetValue(orderId, out PaperOrder? order) || order.State != PaperOrderState.Working)
                return false;
            cancelled = order with { State = PaperOrderState.Cancelled };
            _orders[orderId] = cancelled;
        }
        DateTimeOffset now = _clock.Now;
        PublishLifecycle(cancelled, OrderLifecycleState.Working, OrderLifecycleState.CancelPending, now, "CancelRequested");
        PublishLifecycle(cancelled, OrderLifecycleState.CancelPending, OrderLifecycleState.Cancelled, now, "Cancelled");
        return true;
    }

    public bool CancelByClientOrderId(string strategyInstanceId, string clientOrderId)
    {
        PaperOrder? cancelled;
        lock (_sync)
        {
            PaperOrder[] matches = _orders.Values.Where(x =>
                    x.State == PaperOrderState.Working &&
                    string.Equals(x.StrategyInstanceId, strategyInstanceId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(x.ClientOrderId, clientOrderId, StringComparison.OrdinalIgnoreCase))
                .Take(2).ToArray();
            if (matches.Length != 1) return false;
            cancelled = matches[0] with { State = PaperOrderState.Cancelled };
            _orders[cancelled.OrderId] = cancelled;
        }
        DateTimeOffset now = _clock.Now;
        PublishLifecycle(cancelled, OrderLifecycleState.Working, OrderLifecycleState.CancelPending, now, "CancelRequested");
        PublishLifecycle(cancelled, OrderLifecycleState.CancelPending, OrderLifecycleState.Cancelled, now, "Cancelled");
        return true;
    }

    public int CancelAll(string strategyInstanceId)
    {
        PaperOrder[] cancelled;
        lock (_sync)
        {
            cancelled = _orders.Values
                .Where(x => x.State == PaperOrderState.Working &&
                            string.Equals(x.StrategyInstanceId, strategyInstanceId, StringComparison.OrdinalIgnoreCase))
                .Select(x => x with { State = PaperOrderState.Cancelled }).ToArray();
            foreach (PaperOrder order in cancelled) _orders[order.OrderId] = order;
        }
        DateTimeOffset now = _clock.Now;
        foreach (PaperOrder order in cancelled)
        {
            PublishLifecycle(order, OrderLifecycleState.Working, OrderLifecycleState.CancelPending, now, "CancelRequested");
            PublishLifecycle(order, OrderLifecycleState.CancelPending, OrderLifecycleState.Cancelled, now, "Cancelled");
        }
        return cancelled.Length;
    }

    public IReadOnlyList<PaperOrder> GetWorkingOrders(string strategyInstanceId)
    {
        lock (_sync)
            return _orders.Values.Where(x => x.State == PaperOrderState.Working &&
                string.Equals(x.StrategyInstanceId, strategyInstanceId, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    public void OnMarketEvent(MarketEvent marketEvent)
    {
        if (marketEvent.Kind != MarketEventKind.Trade || marketEvent.LastPrice <= 0m) return;
        List<ExecutionEvent> fills = new();
        List<PaperOrder> filledOrders = new();
        List<PaperOrder> cancelledOcoSiblings = new();
        lock (_sync)
        {
            PaperOrder[] candidates = _orders.Values
                .Where(x => x.State == PaperOrderState.Working &&
                            string.Equals(x.Symbol, marketEvent.Symbol, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.CreatedAt).ThenBy(x => x.OrderId).ToArray();

            foreach (PaperOrder candidateOrder in candidates)
            {
                // A prior fill in this same market event may already have cancelled
                // this candidate as its OCO sibling. Re-read authoritative paper state.
                if (!_orders.TryGetValue(candidateOrder.OrderId, out PaperOrder? current) || current.State != PaperOrderState.Working)
                    continue;
                PaperOrder order = current;
                bool crossed = order.Type switch
                {
                    PaperOrderType.Market => true,
                    PaperOrderType.Limit or PaperOrderType.BrokerLimit when order.Side == BuySell.Buy
                        => marketEvent.LastPrice < order.Price,
                    PaperOrderType.Limit or PaperOrderType.BrokerLimit
                        => marketEvent.LastPrice > order.Price,
                    PaperOrderType.Stop when order.Side == BuySell.Buy => marketEvent.LastPrice > order.Price,
                    PaperOrderType.Stop => marketEvent.LastPrice < order.Price,
                    _ => false
                };
                if (!crossed) continue;

                _orders[order.OrderId] = order with { State = PaperOrderState.Filled };
                filledOrders.Add(order with { State = PaperOrderState.Filled });
                if (!string.IsNullOrWhiteSpace(order.OcoGroupId))
                {
                    bool isEntry = order.Role is PaperOrderRole.EntryLong or PaperOrderRole.EntryShort;
                    bool isExit = order.Role is PaperOrderRole.TakeProfit or PaperOrderRole.StopLoss or PaperOrderRole.Flatten;
                    PaperOrderRole oppositeEntryRole = order.Role == PaperOrderRole.EntryLong
                        ? PaperOrderRole.EntryShort : PaperOrderRole.EntryLong;
                    PaperOrder[] siblings = _orders.Values.Where(candidate =>
                        candidate.State == PaperOrderState.Working &&
                        string.Equals(candidate.StrategyInstanceId, order.StrategyInstanceId, StringComparison.OrdinalIgnoreCase) &&
                        candidate.StrategyRunGeneration == order.StrategyRunGeneration &&
                        string.Equals(candidate.CycleId, order.CycleId, StringComparison.Ordinal) &&
                        string.Equals(candidate.OcoGroupId, order.OcoGroupId, StringComparison.Ordinal) &&
                        candidate.OrderId != order.OrderId &&
                        (isEntry && candidate.Role == oppositeEntryRole ||
                         isExit && candidate.Role is PaperOrderRole.TakeProfit or PaperOrderRole.StopLoss or PaperOrderRole.Flatten)).ToArray();
                    foreach (PaperOrder sibling in siblings)
                    {
                        PaperOrder cancelled = sibling with { State = PaperOrderState.Cancelled };
                        _orders[sibling.OrderId] = cancelled;
                        cancelledOcoSiblings.Add(cancelled);
                    }
                }
                fills.Add(new ExecutionEvent
                {
                    Sequence = ++_executionSequence,
                    Timestamp = marketEvent.Timestamp,
                    StrategyInstanceId = order.StrategyInstanceId,
                    ClientOrderId = order.ClientOrderId,
                    CycleId = order.CycleId,
                    StrategyRunGeneration = order.StrategyRunGeneration,
                    Generation = order.Generation,
                    OrderId = order.OrderId,
                    Symbol = order.Symbol,
                    Side = order.Side,
                    Role = order.Role,
                    Price = order.Type == PaperOrderType.Market ? marketEvent.LastPrice : order.Price,
                    Quantity = order.Quantity,
                    Commission = _commissionPerContract * order.Quantity,
                    TriggerMarketPrice = marketEvent.LastPrice,
                    MarketSequence = marketEvent.Sequence
                });
            }
        }
        for (int i = 0; i < fills.Count; i++)
        {
            Filled?.Invoke(fills[i]);
            PublishLifecycle(filledOrders[i], OrderLifecycleState.Working, OrderLifecycleState.Filled,
                marketEvent.Timestamp, "Filled", filledOrders[i].Quantity, marketEvent.Sequence);
        }
        foreach (PaperOrder sibling in cancelledOcoSiblings)
        {
            PublishLifecycle(sibling, OrderLifecycleState.Working, OrderLifecycleState.CancelPending,
                marketEvent.Timestamp, "CancelRequested", triggerMarketSequence: marketEvent.Sequence);
            PublishLifecycle(sibling, OrderLifecycleState.CancelPending, OrderLifecycleState.Cancelled,
                marketEvent.Timestamp, "OcoSiblingCancelled", triggerMarketSequence: marketEvent.Sequence);
        }
    }

    private void PublishLifecycle(
        PaperOrder order,
        OrderLifecycleState previousState,
        OrderLifecycleState state,
        DateTimeOffset timestamp,
        string reasonCode,
        int filledQuantity = 0,
        long triggerMarketSequence = 0)
    {
        OrderLifecycleChanged?.Invoke(new OrderLifecycleEvent
        {
            Timestamp = timestamp,
            StrategyInstanceId = order.StrategyInstanceId,
            ClientOrderId = order.ClientOrderId,
            CycleId = order.CycleId,
            StrategyRunGeneration = order.StrategyRunGeneration,
            Generation = order.Generation,
            OrderId = order.OrderId,
            Symbol = order.Symbol,
            Side = order.Side,
            Role = order.Role,
            TriggerMarketSequence = triggerMarketSequence,
            PreviousState = previousState,
            State = state,
            Price = order.Price,
            Quantity = order.Quantity,
            FilledQuantity = filledQuantity,
            DeliveredFilledQuantity = filledQuantity,
            BrokerFinalCumulativeFilledQuantity = state is OrderLifecycleState.Filled or OrderLifecycleState.Cancelled or OrderLifecycleState.Rejected
                ? filledQuantity : null,
            RemainingQuantity = order.Quantity - filledQuantity,
            HasFinalCumulativeFillWatermark = state is OrderLifecycleState.Filled or OrderLifecycleState.Cancelled or OrderLifecycleState.Rejected,
            ReasonCode = reasonCode
        });
    }
}
