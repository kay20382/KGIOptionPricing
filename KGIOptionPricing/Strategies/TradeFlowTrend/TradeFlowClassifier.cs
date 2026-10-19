using KGIOptionPricing.StrategyFramework;
namespace KGIOptionPricing.Strategies.TradeFlowTrend;
public static class TradeFlowClassifier
{
    public static TradeFlowSide Classify(MarketEvent trade, TimeSpan maxAge)
    {
        var b=trade.PriorBook;
        if (trade.Kind!=MarketEventKind.Trade || trade.TradeQuantity<=0 || trade.LastPrice<=0 || b is null ||
            trade.ProvenanceWarning!=null || b.Symbol!=trade.Symbol || b.Bid<=0 || b.Ask<=b.Bid ||
            b.Generation!=trade.ConnectionGeneration || b.Timestamp>trade.Timestamp ||
            (b.Timestamp==trade.Timestamp && b.Sequence>=trade.Sequence) || trade.Timestamp-b.Timestamp>maxAge)
            return TradeFlowSide.Unknown;
        return trade.LastPrice>=b.Ask ? TradeFlowSide.Up : trade.LastPrice<=b.Bid ? TradeFlowSide.Down : TradeFlowSide.Zero;
    }
}
