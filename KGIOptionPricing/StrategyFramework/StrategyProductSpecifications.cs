namespace KGIOptionPricing.StrategyFramework;

/// <summary>
/// Product specifications used by the simulation and replay layer.
/// The source contract remains the fallback for products not listed here.
/// </summary>
public static class StrategyProductSpecifications
{
    public static decimal ResolveTickSize(string productId, decimal sourceTickSize = 0m)
    {
        if (string.Equals(productId, "225", StringComparison.OrdinalIgnoreCase)) return 10m;
        if (string.Equals(productId, "225M", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(productId, "225MC", StringComparison.OrdinalIgnoreCase)) return 5m;
        return sourceTickSize > 0m ? sourceTickSize : 1m;
    }

    public static decimal ResolveContractMultiplier(string productId, decimal sourceMultiplier = 0m)
    {
        // SymbolRepository normalizes known product exceptions, including 225MC.
        if (sourceMultiplier > 0m) return sourceMultiplier;

        // Safe fallbacks for archive replay when no contract metadata is available.
        // 225MC: one index point is JPY 10; its 5-point tick is therefore JPY 50.
        if (string.Equals(productId, "225MC", StringComparison.OrdinalIgnoreCase)) return 10m;
        if (string.Equals(productId, "225M", StringComparison.OrdinalIgnoreCase)) return 100m;
        if (string.Equals(productId, "225", StringComparison.OrdinalIgnoreCase)) return 1000m;
        return 1m;
    }
}
