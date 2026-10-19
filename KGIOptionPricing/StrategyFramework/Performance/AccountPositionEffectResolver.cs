using KGIOptionPricing.OrderManagement.Core;

namespace KGIOptionPricing.StrategyFramework.Performance;

public static class AccountPositionEffectResolver
{
    public static PositionEffect Resolve(int accountNetQuantity, BuySell side, int orderQuantity)
    {
        if (orderQuantity <= 0) throw new ArgumentOutOfRangeException(nameof(orderQuantity));
        int signed = side == BuySell.Buy ? orderQuantity : -orderQuantity;
        if (accountNetQuantity == 0 || Math.Sign(accountNetQuantity) == Math.Sign(signed)) return PositionEffect.Open;
        if (orderQuantity <= Math.Abs(accountNetQuantity)) return PositionEffect.Close;
        throw new InvalidOperationException("委託同時跨越平倉與新倉，必須拆成兩筆券商委託。");
    }
}
