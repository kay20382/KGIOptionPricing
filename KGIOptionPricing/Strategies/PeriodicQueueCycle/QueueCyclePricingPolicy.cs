using System;
using KGIOptionPricing.OrderManagement.Core;
using KGIOptionPricing.StrategyFramework;

namespace KGIOptionPricing.Strategies.PeriodicQueueCycle;

public enum QueuePriceAction { Hold, Place, Reprice }

public sealed record QueuePriceDecision(
    QueuePriceAction Action,
    decimal Price,
    int SamePriceAheadQuantity,
    int BetterPriceQuantity,
    string ReasonCode);

/// <summary>
/// Pure queue-pricing rules shared by simulation, replay and controlled live execution.
/// The policy emits prices only; it never submits, cancels or modifies an order.
/// </summary>
public sealed class QueueCyclePricingPolicy
{
    public QueuePriceDecision SelectInitial(
        BuySell side,
        MarketEvent book,
        int targetAhead,
        decimal tickSize)
    {
        if (!IsValidBook(book, tickSize))
            return Hold(0m, 0, 0, "InvalidBook");

        decimal? candidate = SelectForAheadTarget(side, book, Math.Max(0, targetAhead), tickSize, strict: true);
        if (!candidate.HasValue) return Hold(0m, 0, 0, "InsufficientQueueDepth");

        return new QueuePriceDecision(
            QueuePriceAction.Place,
            candidate.Value,
            0,
            0,
            "InitialPriceSelected");
    }

    public QueuePriceDecision EvaluateReprice(
        BuySell side,
        MarketEvent book,
        decimal currentPrice,
        int samePriceAhead,
        int thresholdB,
        int targetC,
        decimal tickSize)
    {
        int normalizedSameAhead = Math.Max(0, samePriceAhead);
        if (!IsValidBook(book, tickSize) || currentPrice <= 0m)
            return Hold(currentPrice, normalizedSameAhead, 0, "InvalidBook");

        int betterQuantity = BetterPriceQuantity(side, book, currentPrice, tickSize);
        long totalAhead = (long)normalizedSameAhead + betterQuantity;
        if (totalAhead <= thresholdB)
            return Hold(currentPrice, normalizedSameAhead, betterQuantity, "RepriceThresholdNotExceeded");

        decimal? target = SelectForAheadTarget(side, book, Math.Max(0, targetC), tickSize, strict: false);
        if (!target.HasValue) return Hold(currentPrice, normalizedSameAhead, betterQuantity, "InsufficientQueueDepth");
        decimal candidate = target.Value;
        bool moreAggressive = side == BuySell.Buy
            ? candidate > currentPrice
            : candidate < currentPrice;

        if (!moreAggressive)
            return Hold(currentPrice, normalizedSameAhead, betterQuantity,
                candidate == currentPrice ? "TargetPriceUnchanged" : "TargetPriceLessAggressive");

        return new QueuePriceDecision(
            QueuePriceAction.Reprice,
            candidate,
            normalizedSameAhead,
            betterQuantity,
            "RepriceThresholdExceeded");
    }

    private static decimal? SelectForAheadTarget(
        BuySell side,
        MarketEvent book,
        int targetAhead,
        decimal tickSize,
        bool strict)
    {
        decimal[] prices = side == BuySell.Buy ? book.BidPrices : book.AskPrices;
        int[] quantities = side == BuySell.Buy ? book.BidQuantities : book.AskQuantities;
        if (targetAhead == 0)
        {
            decimal sameSideBest = prices[0];
            decimal opposite = side == BuySell.Buy ? book.BestAsk : book.BestBid;
            decimal improved = side == BuySell.Buy ? sameSideBest + tickSize : sameSideBest - tickSize;
            bool remainsPassive = side == BuySell.Buy ? improved < opposite : improved > opposite;
            return remainsPassive ? improved : opposite;
        }

        long cumulative = 0;
        int count = VisibleDepthCount(side, book, tickSize);
        for (int i = 0; i < count; i++)
        {
            cumulative += quantities[i];
            if (strict ? cumulative > targetAhead : cumulative >= targetAhead) return prices[i];
        }
        return null;
    }

    private static int BetterPriceQuantity(BuySell side, MarketEvent book, decimal currentPrice, decimal tickSize)
    {
        decimal[] prices = side == BuySell.Buy ? book.BidPrices : book.AskPrices;
        int[] quantities = side == BuySell.Buy ? book.BidQuantities : book.AskQuantities;
        long total = 0;
        int count = VisibleDepthCount(side, book, tickSize);
        for (int i = 0; i < count; i++)
        {
            bool better = side == BuySell.Buy ? prices[i] > currentPrice : prices[i] < currentPrice;
            if (better) total += quantities[i];
        }

        return (int)Math.Min(total, int.MaxValue);
    }

    private static bool IsValidBook(MarketEvent book, decimal tickSize)
    {
        if (book is null || book.Kind != MarketEventKind.Book ||
            book.BidPrices.Length < 3 || book.AskPrices.Length < 3 ||
            book.BidQuantities.Length < 3 || book.AskQuantities.Length < 3 ||
            book.BestBid <= 0m || book.BestAsk <= 0m || book.BestBid >= book.BestAsk)
            return false;

        return VisibleDepthCount(BuySell.Buy, book, tickSize) >= 3 &&
               VisibleDepthCount(BuySell.Sell, book, tickSize) >= 3;
    }

    // Use only the contiguous valid prefix; never skip a missing level and count deeper liquidity.
    internal static int VisibleDepthCount(BuySell side, MarketEvent book, decimal tickSize)
    {
        decimal[] prices = side == BuySell.Buy ? book.BidPrices : book.AskPrices;
        int[] quantities = side == BuySell.Buy ? book.BidQuantities : book.AskQuantities;
        int count = 0;
        while (count < prices.Length && count < quantities.Length)
        {
            if (prices[count] <= 0m || !IsTickAligned(prices[count], tickSize) || quantities[count] < 0 ||
                (count > 0 && (side == BuySell.Buy
                    ? prices[count] >= prices[count - 1] : prices[count] <= prices[count - 1]))) break;
            count++;
        }
        return count;
    }

    private static bool IsTickAligned(decimal price, decimal tickSize) =>
        tickSize > 0m && price % tickSize == 0m;

    private static QueuePriceDecision Hold(
        decimal currentPrice,
        int samePriceAhead,
        int betterQuantity,
        string reasonCode) =>
        new(QueuePriceAction.Hold, currentPrice, samePriceAhead, betterQuantity, reasonCode);
}
