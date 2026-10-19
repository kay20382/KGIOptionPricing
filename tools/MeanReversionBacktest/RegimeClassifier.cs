using KGIOptionPricing.Strategies.MeanReversionScalper;
using KGIOptionPricing.StrategyFramework;

namespace MeanReversionBacktest;

// A strictly backward-looking 225M filter. No outcome data enters the entry label.
public sealed class RegimeClassifier
{
    private readonly MeanReversionSessionSchedule _schedule = MeanReversionSessionSchedule.Parse(
        new Dictionary<string, string>());
    private readonly Queue<(DateTimeOffset Time, decimal Price)> _samples = new();
    private string? _session;
    private DateTimeOffset _lastSampleMinute;
    private DateTimeOffset _lastPriceTime;
    private decimal _lastPrice;

    public void Observe(MarketEvent item)
    {
        if (item.ProductId != "225M" || item.Kind != MarketEventKind.Trade || item.LastPrice <= 0) return;
        string? session = _schedule.Resolve(item.Timestamp)?.SessionId;
        if (session != _session)
        {
            _session = session;
            _samples.Clear();
            _lastSampleMinute = default;
        }
        if (session == null) return;
        _lastPrice = item.LastPrice;
        _lastPriceTime = item.Timestamp;
        DateTimeOffset minute = new(item.Timestamp.Year, item.Timestamp.Month, item.Timestamp.Day,
            item.Timestamp.Hour, item.Timestamp.Minute, 0, item.Timestamp.Offset);
        if (minute == _lastSampleMinute) return;
        _lastSampleMinute = minute;
        _samples.Enqueue((minute, item.LastPrice));
        while (_samples.Count > 0 && minute - _samples.Peek().Time > TimeSpan.FromMinutes(15))
            _samples.Dequeue();
    }

    public string Label(DateTimeOffset time)
    {
        string? session = _schedule.Resolve(time)?.SessionId;
        string phase = session?.StartsWith("D:", StringComparison.Ordinal) == true ? "day" : "night";
        if (session == null || session != _session || time - _lastPriceTime > TimeSpan.FromSeconds(30) ||
            _samples.Count < 12 || _samples.Last().Time - _samples.Peek().Time < TimeSpan.FromMinutes(12))
            return phase + "|unknown";
        decimal[] prices = _samples.Select(x => x.Price).ToArray();
        decimal range = prices.Max() - prices.Min();
        decimal path = 0;
        for (int i = 1; i < prices.Length; i++) path += Math.Abs(prices[i] - prices[i - 1]);
        decimal efficiency = path == 0 ? 0 : Math.Abs(prices[^1] - prices[0]) / path;
        string trend = efficiency >= 0.60m ? "trend" : efficiency <= 0.30m ? "range" : "mixed";
        string volatility = range >= 80m ? "highvol" : "lowvol";
        return phase + "|" + trend + "|" + volatility;
    }
}
