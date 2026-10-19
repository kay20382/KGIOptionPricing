using System.Globalization;
using System.Text;

namespace KGIOptionPricing.ProductSpreads;

public interface IProductSpreadSnapshotStore
{
    Task SaveAsync(
        ProductSpreadDefinition definition,
        ProductSpreadSnapshot snapshot,
        CancellationToken cancellationToken = default);
}

public sealed class ProductSpreadCsvSnapshotStore : IProductSpreadSnapshotStore, IDisposable
{
    public const string Header = "日期時間,價差名稱,成交價,買量,買價,賣價,賣量,A買價,A賣價,B買價,B賣價";

    private readonly string _configDirectory;
    private readonly Action<string>? _log;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly HashSet<string> _reportedRecoveryFiles = new(StringComparer.OrdinalIgnoreCase);

    public ProductSpreadCsvSnapshotStore(string configDirectory, Action<string>? log = null)
    {
        _configDirectory = Path.GetFullPath(configDirectory);
        _log = log;
    }

    public async Task SaveAsync(
        ProductSpreadDefinition definition,
        ProductSpreadSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        string directory = ResolveDirectory(definition.SnapshotPath);
        Directory.CreateDirectory(directory);

        SpreadSnapshotPeriod period = GetPeriod(snapshot.SnapshotTime);
        string safeId = SanitizeFileName(string.IsNullOrWhiteSpace(definition.Id)
            ? definition.ProductName
            : definition.Id);
        string fileName = $"{period.FileDate:yyyyMMdd}_{safeId}.csv";
        string filePath = Path.Combine(directory, fileName);
        string line = FormatLine(definition, snapshot);

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using FileStream processLock = await AcquireProcessLockAsync(filePath, cancellationToken).ConfigureAwait(false);
            try
            {
                await AppendLineWithRetryAsync(filePath, line, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException primaryError)
            {
                string recoveryPath = Path.Combine(
                    directory,
                    $"{Path.GetFileNameWithoutExtension(fileName)}_recovery_{Environment.ProcessId}.csv");
                await AppendLineWithRetryAsync(recoveryPath, line, cancellationToken).ConfigureAwait(false);
                if (_reportedRecoveryFiles.Add(recoveryPath))
                {
                    _log?.Invoke($"[價差快照] 主檔被占用，快照已改存復原檔：{recoveryPath}（{primaryError.Message}）");
                }
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static async Task<FileStream> AcquireProcessLockAsync(string filePath, CancellationToken cancellationToken)
    {
        string lockPath = filePath + ".write.lock";
        IOException? lastError = null;
        for (int attempt = 0; attempt < 20; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException ex)
            {
                lastError = ex;
                await Task.Delay(50 + attempt * 25, cancellationToken).ConfigureAwait(false);
            }
        }
        throw new IOException($"無法取得價差快照跨程序寫入鎖：{lockPath}", lastError);
    }

    private static async Task AppendLineWithRetryAsync(
        string filePath,
        string line,
        CancellationToken cancellationToken)
    {
        IOException? lastError = null;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                bool writeHeader = !File.Exists(filePath) || new FileInfo(filePath).Length == 0;
                await using var stream = new FileStream(
                    filePath,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 4096,
                    useAsync: true);
                await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                if (writeHeader) await writer.WriteLineAsync(Header).ConfigureAwait(false);
                await writer.WriteLineAsync(line).ConfigureAwait(false);
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (IOException ex)
            {
                lastError = ex;
                await Task.Delay(50 * (attempt + 1), cancellationToken).ConfigureAwait(false);
            }
        }
        throw new IOException($"價差快照檔持續被占用：{filePath}", lastError);
    }

    public static SpreadSnapshotPeriod GetPeriod(DateTime time)
    {
        DateTime tradingDate = time.Hour < 6 ? time.Date.AddDays(-1) : time.Date;
        return new SpreadSnapshotPeriod(tradingDate, "0600", "0600");
    }

    public static string FormatLine(ProductSpreadDefinition definition, ProductSpreadSnapshot snapshot)
    {
        ProductSpreadQuote quote = snapshot.Quote;
        return string.Join(",", new[]
        {
            snapshot.SnapshotTime.ToString("yyyyMMdd HH:mm:ss", CultureInfo.InvariantCulture),
            Escape(quote.ProductName),
            FormatPrice(quote.LastPrice, definition.PxDecimal),
            quote.BidQuantity.ToString(CultureInfo.InvariantCulture),
            FormatPrice(quote.BidPrice, definition.PxDecimal),
            FormatPrice(quote.AskPrice, definition.PxDecimal),
            quote.AskQuantity.ToString(CultureInfo.InvariantCulture),
            FormatPrice(quote.LegA.BidPrice, quote.LegA.PxDecimal),
            FormatPrice(quote.LegA.AskPrice, quote.LegA.PxDecimal),
            FormatPrice(quote.LegB.BidPrice, quote.LegB.PxDecimal),
            FormatPrice(quote.LegB.AskPrice, quote.LegB.PxDecimal)
        });
    }

    private string ResolveDirectory(string configuredPath)
    {
        string path = string.IsNullOrWhiteSpace(configuredPath) ? @".\SpreadSnapshots" : configuredPath;
        return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(_configDirectory, path));
    }

    private static string FormatPrice(decimal? value, int decimals)
    {
        return value.HasValue
            ? value.Value.ToString($"F{Math.Clamp(decimals, 0, 8)}", CultureInfo.InvariantCulture)
            : string.Empty;
    }

    private static string Escape(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\r') && !value.Contains('\n'))
            return value;
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private static string SanitizeFileName(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string sanitized = new(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "Spread" : sanitized;
    }

    public void Dispose() => _writeLock.Dispose();
}

public readonly record struct SpreadSnapshotPeriod(DateTime FileDate, string StartLabel, string EndLabel);
