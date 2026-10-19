using KGIOptionPricing.Strategies.BuySellPower;
using KGIOptionPricing.Strategies.PowerReversal;
using KGIOptionPricing.StrategyFramework;
using KGIOptionPricing.OrderManagement.Core;

static class Program
{
    static readonly DateTimeOffset T = new(2026,10,6,8,0,0,TimeSpan.FromHours(8));
    static void Require(bool value, string message) { if(!value) throw new Exception(message); }
    static PowerSnapshot S(int direction, decimal power=50, decimal difference=15, int samples=5) =>
        new(110,11,0,power,10,10-difference,difference,true,true,"就緒",
            new(direction>0?"上漲":"下跌",100,direction>0?190:10,T,T,T,false),Array.Empty<MovementSegment>(),100,100,samples,samples);
    [STAThread] static int Main(string[] args)
    {
        try {
            if(args.Length>0 && args[0]=="--market-replay")return MarketReplay.Run(args);
            if(args.Length>0 && args[0]=="--exit-replay-batch")return ExitReplayBatch.Execute(args);
            if(args.Length>0 && args[0]=="--rebuild-recording")return MarketReplay.RebuildRecording(args[1],args[2]);
            if(args.Length>0 && args[0]=="--validate-experiments"){
                var json=new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);json.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
                var files=Directory.EnumerateFiles(args[1],"parameter-experiments.jsonl",SearchOption.AllDirectories).Where(file=>!Path.GetFullPath(file).StartsWith(Path.GetFullPath(args[2])+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)).ToArray();
                using var recorder=new KGIOptionPricing.StrategyFramework.Storage.SimulationRunRecorder(args[2]);
                int validated=0;foreach(string file in files)
                    foreach(string line in File.ReadLines(file)){if(string.IsNullOrWhiteSpace(line))continue;recorder.Record(System.Text.Json.JsonSerializer.Deserialize<ParameterExperimentRecord>(line,json)!);validated++;}
                if(validated==0)throw new InvalidOperationException("No experiment records validated.");
                Console.WriteLine($"PASS {validated} experiments accepted by common recorder validation");return 0;
            }
            IndicatorExitTests.Run();PerformanceSummaryTests.Run();AdaptiveParameterTests.Run();TradingLifecycle(1,"continuous-adaptive-holding");TradingLifecycle(-1,"continuous-adaptive-holding");AdaptiveTests.Run();TradingLifecycle(1,"continuous-adaptive");TradingLifecycle(-1,"continuous-adaptive");
            QuoteEntryTests.Run();
            var p=PowerReversalParameters.Parse(new Dictionary<string,string>());
            bool contradictory=false;try{PowerReversalParameters.Parse(new Dictionary<string,string>{{"RequireAlignedMovement","1"},{"RequireOppositeMovement","1"}});}catch(ArgumentException){contradictory=true;}
            Require(contradictory,"contradictory direction filters are rejected");
            bool invalidRecovery=false;try{PowerReversalParameters.Parse(new Dictionary<string,string>{{"SignalMode","1"},{"PriceRecoveryTicks","2"}});}catch(ArgumentException){invalidRecovery=true;}
            Require(invalidRecovery,"recovery requires an opposing movement segment");
            using(var panel=new KGIOptionPricing.PowerReversalPanel()){
                var displayed=panel.ReadParameters();
                var expected=System.Text.Json.JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText("docs/superpowers/evidence/power-adaptive-params-20261007/selected-parameters.json"))!;
                var actual=displayed.ToDictionary();Require(expected.All(x=>actual.TryGetValue(x.Key,out var value) && value==x.Value),"both centers preserve all49 frozen best-candidate parameters");
                var selectedExit=System.Text.Json.JsonSerializer.Deserialize<Dictionary<string,Dictionary<string,string>>>(File.ReadAllText("docs/superpowers/evidence/power-exits-20261008/profiles.json"))!["trail1"];
                Require(actual.Count==selectedExit.Count+2 && selectedExit.All(x=>actual.TryGetValue(x.Key,out var value) && value==x.Value),"both centers preserve all54 user-selected trail1 parameters");
                Require(actual["EntryExecutionMode"]=="1" && actual["WeightedMidMode"]=="0","both centers enable weighted-mid hybrid entry");
                Require(displayed["PowerExitMode"]==0 && displayed["TrailingExitMode"]==1 && displayed["TrailingActivateRisk"]==1 && displayed["TrailingDistanceRisk"]==0.5m,"new instances use 1R activation and 0.5R retracement");
                Require(!panel.Confirm.Checked && panel.Loss.Value==3000,"new profile does not grant live authority or change daily loss cap");
                var definition=panel.CreateDefinition();Require(expected.All(x=>definition.Parameters[x.Key]==x.Value),"live/simulation start definition carries frozen profile unchanged");
                var candidate=new PowerReversalFactory().Create(definition);KGIOptionPricing.StrategyFramework.StrategyLiveCapabilityGate.EnsureLiveCapable(candidate);
                Require(panel.Status.Text.Contains("研究候選"),"default candidate research status visible before start");
                Require(displayed["ReversalPoints"]==30 && displayed.MinRewardRisk==1.5m && displayed["MaxDataAgeSeconds"]==2,"research profile preserves risk/data guards");
            }
            SynchronizationContext.SetSynchronizationContext(null); // No message loop in the replay smoke harness.
            var consensus=new ReversalSignalGate(PowerReversalParameters.Parse(new Dictionary<string,string>{{"SignalMode","1"},{"ConfirmSeconds","2"},{"MinJointPower","40"},{"MaxPowerDifference","20"},{"MinShortAveragePower","5"}}));
            Require(consensus.Update(T,S(1,30),S(1,30),true)==0,"weak joint power starts blocked");
            Require(consensus.Update(T.AddSeconds(2),S(1,30),S(1,30),true)==0,"weak joint power cannot arm after confirmation");
            Require(consensus.Update(T.AddSeconds(3),S(1,30),S(1,70),true)==0,"divergent products cannot begin confirmation");
            Require(consensus.Update(T.AddSeconds(5),S(1,30),S(1,70),true)==0,"divergence cannot arm after confirmation");
            Require(consensus.Update(T.AddSeconds(6),S(1),S(1) with{ShortAverage=4},true)==0,"insufficient sustained micro power blocks");
            Require(consensus.Update(T.AddSeconds(8),S(1),S(1) with{ShortAverage=4},true)==0,"insufficient short average cannot arm");
            Require(consensus.Update(T.AddSeconds(9),S(1,40),S(1,60),true)==0,"joint power and difference boundary starts confirmation");
            Require(consensus.Update(T.AddSeconds(11),S(1,40),S(1,60),true)==1,"inclusive consensus boundaries can arm long");
            Require(consensus.Update(T.AddSeconds(12),S(1,40),S(1,61),true)==0,"divergence resets confirmation immediately");
            var consensusSell=S(-1,-50,-15) with{ShortAverage=-10,LongAverage=5};
            Require(consensus.Update(T.AddSeconds(13),consensusSell,consensusSell,true)==0,"short consensus starts fresh confirmation");
            Require(consensus.Update(T.AddSeconds(15),consensusSell,consensusSell,true)==-1,"sign normalized consensus can arm short");
            Require(consensus.Update(T.AddSeconds(16),S(1) with{ShortAverage=4},S(1),true)==0,"mini sustained power also must pass");
            Require(consensus.Update(T.AddSeconds(18),S(1) with{BuyPower=null},S(1),true)==0,"missing mini power cannot pass consensus");
            Require(consensus.Update(T.AddSeconds(20),S(1),S(1) with{ShortAverage=null},true)==0,"missing short average cannot pass enabled filter");
            var continuous=new ReversalSignalGate(PowerReversalParameters.Parse(new Dictionary<string,string>{{"SignalMode","1"},{"ConfirmSeconds","2"}}));
            var recovery=new ReversalSignalGate(PowerReversalParameters.Parse(new Dictionary<string,string>{{"SignalMode","1"},{"RequireOppositeMovement","1"},{"PriceRecoveryTicks","2"},{"ConfirmSeconds","2"}}));
            Require(recovery.Update(T,S(-1),S(-1),true,10,5)==0,"price recovery starts blocked at falling extreme");
            Require(recovery.Update(T.AddSeconds(2),S(-1),S(-1),true,10,5)==0,"power alone cannot arm without price recovery");
            Require(recovery.Update(T.AddSeconds(3),S(-1),S(-1),true,20,5)==0,"two-tick rebound begins fresh confirmation");
            Require(recovery.Update(T.AddSeconds(5),S(-1),S(-1),true,20,5)==1,"confirmed price rebound can arm long");
            Require(recovery.Update(T.AddSeconds(6),S(-1),S(-1),true,null,5)==0,"missing valid price breaks recovery confirmation");
            var recoverySell=S(1,-50,-15) with {ShortAverage=-10,LongAverage=5};
            Require(recovery.Update(T.AddSeconds(7),recoverySell,recoverySell,true,180,5)==0,"two-tick pullback begins sell confirmation");
            Require(recovery.Update(T.AddSeconds(9),recoverySell,recoverySell,true,180,5)==-1,"confirmed pullback can arm short");
            var aligned=new ReversalSignalGate(PowerReversalParameters.Parse(new Dictionary<string,string>{{"SignalMode","1"},{"RequireAlignedMovement","1"},{"ConfirmSeconds","2"}}));
            Require(aligned.Update(T,S(-1),S(-1),true)==0,"aligned movement begins blocked against price trend");
            Require(aligned.Update(T.AddSeconds(2),S(-1),S(-1),true)==0,"aligned movement must not arm against price trend");
            Require(aligned.Update(T.AddSeconds(3),S(1),S(1),true)==0,"aligned movement starts fresh confirmation");
            Require(aligned.Update(T.AddSeconds(5),S(1),S(1),true)==1,"aligned buy trend can arm");
            Require(aligned.Update(T.AddSeconds(6),S(1),S(1) with {CurrentMovement=null},true)==0,"unknown movement cannot pass aligned filter");
            var alignedSell=S(-1,-50,-15) with {ShortAverage=-10,LongAverage=5};
            Require(aligned.Update(T.AddSeconds(7),alignedSell,alignedSell,true)==0,"aligned sell begins fresh confirmation");
            Require(aligned.Update(T.AddSeconds(9),alignedSell,alignedSell,true)==-1,"aligned sell trend can arm");
            var opposite=new ReversalSignalGate(PowerReversalParameters.Parse(new Dictionary<string,string>{{"SignalMode","1"},{"RequireOppositeMovement","1"},{"ConfirmSeconds","2"}}));
            Require(opposite.Update(T,S(1),S(1),true)==0,"opposite movement filter blocks same direction power");
            Require(opposite.Update(T.AddSeconds(2),S(1),S(1),true)==0,"same direction cannot arm after confirmation");
            Require(opposite.Update(T.AddSeconds(3),S(-1),S(-1),true)==0,"opposite movement begins new confirmation");
            Require(opposite.Update(T.AddSeconds(5),S(-1),S(-1),true)==1,"buy power against falling movement can arm");
            Require(opposite.Update(T.AddSeconds(6),S(1),S(1),true)==0,"direction change resets opposite movement confirmation");
            Require(opposite.Update(T.AddSeconds(7),S(-1),S(-1),true)==0,"cannot reuse old confirmation after direction change");
            var sell=S(1,-50,-15) with{ShortAverage=-10,LongAverage=5};
            Require(opposite.Update(T.AddSeconds(8),sell,sell,true)==0,"sell against rising movement starts confirmation");
            Require(opposite.Update(T.AddSeconds(10),sell,sell,true)==-1,"sell power against rising movement can arm");
            Require(opposite.Update(T.AddSeconds(11),sell,sell with{CurrentMovement=null},true)==0,"missing movement cannot pass enabled filter");
            var noMovement=S(1) with {CurrentMovement=null,UpSamples=0,DownSamples=0};
            Require(continuous.Update(T,noMovement,noMovement,true)==0,"continuous power still requires confirmation");
            Require(continuous.Update(T.AddSeconds(2),noMovement,noMovement,true)==1,"continuous mode enters on sustained power without completed movement samples");
            Require(continuous.Update(T.AddSeconds(3),noMovement,noMovement,false)==0,"continuous mode retains health gate");
            var earlyGate=new ReversalSignalGate(PowerReversalParameters.Parse(new Dictionary<string,string>{{"RequireShortAverageDirection","0"}}));
            var early=S(-1) with{ShortAverage=-5,LongAverage=-20};
            Require(earlyGate.PowerMatches(early,1),"improving negative short average may confirm early reversal when direction check disabled");
            Require(!new ReversalSignalGate(p).PowerMatches(early,1),"legacy short average direction filter preserved");
            bool costBlocked=false;try{PowerReversalParameters.EnsureCommission(new(){StrategyType="PowerReversal"},0);}catch(ArgumentException){costBlocked=true;}Require(costBlocked,"cost mismatch blocks");
            Require(p.Power.ReversalPoints==30 && p.ConfirmSeconds==5 && p.MinRewardRisk==1.5m,"defaults");
            var gate=new ReversalSignalGate(p);
            Require(gate.Update(T,S(-1),S(-1),true)==0,"must sustain");
            Require(gate.Update(T.AddSeconds(4),S(-1),S(-1),true)==0,"not yet five");
            Require(gate.Update(T.AddSeconds(5),S(-1),S(-1),true)==1,"armed long");
            Require(gate.Update(T.AddSeconds(6),S(-1),S(-1),false)==0,"stale breaks continuity");
            Require(gate.Update(T.AddSeconds(7),S(-1),S(-1),true)==0,"fresh starts over");
            Require(gate.Update(T.AddSeconds(12),S(-1),S(-1),true)==1,"rearmed");
            gate.Reset();
            var shortSnapshot=S(1,-50,-15) with { ShortAverage=-10,LongAverage=5 };
            Require(gate.Update(T,shortSnapshot,shortSnapshot,true)==0,"short waits");
            Require(gate.Update(T.AddSeconds(5),shortSnapshot,shortSnapshot,true)==-1,"armed short");
            gate.Reset();
            Require(gate.Update(T,S(-1),S(-1) with {DownSamples=4},true)==0,"sample gate");
            Require(gate.Update(T.AddSeconds(6),S(-1),S(-1) with {DownSamples=4},true)==0,"never armed insufficient");
            Require(gate.Update(T.AddSeconds(7),S(-1),S(-1) with {IsWarm=false},true)==0,"warmup gate");
            TradingLifecycle(1,"stop");TradingLifecycle(-1,"stop");TradingLifecycle(1,"timeout");TradingLifecycle(1,"time");TradingLifecycle(1,"target");
            TradingLifecycle(1,"quote-gap");
            TradingLifecycle(1,"unarmed-fast");
            TradingLifecycle(1,"pullback");TradingLifecycle(-1,"pullback");
            TradingLifecycle(1,"continuous");TradingLifecycle(-1,"continuous");
            TradingLifecycle(1,"continuous-risk");
            TradingLifecycle(1,"continuous-filter");
            TradingLifecycle(1,"continuous-aligned");
            TradingLifecycle(1,"continuous-recovery");
            TradingLifecycle(1,"continuous-consensus");
            TradingLifecycle(1,"continuous-trail");TradingLifecycle(-1,"continuous-trail");
            IntegrationTests.Run();
            Console.WriteLine("PowerReversal signal and lifecycle PASS"); return 0;
        } catch(Exception ex) {Console.Error.WriteLine(ex);return 1;}
    }
    sealed class View : IStrategyPerformanceView { public PositionSnapshot Position=new(); public PositionSnapshot GetPosition(string id,string symbol)=>Position; }
    static void TradingLifecycle(int direction,string mode)
    {
        var values=new Dictionary<string,string> { ["PowerWindowSeconds"]="10",["ShortAverageSeconds"]="1",["LongAverageSeconds"]="1",
            ["PowerThreshold"]="0",["BaisThreshold"]="0",["ConfirmSeconds"]="1",["MovementRatio"]="0",["MinMovementSamples"]="1",
            ["MinQuantity"]="1",["MinTradeCount"]="1",["MaxDataAgeSeconds"]="5",["MaxSkewSeconds"]="5",["CommissionPerSide"]="0",["SlippageTicks"]="0" };
        var d=new StrategyDefinition {StrategyType=PowerReversalFactory.TypeName,InstanceId="test",Parameters=values};
        if(mode=="quote-gap")values["PowerThreshold"]="80";
        if(mode=="unarmed-fast")values["MinQuantity"]="10000";
        if(mode=="pullback"){values["CommissionPerSide"]="36";values["SlippageTicks"]="4";values["AllowRiskRewardPullback"]="1";}
        if(mode.StartsWith("continuous")){values["SignalMode"]="1";values["CooldownSeconds"]="60";values["StopTicks"]="6";values["TargetTicks"]="16";}
        if(mode.StartsWith("continuous-adaptive"))values["AdaptiveExitMode"]="1";
        if(mode=="continuous-adaptive-holding")values["AdaptiveHoldingMode"]="1";
        if(mode=="continuous-risk"){values["TargetTicks"]="10";values["CommissionPerSide"]="36";values["SlippageTicks"]="2";}
        if(mode=="continuous-filter")values["RequireOppositeMovement"]="1";
        if(mode=="continuous-aligned")values["RequireAlignedMovement"]="1";
        if(mode=="continuous-recovery"){values["RequireOppositeMovement"]="1";values["PriceRecoveryTicks"]="2";}
        if(mode=="continuous-consensus"){values["MinJointPower"]="90";values["MaxPowerDifference"]="5";values["MinShortAveragePower"]="5";}
        if(mode=="continuous-trail")values["TrailingExitMode"]="1";
        var universe=new ResolvedStrategyUniverse {Tradable=new(){ProductId="225MC",Symbol="MC",TickSize=5,ContractMultiplier=10},References=new[]{
            new ResolvedProduct{ProductId="225M",Symbol="M",TickSize=5,ContractMultiplier=100},new ResolvedProduct{ProductId="225",Symbol="L",TickSize=10,ContractMultiplier=1000}}};
        var view=new View(); var strategy=new PowerReversalStrategy(d);var orders=new List<PaperOrderRequest>();
        var decisions=new List<StrategyDecisionObservation>();strategy.DecisionObserved+=decisions.Add;
        strategy.OrderRequested+=o=>{orders.Add(o);strategy.OnOrderSubmitted(o);};var cancels=new List<string>();strategy.CancelOrderRequested+=cancels.Add;
        strategy.Start(new(universe,new SystemStrategyClock(),view));
        long sequence=0;
        void Send(int sec,decimal price,int qty=11) {foreach(var product in universe.AllProducts) {
            bool sell=mode=="quote-gap" && product.ProductId=="225MC" && sec==20 || mode=="continuous-consensus" && product.ProductId=="225M" && sec<=12;
            int quantity=mode=="quote-gap" && product.ProductId=="225MC" && sec==12?5001:sell?101:qty;
            strategy.OnMarketEvent(new(){Sequence=++sequence,Timestamp=T.AddSeconds(sec),Symbol=product.Symbol,ProductId=product.ProductId,Kind=MarketEventKind.Trade,LastPrice=price,TradeQuantity=quantity,BidPrices=new[]{sell?price:direction>0?price-5:price},AskPrices=new[]{sell?price+5:direction>0?price:price+5}});}}
        for(int sec=0;sec<=12;sec++)Send(sec,direction>0?100:300);
        if(mode.StartsWith("continuous-adaptive")){
            Require(orders.Count==0,"adaptive sample gate must block warm power without completed price segments");
            decimal origin=direction>0?100:300;
            Send(13,origin+direction*100);Send(14,origin);Send(15,origin+direction*100);Send(16,origin+direction*100);
            Require(orders.Count==1,"completed up and down segments enable adaptive entry");
            var first=orders[0];view.Position=new(){NetQuantity=direction,AveragePrice=first.Price};
            strategy.OnOrderLifecycle(new(){StrategyInstanceId="test",ClientOrderId=first.ClientOrderId,CycleId=first.CycleId,StrategyRunGeneration=1,State=OrderLifecycleState.Filled});
            decimal adaptiveStop=first.Price-direction*50,target=first.Price+direction*100;
            strategy.OnExecution(new(){StrategyInstanceId="test",ClientOrderId=first.ClientOrderId,CycleId=first.CycleId,StrategyRunGeneration=1,Role=first.Role,Quantity=1,Price=first.Price,Timestamp=T.AddSeconds(16),Symbol="MC"});
            Send(17,origin+direction*130);Send(18,origin+direction*90);
            Require(strategy.GetSnapshot().StopPrice==adaptiveStop && strategy.GetSnapshot().Features["TargetTrigger"]==target,$"new movement averages cannot loosen filled entry plan: expected {adaptiveStop}/{target}, actual {strategy.GetSnapshot().StopPrice}/{strategy.GetSnapshot().Features["TargetTrigger"]}, intent {first.Price}");
            if(mode=="continuous-adaptive-holding"){
                Require(strategy.GetSnapshot().Features["EffectiveHoldingSeconds"]==60,"completed duration is frozen on entry fill");
                Send(75,first.Price+direction*90);
                Require(orders.Count==1 && strategy.GetPulseTimes(T.AddSeconds(75),T.AddSeconds(77)).Contains(T.AddSeconds(76)),"adaptive holding schedules the actual frozen deadline");
                strategy.OnHeartbeat(T.AddSeconds(76));Require(orders.Count==2 && orders[1].Role==PaperOrderRole.Flatten,"adaptive holding deadline exits through common Flatten role");
                strategy.Stop("test complete");Console.WriteLine("PASS adaptive holding frozen timer and flatten "+direction);return;
            }
            strategy.Pause("test paused protection");Send(19,adaptiveStop-direction*5);
            Require(orders.Count==2 && orders[1].Role==PaperOrderRole.StopLoss,"adaptive frozen stop remains protective while paused");
            strategy.Stop("test complete");Console.WriteLine("PASS adaptive sample gate, frozen entry plan and paused stop "+direction);return;
        }
        if(mode=="continuous-consensus"){
            Require(orders.Count==0,"cross-product disagreement blocks entry");
            Require(decisions.Any(x=>x.Thresholds.Any(t=>t.Name=="PowerDifference" && !t.Passed)),"cross-product failure is observable");
            for(int sec=13;sec<=25;sec++)Send(sec,100);
            Require(orders.Count==1,"valid sustained consensus permits one entry");
            Require(decisions.Any(x=>x.Decision==StrategyDecision.Accept && x.Thresholds.Any(t=>t.Name=="JointBuyPower" && t.Passed) && x.Thresholds.Any(t=>t.Name=="225M.SustainedPower" && t.Passed)),"accepted entry records cross-product values");
            strategy.Stop("test complete");Console.WriteLine("PASS consensus integration and recorded thresholds");return;
        }
        if(mode=="continuous-aligned"){
            Require(orders.Count==0,"aligned movement blocks uninitialized direction");
            Send(13,70);Send(14,65);Require(orders.Count==0,"positive power cannot enter falling price segment");
            Send(15,100);Send(16,105);
            Require(orders.Count==1 && orders[0].Role==PaperOrderRole.EntryLong,"price trend and sustained buy power enter together");
            Require(decisions.Any(x=>x.Decision==StrategyDecision.Accept && x.Thresholds.Any(t=>t.Name=="MovementAlignedPower" && t.Passed)),"aligned entry exposes its price direction threshold");
            strategy.Stop("test complete");Console.WriteLine("PASS aligned movement entry and recorded threshold");return;
        }
        if(mode=="continuous-recovery"){
            Require(orders.Count==0,"recovery blocks unknown direction");
            Send(13,70);Send(14,70);Send(15,75);Send(16,85,10);Send(17,85,10);
            Require(orders.Count==0,"ignored quantity cannot establish price recovery");
            Require(decisions.Any(x=>x.Timestamp==T.AddSeconds(17) && x.Thresholds.Any(t=>t.Name=="PriceRecoveryTicks" && t.ObservedValue==1 && !t.Passed)),"recovery uses last valid trade rather than quoted or ignored price");
            Send(18,80);Send(19,80);Require(orders.Count==1,"valid rebound plus confirmation enters");
            Require(decisions.Any(x=>x.Decision==StrategyDecision.Accept && x.Thresholds.Any(t=>t.Name=="PriceRecoveryTicks" && t.ObservedValue==2 && t.Passed)),"accepted recovery records its observed threshold");
            strategy.Stop("test complete");Console.WriteLine("PASS valid-trade price recovery and recorded threshold");return;
        }
        if(mode=="continuous-filter"){
            Require(orders.Count==0,"no initialized movement means no filtered entry");Send(13,200);Send(14,200);
            Require(orders.Count==0,"filter blocks buying in rising segment");Send(15,170);Send(16,165);
            Require(orders.Count==1 && orders[0].Role==PaperOrderRole.EntryLong,"falling segment plus sustained buy power enters");
            Require(decisions.Any(x=>x.Decision==StrategyDecision.Accept && x.Thresholds.Any(t=>t.Name=="MovementOpposesPower" && t.Passed)),"accepted entry exposes directional filter");
            strategy.Stop("test complete");Console.WriteLine("PASS opposite movement entry and recorded threshold");return;
        }
        if(mode.StartsWith("continuous-adaptive"))values["AdaptiveExitMode"]="1";
        if(mode=="continuous-adaptive-holding")values["AdaptiveHoldingMode"]="1";
        if(mode=="continuous-risk"){
            Require(orders.Count==0 && decisions.Any(x=>x.PrimaryReasonCode=="INSUFFICIENT_ROOM" && x.Thresholds.Any(t=>t.Name=="NetRewardRisk" && !t.Passed)),"continuous mode rejects reward/risk below 1.5 after real cost budget");
            strategy.Stop("test complete");Console.WriteLine("PASS continuous cost guard");return;
        }
        if(mode=="continuous-trail"){
            Require(orders.Count==1,"trail mode still enters one market order");
            var first=orders[0];view.Position=new(){NetQuantity=direction,AveragePrice=first.Price};
            strategy.OnOrderLifecycle(new(){StrategyInstanceId="test",ClientOrderId=first.ClientOrderId,CycleId=first.CycleId,StrategyRunGeneration=1,State=OrderLifecycleState.Filled});
            strategy.OnExecution(new(){StrategyInstanceId="test",ClientOrderId=first.ClientOrderId,CycleId=first.CycleId,StrategyRunGeneration=1,Role=first.Role,Quantity=1,Price=first.Price,Timestamp=T.AddSeconds(12),Symbol="MC"});
            strategy.Pause("verify protection while paused");
            Send(13,first.Price+direction*40);Require(orders.Count==1,"trail activation does not submit exit");
            Send(14,first.Price+direction*20);
            Require(orders.Count==2 && orders[1].Role==PaperOrderRole.Flatten && orders[1].Type==PaperOrderType.Market,"paused trail submits one market flatten");
            Send(15,first.Price+direction*10);Require(orders.Count==2,"pending exit prevents duplicate order");
            Require(decisions.Any(x=>x.PrimaryReasonCode=="TRAIL_EXIT"),"trail exit reason observable");
            Require(strategy.GetSnapshot().StopPrice==first.Price-direction*30,"hard stop remains frozen");
            strategy.Stop("test complete");Console.WriteLine("PASS paused trailing market exit and duplicate protection "+direction);return;
        }
        if(mode=="continuous"){
            Require(orders.Count==1 && orders[0].Type==PaperOrderType.Market && orders[0].Quantity==1,"continuous power enters one market order without price reversal");
            var first=orders[0];view.Position=new(){NetQuantity=direction,AveragePrice=first.Price};
            strategy.OnOrderLifecycle(new(){StrategyInstanceId="test",ClientOrderId=first.ClientOrderId,CycleId=first.CycleId,StrategyRunGeneration=1,State=OrderLifecycleState.Filled});
            strategy.OnExecution(new(){StrategyInstanceId="test",ClientOrderId=first.ClientOrderId,CycleId=first.CycleId,StrategyRunGeneration=1,Role=first.Role,Quantity=1,Price=first.Price,Timestamp=T.AddSeconds(12),Symbol="MC"});
            Require(strategy.GetSnapshot().StopPrice==first.Price-direction*30,"continuous fixed stop uses resolved tick");
            Send(13,first.Price+direction*90);Require(orders.Count==2 && orders[1].Role==PaperOrderRole.TakeProfit,"continuous target protects position");
            var closing=orders[1];view.Position=new();strategy.OnOrderLifecycle(new(){StrategyInstanceId="test",ClientOrderId=closing.ClientOrderId,CycleId=closing.CycleId,StrategyRunGeneration=1,State=OrderLifecycleState.Filled});
            strategy.OnExecution(new(){StrategyInstanceId="test",ClientOrderId=closing.ClientOrderId,CycleId=closing.CycleId,StrategyRunGeneration=1,Role=closing.Role,Quantity=1,Price=first.Price+direction*90,Timestamp=T.AddSeconds(14),Symbol="MC"});
            for(int sec=15;sec<=73;sec++)Send(sec,first.Price+direction*90);
            Require(orders.Count==2,"cooldown prevents repeated entry on unchanged sustained signal");
            Send(74,first.Price+direction*90,10);Send(75,first.Price+direction*90,20);
            strategy.OnHeartbeat(T.AddSeconds(75.01));Require(orders.Count==2,"ignored quantities and heartbeat cannot enter");
            Send(76,first.Price+direction*90);Send(77,first.Price+direction*90);
            Require(orders.Count==3,"continuous power requires fresh confirmation after cooldown");
            strategy.Stop("test complete");Console.WriteLine("PASS continuous entry, target and cooldown "+direction);return;
        }
        decimal[] path={100,200,170,100,130,200,170,165,160,190};
        for(int i=0;i<path.Length;i++) {
            if(mode=="unarmed-fast" && i==9){
                strategy.OnMarketEvent(new(){Symbol="MC",Sequence=++sequence,Timestamp=T.AddSeconds(21.01),Kind=MarketEventKind.Trade,LastPrice=190,TradeQuantity=11,BidPrices=new[]{185m},AskPrices=new[]{190m}});
                Require(orders.Count==0,"unarmed reversal cannot enter");
                Require(decisions.Any(x=>x.PrimaryReasonCode=="REVERSAL_UNARMED" && x.Timestamp==T.AddSeconds(21.01)),"critical rejected reversal must survive UI throttle");
                strategy.Stop("test complete");Console.WriteLine("PASS critical unarmed reversal recorded");return;
            }
            if(mode=="quote-gap" && i==9){
                foreach(string id in new[]{"M","MC"})strategy.OnMarketEvent(new(){Symbol=id,Sequence=++sequence,Timestamp=T.AddSeconds(22.25),Kind=MarketEventKind.Book,BidPrices=new[]{155m},AskPrices=new[]{160m}});
                strategy.OnMarketEvent(new(){Symbol="MC",Sequence=++sequence,Timestamp=T.AddSeconds(22.5),Kind=MarketEventKind.Trade,LastPrice=190,TradeQuantity=10001,BidPrices=new[]{185m},AskPrices=new[]{190m}});
                Require(orders.Count==0,"micro power faded on quote; restored reversal cannot reuse old arm");strategy.Stop("test complete");Console.WriteLine("PASS quote expiry breaks micro continuity");return;
            }
            Send(13+i,direction>0?path[i]:400-path[i]);
        }
        Require(orders.Count==1 && orders[0].Role==(direction>0?PaperOrderRole.EntryLong:PaperOrderRole.EntryShort),"reversal intent exactly once "+direction);
        if(mode=="pullback"){
            var pullback=orders[0];Require(pullback.Price==(direction>0?185:215),"cost aware pullback waits for better price");
            strategy.Stop("pullback verification complete");strategy.OnOrderLifecycle(new(){StrategyInstanceId="test",ClientOrderId=pullback.ClientOrderId,CycleId=pullback.CycleId,StrategyRunGeneration=1,State=OrderLifecycleState.Cancelled});
            Require(strategy.State==StrategyLifecycleState.Stopped,"unfilled pullback cancels safely");Console.WriteLine("PASS pullback "+direction);return;
        }
        Require(decisions.Any(x=>x.Decision==StrategyDecision.Accept && x.Direction==(direction>0?"Long":"Short")),"accepted decision retains direction");
        var entry=orders[0]; Require(entry.Price%5==0 && entry.Quantity==1 && entry.ClientOrderId.Length>0,"aligned managed one lot");
        Send(23,direction>0?190:210);Require(orders.Count==1,"no duplicate while entry working");
        if(mode=="timeout") {strategy.OnHeartbeat(T.AddSeconds(33));Require(cancels.Count==1,"timeout cancel once");strategy.OnHeartbeat(T.AddSeconds(34));Require(cancels.Count==1,"wait cancel acknowledgement");}
        view.Position=new(){NetQuantity=direction,AveragePrice=entry.Price};
        strategy.OnOrderLifecycle(new(){StrategyInstanceId="test",ClientOrderId=entry.ClientOrderId,CycleId=entry.CycleId,StrategyRunGeneration=1,State=OrderLifecycleState.Filled});
        var fill=new ExecutionEvent{StrategyInstanceId="test",ClientOrderId=entry.ClientOrderId,CycleId=entry.CycleId,StrategyRunGeneration=1,Role=entry.Role,Price=entry.Price,Quantity=1,Timestamp=T.AddSeconds(mode=="timeout"?34:23),Symbol="MC"};strategy.OnExecution(fill);
        decimal stop=strategy.GetSnapshot().StopPrice;Require(stop==(direction>0?155:245),"fixed extreme outside one tick");
        if(mode=="stop"){strategy.Pause("test pause");Send(24,stop-direction*5);Require(orders.Count==2 && orders[1].Role==PaperOrderRole.StopLoss,"paused protective exit");Send(25,stop-direction*10);}
        if(mode=="time"){strategy.OnHeartbeat(T.AddSeconds(204));Require(orders.Count==2 && orders[1].Role==PaperOrderRole.Flatten,"time exit is Flatten");}
        if(mode=="target"){Send(24,265);Require(orders.Count==2 && orders[1].Role==PaperOrderRole.TakeProfit,"target exit");}
        if(mode=="timeout")Require(orders.Count==2 && orders[1].Role==PaperOrderRole.Flatten,"late fill closes");
        strategy.OnExecution(fill);Require(orders.Count==2,"duplicate fill has no second exit");
        view.Position=new();var exit=orders[1];strategy.OnOrderLifecycle(new(){StrategyInstanceId="test",ClientOrderId=exit.ClientOrderId,CycleId=exit.CycleId,StrategyRunGeneration=1,State=OrderLifecycleState.Filled});strategy.OnExecution(new(){StrategyInstanceId="test",ClientOrderId=exit.ClientOrderId,CycleId=exit.CycleId,StrategyRunGeneration=1,Role=exit.Role,Price=stop-5,Quantity=1,Timestamp=T.AddSeconds(25),Symbol="MC"});
        strategy.Stop("test complete");Require(strategy.State==StrategyLifecycleState.Stopped,"safe close flat");
        Console.WriteLine($"PASS {direction} {mode}");
        var failed=new PowerReversalStrategy(d);failed.Start(new(universe,new SystemStrategyClock(),new View()));
        failed.NotifyRecordingFailure(new IOException("injected save error"));Require(!failed.CanResume,"flat recording failure prevents resume");
        bool denied=false;try{failed.Resume();}catch(InvalidOperationException){denied=true;}Require(denied,"resume cannot bypass persistent record error");failed.Stop("fault verification complete");
    }
}

