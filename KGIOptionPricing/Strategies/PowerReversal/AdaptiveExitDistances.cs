using KGIOptionPricing.Strategies.BuySellPower;
namespace KGIOptionPricing.Strategies.PowerReversal;

/// <summary>Entry-time distances from completed segments only. The resulting order plan is immutable.</summary>
public static class AdaptiveExitDistances
{
    public static (decimal Stop,decimal Target)? Calculate(PowerSnapshot snapshot,int direction,decimal tick,PowerReversalParameters p)
    {
        if(direction is not (1 or -1) || tick<=0)throw new ArgumentOutOfRangeException(nameof(direction));
        if(snapshot.UpSamples<p["MinMovementSamples"] || snapshot.DownSamples<p["MinMovementSamples"] ||
            !(snapshot.AverageUpPoints>0) || !(snapshot.AverageDownPoints>0))return null;
        decimal risk=(direction>0?snapshot.AverageDownPoints:snapshot.AverageUpPoints)!.Value*p["StopMovementFraction"];
        decimal reward=(direction>0?snapshot.AverageUpPoints:snapshot.AverageDownPoints)!.Value*p["TargetMovementFraction"];
        decimal stopTicks=Math.Clamp(decimal.Ceiling(risk/tick),p["AdaptiveMinStopTicks"],p["AdaptiveMaxStopTicks"]);
        decimal targetTicks=Math.Min(decimal.Floor(reward/tick),p["AdaptiveMaxTargetTicks"]);
        return targetTicks<1?null:(stopTicks*tick,targetTicks*tick);
    }
}
