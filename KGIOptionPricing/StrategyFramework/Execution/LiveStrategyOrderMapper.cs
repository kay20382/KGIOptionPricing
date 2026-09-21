using KGIOptionPricing.OrderManagement.Core;

namespace KGIOptionPricing.StrategyFramework.Execution;

public static class LiveStrategyOrderMapper
{
    public static OrderRequest CreateBrokerLimit(
        PaperOrderRequest request,
        InstrumentKey instrument,
        string brokerId,
        string account,
        string realOrderId)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(instrument);
        if (request.Quantity != 1)
            throw new ArgumentOutOfRangeException(nameof(request), request.Quantity, "實盤策略委託固定為一口。");
        if (request.Price <= 0m)
            throw new ArgumentOutOfRangeException(nameof(request), request.Price, "實盤限價必須為正數。");
        if (string.IsNullOrWhiteSpace(brokerId))
            throw new ArgumentException("券商代碼不得空白。", nameof(brokerId));
        if (string.IsNullOrWhiteSpace(account))
            throw new ArgumentException("交易帳號不得空白。", nameof(account));
        if (string.IsNullOrWhiteSpace(realOrderId))
            throw new ArgumentException("真實委託識別不得空白。", nameof(realOrderId));

        return new OrderRequest
        {
            OrderId = realOrderId,
            Instrument = instrument,
            BuySell = request.Side,
            Price = (double)request.Price,
            Quantity = 1,
            TimeInForce = TimeInForce.ROD,
            PositionEffect = PositionEffect.Auto,
            BrokerId = brokerId,
            Account = account
        };
    }
}
