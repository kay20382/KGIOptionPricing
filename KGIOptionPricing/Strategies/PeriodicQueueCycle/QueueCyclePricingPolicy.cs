using System;
using KGIOptionPricing.OrderManagement.Core;
using KGIOptionPricing.StrategyFramework;

namespace KGIOptionPricing.Strategies.PeriodicQueueCycle;

public enum QueuePriceAction { Hold, Place, Reprice }

public sealed record FrozenPriceBounds(
    decimal Anchor,
    decimal TickSize,
    decimal BuyLimit,
    decimal SellLimit);

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
    public static FrozenPriceBounds CreateBounds(decimal anchor, decimal tickSize, decimal maxChaseTicks)
    {
        if (anchor <= 0m) throw new ArgumentOutOfRangeException(nameof(anchor));
        if (tickSize <= 0m) throw new ArgumentOutOfRangeException(nameof(tickSize));
        if (maxChaseTicks < 0m) throw new ArgumentOutOfRangeException(nameof(maxChaseTicks));

        decimal chaseDistance = maxChaseTicks * tickSize;
        decimal buyLimit = AlignDown(anchor + chaseDistance, tickSize);
        decimal sellLimit = AlignUp(anchor - chaseDistance, tickSize);
        var bounds = new FrozenPriceBounds(anchor, tickSize, buyLimit, sellLimit);
        if (!IsValidBounds(bounds))
            throw new ArgumentException("The derived frozen price bounds are inconsistent.", nameof(maxChaseTicks));
        return bounds;
    }

    public QueuePriceDecision SelectInitial(
        BuySell side,
        MarketEvent book,
        int targetAhead,
        FrozenPriceBounds bounds)
    {
        if (!IsValidBounds(bounds))
            return Hold(0m, 0, 0, "InvalidBounds");
        if (!IsValidBook(book, bounds.TickSize))
            return Hold(0m, 0, 0, "InvalidBook");

        decimal candidate = SelectForAheadTarget(side, book, Math.Max(0, targetAhead), bounds.TickSize);
        decimal bounded = side == BuySell.Buy
            ? Math.Min(candidate, bounds.BuyLimit)
            : Math.Max(candidate, bounds.SellLimit);

        return new QueuePriceDecision(
            QueuePriceAction.Place,
            bounded,
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
        FrozenPriceBounds bounds)
    {
        int normalizedSameAhead = Math.Max(0, samePriceAhead);
        if (!IsValidBounds(bounds))
            return Hold(currentPrice, normalizedSameAhead, 0, "InvalidBounds");
        if (!IsValidBook(book, bounds.TickSize) || currentPrice <= 0m)
            return Hold(currentPrice, normalizedSameAhead, 0, "InvalidBook");

        int betterQuantity = BetterPriceQuantity(side, book, currentPrice);
        long totalAhead = (long)normalizedSameAhead + betterQuantity;
        if (totalAhead <= thresholdB)
            return Hold(currentPrice, normalizedSameAhead, betterQuantity, "RepriceThresholdNotExceeded");

        decimal candidate = SelectForAheadTarget(side, book, Math.Max(0, targetC), bounds.TickSize);
        bool crossesBoundary = side == BuySell.Buy
            ? candidate > bounds.BuyLimit
            : candidate < bounds.SellLimit;
        bool moreAggressive = side == BuySell.Buy
            ? candidate > currentPrice
            : candidate < currentPrice;

        if (crossesBoundary || !moreAggressive)
            return Hold(currentPrice, normalizedSameAhead, betterQuantity, "PriceBoundaryReached");

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

    private static bool IsValidBounds(FrozenPriceBounds bounds) =>
        bounds is not null &&
        bounds.Anchor > 0m &&
        bounds.TickSize > 0m &&
        bounds.BuyLimit > 0m &&
        bounds.SellLimit > 0m &&
        IsTickAligned(bounds.BuyLimit, bounds.TickSize) &&
        IsTickAligned(bounds.SellLimit, bounds.TickSize);

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

    private static decimal AlignDown(decimal price, decimal tickSize) =>
        decimal.Floor(price / tickSize) * tickSize;

    private static decimal AlignUp(decimal price, decimal tickSize) =>
        decimal.Ceiling(price / tickSize) * tickSize;

    private static bool IsTickAligned(decimal price, decimal tickSize) =>
        tickSize > 0m && price % tickSize == 0m;

    private static QueuePriceDecision Hold(
        decimal currentPrice,
        int samePriceAhead,
        int betterQuantity,
        string reasonCode) =>
        new(QueuePriceAction.Hold, currentPrice, samePriceAhead, betterQuantity, reasonCode);
}
