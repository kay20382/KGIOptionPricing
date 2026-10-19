namespace KGIOptionPricing.Strategies.MultiFactorMeanR17;

public static class ResearchRiskDistances
{
    // Only a causal feature snapshot is accepted; exits still use the common order pipeline.
    public static (decimal Stop,decimal Target)? Calculate(decimal? tickVolatility,decimal tick,
        decimal multiplier,decimal rewardRatio,int minStopTicks=4,int maxStopTicks=30,int maxTargetTicks=60)
    {
        if(tick<=0 || multiplier<=0 || rewardRatio<=0 || minStopTicks<1 || maxStopTicks<minStopTicks
            || maxStopTicks>30 || maxTargetTicks<1)throw new ArgumentOutOfRangeException(nameof(maxStopTicks));
        if(!tickVolatility.HasValue || tickVolatility<=0)return null;
        decimal ticks=decimal.Ceiling(tickVolatility.Value*(decimal)Math.Sqrt(30)*multiplier);
        ticks=Math.Clamp(ticks,minStopTicks,maxStopTicks);
        decimal targetTicks=Math.Clamp(decimal.Ceiling(ticks*rewardRatio),1,maxTargetTicks);
        return(ticks*tick,targetTicks*tick);
    }
}
