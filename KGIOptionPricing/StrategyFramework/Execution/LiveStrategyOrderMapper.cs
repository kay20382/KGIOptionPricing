using KGIOptionPricing.OrderManagement.Core;
using KGIOptionPricing.StrategyFramework.Performance;

namespace KGIOptionPricing.StrategyFramework.Execution;

public interface ILiveStrategyOrderGateway
{
    bool IsLoggedIn { get; }
    string BrokerId { get; }
    string Account { get; }
    event Action<OrderInfo>? OrderStateChanged;
    event Action<OrderInfo, int, double>? OrderFilled;
    event Action<OrderInfo, string>? OrderFailed;
    event Action<OrderInfo, string>? CancelFailed;
    void PlaceOrder(OrderRequest request);
    void CancelOrder(string orderId);
    void AppendOcoState(LiveStrategyOcoGroupState state) => throw new NotSupportedException("此 gateway 未支援持久化 OCO 狀態。");
    IReadOnlyList<LiveStrategyOcoGroupState> GetOcoStates(string strategyInstanceId, long strategyRunGeneration) => Array.Empty<LiveStrategyOcoGroupState>();
}

internal sealed class OrderManagerLiveStrategyOrderGateway : ILiveStrategyOrderGateway
{
    private readonly OrderManager _orders;
    private readonly KGITradeService _trade;

    public OrderManagerLiveStrategyOrderGateway(OrderManager orders, KGITradeService trade)
    {
        _orders = orders;
        _trade = trade;
    }

    public bool IsLoggedIn => _trade.IsLoggedIn;
    public string BrokerId => _trade.BrokerID;
    public string Account => _trade.Account;
    public event Action<OrderInfo>? OrderStateChanged
    {
        add => _orders.OnOrderStateChanged += value;
        remove => _orders.OnOrderStateChanged -= value;
    }
    public event Action<OrderInfo, int, double>? OrderFilled
    {
        add => _orders.OnOrderFilled += value;
        remove => _orders.OnOrderFilled -= value;
    }
    public event Action<OrderInfo, string>? OrderFailed
    {
        add => _orders.OnOrderFailed += value;
        remove => _orders.OnOrderFailed -= value;
    }
    public event Action<OrderInfo, string>? CancelFailed
    {
        add => _orders.OnCancelFailed += value;
        remove => _orders.OnCancelFailed -= value;
    }

    public void PlaceOrder(OrderRequest request) => _orders.PlaceOrder(request);
    public void CancelOrder(string orderId) => _orders.CancelOrder(orderId);
}

public static class LiveStrategyOrderMapper
{
    public static OrderRequest CreateBrokerLimit(
        PaperOrderRequest request,
        InstrumentKey instrument,
        string brokerId,
        string account,
        string realOrderId,
        int accountNetQuantity = 0)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(instrument);
        if (request.Type != PaperOrderType.BrokerLimit)
            throw new ArgumentException("此映射器僅接受 BrokerLimit 委託。", nameof(request));
        if (string.IsNullOrWhiteSpace(request.ClientOrderId))
            throw new ArgumentException("ClientOrderId 不得空白。", nameof(request));
        if (string.IsNullOrWhiteSpace(request.StrategyInstanceId))
            throw new ArgumentException("StrategyInstanceId 不得空白。", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Symbol))
            throw new ArgumentException("商品代碼不得空白。", nameof(request));
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
            PositionEffect = AccountPositionEffectResolver.Resolve(accountNetQuantity, request.Side, request.Quantity),
            BrokerId = brokerId,
            Account = account,
            StrategyRoute = new StrategyOrderRouteMetadata(
                request.StrategyInstanceId,
                request.StrategyRunGeneration,
                request.ClientOrderId,
                request.CycleId,
                request.Symbol,
                request.Role.ToString(),
                request.OcoGroupId)
        };
    }

    public static InstrumentKey ToInstrument(ContractInfo contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        bool foreign = !string.IsNullOrWhiteSpace(contract.ExchangeNm) &&
                       !contract.ExchangeNm.Equals("TIMEX", StringComparison.OrdinalIgnoreCase) &&
                       !contract.ExchangeNm.Equals("TAIFEX", StringComparison.OrdinalIgnoreCase);
        bool option = contract.CPF is CPFEnum.Call or CPFEnum.Put ||
                      (contract.CPF == CPFEnum.None && contract.ComType is 'C' or 'P');
        return new InstrumentKey
        {
            Market = foreign ? (option ? MarketType.ForeignOption : MarketType.ForeignFuture) :
                (option ? MarketType.DomesticOption : MarketType.DomesticFuture),
            Exchange = contract.ExchangeNm,
            Symbol = foreign && !string.IsNullOrWhiteSpace(contract.ProductId) ? contract.ProductId : contract.Symbol,
            ContractMonth = !string.IsNullOrWhiteSpace(contract.ComYM2) ? contract.ComYM2 :
                !string.IsNullOrWhiteSpace(contract.ComYM) ? contract.ComYM : contract.Month
        };
    }
}
