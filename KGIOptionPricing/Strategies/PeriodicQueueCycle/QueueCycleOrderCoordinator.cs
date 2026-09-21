using KGIOptionPricing.OrderManagement.Core;
using KGIOptionPricing.StrategyFramework;

namespace KGIOptionPricing.Strategies.PeriodicQueueCycle;

internal enum QueueCyclePhase
{
    WarmingUp,
    Ready,
    SubmittingPair,
    WorkingBoth,
    WorkingOneLeg,
    CancellingForReprice,
    CancellingForExit,
    Flattening,
    CoolingDown,
    Suspended,
    Stopped,
    Faulted
}

internal sealed class QueueCycleOrderCoordinator
{
    private enum LegState { Working, CancelPending, Filled, Cancelled }
    private enum CancelIntent { None, Reprice, Exit, LateFillSafety, Fault }

    private sealed class OrderLeg
    {
        internal required BuySell Side { get; init; }
        internal required string CycleId { get; init; }
        internal required int RepriceThresholdB { get; init; }
        internal required int RepriceTargetC { get; init; }
        internal int Generation { get; set; } = 1;
        internal required decimal CurrentPrice { get; set; }
        internal required string ClientOrderId { get; set; }
        internal HashSet<string> ClientOrderIds { get; } = new(StringComparer.Ordinal);
        internal HashSet<string> FilledClientOrderIds { get; } = new(StringComparer.Ordinal);
        internal LegState State { get; set; } = LegState.Working;
        internal bool Submitted { get; set; }
        internal bool HasFill { get; set; }
        internal decimal? PendingReplacementPrice { get; set; }
        internal CancelIntent CancelIntent { get; set; }
    }

    private readonly string _instanceId;
    private readonly string _symbol;
    private readonly PeriodicQueueCycleParameters _parameters;
    private readonly QueueCyclePricingPolicy _pricing = new();
    private readonly HashSet<string> _retiredClientOrderIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _retiredFilledClientOrderIds = new(StringComparer.Ordinal);
    private OrderLeg? _buy;
    private OrderLeg? _sell;

    internal QueueCycleOrderCoordinator(
        string instanceId,
        string symbol,
        PeriodicQueueCycleParameters parameters)
    {
        _instanceId = instanceId;
        _symbol = symbol;
        _parameters = parameters;
    }

    internal QueueCyclePhase Phase { get; private set; } = QueueCyclePhase.Ready;
    internal string ReasonCode { get; private set; } = "WarmupIncomplete";
    internal long CycleNumber { get; private set; }
    internal CycleDirection FrozenDirection { get; private set; } = CycleDirection.Neutral;
    internal DirectionAssessment? FrozenAssessment { get; private set; }
    internal FrozenPriceBounds? FrozenBounds { get; private set; }
    internal bool IsFaulted => Phase == QueueCyclePhase.Faulted;
    internal bool BothUnfilled => _buy is not null && _sell is not null &&
        !_buy.HasFill && !_sell.HasFill;
    internal bool HasOutstandingOrders => IsOutstanding(_buy) || IsOutstanding(_sell);
    internal string CycleSummary =>
        $"Cycle={_buy?.CycleId ?? "none"};Direction={FrozenDirection};" +
        $"Buy={LegSummary(_buy)};Sell={LegSummary(_sell)}";

    internal event Action<PaperOrderRequest>? OrderRequested;
    internal event Action<string>? CancelOrderRequested;

    internal bool TryStart(
        CycleDirection direction,
        DirectionAssessment assessment,
        FrozenPriceBounds bounds,
        MarketEvent book)
    {
        if (direction == CycleDirection.Neutral || Phase is QueueCyclePhase.SubmittingPair or QueueCyclePhase.WorkingBoth)
            return false;

        int buyInitial = direction == CycleDirection.Up
            ? _parameters.TrendInitialAheadA
            : _parameters.OppositeInitialAheadA;
        int sellInitial = direction == CycleDirection.Up
            ? _parameters.OppositeInitialAheadA
            : _parameters.TrendInitialAheadA;
        QueuePriceDecision buy = _pricing.SelectInitial(BuySell.Buy, book, buyInitial, bounds);
        QueuePriceDecision sell = _pricing.SelectInitial(BuySell.Sell, book, sellInitial, bounds);
        if (buy.Action != QueuePriceAction.Place || sell.Action != QueuePriceAction.Place)
        {
            ReasonCode = buy.Action != QueuePriceAction.Place ? buy.ReasonCode : sell.ReasonCode;
            return false;
        }

        Phase = QueueCyclePhase.SubmittingPair;
        CycleNumber++;
        FrozenDirection = direction;
        FrozenAssessment = assessment;
        FrozenBounds = bounds;
        string cycleId = $"cycle-{CycleNumber}";
        _buy = CreateLeg(cycleId, BuySell.Buy, buy.Price,
            direction == CycleDirection.Up ? _parameters.TrendRepriceThresholdB : _parameters.OppositeRepriceThresholdB,
            direction == CycleDirection.Up ? _parameters.TrendRepriceTargetC : _parameters.OppositeRepriceTargetC);
        _sell = CreateLeg(cycleId, BuySell.Sell, sell.Price,
            direction == CycleDirection.Up ? _parameters.OppositeRepriceThresholdB : _parameters.TrendRepriceThresholdB,
            direction == CycleDirection.Up ? _parameters.OppositeRepriceTargetC : _parameters.TrendRepriceTargetC);

        PaperOrderRequest buyOrder = CreateOrder(_buy);
        PaperOrderRequest sellOrder = CreateOrder(_sell);
        _buy.Submitted = true;
        OrderRequested?.Invoke(buyOrder);
        if (IsFaulted) return true;
        _sell.Submitted = true;
        OrderRequested?.Invoke(sellOrder);
        if (IsFaulted) return true;
        UpdateWorkingPhase();
        if (!_buy.HasFill && !_sell.HasFill) ReasonCode = "SignalAccepted";
        return true;
    }

    internal void EvaluateReprices(MarketEvent book)
    {
        if (FrozenBounds is null || IsFaulted) return;
        EvaluateReprice(_buy, book);
        if (IsFaulted) return;
        EvaluateReprice(_sell, book);
        UpdateWorkingPhase();
    }

    internal bool ResetCancelledFlatCycle()
    {
        if (HasOutstandingOrders || _buy?.HasFill == true || _sell?.HasFill == true) return false;
        Retire(_buy);
        Retire(_sell);
        _buy = null;
        _sell = null;
        FrozenDirection = CycleDirection.Neutral;
        FrozenAssessment = null;
        FrozenBounds = null;
        Phase = QueueCyclePhase.Ready;
        ReasonCode = "WarmupIncomplete";
        return true;
    }

    internal void OnExecution(ExecutionEvent execution)
    {
        if (execution.Quantity <= 0) return;
        if (_retiredClientOrderIds.Contains(execution.ClientOrderId))
        {
            FaultForRetiredFill(execution.ClientOrderId);
            return;
        }
        OrderLeg? leg = FindKnown(execution.ClientOrderId);
        if (leg is null ||
            !leg.FilledClientOrderIds.Add(execution.ClientOrderId)) return;

        if (leg.HasFill)
        {
            CancelWithoutReplacement(ReferenceEquals(leg, _buy) ? _sell : _buy, CancelIntent.Fault);
            Phase = QueueCyclePhase.Faulted;
            ReasonCode = "DuplicateGenerationFill";
            return;
        }

        bool currentGeneration = string.Equals(leg.ClientOrderId, execution.ClientOrderId, StringComparison.Ordinal);
        leg.HasFill = true;
        leg.PendingReplacementPrice = null;
        if (currentGeneration)
        {
            leg.State = LegState.Filled;
        }
        else if (leg.State is LegState.Working or LegState.CancelPending)
        {
            bool requestCancel = leg.State == LegState.Working;
            leg.State = LegState.CancelPending;
            leg.CancelIntent = CancelIntent.LateFillSafety;
            ReasonCode = "LateFillPriorGeneration";
            if (requestCancel) CancelOrderRequested?.Invoke(leg.ClientOrderId);
        }
        UpdateWorkingPhase();
        if (currentGeneration) ReasonCode = "OneLegFilled";
    }

    internal void OnOrderLifecycle(OrderLifecycleEvent orderEvent)
    {
        if (orderEvent.State == OrderLifecycleState.Filled &&
            _retiredClientOrderIds.Contains(orderEvent.ClientOrderId))
        {
            FaultForRetiredFill(orderEvent.ClientOrderId);
            return;
        }
        OrderLeg? leg = FindCurrent(orderEvent.ClientOrderId);
        if (leg is null || leg.State is LegState.Filled or LegState.Cancelled) return;

        switch (orderEvent.State)
        {
            case OrderLifecycleState.CancelPending when
                leg.State == LegState.CancelPending &&
                string.Equals(orderEvent.ReasonCode, "CancelRequestNotRouted", StringComparison.Ordinal):
                leg.PendingReplacementPrice = null;
                Phase = QueueCyclePhase.Faulted;
                ReasonCode = "CancelRequestNotRouted";
                return;
            case OrderLifecycleState.CancelPending when leg.State == LegState.CancelPending:
                return;
            case OrderLifecycleState.Filled:
                if (!leg.FilledClientOrderIds.Add(orderEvent.ClientOrderId)) return;
                if (leg.HasFill)
                {
                    CancelWithoutReplacement(ReferenceEquals(leg, _buy) ? _sell : _buy, CancelIntent.Fault);
                    Phase = QueueCyclePhase.Faulted;
                    ReasonCode = "DuplicateGenerationFill";
                    return;
                }
                leg.HasFill = true;
                leg.State = LegState.Filled;
                leg.PendingReplacementPrice = null;
                UpdateWorkingPhase();
                return;
            case OrderLifecycleState.Cancelled when leg.State == LegState.CancelPending:
                decimal? replacement = leg.PendingReplacementPrice;
                leg.PendingReplacementPrice = null;
                leg.CancelIntent = CancelIntent.None;
                if (replacement is null)
                {
                    leg.State = LegState.Cancelled;
                    UpdateWorkingPhase();
                    return;
                }

                leg.Generation++;
                leg.CurrentPrice = replacement.Value;
                leg.ClientOrderId = ClientOrderId(leg.CycleId, leg.Side, leg.Generation);
                leg.ClientOrderIds.Add(leg.ClientOrderId);
                leg.State = LegState.Working;
                leg.Submitted = true;
                OrderRequested?.Invoke(CreateOrder(leg));
                if (IsFaulted || leg.State != LegState.Working || leg.HasFill) return;
                ReasonCode = "ReplacementSubmitted";
                UpdateWorkingPhase();
                return;
            case OrderLifecycleState.Working when
                leg.State == LegState.CancelPending &&
                string.Equals(orderEvent.ReasonCode, "CancelFailed", StringComparison.OrdinalIgnoreCase):
                if (leg.CancelIntent != CancelIntent.Reprice)
                {
                    leg.PendingReplacementPrice = null;
                    Phase = QueueCyclePhase.Faulted;
                    ReasonCode = "CancelFailedDuringExit";
                    return;
                }
                leg.PendingReplacementPrice = null;
                leg.CancelIntent = CancelIntent.None;
                leg.State = LegState.Working;
                ReasonCode = "CancelFailed";
                UpdateWorkingPhase();
                return;
            case OrderLifecycleState.Rejected:
                leg.PendingReplacementPrice = null;
                leg.CancelIntent = CancelIntent.None;
                leg.State = LegState.Cancelled;
                CancelWithoutReplacement(ReferenceEquals(leg, _buy) ? _sell : _buy, CancelIntent.Fault);
                Phase = QueueCyclePhase.Faulted;
                ReasonCode = "OrderRejected";
                return;
        }
    }

    internal bool CancelBothBeforeFill(string reasonCode)
    {
        if (!BothUnfilled) return false;
        Phase = QueueCyclePhase.CancellingForExit;
        ReasonCode = reasonCode;
        CancelWithoutReplacement(_buy, CancelIntent.Exit);
        CancelWithoutReplacement(_sell, CancelIntent.Exit);
        return true;
    }

    internal void CancelAllWorking(string reasonCode)
    {
        Phase = QueueCyclePhase.CancellingForExit;
        ReasonCode = reasonCode;
        CancelWithoutReplacement(_buy, CancelIntent.Exit);
        CancelWithoutReplacement(_sell, CancelIntent.Exit);
    }

    private void EvaluateReprice(OrderLeg? leg, MarketEvent book)
    {
        if (IsFaulted || leg is null || leg.State != LegState.Working || FrozenBounds is null) return;
        int samePriceAhead = QuantityAtPrice(leg.Side, book, leg.CurrentPrice);
        QueuePriceDecision decision = _pricing.EvaluateReprice(
            leg.Side,
            book,
            leg.CurrentPrice,
            samePriceAhead,
            leg.RepriceThresholdB,
            leg.RepriceTargetC,
            FrozenBounds);
        if (decision.Action != QueuePriceAction.Reprice) return;

        leg.PendingReplacementPrice = decision.Price;
        leg.State = LegState.CancelPending;
        leg.CancelIntent = CancelIntent.Reprice;
        Phase = QueueCyclePhase.CancellingForReprice;
        ReasonCode = "RepriceThresholdExceeded";
        CancelOrderRequested?.Invoke(leg.ClientOrderId);
    }

    private void CancelWithoutReplacement(OrderLeg? leg, CancelIntent intent)
    {
        if (leg is null || leg.State is LegState.Filled or LegState.Cancelled) return;
        leg.PendingReplacementPrice = null;
        leg.CancelIntent = intent;
        if (!leg.Submitted)
        {
            leg.State = LegState.Cancelled;
            return;
        }
        if (leg.State == LegState.CancelPending) return;
        leg.State = LegState.CancelPending;
        CancelOrderRequested?.Invoke(leg.ClientOrderId);
    }

    private void FaultForRetiredFill(string clientOrderId)
    {
        if (!_retiredFilledClientOrderIds.Add(clientOrderId)) return;
        Phase = QueueCyclePhase.Faulted;
        ReasonCode = "LateFillRetiredCycle";
        CancelWithoutReplacement(_buy, CancelIntent.Fault);
        CancelWithoutReplacement(_sell, CancelIntent.Fault);
    }

    private void Retire(OrderLeg? leg)
    {
        if (leg is null) return;
        foreach (string clientOrderId in leg.ClientOrderIds)
            _retiredClientOrderIds.Add(clientOrderId);
    }

    private void UpdateWorkingPhase()
    {
        if (Phase == QueueCyclePhase.Faulted) return;
        if (_buy is null || _sell is null) return;
        if (_buy.HasFill || _sell.HasFill)
        {
            Phase = QueueCyclePhase.WorkingOneLeg;
            return;
        }
        if (_buy.State == LegState.CancelPending || _sell.State == LegState.CancelPending)
        {
            if (Phase != QueueCyclePhase.CancellingForExit)
                Phase = QueueCyclePhase.CancellingForReprice;
            return;
        }
        if (_buy.State == LegState.Working || _sell.State == LegState.Working)
            Phase = QueueCyclePhase.WorkingBoth;
    }

    private OrderLeg? FindCurrent(string clientOrderId)
    {
        if (string.IsNullOrWhiteSpace(clientOrderId)) return null;
        if (string.Equals(_buy?.ClientOrderId, clientOrderId, StringComparison.Ordinal)) return _buy;
        if (string.Equals(_sell?.ClientOrderId, clientOrderId, StringComparison.Ordinal)) return _sell;
        return null;
    }

    private OrderLeg? FindKnown(string clientOrderId)
    {
        if (string.IsNullOrWhiteSpace(clientOrderId)) return null;
        if (_buy?.ClientOrderIds.Contains(clientOrderId) == true) return _buy;
        if (_sell?.ClientOrderIds.Contains(clientOrderId) == true) return _sell;
        return null;
    }

    private static int QuantityAtPrice(BuySell side, MarketEvent book, decimal price)
    {
        decimal[] prices = side == BuySell.Buy ? book.BidPrices : book.AskPrices;
        int[] quantities = side == BuySell.Buy ? book.BidQuantities : book.AskQuantities;
        for (int index = 0; index < prices.Length && index < quantities.Length; index++)
            if (prices[index] == price) return Math.Max(0, quantities[index]);
        return 0;
    }

    private OrderLeg CreateLeg(
        string cycleId,
        BuySell side,
        decimal price,
        int thresholdB,
        int targetC)
    {
        var leg = new OrderLeg
        {
            CycleId = cycleId,
            Side = side,
            CurrentPrice = price,
            ClientOrderId = ClientOrderId(cycleId, side, 1),
            RepriceThresholdB = thresholdB,
            RepriceTargetC = targetC
        };
        leg.ClientOrderIds.Add(leg.ClientOrderId);
        return leg;
    }

    private PaperOrderRequest CreateOrder(OrderLeg leg) => new()
    {
        ClientOrderId = leg.ClientOrderId,
        StrategyInstanceId = _instanceId,
        Symbol = _symbol,
        Side = leg.Side,
        Type = PaperOrderType.BrokerLimit,
        Role = leg.Side == BuySell.Buy ? PaperOrderRole.EntryLong : PaperOrderRole.EntryShort,
        Price = leg.CurrentPrice,
        Quantity = 1
    };

    private string ClientOrderId(string cycleId, BuySell side, int generation) =>
        $"{_instanceId}-{cycleId}-{(side == BuySell.Buy ? "buy" : "sell")}-g{generation}";

    private static bool IsOutstanding(OrderLeg? leg) =>
        leg is { Submitted: true, State: LegState.Working or LegState.CancelPending };

    private static string LegSummary(OrderLeg? leg) => leg is null
        ? "none"
        : $"{leg.ClientOrderId},Generation={leg.Generation},State={leg.State},Price={leg.CurrentPrice},Intent={leg.CancelIntent}";
}
