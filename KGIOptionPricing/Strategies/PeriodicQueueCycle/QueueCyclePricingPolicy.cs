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

        decimal candidate = SelectForAheadTarget(side, book, Math.Max(0, targetAhead), tickSize);

        return new QueuePriceDecision(
            QueuePriceAction.Place,
            candidate,
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

        int betterQuantity = BetterPriceQuantity(side, book, currentPrice);
        long totalAhead = (long)normalizedSameAhead + betterQuantity;
        if (totalAhead <= thresholdB)
            return Hold(currentPrice, normalizedSameAhead, betterQuantity, "RepriceThresholdNotExceeded");

        decimal candidate = SelectForAheadTarget(side, book, Math.Max(0, targetC), tickSize);
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

    private static decimal SelectForAheadTarget(
        BuySell side,
        MarketEvent book,
        int targetAhead,
        decimal tickSize)
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

        int cumulative = 0;
        decimal selected = prices[^1];
        for (int i = 0; i < prices.Length; i++)
        {
            cumulative += quantities[i];
            selected = prices[i];
            if (cumulative >= targetAhead) break;
        }

        return selected;
    }

    private static int BetterPriceQuantity(BuySell side, MarketEvent book, decimal currentPrice)
    {
        decimal[] prices = side == BuySell.Buy ? book.BidPrices : book.AskPrices;
        int[] quantities = side == BuySell.Buy ? book.BidQuantities : book.AskQuantities;
        int total = 0;
        for (int i = 0; i < prices.Length; i++)
        {
            bool better = side == BuySell.Buy ? prices[i] > currentPrice : prices[i] < currentPrice;
            if (better) total += quantities[i];
        }

        return total;
    }

    private static bool IsValidBook(MarketEvent book, decimal tickSize)
    {
        if (book is null || book.Kind != MarketEventKind.Book ||
            book.BidPrices.Length == 0 || book.AskPrices.Length == 0 ||
            book.BidPrices.Length != book.BidQuantities.Length ||
            book.AskPrices.Length != book.AskQuantities.Length ||
            book.BestBid <= 0m || book.BestAsk <= 0m || book.BestBid >= book.BestAsk)
            return false;

        for (int i = 0; i < book.BidPrices.Length; i++)
        {
            if (book.BidPrices[i] <= 0m || !IsTickAligned(book.BidPrices[i], tickSize) ||
                book.BidQuantities[i] < 0 ||
                (i > 0 && book.BidPrices[i] >= book.BidPrices[i - 1]))
                return false;
        }

        for (int i = 0; i < book.AskPrices.Length; i++)
        {
            if (book.AskPrices[i] <= 0m || !IsTickAligned(book.AskPrices[i], tickSize) ||
                book.AskQuantities[i] < 0 ||
                (i > 0 && book.AskPrices[i] <= book.AskPrices[i - 1]))
                return false;
        }

        return true;
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
