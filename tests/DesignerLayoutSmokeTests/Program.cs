using System.Reflection;
using System.Windows.Forms;
using KGIOptionPricing;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        try
        {
        Application.EnableVisualStyles();
        RequireField<ToolStripMenuItem>(typeof(Form1), "tsmQueueFollow");
        RequireField<ToolStripMenuItem>(typeof(Form1), "tsmSimulation");
        RequireField<ToolStripMenuItem>(typeof(Form1), "tsmLiveStrategy");
        RequireUiOnlyForm<OptionPricingForm>();
        RequireUiOnlyForm<OrderPlacementForm>();
        RequireUiOnlyForm<DealReportForm>();
        RequireUiOnlyForm<OrderReportForm>();
        RequireUiOnlyForm<MarginEquityForm>();
        RequireUiOnlyForm<PositionForm>();
        RequireTabCount(typeof(MarginEquityForm), "_marketTabs", 2);
        RequireGridColumnCount(typeof(MarginEquityForm), "_foreignGrid", 11);
        RequireTabCount(typeof(PositionForm), "_marketTabs", 2);
        RequireGridColumnCount(typeof(PositionForm), "_foreignSummary", 11);
        RequireGridColumnCount(typeof(PositionForm), "_foreignDetail", 13);
        RequireField<SplitContainer>(typeof(InstrumentSelectorForm), "splitContainer");
        RequireUiOnlyForm<InstrumentSelectorForm>();
        RequireGridColumnCount(typeof(InstrumentSelectorForm), "_grid", 4);
        RequireField<TableLayoutPanel>(typeof(LightningOrderSettingsForm), "settingsLayout");
        RequireUiOnlyForm<LightningOrderSettingsForm>();
        RequireEditDialog("LightningPriceEditDialog", new object[] { 4122.25m, 2 }, 4122.25m, null);
        RequireEditDialog("OrderModificationDialog", new object[] { 65005m, 1 }, 65005m, 1);
        RequireField<TableLayoutPanel>(typeof(LightningOrderForm), "topPanel");
        RequireField<TableLayoutPanel>(typeof(LightningOrderForm), "bottomActions");
        RequireUiOnlyForm<LightningOrderForm>();
        RequireGridColumnCount(typeof(LightningOrderForm), "_ladder", 5);
        RequireField<TableLayoutPanel>(typeof(QueueFollowStrategyForm), "commandPanel");
        RequireUiOnlyForm<QueueFollowStrategyForm>();
        RequireGridColumnCount(typeof(QueueFollowStrategyForm), "_grid", 16);
        RequireField<NumericUpDown>(typeof(PeriodicQueueCycleControls), "trendInitialAheadAInput");
        using (var periodic = new PeriodicQueueCycleControls())
        {
            if (!periodic.Parameters().ContainsKey("TrendInitialAheadA"))
                throw new InvalidOperationException("Periodic queue parameter editor lost TrendInitialAheadA");
        }
        RequireField<TabControl>(typeof(StrategyCenterForm), "strategyTabs");
        RequireUiOnlyForm<StrategyCenterForm>();
        RequireTabCount(typeof(StrategyCenterForm), "strategyTabs", 9);
        RequireField<PowerReversalPanel>(typeof(StrategyCenterForm), "powerReversalPanel");
        RequireField<TradeFlowTrendPanel>(typeof(StrategyCenterForm), "tradeFlowPanel");
        RequireGridColumnCount(typeof(StrategyCenterForm), "_mrGrid", 21);
        RequireGridColumnCount(typeof(StrategyCenterForm), "_pqcExecutions", 10);
        RequireField<Button>(typeof(StrategyCenterForm), "pqcReplayButton");
        RequireField<TabControl>(typeof(LiveStrategyCenterForm), "strategyTabs");
        RequireField<Label>(typeof(LiveStrategyCenterForm), "liveWarningBanner");
        RequireUiOnlyForm<LiveStrategyCenterForm>();
        RequireTabCount(typeof(LiveStrategyCenterForm), "strategyTabs", 9);
        RequireField<PowerReversalPanel>(typeof(LiveStrategyCenterForm), "powerReversalPanel");
        RequireField<TradeFlowTrendPanel>(typeof(LiveStrategyCenterForm), "tradeFlowPanel");
        RequireGridColumnCount(typeof(LiveStrategyCenterForm), "_mrGrid", 22);
        RequireGridColumnCount(typeof(LiveStrategyCenterForm), "_pqcFills", 10);
        RequireField<CheckBox>(typeof(LiveStrategyCenterForm), "_pqcConfirm");
        Console.WriteLine("Designer layout smoke PASS");
        return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void RequireField<TControl>(Type formType, string fieldName) where TControl : class
    {
        FieldInfo? field = formType.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        if (field?.FieldType != typeof(TControl))
            throw new InvalidOperationException($"{formType.Name}.{fieldName} is missing as a {typeof(TControl).Name} field");
    }

    private static void RequireUiOnlyForm<TForm>() where TForm : Form
    {
        using var form = (TForm?)Activator.CreateInstance(typeof(TForm))
            ?? throw new InvalidOperationException($"{typeof(TForm).Name} has no UI-only constructor");
        if (form.Controls.Count == 0)
            throw new InvalidOperationException($"{typeof(TForm).Name} has no designed controls");
    }

    private static void RequireTabCount(Type formType, string fieldName, int expected)
    {
        using var form = (Form)Activator.CreateInstance(formType)!;
        var tabs = GetControl<TabControl>(form, fieldName);
        if (tabs.TabPages.Count != expected) throw new InvalidOperationException($"{formType.Name}.{fieldName} has {tabs.TabPages.Count} tabs, expected {expected}");
    }

    private static void RequireGridColumnCount(Type formType, string fieldName, int expected)
    {
        using var form = (Form)Activator.CreateInstance(formType)!;
        var grid = GetControl<DataGridView>(form, fieldName);
        if (grid.Columns.Count != expected) throw new InvalidOperationException($"{formType.Name}.{fieldName} has {grid.Columns.Count} columns, expected {expected}");
    }

    private static TControl GetControl<TControl>(Form form, string fieldName) where TControl : Control
    {
        var field = form.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        if (field?.GetValue(form) is not TControl control || !form.Contains(control))
            throw new InvalidOperationException($"{form.GetType().Name}.{fieldName} missing from control tree");
        return control;
    }

    private static void RequireEditDialog(string typeName, object[] args, decimal expectedPrice, int? expectedQty)
    {
        var type = typeof(Form1).Assembly.GetType("KGIOptionPricing." + typeName)
            ?? throw new InvalidOperationException($"{typeName} is missing");
        using var dialog = (Form)Activator.CreateInstance(type, args)!;
        decimal price = (decimal)type.GetProperty("Price")!.GetValue(dialog)!;
        if (price != expectedPrice) throw new InvalidOperationException($"{typeName}.Price changed");
        if (expectedQty.HasValue && (int)type.GetProperty("Quantity")!.GetValue(dialog)! != expectedQty.Value)
            throw new InvalidOperationException($"{typeName}.Quantity changed");
        if (dialog.AcceptButton is not Button ok || ok.DialogResult != DialogResult.OK ||
            dialog.CancelButton is not Button cancel || cancel.DialogResult != DialogResult.Cancel)
            throw new InvalidOperationException($"{typeName} confirmation buttons changed");
    }
}
