namespace KGIOptionPricing.Strategies.MultiFactorMeanR17;

public static class FillAnchoredRisk
{
    public static (decimal Stop,decimal Target)? Calculate(decimal entry,decimal submissionPrice,decimal plannedStop,decimal plannedTarget,int direction,decimal tick)
    {
        if(direction is not (-1 or 1) || tick<=0 || entry<=0 || submissionPrice<=0)return null;
        decimal risk=direction*(submissionPrice-plannedStop),reward=direction*(plannedTarget-submissionPrice);
        if(risk<=0 || risk>150 || reward<=0 || entry%tick!=0 || risk%tick!=0 || reward%tick!=0)return null;
        decimal stop=entry-direction*risk,target=entry+direction*reward;
        return stop>0 && target>0?(stop,target):null;
    }
}
