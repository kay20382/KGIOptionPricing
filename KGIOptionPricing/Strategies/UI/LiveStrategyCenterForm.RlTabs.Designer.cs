namespace KGIOptionPricing;

public sealed partial class LiveStrategyCenterForm
{
    private readonly NikkeiRlPanel _rlPanel = new();
    private readonly NikkeiRlPanel _grid15PositivePanel = new();
    private readonly NikkeiRlPanel _grid15Q90Panel = new();
    private NikkeiRlPanel[] RlPanels => [_rlPanel, _grid15PositivePanel, _grid15Q90Panel];

    private void InitializeRlTabs()
    {
        strategyTabs.Multiline = true;
        Add(_rlPanel, "earlier", "日經 RL earlier（實盤）", "rlEarlierTab");
        Add(_grid15PositivePanel, "grid15-positive-net", "Grid15 Positive Net（實盤）", "grid15PositiveTab");
        Add(_grid15Q90Panel, "grid15-q90-research", "Grid15 Q90（實盤）", "grid15Q90Tab");
        void Add(NikkeiRlPanel panel, string variant, string title, string name)
        {
            panel.LockPolicyVariant(variant);
            panel.ConfigureLive(true);
            panel.Instance.Text = $"LIVE-{panel.InstancePrefix}-225MC-{DateTime.Now:HHmmssfff}";
            var tab = new TabPage(title) { Name = name };
            tab.Controls.Add(panel);
            strategyTabs.TabPages.Add(tab);
        }
    }
}
