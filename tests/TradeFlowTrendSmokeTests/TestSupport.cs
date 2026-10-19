namespace TradeFlowTrendSmokeTests;
internal static class TestSupport
{
    public static DateTimeOffset At(string value) => DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
    public static void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
    public static void Reject(Action action) { try { action(); } catch (ArgumentException) { return; } throw new InvalidOperationException("Invalid input accepted"); }
}
