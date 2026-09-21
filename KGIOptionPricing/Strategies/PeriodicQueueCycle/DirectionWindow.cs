using System;
using System.Collections.Generic;
using System.Linq;
using KGIOptionPricing.StrategyFramework;

namespace KGIOptionPricing.Strategies.PeriodicQueueCycle;

public enum CycleDirection { Neutral, Up, Down }

public sealed record ThresholdResult(string Name, decimal Observed, decimal Required, bool Passed);

public sealed record DirectionAssessment(
    CycleDirection Direction,
    decimal NetMoveTicks,
    decimal PathTicks,
    decimal TrendEfficiency,
    decimal AverageObi,
    decimal SpreadTicks,
    decimal VolatilityTicks,
    string PrimaryReasonCode,
    IReadOnlyList<string> OtherFailedReasonCodes,
    IReadOnlyList<ThresholdResult> Thresholds);

public sealed class DirectionWindow
{
    private readonly decimal _tickSize;
    private readonly PeriodicQueueCycleParameters _parameters;
    private readonly SortedDictionary<long, Sample> _samples = new();
    private Rejection? _lastRejection;

    public DirectionWindow(decimal tickSize, PeriodicQueueCycleParameters parameters)
    {
        if (tickSize <= 0m) throw new ArgumentOutOfRangeException(nameof(tickSize));
        _tickSize = tickSize;
        _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
    }

    public int SampleCount => _samples.Count;
    public long LatestSequence => _samples.Count == 0 ? 0L : _samples.Values.Last().Sequence;

    public bool TryAdd(MarketEvent item, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!TryCreateSample(item, out Sample? candidate))
        {
            _lastRejection = new Rejection(
                item.Timestamp,
                item.Sequence,
                "InvalidBook",
                Array.Empty<string>(),
                new[] { new ThresholdResult("ValidBook", 0m, 1m, false) },
                0m,
                0m);
            return false;
        }

        Sample next = candidate!;
        if (_samples.TryGetValue(next.UtcSecond, out Sample? sameSecond))
        {
            bool timeRegressed = next.Timestamp < sameSecond.Timestamp;
            bool sequenceRegressed = next.Sequence < sameSecond.Sequence;
            bool exactDuplicate = next.Timestamp == sameSecond.Timestamp && next.Sequence == sameSecond.Sequence;
            if (timeRegressed || sequenceRegressed || exactDuplicate)
            {
                return false;
            }
        }
        else if (_samples.Count > 0)
        {
            Sample latest = _samples.Values.Last();
            if (next.Timestamp <= latest.Timestamp || next.Sequence <= latest.Sequence)
            {
                return false;
            }
        }

        List<ThresholdResult> healthThresholds = HealthThresholds(next, now, _tickSize, _parameters, includeProspective: true);
        List<string> failures = HealthFailures(healthThresholds);
        if (failures.Count > 0)
        {
            decimal volatility = ThresholdObserved(healthThresholds, "MaxVolatilityTicks");
            _lastRejection = new Rejection(
                next.Timestamp,
                next.Sequence,
                failures[0],
                failures.Skip(1).ToArray(),
                healthThresholds,
                next.SpreadPrice / _tickSize,
                volatility);
            return false;
        }

        _samples[next.UtcSecond] = next;
        long oldestSecond = next.UtcSecond - _parameters.DirectionWindowSeconds + 1L;
        foreach (long key in _samples.Keys.Where(key => key < oldestSecond).ToArray())
        {
            _samples.Remove(key);
        }

        _lastRejection = null;
        return true;
    }

    public DirectionAssessment Evaluate(
        DateTimeOffset now,
        decimal tickSize,
        PeriodicQueueCycleParameters parameters)
    {
        if (tickSize <= 0m) throw new ArgumentOutOfRangeException(nameof(tickSize));
        ArgumentNullException.ThrowIfNull(parameters);

        Sample? latest = _samples.Count == 0 ? null : _samples.Values.Last();
        if (_lastRejection is not null && (latest is null || _lastRejection.Timestamp >= latest.Timestamp))
        {
            return Rejected(_lastRejection);
        }

        if (latest is null)
        {
            return Assessment(
                "WarmupIncomplete",
                Array.Empty<string>(),
                new[] { new ThresholdResult("DirectionWindowSeconds", 0m, parameters.DirectionWindowSeconds, false) });
        }

        List<ThresholdResult> healthThresholds = HealthThresholds(latest, now, tickSize, parameters, includeProspective: false);
        List<string> healthFailures = HealthFailures(healthThresholds);
        if (healthFailures.Count > 0)
        {
            return Assessment(
                healthFailures[0],
                healthFailures.Skip(1).ToArray(),
                healthThresholds,
                spreadTicks: latest.SpreadPrice / tickSize,
                volatilityTicks: ThresholdObserved(healthThresholds, "MaxVolatilityTicks"));
        }

        long firstRequiredSecond = latest.UtcSecond - parameters.DirectionWindowSeconds + 1L;
        List<Sample> window = _samples.Values
            .Where(sample => sample.UtcSecond >= firstRequiredSecond && sample.UtcSecond <= latest.UtcSecond)
            .OrderBy(sample => sample.UtcSecond)
            .ToList();
        int consecutiveCount = 0;
        long expectedSecond = latest.UtcSecond;
        for (int index = window.Count - 1; index >= 0; index--)
        {
            if (window[index].UtcSecond != expectedSecond)
            {
                break;
            }

            consecutiveCount++;
            expectedSecond--;
        }

        bool consecutive = consecutiveCount == parameters.DirectionWindowSeconds;
        var thresholds = new List<ThresholdResult>(healthThresholds)
        {
            new("DirectionWindowSeconds", consecutiveCount, parameters.DirectionWindowSeconds, consecutive)
        };
        if (!consecutive)
        {
            return Assessment(
                "WarmupIncomplete",
                Array.Empty<string>(),
                thresholds,
                spreadTicks: latest.SpreadPrice / tickSize,
                volatilityTicks: VolatilityTicks(window, tickSize));
        }

        decimal netMoveTicks = (window[^1].MidPrice - window[0].MidPrice) / tickSize;
        decimal pathTicks = 0m;
        for (int index = 1; index < window.Count; index++)
        {
            pathTicks += Math.Abs(window[index].MidPrice - window[index - 1].MidPrice) / tickSize;
        }

        decimal efficiency = Math.Abs(netMoveTicks) / Math.Max(pathTicks, 1m);
        decimal averageObi = window
            .TakeLast(parameters.ObiConfirmSeconds)
            .Average(sample => sample.DepthObi);
        decimal volatilityTicks = VolatilityTicks(window, tickSize);

        bool upMovePassed = netMoveTicks >= parameters.MinMoveTicks;
        bool downMovePassed = netMoveTicks <= -parameters.MinMoveTicks;
        bool efficiencyPassed = efficiency >= parameters.MinTrendEfficiency;
        bool upObiPassed = averageObi >= parameters.MinDepthObi;
        bool downObiPassed = averageObi <= -parameters.MinDepthObi;
        thresholds.Add(new ThresholdResult("UpNetMoveTicks", netMoveTicks, parameters.MinMoveTicks, upMovePassed));
        thresholds.Add(new ThresholdResult("DownNetMoveTicks", -netMoveTicks, parameters.MinMoveTicks, downMovePassed));
        thresholds.Add(new ThresholdResult("MinTrendEfficiency", efficiency, parameters.MinTrendEfficiency, efficiencyPassed));
        thresholds.Add(new ThresholdResult("UpMinDepthObi", averageObi, parameters.MinDepthObi, upObiPassed));
        thresholds.Add(new ThresholdResult("DownMinDepthObi", -averageObi, parameters.MinDepthObi, downObiPassed));

        if (upMovePassed && efficiencyPassed && upObiPassed)
        {
            return new DirectionAssessment(
                CycleDirection.Up, netMoveTicks, pathTicks, efficiency, averageObi,
                latest.SpreadPrice / tickSize, volatilityTicks, "SignalAccepted", Array.Empty<string>(), thresholds);
        }

        if (downMovePassed && efficiencyPassed && downObiPassed)
        {
            return new DirectionAssessment(
                CycleDirection.Down, netMoveTicks, pathTicks, efficiency, averageObi,
                latest.SpreadPrice / tickSize, volatilityTicks, "SignalAccepted", Array.Empty<string>(), thresholds);
        }

        var directionFailures = new List<string>();
        if (!upMovePassed && !downMovePassed) directionFailures.Add("NetMoveBelowThreshold");
        if (!efficiencyPassed) directionFailures.Add("TrendEfficiencyBelowThreshold");
        bool obiForObservedMovePassed = netMoveTicks < 0m ? downObiPassed : upObiPassed;
        if (!obiForObservedMovePassed) directionFailures.Add("DepthObiBelowThreshold");
        return new DirectionAssessment(
            CycleDirection.Neutral, netMoveTicks, pathTicks, efficiency, averageObi,
            latest.SpreadPrice / tickSize, volatilityTicks, "NeutralDirection", directionFailures, thresholds);
    }

    private List<ThresholdResult> HealthThresholds(
        Sample sample,
        DateTimeOffset now,
        decimal tickSize,
        PeriodicQueueCycleParameters parameters,
        bool includeProspective)
    {
        decimal quoteAgeSeconds = Math.Max(0m, (decimal)(now - sample.Timestamp).Ticks / TimeSpan.TicksPerSecond);
        decimal minSideDepth = Math.Min(sample.BidDepth, sample.AskDepth);
        decimal minNearDepth = Math.Min(sample.BidNearDepth, sample.AskNearDepth);
        decimal maxSideDepth = Math.Max(sample.BidDepth, sample.AskDepth);
        decimal depthRatio = maxSideDepth == 0m ? 0m : minSideDepth / maxSideDepth;
        decimal volatilityTicks = includeProspective
            ? ProspectiveVolatilityTicks(sample, tickSize, parameters.DirectionWindowSeconds)
            : CurrentVolatilityTicks(tickSize, parameters.DirectionWindowSeconds);

        return new List<ThresholdResult>
        {
            new("QuoteFreshnessSeconds", quoteAgeSeconds, parameters.QuoteFreshnessSeconds, quoteAgeSeconds <= parameters.QuoteFreshnessSeconds),
            new("MinSideDepth", minSideDepth, parameters.MinSideDepth, minSideDepth >= parameters.MinSideDepth),
            new("MinNearDepth", minNearDepth, parameters.MinNearDepth, minNearDepth >= parameters.MinNearDepth),
            new("MinDepthRatio", depthRatio, parameters.MinDepthRatio, depthRatio >= parameters.MinDepthRatio),
            new("MaxSpreadTicks", sample.SpreadPrice / tickSize, parameters.MaxSpreadTicks, sample.SpreadPrice / tickSize <= parameters.MaxSpreadTicks),
            new("MaxVolatilityTicks", volatilityTicks, parameters.MaxVolatilityTicks, volatilityTicks <= parameters.MaxVolatilityTicks)
        };
    }

    private static List<string> HealthFailures(IEnumerable<ThresholdResult> thresholds)
    {
        var failures = new List<string>();
        foreach (ThresholdResult threshold in thresholds)
        {
            if (threshold.Passed) continue;
            failures.Add(threshold.Name switch
            {
                "QuoteFreshnessSeconds" => "StaleQuote",
                "MinSideDepth" => "InsufficientSideDepth",
                "MinNearDepth" => "InsufficientNearDepth",
                "MinDepthRatio" => "DepthImbalance",
                "MaxSpreadTicks" => "SpreadTooWide",
                "MaxVolatilityTicks" => "VolatilityTooHigh",
                _ => "InvalidBook"
            });
        }

        return failures;
    }

    private decimal ProspectiveVolatilityTicks(Sample candidate, decimal tickSize, int windowSeconds)
    {
        long firstSecond = candidate.UtcSecond - windowSeconds + 1L;
        List<Sample> prospective = _samples.Values
            .Where(sample => sample.UtcSecond >= firstSecond && sample.UtcSecond <= candidate.UtcSecond && sample.UtcSecond != candidate.UtcSecond)
            .ToList();
        prospective.Add(candidate);
        return VolatilityTicks(prospective, tickSize);
    }

    private decimal CurrentVolatilityTicks(decimal tickSize, int windowSeconds)
    {
        if (_samples.Count == 0) return 0m;
        long latestSecond = _samples.Keys.Last();
        long firstSecond = latestSecond - windowSeconds + 1L;
        return VolatilityTicks(_samples.Values.Where(sample => sample.UtcSecond >= firstSecond).ToArray(), tickSize);
    }

    private static decimal VolatilityTicks(IReadOnlyCollection<Sample> samples, decimal tickSize)
    {
        if (samples.Count == 0) return 0m;
        decimal minimum = samples.Min(sample => sample.MidPrice);
        decimal maximum = samples.Max(sample => sample.MidPrice);
        return (maximum - minimum) / tickSize;
    }

    private static bool TryCreateSample(MarketEvent item, out Sample? sample)
    {
        sample = null;
        if (item.Kind != MarketEventKind.Book ||
            item.BidPrices.Length < 10 || item.AskPrices.Length < 10 ||
            item.BidQuantities.Length < 10 || item.AskQuantities.Length < 10)
        {
            return false;
        }

        decimal bidDepth = 0m;
        decimal askDepth = 0m;
        decimal bidNear = 0m;
        decimal askNear = 0m;
        decimal weightedBid = 0m;
        decimal weightedAsk = 0m;
        for (int level = 0; level < 10; level++)
        {
            decimal bidPrice = item.BidPrices[level];
            decimal askPrice = item.AskPrices[level];
            int bidQuantity = item.BidQuantities[level];
            int askQuantity = item.AskQuantities[level];
            if (bidPrice <= 0m || askPrice <= 0m || bidQuantity <= 0 || askQuantity <= 0) return false;
            if (level > 0 &&
                (bidPrice >= item.BidPrices[level - 1] || askPrice <= item.AskPrices[level - 1])) return false;

            bidDepth += bidQuantity;
            askDepth += askQuantity;
            if (level < 3)
            {
                bidNear += bidQuantity;
                askNear += askQuantity;
            }

            decimal weight = 1m / (level + 1m);
            weightedBid += bidQuantity * weight;
            weightedAsk += askQuantity * weight;
        }

        decimal bestBid = item.BidPrices[0];
        decimal bestAsk = item.AskPrices[0];
        if (bestBid >= bestAsk) return false;
        decimal weightedTotal = weightedBid + weightedAsk;
        if (weightedTotal <= 0m) return false;

        DateTimeOffset utcTimestamp = item.Timestamp.ToUniversalTime();
        sample = new Sample(
            utcTimestamp.Ticks / TimeSpan.TicksPerSecond,
            item.Sequence,
            item.Timestamp,
            (bestBid + bestAsk) / 2m,
            (weightedBid - weightedAsk) / weightedTotal,
            bidDepth,
            askDepth,
            bidNear,
            askNear,
            bestAsk - bestBid);
        return true;
    }

    private DirectionAssessment Rejected(Rejection rejection) => Assessment(
        rejection.PrimaryReasonCode,
        rejection.OtherFailedReasonCodes,
        rejection.Thresholds,
        spreadTicks: rejection.SpreadTicks,
        volatilityTicks: rejection.VolatilityTicks);

    private static DirectionAssessment Assessment(
        string primaryReasonCode,
        IReadOnlyList<string> otherFailedReasonCodes,
        IReadOnlyList<ThresholdResult> thresholds,
        decimal netMoveTicks = 0m,
        decimal pathTicks = 0m,
        decimal trendEfficiency = 0m,
        decimal averageObi = 0m,
        decimal spreadTicks = 0m,
        decimal volatilityTicks = 0m) => new(
            CycleDirection.Neutral,
            netMoveTicks,
            pathTicks,
            trendEfficiency,
            averageObi,
            spreadTicks,
            volatilityTicks,
            primaryReasonCode,
            otherFailedReasonCodes,
            thresholds);

    private static decimal ThresholdObserved(IEnumerable<ThresholdResult> thresholds, string name) =>
        thresholds.First(result => result.Name == name).Observed;

    private sealed record Sample(
        long UtcSecond,
        long Sequence,
        DateTimeOffset Timestamp,
        decimal MidPrice,
        decimal DepthObi,
        decimal BidDepth,
        decimal AskDepth,
        decimal BidNearDepth,
        decimal AskNearDepth,
        decimal SpreadPrice);

    private sealed record Rejection(
        DateTimeOffset Timestamp,
        long Sequence,
        string PrimaryReasonCode,
        IReadOnlyList<string> OtherFailedReasonCodes,
        IReadOnlyList<ThresholdResult> Thresholds,
        decimal SpreadTicks,
        decimal VolatilityTicks);
}
