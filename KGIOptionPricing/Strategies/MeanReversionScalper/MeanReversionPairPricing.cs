namespace KGIOptionPricing.Strategies.MeanReversionScalper;

public readonly record struct MeanReversionPairPrices(
    decimal Buy, decimal Sell, decimal LongStop, decimal ShortStop,
    int RequestedShiftTicks, int AppliedShiftTicks);

public static class MeanReversionPairPricing
{
    public static bool TryCompute(decimal center, decimal range, decimal f, decimal k,
        decimal tickSize, decimal bid1, decimal ask1, int trendSign, int requestedShiftTicks,
        out MeanReversionPairPrices prices)
    {
        prices = default;
        if (center <= 0m || range <= 0m || f <= 0m || k <= f || tickSize <= 0m ||
            bid1 <= 0m || ask1 <= bid1 || trendSign is < -1 or > 1 ||
            requestedShiftTicks < 0 || requestedShiftTicks > 20)
            return false;

        decimal buyBase = RoundDown(center - range * f / 2m, tickSize);
        decimal sellBase = RoundUp(center + range * f / 2m, tickSize);
        decimal longStopBase = RoundDown(center - range * k / 2m, tickSize);
        decimal shortStopBase = RoundUp(center + range * k / 2m, tickSize);
        int maximumShift = trendSign == 0 ? 0 : requestedShiftTicks;

        for (int n = maximumShift; n >= 0; n--)
        {
            decimal shift = trendSign * n * tickSize;
            decimal buy = buyBase + shift;
            decimal sell = sellBase + shift;
            decimal longStop = longStopBase + shift;
            decimal shortStop = shortStopBase + shift;
            if (buy <= 0m || longStop <= 0m || sell <= buy || buy > bid1 || sell < ask1)
                continue;
            prices = new MeanReversionPairPrices(buy, sell, longStop, shortStop,
                requestedShiftTicks, n);
            return true;
        }

        return false;
    }

    private static decimal RoundDown(decimal price, decimal tickSize) =>
        decimal.Floor(price / tickSize) * tickSize;

    private static decimal RoundUp(decimal price, decimal tickSize) =>
        decimal.Ceiling(price / tickSize) * tickSize;
}
