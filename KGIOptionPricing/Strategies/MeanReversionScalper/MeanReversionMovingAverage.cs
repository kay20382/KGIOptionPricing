namespace KGIOptionPricing.Strategies.MeanReversionScalper;

public sealed class MeanReversionMovingAverage
{
    private readonly SortedList<long, Sample> _samples = new();

    private readonly record struct Sample(long TimestampMilliseconds, decimal Mid);

    public void Observe(DateTimeOffset timestamp, decimal bid1, decimal ask1)
    {
        if (bid1 <= 0m || ask1 <= bid1)
            return;
        long second = timestamp.ToUnixTimeSeconds();
        var sample = new Sample(timestamp.ToUnixTimeMilliseconds(), (bid1 + ask1) / 2m);
        if (!_samples.TryGetValue(second, out Sample previous) ||
            sample.TimestampMilliseconds >= previous.TimestampMilliseconds)
            _samples[second] = sample;
    }

    public bool TryGet(DateTimeOffset now, int horizonSeconds, int freshnessSeconds,
        decimal currentMid, out decimal sma, out int trendSign)
    {
        sma = 0m;
        trendSign = 0;
        if (horizonSeconds <= 0 || freshnessSeconds <= 0 || currentMid <= 0m)
            return false;

        long endSecond = now.ToUnixTimeSeconds();
        long firstSecond = endSecond - horizonSeconds;
        decimal sum = 0m;
        for (long second = firstSecond; second < endSecond; second++)
        {
            int index = FloorIndex(second);
            if (index < 0)
                return false;
            Sample sample = _samples.Values[index];
            long ageMilliseconds = (second + 1) * 1000L - sample.TimestampMilliseconds;
            if (ageMilliseconds <= 0 || ageMilliseconds > freshnessSeconds * 1000L)
                return false;
            sum += sample.Mid;
        }

        sma = sum / horizonSeconds;
        trendSign = currentMid > sma ? 1 : currentMid < sma ? -1 : 0;
        Prune(firstSecond - freshnessSeconds - 1);
        return true;
    }

    public void Reset() => _samples.Clear();

    private int FloorIndex(long second)
    {
        int left = 0;
        int right = _samples.Count - 1;
        int found = -1;
        while (left <= right)
        {
            int middle = left + (right - left) / 2;
            if (_samples.Keys[middle] <= second)
            {
                found = middle;
                left = middle + 1;
            }
            else
            {
                right = middle - 1;
            }
        }
        return found;
    }

    private void Prune(long oldestRelevantSecond)
    {
        while (_samples.Count > 1 && _samples.Keys[1] < oldestRelevantSecond)
            _samples.RemoveAt(0);
    }
}
