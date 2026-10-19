namespace KGIOptionPricing.Strategies.TradeFlowTrend;
public sealed record FlowInputs(decimal? MiniFast,decimal? MiniSlow,decimal? MiniReal,decimal? MiniAverage,decimal? LargeReal,decimal? LargeAverage);
public static class TradeFlowSignalRules
{
    public static bool SarExit(int positionSign,decimal quote,SarSnapshot sar)=>positionSign>0
        ? !sar.IsLong || quote<=sar.Value : positionSign<0 && (sar.IsLong || quote>=sar.Value);
    public static int EntryDirection(FlowInputs x,TradeFlowParameters p)=>
        x.MiniFast>x.MiniSlow && x.MiniReal>p.EntryDirectionThreshold && x.MiniReal-x.MiniAverage>p.EntryAverageMargin &&
        x.LargeReal>p.EntryDirectionThreshold && x.LargeReal-x.LargeAverage>p.EntryAverageMargin?1:
        x.MiniFast<x.MiniSlow && x.MiniReal< -p.EntryDirectionThreshold && x.MiniReal-x.MiniAverage< -p.EntryAverageMargin &&
        x.LargeReal< -p.EntryDirectionThreshold && x.LargeReal-x.LargeAverage< -p.EntryAverageMargin?-1:0;
    public static bool TrendExit(int positionSign,FlowInputs x,TradeFlowParameters p)=>
        positionSign>0?x.MiniFast<x.MiniSlow && x.MiniReal< -p.ExitDirectionThreshold && x.LargeReal< -p.ExitDirectionThreshold:
        positionSign<0 && x.MiniFast>x.MiniSlow && x.MiniReal>p.ExitDirectionThreshold && x.LargeReal>p.ExitDirectionThreshold;
}
