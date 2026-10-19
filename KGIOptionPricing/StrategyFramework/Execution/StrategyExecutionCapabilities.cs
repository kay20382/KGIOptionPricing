using KGIOptionPricing.OrderManagement.Core;
namespace KGIOptionPricing.StrategyFramework.Execution;
public sealed record StrategyExecutionCapabilities(int MaximumOrderQuantity=1,bool AllowLimitIoc=false,
    bool AllowPairedRestingLive=false)
{
    public void Validate(PaperOrderRequest request)
    {
        if (request.RestingPairMode == RestingPairMode.OppositeLegIsExit &&
            (!AllowPairedRestingLive || request.Type != PaperOrderType.BrokerLimit ||
             request.TimeInForce != TimeInForce.ROD || string.IsNullOrWhiteSpace(request.PairId) ||
             string.IsNullOrWhiteSpace(request.CycleId) || request.StrategyRunGeneration <= 0))
            throw new ArgumentException("Paired live limit mode is not certified or its identity is incomplete.");
        if(MaximumOrderQuantity is <1 or >100 || request.Quantity<1 || request.Quantity>MaximumOrderQuantity)
            throw new ArgumentOutOfRangeException(nameof(request.Quantity));
        if(request.TimeInForce!=TimeInForce.ROD && !(request.TimeInForce==TimeInForce.IOC && AllowLimitIoc && request.Type==PaperOrderType.BrokerLimit))
            throw new ArgumentException("策略未啟用此限價效期");
        if(request.Type!=PaperOrderType.BrokerLimit && request.Quantity!=1)throw new ArgumentException("多口僅供明確限價委託");
    }
}
