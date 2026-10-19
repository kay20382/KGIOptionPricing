using System;
using KGIOptionPricing.OrderManagement.Core;

namespace KGIOptionPricing.Strategies;

public sealed class QueueFollowPricePlanner
{
    private readonly IQueueFollowTickProvider _ticks;

    public QueueFollowPricePlanner(IQueueFollowTickProvider ticks) => _ticks = ticks;

    public QueueFollowDecision Decide(
        QueueFollowRequest request,
        OrderBookSnapshot book,
        decimal currentPrice,
        int remainingQuantity,
        int samePriceAheadQuantity)
    {
        if (remainingQuantity <= 0)
            return Hold("已無剩餘口數", samePriceAheadQuantity, 0);
        if (book.BestBid <= 0 || book.BestAsk <= 0)
            return Hold("買賣第一檔不完整", samePriceAheadQuantity, 0);

        int betterQuantity = BetterPriceQuantity(request.Side, book, currentPrice);
        int totalAhead = samePriceAheadQuantity + betterQuantity;
        int threshold = request.RepriceAheadThreshold;
        if (totalAhead <= threshold)
            return Hold($"前方量 {totalAhead} 未超過改掛門檻 B={threshold}", samePriceAheadQuantity, betterQuantity);

        decimal target = SelectPriceForAheadTarget(request, book, request.RepriceTargetAheadQuantity);
        string reason = $"前方量 {totalAhead} 大於 B={threshold}，改掛至目標前方量 C={request.RepriceTargetAheadQuantity}";
        return ApplyLimit(request, currentPrice, target, samePriceAheadQuantity, betterQuantity, reason);
    }

    /// <summary>
    /// 從第一檔向外累計委託量，選擇「第 targetAheadQuantity 口」所在的價位。
    /// 例如賣一 3 口、賣二 7 口、賣三 10 口，目標 20 會選擇賣三。
    /// 目標為 0 時代表排在目前第一檔之前；價差僅一跳時會使用對手第一檔。
    /// </summary>
    public decimal SelectPriceForAheadTarget(QueueFollowRequest request, OrderBookSnapshot book, int targetAheadQuantity)
    {
        if (book.BestBid <= 0 || book.BestAsk <= 0) return 0m;

        decimal sameSideBest = request.Side == BuySell.Buy ? book.BestBid : book.BestAsk;
        decimal opposite = request.Side == BuySell.Buy ? book.BestAsk : book.BestBid;
        decimal improved = _ticks.Next(sameSideBest, request.TickType, request.Side == BuySell.Buy);
        bool remainsPassive = request.Side == BuySell.Buy ? improved < opposite : improved > opposite;
        decimal selected = remainsPassive ? improved : opposite;

        decimal[] prices = request.Side == BuySell.Buy ? book.BidPrices : book.AskPrices;
        int[] quantities = request.Side == BuySell.Buy ? book.BidQuantities : book.AskQuantities;
        int cumulative = 0;
        if (targetAheadQuantity > 0)
        {
            for (int i = 0; i < Math.Min(prices.Length, quantities.Length); i++)
            {
                if (prices[i] <= 0) continue;
                cumulative += Math.Max(0, quantities[i]);
                selected = prices[i];
                if (cumulative >= targetAheadQuantity) break;
            }
        }

        return ConstrainToPriceLimit(request, selected);
    }

    public decimal SelectFirstPrice(QueueFollowRequest request, OrderBookSnapshot book)
    {
        decimal first = request.Side == BuySell.Buy ? book.BestBid : book.BestAsk;
        return first > 0 ? ConstrainToPriceLimit(request, first) : 0m;
    }

    public static int QuantityAtPrice(OrderBookSnapshot book, BuySell side, decimal price)
    {
        decimal[] prices = side == BuySell.Buy ? book.BidPrices : book.AskPrices;
        int[] quantities = side == BuySell.Buy ? book.BidQuantities : book.AskQuantities;
        for (int i = 0; i < Math.Min(prices.Length, quantities.Length); i++)
            if (prices[i] == price) return Math.Max(0, quantities[i]);
        return 0;
    }

    public static int BetterPriceQuantity(BuySell side, OrderBookSnapshot book, decimal currentPrice)
    {
        decimal[] prices = side == BuySell.Buy ? book.BidPrices : book.AskPrices;
        int[] quantities = side == BuySell.Buy ? book.BidQuantities : book.AskQuantities;
        int total = 0;
        for (int i = 0; i < Math.Min(prices.Length, quantities.Length); i++)
        {
            if (prices[i] <= 0) continue;
            bool better = side == BuySell.Buy ? prices[i] > currentPrice : prices[i] < currentPrice;
            if (better) total += Math.Max(0, quantities[i]);
        }
        return total;
    }

    private QueueFollowDecision ApplyLimit(
        QueueFollowRequest request,
        decimal currentPrice,
        decimal target,
        int sameAhead,
        int betterQuantity,
        string reason)
    {
        target = ConstrainToPriceLimit(request, target);
        if (target <= 0)
            return Hold("無法計算合法改掛價格", sameAhead, betterQuantity);
        if (target == currentPrice ||
            (request.Side == BuySell.Buy && target < currentPrice) ||
            (request.Side == BuySell.Sell && target > currentPrice))
            return Hold("目標前方量未產生更積極價格或已達價格界線，維持目前委託", sameAhead, betterQuantity);

        return new QueueFollowDecision
        {
            Kind = QueueFollowDecisionKind.Modify,
            TargetPrice = target,
            SamePriceAheadQuantity = sameAhead,
            BetterPriceQuantity = betterQuantity,
            Reason = reason
        };
    }

    private decimal ConstrainToPriceLimit(QueueFollowRequest request, decimal target)
    {
        target = _ticks.Regular(target, request.TickType, request.Side == BuySell.Buy);
        decimal safeLimit = _ticks.Regular(
            request.PriceLimit,
            request.TickType,
            request.Side == BuySell.Sell);
        if (request.Side == BuySell.Buy && target > safeLimit)
            target = safeLimit;
        else if (request.Side == BuySell.Sell && target < safeLimit)
            target = safeLimit;
        return target;
    }

    private static QueueFollowDecision Hold(string reason, int sameAhead, int betterQuantity) => new()
    {
        Kind = QueueFollowDecisionKind.Hold,
        SamePriceAheadQuantity = sameAhead,
        BetterPriceQuantity = betterQuantity,
        Reason = reason
    };
}
