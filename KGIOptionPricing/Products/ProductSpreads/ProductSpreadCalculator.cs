namespace KGIOptionPricing.ProductSpreads;

public static class ProductSpreadCalculator
{
    public static ProductSpreadQuote Calculate(
        ProductSpreadDefinition definition,
        SpreadLegQuote legA,
        SpreadLegQuote legB,
        DateTime calculationTime)
    {
        List<string> errors = Validate(definition, legA, legB);

        decimal? last = CalculatePrice(
            definition,
            legA,
            legB,
            leg => leg.LastPrice,
            PriceRounding.Nearest);

        // Spread Bid 是賣出價差可成交的價格：
        // 原買腳賣在 Bid，原賣腳買回 Ask，最後向下對齊 Tick。
        decimal? bid = CalculatePrice(
            definition,
            legA,
            legB,
            leg => leg.Direction == SpreadLegDirection.Buy ? leg.BidPrice : leg.AskPrice,
            PriceRounding.Down);

        // Spread Ask 是買進價差所需的價格：
        // 原買腳買在 Ask，原賣腳賣在 Bid，最後向上對齊 Tick。
        decimal? ask = CalculatePrice(
            definition,
            legA,
            legB,
            leg => leg.Direction == SpreadLegDirection.Buy ? leg.AskPrice : leg.BidPrice,
            PriceRounding.Up);

        int bidQuantity = CalculateAvailableQuantity(
            legA,
            legB,
            leg => leg.Direction == SpreadLegDirection.Buy ? leg.BidQuantity : leg.AskQuantity);
        int askQuantity = CalculateAvailableQuantity(
            legA,
            legB,
            leg => leg.Direction == SpreadLegDirection.Buy ? leg.AskQuantity : leg.BidQuantity);

        if (bid == null) errors.Add("Spread 買價所需的第一檔行情不完整");
        if (ask == null) errors.Add("Spread 賣價所需的第一檔行情不完整");
        if (last == null) errors.Add("兩腳成交價不完整");

        return new ProductSpreadQuote
        {
            SpreadId = definition.Id,
            ProductName = definition.ProductName,
            LastPrice = last,
            BidPrice = bid,
            AskPrice = ask,
            BidQuantity = bidQuantity,
            AskQuantity = askQuantity,
            ShiftPoint = definition.ShiftPoint,
            MinimumTick = definition.MinimumTick,
            PxDecimal = definition.PxDecimal,
            UpdateTime = calculationTime,
            LegA = legA,
            LegB = legB,
            IsValid = errors.Count == 0,
            InvalidReason = string.Join("；", errors.Distinct())
        };
    }

    public static IReadOnlyList<string> ValidateDefinition(ProductSpreadDefinition definition)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(definition.Id)) errors.Add("Id 不可為空");
        if (string.IsNullOrWhiteSpace(definition.ProductName)) errors.Add("ProductName 不可為空");
        ValidateLeg(definition.LegA, "LegA", errors);
        ValidateLeg(definition.LegB, "LegB", errors);
        if (definition.MinimumTick <= 0) errors.Add("MinimumTick 必須大於 0");
        if (definition.PxDecimal is < 0 or > 8) errors.Add("PxDecimal 必須介於 0 到 8");
        else if (decimal.Round(definition.MinimumTick, definition.PxDecimal) != definition.MinimumTick)
            errors.Add("MinimumTick 小數位數不可超過 PxDecimal");
        if (definition.SnapshotEnabled && definition.SnapshotIntervalSeconds < 1)
            errors.Add("SnapshotIntervalSeconds 必須大於或等於 1");
        return errors;
    }

    private static List<string> Validate(
        ProductSpreadDefinition definition,
        SpreadLegQuote legA,
        SpreadLegQuote legB)
    {
        var errors = ValidateDefinition(definition).ToList();
        if (string.IsNullOrWhiteSpace(legA.Symbol)) errors.Add("LegA 尚未解析熱門月");
        if (string.IsNullOrWhiteSpace(legB.Symbol)) errors.Add("LegB 尚未解析熱門月");
        if (legA.ContractMultiplier <= 0) errors.Add("LegA 合約乘數無效");
        if (legB.ContractMultiplier <= 0) errors.Add("LegB 合約乘數無效");
        return errors;
    }

    private static void ValidateLeg(ProductSpreadLegDefinition leg, string name, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(leg.ProductID)) errors.Add($"{name}.ProductID 不可為空");
        if (leg.Quantity <= 0) errors.Add($"{name}.Quantity 必須大於 0");
    }

    private static decimal? CalculatePrice(
        ProductSpreadDefinition definition,
        SpreadLegQuote legA,
        SpreadLegQuote legB,
        Func<SpreadLegQuote, decimal?> priceSelector,
        PriceRounding rounding)
    {
        decimal? priceA = priceSelector(legA);
        decimal? priceB = priceSelector(legB);
        if (priceA is not > 0 || priceB is not > 0 ||
            legA.ContractMultiplier <= 0 || legB.ContractMultiplier <= 0 ||
            definition.MinimumTick <= 0)
        {
            return null;
        }

        decimal raw = SignedValue(legA, priceA.Value) +
                      SignedValue(legB, priceB.Value) +
                      definition.ShiftPoint;
        return Normalize(raw, definition.MinimumTick, definition.PxDecimal, rounding);
    }

    private static decimal SignedValue(SpreadLegQuote leg, decimal price)
    {
        decimal sign = leg.Direction == SpreadLegDirection.Buy ? 1m : -1m;
        return sign * price * leg.ContractMultiplier * leg.Quantity;
    }

    private static int CalculateAvailableQuantity(
        SpreadLegQuote legA,
        SpreadLegQuote legB,
        Func<SpreadLegQuote, int> quantitySelector)
    {
        if (legA.Quantity <= 0 || legB.Quantity <= 0) return 0;
        int availableA = Math.Max(0, quantitySelector(legA)) / legA.Quantity;
        int availableB = Math.Max(0, quantitySelector(legB)) / legB.Quantity;
        return Math.Min(availableA, availableB);
    }

    private static decimal Normalize(decimal value, decimal tick, int decimals, PriceRounding rounding)
    {
        decimal ticks = value / tick;
        decimal normalized = rounding switch
        {
            PriceRounding.Up => decimal.Ceiling(ticks) * tick,
            PriceRounding.Down => decimal.Floor(ticks) * tick,
            _ => decimal.Round(ticks, 0, MidpointRounding.AwayFromZero) * tick
        };
        return decimal.Round(normalized, decimals, MidpointRounding.AwayFromZero);
    }

    private enum PriceRounding
    {
        Nearest,
        Up,
        Down
    }
}
