using System.Globalization;
using KGIOptionPricing;
using KGIOptionPricing.OrderManagement.Core;
using KGIOptionPricing.Strategies.MiniLeadMicro;
using KGIOptionPricing.Strategies.PeriodicQueueCycle;
using KGIOptionPricing.StrategyFramework;

TestParameterDefaultsAndSummary();
TestPeriodicQueueLegacyPriceBoundaryIsIgnored();
TestPeriodicQueueLegacyMaxChaseSettingIsIgnored();
TestInvariantParsing();
TestParameterSafetyRanges();
TestParameterSafetyBoundariesAreAccepted();
TestParameterCrossConstraints();
TestDirectionalAssessments();
TestNeutralDirectionConditions();
TestHealthRejectionCodes();
TestUtcSecondSamplingAndWarmup();
TestQueueInitialPricingUsesCumulativeAheadSymmetrically();
TestQueueRepriceRequiresStrictlyMoreThanThreshold();
TestQueueRepriceNeverBecomesLessAggressive();
TestQueuePricingFollowsBookWithoutBoundary();
TestQueuePricingHandlesZeroTargetOneTickSpreadAndInvalidBooks();
TestQueuePricingSupportsDifferentTickSizes();
TestQueuePricingRejectsOffTickBooksAndInvalidTickSize();
TestPeriodicStrategyWarmsUpAndSubmitsBothSidesTogether();
TestPeriodicStrategyMapsDirectionalQueueParameters();
TestPeriodicStrategyRepricesOnlyAfterExactCancellation();
TestPeriodicStrategyIsNotOcoWhenOneSideFills();
TestPeriodicStrategySuppressesReplacementAfterCancelFillRace();
TestPeriodicStrategyCancelsBothOnConfirmedPreFillReversal();
TestPeriodicStrategyHandlesLateFillFromReplacedGeneration();
TestPeriodicStrategyHandlesRejectedAndCancelFailedLifecycle();
TestPeriodicStrategyCountsDistinctOppositeSeconds();
TestPeriodicStrategyPreservesReentrantFirstLegFill();
TestPeriodicStrategyHandlesSynchronousLateFillCancellation();
TestPeriodicStrategyFaultsOnDistinctGenerationOverfill();
TestPeriodicStrategyPreservesReentrantInitialRejection();
TestPeriodicStrategyFaultsWhenExitCancellationFails();
TestPeriodicStrategyPreservesSynchronousReplacementRejectionReason();
TestPeriodicStrategyPauseCancelsFlatPairAndKeepsPositionProtection();
TestPeriodicStrategyPreservesFaultDuringReentrantExitCancelFailure();
TestPeriodicStrategyDoesNotRepriceOnUnhealthyBook();
TestPeriodicStrategyStopsOnlyAfterOrdersAndPositionAreZero();
TestPeriodicStrategySnapshotExposesFrozenCycleAndLegState();
TestPeriodicStrategyFaultsWhenRunnerCannotRouteTargetedCancel();
TestPeriodicStrategyPreservesFaultDuringReentrantStopCancelFailure();
TestPeriodicStrategyRetriesClosingBlockedCancellation();
TestPeriodicRunnerStopUsesTargetedManagedCancellation();
TestPeriodicStrategyResumeStartsFreshCycleAfterFlatPause();
TestPeriodicStrategyStopsRepriceEvaluationAfterFirstRouteFailure();
TestPeriodicStrategyFaultsOnLateFillFromRetiredCycle();
TestPeriodicStrategyDoesNotStartCycleWithExistingPosition();
TestPeriodicStrategyFixedStopsUseLedgerAndWaitForPeerTerminal();
TestPeriodicFixedStopWaitsForFreshCounterpartyQuote();
TestPeriodicStrategyOneLegTimeoutHasExactBoundaryAndFlattens();
TestPeriodicStrategyUnhealthyBookRecoversOrFlattensWithoutFalseStop();
TestPeriodicStrategyCancelFillRaceRereadsLedgerBeforeExit();
TestPeriodicStrategyCooldownsClearDirectionWindow();
TestPeriodicStrategyPauseProtectsHeldPositionAndStopFlattensSafely();
TestPeriodicStrategyAcceptsFilledLifecycleAfterExecutionStartsCooldown();
TestPeriodicStrategyIgnoresDuplicateBookForRiskTriggers();
TestPeriodicHeartbeatAdvancesTimeoutAndHealthRecoveryWithoutMarketEvents();
TestPeriodicLifecycleFirstWaitsForAuthoritativeExecutions();
TestPeriodicLifecycleFirstStopWaitsForExecutionAndProtection();
TestPeriodicFixedStopDoesNotTriggerBelowSixTicks();
TestPeriodicExitCallbacksAreIdempotentOutOfOrder();
TestPeriodicHealthFlattenUsesNextTradePriceThroughRealPaperBroker();
TestPeriodicStrategyPublishesTypedDecisionEvidence();
TestPeriodicRegistrationAndResearchValidationGate();
TestPeriodicControlSurfaceDefaultsAndResearchGate();
TestPeriodicLiveTestingDoesNotRequireResearchValidation();

Console.WriteLine("PeriodicQueueCycle smoke tests passed.");

static void TestPeriodicStrategyPublishesTypedDecisionEvidence()
{
    DateTimeOffset start = new(2026, 9, 24, 1, 0, 0, TimeSpan.Zero);
    var clock = new MutableClock(start);
    var strategy = (PeriodicQueueCycleStrategy)new PeriodicQueueCycleFactory().Create(
        PeriodicDefinition("decision-evidence", RiskParameters()));
    var decisions = new List<StrategyDecisionObservation>();
    ((IStrategyDecisionSource)strategy).DecisionObserved += decisions.Add;
    strategy.Start(PeriodicContext(clock));
    strategy.OnMarketEvent(StrategyBook(100m, 105m, 20, 10, 1, start));
    clock.Advance(start.AddSeconds(1));
    strategy.OnMarketEvent(new MarketEvent
    {
        Sequence = 2, Timestamp = clock.Now, Symbol = "225MC", ProductId = "225MC",
        Kind = MarketEventKind.Book,
        BidPrices = new[] { 110m }, BidQuantities = new[] { 1 },
        AskPrices = new[] { 115m }, AskQuantities = new[] { 1 }
    });
    clock.Advance(start.AddSeconds(2));
    strategy.OnMarketEvent(StrategyBook(110m, 115m, 20, 10, 3, clock.Now));
    clock.Advance(start.AddSeconds(3));
    strategy.OnMarketEvent(StrategyBook(120m, 125m, 20, 10, 4, clock.Now));
    clock.Advance(start.AddSeconds(4));
    strategy.OnMarketEvent(StrategyBook(125m, 130m, 20, 10, 5, clock.Now));

    StrategyDecisionObservation warmup = decisions.First(x => x.Decision == StrategyDecision.Warmup && x.Thresholds.Count > 0);
    StrategyDecisionObservation noBook = decisions.First(x => x.Sequence == 0);
    StrategyDecisionObservation rejected = decisions.First(x => x.PrimaryReasonCode == "InvalidBook");
    StrategyDecisionObservation accepted = decisions.First(x => x.Decision == StrategyDecision.Accept);
    Assert(warmup.Thresholds.Any(x => x.Name == "DirectionWindowSeconds" && !x.Passed),
        "暖機 observation 必須包含尚未通過的方向窗門檻");
    Assert(noBook.RequiredProductsPresent is null && noBook.CrossProductReceiveSkewSeconds is null &&
           noBook.UnavailableReasons["requiredProductsPresent"] == "NoBookData" &&
           noBook.UnavailableReasons["crossProductReceiveSkewSeconds"] == "NotApplicableSingleProduct",
        "尚無 book 時不得伪造 required-products=true 或 cross-product skew");
    Assert(rejected.DataQualityFlags.Contains("InvalidBook"),
        "健康拒絕 observation 必須包含穩定資料品質原因");
    Assert(accepted.PrimaryReasonCode == "SignalAccepted" && accepted.CycleId == "cycle-1",
        $"接受 observation 必須關聯真正 cycle；actual={accepted.PrimaryReasonCode}/{accepted.CycleId}");
    Assert(accepted.Thresholds.Any(x => x.Name == "MinTrendEfficiency" && x.Passed) &&
           accepted.Features.ContainsKey("averageObi") && accepted.QuoteAgeSeconds == 0m &&
           accepted.SpreadTicks == 1m,
        "接受 observation 必須包含特徵、門檻、報價年齡與價差");
    Assert(decisions.Count(x => x.Decision == StrategyDecision.Accept) == 1,
        "同一 candidate/cycle 只能發布一次 SignalAccepted");
    Assert(decisions.Count(x => x.PrimaryReasonCode == "SignalAccepted") == 1,
        "SignalAccepted reason 只能在真正建立新 cycle 時發布一次");
    StrategyDecisionObservation working = decisions.Last();
    Assert(working.Decision == StrategyDecision.Hold && working.PrimaryReasonCode == "WorkingBoth" &&
           working.CandidateId == accepted.CandidateId && working.CycleId == accepted.CycleId,
        $"進入工作狀態後必須保留 coordinator reason，不得被舊 assessment 改回 Accept；actual={working.Decision}/{working.PrimaryReasonCode}");
}

static void TestPeriodicRegistrationAndResearchValidationGate()
{
    Assert(StrategySimulationService.DefaultFactories.Any(factory =>
               factory.StrategyType == PeriodicQueueCycleFactory.TypeName &&
               factory.DisplayName == "週期雙邊完成單") &&
           LiveStrategyService.DefaultFactories.Any(factory =>
               factory.StrategyType == PeriodicQueueCycleFactory.TypeName &&
               factory.DisplayName == "週期雙邊完成單"),
        "模擬與實盤服務都必須註冊週期雙邊完成單工廠");

    StrategyDefinition periodicMissing = PeriodicDefinition("live-periodic-missing", Values());
    Assert(LiveStrategyService.TryValidateResearchStrategy(periodicMissing, out string? periodicMissingReason) &&
           periodicMissingReason == null,
        "週期策略解除實盤測試鎖定後，不得要求 ResearchValidated");
    StrategyDefinition periodicFalse = PeriodicDefinition("live-periodic-false", Values(("ResearchValidated", "false")));
    Assert(LiveStrategyService.TryValidateResearchStrategy(periodicFalse, out string? periodicFalseReason) &&
           periodicFalseReason == null,
        "ResearchValidated=false 的週期策略不得被實盤研究閘門拒絕");
    StrategyDefinition periodicTrue = PeriodicDefinition("live-periodic-true", Values(("ResearchValidated", "true")));
    Assert(LiveStrategyService.TryValidateResearchStrategy(periodicTrue, out _),
        "ResearchValidated=true 的週期策略必須通過研究閘門");
    var nonResearch = new StrategyDefinition
    {
        StrategyType = "MeanReversionScalper",
        InstanceId = "live-nonresearch",
        Universe = new StrategyUniverse { Tradable = new ProductSelection("225MC", "225MC") },
        Parameters = Values()
    };
    Assert(LiveStrategyService.TryValidateResearchStrategy(nonResearch, out _),
        "非研究策略不得被週期策略研究閘門誤拒絕");
    var miniLead = new StrategyDefinition
    {
        StrategyType = MiniLeadMicroFactory.TypeName,
        InstanceId = "live-mini-lead-missing",
        Universe = new StrategyUniverse { Tradable = new ProductSelection("225MC", "225MC") },
        Parameters = Values()
    };
    Assert(!LiveStrategyService.TryValidateResearchStrategy(miniLead, out string? miniLeadReason) &&
           miniLeadReason == "ResearchValidationRequired",
        "解除週期策略鎖定不得意外解除 Mini Lead 的研究驗證閘門");
}

static void TestPeriodicLiveTestingDoesNotRequireResearchValidation()
{
    StrategyDefinition periodic = PeriodicDefinition("live-periodic-testing", Values(("ResearchValidated", "false")));
    Assert(LiveStrategyService.TryValidateResearchStrategy(periodic, out string? reasonCode) && reasonCode == null,
        "解除週期策略實盤測試鎖定後，ResearchValidated=false 不得阻擋既有的人工確認實盤流程");
}

static void TestPeriodicControlSurfaceDefaultsAndResearchGate()
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try
        {
            using var simulation = new PeriodicQueueCycleControls(live: false);
            using var live = new PeriodicQueueCycleControls(live: true);
            StrategyDefinition simulationDefinition = simulation.CreateDefinition();
            StrategyDefinition liveDefinition = live.CreateDefinition();
            Assert(simulationDefinition.StrategyType == PeriodicQueueCycleFactory.TypeName &&
                   simulationDefinition.Parameters["MaxOneLegSeconds"] == "30" &&
                   simulationDefinition.Parameters["CycleCooldownSeconds"] == "45" &&
                   simulationDefinition.Parameters["StopLossCooldownSeconds"] == "60",
                "週期策略控制頁必須暴露單邊逾時與各種冷卻時間的預設值");
            Assert(!simulation.Controls.OfType<CheckBox>().Any(control => control.Name == "ResearchValidated") &&
                   live.Controls.OfType<CheckBox>().Single(control => control.Name == "ResearchValidated").Checked == false &&
                   LiveStrategyService.TryValidateResearchStrategy(liveDefinition, out string? reason) &&
                   reason == null,
                "實盤週期控制頁的研究欄位只供紀錄，不得再鎖定測試啟動");
            live.ResearchValidated.Checked = true;
            Assert(LiveStrategyService.TryValidateResearchStrategy(live.CreateDefinition(), out _),
                "研究紀錄欄位不應改變週期策略的實盤測試可用性");
        }
        catch (Exception exception)
        {
            failure = exception;
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (failure != null) throw new InvalidOperationException("週期策略控制頁建構或安全閘門失敗", failure);
}

static void TestParameterDefaultsAndSummary()
{
    PeriodicQueueCycleParameters p = PeriodicQueueCycleParameters.Parse(new Dictionary<string, string>());
    Assert(p.DirectionWindowSeconds == 15 && p.MinMoveTicks == 2m &&
           p.MinTrendEfficiency == 0.40m && p.MinDepthObi == 0.10m &&
           p.ObiConfirmSeconds == 3 && p.DirectionReverseSeconds == 3 &&
           p.MinSideDepth == 827 && p.MinNearDepth == 165 && p.MinDepthRatio == 0.35m &&
           p.MaxSpreadTicks == 2m && p.MaxVolatilityTicks == 5m &&
           p.QuoteFreshnessSeconds == 2m &&
           p.StopLossTicks == 6m && p.MaxOneLegSeconds == 30 &&
           p.CycleCooldownSeconds == 45 && p.StopLossCooldownSeconds == 60 &&
           p.ShockCooldownSeconds == 45 && p.MarketDataRecoverySeconds == 2m &&
           p.CommissionPerContract == 36m && !p.ResearchValidated &&
           p.TrendInitialAheadA == 22 && p.TrendRepriceThresholdB == 49 && p.TrendRepriceTargetC == 22 &&
           p.OppositeInitialAheadA == 240 && p.OppositeRepriceThresholdB == 312 && p.OppositeRepriceTargetC == 22,
        "週期策略預設參數不符設計規格");

    const string expected = "DirectionWindowSeconds=15;MinMoveTicks=2;MinTrendEfficiency=0.40;MinDepthObi=0.10;ObiConfirmSeconds=3;DirectionReverseSeconds=3;MinSideDepth=827;MinNearDepth=165;MinDepthRatio=0.35;MaxSpreadTicks=2;MaxVolatilityTicks=5;QuoteFreshnessSeconds=2;StopLossTicks=6;MaxOneLegSeconds=30;CycleCooldownSeconds=45;StopLossCooldownSeconds=60;ShockCooldownSeconds=45;MarketDataRecoverySeconds=2;CommissionPerContract=36;ResearchValidated=false;TrendInitialAheadA=22;TrendRepriceThresholdB=49;TrendRepriceTargetC=22;OppositeInitialAheadA=240;OppositeRepriceThresholdB=312;OppositeRepriceTargetC=22";
    Assert(p.ParameterSummary == expected, $"參數摘要必須完整且穩定。實際：{p.ParameterSummary}");
}

static void TestInvariantParsing()
{
    CultureInfo previous = CultureInfo.CurrentCulture;
    try
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        PeriodicQueueCycleParameters p = PeriodicQueueCycleParameters.Parse(new Dictionary<string, string>
        {
            ["MinTrendEfficiency"] = "0.25",
            ["QuoteFreshnessSeconds"] = "1.5",
            ["ResearchValidated"] = "true"
        });
        Assert(p.MinTrendEfficiency == 0.25m && p.QuoteFreshnessSeconds == 1.5m && p.ResearchValidated,
            "參數必須使用 InvariantCulture 解析");
        AssertThrows<ArgumentException>(() => PeriodicQueueCycleParameters.Parse(
            new Dictionary<string, string> { ["MinTrendEfficiency"] = "0,25" }));
        AssertThrows<ArgumentException>(() => PeriodicQueueCycleParameters.Parse(
            new Dictionary<string, string> { ["ResearchValidated"] = "1" }));
    }
    finally
    {
        CultureInfo.CurrentCulture = previous;
    }
}

static void TestParameterSafetyRanges()
{
    var invalid = new (string Name, string Value)[]
    {
        ("DirectionWindowSeconds", "1"), ("DirectionWindowSeconds", "301"),
        ("MinMoveTicks", "0.49"), ("MinMoveTicks", "50.01"),
        ("MinTrendEfficiency", "-0.01"), ("MinTrendEfficiency", "1.01"),
        ("MinDepthObi", "-0.01"), ("MinDepthObi", "1.01"),
        ("ObiConfirmSeconds", "0"), ("ObiConfirmSeconds", "31"),
        ("DirectionReverseSeconds", "0"), ("DirectionReverseSeconds", "31"),
        ("MinSideDepth", "0"), ("MinSideDepth", "1000001"),
        ("MinNearDepth", "0"), ("MinNearDepth", "1000001"),
        ("MinDepthRatio", "-0.01"), ("MinDepthRatio", "1.01"),
        ("MaxSpreadTicks", "0.99"), ("MaxSpreadTicks", "20.01"),
        ("MaxVolatilityTicks", "0.99"), ("MaxVolatilityTicks", "100.01"),
        ("QuoteFreshnessSeconds", "0.09"), ("QuoteFreshnessSeconds", "30.01"),
        ("StopLossTicks", "0.99"), ("StopLossTicks", "100.01"),
        ("MaxOneLegSeconds", "0"), ("MaxOneLegSeconds", "301"),
        ("CycleCooldownSeconds", "-1"), ("CycleCooldownSeconds", "3601"),
        ("StopLossCooldownSeconds", "-1"), ("StopLossCooldownSeconds", "3601"),
        ("ShockCooldownSeconds", "-1"), ("ShockCooldownSeconds", "3601"),
        ("MarketDataRecoverySeconds", "0.09"), ("MarketDataRecoverySeconds", "30.01"),
        ("CommissionPerContract", "-0.01"),
        ("TrendInitialAheadA", "-1"), ("TrendRepriceThresholdB", "0"), ("TrendRepriceTargetC", "-1"),
        ("OppositeInitialAheadA", "-1"), ("OppositeRepriceThresholdB", "0"), ("OppositeRepriceTargetC", "-1")
    };

    foreach ((string name, string value) in invalid)
    {
        AssertThrows<ArgumentException>(() => PeriodicQueueCycleParameters.Parse(
            new Dictionary<string, string> { [name] = value }), $"{name}={value} 應被拒絕");
    }
}

static void TestParameterSafetyBoundariesAreAccepted()
{
    var valid = new (string Label, IReadOnlyDictionary<string, string> Values)[]
    {
        ("DirectionWindowSeconds lower", Values(("DirectionWindowSeconds", "2"), ("ObiConfirmSeconds", "2"))),
        ("DirectionWindowSeconds upper", Values(("DirectionWindowSeconds", "300"))),
        ("MinMoveTicks lower", Values(("MinMoveTicks", "0.5"))),
        ("MinMoveTicks upper", Values(("MinMoveTicks", "50"))),
        ("MinTrendEfficiency lower", Values(("MinTrendEfficiency", "0"))),
        ("MinTrendEfficiency upper", Values(("MinTrendEfficiency", "1"))),
        ("MinDepthObi lower", Values(("MinDepthObi", "0"))),
        ("MinDepthObi upper", Values(("MinDepthObi", "1"))),
        ("ObiConfirmSeconds lower", Values(("ObiConfirmSeconds", "1"))),
        ("ObiConfirmSeconds upper", Values(("DirectionWindowSeconds", "30"), ("ObiConfirmSeconds", "30"))),
        ("DirectionReverseSeconds lower", Values(("DirectionReverseSeconds", "1"))),
        ("DirectionReverseSeconds upper", Values(("DirectionReverseSeconds", "30"))),
        ("MinSideDepth lower", Values(("MinSideDepth", "1"))),
        ("MinSideDepth upper", Values(("MinSideDepth", "1000000"))),
        ("MinNearDepth lower", Values(("MinNearDepth", "1"))),
        ("MinNearDepth upper", Values(("MinNearDepth", "1000000"))),
        ("MinDepthRatio lower", Values(("MinDepthRatio", "0"))),
        ("MinDepthRatio upper", Values(("MinDepthRatio", "1"))),
        ("MaxSpreadTicks lower", Values(("MaxSpreadTicks", "1"))),
        ("MaxSpreadTicks upper", Values(("MaxSpreadTicks", "20"))),
        ("MaxVolatilityTicks lower", Values(("MaxVolatilityTicks", "1"))),
        ("MaxVolatilityTicks upper", Values(("MaxVolatilityTicks", "100"))),
        ("QuoteFreshnessSeconds lower", Values(("QuoteFreshnessSeconds", "0.1"))),
        ("QuoteFreshnessSeconds upper", Values(("QuoteFreshnessSeconds", "30"))),
        ("StopLossTicks lower", Values(("StopLossTicks", "1"))),
        ("StopLossTicks upper", Values(("StopLossTicks", "100"))),
        ("MaxOneLegSeconds lower", Values(("MaxOneLegSeconds", "1"))),
        ("MaxOneLegSeconds upper", Values(("MaxOneLegSeconds", "300"))),
        ("CycleCooldownSeconds lower", Values(("CycleCooldownSeconds", "0"))),
        ("CycleCooldownSeconds upper", Values(("CycleCooldownSeconds", "3600"))),
        ("StopLossCooldownSeconds lower", Values(("StopLossCooldownSeconds", "0"))),
        ("StopLossCooldownSeconds upper", Values(("StopLossCooldownSeconds", "3600"))),
        ("ShockCooldownSeconds lower", Values(("ShockCooldownSeconds", "0"))),
        ("ShockCooldownSeconds upper", Values(("ShockCooldownSeconds", "3600"))),
        ("MarketDataRecoverySeconds lower", Values(("MarketDataRecoverySeconds", "0.1"))),
        ("MarketDataRecoverySeconds upper", Values(("MarketDataRecoverySeconds", "30"))),
        ("CommissionPerContract lower", Values(("CommissionPerContract", "0"))),
        ("ResearchValidated false", Values(("ResearchValidated", "false"))),
        ("ResearchValidated true", Values(("ResearchValidated", "true"))),
        ("TrendInitialAheadA lower", Values(("TrendInitialAheadA", "0"))),
        ("TrendRepriceThresholdB lower", Values(("TrendRepriceThresholdB", "1"), ("TrendRepriceTargetC", "0"))),
        ("TrendRepriceTargetC lower", Values(("TrendRepriceTargetC", "0"))),
        ("OppositeInitialAheadA lower", Values(("OppositeInitialAheadA", "0"))),
        ("OppositeRepriceThresholdB lower", Values(("OppositeRepriceThresholdB", "1"), ("OppositeRepriceTargetC", "0"))),
        ("OppositeRepriceTargetC lower", Values(("OppositeRepriceTargetC", "0")))
    };

    foreach ((string label, IReadOnlyDictionary<string, string> values) in valid)
    {
        try
        {
            PeriodicQueueCycleParameters.Parse(values);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"安全邊界本身應可接受：{label}", ex);
        }
    }
}

static void TestParameterCrossConstraints()
{
    AssertThrows<ArgumentException>(() => PeriodicQueueCycleParameters.Parse(
        new Dictionary<string, string> { ["DirectionWindowSeconds"] = "2", ["ObiConfirmSeconds"] = "3" }));
    AssertThrows<ArgumentException>(() => PeriodicQueueCycleParameters.Parse(
        new Dictionary<string, string> { ["TrendRepriceThresholdB"] = "10", ["TrendRepriceTargetC"] = "10" }));
    AssertThrows<ArgumentException>(() => PeriodicQueueCycleParameters.Parse(
        new Dictionary<string, string> { ["OppositeRepriceThresholdB"] = "10", ["OppositeRepriceTargetC"] = "11" }));
}

static void TestDirectionalAssessments()
{
    PeriodicQueueCycleParameters parameters = PeriodicQueueCycleParameters.Parse(new Dictionary<string, string>());
    DateTimeOffset start = new(2026, 9, 21, 0, 0, 0, TimeSpan.Zero);

    var up = new DirectionWindow(5m, parameters);
    for (int i = 0; i < 15; i++)
    {
        decimal mid = i == 14 ? 110m : 100m;
        Assert(up.TryAdd(Book(mid, 130, 90, i + 1, start.AddSeconds(i)), start.AddSeconds(i)), "向上測試樣本應健康");
    }

    DirectionAssessment upAssessment = up.Evaluate(start.AddSeconds(14), 5m, parameters);
    Assert(up.SampleCount == 15 && upAssessment.Direction == CycleDirection.Up,
        $"預期向上，實際 {upAssessment.Direction}/{upAssessment.PrimaryReasonCode}");
    Assert(upAssessment.NetMoveTicks == 2m && upAssessment.PathTicks == 2m &&
           upAssessment.TrendEfficiency == 1m && upAssessment.SpreadTicks == 1m &&
           upAssessment.VolatilityTicks == 2m,
        "向上方向的價格特徵必須符合手算值");
    AssertNear(upAssessment.AverageObi, 0.181818m, 0.000001m, "最近三秒加權 OBI 手算值不符");
    Assert(upAssessment.PrimaryReasonCode == "SignalAccepted" && upAssessment.OtherFailedReasonCodes.Count == 0,
        "向上訊號應以穩定原因碼接受");
    ThresholdResult upMove = upAssessment.Thresholds.Single(x => x.Name == "UpNetMoveTicks");
    Assert(upMove.Observed == 2m && upMove.Required == 2m && upMove.Passed,
        "方向門檻結果必須揭露手算觀測值、需求值及通過結果");

    var down = new DirectionWindow(5m, parameters);
    for (int i = 0; i < 15; i++)
    {
        decimal mid = i switch
        {
            0 => 120m,
            1 => 115m,
            2 => 120m,
            13 => 110m,
            14 => 105m,
            _ => 120m
        };
        (int bidQuantity, int askQuantity) = i switch
        {
            12 => (90, 130),
            13 => (100, 140),
            14 => (110, 150),
            _ => (150, 90)
        };
        Assert(down.TryAdd(Book(mid, bidQuantity, askQuantity, i + 1, start.AddSeconds(i)), start.AddSeconds(i)), "向下測試樣本應健康");
    }

    DirectionAssessment downAssessment = down.Evaluate(start.AddSeconds(14), 5m, parameters);
    Assert(downAssessment.Direction == CycleDirection.Down && downAssessment.NetMoveTicks == -3m &&
           downAssessment.PathTicks == 5m && downAssessment.TrendEfficiency == 0.6m,
        $"非鏡像向下價格特徵不符手算值，實際 {downAssessment.Direction}/{downAssessment.NetMoveTicks}/{downAssessment.PathTicks}/{downAssessment.TrendEfficiency}");
    AssertNear(downAssessment.AverageObi, -0.167444m, 0.000001m, "向下 OBI 必須只平均最近三秒的不同深度");
    ThresholdResult downMove = downAssessment.Thresholds.Single(x => x.Name == "DownNetMoveTicks");
    ThresholdResult downEfficiency = downAssessment.Thresholds.Single(x => x.Name == "MinTrendEfficiency");
    ThresholdResult downObi = downAssessment.Thresholds.Single(x => x.Name == "DownMinDepthObi");
    Assert(downMove.Observed == 3m && downMove.Required == 2m && downMove.Passed,
        "向下淨移動門檻結果不符手算值");
    Assert(downEfficiency.Observed == 0.6m && downEfficiency.Required == 0.40m && downEfficiency.Passed,
        "向下效率門檻結果不符手算值");
    AssertNear(downObi.Observed, 0.167444m, 0.000001m, "向下 OBI 門檻觀測值不符手算值");
    Assert(downObi.Required == 0.10m && downObi.Passed, "向下 OBI 門檻需求與結果不符");
}

static void TestNeutralDirectionConditions()
{
    PeriodicQueueCycleParameters parameters = PeriodicQueueCycleParameters.Parse(new Dictionary<string, string>());
    DateTimeOffset start = new(2026, 9, 21, 1, 0, 0, TimeSpan.Zero);

    var lowEfficiency = new DirectionWindow(5m, parameters);
    for (int i = 0; i < 15; i++)
    {
        decimal mid = i == 14 ? 110m : (i % 2 == 0 ? 100m : 105m);
        Assert(lowEfficiency.TryAdd(Book(mid, 130, 90, i + 1, start.AddSeconds(i)), start.AddSeconds(i)), "低效率樣本仍應是健康行情");
    }

    DirectionAssessment lowEfficiencyAssessment = lowEfficiency.Evaluate(start.AddSeconds(14), 5m, parameters);
    Assert(lowEfficiencyAssessment.Direction == CycleDirection.Neutral &&
           lowEfficiencyAssessment.PathTicks == 14m &&
           lowEfficiencyAssessment.TrendEfficiency < 0.15m &&
           lowEfficiencyAssessment.OtherFailedReasonCodes.Contains("TrendEfficiencyBelowThreshold", StringComparer.Ordinal),
        "價格淨移動足夠但路徑效率不足時必須保持中性");

    var obiDisagreement = new DirectionWindow(5m, parameters);
    for (int i = 0; i < 15; i++)
    {
        decimal mid = i == 14 ? 110m : 100m;
        Assert(obiDisagreement.TryAdd(Book(mid, 90, 130, i + 1, start.AddSeconds(i)), start.AddSeconds(i)), "OBI 反向樣本仍應是健康行情");
    }

    DirectionAssessment obiAssessment = obiDisagreement.Evaluate(start.AddSeconds(14), 5m, parameters);
    Assert(obiAssessment.Direction == CycleDirection.Neutral && obiAssessment.PrimaryReasonCode == "NeutralDirection" &&
           obiAssessment.OtherFailedReasonCodes.Contains("DepthObiBelowThreshold", StringComparer.Ordinal),
        "價格向上但 OBI 向下時不得以多數決接受方向");
}

static void TestHealthRejectionCodes()
{
    PeriodicQueueCycleParameters parameters = PeriodicQueueCycleParameters.Parse(new Dictionary<string, string>());
    DateTimeOffset t = new(2026, 9, 21, 2, 0, 0, TimeSpan.Zero);

    AssertRejected("StaleQuote", Book(100m, 130, 90, 1, t), t.AddSeconds(3), parameters);
    AssertRejected("InvalidBook", Book(100m, 130, 90, 1, t) with
    {
        BidPrices = new[] { 97.5m }, BidQuantities = new[] { 130 },
        AskPrices = new[] { 102.5m }, AskQuantities = new[] { 90 }
    }, t, parameters);
    AssertRejected("InvalidBook", Book(100m, 130, 90, 1, t) with
    {
        BidPrices = Enumerable.Range(0, 10).Select(i => 105m - i * 5m).ToArray()
    }, t, parameters);
    AssertRejected("InvalidBook", Book(100m, 130, 90, 1, t) with
    {
        BidPrices = new[] { 97.5m, 92.5m, 95m, 87.5m, 82.5m, 77.5m, 72.5m, 67.5m, 62.5m, 57.5m }
    }, t, parameters);
    AssertRejected("InvalidBook", Book(100m, 130, 90, 1, t) with
    {
        AskPrices = new[] { 102.5m, 107.5m, 105m, 117.5m, 122.5m, 127.5m, 132.5m, 137.5m, 142.5m, 147.5m }
    }, t, parameters);
    AssertRejected("InsufficientSideDepth", Book(100m, 80, 90, 1, t), t, parameters);

    MarketEvent nearShallow = Book(100m, 200, 200, 1, t) with
    {
        BidQuantities = new[] { 10, 10, 10, 200, 200, 200, 200, 200, 200, 200 },
        AskQuantities = new[] { 10, 10, 10, 200, 200, 200, 200, 200, 200, 200 }
    };
    AssertRejected("InsufficientNearDepth", nearShallow, t, parameters);
    AssertRejected("DepthImbalance", Book(100m, 90, 300, 1, t), t, parameters);
    AssertRejected("SpreadTooWide", Book(100m, 130, 90, 1, t, spreadTicks: 3m), t, parameters);

    var volatileWindow = new DirectionWindow(5m, parameters);
    for (int i = 0; i < 14; i++)
    {
        Assert(volatileWindow.TryAdd(Book(100m, 130, 90, i + 1, t.AddSeconds(i)), t.AddSeconds(i)), "波動測試的基準樣本應健康");
    }
    Assert(!volatileWindow.TryAdd(Book(130m, 130, 90, 15, t.AddSeconds(14)), t.AddSeconds(14)), "超過五跳的樣本不得加入方向窗");
    Assert(volatileWindow.Evaluate(t.AddSeconds(14), 5m, parameters).PrimaryReasonCode == "VolatilityTooHigh",
        "過度波動必須回報穩定拒絕原因");
}

static void TestUtcSecondSamplingAndWarmup()
{
    PeriodicQueueCycleParameters parameters = PeriodicQueueCycleParameters.Parse(new Dictionary<string, string>());
    DateTimeOffset t = new(2026, 9, 21, 3, 0, 0, TimeSpan.Zero);
    var window = new DirectionWindow(5m, parameters);

    Assert(window.TryAdd(Book(100m, 130, 90, 1, t.AddMilliseconds(100)), t.AddMilliseconds(100)), "首筆同秒事件應加入");
    Assert(window.TryAdd(Book(105m, 130, 90, 3, t.AddMilliseconds(800)), t.AddMilliseconds(800)), "同秒較新事件應取代");
    Assert(!window.TryAdd(Book(110m, 130, 90, 2, t.AddMilliseconds(500)), t.AddMilliseconds(800)), "同秒舊事件不得覆寫");
    Assert(!window.TryAdd(Book(110m, 130, 90, 2, t.AddMilliseconds(900)), t.AddMilliseconds(900)), "時間較晚但序號倒退的同秒事件不得覆寫");
    Assert(window.SampleCount == 1 && window.LatestSequence == 3,
        "同秒或亂序事件不得以舊序號覆寫較新的健康樣本");
    MarketEvent malformedOlderSequence = Book(110m, 130, 90, 2, t.AddMilliseconds(900)) with
    {
        BidPrices = new[] { 107.5m }, BidQuantities = new[] { 130 },
        AskPrices = new[] { 112.5m }, AskQuantities = new[] { 90 }
    };
    Assert(!window.TryAdd(malformedOlderSequence, t.AddMilliseconds(900)), "亂序 malformed 事件不得加入方向窗");
    DirectionAssessment retained = window.Evaluate(t.AddMilliseconds(900), 5m, parameters);
    Assert(window.LatestSequence == 3 && retained.PrimaryReasonCode != "InvalidBook",
        "亂序 malformed 事件不得覆寫健康樣本或污染拒絕狀態");
    Assert(!window.TryAdd(Book(100m, 130, 90, 4, t.AddSeconds(-1)), t.AddMilliseconds(800)), "跨秒倒序事件不得污染方向窗");

    var gap = new DirectionWindow(5m, parameters);
    long sequence = 1;
    for (int i = 0; i <= 15; i++)
    {
        if (i == 7) continue;
        gap.TryAdd(Book(100m, 130, 90, sequence++, t.AddSeconds(i)), t.AddSeconds(i));
    }

    DirectionAssessment gapAssessment = gap.Evaluate(t.AddSeconds(15), 5m, parameters);
    Assert(gapAssessment.Direction == CycleDirection.Neutral && gapAssessment.PrimaryReasonCode == "WarmupIncomplete",
        "只有樣本數但 UTC 秒不連續時不得視為完整 15 秒暖機");
    ThresholdResult warmup = gapAssessment.Thresholds.Single(x => x.Name == "DirectionWindowSeconds");
    Assert(warmup.Observed == 8m && warmup.Required == 15m && !warmup.Passed,
        $"缺秒暖機必須揭露尾端實際連續秒數，實際 {warmup.Observed}");
}

static void TestPeriodicQueueLegacyPriceBoundaryIsIgnored()
{
    var policy = new QueueCyclePricingPolicy();
    MarketEvent book = QueueBook(5m, 100m, 110m, new[] { 10, 15 }, new[] { 10, 15 });

    QueuePriceDecision decision = policy.EvaluateReprice(
        BuySell.Buy, book, 85m, 40, 10, 22, 5m);

    Assert(decision.Action == QueuePriceAction.Reprice && decision.Price == 95m,
        "目標價較目前買價積極時，即使跨越舊價格邊界也必須改價");
}

static void TestPeriodicQueueLegacyMaxChaseSettingIsIgnored()
{
    PeriodicQueueCycleParameters parameters = PeriodicQueueCycleParameters.Parse(Values(
        ("MaxChaseTicks", "-100")));

    Assert(!parameters.ParameterSummary.Contains("MaxChaseTicks=", StringComparison.Ordinal),
        "舊 MaxChaseTicks 設定必須被安全忽略，且不得顯示為有效參數");
}

static void TestQueueInitialPricingUsesCumulativeAheadSymmetrically()
{
    var policy = new QueueCyclePricingPolicy();
    MarketEvent book = QueueBook(5m, 100m, 110m, new[] { 10, 15, 20 }, new[] { 7, 9, 30 });

    QueuePriceDecision buy = policy.SelectInitial(BuySell.Buy, book, 22, 5m);
    QueuePriceDecision sell = policy.SelectInitial(BuySell.Sell, book, 22, 5m);

    Assert(buy.Action == QueuePriceAction.Place && buy.Price == 95m,
        $"買單 A 必須選在累積第 22 口所在價位 95，實際 {buy.Action}/{buy.Price}");
    Assert(sell.Action == QueuePriceAction.Place && sell.Price == 120m,
        $"賣單 A 必須依獨立賣方累積量選在第 22 口所在價位 120，實際 {sell.Action}/{sell.Price}");
}

static void TestQueueRepriceRequiresStrictlyMoreThanThreshold()
{
    var policy = new QueueCyclePricingPolicy();
    MarketEvent book = QueueBook(5m, 100m, 110m, new[] { 10, 15, 20 }, new[] { 7, 9, 30 });

    QueuePriceDecision buyAtB = policy.EvaluateReprice(BuySell.Buy, book, 90m, 5, 30, 22, 5m);
    QueuePriceDecision buyAboveB = policy.EvaluateReprice(BuySell.Buy, book, 90m, 5, 29, 22, 5m);
    QueuePriceDecision sellAtB = policy.EvaluateReprice(BuySell.Sell, book, 125m, 5, 51, 12, 5m);
    QueuePriceDecision sellAboveB = policy.EvaluateReprice(BuySell.Sell, book, 125m, 5, 50, 12, 5m);

    Assert(buyAtB.Action == QueuePriceAction.Hold && buyAtB.Price == 90m &&
           buyAtB.BetterPriceQuantity == 25 && buyAtB.SamePriceAheadQuantity == 5,
        "同價前方量加更佳價位量恰等於 B 時不得改掛");
    Assert(buyAboveB.Action == QueuePriceAction.Reprice && buyAboveB.Price == 95m,
        $"買單 totalAhead 大於 B 時必須依 C 改掛至 95，實際 {buyAboveB.Action}/{buyAboveB.Price}");
    Assert(sellAtB.Action == QueuePriceAction.Hold && sellAtB.Price == 125m &&
           sellAtB.BetterPriceQuantity == 46 && sellAtB.SamePriceAheadQuantity == 5,
        "賣單的獨立總前方量恰等於 B 時不得改掛");
    Assert(sellAboveB.Action == QueuePriceAction.Reprice && sellAboveB.Price == 115m,
        $"賣單 totalAhead 大於 B 時必須依獨立賣方 C 累積量改掛至 115，實際 {sellAboveB.Action}/{sellAboveB.Price}");
}

static void TestQueueRepriceNeverBecomesLessAggressive()
{
    var policy = new QueueCyclePricingPolicy();
    MarketEvent book = QueueBook(5m, 100m, 110m, new[] { 10, 15, 20 }, new[] { 10, 15, 20 });

    QueuePriceDecision buy = policy.EvaluateReprice(BuySell.Buy, book, 100m, 50, 10, 22, 5m);
    QueuePriceDecision sell = policy.EvaluateReprice(BuySell.Sell, book, 110m, 50, 10, 22, 5m);

    Assert(buy.Action == QueuePriceAction.Hold && buy.Price == 100m && buy.ReasonCode == "TargetPriceLessAggressive",
        "買單 C 候選較低時不得往較不積極方向改掛");
    Assert(sell.Action == QueuePriceAction.Hold && sell.Price == 110m && sell.ReasonCode == "TargetPriceLessAggressive",
        "賣單 C 候選較高時不得往較不積極方向改掛");
}

static void TestQueuePricingFollowsBookWithoutBoundary()
{
    var policy = new QueueCyclePricingPolicy();
    MarketEvent buyBook = QueueBook(5m, 100m, 110m, new[] { 10, 15 }, new[] { 10, 15 });
    QueuePriceDecision buy = policy.EvaluateReprice(BuySell.Buy, buyBook, 85m, 40, 10, 22, 5m);
    Assert(buy.Action == QueuePriceAction.Reprice && buy.Price == 95m,
        "買單改價不受舊價格邊界限制，必須跟隨目前十檔的 C 目標");

    MarketEvent sellBook = QueueBook(5m, 100m, 110m, new[] { 4, 6, 8 }, new[] { 7, 9, 30 });
    QueuePriceDecision sell = policy.EvaluateReprice(BuySell.Sell, sellBook, 125m, 40, 10, 12, 5m);
    Assert(sell.Action == QueuePriceAction.Reprice && sell.Price == 115m,
        "賣單改價不受舊價格邊界限制，必須跟隨目前十檔的 C 目標");
}

static void TestQueuePricingHandlesZeroTargetOneTickSpreadAndInvalidBooks()
{
    var policy = new QueueCyclePricingPolicy();
    MarketEvent oneTick = QueueBook(5m, 100m, 105m, new[] { 10 }, new[] { 10 });

    QueuePriceDecision buy = policy.SelectInitial(BuySell.Buy, oneTick, 0, 5m);
    QueuePriceDecision sell = policy.SelectInitial(BuySell.Sell, oneTick, 0, 5m);
    Assert(buy.Action == QueuePriceAction.Place && buy.Price == 105m,
        "A=0 且價差一跳時買單必須使用對手第一檔，不得穿價");
    Assert(sell.Action == QueuePriceAction.Place && sell.Price == 100m,
        "A=0 且價差一跳時賣單必須使用對手第一檔，不得穿價");

    MarketEvent invalid = oneTick with { AskPrices = Array.Empty<decimal>(), AskQuantities = Array.Empty<int>() };
    QueuePriceDecision invalidInitial = policy.SelectInitial(BuySell.Buy, invalid, 22, 5m);
    QueuePriceDecision invalidReprice = policy.EvaluateReprice(BuySell.Sell, invalid, 105m, 20, 10, 22, 5m);
    Assert(invalidInitial.Action == QueuePriceAction.Hold && invalidInitial.Price == 0m &&
           invalidInitial.ReasonCode == "InvalidBook",
        "無效 Book 不得產生首次掛單價格");
    Assert(invalidReprice.Action == QueuePriceAction.Hold && invalidReprice.Price == 105m &&
           invalidReprice.ReasonCode == "InvalidBook",
        "無效 Book 不得產生改掛價格");
}

static void TestQueuePricingSupportsDifferentTickSizes()
{
    var policy = new QueueCyclePricingPolicy();
    MarketEvent book = QueueBook(2.5m, 100m, 102.5m, new[] { 10 }, new[] { 10 });
    QueuePriceDecision buy = policy.SelectInitial(BuySell.Buy, book, 0, 2.5m);
    QueuePriceDecision sell = policy.SelectInitial(BuySell.Sell, book, 0, 2.5m);
    Assert(buy.Price == 102.5m && sell.Price == 100m,
        $"政策必須使用傳入的 TickSize，不得寫死 5：{buy.Price}/{sell.Price}");
}

static void TestQueuePricingRejectsOffTickBooksAndInvalidTickSize()
{
    var policy = new QueueCyclePricingPolicy();

    MarketEvent offTick = QueueBook(5m, 100m, 110m, new[] { 10, 15 }, new[] { 7, 9 }) with
    {
        BidPrices = new[] { 100m, 92.5m }
    };
    QueuePriceDecision offTickInitial = policy.SelectInitial(BuySell.Buy, offTick, 22, 5m);
    QueuePriceDecision offTickReprice = policy.EvaluateReprice(BuySell.Buy, offTick, 90m, 40, 10, 22, 5m);
    Assert(offTickInitial.Action == QueuePriceAction.Hold && offTickInitial.Price == 0m &&
           offTickInitial.ReasonCode == "InvalidBook",
        "任一正價 Book level 未對齊 Tick 時不得猜測修正或首次掛單");
    Assert(offTickReprice.Action == QueuePriceAction.Hold && offTickReprice.Price == 90m &&
           offTickReprice.ReasonCode == "InvalidBook",
        "任一正價 Book level 未對齊 Tick 時不得猜測修正或改掛");

    MarketEvent offTickAsk = QueueBook(5m, 100m, 110m, new[] { 10, 15 }, new[] { 7, 30 }) with
    {
        AskPrices = new[] { 110m, 117.5m }
    };
    QueuePriceDecision offTickSell = policy.SelectInitial(BuySell.Sell, offTickAsk, 22, 5m);
    Assert(offTickSell.Action == QueuePriceAction.Hold && offTickSell.Price == 0m &&
           offTickSell.ReasonCode == "InvalidBook",
        "賣方候選價未對齊 Tick 時也不得猜測修正或掛單");

    MarketEvent bidOutOfOrder = QueueBook(5m, 100m, 110m, new[] { 10, 15, 20 }, new[] { 7, 9, 30 }) with
    {
        BidPrices = new[] { 100m, 90m, 95m }
    };
    MarketEvent askOutOfOrder = QueueBook(5m, 100m, 110m, new[] { 10, 15, 20 }, new[] { 7, 9, 30 }) with
    {
        AskPrices = new[] { 110m, 120m, 115m }
    };
    Assert(policy.SelectInitial(BuySell.Buy, bidOutOfOrder, 22, 5m).ReasonCode == "InvalidBook",
        "買方價位未嚴格遞減時必須拒絕 Book");
    Assert(policy.SelectInitial(BuySell.Sell, askOutOfOrder, 22, 5m).ReasonCode == "InvalidBook",
        "賣方價位未嚴格遞增時必須拒絕 Book");

    QueuePriceDecision invalidTick = policy.SelectInitial(
        BuySell.Buy, QueueBook(5m, 100m, 110m, new[] { 10 }, new[] { 7 }), 1, 0m);
    Assert(invalidTick.Action == QueuePriceAction.Hold && invalidTick.ReasonCode == "InvalidBook",
        "零 TickSize 不得產生猜測價格");
}

static void TestPeriodicStrategyWarmsUpAndSubmitsBothSidesTogether()
{
    DateTimeOffset start = new(2026, 9, 22, 0, 0, 0, TimeSpan.Zero);
    var clock = new MutableClock(start);
    var factory = new PeriodicQueueCycleFactory();
    Assert(factory.StrategyType == PeriodicQueueCycleFactory.TypeName &&
           factory.StrategyType == "PeriodicQueueCycle",
        "Factory 必須提供穩定的 PeriodicQueueCycle 型別名稱");

    var definition = PeriodicDefinition("periodic-up", Values(
        ("DirectionWindowSeconds", "2"), ("ObiConfirmSeconds", "1"),
        ("MinSideDepth", "1"), ("MinNearDepth", "1"),
        ("MaxChaseTicks", "20"),
        ("TrendInitialAheadA", "5"), ("TrendRepriceThresholdB", "100000"), ("TrendRepriceTargetC", "1"),
        ("OppositeInitialAheadA", "20"), ("OppositeRepriceThresholdB", "100000"), ("OppositeRepriceTargetC", "1")));
    var strategy = (PeriodicQueueCycleStrategy)factory.Create(definition);
    var orders = new List<(int Callback, PaperOrderRequest Order)>();
    int callback = 0;
    strategy.OrderRequested += order => orders.Add((callback, order));

    Assert(strategy.State == StrategyLifecycleState.Created, "策略建立後必須處於 Created");
    strategy.Start(PeriodicContext(clock));
    Assert(strategy.State == StrategyLifecycleState.WarmingUp, "啟動後必須進入 WarmingUp");

    callback = 1;
    strategy.OnMarketEvent(StrategyBook(100m, 105m, 20, 10, 1, start));
    Assert(orders.Count == 0 && strategy.State == StrategyLifecycleState.WarmingUp,
        "完整方向視窗以前不得送單");

    callback = 2;
    clock.Now = start.AddSeconds(1);
    strategy.OnMarketEvent(StrategyBook(110m, 115m, 20, 10, 2, clock.Now));
    Assert(orders.Count == 2 && orders.All(x => x.Callback == 2) &&
           orders.Any(x => x.Order.Side == BuySell.Buy) &&
           orders.Any(x => x.Order.Side == BuySell.Sell),
        "方向成立時必須在同一市場回呼建立買賣兩張完成單");
    Assert(orders.All(x => x.Order.Type == PaperOrderType.BrokerLimit && x.Order.Quantity == 1),
        "雙邊完成單必須是一口 BrokerLimit");
    Assert(orders.Single(x => x.Order.Side == BuySell.Buy).Order.ClientOrderId.Contains("buy-g1", StringComparison.Ordinal) &&
           orders.Single(x => x.Order.Side == BuySell.Sell).Order.ClientOrderId.Contains("sell-g1", StringComparison.Ordinal) &&
           orders.Select(x => x.Order.ClientOrderId).Distinct(StringComparer.Ordinal).Count() == 2,
        "雙邊完成單必須有不同且可追蹤的世代識別");
    Assert(strategy.State == StrategyLifecycleState.Running,
        "送出雙邊完成單後公開生命週期必須是 Running");
    Assert(strategy.GetSnapshot().ParameterSummary == PeriodicQueueCycleParameters.Parse(definition.Parameters).ParameterSummary,
        "策略快照必須揭露每個實際生效參數");
}

static void TestPeriodicStrategyMapsDirectionalQueueParameters()
{
    IReadOnlyDictionary<string, string> parameters = Values(
        ("DirectionWindowSeconds", "2"), ("ObiConfirmSeconds", "1"),
        ("MinSideDepth", "1"), ("MinNearDepth", "1"),
        ("MaxChaseTicks", "20"),
        ("TrendInitialAheadA", "5"), ("TrendRepriceThresholdB", "100000"), ("TrendRepriceTargetC", "1"),
        ("OppositeInitialAheadA", "20"), ("OppositeRepriceThresholdB", "100000"), ("OppositeRepriceTargetC", "1"));
    DateTimeOffset start = new(2026, 9, 22, 1, 0, 0, TimeSpan.Zero);

    List<PaperOrderRequest> up = RunInitialPair(
        "mapping-up", parameters, start,
        StrategyBook(100m, 105m, 20, 10, 1, start),
        StrategyBook(110m, 115m, 20, 10, 2, start.AddSeconds(1)));
    Assert(up.Single(x => x.Side == BuySell.Buy).Price == 110m &&
           up.Single(x => x.Side == BuySell.Sell).Price == 120m,
        "向上時買方必須採趨勢 A、賣方採反方向 A");

    List<PaperOrderRequest> down = RunInitialPair(
        "mapping-down", parameters, start,
        StrategyBook(110m, 115m, 10, 20, 1, start),
        StrategyBook(100m, 105m, 10, 20, 2, start.AddSeconds(1)));
    Assert(down.Single(x => x.Side == BuySell.Buy).Price == 95m &&
           down.Single(x => x.Side == BuySell.Sell).Price == 105m,
        "向下時買方必須採反方向 A、賣方採趨勢 A");
}

static void TestPeriodicStrategyRepricesOnlyAfterExactCancellation()
{
    DateTimeOffset start = new(2026, 9, 22, 2, 0, 0, TimeSpan.Zero);
    (PeriodicQueueCycleStrategy strategy, MutableClock clock, List<PaperOrderRequest> orders) =
        StartWorkingUpStrategy("reprice", RepriceParameters(), start);
    var cancels = new List<string>();
    strategy.CancelOrderRequested += cancels.Add;
    string oldBuy = orders.Single(x => x.Side == BuySell.Buy).ClientOrderId;
    string oldSell = orders.Single(x => x.Side == BuySell.Sell).ClientOrderId;

    clock.Now = start.AddSeconds(2);
    strategy.OnMarketEvent(StrategyBook(110m, 115m, 20, 10, 3, clock.Now));
    Assert(cancels.SequenceEqual(new[] { oldBuy }, StringComparer.Ordinal),
        "B 超過時只能針對需要改掛的買單要求撤單");
    Assert(orders.Count == 2, "撤單確認前不得提前送出替代世代");

    strategy.OnOrderLifecycle(Cancelled(oldSell, strategy.InstanceId, clock.Now));
    Assert(orders.Count == 2, "非目標 client 或另一側 Cancelled 不得建立替代單");
    strategy.OnOrderLifecycle(Cancelled(oldBuy, strategy.InstanceId, clock.Now));
    Assert(orders.Count == 3 && orders[^1].Side == BuySell.Buy &&
           orders[^1].ClientOrderId.Contains("buy-g2", StringComparison.Ordinal) &&
           orders[^1].Price == 115m,
        "只有目標舊世代 Cancelled 才能建立 g+1 替代單");
    strategy.OnOrderLifecycle(Cancelled(oldBuy, strategy.InstanceId, clock.Now));
    Assert(orders.Count == 3, "重複或過期的舊世代 Cancelled 必須忽略");
}

static void TestPeriodicStrategyIsNotOcoWhenOneSideFills()
{
    DateTimeOffset start = new(2026, 9, 22, 3, 0, 0, TimeSpan.Zero);
    (PeriodicQueueCycleStrategy strategy, MutableClock _, List<PaperOrderRequest> orders) =
        StartWorkingUpStrategy("non-oco", NoRepriceParameters(), start);
    var cancels = new List<string>();
    strategy.CancelOrderRequested += cancels.Add;
    PaperOrderRequest buy = orders.Single(x => x.Side == BuySell.Buy);

    strategy.OnExecution(Fill(buy.ClientOrderId, strategy.InstanceId, BuySell.Buy, buy.Price, start.AddSeconds(2)));
    Assert(cancels.Count == 0,
        "一側成交不得自動取消另一側；雙邊完成單不是 OCO");
    Assert(orders.Count == 2 && orders.Any(x => x.Side == BuySell.Sell),
        "一側成交後另一側完成單必須維持獨立工作");
    PaperOrderRequest sell = orders.Single(x => x.Side == BuySell.Sell);
    strategy.OnExecution(Fill(sell.ClientOrderId, strategy.InstanceId, BuySell.Sell, sell.Price, start.AddSeconds(3)));
    Assert(strategy.GetSnapshot().CompletedTrades == 0,
        "Periodic 不得自行計數 completed trade；權威值必須由 Runner 的共用 ledger 覆寫");
}

static void TestPeriodicStrategySuppressesReplacementAfterCancelFillRace()
{
    DateTimeOffset start = new(2026, 9, 22, 4, 0, 0, TimeSpan.Zero);
    (PeriodicQueueCycleStrategy strategy, MutableClock clock, List<PaperOrderRequest> orders) =
        StartWorkingUpStrategy("cancel-fill-race", RepriceParameters(), start);
    var cancels = new List<string>();
    strategy.CancelOrderRequested += cancels.Add;
    PaperOrderRequest oldBuy = orders.Single(x => x.Side == BuySell.Buy);

    clock.Now = start.AddSeconds(2);
    strategy.OnMarketEvent(StrategyBook(110m, 115m, 20, 10, 3, clock.Now));
    Assert(cancels.SequenceEqual(new[] { oldBuy.ClientOrderId }, StringComparer.Ordinal),
        "競速測試必須先進入指定買單撤單中");
    strategy.OnOrderLifecycle(CancelPending(oldBuy.ClientOrderId, strategy.InstanceId, clock.Now));
    strategy.OnExecution(Fill(oldBuy.ClientOrderId, strategy.InstanceId, BuySell.Buy, 110m, clock.Now));
    strategy.OnOrderLifecycle(Cancelled(oldBuy.ClientOrderId, strategy.InstanceId, clock.Now));
    Assert(orders.Count(x => x.ClientOrderId.Contains("buy-g2", StringComparison.Ordinal)) == 0,
        "撤單競速期間已成交的舊世代不得再建立替代買單");
}

static void TestPeriodicStrategyCancelsBothOnConfirmedPreFillReversal()
{
    DateTimeOffset start = new(2026, 9, 22, 5, 0, 0, TimeSpan.Zero);
    (PeriodicQueueCycleStrategy strategy, MutableClock clock, List<PaperOrderRequest> orders) =
        StartWorkingUpStrategy("reverse", NoRepriceParameters(), start);
    var cancels = new List<string>();
    strategy.CancelOrderRequested += cancels.Add;

    var reverseBooks = new[]
    {
        StrategyBook(95m, 100m, 10, 20, 3, start.AddSeconds(2)),
        StrategyBook(85m, 90m, 10, 20, 4, start.AddSeconds(3)),
        StrategyBook(75m, 80m, 10, 20, 5, start.AddSeconds(4))
    };
    foreach (MarketEvent item in reverseBooks)
    {
        clock.Now = item.Timestamp;
        strategy.OnMarketEvent(item);
    }

    string[] originalIds = orders.Take(2).Select(x => x.ClientOrderId).OrderBy(x => x, StringComparer.Ordinal).ToArray();
    Assert(cancels.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(originalIds, StringComparer.Ordinal),
        "兩側都未成交且完整反向訊號連續三秒時必須定向撤銷雙邊");
    Assert(strategy.GetSnapshot().Reason == "SignalReversedBeforeFill",
        "反向撤單必須使用穩定原因碼 SignalReversedBeforeFill");
    Assert(orders.All(x => x.Role is not PaperOrderRole.StopLoss and not PaperOrderRole.Flatten),
        "成交前訊號反轉不是停損或中性平倉");
}

static void TestPeriodicStrategyHandlesLateFillFromReplacedGeneration()
{
    DateTimeOffset start = new(2026, 9, 22, 6, 0, 0, TimeSpan.Zero);
    (PeriodicQueueCycleStrategy strategy, MutableClock clock, List<PaperOrderRequest> orders) =
        StartWorkingUpStrategy("late-old-fill", RepriceParameters(), start);
    var cancels = new List<string>();
    strategy.CancelOrderRequested += cancels.Add;
    PaperOrderRequest oldBuy = orders.Single(x => x.Side == BuySell.Buy);

    clock.Now = start.AddSeconds(2);
    strategy.OnMarketEvent(StrategyBook(110m, 115m, 20, 10, 3, clock.Now));
    strategy.OnOrderLifecycle(Cancelled(oldBuy.ClientOrderId, strategy.InstanceId, clock.Now));
    PaperOrderRequest replacement = orders.Single(x => x.ClientOrderId.Contains("buy-g2", StringComparison.Ordinal));
    strategy.OnExecution(Fill(oldBuy.ClientOrderId, strategy.InstanceId, BuySell.Buy, oldBuy.Price, clock.Now.AddMilliseconds(1)));

    Assert(cancels.Contains(replacement.ClientOrderId, StringComparer.Ordinal),
        "舊世代在替代單送出後延遲成交時，必須撤銷目前替代世代以避免同側重複曝險");
    strategy.OnOrderLifecycle(Cancelled(replacement.ClientOrderId, strategy.InstanceId, clock.Now.AddMilliseconds(2)));
    Assert(orders.Count(x => x.Side == BuySell.Buy) == 2,
        "舊世代延遲成交後撤掉替代單不得再建立 g3");
}

static void TestPeriodicStrategyHandlesRejectedAndCancelFailedLifecycle()
{
    DateTimeOffset start = new(2026, 9, 22, 7, 0, 0, TimeSpan.Zero);
    (PeriodicQueueCycleStrategy rejected, MutableClock _, List<PaperOrderRequest> rejectedOrders) =
        StartWorkingUpStrategy("rejected", NoRepriceParameters(), start);
    var rejectedCancels = new List<string>();
    rejected.CancelOrderRequested += rejectedCancels.Add;
    PaperOrderRequest rejectedBuy = rejectedOrders.Single(x => x.Side == BuySell.Buy);
    PaperOrderRequest rejectedSell = rejectedOrders.Single(x => x.Side == BuySell.Sell);
    rejected.OnOrderLifecycle(Rejected(rejectedBuy.ClientOrderId, rejected.InstanceId, start.AddSeconds(2)));
    Assert(rejected.State == StrategyLifecycleState.Suspended &&
           rejected.GetSnapshot().Reason == "ClosingBlocked" &&
           rejectedCancels.SequenceEqual(new[] { rejectedSell.ClientOrderId }, StringComparer.Ordinal),
        "任一完成單被拒絕時必須停止後續交易並定向撤銷另一側，但不得誤標 Faulted");

    (PeriodicQueueCycleStrategy cancelFailed, MutableClock clock, List<PaperOrderRequest> orders) =
        StartWorkingUpStrategy("cancel-failed", RepriceParameters(), start.AddMinutes(1));
    var cancels = new List<string>();
    cancelFailed.CancelOrderRequested += cancels.Add;
    string buyId = orders.Single(x => x.Side == BuySell.Buy).ClientOrderId;
    clock.Now = start.AddMinutes(1).AddSeconds(2);
    MarketEvent repriceBook = StrategyBook(110m, 115m, 20, 10, 3, clock.Now);
    cancelFailed.OnMarketEvent(repriceBook);
    cancelFailed.OnOrderLifecycle(CancelFailed(buyId, cancelFailed.InstanceId, clock.Now));
    clock.Now = clock.Now.AddSeconds(1);
    cancelFailed.OnMarketEvent(repriceBook with { Timestamp = clock.Now, Sequence = 4 });
    Assert(cancels.Count(x => x == buyId) == 2 && orders.Count == 2,
        "CancelFailed 必須把該世代恢復為工作中，後續可重新要求撤單且不得提前重掛");
}

static void TestPeriodicStrategyCountsDistinctOppositeSeconds()
{
    DateTimeOffset start = new(2026, 9, 22, 8, 0, 0, TimeSpan.Zero);
    (PeriodicQueueCycleStrategy strategy, MutableClock clock, List<PaperOrderRequest> orders) =
        StartWorkingUpStrategy("same-second-reverse", NoRepriceParameters(), start);
    var cancels = new List<string>();
    strategy.CancelOrderRequested += cancels.Add;
    long sequence = 3;
    decimal bid = 95m;
    for (int second = 2; second <= 4; second++)
    {
        for (int update = 0; update < 2; update++)
        {
            clock.Now = start.AddSeconds(second).AddMilliseconds(update * 500);
            strategy.OnMarketEvent(StrategyBook(
                bid, bid + 5m, 10, 20, sequence++, clock.Now));
        }
        bid -= 10m;
    }

    Assert(cancels.Count == 2 &&
           cancels.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(
               orders.Take(2).Select(x => x.ClientOrderId).OrderBy(x => x, StringComparer.Ordinal),
               StringComparer.Ordinal),
        "同秒較新報價只能更新該秒判斷，不得重設或重複增加連續反向秒數");
}

static void TestPeriodicStrategyPreservesReentrantFirstLegFill()
{
    DateTimeOffset start = new(2026, 9, 22, 9, 0, 0, TimeSpan.Zero);
    var clock = new MutableClock(start);
    var strategy = (PeriodicQueueCycleStrategy)new PeriodicQueueCycleFactory().Create(
        PeriodicDefinition("reentrant-fill", NoRepriceParameters()));
    bool filled = false;
    strategy.OrderRequested += order =>
    {
        if (!filled && order.Side == BuySell.Buy)
        {
            filled = true;
            strategy.OnExecution(Fill(order.ClientOrderId, strategy.InstanceId, order.Side, order.Price, clock.Now));
        }
    };
    strategy.Start(PeriodicContext(clock));
    strategy.OnMarketEvent(StrategyBook(100m, 105m, 20, 10, 1, start));
    clock.Now = start.AddSeconds(1);
    strategy.OnMarketEvent(StrategyBook(110m, 115m, 20, 10, 2, clock.Now));

    Assert(filled && strategy.GetSnapshot().Reason == "OneLegFilled",
        "首張委託回呼同步成交時不得被 TryStart 結尾覆寫回雙邊工作狀態");
}

static void TestPeriodicStrategyHandlesSynchronousLateFillCancellation()
{
    DateTimeOffset start = new(2026, 9, 22, 10, 0, 0, TimeSpan.Zero);
    (PeriodicQueueCycleStrategy strategy, MutableClock clock, List<PaperOrderRequest> orders) =
        StartWorkingUpStrategy("sync-late-cancel", RepriceParameters(), start);
    var cancels = new List<string>();
    strategy.CancelOrderRequested += clientOrderId =>
    {
        cancels.Add(clientOrderId);
        if (clientOrderId.Contains("buy-g2", StringComparison.Ordinal))
        {
            strategy.OnOrderLifecycle(CancelPending(clientOrderId, strategy.InstanceId, clock.Now));
            strategy.OnOrderLifecycle(Cancelled(clientOrderId, strategy.InstanceId, clock.Now));
        }
    };
    PaperOrderRequest oldBuy = orders.Single(x => x.Side == BuySell.Buy);
    clock.Now = start.AddSeconds(2);
    strategy.OnMarketEvent(StrategyBook(110m, 115m, 20, 10, 3, clock.Now));
    strategy.OnOrderLifecycle(Cancelled(oldBuy.ClientOrderId, strategy.InstanceId, clock.Now));
    PaperOrderRequest replacement = orders.Single(x => x.ClientOrderId.Contains("buy-g2", StringComparison.Ordinal));
    strategy.OnExecution(Fill(oldBuy.ClientOrderId, strategy.InstanceId, BuySell.Buy, oldBuy.Price, clock.Now));
    strategy.OnOrderLifecycle(CancelFailed(replacement.ClientOrderId, strategy.InstanceId, clock.Now));
    clock.Now = clock.Now.AddSeconds(1);
    strategy.OnMarketEvent(StrategyBook(115m, 120m, 20, 10, 4, clock.Now));

    Assert(cancels.Count(x => x == replacement.ClientOrderId) == 1,
        "延遲成交撤銷替代單時必須先進入 CancelPending，才能接住同步 Cancelled 且不再重複撤單");
}

static void TestPeriodicStrategyFaultsOnDistinctGenerationOverfill()
{
    DateTimeOffset start = new(2026, 9, 22, 11, 0, 0, TimeSpan.Zero);
    (PeriodicQueueCycleStrategy strategy, MutableClock clock, List<PaperOrderRequest> orders) =
        StartWorkingUpStrategy("generation-overfill", RepriceParameters(), start);
    clock.Now = start.AddSeconds(2);
    strategy.OnMarketEvent(StrategyBook(110m, 115m, 20, 10, 3, clock.Now));
    PaperOrderRequest oldBuy = orders.Single(x => x.Side == BuySell.Buy);
    strategy.OnOrderLifecycle(Cancelled(oldBuy.ClientOrderId, strategy.InstanceId, clock.Now));
    PaperOrderRequest replacement = orders.Single(x => x.ClientOrderId.Contains("buy-g2", StringComparison.Ordinal));
    strategy.OnExecution(Fill(replacement.ClientOrderId, strategy.InstanceId, BuySell.Buy, replacement.Price, clock.Now));
    strategy.OnExecution(Fill(oldBuy.ClientOrderId, strategy.InstanceId, BuySell.Buy, oldBuy.Price, clock.Now.AddMilliseconds(1)));

    Assert(strategy.State == StrategyLifecycleState.Faulted &&
           strategy.GetSnapshot().Reason == "DuplicateGenerationFill",
        "同側兩個不同世代都真實成交時必須辨識為超額曝險並 Faulted，不得當成同一回報重複");
}

static void TestPeriodicStrategyPreservesReentrantInitialRejection()
{
    foreach (BuySell rejectedSide in new[] { BuySell.Buy, BuySell.Sell })
    {
        DateTimeOffset start = new(2026, 9, 22, 12, rejectedSide == BuySell.Buy ? 0 : 10, 0, TimeSpan.Zero);
        var clock = new MutableClock(start);
        var strategy = (PeriodicQueueCycleStrategy)new PeriodicQueueCycleFactory().Create(
            PeriodicDefinition($"reentrant-reject-{rejectedSide}", NoRepriceParameters()));
        var submitted = new List<PaperOrderRequest>();
        var cancels = new List<string>();
        var decisions = new List<StrategyDecisionObservation>();
        var causalEvents = new List<string>();
        ((IStrategyDecisionSource)strategy).DecisionObserved += decision =>
        {
            decisions.Add(decision);
            causalEvents.Add("decision:" + decision.PrimaryReasonCode);
        };
        strategy.CancelOrderRequested += cancels.Add;
        strategy.OrderRequested += order =>
        {
            submitted.Add(order);
            causalEvents.Add("submitted:" + order.Side);
            if (order.Side == rejectedSide)
                strategy.OnOrderLifecycle(Rejected(order.ClientOrderId, strategy.InstanceId, clock.Now));
        };
        strategy.Start(PeriodicContext(clock));
        strategy.OnMarketEvent(StrategyBook(100m, 105m, 20, 10, 1, start));
        clock.Now = start.AddSeconds(1);
        strategy.OnMarketEvent(StrategyBook(110m, 115m, 20, 10, 2, clock.Now));

        Assert(strategy.State == StrategyLifecycleState.Suspended &&
               strategy.GetSnapshot().Reason == "ClosingBlocked",
            $"初始 {rejectedSide} 同步拒絕不得被 TryStart 覆寫為繼續工作或 Faulted");
        Assert(decisions.Count(x => x.Decision == StrategyDecision.Accept &&
                                    x.PrimaryReasonCode == "SignalAccepted") == 1 &&
               causalEvents.IndexOf("decision:SignalAccepted") <
               causalEvents.FindIndex(x => x.StartsWith("submitted:", StringComparison.Ordinal)) &&
               decisions.Last().Decision == StrategyDecision.Halt &&
               decisions.Last().PrimaryReasonCode == "ClosingBlocked",
            $"初始 {rejectedSide} 必須先發布 SignalAccepted 意圖再送單；同步拒絕後最終決策轉為 ClosingBlocked");
        if (rejectedSide == BuySell.Buy)
            Assert(submitted.Count == 1 && cancels.Count == 0,
                "第一張買單同步拒絕後不得再送出或撤銷尚未送出的賣單");
        else
            Assert(submitted.Count == 2 && cancels.SequenceEqual(new[] { submitted[0].ClientOrderId }, StringComparer.Ordinal),
                "第二張賣單同步拒絕後必須只撤銷已送出的買單");
    }
}

static void TestPeriodicStrategyFaultsWhenExitCancellationFails()
{
    DateTimeOffset start = new(2026, 9, 22, 13, 0, 0, TimeSpan.Zero);
    (PeriodicQueueCycleStrategy strategy, MutableClock clock, List<PaperOrderRequest> orders) =
        StartWorkingUpStrategy("exit-cancel-failed", NoRepriceParameters(), start);
    var cancels = new List<string>();
    strategy.CancelOrderRequested += cancels.Add;
    var reverseBooks = new[]
    {
        StrategyBook(95m, 100m, 10, 20, 3, start.AddSeconds(2)),
        StrategyBook(85m, 90m, 10, 20, 4, start.AddSeconds(3)),
        StrategyBook(75m, 80m, 10, 20, 5, start.AddSeconds(4))
    };
    foreach (MarketEvent item in reverseBooks)
    {
        clock.Now = item.Timestamp;
        strategy.OnMarketEvent(item);
    }
    string failedId = cancels[0];
    strategy.OnOrderLifecycle(CancelFailed(failedId, strategy.InstanceId, clock.Now));

    Assert(strategy.State == StrategyLifecycleState.Suspended &&
           strategy.GetSnapshot().Reason == "ClosingBlocked" &&
           orders.Count == 2,
        "訊號反轉退出撤單失敗不得恢復工作或重掛，必須維持可重試的關閉封鎖");
}

static void TestPeriodicStrategyPreservesSynchronousReplacementRejectionReason()
{
    DateTimeOffset start = new(2026, 9, 22, 14, 0, 0, TimeSpan.Zero);
    (PeriodicQueueCycleStrategy strategy, MutableClock clock, List<PaperOrderRequest> orders) =
        StartWorkingUpStrategy("replacement-reject", RepriceParameters(), start);
    strategy.OrderRequested += order =>
    {
        if (order.ClientOrderId.Contains("buy-g2", StringComparison.Ordinal))
            strategy.OnOrderLifecycle(Rejected(order.ClientOrderId, strategy.InstanceId, clock.Now));
    };
    clock.Now = start.AddSeconds(2);
    strategy.OnMarketEvent(StrategyBook(110m, 115m, 20, 10, 3, clock.Now));
    string oldBuy = orders.Single(x => x.Side == BuySell.Buy).ClientOrderId;
    strategy.OnOrderLifecycle(Cancelled(oldBuy, strategy.InstanceId, clock.Now));

    Assert(strategy.State == StrategyLifecycleState.Suspended &&
           strategy.GetSnapshot().Reason == "ClosingBlocked",
        "替代單同步拒絕的終端原因不得被外層 ReplacementSubmitted 覆寫");
}

static void TestPeriodicStrategyPauseCancelsFlatPairAndKeepsPositionProtection()
{
    DateTimeOffset start = new(2026, 9, 22, 15, 0, 0, TimeSpan.Zero);
    (PeriodicQueueCycleStrategy flat, MutableClock _, List<PaperOrderRequest> flatOrders) =
        StartWorkingUpStrategy("pause-flat", NoRepriceParameters(), start);
    var flatCancels = new List<string>();
    flat.CancelOrderRequested += flatCancels.Add;
    flat.Pause("使用者暫停");
    Assert(flat.State == StrategyLifecycleState.Suspended &&
           flatCancels.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(
               flatOrders.Select(x => x.ClientOrderId).OrderBy(x => x, StringComparer.Ordinal),
               StringComparer.Ordinal),
        "空手但雙邊工作中 Pause 必須定向撤銷兩張 entry，不得只切換畫面狀態");

    var performance = new MutablePerformance();
    IReadOnlyDictionary<string, string> parameters = Values(
        ("DirectionWindowSeconds", "2"), ("ObiConfirmSeconds", "1"),
        ("MinSideDepth", "1"), ("MinNearDepth", "1"), ("MaxChaseTicks", "20"),
        ("TrendInitialAheadA", "5"), ("TrendRepriceThresholdB", "100000"), ("TrendRepriceTargetC", "1"),
        ("OppositeInitialAheadA", "20"), ("OppositeRepriceThresholdB", "15"), ("OppositeRepriceTargetC", "0"));
    (PeriodicQueueCycleStrategy held, MutableClock clock, List<PaperOrderRequest> heldOrders) =
        StartWorkingUpStrategy("pause-held", parameters, start.AddHours(1), performance);
    PaperOrderRequest buy = heldOrders.Single(x => x.Side == BuySell.Buy);
    performance.NetQuantity = 1;
    performance.AveragePrice = buy.Price;
    held.OnExecution(Fill(buy.ClientOrderId, held.InstanceId, BuySell.Buy, buy.Price, clock.Now));
    var heldCancels = new List<string>();
    held.CancelOrderRequested += heldCancels.Add;
    held.Pause("持倉暫停");
    Assert(heldCancels.Count == 0, "持倉 Pause 不得撤掉仍可中和部位的 peer 完成單");

    clock.Now = clock.Now.AddSeconds(1);
    held.OnMarketEvent(StrategyBook(110m, 115m, 20, 10, 3, clock.Now));
    string sellId = heldOrders.Single(x => x.Side == BuySell.Sell).ClientOrderId;
    Assert(heldCancels.SequenceEqual(new[] { sellId }, StringComparer.Ordinal),
        "持倉 Pause 後仍必須處理健康行情並保留 peer 的定向改掛生命週期");
    held.OnOrderLifecycle(Cancelled(sellId, held.InstanceId, clock.Now));
    Assert(heldOrders.Any(x => x.ClientOrderId.Contains("sell-g2", StringComparison.Ordinal)) &&
           held.State == StrategyLifecycleState.Suspended,
        "持倉 Pause 後仍必須接收 Cancelled 並建立 peer 新世代，同時維持 Suspended 公開狀態");
}

static void TestPeriodicStrategyPreservesFaultDuringReentrantExitCancelFailure()
{
    DateTimeOffset start = new(2026, 9, 22, 17, 0, 0, TimeSpan.Zero);
    (PeriodicQueueCycleStrategy strategy, MutableClock clock, List<PaperOrderRequest> _) =
        StartWorkingUpStrategy("reentrant-exit-cancel-fail", NoRepriceParameters(), start);
    var cancels = new List<string>();
    strategy.CancelOrderRequested += clientOrderId =>
    {
        cancels.Add(clientOrderId);
        if (cancels.Count == 1)
            strategy.OnOrderLifecycle(CancelFailed(clientOrderId, strategy.InstanceId, clock.Now));
    };
    foreach (MarketEvent item in new[]
    {
        StrategyBook(95m, 100m, 10, 20, 3, start.AddSeconds(2)),
        StrategyBook(85m, 90m, 10, 20, 4, start.AddSeconds(3)),
        StrategyBook(75m, 80m, 10, 20, 5, start.AddSeconds(4))
    })
    {
        clock.Now = item.Timestamp;
        strategy.OnMarketEvent(item);
    }

    Assert(cancels.Count == 2 && strategy.State == StrategyLifecycleState.Suspended &&
           strategy.GetSnapshot().Reason == "ClosingBlocked",
        "退出撤單第一腿同步 CancelFailed 時仍須盡力撤第二腿，且最後維持可重試的關閉封鎖");
    strategy.OnOrderLifecycle(CancelFailed(cancels[0], strategy.InstanceId, clock.Now));
    Assert(strategy.State == StrategyLifecycleState.Suspended &&
           strategy.GetSnapshot().Reason == "ClosingBlocked",
        "重複 CancelFailed 不得解除 ClosingBlocked 或重掛委託");
}

static void TestPeriodicStrategyDoesNotRepriceOnUnhealthyBook()
{
    DateTimeOffset start = new(2026, 9, 22, 18, 0, 0, TimeSpan.Zero);
    (PeriodicQueueCycleStrategy strategy, MutableClock clock, List<PaperOrderRequest> _) =
        StartWorkingUpStrategy("unhealthy-no-reprice", RepriceParameters(), start);
    var cancels = new List<string>();
    strategy.CancelOrderRequested += cancels.Add;
    clock.Now = start.AddSeconds(2);
    strategy.OnMarketEvent(StrategyBook(110m, 125m, 20, 10, 3, clock.Now));

    Assert(cancels.Count == 0 && strategy.GetSnapshot().Reason == "SpreadTooWide",
        "當前 Book 未通過任一健康閘門時不得改掛成更積極價格");
}

static void TestPeriodicStrategyStopsOnlyAfterOrdersAndPositionAreZero()
{
    DateTimeOffset start = new(2026, 9, 22, 19, 0, 0, TimeSpan.Zero);
    var flatPerformance = new MutablePerformance();
    (PeriodicQueueCycleStrategy flat, MutableClock clock, List<PaperOrderRequest> flatOrders) =
        StartWorkingUpStrategy("stop-flat", NoRepriceParameters(), start, flatPerformance);
    var cancels = new List<string>();
    flat.CancelOrderRequested += cancels.Add;
    flat.Stop("使用者停止");
    Assert(flat.State != StrategyLifecycleState.Stopped && cancels.Count == 2,
        "工作委託尚未確認撤銷前 Stop 不得宣告 Stopped");
    flat.OnOrderLifecycle(Cancelled(flatOrders[0].ClientOrderId, flat.InstanceId, clock.Now));
    Assert(flat.State != StrategyLifecycleState.Stopped, "只撤一腿時仍不得宣告 Stopped");
    flat.OnOrderLifecycle(Cancelled(flatOrders[1].ClientOrderId, flat.InstanceId, clock.Now));
    Assert(flat.State == StrategyLifecycleState.Stopped, "兩腿 exact Cancelled 且部位為零後才能 Stopped");
    flat.OnOrderLifecycle(Cancelled(flatOrders[1].ClientOrderId, flat.InstanceId, clock.Now));
    Assert(flat.State == StrategyLifecycleState.Stopped, "重複 Cancelled 不得破壞終止狀態");

    var heldPerformance = new MutablePerformance();
    (PeriodicQueueCycleStrategy held, MutableClock heldClock, List<PaperOrderRequest> heldOrders) =
        StartWorkingUpStrategy("stop-held", NoRepriceParameters(), start.AddHours(1), heldPerformance);
    heldPerformance.NetQuantity = 1;
    heldPerformance.AveragePrice = 110m;
    PaperOrderRequest heldBuy = heldOrders.Single(x => x.Side == BuySell.Buy);
    held.OnExecution(Fill(heldBuy.ClientOrderId, held.InstanceId, BuySell.Buy, heldBuy.Price, heldClock.Now));
    var heldCancels = new List<string>();
    held.CancelOrderRequested += heldCancels.Add;
    held.Stop("持倉停止");
    string heldSell = heldOrders.Single(x => x.Side == BuySell.Sell).ClientOrderId;
    held.OnOrderLifecycle(Cancelled(heldSell, held.InstanceId, heldClock.Now));
    Assert(held.State != StrategyLifecycleState.Stopped && heldPerformance.NetQuantity == 1,
        "即使工作單都終止，只要共用帳本部位不為零就不得宣告 Stopped");
}

static void TestPeriodicStrategySnapshotExposesFrozenCycleAndLegState()
{
    DateTimeOffset start = new(2026, 9, 22, 21, 0, 0, TimeSpan.Zero);
    (PeriodicQueueCycleStrategy strategy, MutableClock clock, List<PaperOrderRequest> orders) =
        StartWorkingUpStrategy("snapshot-cycle", RepriceParameters(), start);
    string initial = strategy.GetSnapshot().ReferenceSummary;
    Assert(initial.Contains("Cycle=cycle-1", StringComparison.Ordinal) &&
           initial.Contains("Direction=Up", StringComparison.Ordinal) &&
           initial.Contains("buy-g1", StringComparison.Ordinal) &&
           initial.Contains("sell-g1", StringComparison.Ordinal) &&
           initial.Contains("State=Working", StringComparison.Ordinal) &&
           initial.Contains("Intent=None", StringComparison.Ordinal),
        "快照必須揭露週期、凍結方向與雙腿世代/狀態/價格/撤單意圖");

    clock.Now = start.AddSeconds(2);
    strategy.OnMarketEvent(StrategyBook(110m, 115m, 20, 10, 3, clock.Now));
    string pending = strategy.GetSnapshot().ReferenceSummary;
    Assert(pending.Contains(orders.Single(x => x.Side == BuySell.Buy).ClientOrderId, StringComparison.Ordinal) &&
           pending.Contains("State=CancelPending", StringComparison.Ordinal) &&
           pending.Contains("Intent=Reprice", StringComparison.Ordinal),
        "快照必須在定向改掛期間揭露 current generation 與 cancel intent");
}

static void TestPeriodicStrategyFaultsWhenRunnerCannotRouteTargetedCancel()
{
    DateTimeOffset start = new(2026, 9, 22, 22, 0, 0, TimeSpan.Zero);
    var clock = new MutableClock(start);
    var broker = new UnroutableManagedExecutionService();
    var strategy = (PeriodicQueueCycleStrategy)new PeriodicQueueCycleFactory().Create(
        PeriodicDefinition("runner-unrouted", RepriceParameters()));
    using var runner = new StrategyInstanceRunner(
        strategy,
        new ResolvedStrategyUniverse
        {
            Tradable = new ResolvedProduct
            {
                ProductId = "225MC", Symbol = "225MC", TickSize = 5m, ContractMultiplier = 10m
            }
        },
        new KGIOptionPricing.StrategyFramework.Performance.PaperPortfolioLedger(),
        clock,
        0m,
        broker);
    runner.OnMarketEvent(StrategyBook(100m, 105m, 20, 10, 1, start));
    clock.Now = start.AddSeconds(1);
    runner.OnMarketEvent(StrategyBook(110m, 115m, 20, 10, 2, clock.Now));
    clock.Now = start.AddSeconds(2);
    runner.OnMarketEvent(StrategyBook(110m, 115m, 20, 10, 3, clock.Now));

    StrategySnapshot snapshot = runner.GetSnapshot();
    Assert(broker.CancelledClientOrderIds.Count == 1 &&
           snapshot.State == StrategyLifecycleState.Suspended &&
           snapshot.Reason == "ClosingBlocked",
        "真實 Runner 收到 targeted cancel=false 時必須封鎖策略並等待可重試關閉，不得誤標 Faulted");
}

static void TestPeriodicStrategyPreservesFaultDuringReentrantStopCancelFailure()
{
    DateTimeOffset start = new(2026, 9, 22, 23, 0, 0, TimeSpan.Zero);
    (PeriodicQueueCycleStrategy strategy, MutableClock clock, List<PaperOrderRequest> _) =
        StartWorkingUpStrategy("stop-reentrant-fail", NoRepriceParameters(), start);
    var cancels = new List<string>();
    strategy.CancelOrderRequested += clientOrderId =>
    {
        cancels.Add(clientOrderId);
        if (cancels.Count == 1)
            strategy.OnOrderLifecycle(CancelFailed(clientOrderId, strategy.InstanceId, clock.Now));
    };
    strategy.Stop("同步失敗停止");

    Assert(cancels.Count == 2 && strategy.State == StrategyLifecycleState.Suspended &&
           strategy.GetSnapshot().Reason == "ClosingBlocked",
        "Stop 第一腿同步撤單失敗時仍須嘗試第二腿，並維持可重試關閉封鎖");
}

static void TestPeriodicStrategyRetriesClosingBlockedCancellation()
{
    DateTimeOffset start = new(2026, 9, 23, 23, 30, 0, TimeSpan.Zero);
    (PeriodicQueueCycleStrategy strategy, MutableClock clock, List<PaperOrderRequest> orders) =
        StartWorkingUpStrategy("closing-blocked-retry", NoRepriceParameters(), start);
    var cancels = new List<string>();
    strategy.CancelOrderRequested += cancels.Add;

    strategy.Stop("第一次關閉");
    string first = orders[0].ClientOrderId;
    string second = orders[1].ClientOrderId;
    strategy.OnOrderLifecycle(CancelFailed(first, strategy.InstanceId, clock.Now));
    Assert(strategy.State == StrategyLifecycleState.Suspended &&
           strategy.GetSnapshot().Reason == "ClosingBlocked",
        "撤單失敗後必須停在 ClosingBlocked，而不是 Faulted");

    strategy.Stop("重試關閉");
    Assert(cancels.Count(x => x == first) == 2 && cancels.Count(x => x == second) == 1,
        "ClosingBlocked 的 Stop 重試只能再次撤銷失敗腿，不得重複取消已在 pending 的另一腿");
    strategy.OnOrderLifecycle(Cancelled(first, strategy.InstanceId, clock.Now));
    strategy.OnOrderLifecycle(Cancelled(second, strategy.InstanceId, clock.Now));
    Assert(strategy.State == StrategyLifecycleState.Stopped,
        "重試撤單收到全部終態且帳本為零後，策略必須安全停止");
}

static void TestPeriodicRunnerStopUsesTargetedManagedCancellation()
{
    DateTimeOffset start = new(2026, 9, 23, 0, 0, 0, TimeSpan.Zero);
    var clock = new MutableClock(start);
    var broker = new PendingManagedExecutionService();
    var strategy = (PeriodicQueueCycleStrategy)new PeriodicQueueCycleFactory().Create(
        PeriodicDefinition("runner-stop", NoRepriceParameters()));
    using var runner = new StrategyInstanceRunner(
        strategy,
        new ResolvedStrategyUniverse
        {
            Tradable = new ResolvedProduct
            {
                ProductId = "225MC", Symbol = "225MC", TickSize = 5m, ContractMultiplier = 10m
            }
        },
        new KGIOptionPricing.StrategyFramework.Performance.PaperPortfolioLedger(),
        clock,
        0m,
        broker);
    runner.OnMarketEvent(StrategyBook(100m, 105m, 20, 10, 1, start));
    clock.Now = start.AddSeconds(1);
    runner.OnMarketEvent(StrategyBook(110m, 115m, 20, 10, 2, clock.Now));
    runner.Stop("runner stop");

    Assert(broker.CancelAllCount == 0 && broker.CancelledClientOrderIds.Count == 2 &&
           runner.GetSnapshot().State != StrategyLifecycleState.Stopped,
        "managed strategy Runner.Stop 必須沿用 client-id targeted cancel，等待確認而非先做全撤並提前 Stopped");
    foreach (string clientOrderId in broker.CancelledClientOrderIds.ToArray())
        broker.ConfirmCancelled(strategy.InstanceId, clientOrderId, clock.Now);
    Assert(runner.GetSnapshot().State == StrategyLifecycleState.Stopped,
        "Runner.Stop 必須等兩張 targeted Cancelled 路由回 strategy 且部位為零才 Stopped");
}

static void TestPeriodicStrategyResumeStartsFreshCycleAfterFlatPause()
{
    DateTimeOffset start = new(2026, 9, 23, 1, 0, 0, TimeSpan.Zero);
    (PeriodicQueueCycleStrategy strategy, MutableClock clock, List<PaperOrderRequest> orders) =
        StartWorkingUpStrategy("pause-resume-cycle", NoRepriceParameters(), start);
    var cancels = new List<string>();
    strategy.CancelOrderRequested += cancels.Add;
    strategy.Pause("暫停後重啟");
    foreach (string clientOrderId in cancels.ToArray())
        strategy.OnOrderLifecycle(Cancelled(clientOrderId, strategy.InstanceId, clock.Now));
    strategy.Resume();
    Assert(strategy.State == StrategyLifecycleState.WarmingUp,
        "空手 Pause 的雙腿都 Cancelled 後 Resume 必須清除舊方向窗並重新 WarmingUp");

    clock.Now = start.AddSeconds(10);
    strategy.OnMarketEvent(StrategyBook(100m, 105m, 20, 10, 10, clock.Now));
    Assert(orders.Count == 2, "Resume 後第一秒不得沿用舊方向立即送新週期");
    clock.Now = start.AddSeconds(11);
    strategy.OnMarketEvent(StrategyBook(110m, 115m, 20, 10, 11, clock.Now));
    Assert(orders.Count == 4 && orders.Skip(2).All(x => x.ClientOrderId.Contains("cycle-2", StringComparison.Ordinal)),
        "Resume 完整重新暖機後必須能建立 cycle-2，不得卡在 CancellingForExit");
}

static void TestPeriodicStrategyStopsRepriceEvaluationAfterFirstRouteFailure()
{
    DateTimeOffset start = new(2026, 9, 23, 2, 0, 0, TimeSpan.Zero);
    IReadOnlyDictionary<string, string> parameters = Values(
        ("DirectionWindowSeconds", "2"), ("ObiConfirmSeconds", "1"),
        ("MinSideDepth", "1"), ("MinNearDepth", "1"), ("MaxChaseTicks", "20"),
        ("TrendInitialAheadA", "5"), ("TrendRepriceThresholdB", "15"), ("TrendRepriceTargetC", "0"),
        ("OppositeInitialAheadA", "5"), ("OppositeRepriceThresholdB", "15"), ("OppositeRepriceTargetC", "0"));
    var clock = new MutableClock(start);
    var broker = new FirstCancelFailsManagedExecutionService();
    var strategy = (PeriodicQueueCycleStrategy)new PeriodicQueueCycleFactory().Create(
        PeriodicDefinition("mixed-route", parameters));
    using var runner = new StrategyInstanceRunner(
        strategy,
        new ResolvedStrategyUniverse
        {
            Tradable = new ResolvedProduct
            {
                ProductId = "225MC", Symbol = "225MC", TickSize = 5m, ContractMultiplier = 10m
            }
        },
        new KGIOptionPricing.StrategyFramework.Performance.PaperPortfolioLedger(),
        clock,
        0m,
        broker);
    runner.OnMarketEvent(StrategyBook(100m, 105m, 20, 10, 1, start));
    clock.Now = start.AddSeconds(1);
    runner.OnMarketEvent(StrategyBook(110m, 115m, 20, 10, 2, clock.Now));
    clock.Now = start.AddSeconds(2);
    runner.OnMarketEvent(StrategyBook(110m, 115m, 20, 20, 3, clock.Now));

    StrategySnapshot snapshot = runner.GetSnapshot();
    Assert(broker.CancelAttempts.Count == 1 && snapshot.State == StrategyLifecycleState.Suspended &&
           snapshot.Reason == "ClosingBlocked",
        "第一腿 cancel=false 後必須立即停止評估第二腿，並轉為可重試的 ClosingBlocked");
}

static void TestPeriodicStrategyFaultsOnLateFillFromRetiredCycle()
{
    DateTimeOffset start = new(2026, 9, 23, 3, 0, 0, TimeSpan.Zero);
    (PeriodicQueueCycleStrategy strategy, MutableClock clock, List<PaperOrderRequest> orders) =
        StartWorkingUpStrategy("retired-late-fill", NoRepriceParameters(), start);
    var cancels = new List<string>();
    strategy.CancelOrderRequested += cancels.Add;
    strategy.Pause("退休週期測試");
    string retiredBuy = orders.Single(x => x.Side == BuySell.Buy).ClientOrderId;
    foreach (string clientOrderId in cancels.ToArray())
        strategy.OnOrderLifecycle(Cancelled(clientOrderId, strategy.InstanceId, clock.Now));
    strategy.Resume();
    strategy.OnExecution(Fill(retiredBuy, strategy.InstanceId, BuySell.Buy, 110m, clock.Now.AddMilliseconds(1)));
    Assert(strategy.State == StrategyLifecycleState.Faulted &&
           strategy.GetSnapshot().Reason == "LateFillRetiredCycle",
        "週期重設後仍必須保留退休 ClientOrderId tombstone，合法延遲成交要進入風險 Faulted");

    clock.Now = start.AddSeconds(10);
    strategy.OnMarketEvent(StrategyBook(100m, 105m, 20, 10, 10, clock.Now));
    clock.Now = start.AddSeconds(11);
    strategy.OnMarketEvent(StrategyBook(110m, 115m, 20, 10, 11, clock.Now));
    Assert(orders.Count == 2, "退休週期延遲成交後不得再建立 cycle-2");
}

static void TestPeriodicStrategyDoesNotStartCycleWithExistingPosition()
{
    DateTimeOffset start = new(2026, 9, 23, 4, 0, 0, TimeSpan.Zero);
    var performance = new MutablePerformance { NetQuantity = 1, AveragePrice = 100m };
    var clock = new MutableClock(start);
    var strategy = (PeriodicQueueCycleStrategy)new PeriodicQueueCycleFactory().Create(
        PeriodicDefinition("existing-position", NoRepriceParameters()));
    var orders = new List<PaperOrderRequest>();
    strategy.OrderRequested += orders.Add;
    strategy.Start(PeriodicContext(clock, performance));
    strategy.OnMarketEvent(StrategyBook(100m, 105m, 20, 10, 1, start));
    clock.Now = start.AddSeconds(1);
    strategy.OnMarketEvent(StrategyBook(110m, 115m, 20, 10, 2, clock.Now));

    Assert(orders.Count == 0 && strategy.State == StrategyLifecycleState.Suspended &&
           strategy.GetSnapshot().Reason == "PositionNotFlat",
        "共用績效顯示既有部位時不得啟動新週期或疊加雙邊 entry");
}

static void TestPeriodicStrategyFixedStopsUseLedgerAndWaitForPeerTerminal()
{
    foreach (BuySell filledSide in new[] { BuySell.Buy, BuySell.Sell })
    {
        DateTimeOffset start = new(2026, 9, 23, filledSide == BuySell.Buy ? 5 : 6, 0, 0, TimeSpan.Zero);
        var ledger = new KGIOptionPricing.StrategyFramework.Performance.PaperPortfolioLedger();
        IReadOnlyDictionary<string, string> parameters = RiskParameters();
        (PeriodicQueueCycleStrategy strategy, MutableClock clock, List<PaperOrderRequest> orders) =
            StartWorkingUpStrategy($"fixed-stop-{filledSide}", parameters, start, ledger);
        var cancels = new List<string>();
        strategy.CancelOrderRequested += cancels.Add;
        PaperOrderRequest entry = orders.Single(x => x.Side == filledSide);
        ApplyFill(strategy, ledger, entry, 200m, clock.Now);

        clock.Advance(clock.Now.AddSeconds(1));
        MarketEvent adverse = filledSide == BuySell.Buy
            ? StrategyBook(170m, 175m, 20, 10, 3, clock.Now)
            : StrategyBook(225m, 230m, 10, 20, 3, clock.Now);
        strategy.OnMarketEvent(adverse);

        string peerId = orders.Single(x => x.Side != filledSide).ClientOrderId;
        Assert(cancels.SequenceEqual(new[] { peerId }, StringComparer.Ordinal),
            $"{filledSide} 固定停損必須先定向撤銷 peer 完成單");
        Assert(orders.Count == 2, "peer 尚未 exact terminal 前不得發出停損單");

        strategy.OnOrderLifecycle(Cancelled(peerId, strategy.InstanceId, clock.Now));
        PaperOrderRequest exit = orders.Single(x => x.Role == PaperOrderRole.StopLoss);
        decimal expectedCounterpartyPrice = filledSide == BuySell.Buy ? adverse.BestBid : adverse.BestAsk;
        Assert(exit.Side != filledSide && exit.Quantity == 1 &&
               exit.Type == PaperOrderType.BrokerLimit && exit.Price == expectedCounterpartyPrice,
            $"{filledSide} 固定停損必須以共用帳本淨部位、對手價 BrokerLimit 發出恰一張反向單");
        Assert(strategy.GetSnapshot().Reason.Contains("FixedStopLoss", StringComparison.Ordinal),
            "固定反向價格退出必須使用穩定原因 FixedStopLoss");
        strategy.OnOrderLifecycle(Cancelled(peerId, strategy.InstanceId, clock.Now));
        Assert(orders.Count == 3, "重複 peer terminal 生命週期不得送出第二張停損單");
        ApplyFill(strategy, ledger, exit, filledSide == BuySell.Buy ? 170m : 230m, clock.Now);
        clock.Advance(clock.Now.AddSeconds(59));
        strategy.OnMarketEvent(StrategyBook(195m, 200m, 20, 10, 4, clock.Now));
        Assert(strategy.GetSnapshot().Reason == "CoolingDown", "固定停損後 59 秒仍須處於 60 秒冷卻");
        clock.Advance(clock.Now.AddSeconds(1));
        strategy.OnMarketEvent(StrategyBook(200m, 205m, 20, 10, 5, clock.Now));
        Assert(strategy.State == StrategyLifecycleState.WarmingUp,
            "固定停損 60 秒冷卻結束後必須清空舊方向窗重新暖機");
    }
}

static void TestPeriodicFixedStopWaitsForFreshCounterpartyQuote()
{
    DateTimeOffset start = new(2026, 9, 23, 6, 30, 0, TimeSpan.Zero);
    var ledger = new KGIOptionPricing.StrategyFramework.Performance.PaperPortfolioLedger();
    (PeriodicQueueCycleStrategy strategy, MutableClock clock, List<PaperOrderRequest> orders) =
        StartWorkingUpStrategy("fixed-stop-fresh-quote", RiskParameters(), start, ledger);
    var cancels = new List<string>();
    strategy.CancelOrderRequested += cancels.Add;
    PaperOrderRequest buy = orders.Single(x => x.Side == BuySell.Buy);
    ApplyFill(strategy, ledger, buy, 200m, clock.Now);

    clock.Advance(clock.Now.AddSeconds(1));
    strategy.OnMarketEvent(StrategyBook(170m, 175m, 20, 10, 3, clock.Now));
    string peerId = orders.Single(x => x.Side == BuySell.Sell).ClientOrderId;
    Assert(cancels.SequenceEqual(new[] { peerId }, StringComparer.Ordinal),
        "固定停損必須先撤銷 peer 完成單");

    clock.Advance(clock.Now.AddSeconds(3));
    strategy.OnOrderLifecycle(Cancelled(peerId, strategy.InstanceId, clock.Now));
    Assert(orders.Count == 2,
        "取消確認時的健康報價已過期，固定停損不得沿用舊對手價送出委託");

    strategy.OnMarketEvent(StrategyBook(165m, 170m, 20, 10, 4, clock.Now));
    PaperOrderRequest exit = orders.Single(x => x.Role == PaperOrderRole.StopLoss);
    Assert(exit.Type == PaperOrderType.BrokerLimit && exit.Side == BuySell.Sell && exit.Price == 165m,
        "下一筆健康行情到達後，固定停損必須以該筆最佳買價賣出");
}

static void TestPeriodicStrategyOneLegTimeoutHasExactBoundaryAndFlattens()
{
    DateTimeOffset start = new(2026, 9, 23, 7, 0, 0, TimeSpan.Zero);
    var ledger = new KGIOptionPricing.StrategyFramework.Performance.PaperPortfolioLedger();
    (PeriodicQueueCycleStrategy strategy, MutableClock clock, List<PaperOrderRequest> orders) =
        StartWorkingUpStrategy("one-leg-timeout", RiskParameters(), start, ledger);
    var decisions = new List<StrategyDecisionObservation>();
    ((IStrategyDecisionSource)strategy).DecisionObserved += decisions.Add;
    var cancels = new List<string>();
    strategy.CancelOrderRequested += cancels.Add;
    PaperOrderRequest buy = orders.Single(x => x.Side == BuySell.Buy);
    ApplyFill(strategy, ledger, buy, 200m, clock.Now);
    Assert(decisions.Last().Decision == StrategyDecision.Hold &&
           decisions.Last().PrimaryReasonCode == "OneLegFilled",
        "單邊成交後必須發布 Hold/OneLegFilled，不得重複 SignalAccepted");

    clock.Advance(clock.Now.AddSeconds(29));
    strategy.OnMarketEvent(StrategyBook(195m, 200m, 20, 10, 3, clock.Now));
    Assert(cancels.Count == 0, "單邊成交 29 秒不得提早逾時");

    clock.Advance(clock.Now.AddSeconds(1));
    strategy.OnMarketEvent(StrategyBook(195m, 200m, 20, 10, 4, clock.Now));
    string peerId = orders.Single(x => x.Side == BuySell.Sell).ClientOrderId;
    Assert(cancels.SequenceEqual(new[] { peerId }, StringComparer.Ordinal),
        "單邊成交滿 30 秒必須撤銷 peer");
    Assert(orders.Count == 2, "逾時退出仍須等待 peer exact terminal");

    strategy.OnOrderLifecycle(Cancelled(peerId, strategy.InstanceId, clock.Now));
    PaperOrderRequest exit = orders.Single(x => x.Role == PaperOrderRole.Flatten);
    Assert(exit.Side == BuySell.Sell && exit.Quantity == 1 &&
           strategy.GetSnapshot().Reason.Contains("OneLegTimeout", StringComparison.Ordinal),
        "逾時退出必須使用 Flatten/OneLegTimeout，不得誤標固定停損");
    Assert(decisions.Any(x => x.Decision == StrategyDecision.Exit &&
                              x.PrimaryReasonCode == "OneLegTimeout"),
        "單邊逾時必須保留 Exit/OneLegTimeout 決策證據");
}

static void TestPeriodicStrategyUnhealthyBookRecoversOrFlattensWithoutFalseStop()
{
    DateTimeOffset start = new(2026, 9, 23, 8, 0, 0, TimeSpan.Zero);
    var ledger = new KGIOptionPricing.StrategyFramework.Performance.PaperPortfolioLedger();
    (PeriodicQueueCycleStrategy strategy, MutableClock clock, List<PaperOrderRequest> orders) =
        StartWorkingUpStrategy("health-timeout", RiskParameters(), start, ledger);
    var cancels = new List<string>();
    strategy.CancelOrderRequested += cancels.Add;
    PaperOrderRequest buy = orders.Single(x => x.Side == BuySell.Buy);
    ApplyFill(strategy, ledger, buy, 200m, clock.Now);

    clock.Advance(clock.Now.AddSeconds(1));
    strategy.OnMarketEvent(StrategyBook(150m, 165m, 20, 10, 3, clock.Now));
    string peerId = orders.Single(x => x.Side == BuySell.Sell).ClientOrderId;
    Assert(cancels.SequenceEqual(new[] { peerId }, StringComparer.Ordinal) &&
           orders.All(x => x.Role != PaperOrderRole.StopLoss),
        "不健康 Book 必須停止改掛並撤 peer，但不得用壞價誤觸 StopLoss");
    strategy.OnOrderLifecycle(Cancelled(peerId, strategy.InstanceId, clock.Now));

    clock.Advance(clock.Now.AddSeconds(1));
    strategy.OnMarketEvent(StrategyBook(195m, 200m, 20, 10, 4, clock.Now));
    Assert(orders.Count == 2, "行情在 2 秒期限內恢復且未達固定停損時不得立即平倉");

    clock.Advance(start.AddSeconds(10));
    strategy.OnMarketEvent(StrategyBook(150m, 165m, 20, 10, 5, clock.Now));
    clock.Advance(clock.Now.AddSeconds(2));
    strategy.OnMarketEvent(StrategyBook(150m, 165m, 20, 10, 6, clock.Now));
    PaperOrderRequest exit = orders.Single(x => x.Role == PaperOrderRole.Flatten);
    Assert(exit.Role == PaperOrderRole.Flatten &&
           strategy.GetSnapshot().Reason.Contains("MarketDataRecoveryTimeout", StringComparison.Ordinal),
        "單邊持倉失去健康行情時必須中性平倉，不得誤標固定停損");
    ApplyFill(strategy, ledger, exit, 195m, clock.Now);
    clock.Advance(clock.Now.AddSeconds(44));
    strategy.OnMarketEvent(StrategyBook(195m, 200m, 20, 10, 7, clock.Now));
    Assert(strategy.GetSnapshot().Reason == "CoolingDown", "行情異常平倉後 44 秒仍須處於 45 秒冷卻");
    clock.Advance(clock.Now.AddSeconds(1));
    strategy.OnMarketEvent(StrategyBook(200m, 205m, 20, 10, 8, clock.Now));
    Assert(strategy.State == StrategyLifecycleState.WarmingUp,
        "行情異常平倉 45 秒冷卻結束後必須重新暖機");
}

static void TestPeriodicStrategyCancelFillRaceRereadsLedgerBeforeExit()
{
    DateTimeOffset start = new(2026, 9, 23, 9, 0, 0, TimeSpan.Zero);
    var ledger = new KGIOptionPricing.StrategyFramework.Performance.PaperPortfolioLedger();
    (PeriodicQueueCycleStrategy strategy, MutableClock clock, List<PaperOrderRequest> orders) =
        StartWorkingUpStrategy("risk-cancel-fill-race", RiskParameters(), start, ledger);
    var cancels = new List<string>();
    strategy.CancelOrderRequested += cancels.Add;
    PaperOrderRequest buy = orders.Single(x => x.Side == BuySell.Buy);
    PaperOrderRequest sell = orders.Single(x => x.Side == BuySell.Sell);
    ApplyFill(strategy, ledger, buy, 200m, clock.Now);
    clock.Advance(clock.Now.AddSeconds(1));
    strategy.OnMarketEvent(StrategyBook(170m, 175m, 20, 10, 3, clock.Now));
    Assert(cancels.SequenceEqual(new[] { sell.ClientOrderId }, StringComparer.Ordinal),
        "競速測試必須先進入 peer cancel pending");

    ApplyFill(strategy, ledger, sell, 200m, clock.Now);
    Assert(ledger.GetPosition(strategy.InstanceId, "225MC").NetQuantity == 0,
        "競速 peer fill 必須先由真實共用帳本中和部位");
    Assert(orders.Count == 2, "重讀帳本為零後不得再送多餘退出單");
}

static void TestPeriodicStrategyCooldownsClearDirectionWindow()
{
    DateTimeOffset start = new(2026, 9, 23, 10, 0, 0, TimeSpan.Zero);
    var ledger = new KGIOptionPricing.StrategyFramework.Performance.PaperPortfolioLedger();
    IReadOnlyDictionary<string, string> parameters = Values(
        ("DirectionWindowSeconds", "2"), ("ObiConfirmSeconds", "1"),
        ("MinSideDepth", "1"), ("MinNearDepth", "1"), ("MaxChaseTicks", "20"),
        ("CycleCooldownSeconds", "45"), ("StopLossCooldownSeconds", "60"),
        ("TrendInitialAheadA", "5"), ("TrendRepriceThresholdB", "100000"), ("TrendRepriceTargetC", "1"),
        ("OppositeInitialAheadA", "20"), ("OppositeRepriceThresholdB", "100000"), ("OppositeRepriceTargetC", "1"));
    (PeriodicQueueCycleStrategy strategy, MutableClock clock, List<PaperOrderRequest> orders) =
        StartWorkingUpStrategy("cycle-cooldown", parameters, start, ledger);
    var decisions = new List<StrategyDecisionObservation>();
    ((IStrategyDecisionSource)strategy).DecisionObserved += decisions.Add;
    PaperOrderRequest buy = orders.Single(x => x.Side == BuySell.Buy);
    PaperOrderRequest sell = orders.Single(x => x.Side == BuySell.Sell);
    ApplyFill(strategy, ledger, buy, 200m, clock.Now);
    ApplyFill(strategy, ledger, sell, 205m, clock.Now);
    Assert(strategy.GetSnapshot().Reason == "CoolingDown", "雙邊完成且帳本歸零後必須進入週期冷卻");
    StrategyDecisionObservation cooling = decisions.Last();
    Assert(!cooling.IsCandidate && cooling.CandidateId.Length == 0 && cooling.CycleId.Length == 0,
        "market/lifecycle 進入 CoolingDown 時必須清除舊 candidate correlation，避免空 cycle 重複計數");

    clock.Advance(clock.Now.AddSeconds(44));
    strategy.OnHeartbeat(clock.Now);
    Assert(orders.Count == 2 && strategy.GetSnapshot().Reason == "CoolingDown",
        "45 秒週期冷卻尚未結束時不得建立新單");
    Assert(!decisions.Last().IsCandidate && decisions.Last().CandidateId.Length == 0,
        "heartbeat 冷卻路徑同樣不得沿用舊 candidate ID");
    clock.Advance(clock.Now.AddSeconds(1));
    strategy.OnMarketEvent(StrategyBook(220m, 225m, 20, 10, 4, clock.Now));
    Assert(orders.Count == 2 && strategy.State == StrategyLifecycleState.WarmingUp,
        "冷卻結束必須清空舊方向窗，第一筆只能重新暖機");
    clock.Advance(clock.Now.AddSeconds(1));
    strategy.OnMarketEvent(StrategyBook(230m, 235m, 20, 10, 5, clock.Now));
    Assert(orders.Count == 4, "完整重新暖機後才能啟動下一週期");
}

static void TestPeriodicStrategyPauseProtectsHeldPositionAndStopFlattensSafely()
{
    DateTimeOffset start = new(2026, 9, 23, 11, 0, 0, TimeSpan.Zero);
    var ledger = new KGIOptionPricing.StrategyFramework.Performance.PaperPortfolioLedger();
    (PeriodicQueueCycleStrategy paused, MutableClock clock, List<PaperOrderRequest> pausedOrders) =
        StartWorkingUpStrategy("pause-risk", RiskParameters(), start, ledger);
    var pausedCancels = new List<string>();
    paused.CancelOrderRequested += pausedCancels.Add;
    PaperOrderRequest buy = pausedOrders.Single(x => x.Side == BuySell.Buy);
    ApplyFill(paused, ledger, buy, 200m, clock.Now);
    paused.Pause("持倉保護暫停");
    clock.Advance(clock.Now.AddSeconds(1));
    paused.OnMarketEvent(StrategyBook(170m, 175m, 20, 10, 3, clock.Now));
    string pausedPeer = pausedOrders.Single(x => x.Side == BuySell.Sell).ClientOrderId;
    paused.OnOrderLifecycle(Cancelled(pausedPeer, paused.InstanceId, clock.Now));
    Assert(paused.State == StrategyLifecycleState.Suspended &&
           pausedOrders.Count(x => x.Role == PaperOrderRole.StopLoss) == 1,
        "持倉 Pause 必須維持 Suspended 公開狀態，但固定停損保護仍須有效");

    DateTimeOffset stopStart = start.AddHours(1);
    var stopLedger = new KGIOptionPricing.StrategyFramework.Performance.PaperPortfolioLedger();
    (PeriodicQueueCycleStrategy stopped, MutableClock stopClock, List<PaperOrderRequest> stopOrders) =
        StartWorkingUpStrategy("safe-stop", RiskParameters(), stopStart, stopLedger);
    var stopCancels = new List<string>();
    stopped.CancelOrderRequested += stopCancels.Add;
    PaperOrderRequest stopBuy = stopOrders.Single(x => x.Side == BuySell.Buy);
    ApplyFill(stopped, stopLedger, stopBuy, 200m, stopClock.Now);
    stopped.Stop("安全關閉");
    Assert(stopped.State != StrategyLifecycleState.Stopped && stopOrders.Count == 2,
        "持倉 Stop 必須先等待 peer exact terminal，不得提前停止或送平倉");
    string stopPeer = stopOrders.Single(x => x.Side == BuySell.Sell).ClientOrderId;
    stopped.OnOrderLifecycle(Cancelled(stopPeer, stopped.InstanceId, stopClock.Now));
    PaperOrderRequest flatten = stopOrders.Single(x => x.Role == PaperOrderRole.Flatten);
    Assert(stopped.State != StrategyLifecycleState.Stopped && flatten.Quantity == 1,
        "Stop 撤單完成後必須依共用帳本送出恰一張行政 Flatten");
    ApplyFill(stopped, stopLedger, flatten, 195m, stopClock.Now);
    Assert(stopped.State == StrategyLifecycleState.Stopped &&
           stopLedger.GetPosition(stopped.InstanceId, "225MC").NetQuantity == 0,
        "Stop 只有在退出成交、委託為零且部位為零後才能安全關閉");
}

static void TestPeriodicStrategyAcceptsFilledLifecycleAfterExecutionStartsCooldown()
{
    DateTimeOffset start = new(2026, 9, 23, 13, 0, 0, TimeSpan.Zero);
    var ledger = new KGIOptionPricing.StrategyFramework.Performance.PaperPortfolioLedger();
    (PeriodicQueueCycleStrategy strategy, MutableClock clock, List<PaperOrderRequest> orders) =
        StartWorkingUpStrategy("filled-lifecycle-after-cooldown", RiskParameters(), start, ledger);
    PaperOrderRequest buy = orders.Single(x => x.Side == BuySell.Buy);
    PaperOrderRequest sell = orders.Single(x => x.Side == BuySell.Sell);
    ApplyFill(strategy, ledger, buy, 200m, clock.Now);
    ApplyFill(strategy, ledger, sell, 205m, clock.Now);
    Assert(strategy.GetSnapshot().Reason == "CoolingDown", "測試前置必須由第二腿 execution 進入冷卻");

    strategy.OnOrderLifecycle(new OrderLifecycleEvent
    {
        StrategyInstanceId = strategy.InstanceId,
        ClientOrderId = sell.ClientOrderId,
        Timestamp = clock.Now,
        State = OrderLifecycleState.Filled,
        Quantity = 1,
        FilledQuantity = 1
    });
    Assert(strategy.State != StrategyLifecycleState.Faulted &&
           strategy.GetSnapshot().Reason == "CoolingDown",
        "同一成交的後續 Filled 生命週期不得被誤判為退休週期的新延遲成交");
}

static void TestPeriodicStrategyIgnoresDuplicateBookForRiskTriggers()
{
    DateTimeOffset start = new(2026, 9, 23, 14, 0, 0, TimeSpan.Zero);
    var ledger = new KGIOptionPricing.StrategyFramework.Performance.PaperPortfolioLedger();
    (PeriodicQueueCycleStrategy strategy, MutableClock clock, List<PaperOrderRequest> orders) =
        StartWorkingUpStrategy("duplicate-risk-book", RiskParameters(), start, ledger);
    var cancels = new List<string>();
    strategy.CancelOrderRequested += cancels.Add;
    PaperOrderRequest buy = orders.Single(x => x.Side == BuySell.Buy);
    ApplyFill(strategy, ledger, buy, 200m, clock.Now);

    MarketEvent duplicate = StrategyBook(110m, 115m, 20, 10, 2, clock.Now);
    strategy.OnMarketEvent(duplicate);
    Assert(cancels.Count == 0 && orders.Count == 2,
        "重複或倒序 Book 必須直接忽略，不得當成行情中斷或拿其價格觸發停損");
}

static void TestPeriodicHeartbeatAdvancesTimeoutAndHealthRecoveryWithoutMarketEvents()
{
    DateTimeOffset start = new(2026, 9, 23, 15, 0, 0, TimeSpan.Zero);
    var timeoutLedger = new KGIOptionPricing.StrategyFramework.Performance.PaperPortfolioLedger();
    IReadOnlyDictionary<string, string> timeoutParameters = RiskParameters().Concat(
        new[] { new KeyValuePair<string, string>("QuoteFreshnessSeconds", "30") })
        .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
    (PeriodicQueueCycleStrategy timeout, MutableClock clock, List<PaperOrderRequest> timeoutOrders) =
        StartWorkingUpStrategy("heartbeat-timeout", timeoutParameters, start, timeoutLedger);
    var timeoutCancels = new List<string>();
    timeout.CancelOrderRequested += timeoutCancels.Add;
    ApplyFill(timeout, timeoutLedger, timeoutOrders.Single(x => x.Side == BuySell.Buy), 200m, clock.Now);
    clock.Advance(clock.Now.AddSeconds(29));
    ((IStrategyHeartbeat)timeout).OnHeartbeat(clock.Now);
    Assert(timeoutCancels.Count == 0, "無新行情時 heartbeat 29 秒不得提前單邊逾時");
    clock.Advance(clock.Now.AddSeconds(1));
    ((IStrategyHeartbeat)timeout).OnHeartbeat(clock.Now);
    string timeoutPeer = timeoutOrders.Single(x => x.Side == BuySell.Sell).ClientOrderId;
    Assert(timeoutCancels.SequenceEqual(new[] { timeoutPeer }, StringComparer.Ordinal),
        "無新行情時 heartbeat 滿 30 秒仍必須定向撤銷 peer");
    timeout.OnOrderLifecycle(Cancelled(timeoutPeer, timeout.InstanceId, clock.Now));
    Assert(timeoutOrders.Single(x => x.Role == PaperOrderRole.Flatten).Quantity == 1 &&
           timeout.GetSnapshot().Reason.Contains("OneLegTimeout", StringComparison.Ordinal),
        "heartbeat 單邊逾時必須送 Flatten/OneLegTimeout");

    DateTimeOffset healthStart = start.AddHours(1);
    var healthLedger = new KGIOptionPricing.StrategyFramework.Performance.PaperPortfolioLedger();
    (PeriodicQueueCycleStrategy health, MutableClock healthClock, List<PaperOrderRequest> healthOrders) =
        StartWorkingUpStrategy("heartbeat-health", RiskParameters(), healthStart, healthLedger);
    var healthCancels = new List<string>();
    health.CancelOrderRequested += healthCancels.Add;
    ApplyFill(health, healthLedger, healthOrders.Single(x => x.Side == BuySell.Buy), 200m, healthClock.Now);
    DateTimeOffset staleBoundary = healthClock.Now.AddSeconds(2);
    healthClock.Advance(staleBoundary);
    ((IStrategyHeartbeat)health).OnHeartbeat(healthClock.Now);
    Assert(healthCancels.Count == 0, "最新健康報價年齡恰等於 QuoteFreshnessSeconds 時仍屬有效");
    healthClock.Advance(staleBoundary.AddMilliseconds(1));
    ((IStrategyHeartbeat)health).OnHeartbeat(healthClock.Now);
    string healthPeer = healthOrders.Single(x => x.Side == BuySell.Sell).ClientOrderId;
    Assert(healthCancels.SequenceEqual(new[] { healthPeer }, StringComparer.Ordinal),
        "健康報價超過新鮮度後 heartbeat 必須立即撤 peer");
    health.OnOrderLifecycle(Cancelled(healthPeer, health.InstanceId, healthClock.Now));
    healthClock.Advance(staleBoundary.AddSeconds(2));
    ((IStrategyHeartbeat)health).OnHeartbeat(healthClock.Now);
    Assert(healthOrders.Single(x => x.Role == PaperOrderRole.Flatten).Quantity == 1 &&
           health.GetSnapshot().Reason.Contains("MarketDataRecoveryTimeout", StringComparison.Ordinal),
        "報價老化起算滿 MarketDataRecoverySeconds 後 heartbeat 必須中性平倉");
}

static void TestPeriodicLifecycleFirstWaitsForAuthoritativeExecutions()
{
    DateTimeOffset start = new(2026, 9, 23, 17, 0, 0, TimeSpan.Zero);
    var ledger = new KGIOptionPricing.StrategyFramework.Performance.PaperPortfolioLedger();
    (PeriodicQueueCycleStrategy strategy, MutableClock clock, List<PaperOrderRequest> orders) =
        StartWorkingUpStrategy("lifecycle-first", RiskParameters(), start, ledger);
    PaperOrderRequest buy = orders.Single(x => x.Side == BuySell.Buy);
    PaperOrderRequest sell = orders.Single(x => x.Side == BuySell.Sell);
    foreach (PaperOrderRequest order in new[] { buy, sell })
    {
        strategy.OnOrderLifecycle(new OrderLifecycleEvent
        {
            StrategyInstanceId = strategy.InstanceId,
            ClientOrderId = order.ClientOrderId,
            Timestamp = clock.Now,
            State = OrderLifecycleState.Filled,
            Quantity = 1,
            FilledQuantity = 1
        });
    }
    Assert(strategy.GetSnapshot().Reason != "CoolingDown" && strategy.State != StrategyLifecycleState.Faulted,
        "兩腿只有 Filled lifecycle、帳本仍為零時不得完成或退休週期");

    ApplyFill(strategy, ledger, buy, 200m, clock.Now);
    Assert(ledger.GetPosition(strategy.InstanceId, "225MC").NetQuantity == 1 &&
           strategy.GetSnapshot().Reason != "CoolingDown" && strategy.State != StrategyLifecycleState.Faulted,
        "後到 execution 形成部位時必須立即進入保護，不得落入 45 秒裸倉冷卻");
    ApplyFill(strategy, ledger, sell, 205m, clock.Now);
    Assert(strategy.GetSnapshot().Reason == "CoolingDown" &&
           ledger.GetPosition(strategy.InstanceId, "225MC").NetQuantity == 0,
        "只有兩筆權威 execution 使帳本歸零後才能完成週期");
}

static void TestPeriodicLifecycleFirstStopWaitsForExecutionAndProtection()
{
    DateTimeOffset start = new(2026, 9, 23, 17, 30, 0, TimeSpan.Zero);
    var ledger = new KGIOptionPricing.StrategyFramework.Performance.PaperPortfolioLedger();
    (PeriodicQueueCycleStrategy strategy, MutableClock clock, List<PaperOrderRequest> orders) =
        StartWorkingUpStrategy("lifecycle-first-stop", RiskParameters(), start, ledger);
    PaperOrderRequest buy = orders.Single(x => x.Side == BuySell.Buy);
    PaperOrderRequest sell = orders.Single(x => x.Side == BuySell.Sell);
    foreach (PaperOrderRequest order in new[] { buy, sell })
    {
        strategy.OnOrderLifecycle(new OrderLifecycleEvent
        {
            StrategyInstanceId = strategy.InstanceId,
            ClientOrderId = order.ClientOrderId,
            Timestamp = clock.Now,
            State = OrderLifecycleState.Filled,
            Quantity = 1,
            FilledQuantity = 1
        });
    }

    strategy.Stop("等待權威成交");
    Assert(strategy.State != StrategyLifecycleState.Stopped,
        "兩腿只有 Filled lifecycle、ledger=0 時 Stop 不得越過 unreconciled fill 宣告 Stopped");
    ApplyFill(strategy, ledger, buy, 200m, clock.Now);
    Assert(ledger.GetPosition(strategy.InstanceId, "225MC").NetQuantity == 1 &&
           strategy.State != StrategyLifecycleState.Stopped,
        "StopPending 後到 execution 形成部位時仍必須保留策略保護生命週期");
    clock.Advance(clock.Now.AddSeconds(30));
    ((IStrategyHeartbeat)strategy).OnHeartbeat(clock.Now);
    Assert(strategy.State != StrategyLifecycleState.Stopped && strategy.GetSnapshot().StopPrice == 170m &&
           strategy.GetSnapshot().Reason.Contains("StopRequested", StringComparison.Ordinal),
        "後到 execution 形成部位後 heartbeat 必須繼續維持固定停損與 StopRequested 保護，不得因誤 Stopped 忽略");
    ApplyFill(strategy, ledger, sell, 205m, clock.Now);
    Assert(strategy.State == StrategyLifecycleState.Stopped &&
           ledger.GetPosition(strategy.InstanceId, "225MC").NetQuantity == 0,
        "所有權威 executions 到齊且帳本歸零後才可完成 Stop");
}

static void TestPeriodicFixedStopDoesNotTriggerBelowSixTicks()
{
    foreach (BuySell side in new[] { BuySell.Buy, BuySell.Sell })
    {
        DateTimeOffset start = new(2026, 9, 23, side == BuySell.Buy ? 18 : 19, 0, 0, TimeSpan.Zero);
        var ledger = new KGIOptionPricing.StrategyFramework.Performance.PaperPortfolioLedger();
        (PeriodicQueueCycleStrategy strategy, MutableClock clock, List<PaperOrderRequest> orders) =
            StartWorkingUpStrategy($"sub-six-{side}", RiskParameters(), start, ledger);
        var cancels = new List<string>();
        strategy.CancelOrderRequested += cancels.Add;
        ApplyFill(strategy, ledger, orders.Single(x => x.Side == side), 200m, clock.Now);
        clock.Advance(clock.Now.AddSeconds(1));
        strategy.OnMarketEvent(side == BuySell.Buy
            ? StrategyBook(175m, 180m, 20, 10, 3, clock.Now)
            : StrategyBook(220m, 225m, 10, 20, 3, clock.Now));
        Assert(cancels.Count == 0 && orders.All(x => x.Role != PaperOrderRole.StopLoss),
            $"{side} 不利 5 ticks 不得提早觸發 6 ticks 固定停損");
    }
}

static void TestPeriodicExitCallbacksAreIdempotentOutOfOrder()
{
    DateTimeOffset start = new(2026, 9, 23, 20, 0, 0, TimeSpan.Zero);
    var ledger = new KGIOptionPricing.StrategyFramework.Performance.PaperPortfolioLedger();
    (PeriodicQueueCycleStrategy strategy, MutableClock clock, List<PaperOrderRequest> orders) =
        StartWorkingUpStrategy("exit-idempotence", RiskParameters(), start, ledger);
    PaperOrderRequest buy = orders.Single(x => x.Side == BuySell.Buy);
    ApplyFill(strategy, ledger, buy, 200m, clock.Now);
    clock.Advance(clock.Now.AddSeconds(1));
    strategy.OnMarketEvent(StrategyBook(170m, 175m, 20, 10, 3, clock.Now));
    string peer = orders.Single(x => x.Side == BuySell.Sell).ClientOrderId;
    strategy.OnOrderLifecycle(Cancelled(peer, strategy.InstanceId, clock.Now));
    PaperOrderRequest exit = orders.Single(x => x.Role == PaperOrderRole.StopLoss);
    ApplyFill(strategy, ledger, exit, 170m, clock.Now);
    strategy.OnOrderLifecycle(new OrderLifecycleEvent
    {
        StrategyInstanceId = strategy.InstanceId, ClientOrderId = exit.ClientOrderId,
        Timestamp = clock.Now, State = OrderLifecycleState.Filled, Quantity = 1, FilledQuantity = 1
    });
    strategy.OnExecution(Fill(exit.ClientOrderId, strategy.InstanceId, exit.Side, 170m, clock.Now) with
    {
        Role = PaperOrderRole.StopLoss
    });
    Assert(strategy.State != StrategyLifecycleState.Faulted && orders.Count == 3 &&
           strategy.GetSnapshot().Reason == "CoolingDown",
        "退出 execution/lifecycle 重複或亂序不得重複平倉或誤進 Faulted");
}

static void TestPeriodicHealthFlattenUsesNextTradePriceThroughRealPaperBroker()
{
    DateTimeOffset start = new(2026, 9, 23, 21, 0, 0, TimeSpan.Zero);
    var clock = new MutableClock(start);
    var ledger = new KGIOptionPricing.StrategyFramework.Performance.PaperPortfolioLedger();
    var broker = new KGIOptionPricing.StrategyFramework.Execution.CrossPricePaperExecutionService(0m, clock);
    var strategy = (PeriodicQueueCycleStrategy)new PeriodicQueueCycleFactory().Create(
        PeriodicDefinition("paper-health-gap", RiskParameters()));
    using var runner = new StrategyInstanceRunner(
        strategy,
        new ResolvedStrategyUniverse
        {
            Tradable = new ResolvedProduct
            {
                ProductId = "225MC", Symbol = "225MC", TickSize = 5m, ContractMultiplier = 10m
            }
        },
        ledger,
        clock,
        0m,
        broker);
    var executions = new List<ExecutionEvent>();
    runner.ExecutionOccurred += executions.Add;
    runner.OnMarketEvent(StrategyBook(100m, 105m, 20, 10, 1, start));
    clock.Advance(start.AddSeconds(1));
    runner.OnMarketEvent(StrategyBook(110m, 115m, 20, 10, 2, clock.Now));
    PaperOrder buy = broker.GetWorkingOrders(strategy.InstanceId).Single(x => x.Side == BuySell.Buy);
    clock.Advance(start.AddSeconds(2));
    runner.OnMarketEvent(new MarketEvent
    {
        Sequence = 3, Timestamp = clock.Now, Symbol = "225MC", ProductId = "225MC",
        Kind = MarketEventKind.Trade, LastPrice = buy.Price - 5m, TradeQuantity = 1
    });
    Assert(ledger.GetPosition(strategy.InstanceId, "225MC").NetQuantity == 1,
        "端到端測試前置必須只成交買進完成單");

    clock.Advance(start.AddSeconds(3).AddMilliseconds(1));
    runner.Pulse(clock.Now);
    clock.Advance(start.AddSeconds(5));
    runner.Pulse(clock.Now);
    Assert(broker.GetWorkingOrders(strategy.InstanceId).Single().Role == PaperOrderRole.Flatten,
        "行情恢復逾時後真實 paper broker 必須持有一張 Market Flatten");

    const decimal gapTrade = 70m;
    clock.Advance(start.AddSeconds(6));
    runner.OnMarketEvent(new MarketEvent
    {
        Sequence = 4, Timestamp = clock.Now, Symbol = "225MC", ProductId = "225MC",
        Kind = MarketEventKind.Trade, LastPrice = gapTrade, TradeQuantity = 1
    });
    ExecutionEvent flatten = executions.Single(x => x.Role == PaperOrderRole.Flatten);
    Assert(flatten.Price == gapTrade && ledger.GetPosition(strategy.InstanceId, "225MC").NetQuantity == 0,
        "行情缺口後 Market Flatten 必須按當下 Trade 價成交並由共用帳本歸零");
}

static IReadOnlyDictionary<string, string> RiskParameters() => Values(
    ("DirectionWindowSeconds", "2"), ("ObiConfirmSeconds", "1"),
    ("MinSideDepth", "1"), ("MinNearDepth", "1"), ("MaxVolatilityTicks", "100"),
    ("MaxChaseTicks", "20"), ("StopLossTicks", "6"), ("MaxOneLegSeconds", "30"),
    ("MarketDataRecoverySeconds", "2"),
    ("TrendInitialAheadA", "5"), ("TrendRepriceThresholdB", "100000"), ("TrendRepriceTargetC", "1"),
    ("OppositeInitialAheadA", "20"), ("OppositeRepriceThresholdB", "100000"), ("OppositeRepriceTargetC", "1"));

static void ApplyFill(
    PeriodicQueueCycleStrategy strategy,
    KGIOptionPricing.StrategyFramework.Performance.PaperPortfolioLedger ledger,
    PaperOrderRequest order,
    decimal price,
    DateTimeOffset timestamp)
{
    ExecutionEvent execution = Fill(order.ClientOrderId, strategy.InstanceId, order.Side, price, timestamp) with
    {
        Role = order.Role
    };
    ledger.ApplyExecution(execution);
    strategy.OnExecution(execution);
}

static (PeriodicQueueCycleStrategy Strategy, MutableClock Clock, List<PaperOrderRequest> Orders)
    StartWorkingUpStrategy(
        string instanceId,
        IReadOnlyDictionary<string, string> parameters,
        DateTimeOffset start,
        IStrategyPerformanceView? performance = null)
{
    var clock = new MutableClock(start);
    var strategy = (PeriodicQueueCycleStrategy)new PeriodicQueueCycleFactory().Create(
        PeriodicDefinition(instanceId, parameters));
    var orders = new List<PaperOrderRequest>();
    strategy.OrderRequested += orders.Add;
    strategy.Start(PeriodicContext(clock, performance));
    strategy.OnMarketEvent(StrategyBook(100m, 105m, 20, 10, 1, start));
    clock.Now = start.AddSeconds(1);
    strategy.OnMarketEvent(StrategyBook(110m, 115m, 20, 10, 2, clock.Now));
    Assert(orders.Count == 2, "測試前置必須建立雙邊完成單");
    return (strategy, clock, orders);
}

static IReadOnlyDictionary<string, string> RepriceParameters() => Values(
    ("DirectionWindowSeconds", "2"), ("ObiConfirmSeconds", "1"),
    ("DirectionReverseSeconds", "3"), ("MinSideDepth", "1"), ("MinNearDepth", "1"),
    ("MaxChaseTicks", "20"),
    ("TrendInitialAheadA", "5"), ("TrendRepriceThresholdB", "15"), ("TrendRepriceTargetC", "0"),
    ("OppositeInitialAheadA", "20"), ("OppositeRepriceThresholdB", "100000"), ("OppositeRepriceTargetC", "1"));

static IReadOnlyDictionary<string, string> NoRepriceParameters() => Values(
    ("DirectionWindowSeconds", "2"), ("ObiConfirmSeconds", "1"),
    ("DirectionReverseSeconds", "3"), ("MinSideDepth", "1"), ("MinNearDepth", "1"),
    ("MaxChaseTicks", "20"),
    ("TrendInitialAheadA", "5"), ("TrendRepriceThresholdB", "100000"), ("TrendRepriceTargetC", "1"),
    ("OppositeInitialAheadA", "20"), ("OppositeRepriceThresholdB", "100000"), ("OppositeRepriceTargetC", "1"));

static OrderLifecycleEvent CancelPending(string clientOrderId, string instanceId, DateTimeOffset timestamp) => new()
{
    StrategyInstanceId = instanceId,
    ClientOrderId = clientOrderId,
    Timestamp = timestamp,
    State = OrderLifecycleState.CancelPending,
    Quantity = 1,
    RemainingQuantity = 1
};

static OrderLifecycleEvent Cancelled(string clientOrderId, string instanceId, DateTimeOffset timestamp) => new()
{
    StrategyInstanceId = instanceId,
    ClientOrderId = clientOrderId,
    Timestamp = timestamp,
    State = OrderLifecycleState.Cancelled,
    Quantity = 1,
    RemainingQuantity = 1
};

static OrderLifecycleEvent Rejected(string clientOrderId, string instanceId, DateTimeOffset timestamp) => new()
{
    StrategyInstanceId = instanceId,
    ClientOrderId = clientOrderId,
    Timestamp = timestamp,
    State = OrderLifecycleState.Rejected,
    Quantity = 1,
    RemainingQuantity = 1,
    ReasonCode = "Rejected"
};

static OrderLifecycleEvent CancelFailed(string clientOrderId, string instanceId, DateTimeOffset timestamp) => new()
{
    StrategyInstanceId = instanceId,
    ClientOrderId = clientOrderId,
    Timestamp = timestamp,
    State = OrderLifecycleState.Working,
    Quantity = 1,
    RemainingQuantity = 1,
    ReasonCode = "CancelFailed"
};

static ExecutionEvent Fill(
    string clientOrderId,
    string instanceId,
    BuySell side,
    decimal price,
    DateTimeOffset timestamp) => new()
{
    StrategyInstanceId = instanceId,
    ClientOrderId = clientOrderId,
    Timestamp = timestamp,
    Symbol = "225MC",
    Side = side,
    Role = side == BuySell.Buy ? PaperOrderRole.EntryLong : PaperOrderRole.EntryShort,
    Price = price,
    Quantity = 1
};

static List<PaperOrderRequest> RunInitialPair(
    string instanceId,
    IReadOnlyDictionary<string, string> parameters,
    DateTimeOffset start,
    MarketEvent first,
    MarketEvent second)
{
    var clock = new MutableClock(start);
    var strategy = (PeriodicQueueCycleStrategy)new PeriodicQueueCycleFactory().Create(
        PeriodicDefinition(instanceId, parameters));
    var orders = new List<PaperOrderRequest>();
    strategy.OrderRequested += orders.Add;
    strategy.Start(PeriodicContext(clock));
    strategy.OnMarketEvent(first);
    clock.Now = start.AddSeconds(1);
    strategy.OnMarketEvent(second);
    return orders;
}

static StrategyDefinition PeriodicDefinition(string instanceId, IReadOnlyDictionary<string, string> parameters) => new()
{
    StrategyType = PeriodicQueueCycleFactory.TypeName,
    InstanceId = instanceId,
    Universe = new StrategyUniverse { Tradable = new ProductSelection("225MC", "225MC") },
    Parameters = parameters
};

static StrategyRuntimeContext PeriodicContext(
    IStrategyClock clock,
    IStrategyPerformanceView? performance = null) => new(
    new ResolvedStrategyUniverse
    {
        Tradable = new ResolvedProduct
        {
            ProductId = "225MC", Symbol = "225MC", TickSize = 5m, ContractMultiplier = 10m
        }
    },
    clock,
    performance ?? new ZeroPerformance());

static MarketEvent StrategyBook(
    decimal bestBid,
    decimal bestAsk,
    int bidQuantity,
    int askQuantity,
    long sequence,
    DateTimeOffset timestamp) => new()
{
    Sequence = sequence,
    Timestamp = timestamp,
    Symbol = "225MC",
    ProductId = "225MC",
    Kind = MarketEventKind.Book,
    BidPrices = Enumerable.Range(0, 10).Select(i => bestBid - i * 5m).ToArray(),
    AskPrices = Enumerable.Range(0, 10).Select(i => bestAsk + i * 5m).ToArray(),
    BidQuantities = Enumerable.Repeat(bidQuantity, 10).ToArray(),
    AskQuantities = Enumerable.Repeat(askQuantity, 10).ToArray()
};

static MarketEvent QueueBook(decimal tick, decimal bestBid, decimal bestAsk,
    int[] bidQuantities, int[] askQuantities)
{
    return new MarketEvent
    {
        Timestamp = new DateTimeOffset(2026, 9, 21, 4, 0, 0, TimeSpan.Zero),
        Sequence = 1,
        Symbol = "225MC",
        ProductId = "225MC",
        Kind = MarketEventKind.Book,
        BidPrices = Enumerable.Range(0, bidQuantities.Length).Select(i => bestBid - i * tick).ToArray(),
        BidQuantities = bidQuantities,
        AskPrices = Enumerable.Range(0, askQuantities.Length).Select(i => bestAsk + i * tick).ToArray(),
        AskQuantities = askQuantities
    };
}

static void AssertRejected(string reasonCode, MarketEvent item, DateTimeOffset now,
    PeriodicQueueCycleParameters parameters)
{
    var window = new DirectionWindow(5m, parameters);
    Assert(!window.TryAdd(item, now), $"{reasonCode} 測試事件應被拒絕");
    DirectionAssessment assessment = window.Evaluate(now, 5m, parameters);
    Assert(assessment.Direction == CycleDirection.Neutral && assessment.PrimaryReasonCode == reasonCode,
        $"預期拒絕原因 {reasonCode}，實際 {assessment.PrimaryReasonCode}");
    Assert(assessment.Thresholds.Count > 0, $"{reasonCode} 必須保留門檻結果");
}

static MarketEvent Book(decimal mid, int bidQuantity, int askQuantity, long sequence, DateTimeOffset timestamp,
    decimal spreadTicks = 1m)
{
    const decimal tick = 5m;
    decimal halfSpread = spreadTicks * tick / 2m;
    decimal bestBid = mid - halfSpread;
    decimal bestAsk = mid + halfSpread;
    return new MarketEvent
    {
        Sequence = sequence,
        Timestamp = timestamp,
        Symbol = "225MC",
        ProductId = "225MC",
        Kind = MarketEventKind.Book,
        BidPrices = Enumerable.Range(0, 10).Select(i => bestBid - i * tick).ToArray(),
        AskPrices = Enumerable.Range(0, 10).Select(i => bestAsk + i * tick).ToArray(),
        BidQuantities = Enumerable.Repeat(bidQuantity, 10).ToArray(),
        AskQuantities = Enumerable.Repeat(askQuantity, 10).ToArray()
    };
}

static void AssertNear(decimal actual, decimal expected, decimal tolerance, string message)
{
    if (Math.Abs(actual - expected) > tolerance)
    {
        throw new InvalidOperationException($"{message}：expected={expected}, actual={actual}");
    }
}

static IReadOnlyDictionary<string, string> Values(params (string Name, string Value)[] pairs) =>
    pairs.ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void AssertThrows<T>(Action action, string? message = null) where T : Exception
{
    try
    {
        action();
    }
    catch (T)
    {
        return;
    }

    throw new InvalidOperationException(message ?? $"預期拋出 {typeof(T).Name}");
}

sealed class MutableClock : IEventDrivenStrategyClock
{
    public MutableClock(DateTimeOffset now) => Now = now;
    public DateTimeOffset Now { get; set; }
    public void Advance(DateTimeOffset timestamp) => Now = timestamp;
}

sealed class ZeroPerformance : IStrategyPerformanceView
{
    public PositionSnapshot GetPosition(string strategyInstanceId, string symbol) => new()
    {
        StrategyInstanceId = strategyInstanceId,
        Symbol = symbol
    };
}

sealed class MutablePerformance : IStrategyPerformanceView
{
    public int NetQuantity { get; set; }
    public decimal AveragePrice { get; set; }

    public PositionSnapshot GetPosition(string strategyInstanceId, string symbol) => new()
    {
        StrategyInstanceId = strategyInstanceId,
        Symbol = symbol,
        NetQuantity = NetQuantity,
        AveragePrice = AveragePrice
    };
}

sealed class UnroutableManagedExecutionService : IManagedStrategyExecutionService
{
    private int _orderNumber;
    public List<string> CancelledClientOrderIds { get; } = new();

    public event Action<ExecutionEvent>? Filled { add { } remove { } }
    public event Action<string>? Failed { add { } remove { } }
    public event Action<OrderLifecycleEvent>? OrderLifecycleChanged { add { } remove { } }

    public PaperOrder Submit(PaperOrderRequest request, DateTimeOffset now) => new()
    {
        OrderId = $"UNROUTED-{++_orderNumber}",
        ClientOrderId = request.ClientOrderId,
        StrategyInstanceId = request.StrategyInstanceId,
        Symbol = request.Symbol,
        Side = request.Side,
        Type = request.Type,
        Role = request.Role,
        Price = request.Price,
        Quantity = request.Quantity,
        State = PaperOrderState.Working,
        CreatedAt = now
    };

    public bool CancelByClientOrderId(string strategyInstanceId, string clientOrderId)
    {
        CancelledClientOrderIds.Add(clientOrderId);
        return false;
    }

    public bool Cancel(string orderId) => false;
    public int CancelAll(string strategyInstanceId) => 0;
    public IReadOnlyList<PaperOrder> GetWorkingOrders(string strategyInstanceId) => Array.Empty<PaperOrder>();
    public void OnMarketEvent(MarketEvent marketEvent) { }
}

sealed class PendingManagedExecutionService : IManagedStrategyExecutionService
{
    private int _orderNumber;
    public int CancelAllCount { get; private set; }
    public List<string> CancelledClientOrderIds { get; } = new();

    public event Action<ExecutionEvent>? Filled { add { } remove { } }
    public event Action<string>? Failed { add { } remove { } }
    public event Action<OrderLifecycleEvent>? OrderLifecycleChanged;

    public PaperOrder Submit(PaperOrderRequest request, DateTimeOffset now) => new()
    {
        OrderId = $"PENDING-{++_orderNumber}",
        ClientOrderId = request.ClientOrderId,
        StrategyInstanceId = request.StrategyInstanceId,
        Symbol = request.Symbol,
        Side = request.Side,
        Type = request.Type,
        Role = request.Role,
        Price = request.Price,
        Quantity = request.Quantity,
        State = PaperOrderState.Working,
        CreatedAt = now
    };

    public bool CancelByClientOrderId(string strategyInstanceId, string clientOrderId)
    {
        CancelledClientOrderIds.Add(clientOrderId);
        return true;
    }

    public void ConfirmCancelled(string instanceId, string clientOrderId, DateTimeOffset timestamp) =>
        OrderLifecycleChanged?.Invoke(new OrderLifecycleEvent
        {
            Timestamp = timestamp,
            StrategyInstanceId = instanceId,
            ClientOrderId = clientOrderId,
            PreviousState = OrderLifecycleState.CancelPending,
            State = OrderLifecycleState.Cancelled,
            Quantity = 1,
            RemainingQuantity = 1
        });

    public bool Cancel(string orderId) => false;
    public int CancelAll(string strategyInstanceId) { CancelAllCount++; return 0; }
    public IReadOnlyList<PaperOrder> GetWorkingOrders(string strategyInstanceId) => Array.Empty<PaperOrder>();
    public void OnMarketEvent(MarketEvent marketEvent) { }
}

sealed class FirstCancelFailsManagedExecutionService : IManagedStrategyExecutionService
{
    private int _orderNumber;
    public List<string> CancelAttempts { get; } = new();

    public event Action<ExecutionEvent>? Filled { add { } remove { } }
    public event Action<string>? Failed { add { } remove { } }
    public event Action<OrderLifecycleEvent>? OrderLifecycleChanged { add { } remove { } }

    public PaperOrder Submit(PaperOrderRequest request, DateTimeOffset now) => new()
    {
        OrderId = $"MIXED-{++_orderNumber}", ClientOrderId = request.ClientOrderId,
        StrategyInstanceId = request.StrategyInstanceId, Symbol = request.Symbol,
        Side = request.Side, Type = request.Type, Role = request.Role,
        Price = request.Price, Quantity = request.Quantity,
        State = PaperOrderState.Working, CreatedAt = now
    };

    public bool CancelByClientOrderId(string strategyInstanceId, string clientOrderId)
    {
        CancelAttempts.Add(clientOrderId);
        return CancelAttempts.Count != 1;
    }

    public bool Cancel(string orderId) => false;
    public int CancelAll(string strategyInstanceId) => 0;
    public IReadOnlyList<PaperOrder> GetWorkingOrders(string strategyInstanceId) => Array.Empty<PaperOrder>();
    public void OnMarketEvent(MarketEvent marketEvent) { }
}
