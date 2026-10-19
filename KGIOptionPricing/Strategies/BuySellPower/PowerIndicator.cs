namespace KGIOptionPricing.Strategies.BuySellPower;

public sealed record PowerSnapshot(long UpQuantity, long DownQuantity, long ZeroQuantity, decimal? BuyPower,
    decimal? ShortAverage, decimal? LongAverage, decimal? BaisPower, bool HasData, bool IsWarm,
    string Status, MovementSegment? CurrentMovement, IReadOnlyList<MovementSegment> History,
    decimal? AverageUpPoints, decimal? AverageDownPoints, int UpSamples, int DownSamples)
{
    public long TotalQuantity => UpQuantity + DownQuantity + ZeroQuantity;
    public decimal? UpPercent => Share(UpQuantity, TotalQuantity);
    public decimal? DownPercent => Share(DownQuantity, TotalQuantity);
    // Absorb display rounding so the three displayed shares add to exactly 100%.
    public decimal? ZeroPercent => TotalQuantity == 0 ? null : 100m - UpPercent - DownPercent;
    internal static decimal? Share(long quantity, long total) => total == 0 ? null :
        decimal.Round(quantity * 100m / total, 4, MidpointRounding.AwayFromZero);
}

/// <summary>Single ordered product stream. Call Advance even when no new trade arrives.</summary>
public sealed class PowerIndicator
{
    public int TradeCount => _trades.Count;
    private sealed record Trade(DateTimeOffset Expires, int Quantity, int Side);
    private readonly Queue<Trade> _trades = new();
    private TimeWeightedPowerWindow? _short, _long;
    private DateTimeOffset? _first, _now;
    private long _up, _down, _zero;
    public PowerParameters Parameters { get; }
    public MovementTracker Movement { get; }
    public PowerIndicator(PowerParameters parameters)
    { parameters.Validate(); Parameters = parameters; Movement = new(parameters.ReversalPoints, parameters.MovementAveragePeriods); }

    public bool OnTrade(DateTimeOffset at, decimal price, int quantity, decimal bid, decimal ask)
    {
        if (_now.HasValue && at < _now.Value) return false;
        Advance(at);
        if (price <= 0 || quantity <= 0 || quantity % 10 == 0 || bid <= 0 || ask <= bid) return false;
        if (_first == null)
        {
            _first = at; var ready = at.AddSeconds(Parameters.PowerWindowSeconds);
            _short = new(Parameters.ShortAverageSeconds, ready); _long = new(Parameters.LongAverageSeconds, ready);
        }
        int side = price >= ask ? 1 : price <= bid ? -1 : 0;
        _trades.Enqueue(new(at.AddSeconds(Parameters.PowerWindowSeconds), quantity, side));
        AddQuantity(side, quantity); UpdateAverages(at);
        Movement.OnPrice(at, price); return true;
    }

    public void Advance(DateTimeOffset at)
    {
        if (_now.HasValue && at < _now.Value) return;
        while (_trades.TryPeek(out var trade) && trade.Expires <= at)
        {
            _trades.Dequeue(); AddQuantity(trade.Side, -trade.Quantity); UpdateAverages(trade.Expires);
        }
        _now = at;
        UpdateAverages(at);
    }

    private void AddQuantity(int side, int quantity)
    { if (side > 0) _up += quantity; else if (side < 0) _down += quantity; else _zero += quantity; }
    private void UpdateAverages(DateTimeOffset at)
    {
        // An empty window has no displayed percentage and contributes neutral zero to elapsed time.
        decimal power = PercentagePower() ?? 0m;
        _short?.Update(at, power); _long?.Update(at, power);
    }
    private decimal? PercentagePower()
    {
        long total = _up + _down + _zero;
        return PowerSnapshot.Share(_up, total) - PowerSnapshot.Share(_down, total);
    }
    public PowerSnapshot Snapshot()
    {
        decimal? shortAvg = _now.HasValue ? _short?.Average(_now.Value) : null;
        decimal? longAvg = _now.HasValue ? _long?.Average(_now.Value) : null;
        bool warm = shortAvg.HasValue && longAvg.HasValue;
        decimal? bias = warm ? shortAvg - longAvg : null;
        return new(_up, _down, _zero, PercentagePower(), shortAvg, longAvg, bias, _first.HasValue, warm,
            _first == null ? "尚無資料" : !warm ? "暖機中" : bias == null ? "無法計算" : "就緒",
            Movement.Current, Movement.History, Movement.AverageUp, Movement.AverageDown, Movement.UpSamples, Movement.DownSamples);
    }
}
