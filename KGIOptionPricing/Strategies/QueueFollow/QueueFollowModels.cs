using System;
using System.Collections.Generic;
using KGIOptionPricing.OrderManagement.Core;

namespace KGIOptionPricing.Strategies;

public enum QueueFollowState
{
    WaitingForMarket,
    PendingNew,
    Working,
    PendingModify,
    PendingCancel,
    Completed,
    Stopped,
    Faulted
}

public sealed class QueueFollowRequest
{
    public string StrategyId { get; init; } = Guid.NewGuid().ToString("N");
    public InstrumentKey Instrument { get; init; } = new();
    public string QuoteSymbol { get; init; } = string.Empty;
    public string ProductId { get; init; } = string.Empty;
    public TickTypeEnum TickType { get; init; }
    public BuySell Side { get; init; }
    public int Quantity { get; init; }
    /// <summary>首次掛單允許的最大前方累計口數 (A)。</summary>
    public int InitialTargetAheadQuantity { get; init; } = 10;
    /// <summary>目前前方總量大於此值時重新掛單 (B)。</summary>
    public int RepriceAheadThreshold { get; init; } = 10;
    /// <summary>重新掛單後允許的最大前方累計口數 (C)。</summary>
    public int RepriceTargetAheadQuantity { get; init; }
    /// <summary>買單為最高買價，賣單為最低賣價。</summary>
    public decimal PriceLimit { get; init; }
    public string BrokerId { get; init; } = string.Empty;
    public string Account { get; init; } = string.Empty;
    public TimeInForce TimeInForce { get; init; } = TimeInForce.ROD;
    public PositionEffect PositionEffect { get; init; } = PositionEffect.Auto;
}

public sealed class OrderBookSnapshot
{
    public string Symbol { get; init; } = string.Empty;
    public decimal[] BidPrices { get; init; } = Array.Empty<decimal>();
    public int[] BidQuantities { get; init; } = Array.Empty<int>();
    public decimal[] AskPrices { get; init; } = Array.Empty<decimal>();
    public int[] AskQuantities { get; init; } = Array.Empty<int>();
    public decimal LastPrice { get; init; }
    public DateTime ReceivedAt { get; init; } = DateTime.Now;

    public decimal BestBid => BidPrices.Length > 0 ? BidPrices[0] : 0m;
    public decimal BestAsk => AskPrices.Length > 0 ? AskPrices[0] : 0m;
}

public sealed class QueueFollowSnapshot
{
    public string StrategyId { get; init; } = string.Empty;
    public string OrderId { get; init; } = string.Empty;
    public InstrumentKey Instrument { get; init; } = new();
    public string QuoteSymbol { get; init; } = string.Empty;
    public BuySell Side { get; init; }
    public int TargetQuantity { get; init; }
    public int FilledQuantity { get; init; }
    public int RemainingQuantity { get; init; }
    public int InitialTargetAheadQuantity { get; init; }
    public int RepriceAheadThreshold { get; init; }
    public int RepriceTargetAheadQuantity { get; init; }
    public decimal CurrentOrderPrice { get; init; }
    public decimal PlannedPrice { get; init; }
    public decimal PriceLimit { get; init; }
    public int SamePriceAheadQuantity { get; init; }
    public int BetterPriceQuantity { get; init; }
    public int TotalAheadQuantity => SamePriceAheadQuantity + BetterPriceQuantity;
    public int QueueTolerance => RepriceAheadThreshold;
    public QueueFollowState State { get; init; }
    public string Reason { get; init; } = string.Empty;
    public DateTime UpdatedAt { get; init; }
}

public enum QueueFollowDecisionKind
{
    Hold,
    Modify
}

public sealed class QueueFollowDecision
{
    public QueueFollowDecisionKind Kind { get; init; }
    public decimal TargetPrice { get; init; }
    public int SamePriceAheadQuantity { get; init; }
    public int BetterPriceQuantity { get; init; }
    public string Reason { get; init; } = string.Empty;
}

public interface IQueueFollowMarketDataSource
{
    event Action<OrderBookSnapshot>? MarketChanged;
    void Subscribe(string symbol);
    bool TryGetSnapshot(string symbol, out OrderBookSnapshot snapshot);
}

public interface IQueueFollowOrderService
{
    bool IsReady { get; }
    event Action<OrderInfo>? OrderChanged;
    event Action<OrderInfo, int, double>? OrderFilled;
    event Action<OrderInfo, string>? OrderFailed;
    event Action<OrderInfo, string>? ModifyFailed;
    event Action<OrderInfo, string>? CancelFailed;
    void Place(OrderRequest request);
    void Modify(string orderId, double price, int quantity);
    void Cancel(string orderId);
}

public interface IQueueFollowTickProvider
{
    decimal Next(decimal price, TickTypeEnum tickType, bool upward);
    decimal Regular(decimal price, TickTypeEnum tickType, bool upward);
}
