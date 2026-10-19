using System.Text.Json.Serialization;

namespace KGIOptionPricing.ProductSpreads;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SpreadLegDirection
{
    Buy,
    Sell
}

public sealed record ProductSpreadLegDefinition
{
    public string ProductID { get; init; } = string.Empty;
    public SpreadLegDirection Direction { get; init; }
    public int Quantity { get; init; } = 1;
}

public sealed record ProductSpreadDefinition
{
    public string Id { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public ProductSpreadLegDefinition LegA { get; init; } = new();
    public ProductSpreadLegDefinition LegB { get; init; } = new();
    public decimal ShiftPoint { get; init; }
    public decimal MinimumTick { get; init; } = 1m;
    public int PxDecimal { get; init; }
    public bool SnapshotEnabled { get; init; } = true;
    public int SnapshotIntervalSeconds { get; init; } = 5;
    public string SnapshotPath { get; init; } = @".\SpreadSnapshots";
}

public sealed record SpreadLegQuote
{
    public string ProductID { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public string ContractMonth { get; init; } = string.Empty;
    public SpreadLegDirection Direction { get; init; }
    public int Quantity { get; init; }
    public decimal ContractMultiplier { get; init; }
    public int PxDecimal { get; init; }
    public decimal? LastPrice { get; init; }
    public decimal? BidPrice { get; init; }
    public decimal? AskPrice { get; init; }
    public int BidQuantity { get; init; }
    public int AskQuantity { get; init; }
    public DateTime UpdateTime { get; init; }
}

public sealed record ProductSpreadQuote
{
    public string SpreadId { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public decimal? LastPrice { get; init; }
    public decimal? BidPrice { get; init; }
    public decimal? AskPrice { get; init; }
    public int BidQuantity { get; init; }
    public int AskQuantity { get; init; }
    public decimal ShiftPoint { get; init; }
    public decimal MinimumTick { get; init; }
    public int PxDecimal { get; init; }
    public DateTime UpdateTime { get; init; }
    public SpreadLegQuote LegA { get; init; } = new();
    public SpreadLegQuote LegB { get; init; } = new();
    public bool IsValid { get; init; }
    public string InvalidReason { get; init; } = string.Empty;
}

public sealed record ProductSpreadSnapshot
{
    public DateTime SnapshotTime { get; init; }
    public ProductSpreadQuote Quote { get; init; } = new();
}
