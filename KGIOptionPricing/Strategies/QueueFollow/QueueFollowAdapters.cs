using System;
using System.Linq;
using KGIOptionPricing.OrderManagement.Core;

namespace KGIOptionPricing.Strategies;

public sealed class KgiQueueFollowMarketDataSource : IQueueFollowMarketDataSource, IDisposable
{
    private readonly KGIQuoteService _quote;
    public event Action<OrderBookSnapshot>? MarketChanged;

    public KgiQueueFollowMarketDataSource(KGIQuoteService quote)
    {
        _quote = quote;
        _quote.OnMarketDataReceived += OnMarketDataReceived;
    }

    public void Subscribe(string symbol) => _quote.Subscribe(symbol);

    public bool TryGetSnapshot(string symbol, out OrderBookSnapshot snapshot)
    {
        KGIQuoteService.MarketDataStruct? market = _quote.GetMarketData(symbol) ?? _quote.GetMarketData("TW." + symbol);
        if (market == null)
        {
            snapshot = new OrderBookSnapshot();
            return false;
        }
        snapshot = Copy(symbol, market);
        return true;
    }

    private void OnMarketDataReceived(string symbol, KGIQuoteService.MarketDataStruct market) =>
        MarketChanged?.Invoke(Copy(symbol, market));

    private static OrderBookSnapshot Copy(string symbol, KGIQuoteService.MarketDataStruct market) => new()
    {
        Symbol = symbol,
        BidPrices = market.BuyPx?.ToArray() ?? Array.Empty<decimal>(),
        BidQuantities = market.BuyQty?.ToArray() ?? Array.Empty<int>(),
        AskPrices = market.SellPx?.ToArray() ?? Array.Empty<decimal>(),
        AskQuantities = market.SellQty?.ToArray() ?? Array.Empty<int>(),
        LastPrice = market.MatchcPx > 0 ? market.MatchcPx : market.LastPx,
        ReceivedAt = DateTime.Now
    };

    public void Dispose() => _quote.OnMarketDataReceived -= OnMarketDataReceived;
}

public sealed class OrderManagerQueueFollowOrderService : IQueueFollowOrderService, IDisposable
{
    private readonly OrderManager _orders;
    private readonly KGITradeService _trade;

    public bool IsReady => _trade.IsLoggedIn;
    public event Action<OrderInfo>? OrderChanged;
    public event Action<OrderInfo, int, double>? OrderFilled;
    public event Action<OrderInfo, string>? OrderFailed;
    public event Action<OrderInfo, string>? ModifyFailed;
    public event Action<OrderInfo, string>? CancelFailed;

    public OrderManagerQueueFollowOrderService(OrderManager orders, KGITradeService trade)
    {
        _orders = orders;
        _trade = trade;
        _orders.OnOrderStateChanged += ForwardChanged;
        _orders.OnOrderFilled += ForwardFilled;
        _orders.OnOrderFailed += ForwardOrderFailed;
        _orders.OnModifyFailed += ForwardModifyFailed;
        _orders.OnCancelFailed += ForwardCancelFailed;
    }

    public void Place(OrderRequest request) => _orders.PlaceOrder(request);
    public void Modify(string orderId, double price, int quantity) => _orders.ModifyOrder(orderId, price, quantity);
    public void Cancel(string orderId) => _orders.CancelOrder(orderId);

    private void ForwardChanged(OrderInfo order) => OrderChanged?.Invoke(order);
    private void ForwardFilled(OrderInfo order, int qty, double price) => OrderFilled?.Invoke(order, qty, price);
    private void ForwardOrderFailed(OrderInfo order, string reason) => OrderFailed?.Invoke(order, reason);
    private void ForwardModifyFailed(OrderInfo order, string reason) => ModifyFailed?.Invoke(order, reason);
    private void ForwardCancelFailed(OrderInfo order, string reason) => CancelFailed?.Invoke(order, reason);

    public void Dispose()
    {
        _orders.OnOrderStateChanged -= ForwardChanged;
        _orders.OnOrderFilled -= ForwardFilled;
        _orders.OnOrderFailed -= ForwardOrderFailed;
        _orders.OnModifyFailed -= ForwardModifyFailed;
        _orders.OnCancelFailed -= ForwardCancelFailed;
    }
}

public sealed class KgiQueueFollowTickProvider : IQueueFollowTickProvider
{
    public decimal Next(decimal price, TickTypeEnum tickType, bool upward) => PriceTick.getNextTick(price, tickType, upward);
    public decimal Regular(decimal price, TickTypeEnum tickType, bool upward) => PriceTick.getRegularPx(tickType, price, upward);
}
