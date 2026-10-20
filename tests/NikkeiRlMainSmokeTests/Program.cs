using KGIOptionPricing.Strategies.NikkeiRlDirection;
using KGIOptionPricing.StrategyFramework;

try {
if(args.Length==2&&args[0]=="--render-model-menu"){ModelMenuTests.Render(args[1]);return;}
if(args.Contains("--model-menu")){ModelMenuTests.Run();return;}
if(args.Contains("--grid15")){Grid15IntegrationTests.Run();return;}
ModelMenuTests.Run();
Grid15IntegrationTests.Run();
if(args.Contains("--performance-output")){SessionLiveTests.Run();return;}
if(args.Contains("--live-latency")){LiveLatencyTests.Run();return;}
LiveLatencyTests.Run();
if(args.Contains("--earlier-contract")){EarlierIntegrationTests.Run();return;}
EarlierIntegrationTests.Run();
SessionLiveTests.Run();
var p=NikkeiRlParameters.Parse(new Dictionary<string,string>());
if(p.ToDictionary()["ModelId"]!="pruned-earlier-seed22")throw new Exception("Wrong model");
if(!StrategySimulationService.DefaultFactories.Any(x=>x.StrategyType==NikkeiRlFactory.TypeName))throw new Exception("Missing simulation factory");
if(!LiveStrategyService.DefaultFactories.Any(x=>x.StrategyType==NikkeiRlFactory.TypeName))throw new Exception("Missing live catalog");
if(LiveStrategyService.TryValidateResearchStrategy(new(){StrategyType=NikkeiRlFactory.TypeName},out _))throw new Exception("Unvalidated live model allowed");
using var actor=new FrozenRlMainPolicy(p);
var prediction=actor.Predict(new double[60],[true,false,false,false,false]);
if(prediction.Action!=0)throw new Exception("Masked action ignored");
Console.WriteLine("PASS frozen policy, defaults, both factories and live gate");

var start=new DateTimeOffset(2026,10,6,8,0,0,TimeSpan.FromHours(8));
var stream=new NikkeiRlFeatureStream(5);
MarketEvent Book(string product,DateTimeOffset at,decimal bid=40000)=>new(){ProductId=product,Symbol=product,Kind=MarketEventKind.Book,Timestamp=at,BidPrices=[bid,bid-5,bid-10,bid-15,bid-20],AskPrices=[bid+5,bid+10,bid+15,bid+20,bid+25],BidQuantities=[10,10,10,10,10],AskQuantities=[10,10,10,10,10]};
MarketEvent Trade(string product,DateTimeOffset at,decimal price)=>new(){ProductId=product,Symbol=product,Kind=MarketEventKind.Trade,Timestamp=at,LastPrice=price,TradeQuantity=1};
for(int s=0;s<240;s++) {stream.Accept(Book("225MC",start.AddSeconds(s)));stream.Accept(Book("225M",start.AddSeconds(s)));stream.Accept(Trade("225MC",start.AddSeconds(s).AddMilliseconds(1),40005));stream.Accept(Trade("225M",start.AddSeconds(s).AddMilliseconds(1),40005));if(s%3==2)stream.Emit(start.AddSeconds(s+1));}
var frame=stream.Emit(start.AddSeconds(240));
if(frame.Market.Length!=30||frame.Research.Length!=20||frame.Bid!=40000)throw new Exception("Feature shape");
if(stream.Accept(Trade("225MC",start.AddSeconds(239),41000)))throw new Exception("Late feature event accepted");
Console.WriteLine("PASS causal features, raw60 layout and late event rejection");

var output=Path.Combine(Path.GetTempPath(),"rl-summary-"+Guid.NewGuid().ToString("N"));
using(var recorder=new KGIOptionPricing.StrategyFramework.Storage.SimulationRunRecorder(output,summaryOnly:true))
{
 recorder.Record(new StrategySnapshot{InstanceId="test",StrategyType=NikkeiRlFactory.TypeName});
 recorder.FinalizeSummary(start);
}
if(Directory.GetFiles(output).Any(x=>x.EndsWith(".jsonl")||x.EndsWith("executions.csv")))throw new Exception("Detailed disk output persisted");
Console.WriteLine("PASS summary-only disk policy");

using(var panel=new KGIOptionPricing.NikkeiRlPanel())
{panel.ConfigureLive(true);if(!panel.StartButton.Enabled)throw new Exception("Live UI disabled");panel.ConfigureLive(false);if(!panel.StartButton.Enabled)throw new Exception("Simulation UI disabled");}
Console.WriteLine("PASS live/simulation UI boundary");

var clock=new TestClock{Now=start};
var ledger=new KGIOptionPricing.StrategyFramework.Performance.PaperPortfolioLedger();
var definition=new StrategyDefinition{StrategyType=NikkeiRlFactory.TypeName,InstanceId="RL-test",Universe=new(){Tradable=new("225MC"),References=[new("225M")]}};
var universe=new ResolvedStrategyUniverse{Tradable=new(){ProductId="225MC",Symbol="225MC",TickSize=5,ContractMultiplier=10},References=[new(){ProductId="225M",Symbol="225M",TickSize=5,ContractMultiplier=100}]};
var forced=new ForcedPolicy();var strategy=new NikkeiRlStrategy(definition,_=>forced);
using var runner=new StrategyInstanceRunner(strategy,universe,ledger,clock,36);
long seq=0;
void Feed(MarketEvent e){clock.Now=e.Timestamp;runner.OnMarketEvent(e with{Sequence=++seq});}
for(int s=0;s<240;s++){Feed(Book("225MC",start.AddSeconds(s)));Feed(Book("225M",start.AddSeconds(s)));Feed(Trade("225MC",start.AddSeconds(s).AddMilliseconds(1),40005));Feed(Trade("225M",start.AddSeconds(s).AddMilliseconds(1),40005));}
var decision=start.AddSeconds(240);forced.Action=1;
Feed(Trade("225MC",decision,39995));
if(ledger.GetPosition("RL-test","225MC").NetQuantity!=0)throw new Exception("Boundary traded before latency");
Feed(Trade("225MC",decision.AddTicks(9999),39995));
if(ledger.GetPosition("RL-test","225MC").NetQuantity!=0)throw new Exception("Sub-ms fill");
Feed(Trade("225MC",decision.AddMilliseconds(1),40000));
if(ledger.GetPosition("RL-test","225MC").NetQuantity!=0)throw new Exception("Touch filled");
Feed(Trade("225MC",decision.AddMilliseconds(2),39995));
if(ledger.GetPosition("RL-test","225MC").NetQuantity!=1)throw new Exception("Strict-cross delayed entry not filled");
Feed(Trade("225MC",decision.AddMilliseconds(1.5),39000));
if(ledger.GetPosition("RL-test","225MC").NetQuantity!=1)throw new Exception("Late trade filled stop");
runner.Pause();
Feed(Trade("225MC",decision.AddMilliseconds(3),39850));
if(ledger.GetPosition("RL-test","225MC").NetQuantity!=1)throw new Exception("Stop touch filled");
Feed(Trade("225MC",decision.AddMilliseconds(4),39845));
if(ledger.GetPosition("RL-test","225MC").NetQuantity!=0)throw new Exception("Paused stop missing");
if(ledger.GetPosition("RL-test","225MC").NetPnl!=-1572)throw new Exception("Shared ledger/cost error");
runner.Resume();forced.Action=0;
Feed(Book("225MC",decision.AddSeconds(1)));Feed(Book("225M",decision.AddSeconds(1)));
Feed(Trade("225MC",decision.AddSeconds(3),40005));
if(forced.LastMask![1])throw new Exception("Actual-exit cooldown ignored");
Console.WriteLine("PASS boundary, 1ms, strict-cross, late stop rejection, paused stop, shared costs and cooldown");

forced.Action=0;
for(int s=244;s<255;s++){Feed(Book("225MC",start.AddSeconds(s)));Feed(Book("225M",start.AddSeconds(s)));Feed(Trade("225MC",start.AddSeconds(s).AddMilliseconds(1),40005));Feed(Trade("225M",start.AddSeconds(s).AddMilliseconds(1),40005));}
forced.Action=1;var again=start.AddSeconds(255);
Feed(Trade("225MC",again,40005));Feed(Trade("225MC",again.AddMilliseconds(1),39995));
if(ledger.GetPosition("RL-test","225MC").NetQuantity!=1)throw new Exception("Cooldown did not expire");
Feed(Trade("225MC",again.AddMilliseconds(2),40500));
var high=ledger.GetPosition("RL-test","225MC").NetPnl;
Feed(Trade("225MC",again.AddMilliseconds(3),40000));
var low=ledger.GetPosition("RL-test","225MC").NetPnl;
Feed(Book("225MC",again.AddSeconds(1)));Feed(Book("225M",again.AddSeconds(1)));Feed(Trade("225M",again.AddSeconds(1),40005));
Feed(Book("225MC",again.AddSeconds(2)));Feed(Book("225M",again.AddSeconds(2)));
Feed(Trade("225MC",again.AddSeconds(3),40000));
if(Math.Abs(forced.Observation![34]-(double)((high-low)/50))>1e-9)throw new Exception("Intraperiod peak omitted from actor drawdown state");
if(ledger.GetPosition("RL-test","225MC").NetQuantity!=1)throw new Exception("Same direction failed to hold");
forced.Action=2;
Feed(Book("225MC",again.AddSeconds(4)));Feed(Book("225M",again.AddSeconds(4)));
Feed(Book("225MC",again.AddSeconds(5)));Feed(Book("225M",again.AddSeconds(5)));Feed(Trade("225M",again.AddSeconds(5),40005));
Feed(Trade("225MC",again.AddSeconds(6),40000));
if(ledger.GetPosition("RL-test","225MC").NetQuantity!=1)throw new Exception("Reverse exited before latency");
Feed(Trade("225MC",again.AddSeconds(6).AddMilliseconds(1),40000));
if(ledger.GetPosition("RL-test","225MC").NetQuantity!=0)throw new Exception("Reverse did not flatten");
Console.WriteLine("PASS intraperiod state, same direction holding and reverse signal");
runner.RequestClose("test close");
if(strategy.State!=StrategyLifecycleState.Stopped||!forced.Disposed)throw new Exception("Flat shutdown did not release actor");
var openOutput=Path.Combine(Path.GetTempPath(),"rl-open-"+Guid.NewGuid().ToString("N"));
using(var recorder=new KGIOptionPricing.StrategyFramework.Storage.SimulationRunRecorder(openOutput,summaryOnly:true))
{recorder.Record(new StrategySnapshot{InstanceId="open",StrategyType=NikkeiRlFactory.TypeName,TradableSymbol="MC",Position=1,RealizedPnl=0,NetPnl=-36});recorder.Record(new ExecutionEvent{StrategyInstanceId="open",Symbol="MC",Side=KGIOptionPricing.OrderManagement.Core.BuySell.Buy,Role=PaperOrderRole.EntryLong,Price=40000,Quantity=1,Commission=36,Timestamp=start});recorder.FinalizeSummary(start);}
using(var summary=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(openOutput,"run-summary.json"))))
{if(summary.RootElement.GetProperty("netPnl").GetDecimal()!=0)throw new Exception("Open entry cost leaked into completed performance");}
Console.WriteLine("PASS shutdown and open-position cost exclusion");

} catch(Exception ex){Console.Error.WriteLine(ex.ToString());Environment.ExitCode=1;}

sealed class TestClock:IEventDrivenStrategyClock {public DateTimeOffset Now {get;set;} public void Advance(DateTimeOffset at)=>Now=at;}
sealed class ForcedPolicy:IRlMainPolicy
{public int Action;public bool Disposed;public bool[]? LastMask;public double[]? Observation;public NikkeiRl.RlPolicyPrediction Predict(double[] obs,bool[] mask){LastMask=mask;Observation=obs;int a=mask[Action]?Action:0;return new(a,new double[5],0);}public void Dispose(){Disposed=true;}}

