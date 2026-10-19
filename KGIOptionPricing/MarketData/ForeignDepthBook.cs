namespace KGIOptionPricing;

/// <summary>Price-keyed foreign depth reconstruction, version 2.</summary>
public static class ForeignDepthBook
{
    public static void Apply(decimal[] bids, int[] bidQty, decimal[] asks, int[] askQty,
        char side, string position, char topMark, decimal price, int quantity)
    {
        if (side != 'B' && side != 'S') return;
        if (price <= 0 || quantity < 0) return;
        bool buy = side == 'B';
        var prices = buy ? bids : asks;
        var quantities = buy ? bidQty : askQty;
        bool delete = position == "99" && quantity == 0;
        // Position is not an insertion index. Existing prices are unique keys.
        var levels = new Dictionary<decimal, int>();
        for (int i = 0; i < prices.Length; i++)
            if (prices[i] > 0 && !levels.ContainsKey(prices[i]))
                levels.Add(prices[i], quantities[i]);
        if (delete) levels.Remove(price);
        else
        {
            // Retain the previous top-marker rule: discard superseded better prices.
            if (topMark == '0')
                foreach (var key in levels.Keys.Where(p => buy ? p > price : p < price).ToArray())
                    levels.Remove(key);
            levels[price] = quantity;
        }
        var ordered = buy ? levels.OrderByDescending(x => x.Key) : levels.OrderBy(x => x.Key);
        Array.Clear(prices);
        Array.Clear(quantities);
        int index = 0;
        foreach (var level in ordered.Take(prices.Length))
        {
            prices[index] = level.Key;
            quantities[index++] = level.Value;
        }
    }
}
