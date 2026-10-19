using System;
using System.Collections.Generic;
using System.Linq;
using KGIOptionPricing.OrderManagement.Core;

namespace KGIOptionPricing;

public sealed class PriceLadderRange
{
    public decimal ReferencePrice { get; init; }
    public decimal LowerBound { get; init; }
    public decimal UpperBound { get; init; }
    public bool HasDailyLimits { get; init; }
    public int LowerExpansionLevel { get; init; }
    public int UpperExpansionLevel { get; init; }
}

public static class PriceLadderRangeBuilder
{
    public static PriceLadderRange Create(ContractInfo contract, decimal settlementOrLast)
    {
        if (contract.FallPx > 0 && contract.RisePx > contract.FallPx)
        {
            return new PriceLadderRange
            {
                ReferencePrice = settlementOrLast > 0 ? settlementOrLast : contract.PreClosePx,
                LowerBound = Regular(contract, contract.FallPx, true),
                UpperBound = Regular(contract, contract.RisePx, false),
                HasDailyLimits = true
            };
        }

        decimal reference = contract.PreClosePx > 0 ? contract.PreClosePx : settlementOrLast;
        if (reference <= 0) return new PriceLadderRange();
        return CreateUnlimited(contract, reference, 0, 0);
    }

    public static PriceLadderRange ExpandIfNeeded(ContractInfo contract, PriceLadderRange range, decimal marketPrice)
    {
        if (range.HasDailyLimits || range.ReferencePrice <= 0 || marketPrice <= 0) return range;
        decimal span = range.UpperBound - range.LowerBound;
        if (span <= 0) return range;
        int upper = range.UpperExpansionLevel;
        int lower = range.LowerExpansionLevel;
        if (marketPrice >= range.UpperBound - span * 0.10m) upper++;
        else if (marketPrice <= range.LowerBound + span * 0.10m) lower++;
        return upper == range.UpperExpansionLevel && lower == range.LowerExpansionLevel
            ? range : CreateUnlimited(contract, range.ReferencePrice, lower, upper);
    }

    public static PriceLadderRange IncludeWorkingPrices(ContractInfo contract, PriceLadderRange range, IEnumerable<decimal> prices)
    {
        if (range.ReferencePrice <= 0 || range.HasDailyLimits) return range;
        decimal min = prices.DefaultIfEmpty(range.LowerBound).Min();
        decimal max = prices.DefaultIfEmpty(range.UpperBound).Max();
        int lower = range.LowerExpansionLevel;
        int upper = range.UpperExpansionLevel;
        while (min < range.ReferencePrice * (0.90m - lower * 0.05m)) lower++;
        while (max > range.ReferencePrice * (1.10m + upper * 0.05m)) upper++;
        return CreateUnlimited(contract, range.ReferencePrice, lower, upper);
    }

    public static List<decimal> BuildPrices(ContractInfo contract, PriceLadderRange range, int safetyLimit = 200000)
    {
        var result = new List<decimal>();
        if (range.UpperBound <= range.LowerBound) return result;
        decimal price = range.LowerBound;
        for (int i = 0; i < safetyLimit && price <= range.UpperBound; i++)
        {
            result.Add(price);
            decimal next = PriceTick.getNextTick(price, contract.ProductId, true);
            if (next <= price) next = price + FallbackTick(contract);
            price = next;
        }
        if (result.Count == 0 || result[^1] < range.UpperBound) result.Add(range.UpperBound);
        result.Reverse();
        return result;
    }

    private static PriceLadderRange CreateUnlimited(ContractInfo contract, decimal reference, int lower, int upper)
    {
        decimal lowFactor = Math.Max(0.01m, 0.90m - lower * 0.05m);
        decimal highFactor = 1.10m + upper * 0.05m;
        return new PriceLadderRange
        {
            ReferencePrice = reference,
            LowerBound = Regular(contract, reference * lowFactor, false),
            UpperBound = Regular(contract, reference * highFactor, true),
            LowerExpansionLevel = lower,
            UpperExpansionLevel = upper
        };
    }

    private static decimal Regular(ContractInfo c, decimal p, bool up)
    {
        decimal value = PriceTick.getRegularPx(c.TickType, p, up);
        return value > 0 ? value : p;
    }

    private static decimal FallbackTick(ContractInfo c) => c.TickSize > 0 ? c.TickSize : 0.01m;
}

public sealed class WorkingOrderLevel
{
    public decimal Price { get; init; }
    public int BuyLeaves { get; init; }
    public int SellLeaves { get; init; }
    public int BuyCount { get; init; }
    public int SellCount { get; init; }
    public bool HasPendingBuy { get; init; }
    public bool HasPendingSell { get; init; }
    public IReadOnlyList<OrderInfo> Orders { get; init; } = Array.Empty<OrderInfo>();
}

public static class WorkingOrderAggregator
{
    public static IReadOnlyDictionary<decimal, WorkingOrderLevel> Aggregate(IEnumerable<OrderInfo> orders, ContractInfo contract)
    {
        int decimals = Math.Max(contract.PxDecimal, PriceTick.getPrcDigital(contract.ProductId));
        return orders
            .Where(o => MatchesContract(o.Instrument, contract))
            .Where(o => o.Status is not OrderStatus.Filled and not OrderStatus.Cancelled and not OrderStatus.Rejected)
            .Where(o => o.OrderPrice > 0 && o.LeavesQty > 0)
            .GroupBy(o => decimal.Round((decimal)o.OrderPrice, decimals, MidpointRounding.AwayFromZero))
            .ToDictionary(g => g.Key, g => new WorkingOrderLevel
            {
                Price = g.Key,
                BuyLeaves = g.Where(o => o.BuySell == BuySell.Buy).Sum(o => o.LeavesQty),
                SellLeaves = g.Where(o => o.BuySell == BuySell.Sell).Sum(o => o.LeavesQty),
                BuyCount = g.Count(o => o.BuySell == BuySell.Buy),
                SellCount = g.Count(o => o.BuySell == BuySell.Sell),
                HasPendingBuy = g.Any(o => o.BuySell == BuySell.Buy && IsPending(o.Status)),
                HasPendingSell = g.Any(o => o.BuySell == BuySell.Sell && IsPending(o.Status)),
                Orders = g.OrderBy(o => o.SubmitTime).ToArray()
            });
    }

    public static bool MatchesContract(InstrumentKey? instrument, ContractInfo contract)
    {
        if (instrument is null)
            return false;

        bool directSymbolMatch = SymbolEquals(instrument.Symbol, contract.Symbol);
        bool foreignProductMatch = instrument.Market is MarketType.ForeignFuture or MarketType.ForeignOption &&
                                   SymbolEquals(instrument.Symbol, contract.ProductId);
        if (!directSymbolMatch && !foreignProductMatch)
            return false;

        string exchange = instrument.Exchange.Trim();
        string contractExchange = contract.ExchangeNm.Trim();
        if (exchange.Length > 0 && contractExchange.Length > 0 &&
            !exchange.Equals(contractExchange, StringComparison.OrdinalIgnoreCase))
            return false;

        string month = Digits(instrument.ContractMonth);
        if (month.Length == 0)
            return true;

        return new[] { contract.ComYM2, contract.ComYM, contract.Month }
            .Select(Digits)
            .Any(candidate => candidate.Length > 0 && candidate.Equals(month, StringComparison.Ordinal));
    }

    private static bool SymbolEquals(string left, string right)
    {
        string a = left.Trim();
        string b = right.Trim();
        return a.Equals(b, StringComparison.OrdinalIgnoreCase) ||
               a.EndsWith("." + b, StringComparison.OrdinalIgnoreCase) ||
               b.EndsWith("." + a, StringComparison.OrdinalIgnoreCase);
    }

    private static string Digits(string value) => new(value.Where(char.IsDigit).ToArray());

    private static bool IsPending(OrderStatus s) => s is OrderStatus.PendingSubmit or OrderStatus.Submitted or OrderStatus.ModifyPending or OrderStatus.CancelPending;
}
