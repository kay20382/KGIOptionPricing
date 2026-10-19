using System.Globalization;

namespace KGIOptionPricing.Strategies.MultiFactorMeanR17;

public sealed record PowerParameters(int PowerWindowSeconds = 30, int ShortAverageSeconds = 60,
    int LongAverageSeconds = 180, decimal ReversalPoints = 30m, int MovementAveragePeriods = 20)
{
    public void Validate()
    {
        if (PowerWindowSeconds is < 1 or > 3600 || ShortAverageSeconds is < 1 or > 86400 ||
            LongAverageSeconds < ShortAverageSeconds || LongAverageSeconds > 86400 ||
            ReversalPoints is <= 0 or > 10000 || MovementAveragePeriods is < 1 or > 10000)
            throw new ArgumentException("力道視窗須為 1–3600 秒，平均須為 1–86400 秒且長期不小於短期，反向點數須為 (0,10000]，平均段數須為 1–10000。");
    }

    public IReadOnlyDictionary<string, string> ToDictionary(string prefix = "") => new Dictionary<string, string>
    {
        [prefix + nameof(PowerWindowSeconds)] = PowerWindowSeconds.ToString(CultureInfo.InvariantCulture),
        [prefix + nameof(ShortAverageSeconds)] = ShortAverageSeconds.ToString(CultureInfo.InvariantCulture),
        [prefix + nameof(LongAverageSeconds)] = LongAverageSeconds.ToString(CultureInfo.InvariantCulture),
        [prefix + nameof(ReversalPoints)] = ReversalPoints.ToString(CultureInfo.InvariantCulture),
        [prefix + nameof(MovementAveragePeriods)] = MovementAveragePeriods.ToString(CultureInfo.InvariantCulture)
    };

    public static PowerParameters Parse(IReadOnlyDictionary<string, string> values, string product)
    {
        var map = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
        decimal Read(string name, decimal fallback) => map.TryGetValue(product + "." + name, out string? value)
            ? decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture) : fallback;
        int Integer(string name, int fallback)
        {
            decimal value = Read(name, fallback);
            if (value != decimal.Truncate(value)) throw new ArgumentException(name + " 須為整數。");
            return checked((int)value);
        }
        var result = new PowerParameters(Integer(nameof(PowerWindowSeconds), 30), Integer(nameof(ShortAverageSeconds), 60),
            Integer(nameof(LongAverageSeconds), 180), Read(nameof(ReversalPoints), 30), Integer(nameof(MovementAveragePeriods), 20));
        result.Validate(); return result;
    }
}
