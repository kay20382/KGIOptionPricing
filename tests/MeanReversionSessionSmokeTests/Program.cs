using KGIOptionPricing.Strategies.MeanReversionScalper;
using KGIOptionPricing.StrategyFramework;
using KGIOptionPricing.StrategyFramework.Storage;
using System.Text.Json;
using System.Text.Json.Serialization;

if (args.Length != 1 || args[0] is not ("schedule" or "prewarm" or "closing" or "ui" or "replay" or "paused-heartbeat" or "direction" or "pair-pricing" or "ma-sampling" or "dual-resting"))
    throw new ArgumentException("Usage: MeanReversionSessionSmokeTests schedule|prewarm|closing|ui|replay|paused-heartbeat|direction|pair-pricing|ma-sampling");
if (args[0] == "pair-pricing")
{
    TestPairPricing();
    return;
}
if (args[0] == "dual-resting")
{
    TestDualResting();
    return;
}
if (args[0] == "ma-sampling")
{
    TestMovingAverageSampling();
    return;
}
if (args[0] == "direction")
{
    TestOptionalMovingAverageEntry();
    return;
}
if (args[0] == "prewarm")
{
    TestPrewarm();
    TestNewSessionRange();
    TestRollingWhileHeldAndCooling();
    return;
}
if (args[0] == "closing")
{
    TestClosing();
    return;
}
if (args[0] == "ui")
{
    var values = new Dictionary<string, string> { ["HorizonSeconds"] = "150" };
    KGIOptionPricing.MeanReversionSessionParameterWriter.Add(values, "08:00", "14:40", "16:10", "04:55");
    Check(values["DayStartTime"] == "08:00" && values["DayStopTime"] == "14:40" &&
          values["NightStartTime"] == "16:10" && values["NightStopTime"] == "04:55",
          "Both UIs must pass all four schedule values.");
    Check(values["HorizonSeconds"] == "150", "Schedule values must not overwrite strategy parameters.");
    Exception? uiFailure = null;
    var uiThread = new Thread(() =>
    {
        try
        {
            Inspect(new KGIOptionPricing.StrategyCenterForm());
            Inspect(new KGIOptionPricing.LiveStrategyCenterForm());
        }
        catch (Exception error) { uiFailure = error; }
    });
    uiThread.SetApartmentState(ApartmentState.STA);
    uiThread.Start();
    uiThread.Join();
    if (uiFailure != null) throw new Exception("Session controls failed UI inspection.", uiFailure);
    Console.WriteLine("PASS ui");
    return;

    static void Inspect(System.Windows.Forms.Form form)
    {
        using (form)
        {
            foreach ((string name, string expected) in new[]
            {
                ("mrDayStartTime", "07:45"), ("mrDayStopTime", "14:40"),
                ("mrNightStartTime", "16:00"), ("mrNightStopTime", "04:55")
            })
            {
                System.Windows.Forms.Control[] matches = form.Controls.Find(name, true);
                Check(matches.Length == 1 && matches[0].Text == expected && matches[0].Width == 56,
                    $"{form.GetType().Name} must show a fixed-width {name} field with its default.");
            }
            foreach ((string name, decimal expected) in new[]
            {
                ("_mrH", 600m), ("_mrF", 0.6m), ("_mrK", 1.2m),
                (form is KGIOptionPricing.StrategyCenterForm ? "_mrMinRange" : "_mrMinR", 8m),
                (form is KGIOptionPricing.StrategyCenterForm ? "_mrMaxRange" : "_mrMaxR", 20m)
            })
            {
                System.Windows.Forms.Control[] matches = form.Controls.Find(name, true);
                Check(matches.Length == 1 && matches[0] is System.Windows.Forms.NumericUpDown number &&
                      number.Value == expected, $"{form.GetType().Name}.{name} must default to {expected}.");
            }
            System.Windows.Forms.Control[] panels = form.Controls.Find("mrSettingsPanel", true);
            Check(panels.Length == 1 && panels[0].Controls[0] is System.Windows.Forms.Button,
                "The start action must remain leftmost on each strategy tab.");
            if (form is KGIOptionPricing.LiveStrategyCenterForm)
            {
                Check(panels[0].Controls[0].Enabled,
                    "Explicitly opted-in paired live routing must be startable from the real-account tab.");
                Check(form.Controls.Find("mrCertificationLabel", true).Single().Text.Contains("未認證"),
                    "The live tab must disclose that broker certification remains incomplete.");
            }
            Check(form.Controls.Find("mrUseMovingAverage", true).Single() is System.Windows.Forms.CheckBox { Checked: false, Width: 90 } &&
                  form.Controls.Find("mrTrendShiftTicks", true).Single() is System.Windows.Forms.NumericUpDown { Value: 1m, Maximum: 20m, Width: 56 } &&
                  form.Controls.Find("mrMovingAverageReference", true).Single() is System.Windows.Forms.TextBox { Text: "225M", Width: 72 },
                "Both centers must expose fixed-width MA enable, N and reference controls.");
            var definitionMethod = form.GetType().GetMethod("MeanReversionDefinition",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            object?[] arguments = form is KGIOptionPricing.StrategyCenterForm ? new object?[] { "" } : Array.Empty<object?>();
            var definition = (StrategyDefinition?)definitionMethod?.Invoke(form, arguments);
            Check(definition?.Parameters.TryGetValue("MaximumRangeTicks", out string? maxR) == true && maxR == "20",
                "Both strategy tabs must pass the configured maximum R to the shared strategy.");
            Check(definition?.Parameters["UseMovingAverage"] == "False" &&
                  definition.Parameters["TrendShiftTicks"] == "1" &&
                  definition.Parameters["MovingAverageReferenceProductId"] == "225M",
                "Both tabs must preserve safe MA defaults in the strategy definition.");
        }
    }
}
if (args[0] == "replay")
{
    TestReplay();
    return;
}
if (args[0] == "paused-heartbeat")
{
    TestPausedHeartbeat();
    return;
}

var schedule = MeanReversionSessionSchedule.Parse(new Dictionary<string, string>
{
    ["DayStartTime"] = "08:00",
    ["DayStopTime"] = "14:40",
    ["NightStartTime"] = "16:10",
    ["NightStopTime"] = "04:55"
});

MeanReversionSessionWindow day = schedule.Resolve(Taipei(2026, 9, 24, 7, 50))!;
Check(day.TradingStart == Taipei(2026, 9, 24, 8, 0), "The day strategy start must be configurable independently from market open.");
Check(day.MarketOpen == Taipei(2026, 9, 24, 7, 45), "The day market-open anchor must not move with strategy start.");
Check(day.Stop == Taipei(2026, 9, 24, 14, 40), "The configured day stop must be exact.");

MeanReversionSessionWindow earlyNight = schedule.Resolve(Taipei(2026, 9, 24, 23, 59))!;
MeanReversionSessionWindow lateNight = schedule.Resolve(Taipei(2026, 9, 25, 4, 54))!;
Check(earlyNight.SessionId == lateNight.SessionId, "Overnight timestamps must resolve to one session.");
Check(lateNight.MarketOpen == Taipei(2026, 9, 24, 16, 0), "Overnight market-open must be on the previous date.");
Check(lateNight.Stop == Taipei(2026, 9, 25, 4, 55), "Overnight stop must occur on the following date.");
Check(schedule.Resolve(Taipei(2026, 9, 25, 5, 0)) is null, "The night session ends at 05:00, exclusive.");

DateTimeOffset[] stops = schedule.StopsBetween(Taipei(2026, 9, 24, 14, 39), Taipei(2026, 9, 25, 4, 55)).ToArray();
Check(stops.SequenceEqual(new[] { Taipei(2026, 9, 24, 14, 40), Taipei(2026, 9, 25, 4, 55) }),
    "Stop enumeration must contain both boundaries in chronological order.");
Check(schedule.StopsBetween(Taipei(2026, 9, 24, 14, 40), Taipei(2026, 9, 24, 14, 41)).Count == 0,
    "The lower stop-enumeration boundary is exclusive.");

MeanReversionSessionSchedule defaults = MeanReversionSessionSchedule.Parse(new Dictionary<string, string>());
Check(defaults.Resolve(Taipei(2026, 9, 24, 7, 45))!.TradingStart == Taipei(2026, 9, 24, 7, 45),
    "The default day start must be 07:45 Taipei time.");
Check(defaults.Resolve(Taipei(2026, 9, 24, 16, 0))!.Stop == Taipei(2026, 9, 25, 4, 55),
    "The default night stop must be 04:55 on the following date.");

Reject(new Dictionary<string, string> { ["DayStartTime"] = "07:30" });
Reject(new Dictionary<string, string> { ["DayStartTime"] = "15:00" });
Reject(new Dictionary<string, string> { ["DayStartTime"] = "8:00" });
Reject(new Dictionary<string, string> { ["NightStartTime"] = "04:00" });
Reject(new Dictionary<string, string> { ["NightStopTime"] = "15:00" });

Console.WriteLine("PASS schedule");

static DateTimeOffset Taipei(int year, int month, int day, int hour, int minute) =>
    new(year, month, day, hour, minute, 0, TimeSpan.FromHours(8));

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

static void TestPairPricing()
{
    Check(MeanReversionPairPricing.TryCompute(100m, 20m, .6m, 1.2m,
        5m, 100m, 110m, +1, 3, out var up), "up-shift must be feasible");
    Check(up.AppliedShiftTicks == 2 && up.Buy == 100m && up.Sell == 120m &&
          up.LongStop == 95m && up.ShortStop == 125m,
        "largest passive up-shift must keep all frozen offsets");
    Check(up.Sell - up.Buy == 20m && up.Buy - up.LongStop == 5m &&
          up.ShortStop - up.Sell == 5m, "shift must preserve spacing");
    Check(MeanReversionPairPricing.TryCompute(100m, 20m, .6m, 1.2m,
        5m, 90m, 105m, -1, 3, out var down) &&
          down.AppliedShiftTicks == 1 && down.Buy == 85m && down.Sell == 105m,
        "down-shift must shrink to ask1 boundary");
    Check(MeanReversionPairPricing.TryCompute(100m, 20m, .6m, 1.2m,
        5m, 90m, 110m, 0, 5, out var neutral) &&
          neutral.AppliedShiftTicks == 0 && neutral.Buy == 90m && neutral.Sell == 110m,
        "neutral signal must still permit both orders");
    Check(!MeanReversionPairPricing.TryCompute(100m, 20m, .6m, 1.2m,
        5m, 85m, 115m, +1, 3, out _), "infeasible zero-shift must reject both orders");
    Check(!MeanReversionPairPricing.TryCompute(100m, 20m, .6m, 1.2m,
        5m, 0m, 110m, 0, 0, out _), "missing bid1 must reject both orders");
    Console.WriteLine("PASS pair-pricing");
}

static void TestMovingAverageSampling()
{
    DateTimeOffset start = Taipei(2026, 9, 24, 8, 0);
    var average = new MeanReversionMovingAverage();
    average.Observe(start.AddMilliseconds(200), 95m, 105m);
    average.Observe(start.AddMilliseconds(800), 100m, 110m);
    average.Observe(start.AddSeconds(1).AddMilliseconds(200), 100m, 110m);
    average.Observe(start.AddSeconds(2).AddMilliseconds(200), 105m, 115m);
    Check(average.TryGet(start.AddSeconds(3), 3, 1, 110m, out decimal sma, out int sign) &&
          sma == 106.66666666666666666666666667m && sign == 1,
        "complete-second SMA must use the final fresh quote in each second");
    Check(average.TryGet(start.AddSeconds(3), 3, 1, sma, out _, out int equalSign) &&
          equalSign == 0, "price equal to SMA must be neutral");
    Check(!average.TryGet(start.AddSeconds(4), 3, 1, 110m, out _, out _),
        "a missing complete-second sample must not be interpolated");
    Console.WriteLine("PASS ma-sampling");
}

static void Reject(IReadOnlyDictionary<string, string> values)
{
    try { _ = MeanReversionSessionSchedule.Parse(values); }
    catch (ArgumentException) { return; }
    throw new Exception("Invalid session input was accepted.");
}

static void TestPrewarm()
{
    var definition = new StrategyDefinition
    {
        StrategyType = MeanReversionScalperFactory.TypeName,
        InstanceId = "session-prewarm",
        Universe = new StrategyUniverse { Tradable = new ProductSelection("225MC", "MC"),
            References = new[] { new ProductSelection("225", "L"), new ProductSelection("225M", "M") } },
        Parameters = new Dictionary<string, string>
        {
            ["HorizonSeconds"] = "600", ["DayStartTime"] = "08:00", ["DayStopTime"] = "14:40",
            ["NightStartTime"] = "16:00", ["NightStopTime"] = "04:55",
            ["F"] = "0.75", ["K"] = "0.87", ["MinimumRangeTicks"] = "1",
            ["MaximumSpreadTicks"] = "4", ["FreshnessSeconds"] = "30"
        }
    };
    var strategy = new MeanReversionScalperStrategy(definition);
    var orders = new List<PaperOrderRequest>();
    var decisions = new List<StrategyDecisionObservation>();
    strategy.DecisionObserved += decisions.Add;
    strategy.OrderRequested += order => { orders.Add(order); strategy.OnOrderSubmitted(order); };
    var universe = DirectionalUniverse();
    strategy.Start(new StrategyRuntimeContext(universe, new SystemStrategyClock(), new FlatPerformanceView()));
    strategy.OnMarketEvent(TradeAt("MC", 100m, Taipei(2026, 9, 24, 7, 46)));
    DirectionAt(strategy, 1000m, 1000m, Taipei(2026, 9, 24, 7, 50));
    strategy.OnMarketEvent(TradeAt("MC", 120m, Taipei(2026, 9, 24, 7, 56)));
    Check(orders.Count == 0, "Pre-start market data may warm H but must not place entry orders.");
    DirectionAt(strategy, 1010m, 1010m, Taipei(2026, 9, 24, 8, 0));
    strategy.OnMarketEvent(TradeAt("MC", 140m, Taipei(2026, 9, 24, 8, 0)));
    Check(orders.Count == 2 && orders.Any(x => x.Role == PaperOrderRole.EntryLong) &&
          orders.Any(x => x.Role == PaperOrderRole.EntryShort),
        "Entry may begin at configured start using already-warmed current-session H on both sides.");
    Check(strategy.GetSnapshot().ParameterSummary.Contains("DayStart=08:00", StringComparison.Ordinal),
        "Actual configured session start must appear in parameter summary.");
    Check(decisions.Any(x => x.PrimaryReasonCode == "PreStart") &&
          decisions.Any(x => x.PrimaryReasonCode == "EntryBracket") &&
          decisions.All(x => x.Thresholds.Any(t => t.Name == "HorizonSeconds")) &&
          decisions.Zip(decisions.Skip(1)).All(pair => pair.First.Sequence < pair.Second.Sequence),
          "Decisions must preserve pre-start and acceptance transitions with H threshold and ordered sequence.");
    Console.WriteLine("PASS prewarm");
}

static void TestDualResting()
{
    var strategy = new MeanReversionScalperStrategy(new StrategyDefinition
    {
        StrategyType = MeanReversionScalperFactory.TypeName,
        InstanceId = "dual-resting",
        Universe = new StrategyUniverse { Tradable = new ProductSelection("225MC", "MC") },
        Parameters = new Dictionary<string, string>
        {
            ["HorizonSeconds"] = "600", ["MinimumRangeTicks"] = "8",
            ["MaximumRangeTicks"] = "20", ["UseMovingAverage"] = "False",
            ["FreshnessSeconds"] = "30"
        }
    });
    var orders = new List<PaperOrderRequest>();
    var cancellations = new List<string>();
    strategy.OrderRequested += order => { orders.Add(order); strategy.OnOrderSubmitted(order); };
    strategy.CancelOrderRequested += cancellations.Add;
    strategy.Start(new StrategyRuntimeContext(new ResolvedStrategyUniverse
    {
        Tradable = new ResolvedProduct { ProductId = "225MC", Symbol = "MC", TickSize = 5m, ContractMultiplier = 10m }
    }, new SystemStrategyClock(), new FlatPerformanceView()));
    strategy.OnMarketEvent(TradeAt("MC", 100m, Taipei(2026, 9, 24, 7, 50)));
    strategy.OnMarketEvent(TradeAt("MC", 140m, Taipei(2026, 9, 24, 8, 0)));
    Check(orders.Count == 2 && orders.Any(x => x.Role == PaperOrderRole.EntryLong) &&
          orders.Any(x => x.Role == PaperOrderRole.EntryShort),
        "MA-off eligible cycle must prepost both original limit orders without references.");
    Check(orders.All(x => x.Type == PaperOrderType.BrokerLimit && x.Quantity == 1 &&
          x.RestingPairMode == RestingPairMode.OppositeLegIsExit &&
          x.CycleId == orders[0].CycleId && x.PairId == orders[0].PairId),
        "Both limits must belong to the same paired cycle.");
    Check(orders[0].ClientOrderId != orders[1].ClientOrderId &&
          orders[0].Price <= 135m && orders[1].Price >= 145m,
        "The orders must have distinct IDs and remain passive at the observed book.");
    PaperOrderRequest buy = orders.Single(x => x.Role == PaperOrderRole.EntryLong);
    PaperOrderRequest sell = orders.Single(x => x.Role == PaperOrderRole.EntryShort);
    strategy.OnExecution(new ExecutionEvent { StrategyInstanceId = strategy.InstanceId,
        StrategyRunGeneration = buy.StrategyRunGeneration,
        ClientOrderId = buy.ClientOrderId, CycleId = buy.CycleId, PairId = buy.PairId,
        Role = PaperOrderRole.EntryLong, OriginalOrderRole = PaperOrderRole.EntryLong,
        Side = KGIOptionPricing.OrderManagement.Core.BuySell.Buy, Symbol = "MC", Quantity = 1,
        Price = buy.Price, Timestamp = Taipei(2026, 9, 24, 8, 0).AddSeconds(1) });
    Check(orders.Count == 3 && orders[2].Role == PaperOrderRole.StopLoss &&
          !cancellations.Contains(sell.ClientOrderId),
        "Entry arms only the local stop and leaves the original opposite limit working.");
    strategy.OnExecution(new ExecutionEvent { StrategyInstanceId = strategy.InstanceId,
        StrategyRunGeneration = sell.StrategyRunGeneration,
        ClientOrderId = sell.ClientOrderId, CycleId = sell.CycleId, PairId = sell.PairId,
        Role = PaperOrderRole.TakeProfit, OriginalOrderRole = PaperOrderRole.EntryShort,
        Side = KGIOptionPricing.OrderManagement.Core.BuySell.Sell, Symbol = "MC", Quantity = 1,
        Price = sell.Price, Timestamp = Taipei(2026, 9, 24, 8, 0).AddSeconds(2) });
    Check(orders.Count == 3 && cancellations.SequenceEqual(new[] { orders[2].ClientOrderId }),
        "Original opposite order exits without a replacement TP; local stop is cancelled.");
    var failed = new MeanReversionScalperStrategy(new StrategyDefinition
    {
        StrategyType = MeanReversionScalperFactory.TypeName, InstanceId = "dual-second-failed",
        Parameters = new Dictionary<string, string> { ["HorizonSeconds"] = "600", ["MinimumRangeTicks"] = "8" }
    });
    var accepted = new List<PaperOrderRequest>();
    var failedCancels = new List<string>();
    failed.OrderRequested += request =>
    {
        if (accepted.Count == 1) throw new InvalidOperationException("second broker submission failed");
        accepted.Add(request);
        failed.OnOrderSubmitted(request);
    };
    failed.CancelOrderRequested += failedCancels.Add;
    failed.Start(new StrategyRuntimeContext(new ResolvedStrategyUniverse
    {
        Tradable = new ResolvedProduct { ProductId = "225MC", Symbol = "MC", TickSize = 5m, ContractMultiplier = 10m }
    }, new SystemStrategyClock(), new FlatPerformanceView()));
    failed.OnMarketEvent(TradeAt("MC", 100m, Taipei(2026, 9, 24, 7, 50)));
    failed.OnMarketEvent(TradeAt("MC", 140m, Taipei(2026, 9, 24, 8, 0)));
    failed.OnMarketEvent(TradeAt("MC", 140m, Taipei(2026, 9, 24, 8, 0).AddSeconds(1)));
    Check(accepted.Count == 1 && failedCancels.SequenceEqual(new[] { accepted[0].ClientOrderId }) &&
          failed.State == StrategyLifecycleState.Suspended,
        "Second-leg failure cancels only the known first leg and remains quarantined on later quotes.");
    Console.WriteLine("PASS dual-resting");
}

static void TestNewSessionRange()
{
    var strategy = new MeanReversionScalperStrategy(new StrategyDefinition
    {
        StrategyType = MeanReversionScalperFactory.TypeName,
        InstanceId = "session-reset",
        Universe = new StrategyUniverse { Tradable = new ProductSelection("225MC", "MC"),
            References = new[] { new ProductSelection("225", "L"), new ProductSelection("225M", "M") } },
        Parameters = new Dictionary<string, string>
        {
            ["HorizonSeconds"] = "600", ["MinimumRangeTicks"] = "1000", ["MaximumRangeTicks"] = "2000"
        }
    });
    strategy.Start(new StrategyRuntimeContext(new ResolvedStrategyUniverse { Tradable = new ResolvedProduct
    { ProductId = "225MC", Symbol = "MC", TickSize = 5m, ContractMultiplier = 10m } },
        new SystemStrategyClock(), new FlatPerformanceView()));
    strategy.OnMarketEvent(TradeAt("MC", 100m, Taipei(2026, 9, 24, 7, 45)));
    strategy.OnMarketEvent(TradeAt("MC", 120m, Taipei(2026, 9, 24, 7, 55)));
    strategy.OnMarketEvent(TradeAt("MC", 160m, Taipei(2026, 9, 24, 8, 0)));
    Check(strategy.GetSnapshot().Range == 40m,
        "H must roll by event time; the 07:45 sample must expire at 08:00.");
    strategy.OnMarketEvent(TradeAt("MC", 300m, Taipei(2026, 9, 24, 16, 0)));
    Check(strategy.GetSnapshot().Range == 0m && strategy.GetSnapshot().State == StrategyLifecycleState.WarmingUp,
        "First night-session trade must not reuse day-session samples.");
    strategy.OnMarketEvent(TradeAt("MC", 320m, Taipei(2026, 9, 24, 16, 10)));
    Check(strategy.GetSnapshot().Range == 20m,
        "Night session must warm its own continuous H window.");
}

static void TestRollingWhileHeldAndCooling()
{
    var performance = new MutablePerformanceView();
    var strategy = new MeanReversionScalperStrategy(new StrategyDefinition
    {
        StrategyType = MeanReversionScalperFactory.TypeName, InstanceId = "rolling-held",
        Universe = new StrategyUniverse { Tradable = new ProductSelection("225MC", "MC"),
            References = new[] { new ProductSelection("225", "L"), new ProductSelection("225M", "M") } },
        Parameters = new Dictionary<string, string>
        {
            ["HorizonSeconds"] = "600", ["MinimumRangeTicks"] = "1", ["CooldownSeconds"] = "120"
        }
    });
    var orders = new List<PaperOrderRequest>();
    strategy.OrderRequested += order => { orders.Add(order); strategy.OnOrderSubmitted(order); };
    strategy.Start(new StrategyRuntimeContext(DirectionalUniverse(),
        new SystemStrategyClock(), performance));
    strategy.OnMarketEvent(TradeAt("MC", 100m, Taipei(2026, 9, 24, 7, 45)));
    DirectionAt(strategy, 1000m, 1000m, Taipei(2026, 9, 24, 7, 45));
    DirectionAt(strategy, 990m, 990m, Taipei(2026, 9, 24, 7, 55));
    strategy.OnMarketEvent(TradeAt("MC", 120m, Taipei(2026, 9, 24, 7, 55)));
    PaperOrderRequest entry = orders.Single(x => x.Role == PaperOrderRole.EntryShort);
    performance.NetQuantity = -1;
    strategy.OnExecution(new ExecutionEvent { StrategyInstanceId = strategy.InstanceId,
        StrategyRunGeneration = entry.StrategyRunGeneration,
        ClientOrderId = entry.ClientOrderId, CycleId = entry.CycleId,
        Role = PaperOrderRole.EntryShort, Side = KGIOptionPricing.OrderManagement.Core.BuySell.Sell,
        Symbol = "MC", Quantity = 1, Price = entry.Price, Timestamp = Taipei(2026, 9, 24, 7, 55) });
    PaperOrderRequest stop = orders.Single(x => x.Role == PaperOrderRole.StopLoss);
    strategy.OnMarketEvent(TradeAt("MC", 160m, Taipei(2026, 9, 24, 8, 5)));
    strategy.OnMarketEvent(TradeAt("MC", 170m, Taipei(2026, 9, 24, 8, 6)));
    performance.NetQuantity = 0;
    strategy.OnExecution(new ExecutionEvent { StrategyInstanceId = strategy.InstanceId,
        StrategyRunGeneration = stop.StrategyRunGeneration,
        ClientOrderId = stop.ClientOrderId, CycleId = stop.CycleId,
        Role = PaperOrderRole.StopLoss, Side = KGIOptionPricing.OrderManagement.Core.BuySell.Buy,
        Symbol = "MC", Quantity = 1, Price = stop.Price,
        Timestamp = Taipei(2026, 9, 24, 8, 6).AddSeconds(1) });
    Check(strategy.GetSnapshot().RollingRange == 10m,
        "After exit, rolling H must retain samples accumulated while the position was held.");
    strategy.OnMarketEvent(TradeAt("MC", 180m, Taipei(2026, 9, 24, 8, 7)));
    Check(strategy.GetSnapshot().RollingRange == 20m,
        "The same H window must continue rolling during cooldown.");
}

static MarketEvent TradeAt(string symbol, decimal price, DateTimeOffset at) => new()
{
    Kind = MarketEventKind.Trade, Symbol = symbol, ProductId = "225MC", Timestamp = at,
    LastPrice = price, BidPrices = new[] { price - 5m }, AskPrices = new[] { price + 5m },
    BidQuantities = new[] { 1 }, AskQuantities = new[] { 1 }
};

static ResolvedStrategyUniverse DirectionalUniverse() => new()
{
    Tradable = new ResolvedProduct { ProductId = "225MC", Symbol = "MC", TickSize = 5m, ContractMultiplier = 10m },
    References = new[]
    {
        new ResolvedProduct { ProductId = "225", Symbol = "L", TickSize = 10m, ContractMultiplier = 1000m },
        new ResolvedProduct { ProductId = "225M", Symbol = "M", TickSize = 5m, ContractMultiplier = 100m }
    }
};

static void DirectionAt(MeanReversionScalperStrategy strategy, decimal large, decimal mini, DateTimeOffset at)
{
    strategy.OnMarketEvent(new MarketEvent { Kind = MarketEventKind.Book, ProductId = "225", Symbol = "L",
        Timestamp = at, BidPrices = new[] { large - 5m }, AskPrices = new[] { large + 5m } });
    strategy.OnMarketEvent(new MarketEvent { Kind = MarketEventKind.Book, ProductId = "225M", Symbol = "M",
        Timestamp = at, BidPrices = new[] { mini - 5m }, AskPrices = new[] { mini + 5m } });
}

static void TestOptionalMovingAverageEntry()
{
    var strategy = new MeanReversionScalperStrategy(new StrategyDefinition
    {
        StrategyType = MeanReversionScalperFactory.TypeName, InstanceId = "ma-reference-only",
        Universe = new StrategyUniverse { Tradable = new ProductSelection("225MC", "MC"),
            References = new[] { new ProductSelection("225", "L"), new ProductSelection("225M", "M") } },
        Parameters = new Dictionary<string, string>
        {
            ["HorizonSeconds"] = "10", ["MinimumRangeTicks"] = "8",
            ["MaximumRangeTicks"] = "20", ["UseMovingAverage"] = "True",
            ["TrendShiftTicks"] = "1", ["MovingAverageReferenceProductId"] = "225M"
        }
    });
    var orders = new List<PaperOrderRequest>();
    strategy.OrderRequested += order => { orders.Add(order); strategy.OnOrderSubmitted(order); };
    strategy.Start(new StrategyRuntimeContext(DirectionalUniverse(), new SystemStrategyClock(), new FlatPerformanceView()));
    DateTimeOffset start = Taipei(2026, 9, 24, 8, 0);
    strategy.OnMarketEvent(TradeAt("MC", 100m, start.AddSeconds(-10)));
    for (int second = -10; second < 0; second++)
        strategy.OnMarketEvent(new MarketEvent { Kind = MarketEventKind.Book, Symbol = "M", ProductId = "225M",
            Timestamp = start.AddSeconds(second), BidPrices = new[] { 995m }, AskPrices = new[] { 1005m } });
    strategy.OnMarketEvent(new MarketEvent { Kind = MarketEventKind.Book, Symbol = "M", ProductId = "225M",
        Timestamp = start, BidPrices = new[] { 1005m }, AskPrices = new[] { 1015m } });
    strategy.OnMarketEvent(TradeAt("MC", 140m, start));
    Check(orders.Count == 2 && orders[0].Price == 130m && orders[1].Price == 160m,
        "Only configured 225M SMA may shift both prices one tick; stale 225 must not block entry.");
    Check(strategy.GetSnapshot().ParameterSummary.Contains("N=1/1t", StringComparison.Ordinal) &&
          strategy.GetSnapshot().ParameterSummary.Contains("SMA=1000", StringComparison.Ordinal),
        "Configured and applied shift plus reference SMA must be observable.");
    Console.WriteLine("PASS direction");
}

static void TestClosing()
{
    var definition = new StrategyDefinition
    {
        StrategyType = MeanReversionScalperFactory.TypeName,
        InstanceId = "session-close",
        Universe = new StrategyUniverse { Tradable = new ProductSelection("225MC", "MC") },
        Parameters = new Dictionary<string, string>
        {
            ["HorizonSeconds"] = "600", ["F"] = "0.75", ["K"] = "0.87",
            ["MinimumRangeTicks"] = "1", ["MaximumSpreadTicks"] = "4"
        }
    };
    var strategy = new MeanReversionScalperStrategy(definition);
    var orders = new List<PaperOrderRequest>();
    var cancelled = new List<string>();
    strategy.OrderRequested += order => { orders.Add(order); strategy.OnOrderSubmitted(order); };
    strategy.CancelOrderRequested += cancelled.Add;
    var universe = DirectionalUniverse();
    strategy.Start(new StrategyRuntimeContext(universe, new SystemStrategyClock(), new FlatPerformanceView()));
    strategy.OnMarketEvent(TradeAt("MC", 100m, Taipei(2026, 9, 24, 7, 45)));
    DirectionAt(strategy, 1000m, 1000m, Taipei(2026, 9, 24, 7, 45));
    DirectionAt(strategy, 990m, 990m, Taipei(2026, 9, 24, 7, 55));
    strategy.OnMarketEvent(TradeAt("MC", 120m, Taipei(2026, 9, 24, 7, 55)));
    Check(orders.Count == 2 && orders.Any(x => x.Role == PaperOrderRole.EntryLong) &&
          orders.Any(x => x.Role == PaperOrderRole.EntryShort),
        "Test setup must create both paired entry limits.");
    strategy.OnHeartbeat(Taipei(2026, 9, 24, 14, 40));
    Check(cancelled.Count == 2, "Session stop must cancel both outstanding limits even without market data.");
    Check(orders.Count == 2, "Session stop cannot send a new entry or flatten while cancellation is unconfirmed.");
    strategy.OnHeartbeat(Taipei(2026, 9, 24, 14, 40));
    Check(cancelled.Count == 2, "Repeated heartbeat cannot duplicate cancel requests.");
    foreach (PaperOrderRequest entry in orders.ToArray())
        strategy.OnOrderLifecycle(new OrderLifecycleEvent
        {
            Timestamp = Taipei(2026, 9, 24, 14, 40), StrategyInstanceId = entry.StrategyInstanceId,
            StrategyRunGeneration = entry.StrategyRunGeneration, ClientOrderId = entry.ClientOrderId,
            CycleId = entry.CycleId, Role = entry.Role, Symbol = entry.Symbol,
            State = OrderLifecycleState.Cancelled, FilledQuantity = 0,
            HasFinalCumulativeFillWatermark = true, BrokerFinalCumulativeFilledQuantity = 0
        });
    Check(strategy.GetSnapshot().Reason.Contains("SessionStop", StringComparison.Ordinal),
        "Flat and settled strategy must stay stopped for this session.");
    strategy.OnMarketEvent(TradeAt("MC", 140m, Taipei(2026, 9, 24, 14, 41)));
    Check(orders.Count == 2, "Late market data cannot reopen entries after stop.");
    TestHeldPositionClose();
    Console.WriteLine("PASS closing");
}

static void TestHeldPositionClose()
{
    var definition = new StrategyDefinition
    {
        StrategyType = MeanReversionScalperFactory.TypeName, InstanceId = "session-held",
        Universe = new StrategyUniverse { Tradable = new ProductSelection("225MC", "MC"),
            References = new[] { new ProductSelection("225", "L"), new ProductSelection("225M", "M") } },
        Parameters = new Dictionary<string, string> { ["HorizonSeconds"] = "600", ["MinimumRangeTicks"] = "1" }
    };
    var performance = new MutablePerformanceView();
    var strategy = new MeanReversionScalperStrategy(definition);
    var orders = new List<PaperOrderRequest>();
    var cancelled = new List<string>();
    strategy.OrderRequested += order => { orders.Add(order); strategy.OnOrderSubmitted(order); };
    strategy.CancelOrderRequested += cancelled.Add;
    strategy.Start(new StrategyRuntimeContext(DirectionalUniverse(),
        new SystemStrategyClock(), performance));
    strategy.OnMarketEvent(TradeAt("MC", 100m, Taipei(2026, 9, 24, 7, 45)));
    DirectionAt(strategy, 1000m, 1000m, Taipei(2026, 9, 24, 7, 45));
    DirectionAt(strategy, 990m, 990m, Taipei(2026, 9, 24, 7, 55));
    strategy.OnMarketEvent(TradeAt("MC", 120m, Taipei(2026, 9, 24, 7, 55)));
    PaperOrderRequest entryShort = orders.Single(x => x.Role == PaperOrderRole.EntryShort);
    performance.NetQuantity = -1;
    strategy.OnExecution(new ExecutionEvent
    {
        StrategyInstanceId = definition.InstanceId, StrategyRunGeneration = entryShort.StrategyRunGeneration,
        ClientOrderId = entryShort.ClientOrderId, CycleId = entryShort.CycleId,
        Symbol = "MC", Role = PaperOrderRole.EntryShort, Side = KGIOptionPricing.OrderManagement.Core.BuySell.Sell,
        Quantity = 1, Price = entryShort.Price, Timestamp = Taipei(2026, 9, 24, 7, 55)
    });
    PaperOrderRequest stop = orders.Single(x => x.Role == PaperOrderRole.StopLoss);
    Deliver(entryShort, OrderLifecycleState.Filled, 1);
    PaperOrderRequest opposite = orders.Single(x => x.Role == PaperOrderRole.EntryLong);
    strategy.OnMarketEvent(TradeAt("MC", 130m,
        Taipei(2026, 9, 24, 14, 39).AddSeconds(55)));
    strategy.OnHeartbeat(Taipei(2026, 9, 24, 14, 40));
    Check(cancelled.SequenceEqual(new[] { opposite.ClientOrderId }),
        "Stop must cancel the original opposite limit first and keep protective stop working.");
    Check(orders.All(x => x.Role != PaperOrderRole.Flatten), "No flatten before opposite cancellation confirmation.");
    Deliver(opposite, OrderLifecycleState.Cancelled, 0);
    Check(cancelled.Last() == stop.ClientOrderId && orders.All(x => x.Role != PaperOrderRole.Flatten),
        "Only after opposite-limit settlement may the stop be cancelled.");
    Deliver(stop, OrderLifecycleState.Cancelled, 0);
    Check(orders.Count(x => x.Role == PaperOrderRole.Flatten) == 1 &&
          orders.Last().Type == PaperOrderType.Market &&
          orders.Last().Side == KGIOptionPricing.OrderManagement.Core.BuySell.Buy,
          "Settled short position must produce exactly one buy-market flatten.");
    strategy.OnHeartbeat(Taipei(2026, 9, 24, 14, 41));
    Check(orders.Count(x => x.Role == PaperOrderRole.Flatten) == 1, "Repeated heartbeats cannot duplicate flatten.");
    PaperOrderRequest flatten = orders.Single(x => x.Role == PaperOrderRole.Flatten);
    Deliver(flatten, OrderLifecycleState.Rejected, 0);
    Check(strategy.GetSnapshot().Reason.Contains("CloseBlocked", StringComparison.Ordinal),
        "Rejected flatten with a remaining position must block rather than pretend to be flat.");
    strategy.OnHeartbeat(Taipei(2026, 9, 24, 14, 42));
    Check(orders.Count(x => x.Role == PaperOrderRole.Flatten) == 1,
        "Rejected flatten may not be silently retried as a duplicate market order.");

    void Deliver(PaperOrderRequest order, OrderLifecycleState state, int fill)
    {
        strategy.OnOrderLifecycle(new OrderLifecycleEvent
        {
            Timestamp = Taipei(2026, 9, 24, 14, 40), StrategyInstanceId = order.StrategyInstanceId,
            StrategyRunGeneration = order.StrategyRunGeneration, ClientOrderId = order.ClientOrderId,
            CycleId = order.CycleId, Role = order.Role, Symbol = order.Symbol,
            State = state, FilledQuantity = fill, DeliveredFilledQuantity = fill,
            HasFinalCumulativeFillWatermark = true, BrokerFinalCumulativeFilledQuantity = fill
        });
    }
}

static void TestReplay()
{
    string file = Path.GetTempFileName();
    try
    {
        var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() } };
        File.WriteAllLines(file, new[]
        {
            TradeAt("MC", 100m, Taipei(2026, 9, 24, 14, 39)),
            TradeAt("MC", 120m, Taipei(2026, 9, 24, 14, 40))
        }.Select(value => JsonSerializer.Serialize(value, json)));
        foreach (double speed in new[] { 0d, 1000d })
        {
            using var source = new ArchiveReplayMarketDataSource(file);
            using var host = new StrategyHost(source, new SystemStrategyClock());
            MeanReversionSessionSchedule schedule = MeanReversionSessionSchedule.Parse(new Dictionary<string, string>());
            var sequence = new List<string>();
            var strategy = new MeanReversionScalperStrategy(new StrategyDefinition
            {
                StrategyType = MeanReversionScalperFactory.TypeName, InstanceId = "replay-stop",
                Universe = new StrategyUniverse { Tradable = new ProductSelection("225MC", "MC") }
            });
            strategy.DecisionObserved += decision =>
            {
                if (decision.PrimaryReasonCode == "SessionStop")
                    sequence.Add($"decision:{decision.Timestamp:HH:mm:ss}");
            };
            host.Add(strategy, new ResolvedStrategyUniverse { Tradable = new ResolvedProduct
            { ProductId = "225MC", Symbol = "MC", TickSize = 5m, ContractMultiplier = 10m } });
            DateTimeOffset? previous = null;
            source.BeforeMarketEvent += at =>
            {
                if (previous.HasValue)
                    foreach (DateTimeOffset stop in schedule.StopsBetween(previous.Value, at))
                    {
                        sequence.Add($"stop:{stop:HH:mm:ss}");
                        host.Pulse(stop);
                    }
                previous = at;
                sequence.Add($"before:{at:HH:mm:ss}");
            };
            source.MarketEventReceived += market => sequence.Add($"market:{market.Timestamp:HH:mm:ss}");
            source.ReplayAsync(speed).GetAwaiter().GetResult();
            Check(sequence.SequenceEqual(new[]
            {
                "before:14:39:00", "market:14:39:00", "stop:14:40:00",
                "decision:14:40:00", "before:14:40:00", "market:14:40:00"
            }), "Replay must record exact session stop before applying the later market event at either speed.");
        }
        Console.WriteLine("PASS replay");
    }
    finally { File.Delete(file); }
}

static void TestPausedHeartbeat()
{
    using var source = new NoopMarketSource();
    using var host = new StrategyHost(source, new SystemStrategyClock());
    var strategy = new MeanReversionScalperStrategy(new StrategyDefinition
    {
        StrategyType = MeanReversionScalperFactory.TypeName, InstanceId = "paused-session-stop",
        Universe = new StrategyUniverse { Tradable = new ProductSelection("225MC", "MC") }
    });
    host.Add(strategy, new ResolvedStrategyUniverse { Tradable = new ResolvedProduct
    { ProductId = "225MC", Symbol = "MC", TickSize = 5m, ContractMultiplier = 10m } });
    Check(host.Pause(strategy.InstanceId), "Test setup must pause the strategy.");
    host.Pulse(Taipei(2026, 9, 24, 14, 40));
    Check(strategy.GetSnapshot().Reason.Contains("SessionStop", StringComparison.Ordinal),
        $"A flat paused strategy must still receive the scheduled risk heartbeat: {strategy.GetSnapshot().Reason}");
    host.Pulse(Taipei(2026, 9, 24, 16, 0));
    host.Pulse(Taipei(2026, 9, 24, 16, 1));
    Check(strategy.GetSnapshot().State == StrategyLifecycleState.WarmingUp,
        "A settled strategy must enter the new night session once and keep warming without stale samples.");
    Console.WriteLine("PASS paused-heartbeat");
}

sealed class FlatPerformanceView : IStrategyPerformanceView
{
    public PositionSnapshot GetPosition(string strategyInstanceId, string symbol) => new()
    {
        StrategyInstanceId = strategyInstanceId, Symbol = symbol, NetQuantity = 0
    };
}

sealed class MutablePerformanceView : IStrategyPerformanceView
{
    public int NetQuantity { get; set; }
    public PositionSnapshot GetPosition(string strategyInstanceId, string symbol) => new()
    {
        StrategyInstanceId = strategyInstanceId, Symbol = symbol, NetQuantity = NetQuantity
    };
}

sealed class NoopMarketSource : IMarketEventSource
{
    public event Action<MarketEvent>? MarketEventReceived;
    public void Subscribe(IEnumerable<ResolvedProduct> products) { }
    public void Dispose() { }
}

sealed class ZeroClock : IStrategyClock
{
    public DateTimeOffset Now => default;
}
