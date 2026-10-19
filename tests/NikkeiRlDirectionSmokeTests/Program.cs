using NikkeiRl;
using KGIOptionPricing.StrategyFramework;
using System.Text.Json;

int passed=0;
void Check(bool value,string message) { if(!value){Console.Error.WriteLine(message);Environment.Exit(1);}passed++; }
Check(new RlConfig().DecisionLatencyMs==1,"default trading action delay is 1 ms");
Check(new RlConfig().CooldownSeconds==10,"default cooldown is fixed at 10 seconds");
Check(new RlConfig().EntryPriceMode=="same-side","legacy entry quote remains same-side");
var start=DateTimeOffset.Parse("2026-09-01T08:00:00+08:00");
ReplayFrame Frame(int seconds, decimal bid=100, decimal ask=105, params decimal[] trades) => new() {
    At=start.AddSeconds(seconds), Symbol="225MC-TEST", Bid=bid,Ask=ask,Valid=true, Features=new double[30],
    Trades=trades.Select((p,i)=>new ReplayTrade(start.AddSeconds(seconds-4+i),seconds*100+i,p)).ToArray()
};
var frames=new[]{Frame(0),Frame(5,100,105,100),Frame(10,100,105,95),Frame(15,110,115,110),Frame(20,110,115,110),Frame(25,110,115,110),Frame(30,110,115,110),Frame(35,110,115,110),Frame(40,110,115,110)};
var oppositeBuy=new RlReplayEnvironment(frames,new RlConfig{EntryPriceMode="opposite-touch"});oppositeBuy.Reset();var oppositeBuyState=oppositeBuy.Step(1);
Check(oppositeBuyState.Position==1&&oppositeBuyState.Fills.Single().Price==105&&oppositeBuyState.Costs==36,"opposite quote buy fills at ask after strict crossing, no favorable fill price");
var oppositeSellFrames=new[]{Frame(0),Frame(5,100,105,105),Frame(10)};
var oppositeSell=new RlReplayEnvironment(oppositeSellFrames,new RlConfig{EntryPriceMode="opposite-touch"});oppositeSell.Reset();var oppositeSellState=oppositeSell.Step(2);
Check(oppositeSellState.Position==-1&&oppositeSellState.Fills.Single().Price==100,"opposite quote sell fills at bid");
var oppositeTouchFrames=new[]{Frame(0),Frame(5,100,105,105),Frame(10)};
var oppositeTouch=new RlReplayEnvironment(oppositeTouchFrames,new RlConfig{EntryPriceMode="opposite-touch"});oppositeTouch.Reset();var oppositeTouchState=oppositeTouch.Step(1);
Check(oppositeTouchState.Position==0&&oppositeTouchState.PendingEntry,"opposite quote touch still does not fill");
bool badEntryModeRejected=false;try{_ =new RlReplayEnvironment(frames,new RlConfig{EntryPriceMode="market"});}catch(ArgumentException){badEntryModeRejected=true;}
Check(badEntryModeRejected,"unknown entry pricing cannot change order semantics");
var earlyOppositeFrames=new[]{Frame(0),Frame(5) with{Trades=[new(start.AddTicks(5000),1,100)]},Frame(10) with{Trades=[new(start.AddSeconds(6),2,100)]}};
var earlyOpposite=new RlReplayEnvironment(earlyOppositeFrames,new RlConfig{EntryPriceMode="opposite-touch",DecisionLatencyMs=1});earlyOpposite.Reset();var earlyOppositeState=earlyOpposite.Step(1);
Check(earlyOppositeState.Position==0&&earlyOppositeState.PendingEntry,"opposite quote cannot fill before1ms delay");
earlyOppositeState=earlyOpposite.Step(0);
Check(earlyOppositeState.Position==1&&earlyOppositeState.Fills.Single().Price==105,"opposite quote uses fixed decision quote after delay");
string entryOverrideTest=Path.Combine(Path.GetTempPath(),"rl-entry-"+Guid.NewGuid()+".json");
try
{
    File.WriteAllText(entryOverrideTest,"{\"entryPriceMode\":\"opposite-touch\",\"extraCostTicksPerSide\":2}");
    var entryOverride=EvaluationReplay.ApplyEntryOverrides(new RlConfig{StopMode="fixed",FixedStopPoints=150,MaxStopPoints=150},entryOverrideTest);
    Check(entryOverride.EntryPriceMode=="opposite-touch"&&entryOverride.ExtraCostTicksPerSide==2&&entryOverride.Commission==36&&entryOverride.MaxStopPoints==150,"entry sensitivity preserves costs and stop contract");
    foreach(var invalid in new[]{"{\"entryPriceMode\":\"opposite-touch\",\"commission\":0}","{\"entryPriceMode\":\"market\"}","{\"entryPriceMode\":\"same-side\",\"extraCostTicksPerSide\":3}"})
    {
        File.WriteAllText(entryOverrideTest,invalid);bool rejectedEntryOverride=false;
        try{_ =EvaluationReplay.ApplyEntryOverrides(new RlConfig(),entryOverrideTest);}catch(InvalidDataException){rejectedEntryOverride=true;}
        Check(rejectedEntryOverride,"unsafe entry override rejected");
    }
}
finally{File.Delete(entryOverrideTest);}
var env=new RlReplayEnvironment(frames,new RlConfig{Commission=36,StopTicks=6,CooldownSeconds=0});
var state=env.Reset();
Check(state.Mask.SequenceEqual(new[]{true,true,true,false,false}),"flat mask");
state=env.Step(1);
Check(state.Position==0 && state.PendingEntry,"touch must not fill");
Check(state.Reward==0,"unfilled reward zero");
state=env.Step(0);
Check(state.Position==1 && state.Costs==36,"strict cross uses shared fee");
Check(state.Fills.Single().Price==100,"limit fill fixed at requested price");
Check(!state.Mask[1] && !state.Mask[2] && state.Mask[3],"held mask");
state=env.Step(3);
Check(state.Position==0 && state.CompletedTrades==1,"flatten closes one round trip");
Check(state.NetPnl==28,"shared ledger 10 points * 10 minus 72");
Check(Math.Abs(env.TotalDeltaEquity-28)<0.00001m,"equity rewards telescope");
Check(state.Fills.Single().Role==PaperOrderRole.Flatten,"time/manual exit role");
var bad=new RlReplayEnvironment(frames,new RlConfig());bad.Reset();var rejected=bad.Step(3);
Check(rejected.OverrideReason=="INVALID_ACTION" && rejected.Position==0,"invalid flatten no short");
var riskFrames=new[]{Frame(0),Frame(5,100,105,95),Frame(10,65,70,65),Frame(15,60,65,65),Frame(20),Frame(25),Frame(30),Frame(35)};
var risk=new RlReplayEnvironment(riskFrames,new RlConfig{Commission=36,StopTicks=6});risk.Reset();risk.Step(1);var stop=risk.Step(0);
Check(stop.Fills.Single().Role==PaperOrderRole.StopLoss && stop.Position==0,"fixed stop remains protective");
var truncated=new RlReplayEnvironment(new[]{Frame(0),Frame(5,100,105,95),Frame(10),Frame(15)},new RlConfig());truncated.Reset();truncated.Step(1);truncated.Step(0);var tail=truncated.Step(0);
Check(tail.Truncated && !tail.Terminated && tail.Position==1,"no synthetic terminal close");
Check(tail.Status=="IncompleteUnclosedPosition","open ending incomplete");
var cancelled=new RlReplayEnvironment(frames,new RlConfig());cancelled.Reset();cancelled.Step(1);var cancel=cancelled.Step(4);
Check(cancel.Position==0 && !cancel.PendingEntry && cancel.Costs==0,"cancel before subsequent cross");
var feeStress=new RlReplayEnvironment(frames,new RlConfig{ExtraCostTicksPerSide=1,CooldownSeconds=0});feeStress.Reset();feeStress.Step(1);feeStress.Step(0);var extra=feeStress.Step(3);
Check(extra.NetPnl==-72 && extra.Costs==172,"cost sensitivity once per side");
var gapFrames=new[]{Frame(0),Frame(5),Frame(15)};var gap=new RlReplayEnvironment(gapFrames,new RlConfig());gap.Reset();gap.Step(0);var g=gap.Step(0);
Check(g.Truncated && g.Status=="IncompleteDataGap","missing step invalid");
var latencyFrames=new[]{Frame(0),new ReplayFrame{At=start.AddSeconds(5),Symbol="225MC-TEST",Bid=100,Ask=105,Valid=true,Features=new double[30],Trades=[new(start.AddMilliseconds(50),1,95),new(start.AddMilliseconds(200),2,105)]},Frame(10,100,105,95),Frame(15),Frame(20),Frame(25)};
var delayed=new RlReplayEnvironment(latencyFrames,new RlConfig{DecisionLatencyMs=100});delayed.Reset();var late=delayed.Step(1);
Check(late.Position==0,"inference delay misses crossing before send");
var immediate=new RlReplayEnvironment(latencyFrames,new RlConfig());immediate.Reset();Check(immediate.Step(1).Position==1,"same tape without delay fills");
var masked=RlPolicyPredictor.SelectAction(new float[]{100,1,2,3,4},new[]{true,false,false,false,false});
Check(masked.Action==0 && masked.Probabilities[0]==1,"masked action cannot be selected");
Check(RlPolicyPredictor.SelectAction(new float[]{0,0,0,0,0},new[]{true,true,false,false,false}).Action==0,"tie order deterministic");
bool invalidLogits=false;try{RlPolicyPredictor.SelectAction(new float[]{float.NaN,0,0,0,0},new[]{true,true,true,true,true});}catch(ArgumentException){invalidLogits=true;}
Check(invalidLogits,"NaN policy rejected");
var prefixA=new RlReplayEnvironment(frames.Take(4).ToArray(),new RlConfig());var prefixB=new RlReplayEnvironment(frames,new RlConfig());
Check(prefixA.Reset().Observation.SequenceEqual(prefixB.Reset().Observation),"file tail cannot influence observation");
prefixA.Step(0);prefixB.Step(0);
Check(prefixA.Step(0).Mask.SequenceEqual(prefixB.Step(0).Mask),"file tail cannot influence action mask");
var delayedStopFrames=new[]{Frame(0),Frame(5,100,105,95),new ReplayFrame{At=start.AddSeconds(10),Symbol="225MC-TEST",Bid=60,Ask=65,Valid=true,Features=new double[30],Trades=[new(start.AddSeconds(5.05),2,65),new(start.AddSeconds(5.2),3,100)]},Frame(15),Frame(20),Frame(25)};
var delayedStop=new RlReplayEnvironment(delayedStopFrames,new RlConfig{DecisionLatencyMs=100});delayedStop.Reset();delayedStop.Step(1);
Check(delayedStop.Step(3).Fills.Any(x=>x.Role==PaperOrderRole.StopLoss),"stop survives inference delay before flatten");
var rawGapFrames=new[]{Frame(0),Frame(5,100,105,95),Frame(10) with{DataGap=true},Frame(15),Frame(20)};
var rawGap=new RlReplayEnvironment(rawGapFrames,new RlConfig());rawGap.Reset();rawGap.Step(1);
Check(rawGap.Step(0).Status=="IncompleteDataGap","raw gap cannot be hidden by regular frames");
var regularCloseFrames=new[]{Frame(0) with{At=DateTimeOffset.Parse("2026-09-01T14:39:50+08:00")},Frame(5) with{At=DateTimeOffset.Parse("2026-09-01T14:39:55+08:00")}};
var regularClose=new RlReplayEnvironment(regularCloseFrames,new RlConfig());
Check(!regularClose.Reset().Mask[1],"entry disabled before regular-session trading stops at 14:40 Taipei");
var grossRewardEnv=new RlReplayEnvironment(frames,new RlConfig{RewardCostWeight=0,CooldownSeconds=0});
grossRewardEnv.Reset();grossRewardEnv.Step(1);grossRewardEnv.Step(0);var grossRewardResult=grossRewardEnv.Step(3);
Check(grossRewardResult.NetPnl==28 && grossRewardResult.Costs==72,"gross training reward keeps authoritative net accounting");
Check(grossRewardResult.TotalReward-state.TotalReward==72m/50m,"cost reward weight changes shaping only once");
var tenSecondCooldown=new RlReplayEnvironment(frames,new RlConfig{CooldownSeconds=10});
tenSecondCooldown.Reset();tenSecondCooldown.Step(1);tenSecondCooldown.Step(0);var justClosed=tenSecondCooldown.Step(3);
Check(!justClosed.Mask[1] && !justClosed.Mask[2],"entry blocked before ten-second cooldown expires");
Check(!tenSecondCooldown.Step(0).Mask[1],"entry remains blocked at first decision after exit");
Check(tenSecondCooldown.Step(0).Mask[1],"entry reopens at first decision after ten-second cooldown");
var microFeatures=new RlFeatureAccumulator();
microFeatures.OnBook("225MC",start,1,"225MC-Z26",100,105,10,10);
microFeatures.OnBook("225M",start,2,"225M-Z26",100,105,10,10);
microFeatures.OnBook("225",start,3,"225-Z26",100,105,10,10);
microFeatures.OnBook("225MC",start.AddSeconds(1),4,"225MC-Z26",100,105,20,10);
var ofiFeatures=microFeatures.Snapshot(start.AddSeconds(1));
Check(Math.Abs(ofiFeatures[0]-10d/12.5)<1e-10,"OFI same-price bid replenishment normalized by mean depth");
microFeatures.OnTrade("225MC",start.AddSeconds(1.1),5,105,3);
microFeatures.OnTrade("225MC",start.AddSeconds(1.2),6,100,1);
Check(Math.Abs(microFeatures.Snapshot(start.AddSeconds(1.2))[5]-.5)<1e-10,"signed volume uses previous bid/ask");
microFeatures.OnBook("225M",start.AddSeconds(2),7,"225M-Z26",110,115,10,10);
var leadFeatures=microFeatures.Snapshot(start.AddSeconds(2));
Check(Math.Abs(leadFeatures[3]-2)<1e-10,"one-second mini lead uses known quote history");
microFeatures.OnBook("225MC",start.AddSeconds(5),8,"225MC-Z26",100,110,20,10);
Check(microFeatures.Snapshot(start.AddSeconds(5))[7]==1,"spread widening versus known five-second quote");
Check(microFeatures.Snapshot(start.AddSeconds(5))[6]==.5,"mid-path volatility measures causal price change");
bool futureRejected=false;try{microFeatures.Snapshot(start.AddSeconds(4));}catch(ArgumentException){futureRejected=true;}
Check(futureRejected,"snapshot cannot precede the latest received event");
bool contractRejected=false;try{microFeatures.OnBook("225",start.AddSeconds(6),9,"225-H27",100,105,10,10);}catch(ArgumentException){contractRejected=true;}
Check(contractRejected,"cross-product expiry mismatch rejected");
var staleFeatures=new RlFeatureAccumulator();staleFeatures.OnBook("225MC",start,1,"225MC-Z26",100,105,10,10);
staleFeatures.OnTrade("225MC",start.AddSeconds(3),2,105,4);
Check(staleFeatures.Snapshot(start.AddSeconds(3))[5]==0,"stale quotes do not classify signed volume");
var extraFrames=frames.Select(f=>f with{MarketExtras=Enumerable.Range(1,8).Select(i=>(double)i).ToArray()}).ToArray();
var extendedEnv=new RlReplayEnvironment(extraFrames,new RlConfig{ObservationSize=50,CooldownSeconds=10});
var extendedInitial=extendedEnv.Reset();var originalEnv=new RlReplayEnvironment(frames,new RlConfig{CooldownSeconds=10});
Check(extendedInitial.Observation.Length==50&&extendedInitial.Observation.Take(40).SequenceEqual(originalEnv.Reset().Observation),"50-dimensional prefix equals original40");
Check(extendedInitial.Observation.Skip(40).Take(8).SequenceEqual(Enumerable.Range(1,8).Select(i=>(double)i)),"extra market feature order");
extendedEnv.Step(1);var extendedEntry=extendedEnv.Step(0);
Check(extendedEntry.Observation[48]==0&&extendedEntry.Observation[49]==1,"MAE uses only received post-fill market price");
var extendedHold=extendedEnv.Step(0);
Check(extendedHold.Observation[48]==2&&extendedHold.Observation[49]==1,"MFE/MAE retain known path extrema");
var extendedExit=extendedEnv.Step(3);
Check(extendedExit.Position==0&&extendedExit.Observation[48]==0&&extendedExit.Observation[49]==0,"flat resets holding extrema");
bool missingExtrasRejected=false;try{new RlReplayEnvironment(frames,new RlConfig{ObservationSize=50});}catch(ArgumentException){missingExtrasRejected=true;}
Check(missingExtrasRejected,"50-dimensional environment rejects legacy-only cache");
var wideFeatures=new RlFeatureAccumulator();wideFeatures.OnBook("225MC",start,1,"225MC-Z26",100,115,10,10);
wideFeatures.OnTrade("225MC",start.AddMilliseconds(1),2,115,3);
Check(wideFeatures.Snapshot(start.AddMilliseconds(1))[5]==0,"wide spread is not a healthy trade-classification quote");
var incompleteBookFeatures=new RlFeatureAccumulator();incompleteBookFeatures.OnBook("225MC",start,1,"225MC-Z26",100,105,10,10,false);
incompleteBookFeatures.OnTrade("225MC",start.AddMilliseconds(1),2,105,3);
Check(incompleteBookFeatures.Snapshot(start.AddMilliseconds(1))[5]==0,"incomplete book does not classify signed volume");
var unverifiedManifest=new CacheManifest("nikkei-rl-cache-v3","source","A",new string('A',64),1,0,0,0,2,2,["225MC-Z26"],"day","DevelopmentPreviouslyInspected");
bool unverifiedRejected=false;try{ReplayCache.ValidateManifest(unverifiedManifest,new string('A',64));}catch(InvalidDataException){unverifiedRejected=true;}
Check(unverifiedRejected,"unverified v3 parity cannot be served");
ReplayCache.ValidateManifest(unverifiedManifest with{ParentCacheSha256=new string('B',64),LegacyParityVerified=true},new string('A',64));
Check(true,"verified v3 parity may be served");
var paper=new PaperFeatureAccumulator();
decimal[] bp=[100,95,90,85,80],ap=[105,110,115,120,125];int[] qty=[10,10,10,10,10];
paper.OnBook(start,1,"225MC-Z26",bp,ap,qty,qty);
paper.OnBook(start.AddSeconds(1),2,"225MC-Z26",bp,ap,[10,20,10,10,10],qty);
var ps=paper.Snapshot(start.AddSeconds(1));
Check(Math.Abs(ps[0]-.8)<1e-10&&ps[1]==0,"paper rank2 OFI preserves deeper queue changes");
Check(Math.Abs(ps[4]-Math.Log(13))<1e-10,"paper five-level mean quantity");
paper.OnBook(start.AddSeconds(5),3,"225MC-Z26",bp,ap,[10,20,10,10,10],qty);
ps=paper.Snapshot(start.AddSeconds(5));
Check(Math.Abs(ps[6]-.4)<1e-10&&ps[7]==0,"paper causal five-second depth derivative");
bool pastPaperEvent=false;try{paper.OnBook(start.AddSeconds(4),4,"225MC-Z26",bp,ap,qty,qty);}catch(ArgumentException){pastPaperEvent=true;}
Check(pastPaperEvent,"paper event cannot precede latest snapshot");
paper.OnBook(start.AddSeconds(6),4,"225MC-Z26",[100],ap,qty,qty);
Check(paper.Snapshot(start.AddSeconds(6)).All(x=>x==0),"paper invalid book clears active features");
var paperFrames=frames.Select(f=>f with{PaperExtras=Enumerable.Range(1,8).Select(i=>(double)i).ToArray()}).ToArray();
var paperEnv=new RlReplayEnvironment(paperFrames,new RlConfig{ObservationSize=48});
var paperInitial=paperEnv.Reset();
Check(paperInitial.Observation.Length==48&&paperInitial.Observation.Take(40).SequenceEqual(originalEnv.Reset().Observation),"paper48 removes old10 and keeps base40");
Check(paperInitial.Observation.Skip(40).SequenceEqual(Enumerable.Range(1,8).Select(i=>(double)i)),"paper48 distinct market feature order");
bool wrongPaperCache=false;try{new RlReplayEnvironment(extraFrames,new RlConfig{ObservationSize=48});}catch(ArgumentException){wrongPaperCache=true;}
Check(wrongPaperCache,"paper48 rejects superseded50 cache extras");
var shiftedPaper=new PaperFeatureAccumulator();
shiftedPaper.OnBook(start,1,"225MC-Z26",bp,ap,qty,qty);
shiftedPaper.OnBook(start.AddSeconds(1),2,"225MC-Z26",[105,100,95,90,85],[110,115,120,125,130],qty,qty);
Check(shiftedPaper.Snapshot(start.AddSeconds(1)).Take(4).All(x=>x==2),"ranked flow handles upward price reindexing");
shiftedPaper.OnBook(start.AddSeconds(2),3,"225MC-Z26",bp,ap,qty,qty);
Check(shiftedPaper.Snapshot(start.AddSeconds(2)).Take(4).All(x=>x==0),"ranked flow reverses on downward price move");
shiftedPaper.OnBook(start.AddSeconds(6),4,"225MC-Z26",bp,ap,qty,qty);
Check(shiftedPaper.Snapshot(start.AddSeconds(6)).Take(4).All(x=>x==-2),"paper five-second cutoff excludes event at cutoff");
bool paperParityRejected=false;
try{ReplayCache.ValidateManifest(unverifiedManifest with{Schema="nikkei-rl-cache-v4"},new string('A',64));}catch(InvalidDataException){paperParityRejected=true;}
Check(paperParityRejected,"v4 requires legacy parity verification");
var stalePaper=new PaperFeatureAccumulator();stalePaper.OnBook(start,1,"225MC-Z26",bp,ap,qty,qty);
stalePaper.OnBook(start.AddSeconds(3.1),2,"225MC-Z26",bp,ap,[10,20,10,10,10],qty);
Check(stalePaper.Snapshot(start.AddSeconds(3.1)).Take(4).All(x=>x==0),"first paper book after stale interval does not invent flow");
var interactions=new double[30];interactions[0]=3;interactions[1]=6;interactions[4]=2;interactions[5]=.6;interactions[6]=.2;interactions[8]=4;interactions[9]=.75;interactions[10]=5;interactions[15]=.1;interactions[20]=2;
Check(PredictiveFeatureTransform.Compute(interactions).Zip(new[]{.6,.4,2d,-1d,1d,3d,-.5,2.25}).All(x=>Math.Abs(x.First-x.Second)<1e-10),"hand calculated interactions use actual signed volume ratio");
bool interactionRejected=false;try{PredictiveFeatureTransform.Compute(new double[29]);}catch(ArgumentException){interactionRejected=true;}
Check(interactionRejected,"interaction requires exact market schema");
var predictive=new RlReplayEnvironment(paperFrames,new RlConfig{ObservationSize=56}).Reset();
Check(predictive.Observation.Length==56&&predictive.Observation.Take(48).SequenceEqual(paperInitial.Observation),"56 preserves original40 and paper8 prefix");
Check(predictive.Observation.Skip(48).SequenceEqual(PredictiveFeatureTransform.Compute(paperFrames[0].Features)),"56 appends only current causal interactions");
Check(tail.CompletedNetPnl==0 && tail.CompletedCosts==0 && tail.ExcludedOpenNetPnl==tail.NetPnl,"unclosed loss and entry fee excluded without altering raw ledger");
Check(state.CompletedNetPnl==28 && state.CompletedGrossPnl==100 && state.CompletedCosts==72 && state.ExcludedOpenNetPnl==0,"closed trade authoritative completed-only accounting");
foreach(var mark in new[]{95m,120m})
{
    var openEnv=new RlReplayEnvironment(new[]{Frame(0),Frame(5,100,105,95),Frame(10,mark,mark+5,mark)},new RlConfig());
    openEnv.Reset();openEnv.Step(1);var open=openEnv.Step(0);
    Check(open.Position==1&&open.CompletedNetPnl==0&&open.OpenPositionCosts==36&&open.ExcludedOpenNetPnl==(mark-100)*10-36,"exclude floating gains and losses symmetrically");
}
var evaluationManifest=unverifiedManifest with{Schema="nikkei-rl-cache-v5-evaluation",Usage="EvaluationOnly",DataGapFrames=0};
bool evaluationRejected=false;try{ReplayCache.ValidateManifest(evaluationManifest,new string('A',64));}catch(InvalidDataException){evaluationRejected=true;}
Check(evaluationRejected,"evaluation cache denied by default");
ReplayCache.ValidateManifest(evaluationManifest,new string('A',64),true);
bool evaluationBadRejected=false;try{ReplayCache.ValidateManifest(evaluationManifest with{BadRows=1},new string('A',64),true);}catch(InvalidDataException){evaluationBadRejected=true;}
Check(evaluationBadRejected,"evaluation opt-in cannot bypass bad source rows");
var splitFrames=Enumerable.Range(0,40).Select(i=>Frame(i*5) with{DataGap=i is >=3 and <=6}).ToArray();
var split=EvaluationReplay.Split(splitFrames);
Check(split.Count==2&&split[0][^1].At==start.AddSeconds(10)&&split[1][0].At==start.AddSeconds(95),"gap exclusion and sixty-second recovery warmup");
Check(split.All(s=>s.All(f=>!f.DataGap)),"segments contain no gap frames");
foreach(var mark in new[]{100m,120m})
{
    var combined=new RlReplayEnvironment(new[]{Frame(0),Frame(5,100,105,95),Frame(10,110,115,110),Frame(15,110,115,105),Frame(20,mark,mark+5,mark),Frame(25) with{DataGap=true}},new RlConfig{CooldownSeconds=0});
    combined.Reset();combined.Step(1);combined.Step(3);combined.Step(1);var beforeGap=combined.Step(0);var afterGap=combined.Step(0);
    Check(afterGap.Status=="IncompleteDataGap"&&afterGap.Position==1&&afterGap.Fills.Length==0,"gap never creates a synthetic final close");
    Check(afterGap.CompletedTrades==1&&afterGap.CompletedGrossPnl==100&&afterGap.CompletedNetPnl==28&&afterGap.CompletedCosts==72,"closed cycle preserved before gap with another open cycle");
    Check(afterGap.OpenPositionCosts==36&&afterGap.ExcludedOpenNetPnl==(mark-110)*10-36&&afterGap.NetPnl==beforeGap.NetPnl,"open cycle symmetric exclusion reconciles raw gap equity");
}
var cfFrames=Enumerable.Range(0,19).Select(i=>Frame(i*5,100,105,i==1?95:110)).ToArray();
var cf=ExecutableLabels.Build(cfFrames,0,new RlConfig());
Check(cf!=null&&cf.Long.Completed&&cf.Long.State.CompletedNetPnl==28&&cf.Action==1,"executable warmstart target uses actual passive entry and fee-adjusted close");
var unfilledCf=ExecutableLabels.Build(Enumerable.Range(0,19).Select(i=>Frame(i*5,100,105,100)).ToArray(),0,new RlConfig());
Check(unfilledCf!=null&&!unfilledCf.Long.Completed&&unfilledCf.Long.Status=="Unfilled"&&unfilledCf.Action==0,"up/down proxy cannot fabricate an unfilled trade");
var censoredCf=ExecutableLabels.Build(new[]{Frame(0),Frame(5,100,105,95)},0,new RlConfig());
Check(censoredCf==null,"unclosed counterfactual tail is censored not a losing/wait label");
bool cfGapRejected=false;try{ExecutableLabels.Build(cfFrames.Select((f,i)=>i==2?f with{DataGap=true}:f).ToArray(),0,new RlConfig());}catch(InvalidDataException){cfGapRejected=true;}
Check(cfGapRejected,"counterfactuals cannot cross source gaps");
Check(ExecutableLabels.ValidateEpisodes(new[]{"20260928-postgap-day","20260929-day","20260930-day","20261005-postgap-day"}).Length==4,"additional earlier date can extend training");
bool futureTrainingRejected=false;try{ExecutableLabels.ValidateEpisodes(new[]{"20261006-day"});}catch(ArgumentException){futureTrainingRejected=true;}
Check(futureTrainingRejected,"development evaluation dates forbidden as executable training sources");
var entryMark=new RlReplayEnvironment(new[]{Frame(0),Frame(5,100,105,95),Frame(10,100,105,100)},new RlConfig());
entryMark.Reset();var entryMarkState=entryMark.Step(1);
Check(entryMarkState.Fills.Single().Price==100&&entryMarkState.UnrealizedPnl==-50&&entryMarkState.NetPnl==-86,"entry fill cannot overwrite last actual trade mark with requested limit price");
var threeConfig=new RlConfig{DecisionIntervalSeconds=3};
ReplayFrame ThreeFrame(int seconds)=>Frame(seconds) with{Trades=[]};
var threeEnv=new RlReplayEnvironment(new[]{ThreeFrame(0),ThreeFrame(3),ThreeFrame(6)},threeConfig);
threeEnv.Reset();Check(threeEnv.Step(0).At==start.AddSeconds(3),"three-second environment advances by three seconds");
bool intervalMismatch=false;
try{ReplayCache.ValidateInterval(evaluationManifest with{DecisionIntervalSeconds=5},threeConfig);}catch(InvalidDataException){intervalMismatch=true;}
Check(intervalMismatch,"cannot feed a five-second cache to three-second policy");
var threeFrames=Enumerable.Range(0,31).Select(i=>ThreeFrame(i*3)).ToArray();
threeFrames[5]=threeFrames[5] with{Features=new double[30]};threeFrames[5].Features[1]=3;threeFrames[5].Features[2]=-3;
var threeCf=ExecutableLabels.Build(threeFrames,0,threeConfig,horizonSeconds:15);
Check(threeCf?.ProxyAction==1,"fifteen-second target reads fifteen-second return rather than thirty-second return");
threeFrames[4]=threeFrames[4] with{Valid=false};
Check(ExecutableLabels.Build(threeFrames,0,threeConfig,horizonSeconds:15)?.ProxyAction==null,"proxy validates every three-second frame within horizon");
var fixture=Path.Combine(Path.GetTempPath(),"rl-3s-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);
var events=new List<MarketEvent>();
for(int second=0;second<=21;second++)foreach(var product in new[]{"225MC","225M","225"})
{
    events.Add(new MarketEvent{Timestamp=start.AddSeconds(second),ProductId=product,Symbol=product+"-TEST",Kind=MarketEventKind.Trade,LastPrice=100+second*5,TradeQuantity=1});
}
var raw=Path.Combine(fixture,"raw.jsonl");File.WriteAllLines(raw,events.Select(e=>System.Text.Json.JsonSerializer.Serialize(e,ReplayCache.Json)));
var built=ReplayCache.Build(raw,Path.Combine(fixture,"3s.br"),"day",decisionIntervalSeconds:3);
var builtFrames=ReplayCache.Read(Path.Combine(fixture,"3s.br"));
Check(built.DecisionIntervalSeconds==3&&builtFrames.Length==7&&builtFrames.Zip(builtFrames.Skip(1)).All(p=>(p.Second.At-p.First.At).TotalSeconds==3),"cache emits complete three-second intervals");
Check(builtFrames[0].Trades.All(t=>t.At<start.AddSeconds(3))&&builtFrames[1].Trades.Any(t=>t.At==start.AddSeconds(3)),"boundary trade excluded from current decision and retained in next frame");
Check(builtFrames.Single(f=>f.At==start.AddSeconds(18)).Features[0]==5,"five-second return retains actual five-second horizon at three-second cadence");
var tradedThree=Enumerable.Range(0,31).Select(i=>ThreeFrame(i*3) with{Trades=i==0?[]:[new ReplayTrade(start.AddSeconds(i*3-.5),i,i==1?95:110)]}).ToArray();
var fifteenOutcome=ExecutableLabels.Build(tradedThree,0,threeConfig,horizonSeconds:15);
Check(fifteenOutcome?.Long.Completed==true&&fifteenOutcome.Long.State.At==start.AddSeconds(18)&&fifteenOutcome.Long.State.CompletedNetPnl==28,"counterfactual flatten starts at fifteen seconds and retains actual execution/fees");
var directionConfig=new RlConfig{FeatureProfile="mini-micro-power",DecisionIntervalSeconds=3,MaxHoldingSeconds=0,DirectionDrivenHolding=true};
var directionFrames=Enumerable.Range(0,41).Select(i=>ThreeFrame(i*3) with{Trades=i==0?[]:[new ReplayTrade(start.AddSeconds(i*3-.5),i,i==1?95:110)]}).ToArray();
var directionEnv=new RlReplayEnvironment(directionFrames,directionConfig);directionEnv.Reset();var heldDirection=directionEnv.Step(1);
for(int i=0;i<22;i++)heldDirection=directionEnv.Step(1);
Check(heldDirection.Position==1&&heldDirection.At>start.AddSeconds(60)&&heldDirection.CompletedTrades==0&&!heldDirection.PendingEntry,"same direction holds beyond sixty seconds without duplicate entry");
var reversedDirection=directionEnv.Step(2);
Check(reversedDirection.Position==0&&reversedDirection.CompletedTrades==1&&reversedDirection.OverrideReason=="REVERSE_SIGNAL"&&!reversedDirection.Mask[1]&&!reversedDirection.Mask[2],"opposite signal only flattens and applies ten-second cooldown");
bool profileMismatch=false;try{ReplayCache.ValidateInterval(evaluationManifest with{DecisionIntervalSeconds=3},directionConfig);}catch(InvalidDataException){profileMismatch=true;}
Check(profileMismatch,"same40width different market profile rejected");
var powerRaw=Path.Combine(fixture,"power.jsonl");var powerEvents=new List<MarketEvent>();
for(int second=0;second<=240;second++)foreach(var product in new[]{"225MC","225M"})
{
    powerEvents.Add(new MarketEvent{Timestamp=start.AddSeconds(second),ProductId=product,Symbol=product+"-TEST",Kind=MarketEventKind.Book,BidPrices=[100,95,90,85,80],AskPrices=[105,110,115,120,125],BidQuantities=[1,1,1,1,1],AskQuantities=[1,1,1,1,1]});
    powerEvents.Add(new MarketEvent{Timestamp=start.AddSeconds(second),ProductId=product,Symbol=product+"-TEST",Kind=MarketEventKind.Trade,LastPrice=105,TradeQuantity=1});
}
File.WriteAllLines(powerRaw,powerEvents.Select(e=>System.Text.Json.JsonSerializer.Serialize(e,ReplayCache.Json)));
var powerManifest=ReplayCache.Build(powerRaw,Path.Combine(fixture,"power.br"),"day",decisionIntervalSeconds:3,featureProfile:"mini-micro-power");
var powerFrames=ReplayCache.Read(Path.Combine(fixture,"power.br"));
Check(powerManifest.Symbols.Length==2&&powerFrames.Any(f=>f.Valid)&&powerFrames.Where(f=>f.At<start.AddSeconds(210)).All(f=>!f.Valid),"power profile warms both shared indicators without a large product");
Check(powerFrames[^1].Features[20]==1&&powerFrames[^1].Features[21]==1&&powerFrames[^1].Features[22]==1&&powerFrames[^1].Features[23]==0,"shared power/short/long/bias features use actual BuySellPower indicators");
var powerGapEvents=powerEvents.Concat(Enumerable.Range(300,241).SelectMany(second=>new[]{"225MC","225M"}.SelectMany(product=>new[]{new MarketEvent{Timestamp=start.AddSeconds(second),ProductId=product,Symbol=product+"-TEST",Kind=MarketEventKind.Book,BidPrices=[100,95,90,85,80],AskPrices=[105,110,115,120,125],BidQuantities=[1,1,1,1,1],AskQuantities=[1,1,1,1,1]},new MarketEvent{Timestamp=start.AddSeconds(second),ProductId=product,Symbol=product+"-TEST",Kind=MarketEventKind.Trade,LastPrice=105,TradeQuantity=1}}))).ToArray();
var gapRaw=Path.Combine(fixture,"power-gap.jsonl");File.WriteAllLines(gapRaw,powerGapEvents.Select(e=>System.Text.Json.JsonSerializer.Serialize(e,ReplayCache.Json)));
ReplayCache.Build(gapRaw,Path.Combine(fixture,"power-gap.br"),"day",decisionIntervalSeconds:3,featureProfile:"mini-micro-power");
Check(ReplayCache.Read(Path.Combine(fixture,"power-gap.br")).Where(f=>f.At>=start.AddSeconds(300)&&f.At<start.AddSeconds(510)).All(f=>!f.Valid),"power indicators restart full warmup after source interruption");
RlConfig RiskConfig(RlConfig original,string mode,decimal points,decimal maximum=20,decimal minimum=5)
{
    var json=System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(original,ReplayCache.Json))!;
    json["stopMode"]=mode;json["fixedStopPoints"]=points;json["maxStopPoints"]=maximum;json["minStopPoints"]=minimum;json["stopVolatilitySeconds"]=60;json["stopVolatilityMultiplier"]=2;
    return json.Deserialize<RlConfig>(ReplayCache.Json)!;
}
decimal StopValue(RlState value,string name)=>System.Text.Json.JsonSerializer.SerializeToElement(value,ReplayCache.Json).GetProperty(name).GetDecimal();
var pointRisk=new RlReplayEnvironment(new[]{Frame(0),Frame(5,100,105,95),Frame(10,85,90,85),Frame(15)},RiskConfig(new RlConfig(),"fixed",10));
pointRisk.Reset();pointRisk.Step(1);var pointStopped=pointRisk.Step(0);
Check(pointStopped.Position==0&&pointStopped.Fills.Single().Price==90&&pointStopped.CompletedNetPnl==-172,"fixed ten POINTS uses two ticks not ten ticks");
var cappedRisk=new RlReplayEnvironment(new[]{Frame(0),Frame(5,100,105,95),Frame(10,75,80,75),Frame(15)},RiskConfig(new RlConfig(),"fixed",50));
cappedRisk.Reset();var cappedHeld=cappedRisk.Step(1);
Check(StopValue(cappedHeld,"activeStopPrice")==80&&StopValue(cappedHeld,"activeStopDistancePoints")==20,"fixed stop capped at twenty points despite fifty-point request");
Check(cappedRisk.Step(0).Fills.Single().Price==80,"maximum twenty point stop executes through shared stop role");
var shortRisk=new RlReplayEnvironment(new[]{Frame(0),Frame(5,100,105,110),Frame(10,120,125,120),Frame(15)},RiskConfig(new RlConfig(),"fixed",10));
shortRisk.Reset();shortRisk.Step(2);var shortStopped=shortRisk.Step(0);
Check(shortStopped.Position==0&&shortStopped.Fills.Single().Price==115&&shortStopped.CompletedNetPnl==-172,"short uses symmetric point stop");
bool badStop=false;try{new RlReplayEnvironment(frames,RiskConfig(new RlConfig(),"adaptive-volatility",10,maximum:4));}catch(ArgumentException){badStop=true;}
Check(badStop,"maximum below one tradable tick rejected");
bool badMode=false;try{new RlReplayEnvironment(frames,RiskConfig(new RlConfig(),"unknown",10));}catch(ArgumentException){badMode=true;}
Check(badMode,"unknown risk mode rejected");
var adaptiveFrames=Enumerable.Range(0,41).Select(i=>ThreeFrame(i*3) with{Trades=i==0?[]:[new ReplayTrade(start.AddSeconds(i*3-.5),i,i==1?95:100)]}).ToArray();
var adaptiveRisk=new RlReplayEnvironment(adaptiveFrames,RiskConfig(directionConfig,"adaptive-volatility",20));adaptiveRisk.Reset();var adaptiveHeld=adaptiveRisk.Step(1);
Check(StopValue(adaptiveHeld,"activeStopDistancePoints")==20,"unwarmed volatility falls back to maximum stop");
for(int i=0;i<23;i++)adaptiveHeld=adaptiveRisk.Step(1);
Check(adaptiveHeld.Position==1&&StopValue(adaptiveHeld,"activeStopPrice")==95&&StopValue(adaptiveHeld,"activeStopDistancePoints")==5,"known sixty-second low volatility tightens without time exit");
var volatileFrames=adaptiveFrames.Select((f,i)=>i>=25?f with{Trades=[new ReplayTrade(f.At.AddSeconds(-.5),i,130)]}:f).ToArray();
var ratchet=new RlReplayEnvironment(volatileFrames,RiskConfig(directionConfig,"adaptive-volatility",20));ratchet.Reset();ratchet.Step(1);RlState ratcheted=null!;
for(int i=0;i<28;i++)ratcheted=ratchet.Step(1);
Check(ratcheted.Position==1&&StopValue(ratcheted,"activeStopPrice")==95&&StopValue(ratcheted,"activeStopDistancePoints")==5&&StopValue(ratcheted,"stopVolatilityPoints")>5,"volatility increase cannot loosen an existing long stop");
var shortAdaptiveFrames=adaptiveFrames.Select((f,i)=>f with{Trades=i==0?[]:[new ReplayTrade(f.At.AddSeconds(-.5),i,i==1?110:i>=25?75:105)]}).ToArray();
var shortAdaptive=new RlReplayEnvironment(shortAdaptiveFrames,RiskConfig(directionConfig,"adaptive-volatility",20));shortAdaptive.Reset();shortAdaptive.Step(2);RlState shortAdaptiveState=null!;
for(int i=0;i<28;i++)shortAdaptiveState=shortAdaptive.Step(2);
Check(shortAdaptiveState.Position==-1&&StopValue(shortAdaptiveState,"activeStopPrice")==110&&StopValue(shortAdaptiveState,"activeStopDistancePoints")==5,"adaptive short stop tightens symmetrically and never loosens");
var offGridCap=new RlReplayEnvironment(new[]{Frame(0),Frame(5,100,105,95),Frame(10)},RiskConfig(new RlConfig(),"fixed",50,maximum:19));offGridCap.Reset();
Check(StopValue(offGridCap.Step(1),"activeStopDistancePoints")==15,"rounding down preserves nineteen-point cap on five-point tick grid");
var staleRisk=new RlReplayEnvironment(new[]{Frame(0),Frame(5,100,105,95),Frame(10,75,80,75) with{Valid=false},Frame(15)},RiskConfig(new RlConfig(),"fixed",20));staleRisk.Reset();staleRisk.Step(1);
Check(staleRisk.Step(0).Fills.Single().Role==PaperOrderRole.StopLoss,"stop works even when new entry health is invalid");
var explicitLegacyCap=new RlReplayEnvironment(new[]{Frame(0),Frame(5,100,105,95),Frame(10)},RiskConfig(new RlConfig(),"legacy-fixed",20));explicitLegacyCap.Reset();
Check(StopValue(explicitLegacyCap.Step(1),"activeStopDistancePoints")==20,"an explicit maximum cannot be ignored even by legacy stop mode");
var riskFile=Path.Combine(fixture,"risk-overlay.json");File.WriteAllText(riskFile,"{\"stopMode\":\"fixed\",\"fixedStopPoints\":15,\"maxStopPoints\":20,\"extraCostTicksPerSide\":2}");
var overlay=EvaluationReplay.ApplyRiskOverrides(directionConfig,riskFile);
Check(overlay.FixedStopPoints==15&&overlay.MaxStopPoints==20&&overlay.ExtraCostTicksPerSide==2&&overlay.FeatureProfile==directionConfig.FeatureProfile&&overlay.DecisionLatencyMs==1&&overlay.CooldownSeconds==10&&overlay.MaxHoldingSeconds==0,"risk overlay preserves model and trading contract");
foreach(var invalidRisk in new[]{"{\"stopMode\":\"fixed\",\"maxStopPoints\":20,\"decisionLatencyMs\":0}","{\"stopMode\":\"fixed\",\"maxStopPoints\":155}","{\"stopMode\":\"fixed\"}","{\"stopMode\":\"fixed\",\"maxStopPoints\":20,\"extraCostTicksPerSide\":0.5}"})
{
    File.WriteAllText(riskFile,invalidRisk);bool denied=false;try{EvaluationReplay.ApplyRiskOverrides(directionConfig,riskFile);}catch(InvalidDataException){denied=true;}
    Check(denied,"risk experiment rejects changed contract/missing or different cap/fractional cost");
}
File.WriteAllText(riskFile,"{\"stopMode\":\"fixed\",\"fixedStopPoints\":150,\"maxStopPoints\":150}");
var wideOverlay=EvaluationReplay.ApplyRiskOverrides(directionConfig,riskFile);
Check(wideOverlay.MaxStopPoints==150&&wideOverlay.FixedStopPoints==150&&wideOverlay.DecisionLatencyMs==1&&wideOverlay.CooldownSeconds==10,"risk overlay accepts one hundred fifty POINT cap without changing trading contract");
var wideStop=new RlReplayEnvironment(new[]{Frame(0,1000,1005),Frame(5,1000,1005,995),Frame(10,845,850,845),Frame(15,845,850)},RiskConfig(new RlConfig(),"fixed",200,maximum:150));wideStop.Reset();var wideHeld=wideStop.Step(1);
Check(StopValue(wideHeld,"activeStopDistancePoints")==150&&StopValue(wideHeld,"activeStopPrice")==850,"two hundred-point request capped at one hundred fifty points or thirty ticks");
var wideClosed=wideStop.Step(0);Check(wideClosed.Fills.Single().Price==850&&wideClosed.CompletedNetPnl==-1572,"wide protective stop uses shared fee/point multiplier");
var researchPath=Path.Combine(fixture,"research.br");
var researchManifest=ReplayCache.Build(powerRaw,researchPath,"day",decisionIntervalSeconds:3,featureProfile:"mini-micro-research");
var researchFrames=ReplayCache.Read(researchPath);
Check(researchManifest.Symbols.Length==2&&researchFrames.All(f=>f.ResearchExtras.Length==20),"research cache only mini/micro and20extras");
var researchConfig=directionConfig with{FeatureProfile="mini-micro-research",ObservationSize=60,StopMode="fixed",FixedStopPoints=150,MaxStopPoints=150};
var researchEnv=new RlReplayEnvironment(researchFrames,researchConfig);var researchState=researchEnv.Reset();
Check(researchState.Observation.Length==60&&researchState.Observation[30]==0&&researchState.Observation[38]==(researchFrames[0].Valid?1:0),"research60 preserves state indices");
bool noResearch=false;try{new RlReplayEnvironment(powerFrames,researchConfig);}catch(ArgumentException){noResearch=true;}
Check(noResearch,"research policy rejects cache without20finiteextras");
var researchLabel=ExecutableLabels.Build(researchFrames,70,researchConfig,15);
Check(researchLabel!=null&&researchLabel.Observation.Length==60,"research executable label60 through common engine");
Console.WriteLine($"NikkeiRl PASS {passed}");
var compactMetrics=new CompactEpisodeMetrics();
var compactEnv=new RlReplayEnvironment(frames,new RlConfig{Commission=36,StopTicks=6,CooldownSeconds=0});
var compactBefore=compactEnv.Reset();
foreach(var compactAction in new[]{1,0,3})
{
    var compactAfter=compactEnv.Step(compactAction);compactMetrics.Observe(compactBefore,compactAfter);compactBefore=compactAfter;
}
compactMetrics.Validate(compactBefore,10);
Check(compactMetrics.Wins==1&&compactMetrics.Losses==0&&compactMetrics.WinningNetJPY==28,"compact report counts authoritative net winning trade");
var compactOpen=new CompactEpisodeMetrics();var compactOpenEnv=new RlReplayEnvironment(new[]{Frame(0),Frame(5,100,105,95),Frame(10),Frame(15)},new RlConfig());
var compactPrevious=compactOpenEnv.Reset();
foreach(var compactAction in new[]{1,0,0})
{
    var compactAfter=compactOpenEnv.Step(compactAction);compactOpen.Observe(compactPrevious,compactAfter);compactPrevious=compactAfter;
}
compactOpen.Validate(compactPrevious,10);
Check(compactOpen.Wins==0&&compactOpen.Losses==0&&compactPrevious.CompletedNetPnl==0&&compactPrevious.OpenPositionCosts==36,"compact report symmetrically excludes unfinished position and entry fee");
Console.WriteLine($"NikkeiRl compact PASS {passed}");

var readinessLegacy=JsonSerializer.Deserialize<RlConfig>("{}",ReplayCache.Json)!;
Check(readinessLegacy.ResearchReadinessProfile=="all-research","missing config readiness retains legacy");
Check(researchManifest.ResearchReadinessProfile=="all-research","legacy cache retains all-research");
ReplayCache.ValidateInterval(researchManifest,researchConfig);
bool RejectedReadiness(CacheManifest manifest,RlConfig config)
{
    try{ReplayCache.ValidateInterval(manifest,config);return false;}catch(InvalidDataException){return true;}
}
var prunedConfig=researchConfig with{ResearchReadinessProfile="exclude-flow"};
var prunedManifest=researchManifest with{Schema="nikkei-rl-cache-pruned-flow-v1",ResearchReadinessProfile="exclude-flow"};
Check(RejectedReadiness(researchManifest,prunedConfig),"new config rejects legacy cache");
Check(RejectedReadiness(prunedManifest,researchConfig),"legacy config rejects new cache");
ReplayCache.ValidateInterval(prunedManifest,prunedConfig);
Check(RejectedReadiness(prunedManifest with{ResearchReadinessProfile="unknown"},prunedConfig),"unknown readiness rejected");
Check(RejectedReadiness(prunedManifest with{FeatureProfile="mini-micro-power"},prunedConfig with{FeatureProfile="mini-micro-power"}),"exclude-flow requires research features");
void ValidateReadiness(string schema,string profile,int[] mask)
{
    using var doc=JsonDocument.Parse(JsonSerializer.Serialize(new{schema,observationSize=60,actorExcludedFeatures=mask,environmentConfig=new{researchReadinessProfile=profile,featureProfile="mini-micro-research",decisionIntervalSeconds=3}}));
    RlPolicyPredictor.ValidateReadinessContract(doc.RootElement);
}
bool RejectReadinessPolicy(string schema,string profile,int[] mask)
{
    try{ValidateReadiness(schema,profile,mask);return false;}catch(ArgumentException){return true;}
}
ValidateReadiness("nikkei-rl-research-pruned-flow-v1","exclude-flow",[48,49,50,51,52,53]);
Check(RejectReadinessPolicy("nikkei-rl-research-pruned-flow-v1","exclude-flow",[48,49,50,51,52]),"partial flow mask rejected");
Check(RejectReadinessPolicy("nikkei-rl-research-pruned-flow-v1","all-research",[48,49,50,51,52,53]),"new schema needs explicit new profile");
Check(RejectReadinessPolicy("nikkei-rl-research-v1","exclude-flow",[48,49,50,51,52,53]),"legacy schema cannot opt into pruned flow");
Check(RejectReadinessPolicy("nikkei-rl-research-pruned-flow-v1","exclude-flow",[48,49,50,51,52,53,53]),"duplicate mask rejected");
Check(RejectReadinessPolicy("nikkei-rl-research-pruned-flow-v1","exclude-flow",[48,49,50,51,52,53,60]),"invalid mask rejected");
Console.WriteLine($"NikkeiRl readiness PASS {passed}");
bool unknownEnvironment=false;
try{new RlReplayEnvironment(researchFrames,researchConfig with{ResearchReadinessProfile="unknown"});}catch(InvalidDataException){unknownEnvironment=true;}
Check(unknownEnvironment,"direct environment rejects unknown readiness");
var sparseSource=Path.Combine(fixture,"sparse-flow.jsonl");
File.WriteAllLines(sparseSource,powerEvents.Where(e=>e.ProductId!="225MC"||e.Kind!=MarketEventKind.Book||(int)(e.Timestamp-start).TotalSeconds%3==1).Select(e=>JsonSerializer.Serialize(e,ReplayCache.Json)));
var sparseLegacyPath=Path.Combine(fixture,"sparse-legacy.br");
var sparsePrunedPath=Path.Combine(fixture,"sparse-pruned.br");
var sparseLegacyManifest=ReplayCache.Build(sparseSource,sparseLegacyPath,"day",decisionIntervalSeconds:3,featureProfile:"mini-micro-research");
var sparsePrunedManifest=ReplayCache.Build(sparseSource,sparsePrunedPath,"day",decisionIntervalSeconds:3,featureProfile:"mini-micro-research",researchReadinessProfile:"exclude-flow");
var sparseLegacy=ReplayCache.Read(sparseLegacyPath);var sparsePruned=ReplayCache.Read(sparsePrunedPath);
Check(sparsePrunedManifest.ResearchReadinessProfile=="exclude-flow"&&sparsePrunedManifest.ValidFrames>sparseLegacyManifest.ValidFrames,"pruned cache actually uses active factor readiness");
Check(sparseLegacy.Length==sparsePruned.Length&&sparseLegacy.Zip(sparsePruned).All(p=>p.First.ResearchExtras.SequenceEqual(p.Second.ResearchExtras)&&p.First.Features.SequenceEqual(p.Second.Features)&&p.First.Trades.SequenceEqual(p.Second.Trades)&&p.First.At==p.Second.At),"mode changes admission only, preserves features and causal trades");
Check(sparsePruned.Where(f=>f.At<start.AddSeconds(210)).All(f=>!f.Valid),"pruned admission preserves shared power warmup");
ReplayCache.ValidateInterval(JsonSerializer.Deserialize<CacheManifest>(File.ReadAllText(sparsePrunedPath+".manifest.json"),ReplayCache.Json)!,prunedConfig);
Console.WriteLine($"NikkeiRl cache readiness PASS {passed}");
Check(sparsePrunedManifest.Schema=="nikkei-rl-cache-pruned-flow-v1","new raw cache must use schema rejected by frozen readers");
bool RejectCacheManifest(CacheManifest manifest,bool allowEvaluation=false)
{
    try{ReplayCache.ValidateManifest(manifest,manifest.CacheSha256,allowEvaluation);return false;}catch(InvalidDataException){return true;}
}
Check(RejectCacheManifest(sparsePrunedManifest with{Schema="nikkei-rl-cache-v2"}),"pruned profile cannot masquerade as legacy cache");
Check(RejectCacheManifest(sparseLegacyManifest with{Schema="nikkei-rl-cache-pruned-flow-v1"}),"new cache schema requires explicit pruned profile");
ReplayCache.ValidateManifest(sparsePrunedManifest,sparsePrunedManifest.CacheSha256);
var isolatedEvaluation=sparsePrunedManifest with{Schema="nikkei-rl-cache-pruned-flow-v1-evaluation",Usage="EvaluationOnly",DataGapFrames=0};
Check(RejectCacheManifest(isolatedEvaluation),"new evaluation schema still needs opt-in");
ReplayCache.ValidateManifest(isolatedEvaluation,isolatedEvaluation.CacheSha256,true);
Check(RejectCacheManifest(isolatedEvaluation with{BadRows=1},true),"new evaluation preserves source quality guard");
ReplayCache.ValidateManifest(sparsePrunedManifest with{Schema="nikkei-rl-cache-pruned-flow-v1-derived",DataGapFrames=0},sparsePrunedManifest.CacheSha256);
Console.WriteLine($"NikkeiRl isolated cache schema PASS {passed}");



