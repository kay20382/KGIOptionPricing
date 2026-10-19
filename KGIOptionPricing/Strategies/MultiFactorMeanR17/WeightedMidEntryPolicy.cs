using KGIOptionPricing.StrategyFramework;

namespace KGIOptionPricing.Strategies.MultiFactorMeanR17;

public sealed record WeightedMidEntryAssessment(bool Ready,bool UseMarket,decimal? MicroMid,decimal? LargeMid,string ReasonCode)
{
    public decimal? Difference=>MicroMid-LargeMid;
}

/// <summary>First-level quote comparison only; shared execution owns fills and PnL.</summary>
public static class WeightedMidEntryPolicy
{
    public static WeightedMidEntryAssessment Evaluate(DateTimeOffset now,MarketEvent? micro,MarketEvent? large,
        decimal microTick,decimal largeTick,decimal maxAge,decimal maxSkew,int formula,int direction=0)
    {
        WeightedMidEntryAssessment Reject(string reason)=>new(false,false,null,null,reason);
        if(direction is <-1 or >1)return Reject("ENTRY_DIRECTION_INVALID");
        if(formula is <0 or >1)return Reject("WEIGHTED_MID_FORMULA");
        if(!Valid(micro,microTick) || !Valid(large,largeTick))return Reject("ENTRY_QUOTE_INVALID");
        decimal microAge=(decimal)(now-micro!.Timestamp).TotalSeconds,largeAge=(decimal)(now-large!.Timestamp).TotalSeconds;
        if(microAge<0 || largeAge<0 || microAge>maxAge || largeAge>maxAge)return Reject("ENTRY_QUOTE_STALE");
        if(micro.ConnectionGeneration!=large.ConnectionGeneration ||
            (decimal)Math.Abs((micro.Timestamp-large.Timestamp).TotalSeconds)>maxSkew)return Reject("ENTRY_QUOTE_SKEW");
        decimal m=Mid(micro,formula),l=Mid(large,formula);
        // Direction zero validates quotes without selecting an entry type.
        // Higher micro: passive buy / market sell. Otherwise: market buy / passive sell.
        bool market=direction>0?m<=l:direction<0 && m>l;
        return new(true,market,m,l,direction==0?"ENTRY_QUOTE_READY":market?"ENTRY_QUOTE_MARKET":"ENTRY_QUOTE_PASSIVE");
    }
    public static decimal? Calculate(DateTimeOffset now,MarketEvent? book,decimal tick,decimal maxAge,int formula)
    {
        if(formula is <0 or >1 || !Valid(book,tick))return null;
        decimal age=(decimal)(now-book!.Timestamp).TotalSeconds;
        return age<0 || age>maxAge?null:Mid(book,formula);
    }
    private static decimal Mid(MarketEvent e,int formula)
    {
        decimal bidQuantity=e.BidQuantities[0],askQuantity=e.AskQuantities[0];
        return formula==0?(e.BestBid*askQuantity+e.BestAsk*bidQuantity)/(bidQuantity+askQuantity)
            :(e.BestBid*bidQuantity+e.BestAsk*askQuantity)/(bidQuantity+askQuantity);
    }
    private static bool Valid(MarketEvent? e,decimal tick)=>e!=null && tick>0 && e.BestBid>0 && e.BestAsk>e.BestBid &&
        e.BestBid%tick==0 && e.BestAsk%tick==0 && e.BidQuantities.Length>0 && e.AskQuantities.Length>0 &&
        e.BidQuantities[0]>0 && e.AskQuantities[0]>0;
}
