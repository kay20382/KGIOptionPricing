using System.Globalization;
using KGIOptionPricing.OrderManagement.Core;
using KGIOptionPricing.Strategies.PeriodicQueueCycle;
using KGIOptionPricing.StrategyFramework;

TestParameterDefaultsAndSummary();
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
TestQueuePricingUsesFrozenInwardBounds();
TestQueuePricingHandlesZeroTargetOneTickSpreadAndInvalidBooks();
TestQueuePricingSupportsDifferentTickSizes();

Console.WriteLine("PeriodicQueueCycle smoke tests passed.");

static void TestParameterDefaultsAndSummary()
{
    PeriodicQueueCycleParameters p = PeriodicQueueCycleParameters.Parse(new Dictionary<string, string>());
    Assert(p.DirectionWindowSeconds == 15 && p.MinMoveTicks == 2m &&
           p.MinTrendEfficiency == 0.40m && p.MinDepthObi == 0.10m &&
           p.ObiConfirmSeconds == 3 && p.DirectionReverseSeconds == 3 &&
           p.MinSideDepth == 827 && p.MinNearDepth == 165 && p.MinDepthRatio == 0.35m &&
           p.MaxSpreadTicks == 2m && p.MaxVolatilityTicks == 5m &&
           p.QuoteFreshnessSeconds == 2m && p.MaxChaseTicks == 2m &&
           p.StopLossTicks == 6m && p.MaxOneLegSeconds == 30 &&
           p.CycleCooldownSeconds == 45 && p.StopLossCooldownSeconds == 60 &&
           p.ShockCooldownSeconds == 45 && p.MarketDataRecoverySeconds == 2m &&
           p.CommissionPerContract == 36m && !p.ResearchValidated &&
           p.TrendInitialAheadA == 22 && p.TrendRepriceThresholdB == 49 && p.TrendRepriceTargetC == 22 &&
           p.OppositeInitialAheadA == 240 && p.OppositeRepriceThresholdB == 312 && p.OppositeRepriceTargetC == 22,
        "週期策略預設參數不符設計規格");

    const string expected = "DirectionWindowSeconds=15;MinMoveTicks=2;MinTrendEfficiency=0.40;MinDepthObi=0.10;ObiConfirmSeconds=3;DirectionReverseSeconds=3;MinSideDepth=827;MinNearDepth=165;MinDepthRatio=0.35;MaxSpreadTicks=2;MaxVolatilityTicks=5;QuoteFreshnessSeconds=2;MaxChaseTicks=2;StopLossTicks=6;MaxOneLegSeconds=30;CycleCooldownSeconds=45;StopLossCooldownSeconds=60;ShockCooldownSeconds=45;MarketDataRecoverySeconds=2;CommissionPerContract=36;ResearchValidated=false;TrendInitialAheadA=22;TrendRepriceThresholdB=49;TrendRepriceTargetC=22;OppositeInitialAheadA=240;OppositeRepriceThresholdB=312;OppositeRepriceTargetC=22";
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
        ("MaxChaseTicks", "-0.01"), ("MaxChaseTicks", "20.01"),
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
        ("MaxChaseTicks lower", Values(("MaxChaseTicks", "0"))),
        ("MaxChaseTicks upper", Values(("MaxChaseTicks", "20"))),
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

static void TestQueueInitialPricingUsesCumulativeAheadSymmetrically()
{
    var policy = new QueueCyclePricingPolicy();
    MarketEvent book = QueueBook(5m, 100m, 110m, new[] { 10, 15, 20 }, new[] { 10, 15, 20 });
    FrozenPriceBounds bounds = QueueCyclePricingPolicy.CreateBounds(105m, 5m, 10m);

    QueuePriceDecision buy = policy.SelectInitial(BuySell.Buy, book, 22, bounds);
    QueuePriceDecision sell = policy.SelectInitial(BuySell.Sell, book, 22, bounds);

    Assert(buy.Action == QueuePriceAction.Place && buy.Price == 95m,
        $"買單 A 必須選在累積第 22 口所在價位 95，實際 {buy.Action}/{buy.Price}");
    Assert(sell.Action == QueuePriceAction.Place && sell.Price == 115m,
        $"賣單 A 必須對稱選在累積第 22 口所在價位 115，實際 {sell.Action}/{sell.Price}");
}

static void TestQueueRepriceRequiresStrictlyMoreThanThreshold()
{
    var policy = new QueueCyclePricingPolicy();
    MarketEvent book = QueueBook(5m, 100m, 110m, new[] { 10, 15, 20 }, new[] { 10, 15, 20 });
    FrozenPriceBounds bounds = QueueCyclePricingPolicy.CreateBounds(105m, 5m, 10m);

    QueuePriceDecision buyAtB = policy.EvaluateReprice(BuySell.Buy, book, 90m, 5, 30, 22, bounds);
    QueuePriceDecision buyAboveB = policy.EvaluateReprice(BuySell.Buy, book, 90m, 5, 29, 22, bounds);
    QueuePriceDecision sellAboveB = policy.EvaluateReprice(BuySell.Sell, book, 120m, 5, 29, 22, bounds);

    Assert(buyAtB.Action == QueuePriceAction.Hold && buyAtB.Price == 90m &&
           buyAtB.BetterPriceQuantity == 25 && buyAtB.SamePriceAheadQuantity == 5,
        "同價前方量加更佳價位量恰等於 B 時不得改掛");
    Assert(buyAboveB.Action == QueuePriceAction.Reprice && buyAboveB.Price == 95m,
        $"買單 totalAhead 大於 B 時必須依 C 改掛至 95，實際 {buyAboveB.Action}/{buyAboveB.Price}");
    Assert(sellAboveB.Action == QueuePriceAction.Reprice && sellAboveB.Price == 115m,
        $"賣單 totalAhead 大於 B 時必須對稱依 C 改掛至 115，實際 {sellAboveB.Action}/{sellAboveB.Price}");
}

static void TestQueueRepriceNeverBecomesLessAggressive()
{
    var policy = new QueueCyclePricingPolicy();
    MarketEvent book = QueueBook(5m, 100m, 110m, new[] { 10, 15, 20 }, new[] { 10, 15, 20 });
    FrozenPriceBounds bounds = QueueCyclePricingPolicy.CreateBounds(105m, 5m, 10m);

    QueuePriceDecision buy = policy.EvaluateReprice(BuySell.Buy, book, 100m, 50, 10, 22, bounds);
    QueuePriceDecision sell = policy.EvaluateReprice(BuySell.Sell, book, 110m, 50, 10, 22, bounds);

    Assert(buy.Action == QueuePriceAction.Hold && buy.Price == 100m && buy.ReasonCode == "PriceBoundaryReached",
        "買單 C 候選較低時不得往較不積極方向改掛");
    Assert(sell.Action == QueuePriceAction.Hold && sell.Price == 110m && sell.ReasonCode == "PriceBoundaryReached",
        "賣單 C 候選較高時不得往較不積極方向改掛");
}

static void TestQueuePricingUsesFrozenInwardBounds()
{
    var policy = new QueueCyclePricingPolicy();
    FrozenPriceBounds bounds = QueueCyclePricingPolicy.CreateBounds(102.5m, 5m, 2m);
    Assert(bounds.Anchor == 102.5m && bounds.BuyLimit == 110m && bounds.SellLimit == 95m,
        $"半 Tick 中價邊界取整錯誤：{bounds.Anchor}/{bounds.BuyLimit}/{bounds.SellLimit}");

    MarketEvent initialOutside = QueueBook(5m, 115m, 120m, new[] { 10 }, new[] { 10 });
    QueuePriceDecision clampedBuy = policy.SelectInitial(BuySell.Buy, initialOutside, 1, bounds);
    MarketEvent initialSellOutside = QueueBook(5m, 85m, 90m, new[] { 10 }, new[] { 10 });
    QueuePriceDecision clampedSell = policy.SelectInitial(BuySell.Sell, initialSellOutside, 1, bounds);
    Assert(clampedBuy.Action == QueuePriceAction.Place && clampedBuy.Price == 110m,
        "買單首次掛單越界時必須向內夾到凍結上界");
    Assert(clampedSell.Action == QueuePriceAction.Place && clampedSell.Price == 95m,
        "賣單首次掛單越界時必須向內夾到凍結下界");

    MarketEvent buyBook = QueueBook(5m, 100m, 110m, new[] { 10, 15 }, new[] { 10, 15 });
    FrozenPriceBounds exactBuyBounds = new(102.5m, 5m, 95m, 90m);
    QueuePriceDecision exactBuy = policy.EvaluateReprice(BuySell.Buy, buyBook, 90m, 40, 10, 22, exactBuyBounds);
    Assert(exactBuy.Action == QueuePriceAction.Reprice && exactBuy.Price == 95m,
        "買單改掛候選恰好命中凍結上界時應允許改掛");

    FrozenPriceBounds crossedBuyBounds = new(102.5m, 5m, 90m, 90m);
    QueuePriceDecision crossedBuy = policy.EvaluateReprice(BuySell.Buy, buyBook, 85m, 40, 10, 22, crossedBuyBounds);
    Assert(crossedBuy.Action == QueuePriceAction.Hold && crossedBuy.Price == 85m &&
           crossedBuy.ReasonCode == "PriceBoundaryReached",
        "買單改掛候選越過凍結上界時必須維持原價");

    FrozenPriceBounds originalBounds = bounds;
    _ = policy.SelectInitial(BuySell.Buy, QueueBook(5m, 130m, 135m, new[] { 10 }, new[] { 10 }), 1, bounds);
    Assert(bounds == originalBounds && bounds.BuyLimit == 110m && bounds.SellLimit == 95m,
        "市場移動後不得重算或移動週期價格邊界");
}

static void TestQueuePricingHandlesZeroTargetOneTickSpreadAndInvalidBooks()
{
    var policy = new QueueCyclePricingPolicy();
    FrozenPriceBounds bounds = QueueCyclePricingPolicy.CreateBounds(102.5m, 5m, 10m);
    MarketEvent oneTick = QueueBook(5m, 100m, 105m, new[] { 10 }, new[] { 10 });

    QueuePriceDecision buy = policy.SelectInitial(BuySell.Buy, oneTick, 0, bounds);
    QueuePriceDecision sell = policy.SelectInitial(BuySell.Sell, oneTick, 0, bounds);
    Assert(buy.Action == QueuePriceAction.Place && buy.Price == 105m,
        "A=0 且價差一跳時買單必須使用對手第一檔，不得穿價");
    Assert(sell.Action == QueuePriceAction.Place && sell.Price == 100m,
        "A=0 且價差一跳時賣單必須使用對手第一檔，不得穿價");

    MarketEvent invalid = oneTick with { AskPrices = Array.Empty<decimal>(), AskQuantities = Array.Empty<int>() };
    QueuePriceDecision invalidInitial = policy.SelectInitial(BuySell.Buy, invalid, 22, bounds);
    QueuePriceDecision invalidReprice = policy.EvaluateReprice(BuySell.Sell, invalid, 105m, 20, 10, 22, bounds);
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
    FrozenPriceBounds bounds = QueueCyclePricingPolicy.CreateBounds(101.25m, 2.5m, 1m);
    Assert(bounds.BuyLimit == 102.5m && bounds.SellLimit == 100m,
        $"2.5 Tick 的向內價格邊界錯誤：{bounds.BuyLimit}/{bounds.SellLimit}");

    MarketEvent book = QueueBook(2.5m, 100m, 102.5m, new[] { 10 }, new[] { 10 });
    QueuePriceDecision buy = policy.SelectInitial(BuySell.Buy, book, 0, bounds);
    QueuePriceDecision sell = policy.SelectInitial(BuySell.Sell, book, 0, bounds);
    Assert(buy.Price == 102.5m && sell.Price == 100m,
        $"政策必須使用 bounds.TickSize，不得寫死 5：{buy.Price}/{sell.Price}");
}

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
