using KGIOptionPricing;
using System.Windows.Forms;

static class ModelMenuTests
{
    public static void Render(string output)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var fonts = typeof(NikkeiRlPanel).Assembly.GetType("KGIOptionPricing.Infrastructure.WindowsFontBootstrap")!;
                using var loaded = (IDisposable)fonts.GetMethod("Load")!.Invoke(null, [null])!;
                using var form = new StrategyCenterForm { ClientSize = new System.Drawing.Size(1500, 760), Opacity = 0, ShowInTaskbar = false };
                var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                var tabs = (TabControl)typeof(StrategyCenterForm).GetField("strategyTabs", flags)!.GetValue(form)!;
                var panel = (NikkeiRlPanel)typeof(StrategyCenterForm).GetField("rlPanel", flags)!.GetValue(form)!;
                tabs.SelectedTab = tabs.TabPages.Cast<TabPage>().Single(t => t.Text.Contains("RL／Grid15"));
                ((ComboBox)typeof(NikkeiRlPanel).GetProperty("ModelSelector")!.GetValue(panel)!).SelectedIndex = 1;
                form.Show();
                Application.DoEvents();
                using var bitmap = new System.Drawing.Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
                bitmap.Save(output);
                form.Close();
            }
            catch (Exception exception) { error = exception; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(25))) throw new TimeoutException("Model menu rendering timed out");
        if (error is not null) throw error;
    }

    public static void Run()
    {
        using var panel = new NikkeiRlPanel();
        var selector = typeof(NikkeiRlPanel).GetProperty("ModelSelector")?.GetValue(panel) as ComboBox;
        if (selector is null || selector.Items.Count != 3)
            throw new Exception("RL panel must expose earlier and both Grid15 model choices");
        string[] variants = ["earlier", "grid15-positive-net", "grid15-q90-research"];
        for (int i = 0; i < variants.Length; i++)
        {
            selector.SelectedIndex = i;
            var definition = panel.CreateDefinition();
            if (definition.Parameters["PolicyVariant"] != variants[i])
                throw new Exception("Selected model does not match the new strategy definition");
        }
        Console.WriteLine("PASS RL model selector: earlier, Grid15 positive-net and Grid15 q90");
    }
}
