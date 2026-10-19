using KGIOptionPricing.StrategyFramework;

namespace NikkeiRl;

public sealed record ReplayTrade(DateTimeOffset At,long Sequence,decimal Price);
public sealed record ReplayFrame
{
    public DateTimeOffset At {get;init;}
    public string Symbol {get;init;}="";
    public decimal Bid {get;init;}
    public decimal Ask {get;init;}
    public bool Valid {get;init;}
    public bool DataGap {get;init;}
    public double[] Features {get;init;}=[];
    public double[] MarketExtras {get;init;}=[];
    public double[] PaperExtras {get;init;}=[];
    public double[] ResearchExtras {get;init;}=[];
    // Trades occurred AFTER the preceding frame, up to this frame's timestamp.
    public ReplayTrade[] Trades {get;init;}=[];
}
public sealed record RlConfig
{
    public bool AllowIncompleteEvaluation {get;init;}=false;
    public decimal Commission {get;init;}=36;
    public decimal ExtraCostTicksPerSide {get;init;}=0;
    public int DecisionLatencyMs {get;init;}=1;
    // Both modes are strict-cross limit orders; opposite-touch is never a synthetic market fill.
    public string EntryPriceMode {get;init;}="same-side";
    // Legacy bundles without this field retain five-second cadence.
    public int DecisionIntervalSeconds {get;init;}=5;
    public string FeatureProfile {get;init;}="three-product";
    public string ResearchReadinessProfile {get;init;}="all-research";
    public bool DirectionDrivenHolding {get;init;}=false;
    public int StopTicks {get;init;}=6;
    // Legacy mode preserves archived model contracts. New risk profiles require a point cap.
    public string StopMode {get;init;}="legacy-fixed";
    public decimal FixedStopPoints {get;init;}=20;
    public decimal MinStopPoints {get;init;}=5;
    public decimal? MaxStopPoints {get;init;}
    public int StopVolatilitySeconds {get;init;}=60;
    public decimal StopVolatilityMultiplier {get;init;}=2;
    public int MaxHoldingSeconds {get;init;}=60;
    public int CooldownSeconds {get;init;}=10;
    public int ObservationSize {get;init;}=40;
    public decimal DrawdownPenalty {get;init;}=0.25m;
    // Training reward only: 1=net equity, 0=gross equity; the ledger always charges costs.
    public decimal RewardCostWeight {get;init;}=1;
    public decimal TickSize {get;init;}=5;
    public decimal Multiplier {get;init;}=10;
}
public sealed record RlState
{
    public double[] Observation {get;init;}=[];
    public bool[] Mask {get;init;}=[];
    public DateTimeOffset At {get;init;}
    public decimal Reward {get;init;}
    public decimal DeltaEquity {get;init;}
    public decimal DrawdownPenalty {get;init;}
    public decimal NetPnl {get;init;}
    public decimal CompletedGrossPnl {get;init;}
    public decimal CompletedNetPnl {get;init;}
    public decimal CompletedCosts {get;init;}
    public decimal UnrealizedPnl {get;init;}
    public decimal OpenPositionCosts {get;init;}
    public decimal ExcludedOpenNetPnl {get;init;}
    public decimal Costs {get;init;}
    public decimal MaxDrawdown {get;init;}
    public int Position {get;init;}
    public int CompletedTrades {get;init;}
    public bool PendingEntry {get;init;}
    public bool Terminated {get;init;}
    public bool Truncated {get;init;}
    public int ProposedAction {get;init;}
    public int EffectiveAction {get;init;}
    public string OverrideReason {get;init;}="";
    public string Status {get;init;}="Running";
    public decimal TotalReward {get;init;}
    public decimal? WinRate {get;init;}
    public decimal? AverageWinningPoints {get;init;}
    public decimal? AverageLosingPoints {get;init;}
    public decimal? UserDefinedProfitFactor {get;init;}
    public decimal ActiveStopPrice {get;init;}
    public decimal ActiveStopDistancePoints {get;init;}
    public decimal? StopVolatilityPoints {get;init;}
    public bool StopVolatilityWarm {get;init;}
    public int StopAdjustmentCount {get;init;}
    public ExecutionEvent[] Fills {get;init;}=[];
}
