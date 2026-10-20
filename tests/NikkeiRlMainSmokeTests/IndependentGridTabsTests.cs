using KGIOptionPricing;
using KGIOptionPricing.StrategyFramework;
using System.Reflection;
using System.Windows.Forms;

static class IndependentGridTabsTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void Run()
    {
        CheckForm(new StrategyCenterForm(), false);
        CheckForm(new LiveStrategyCenterForm(), true);
        Console.WriteLine("PASS independent earlier/Positive Net/Q90 tabs: fixed models, distinct IDs, isolated performance and execution routing, live confirmation");
    }
    static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls) { yield return child; foreach (var c in Descendants(child)) yield return c; }
    }
    static void CheckForm(Form form, bool live)
    {
        using (form)
        {
            var panels = Descendants(form).OfType<NikkeiRlPanel>().ToArray();
            if (panels.Length != 3) throw new Exception("Both strategy centers must contain three independent model panels");
            var ids = new HashSet<string>();
            foreach (string variant in new[] { "earlier", "grid15-positive-net", "grid15-q90-research" })
            {
                var panel = panels.Single(p => p.CreateDefinition().Parameters["PolicyVariant"] == variant);
                if (panel.Parent is not TabPage || panel.ModelSelector.Enabled)
                    throw new Exception("Each tab must lock its model");
                var definition = panel.CreateDefinition();
                if (!ids.Add(definition.InstanceId)) throw new Exception("Independent model tabs reused an instance ID");
                if (definition.Parameters["LiveTradingEnabled"] != live.ToString().ToLowerInvariant())
                    throw new Exception("Wrong live/simulation mode");
                if (live && panel.Confirm.Checked) throw new Exception("Live tab opted in automatically");
                panel.ModelSelector.SelectedIndex = (panel.ModelSelector.SelectedIndex + 1) % 3;
                if (panel.CreateDefinition().Parameters["PolicyVariant"] != variant)
                    throw new Exception("A fixed strategy tab changed model");
                var snapshot = new StrategySnapshot { StrategyType = "NikkeiRlDirection", InstanceId = definition.InstanceId,
                    State = StrategyLifecycleState.Running, CompletedTrades = 2,
                    Diagnostics = new Dictionary<string,string> { ["ModelId"] = variant == "earlier" ? "pruned-earlier-seed22" : variant } };
                form.GetType().GetMethod("Upsert", Private)!.Invoke(form, [snapshot]);
                form.GetType().GetMethod(live ? "AddFill" : "AddExecution", Private)!.Invoke(form,
                    [new ExecutionEvent { StrategyInstanceId = definition.InstanceId, Symbol = "MC", Quantity = 1 }]);
            }
            if (panels.Any(p => p.Instances.Rows.Count != 1 || p.Executions.Rows.Count != 1))
                throw new Exception("Model tab snapshots or fills were mixed or lost");
            if (!live)
            {
                var key = form.GetType().GetMethod("ReplayKey", Private | BindingFlags.Static)!;
                var keys = panels.Select(p => (string)key.Invoke(null, [p.CreateDefinition()])!).ToArray();
                if (keys.Distinct().Count() != 3) throw new Exception("Model tabs share replay pause/export state");
            }
        }
    }
}
