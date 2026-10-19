using System.Drawing;
using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;
using KGIOptionPricing;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        string output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        var bootstrap=Assembly.GetExecutingAssembly().GetType("KGIOptionPricing.Infrastructure.WindowsFontBootstrap");
        using IDisposable? resources=(IDisposable?)bootstrap?.GetMethod("Load")?.Invoke(null,new object?[]{new Action<string>(Console.WriteLine)});
        var checks=new List<object>();bool passed=true;
        foreach(string family in new[]{"Segoe UI","Microsoft JhengHei UI"})
        foreach(FontStyle style in new[]{FontStyle.Regular,FontStyle.Bold})
        {
            using var image=new Bitmap(400,40);using var graphics=Graphics.FromImage(image);using var font=new Font(family,12,style);
            graphics.Clear(Color.White);
            TextRenderer.DrawText(graphics,"每日執行檔 / Quote API 123",font,new Rectangle(0,0,400,40),Color.Black,Color.White,TextFormatFlags.SingleLine|TextFormatFlags.NoPadding);
            int ink=0;for(int y=0;y<40;y++)for(int x=0;x<400;x++)if(image.GetPixel(x,y).R<200)ink++;
            checks.Add(new{family,style=style.ToString(),inkPixels=ink,passed=ink>30});passed&=ink>30;
        }
        using var form=new Form1 { FormBorderStyle=FormBorderStyle.None,StartPosition=FormStartPosition.Manual,Location=new Point(-30000,-30000),ShowInTaskbar=false };
        form.Show();Application.DoEvents();
        using var preview=new Bitmap(form.Width,form.Height);
        form.DrawToBitmap(preview,new Rectangle(Point.Empty,preview.Size));
        foreach(string name in new[]{"lblQuoteTitle","lblQuoteStatus","lblTradeTitle","lblTradeStatus","lblFuturesTitle","lblFuturesPrice","lblRiskDataTitle","lblRiskDataStatus","lblLogTitle"})
        {
            var label=(Label)typeof(Form1).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(form)!;
            Point at=Point.Empty;for(Control? c=label;c!=form&&c!=null;c=c.Parent)at.Offset(c.Location);
            int ink=BrightPixels(preview,new Rectangle(at,label.Size));
            checks.Add(new{control=name,inkPixels=ink,passed=ink>10});passed&=ink>10;
        }
        foreach(string name in new[]{"menuStrip","toolStrip"})
        {
            var strip=(ToolStrip)typeof(Form1).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(form)!;
            foreach(ToolStripItem item in strip.Items)
            {
                if(item is ToolStripSeparator)continue;
                var bounds=item.Bounds;bounds.Offset(strip.Location);
                int ink=BrightPixels(preview,bounds);
                checks.Add(new{control=item.Name,inkPixels=ink,passed=ink>10});passed&=ink>10;
            }
        }
        preview.Save(Path.Combine(output,"main-window.png"),System.Drawing.Imaging.ImageFormat.Png);
        string report=JsonSerializer.Serialize(new{passed,privateBootstrap=bootstrap!=null,checks},new JsonSerializerOptions{WriteIndented=true});
        File.WriteAllText(Path.Combine(output,"rendering-report.json"),report);Console.WriteLine(report);
        return passed?0:1;
    }
    static int BrightPixels(Bitmap image,Rectangle area)
    {
        area.Intersect(new Rectangle(Point.Empty,image.Size));int ink=0;
        for(int y=area.Top;y<area.Bottom;y++)for(int x=area.Left;x<area.Right;x++){Color c=image.GetPixel(x,y);if(Math.Max(c.R,Math.Max(c.G,c.B))>90)ink++;}
        return ink;
    }
}

namespace KGIOptionPricing
{
    public partial class Form1:Form
    {
        // Render the real Designer without constructing any market, broker or trading services.
        public Form1()=>InitializeComponent();
        void tsmExit_Click(object? s,EventArgs e){}
        void btnTsPricing_Click(object? s,EventArgs e){}
        void btnTsOrderPlacement_Click(object? s,EventArgs e){}
        void btnTsLightningOrder_Click(object? s,EventArgs e){}
        void tsmQueueFollow_Click(object? s,EventArgs e){}
        void tsmSimulation_Click(object? s,EventArgs e){}
        void tsmLiveStrategy_Click(object? s,EventArgs e){}
        void btnTsOrderReport_Click(object? s,EventArgs e){}
        void btnTsDealReport_Click(object? s,EventArgs e){}
        void btnTsPosition_Click(object? s,EventArgs e){}
        void btnTsMargin_Click(object? s,EventArgs e){}
        void btnClearLog_Click(object? s,EventArgs e){}
    }
}
