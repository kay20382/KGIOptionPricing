namespace NikkeiRl;

/// <summary>Count completed outcomes from authoritative shared-ledger deltas; no price/PnL model.</summary>
public sealed class CompactEpisodeMetrics
{
    public int Wins {get;private set;}
    public int Losses {get;private set;}
    public int Breakeven {get;private set;}
    public decimal WinningNetJPY {get;private set;}
    public decimal LosingNetJPY {get;private set;}
    public void Observe(RlState before,RlState after)
    {
        int count=after.CompletedTrades-before.CompletedTrades;
        decimal net=after.CompletedNetPnl-before.CompletedNetPnl;
        if(count==0){if(net!=0)throw new InvalidDataException("Completed PnL changed without completed trade");return;}
        if(count!=1)throw new InvalidDataException("One-contract replay must close at most one trade per action");
        if(net>0){Wins++;WinningNetJPY+=net;}else if(net<0){Losses++;LosingNetJPY+=net;}else Breakeven++;
    }
    public void Validate(RlState final,decimal multiplier)
    {
        if(Wins+Losses+Breakeven!=final.CompletedTrades||WinningNetJPY+LosingNetJPY!=final.CompletedNetPnl
           ||final.CompletedGrossPnl-final.CompletedCosts!=final.CompletedNetPnl
           ||final.NetPnl-final.ExcludedOpenNetPnl!=final.CompletedNetPnl)
            throw new InvalidDataException("Compact/shared ledger reconciliation failed");
        decimal? rate=final.CompletedTrades==0?null:100m*Wins/final.CompletedTrades;
        decimal? win=Wins==0?null:WinningNetJPY/Wins/multiplier;
        decimal? loss=Losses==0?null:LosingNetJPY/Losses/multiplier;
        if(rate!=final.WinRate||win!=final.AverageWinningPoints||loss!=final.AverageLosingPoints)
            throw new InvalidDataException("Compact/shared trade metric mismatch");
    }
}
