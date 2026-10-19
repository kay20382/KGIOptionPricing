using System.Reflection;
using KGIOptionPricing;
using KGIOptionPricing.StrategyFramework;

internal static class Program
{
    private static readonly string[] Removed = { "WaveletScalper", "NikkeiMicrostructureAlpha", "OrderBookTrend" };

    [STAThread]
    private static int Main()
    {
        try
        {
            CheckFactories("模擬", StrategySimulationService.DefaultFactories);
            CheckFactories("實盤", LiveStrategyService.DefaultFactories);
            CheckTabs<StrategyCenterForm>();
            CheckTabs<LiveStrategyCenterForm>();
            Console.WriteLine("Strategy availability smoke PASS: removed 2/3/5 from both registries and centers; retained 7 strategies.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void CheckFactories(string center, IReadOnlyList<IStrategyFactory> factories)
    {
        var registry = new StrategyRegistry();
        foreach (var factory in factories) registry.Register(factory);
        foreach (string type in Removed)
        {
            try { registry.Get(type); }
            catch (KeyNotFoundException) { continue; }
            throw new InvalidOperationException($"{center}仍可建立已移除策略 {type}");
        }
        if (factories.Count != 7) throw new InvalidOperationException($"{center}預期保留 7 個策略，實際 {factories.Count}");
        foreach (var factory in factories)
            if (!ReferenceEquals(registry.Get(factory.StrategyType), factory))
                throw new InvalidOperationException($"{center}其餘策略註冊不一致");
    }

    private static void CheckTabs<TForm>() where TForm : Form, new()
    {
        using var form = new TForm();
        var field = typeof(TForm).GetField("strategyTabs", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var tabs = (TabControl)field.GetValue(form)!;
        foreach (string name in new[] { "waveletTab", "nikkeiAlphaTab", "orderBookTrendTab" })
            if (tabs.TabPages.Contains(GetTab(name)))
                throw new InvalidOperationException($"{typeof(TForm).Name}仍顯示已移除策略 {name}");
        if (tabs.TabPages.Count != 7)
            throw new InvalidOperationException($"{typeof(TForm).Name}預期保留 7 個策略頁籤，實際 {tabs.TabPages.Count}");
        foreach (string name in new[] { "meanReversionTab", "periodicQueueCycleTab" })
            if (!tabs.TabPages.Contains(GetTab(name))) throw new InvalidOperationException($"缺少既有頁籤 {name}");
        foreach (string title in new[] { "內外盤順勢交易", "買賣力道反轉交易", "買賣力道" })
            if (!tabs.TabPages.Cast<TabPage>().Any(x => x.Text == title || x.Text == title + "（實盤）"))
                throw new InvalidOperationException($"缺少既有頁籤 {title}");
        if (!tabs.TabPages.Cast<TabPage>().Any(x => x.Text.Contains("R17")) ||
            !tabs.TabPages.Cast<TabPage>().Any(x => x.Text.Contains("日經RL")))
            throw new InvalidOperationException("缺少 R17 或日經RL頁籤");
        TabPage GetTab(string name) => (TabPage)typeof(TForm)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
    }
}
