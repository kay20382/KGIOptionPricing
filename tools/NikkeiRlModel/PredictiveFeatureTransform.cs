namespace NikkeiRl;

/// <summary>Pure causal transformations of the existing market snapshot, in fixed feature order.</summary>
public static class PredictiveFeatureTransform
{
    public static readonly string[] Names=["mc_weighted_mid_offset_ticks","mc_near_deep_imbalance_gap",
        "mini_mc_return5s_gap","large_mc_return5s_gap","mc_short_momentum_acceleration",
        "mc_trade_direction_log_volume","mini_mc_obi1_gap","mc_trade_direction_momentum"];
    public static double[] Compute(double[] market)
    {
        if(market.Length!=30||market.Any(x=>!double.IsFinite(x)))throw new ArgumentException("Invalid market snapshot");
        // Legacy name uptickVolumeRatio5s actually stores sum(qty*tickSign)/sum(qty), in [-1,1].
        double direction=market[9];
        return [market[4]*market[5]/2,market[5]-market[6],market[10]-market[0],market[20]-market[0],
            market[0]-market[1]/3,direction*market[8],market[15]-market[5],direction*market[0]];
    }
}
