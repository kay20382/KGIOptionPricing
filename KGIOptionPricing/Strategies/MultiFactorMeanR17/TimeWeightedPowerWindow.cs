namespace KGIOptionPricing.Strategies.MultiFactorMeanR17;

/// <summary>Piecewise-constant time integral. Each interval is added/removed once; reads are O(1).</summary>
internal sealed class TimeWeightedPowerWindow(int seconds, DateTimeOffset start)
{
    private sealed class Interval(DateTimeOffset from, DateTimeOffset to, decimal power)
    {
        public DateTimeOffset From = from, To = to;
        public readonly decimal Power = power;
    }
    private readonly Queue<Interval> _intervals = new();
    private Interval? _tail;
    private DateTimeOffset _cursor = start;
    private readonly DateTimeOffset _start = start;
    private decimal _power;
    private decimal _area;
    public void Update(DateTimeOffset at, decimal power)
    {
        if (at > _cursor)
        {
            decimal added = _power * Elapsed(_cursor, at);
            if (_power != 0)
            {
                if (_tail != null && _tail.To == _cursor && _tail.Power == _power) _tail.To = at;
                else { _tail = new(_cursor, at, _power); _intervals.Enqueue(_tail); }
            }
            _area += added; _cursor = at;
            DateTimeOffset left = at.AddSeconds(-seconds);
            while (_intervals.TryPeek(out var head) && head.To <= left)
            {
                _intervals.Dequeue(); _area -= head.Power * Elapsed(head.From, head.To);
                if (ReferenceEquals(head, _tail)) _tail = null;
            }
            if (_intervals.TryPeek(out var partial) && partial.From < left)
            { _area -= partial.Power * Elapsed(partial.From, left); partial.From = left; }
        }
        // Before the full power window exists, remember the value but accumulate no area.
        _power = power;
    }
    public decimal? Average(DateTimeOffset now) => now < _start.AddSeconds(seconds) ? null : _area / seconds;
    private static decimal Elapsed(DateTimeOffset from, DateTimeOffset to) => (to-from).Ticks / (decimal)TimeSpan.TicksPerSecond;
}
