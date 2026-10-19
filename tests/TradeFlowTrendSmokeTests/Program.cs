using TradeFlowTrendSmokeTests;
var suites = new Dictionary<string, Action> { ["parameters"] = ParameterTests.Run, ["market"] = MarketDataTests.Run, ["windows"] = WindowTests.Run, ["indicators"] = IndicatorTests.Run, ["sessions"] = SessionTests.Run };
suites["execution"]=ExecutionTests.Run;
suites["strategy"]=StrategyTests.Run;
suites["observations"]=ObservationTests.Run;
suites["performance"]=PerformanceTests.Run;
suites["ui"]=UiTests.Run;
suites["end-to-end"]=EndToEndTests.Run;
suites["review"]=ReviewRegressionTests.Run;
if(args.Contains("real-replay"))suites["real-replay"]=RealReplayTests.Run;
try {
foreach (var (name, run) in suites.Where(x => args.Length == 0 || args.Contains(x.Key)))
{ run(); Console.WriteLine($"PASS {name}"); }
} catch(Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode=1; }
if (args.Any(a => !suites.ContainsKey(a))) throw new ArgumentException("Unknown suite");
