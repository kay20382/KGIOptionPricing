using KGIOptionPricing.StrategyFramework;

namespace MeanReversionBacktest;

internal sealed class PqcResearchMetrics
{
    public decimal MaximumLoss { get; private set; }
    public decimal MaximumDrawdown { get; private set; }
    private decimal _peak;
    private string _lastDecision = "";
    private DateTimeOffset _lastDecisionTime;
    public bool ShouldRecord(StrategyDecisionObservation observation)
    {
        string signature=string.Join("|",observation.CandidateId,observation.CycleId,observation.State,
            observation.Decision,observation.Direction,observation.PrimaryReasonCode,
            string.Join(",",observation.Thresholds.Select(t=>t.Name+":"+t.Passed)),
            string.Join(",",observation.DataQualityFlags),string.Join(",",observation.SecondaryReasonCodes));
        bool keep=signature!=_lastDecision || observation.Timestamp>=_lastDecisionTime.AddSeconds(5) ||
            observation.Decision is StrategyDecision.Accept or StrategyDecision.Exit;
        if(keep){_lastDecision=signature;_lastDecisionTime=observation.Timestamp;}
        return keep;
    }
    public bool Observe(decimal markedNet)
    {
        decimal loss = Math.Max(0, -markedNet);
        bool newPeak=markedNet>_peak;
        _peak = Math.Max(_peak, markedNet);
        decimal drawdown = _peak - markedNet;
        bool changed = newPeak || loss > MaximumLoss || drawdown > MaximumDrawdown;
        MaximumLoss = Math.Max(MaximumLoss, loss);
        MaximumDrawdown = Math.Max(MaximumDrawdown, drawdown);
        return changed;
    }
}
