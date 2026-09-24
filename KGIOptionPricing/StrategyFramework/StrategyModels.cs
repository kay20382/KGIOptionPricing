using System;
using System.Collections.Generic;
using KGIOptionPricing.OrderManagement.Core;

namespace KGIOptionPricing.StrategyFramework;

public enum StrategyLifecycleState { Created, WarmingUp, Ready, Running, Suspended, Stopped, Faulted }
public enum MarketEventKind { Trade, Book, Connection, Session }
public enum PaperOrderType { Limit, Stop, Market, BrokerLimit }
public enum PaperOrderState { Working, Filled, Cancelled, Rejected }
public enum PaperOrderRole { EntryLong, EntryShort, TakeProfit, StopLoss, Flatten }
public enum OrderLifecycleState
{
    Submitted, Working, CancelPending, PartiallyFilled, Filled, Cancelled, Rejected
}

public sealed record MarketEvent
{
    public long Sequence { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public string Symbol { get; init; } = string.Empty;
    public string ProductId { get; init; } = string.Empty;
    public MarketEventKind Kind { get; init; }
    public decimal LastPrice { get; init; }
    public int TradeQuantity { get; init; }
    public decimal[] BidPrices { get; init; } = Array.Empty<decimal>();
    public int[] BidQuantities { get; init; } = Array.Empty<int>();
    public decimal[] AskPrices { get; init; } = Array.Empty<decimal>();
    public int[] AskQuantities { get; init; } = Array.Empty<int>();
    public decimal BestBid => BidPrices.Length == 0 ? 0m : BidPrices[0];
    public decimal BestAsk => AskPrices.Length == 0 ? 0m : AskPrices[0];
    public decimal MidPrice => BestBid > 0m && BestAsk > 0m ? (BestBid + BestAsk) / 2m : LastPrice;
}

public sealed record ProductSelection(string ProductId, string? Symbol = null);

public sealed record StrategyUniverse
{
    public ProductSelection Tradable { get; init; } = new("225MC");
    public IReadOnlyList<ProductSelection> References { get; init; } =
        new[] { new ProductSelection("225"), new ProductSelection("225M") };
}

public sealed record StrategyDefinition
{
    public string StrategyType { get; init; } = string.Empty;
    public string InstanceId { get; init; } = Guid.NewGuid().ToString("N");
    public StrategyUniverse Universe { get; init; } = new();
    public IReadOnlyDictionary<string, string> Parameters { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public sealed record ResolvedProduct
{
    public string ProductId { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public string ContractMonth { get; init; } = string.Empty;
    public TickTypeEnum TickType { get; init; }
    public decimal TickSize { get; init; }
    public decimal ContractMultiplier { get; init; } = 1m;
}

public sealed record ResolvedStrategyUniverse
{
    public ResolvedProduct Tradable { get; init; } = new();
    public IReadOnlyList<ResolvedProduct> References { get; init; } = Array.Empty<ResolvedProduct>();
    public IEnumerable<ResolvedProduct> AllProducts
    {
        get
        {
            yield return Tradable;
            foreach (ResolvedProduct product in References) yield return product;
        }
    }
}

public sealed record PaperOrderRequest
{
    public string ClientOrderId { get; init; } = string.Empty;
    public string CycleId { get; init; } = string.Empty;
    public long StrategyRunGeneration { get; init; }
    public int Generation { get; init; }
    public string StrategyInstanceId { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public BuySell Side { get; init; }
    public PaperOrderType Type { get; init; }
    public PaperOrderRole Role { get; init; }
    public decimal Price { get; init; }
    public int Quantity { get; init; } = 1;
}

public sealed record PaperOrder
{
    public string OrderId { get; init; } = string.Empty;
    public string ClientOrderId { get; init; } = string.Empty;
    public string CycleId { get; init; } = string.Empty;
    public long StrategyRunGeneration { get; init; }
    public int Generation { get; init; }
    public string StrategyInstanceId { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public BuySell Side { get; init; }
    public PaperOrderType Type { get; init; }
    public PaperOrderRole Role { get; init; }
    public decimal Price { get; init; }
    public int Quantity { get; init; }
    public PaperOrderState State { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed record ExecutionEvent
{
    public long Sequence { get; init; }
    public string ExecutionId { get; init; } = string.Empty;
    public int CumulativeFilledQuantity { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public string StrategyInstanceId { get; init; } = string.Empty;
    public string ClientOrderId { get; init; } = string.Empty;
    public string CycleId { get; init; } = string.Empty;
    public long StrategyRunGeneration { get; init; }
    public int Generation { get; init; }
    public string OrderId { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public BuySell Side { get; init; }
    public PaperOrderRole Role { get; init; }
    public decimal Price { get; init; }
    public int Quantity { get; init; }
    public decimal Commission { get; init; }
    public decimal TriggerMarketPrice { get; init; }
    public long MarketSequence { get; init; }
}

public sealed record OrderLifecycleEvent
{
    public DateTimeOffset Timestamp { get; init; }
    public string StrategyInstanceId { get; init; } = string.Empty;
    public string ClientOrderId { get; init; } = string.Empty;
    public string CycleId { get; init; } = string.Empty;
    public long StrategyRunGeneration { get; init; }
    public int Generation { get; init; }
    public string OrderId { get; init; } = string.Empty;
    public string BrokerOrderId { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public BuySell? Side { get; init; }
    public PaperOrderRole? Role { get; init; }
    public long TriggerMarketSequence { get; init; }
    public OrderLifecycleState PreviousState { get; init; }
    public OrderLifecycleState State { get; init; }
    public decimal Price { get; init; }
    public int Quantity { get; init; }
    public int FilledQuantity { get; init; }
    public int RemainingQuantity { get; init; }
    public bool HasFinalCumulativeFillWatermark { get; init; }
    public string ReasonCode { get; init; } = string.Empty;
}

public sealed record PositionSnapshot
{
    public string StrategyInstanceId { get; init; } = string.Empty;
    public long StrategyRunGeneration { get; init; }
    public string Symbol { get; init; } = string.Empty;
    public int NetQuantity { get; init; }
    public decimal AveragePrice { get; init; }
    public decimal MarkPrice { get; init; }
    public decimal RealizedPnl { get; init; }
    public decimal UnrealizedPnl { get; init; }
    public decimal Costs { get; init; }
    public int CompletedTrades { get; init; }
    public decimal NetPnl => RealizedPnl + UnrealizedPnl - Costs;
}

public sealed record StrategySnapshot
{
    public string StrategyType { get; init; } = string.Empty;
    public string InstanceId { get; init; } = string.Empty;
    public long StrategyRunGeneration { get; init; }
    public string RecoveryState { get; init; } = string.Empty;
    public string RecoveryReason { get; init; } = string.Empty;
    public string ParameterSummary { get; init; } = string.Empty;
    public string TradableProductId { get; init; } = string.Empty;
    public string TradableSymbol { get; init; } = string.Empty;
    public string ReferenceSummary { get; init; } = string.Empty;
    public StrategyLifecycleState State { get; init; }
    public string Reason { get; init; } = string.Empty;
    public decimal LastPrice { get; init; }
    public decimal CenterPrice { get; init; }
    public decimal Range { get; init; }
    public decimal BuyEntry { get; init; }
    public decimal SellEntry { get; init; }
    public decimal StopPrice { get; init; }
    public int Position { get; init; }
    public decimal RealizedPnl { get; init; }
    public decimal UnrealizedPnl { get; init; }
    public decimal NetPnl { get; init; }
    public decimal CommissionPerContract { get; init; }
    public int CompletedTrades { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public interface IStrategyClock { DateTimeOffset Now { get; } }
public interface IEventDrivenStrategyClock : IStrategyClock { void Advance(DateTimeOffset timestamp); }
public sealed class SystemStrategyClock : IStrategyClock { public DateTimeOffset Now => DateTimeOffset.Now; }
