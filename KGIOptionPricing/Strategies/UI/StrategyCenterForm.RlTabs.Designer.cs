namespace KGIOptionPricing;

public sealed partial class StrategyCenterForm
{
    private readonly NikkeiRlPanel rlPanel = new();
    private readonly NikkeiRlPanel grid15PositivePanel = new();
    private readonly NikkeiRlPanel grid15Q90Panel = new();
    private NikkeiRlPanel[] RlPanels => [rlPanel, grid15PositivePanel, grid15Q90Panel];

    private void InitializeRlTabs()
    {
        strategyTabs.Multiline = true;
        Add(rlPanel, "earlier", "日經 RL earlier（模擬）", "rlEarlierTab");
        Add(grid15PositivePanel, "grid15-positive-net", "Grid15 Positive Net（模擬）", "grid15PositiveTab");
        Add(grid15Q90Panel, "grid15-q90-research", "Grid15 Q90（模擬）", "grid15Q90Tab");
        void Add(NikkeiRlPanel panel, string variant, string title, string name)
        {
            panel.LockPolicyVariant(variant);
            panel.ConfigureLive(false);
            panel.Instance.Text = $"{panel.InstancePrefix}-225MC-{DateTime.Now:HHmmssfff}";
            var tab = new TabPage(title) { Name = name };
            tab.Controls.Add(panel);
            strategyTabs.TabPages.Add(tab);
        }
    }
}
