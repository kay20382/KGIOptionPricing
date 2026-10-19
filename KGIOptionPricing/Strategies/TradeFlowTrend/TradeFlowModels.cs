namespace KGIOptionPricing.Strategies.TradeFlowTrend;
public enum MovingAverageKind { Sma, Ema }
public enum TradeFlowSide { Unknown, Up, Zero, Down }
public sealed record FlowWindowSnapshot(int Seconds, decimal? Up, decimal? Zero, decimal? Down,
    decimal? Real, decimal? Average, decimal? Maximum, decimal? Minimum, int ValidSamples, int TargetSamples,
    bool FullHistory, long ClassifiedVolume, long UnknownVolume);
public sealed record ClassifiedTrade(DateTimeOffset Timestamp, TradeFlowSide Side, int Quantity);
public sealed record RiSample(DateTimeOffset End, decimal? Value, bool Partial);
public sealed record TrendSnapshot(decimal? Fast, decimal? Slow, bool Ready);
public sealed record TradeBar(DateTimeOffset Start, DateTimeOffset End, decimal Open, decimal High, decimal Low, decimal Close, long Volume);
public sealed record SarSnapshot(decimal Value, bool IsLong, decimal Extreme, decimal Acceleration, DateTimeOffset BarEnd);
