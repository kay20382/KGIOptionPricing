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
    private string? _exitClientOrderId;
    private LegState _exitState = LegState.Cancelled;
    private PaperOrderRole? _exitRole;
    private long _exitSequence;
    private decimal _tickSize;

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
    internal string CurrentCycleId => _buy?.CycleId ?? string.Empty;
    internal CycleDirection FrozenDirection { get; private set; } = CycleDirection.Neutral;
    internal DirectionAssessment? FrozenAssessment { get; private set; }
    internal decimal BuyWorkingPrice => _buy?.CurrentPrice ?? 0m;
    internal decimal SellWorkingPrice => _sell?.CurrentPrice ?? 0m;
    internal bool IsFaulted => Phase == QueueCyclePhase.Faulted;
    internal bool BothUnfilled => _buy is not null && _sell is not null &&
        !_buy.HasFill && !_sell.HasFill;
    internal bool HasOutstandingOrders => IsOutstanding(_buy) || IsOutstanding(_sell) ||
        HasUnreconciledFill(_buy) || HasUnreconciledFill(_sell) ||
        (_exitClientOrderId is not null && _exitState is LegState.Working or LegState.CancelPending);
    internal bool EntryOrdersTerminal => !IsOutstanding(_buy) && !IsOutstanding(_sell) &&
        !HasUnreconciledFill(_buy) && !HasUnreconciledFill(_sell);
    internal bool HasBothEntryFills => _buy?.HasFill == true && _sell?.HasFill == true;
    internal string CycleSummary =>
        $"Cycle={_buy?.CycleId ?? "none"};Direction={FrozenDirection};" +
        $"Buy={LegSummary(_buy)};Sell={LegSummary(_sell)};" +
        $"Exit={_exitClientOrderId ?? "none"},{_exitRole?.ToString() ?? "none"},{_exitState}";

    internal event Action<PaperOrderRequest>? OrderRequested;
    internal event Action<string>? CancelOrderRequested;

    internal bool PrepareCycle(
        CycleDirection direction,
        DirectionAssessment assessment,
        decimal tickSize,
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
        QueuePriceDecision buy = _pricing.SelectInitial(BuySell.Buy, book, buyInitial, tickSize);
        QueuePriceDecision sell = _pricing.SelectInitial(BuySell.Sell, book, sellInitial, tickSize);
        if (buy.Action != QueuePriceAction.Place || sell.Action != QueuePriceAction.Place)
        {
            ReasonCode = buy.Action != QueuePriceAction.Place ? buy.ReasonCode : sell.ReasonCode;
            return false;
        }

        Phase = QueueCyclePhase.SubmittingPair;
        CycleNumber++;
        FrozenDirection = direction;
        FrozenAssessment = assessment;
        _tickSize = tickSize;
        string cycleId = $"cycle-{CycleNumber}";
        _buy = CreateLeg(cycleId, BuySell.Buy, buy.Price,
            direction == CycleDirection.Up ? _parameters.TrendRepriceThresholdB : _parameters.OppositeRepriceThresholdB,
            direction == CycleDirection.Up ? _parameters.TrendRepriceTargetC : _parameters.OppositeRepriceTargetC);
        _sell = CreateLeg(cycleId, BuySell.Sell, sell.Price,
            direction == CycleDirection.Up ? _parameters.OppositeRepriceThresholdB : _parameters.TrendRepriceThresholdB,
            direction == CycleDirection.Up ? _parameters.OppositeRepriceTargetC : _parameters.TrendRepriceTargetC);

        ReasonCode = "SignalAccepted";
        return true;
    }

    internal void ActivatePreparedCycle()
    {
        if (Phase != QueueCyclePhase.SubmittingPair || _buy is null || _sell is null)
            throw new InvalidOperationException("尚未準備可送出的雙邊週期。");
        PaperOrderRequest buyOrder = CreateOrder(_buy);
        PaperOrderRequest sellOrder = CreateOrder(_sell);
        _buy.Submitted = true;
        OrderRequested?.Invoke(buyOrder);
        if (IsFaulted) return;
        _sell.Submitted = true;
        OrderRequested?.Invoke(sellOrder);
        if (IsFaulted) return;
        UpdateWorkingPhase();
        if (!_buy.HasFill && !_sell.HasFill) ReasonCode = "SignalAccepted";
    }

    internal void EvaluateReprices(MarketEvent book)
    {
        if (_tickSize <= 0m || IsFaulted) return;
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
        _tickSize = 0m;
        Phase = QueueCyclePhase.Ready;
        ReasonCode = "WarmupIncomplete";
        return true;
    }

    internal void BeginRiskCancellation(string reasonCode)
    {
        if (IsFaulted) return;
        Phase = QueueCyclePhase.CancellingForExit;
        ReasonCode = reasonCode;
        CancelWithoutReplacement(_buy, CancelIntent.Exit);
        CancelWithoutReplacement(_sell, CancelIntent.Exit);
    }

    internal bool TrySubmitExit(
        PositionSnapshot position,
        PaperOrderRole role,
        PaperOrderType type,
        decimal price,
        string reasonCode)
    {
        if (IsFaulted || !EntryOrdersTerminal || _exitClientOrderId is not null ||
            position.NetQuantity == 0 || price <= 0m)
            return false;

        _exitClientOrderId = $"{_instanceId}-cycle-{CycleNumber}-exit-{++_exitSequence}";
        _exitState = LegState.Working;
        _exitRole = role;
        Phase = QueueCyclePhase.Flattening;
        ReasonCode = reasonCode;
        OrderRequested?.Invoke(new PaperOrderRequest
        {
            ClientOrderId = _exitClientOrderId,
            CycleId = $"cycle-{CycleNumber}",
            Generation = 1,
            StrategyInstanceId = _instanceId,
            Symbol = _symbol,
            Side = position.NetQuantity > 0 ? BuySell.Sell : BuySell.Buy,
            Type = type,
            Role = role,
            Price = price,
            Quantity = Math.Abs(position.NetQuantity)
        });
        return true;
    }

    internal void EnterCooldown()
    {
        Retire(_buy);
        Retire(_sell);
        if (_exitClientOrderId is not null)
        {
            _retiredClientOrderIds.Add(_exitClientOrderId);
            if (_exitState == LegState.Filled) _retiredFilledClientOrderIds.Add(_exitClientOrderId);
        }
        _buy = null;
        _sell = null;
        _exitClientOrderId = null;
        _exitState = LegState.Cancelled;
        _exitRole = null;
        FrozenDirection = CycleDirection.Neutral;
        FrozenAssessment = null;
        _tickSize = 0m;
        Phase = QueueCyclePhase.CoolingDown;
        ReasonCode = "CoolingDown";
    }

    internal void ResetAfterCooldown()
    {
        if (Phase != QueueCyclePhase.CoolingDown) return;
        Phase = QueueCyclePhase.Ready;
        ReasonCode = "WarmupIncomplete";
    }

    internal void OnExecution(ExecutionEvent execution)
    {
        if (execution.Quantity <= 0) return;
        if (string.Equals(_exitClientOrderId, execution.ClientOrderId, StringComparison.Ordinal))
        {
            if (_exitState == LegState.Filled) return;
            _exitState = LegState.Filled;
            Phase = QueueCyclePhase.Flattening;
            return;
        }
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
        if (string.Equals(_exitClientOrderId, orderEvent.ClientOrderId, StringComparison.Ordinal))
        {
            switch (orderEvent.State)
            {
                case OrderLifecycleState.Filled:
                    _exitState = LegState.Filled;
                    return;
                case OrderLifecycleState.CancelPending:
                    _exitState = LegState.CancelPending;
                    return;
                case OrderLifecycleState.Cancelled:
                case OrderLifecycleState.Rejected:
                    _exitState = LegState.Cancelled;
                    Phase = QueueCyclePhase.Faulted;
                    ReasonCode = orderEvent.State == OrderLifecycleState.Rejected
                        ? "ExitOrderRejected"
                        : "ExitOrderCancelled";
                    return;
                case OrderLifecycleState.Working:
                case OrderLifecycleState.Submitted:
                case OrderLifecycleState.PartiallyFilled:
                    _exitState = LegState.Working;
                    return;
            }
        }
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
                leg.State = LegState.Filled;
                leg.PendingReplacementPrice = null;
                leg.CancelIntent = CancelIntent.None;
                ReasonCode = "FillAwaitingExecution";
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
        if (IsFaulted || leg is null || leg.State != LegState.Working || _tickSize <= 0m) return;
        int samePriceAhead = QuantityAtPrice(leg.Side, book, leg.CurrentPrice);
        QueuePriceDecision decision = _pricing.EvaluateReprice(
            leg.Side,
            book,
            leg.CurrentPrice,
            samePriceAhead,
            leg.RepriceThresholdB,
            leg.RepriceTargetC,
            _tickSize);
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
        foreach (string clientOrderId in leg.FilledClientOrderIds)
            _retiredFilledClientOrderIds.Add(clientOrderId);
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
        CycleId = leg.CycleId,
        Generation = leg.Generation,
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

    private static bool HasUnreconciledFill(OrderLeg? leg) =>
        leg is { State: LegState.Filled, HasFill: false };

    private static string LegSummary(OrderLeg? leg) => leg is null
        ? "none"
        : $"{leg.ClientOrderId},Generation={leg.Generation},State={leg.State},Price={leg.CurrentPrice},Intent={leg.CancelIntent}";
}
