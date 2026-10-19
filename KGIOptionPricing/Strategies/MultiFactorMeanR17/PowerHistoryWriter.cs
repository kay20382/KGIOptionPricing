using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Channels;

namespace KGIOptionPricing.Strategies.MultiFactorMeanR17;

public sealed record PowerHistoryRow(string Product, string Symbol, string SessionId,
    MovementSegment Segment, PowerParameters Parameters, string InstanceId);

/// <summary>Bounded background CSV export. Errors are sticky and visible to the observer.</summary>
public sealed class PowerHistoryWriter : IDisposable
{
    private readonly Channel<PowerHistoryRow> _rows = Channel.CreateBounded<PowerHistoryRow>(
        new BoundedChannelOptions(10000) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly Task _worker;
    private string _error = "";
    private int _pending;
    public string Root { get; }
    public string Error => Volatile.Read(ref _error);
    public int Pending => Volatile.Read(ref _pending);
    public PowerHistoryWriter(string root)
    { Root = Path.GetFullPath(root); _worker = Task.Run(WriteLoop); }
    public void Append(PowerHistoryRow row)
    {
        Interlocked.Increment(ref _pending);
        if (_rows.Writer.TryWrite(row)) return;
        Interlocked.Decrement(ref _pending);
        Volatile.Write(ref _error, "歷史保存佇列已滿或已關閉；有行情段未寫入。");
    }
    private async Task WriteLoop()
    {
        await foreach (PowerHistoryRow row in _rows.Reader.ReadAllAsync())
        {
            try
            {
                string[] session = row.SessionId.Split('/');
                string directory = Path.Combine(Root, Safe(row.Product), Safe(row.Symbol), session[0], session[1]);
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "行情段紀錄.csv");
                bool header = !File.Exists(path) || new FileInfo(path).Length == 0;
                using var writer = new StreamWriter(path, append: true, new UTF8Encoding(true));
                if (header) writer.WriteLine("InstanceId,Product,Symbol,SessionId,Direction,StartPrice,ExtremePrice,Points,StartTime,ExtremeTime,ConfirmedAt,DurationSeconds,IsCompleted,EndReason,PowerWindowSeconds,ShortAverageSeconds,LongAverageSeconds,ReversalPoints,MovementAveragePeriods");
                var s = row.Segment; var p = row.Parameters;
                writer.WriteLine(string.Join(",", new object[] {row.InstanceId,row.Product,row.Symbol,row.SessionId,s.Direction,
                    s.StartPrice,s.ExtremePrice,s.Points,s.StartTime.ToString("O"),s.ExtremeTime.ToString("O"),s.ConfirmedAt.ToString("O"),
                    s.DurationSeconds,s.IsCompleted.ToString().ToLowerInvariant(),s.EndReason,
                    p.PowerWindowSeconds,p.ShortAverageSeconds,p.LongAverageSeconds,p.ReversalPoints,p.MovementAveragePeriods}.Select(Cell)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            { Volatile.Write(ref _error, "保存異常：" + ex.Message); }
            finally { Interlocked.Decrement(ref _pending); }
        }
    }
    private static string Cell(object value)
    {
        string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
    private static string Safe(string value) => string.Concat(value.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
    public void Dispose() { _rows.Writer.TryComplete(); _worker.GetAwaiter().GetResult(); }
}
