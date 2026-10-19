namespace KGIOptionPricing.Strategies.MultiFactorMeanR17;

public sealed record MovementSegment(string Direction, decimal StartPrice, decimal ExtremePrice,
    DateTimeOffset StartTime, DateTimeOffset ExtremeTime, DateTimeOffset ConfirmedAt,
    bool IsCompleted, string EndReason = "")
{
    public decimal Points => Math.Abs(ExtremePrice - StartPrice);
    public decimal DurationSeconds => (decimal)(ExtremeTime - StartTime).TotalSeconds;
}

/// <summary>Price-only directional state; no orders or position authority.</summary>
public sealed class MovementTracker(decimal threshold, int periods)
{
    private (decimal Price, DateTimeOffset At)? _origin;
    private MovementSegment? _current;
    private readonly Queue<decimal> _up = new(), _down = new();
    private readonly Queue<MovementSegment> _history = new();
    private IReadOnlyList<MovementSegment>? _cachedHistory;
    private decimal? _averageUp,_averageDown;
    public MovementSegment? Current => _current;
    public IReadOnlyList<MovementSegment> History => _cachedHistory??=Array.AsReadOnly(_history.ToArray());
    public int UpSamples => _up.Count;
    public int DownSamples => _down.Count;
    public decimal? AverageUp => _averageUp;
    public decimal? AverageDown => _averageDown;
    public event Action<MovementSegment>? SegmentEnded;

    public void OnPrice(DateTimeOffset at, decimal price)
    {
        if (_origin == null) { _origin = (price, at); _current = new("未定", price, price, at, at, at, false); return; }
        if (_current!.Direction == "未定")
        {
            // First valid trade stays the anchor until either X+Y or X-Y is reached.
            decimal move = price - _origin.Value.Price;
            string direction = move >= threshold ? "上漲" : move <= -threshold ? "下跌" : "未定";
            _current = new(direction, _origin.Value.Price, price, _origin.Value.At, at, at, false);
            return;
        }
        bool rising = _current.Direction == "上漲";
        if (rising ? price > _current.ExtremePrice : price < _current.ExtremePrice)
            _current = _current with { ExtremePrice = price, ExtremeTime = at };
        decimal reverse = rising ? _current.ExtremePrice - price : price - _current.ExtremePrice;
        // Initialization and each reversal create a leg with at least threshold points.
        // Equality must remain eligible, or a threshold-sized leg can freeze indefinitely.
        if (_current.Points < threshold || reverse < threshold) return;
        MovementSegment finished = _current with { ConfirmedAt = at, IsCompleted = true, EndReason = "反向確認" };
        AddHistory(finished);
        Queue<decimal> samples = rising ? _up : _down;
        samples.Enqueue(finished.Points); while (samples.Count > periods) samples.Dequeue();
        if(rising)_averageUp=_up.Average();else _averageDown=_down.Average();
        _current = new(rising ? "下跌" : "上漲", finished.ExtremePrice, price,
            finished.ExtremeTime, at, at, false);
        SegmentEnded?.Invoke(finished);
    }

    public void EndIncomplete(DateTimeOffset at, string reason)
    {
        if (_current == null) return;
        var unfinished = _current with { ConfirmedAt = at, IsCompleted = false, EndReason = reason };
        AddHistory(unfinished); _current = null; _origin = null;
        SegmentEnded?.Invoke(unfinished);
    }

    private void AddHistory(MovementSegment value)
    { _history.Enqueue(value); while (_history.Count > 500) _history.Dequeue(); _cachedHistory=null; }
}
