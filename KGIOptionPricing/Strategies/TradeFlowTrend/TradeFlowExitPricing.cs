using KGIOptionPricing.OrderManagement.Core;
namespace KGIOptionPricing.Strategies.TradeFlowTrend;
public sealed record ExitPricePlan(decimal Price,TimeInForce TimeInForce);
public static class TradeFlowExitPricing
{
    public static ExitPricePlan Plan(BuySell side,decimal bid,decimal ask,decimal tick,TradeFlowParameters p)
    {
        if(tick<=0 || bid<=0 || ask<=bid || bid%tick!=0 || ask%tick!=0)throw new ArgumentException("Invalid book/tick");
        bool ioc=(ask-bid)/tick<p.SpreadSwitchTicks;
        decimal price=side==BuySell.Buy?bid+(ioc?p.IocOffsetTicks:p.RodOffsetTicks)*tick:ask-(ioc?p.IocOffsetTicks:p.RodOffsetTicks)*tick;
        price=side==BuySell.Buy?decimal.Floor(price/tick)*tick:decimal.Ceiling(price/tick)*tick;
        if(price<=0)throw new ArgumentException("Invalid order price");
        return new(price,ioc?TimeInForce.IOC:TimeInForce.ROD);
    }
}
