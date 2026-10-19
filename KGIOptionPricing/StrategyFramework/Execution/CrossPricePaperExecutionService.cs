using System;
using System.Collections.Generic;
using System.Linq;
using KGIOptionPricing.OrderManagement.Core;

namespace KGIOptionPricing.StrategyFramework.Execution;

/// <summary>
/// Deterministic paper broker. A touch does not fill; a trade must pass strictly through
/// limit/stop prices. Those fills use the requested price. By default Market
/// fills use the next trade; offline research may opt into next valid contra book.
/// </summary>
public sealed class CrossPricePaperExecutionService : IPaperExecutionService, IManagedStrategyExecutionService
{
    private readonly object _sync = new();
    private readonly Dictionary<string, PaperOrder> _orders = new(StringComparer.OrdinalIgnoreCase);
    private readonly ManagedRestingPairCoordinator _restingPairs = new();
    private readonly decimal _commissionPerContract;
    private readonly IStrategyClock _clock;
    private readonly bool _fillMarketOnBook;
    private readonly bool _allowLimitIoc;
    private readonly TimeSpan _maxBookAge;
    private readonly Dictionary<string,(MarketEvent Book,int BuyUsed,int SellUsed)> _liquidity = new(StringComparer.OrdinalIgnoreCase);
    private long _orderSequence;
    private long _executionSequence;

    public event Action<ExecutionEvent>? Filled;
    public event Action<string>? Failed;
    public event Action<OrderLifecycleEvent>? OrderLifecycleChanged;

    public CrossPricePaperExecutionService(decimal commissionPerContract = 0m, IStrategyClock? clock = null,
        bool fillMarketOnBook = false, bool allowLimitIoc = false, TimeSpan? maxBookAge = null)
    {
        _commissionPerContract = Math.Max(0m, commissionPerContract);
        _clock = clock ?? new SystemStrategyClock();
        _fillMarketOnBook = fillMarketOnBook;
        _allowLimitIoc = allowLimitIoc;
        _maxBookAge = maxBookAge ?? TimeSpan.FromSeconds(2);
    }

    public PaperOrder Submit(PaperOrderRequest request, DateTimeOffset now)
    {
        if (request.Price <= 0m) throw new ArgumentOutOfRangeException(nameof(request.Price));
        if (request.Quantity <= 0) throw new ArgumentOutOfRangeException(nameof(request.Quantity));
        if (request.TimeInForce != TimeInForce.ROD && !(request.TimeInForce == TimeInForce.IOC && _allowLimitIoc && request.Type == PaperOrderType.BrokerLimit))
            throw new ArgumentException("Unsupported limit time in force");
        if (request.RestingPairMode == RestingPairMode.OppositeLegIsExit &&
            (request.Type != PaperOrderType.BrokerLimit || request.TimeInForce != TimeInForce.ROD))
            throw new ArgumentException("Resting pairs require broker-limit ROD orders.");
        var order = new PaperOrder
        {
            RestingPairMode = request.RestingPairMode,
            PairId = request.PairId,
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
            TimeInForce = request.TimeInForce,
            Role = request.Role,
            Price = request.Price,
            Quantity = request.Quantity,
            State = PaperOrderState.Working,
            CreatedAt = now
        };
        lock (_sync)
        {
            if (order.RestingPairMode == RestingPairMode.OppositeLegIsExit)
            {
                var key = new RestingPairKey(order.StrategyInstanceId, order.StrategyRunGeneration,
                    order.CycleId, order.PairId);
                RestingPairDecision registration = _restingPairs.RegisterLeg(key, order.ClientOrderId,
                    order.OrderId, order.Side, order.Role, order.Quantity);
                if (registration.Phase == RestingPairPhase.Quarantined)
                    throw new InvalidOperationException("Paired order registration could not be uniquely attributed.");
            }
            _orders[order.OrderId] = order;
        }
        PublishLifecycle(order, OrderLifecycleState.Submitted, OrderLifecycleState.Submitted, now, "Submitted");
        PublishLifecycle(order, OrderLifecycleState.Submitted, OrderLifecycleState.Working, now, "Working");
        if (order.TimeInForce == TimeInForce.IOC) MatchIoc(order, now);
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
        lock (_sync)
        {
            if (marketEvent.Kind == MarketEventKind.Connection) _liquidity.Clear();
            if (marketEvent.Kind == MarketEventKind.Book)
            {
                bool newer = !_liquidity.TryGetValue(marketEvent.Symbol, out var old) ||
                    (marketEvent.ConnectionGeneration != old.Book.ConnectionGeneration) ||
                    (marketEvent.Timestamp >= old.Book.Timestamp && marketEvent.Sequence > old.Book.Sequence);
                if (newer) _liquidity[marketEvent.Symbol] = (marketEvent, 0, 0);
            }
        }
        bool trade = marketEvent.Kind == MarketEventKind.Trade && marketEvent.LastPrice > 0m;
        bool book = _fillMarketOnBook && marketEvent.Kind == MarketEventKind.Book &&
            marketEvent.BestBid > 0m && marketEvent.BestAsk > marketEvent.BestBid;
        if (!trade && !book) return;
        List<ExecutionEvent> fills = new();
        List<PaperOrder> filledOrders = new();
        List<PaperOrder> cancelledOcoSiblings = new();
        lock (_sync)
        {
            PaperOrder[] candidates = _orders.Values
                .Where(x => x.State == PaperOrderState.Working && x.TimeInForce == TimeInForce.ROD &&
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
                    PaperOrderType.Market when trade => true,
                    PaperOrderType.Market when book && order.Side == BuySell.Buy
                        => marketEvent.AskQuantities.Length > 0 && marketEvent.AskQuantities[0] >= order.Quantity,
                    PaperOrderType.Market when book
                        => marketEvent.BidQuantities.Length > 0 && marketEvent.BidQuantities[0] >= order.Quantity,
                    PaperOrderType.Limit or PaperOrderType.BrokerLimit when trade && order.Side == BuySell.Buy
                        => marketEvent.LastPrice < order.Price,
                    PaperOrderType.Limit or PaperOrderType.BrokerLimit when trade
                        => marketEvent.LastPrice > order.Price,
                    PaperOrderType.Stop when trade && order.Side == BuySell.Buy => marketEvent.LastPrice > order.Price,
                    PaperOrderType.Stop when trade => marketEvent.LastPrice < order.Price,
                    _ => false
                };
                if (!crossed) continue;

                long executionSequence = ++_executionSequence;
                RestingPairDecision? pairedFill = null;
                if (order.RestingPairMode == RestingPairMode.OppositeLegIsExit)
                {
                    var key = new RestingPairKey(order.StrategyInstanceId, order.StrategyRunGeneration,
                        order.CycleId, order.PairId);
                    pairedFill = _restingPairs.RecordFill(key, order.ClientOrderId, order.OrderId,
                        $"SIM-FILL-{executionSequence}", order.Quantity, order.Quantity, executionSequence);
                    if (pairedFill.Phase == RestingPairPhase.Quarantined || pairedFill.EffectiveRole == null)
                        throw new InvalidOperationException("Paired paper fill could not be uniquely attributed.");
                }

                _orders[order.OrderId] = order with { State = PaperOrderState.Filled };
                filledOrders.Add(order with { State = PaperOrderState.Filled });
                if (order.RestingPairMode == RestingPairMode.None && !string.IsNullOrWhiteSpace(order.OcoGroupId))
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
                    Sequence = executionSequence,
                    ExecutionId = $"SIM-FILL-{executionSequence}",
                    CumulativeFilledQuantity = order.Quantity,
                    Timestamp = marketEvent.Timestamp,
                    PairId = order.PairId,
                    OriginalOrderRole = pairedFill == null ? null : order.Role,
                    StrategyInstanceId = order.StrategyInstanceId,
                    ClientOrderId = order.ClientOrderId,
                    CycleId = order.CycleId,
                    StrategyRunGeneration = order.StrategyRunGeneration,
                    Generation = order.Generation,
                    OrderId = order.OrderId,
                    Symbol = order.Symbol,
                    Side = order.Side,
                    Role = pairedFill?.EffectiveRole ?? order.Role,
                    Price = order.Type == PaperOrderType.Market
                        ? book ? order.Side == BuySell.Buy ? marketEvent.BestAsk : marketEvent.BestBid
                            : marketEvent.LastPrice
                        : order.Price,
                    Quantity = order.Quantity,
                    Commission = _commissionPerContract * order.Quantity,
                    TriggerMarketPrice = book
                        ? order.Side == BuySell.Buy ? marketEvent.BestAsk : marketEvent.BestBid
                        : marketEvent.LastPrice,
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
            PairId = order.PairId,
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
            TimeInForce = order.TimeInForce,
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

    private void MatchIoc(PaperOrder order, DateTimeOffset now)
    {
        int quantity=0; MarketEvent? book=null; long sequence=0;
        lock (_sync)
        {
            if (_liquidity.TryGetValue(order.Symbol,out var liquidity))
            {
                book=liquidity.Book;
                bool valid=book.BestBid>0 && book.BestAsk>book.BestBid && book.Timestamp<=now && now-book.Timestamp<=_maxBookAge;
                bool crossed=valid && (order.Side==BuySell.Buy ? order.Price>book.BestAsk : order.Price<book.BestBid);
                int available=order.Side==BuySell.Buy ? book.AskQuantities.FirstOrDefault()-liquidity.BuyUsed : book.BidQuantities.FirstOrDefault()-liquidity.SellUsed;
                quantity=crossed ? Math.Min(order.Quantity,Math.Max(0,available)) : 0;
                _liquidity[order.Symbol]=(book,liquidity.BuyUsed+(order.Side==BuySell.Buy?quantity:0),liquidity.SellUsed+(order.Side==BuySell.Sell?quantity:0));
            }
            _orders[order.OrderId]=order with{State=quantity==order.Quantity?PaperOrderState.Filled:PaperOrderState.Cancelled};
            if(quantity>0) sequence=++_executionSequence;
        }
        if(quantity>0)
        {
            Filled?.Invoke(new ExecutionEvent{Sequence=sequence,ExecutionId=$"SIM-FILL-{sequence}",CumulativeFilledQuantity=quantity,
                Timestamp=now,StrategyInstanceId=order.StrategyInstanceId,StrategyRunGeneration=order.StrategyRunGeneration,
                ClientOrderId=order.ClientOrderId,CycleId=order.CycleId,OrderId=order.OrderId,Symbol=order.Symbol,Side=order.Side,Role=order.Role,
                Price=order.Price,Quantity=quantity,Commission=_commissionPerContract*quantity,MarketSequence=book?.Sequence??0,
                TriggerMarketPrice=order.Side==BuySell.Buy?book!.BestAsk:book!.BestBid});
            if(quantity<order.Quantity) PublishLifecycle(order,OrderLifecycleState.Working,OrderLifecycleState.PartiallyFilled,now,"PartialFill",quantity,book?.Sequence??0);
        }
        PublishLifecycle(order,OrderLifecycleState.Working,quantity==order.Quantity?OrderLifecycleState.Filled:OrderLifecycleState.Cancelled,
            now,quantity==order.Quantity?"Filled":"IocAwaitFreshBook",quantity,book?.Sequence??0);
    }
}
