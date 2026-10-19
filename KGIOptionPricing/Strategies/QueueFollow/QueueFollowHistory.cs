using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using System.Threading.Tasks;
using KGIOptionPricing.OrderManagement.Core;

namespace KGIOptionPricing.Strategies;

public enum QueueFollowHistoryEventType
{
    StrategyStarted,
    StrategyStopRequested,
    MarketChanged,
    Decision,
    OrderCommand,
    OrderReport,
    Fill,
    Error,
    StateChanged
}

/// <summary>可供事後重播與調整策略參數的單筆不可變歷程。</summary>
public sealed class QueueFollowHistoryEntry
{
    public int SchemaVersion { get; init; } = 2;
    public string RunId { get; init; } = string.Empty;
    public long Sequence { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
    public QueueFollowHistoryEventType EventType { get; init; }
    public string StrategyId { get; init; } = string.Empty;
    public string OrderId { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public string Exchange { get; init; } = string.Empty;
    public string ContractMonth { get; init; } = string.Empty;
    public BuySell Side { get; init; }
    public QueueFollowState State { get; init; }
    public string Action { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public decimal CurrentOrderPrice { get; init; }
    public decimal TargetPrice { get; init; }
    public decimal PriceLimit { get; init; }
    public int TargetQuantity { get; init; }
    public int FilledQuantity { get; init; }
    public int RemainingQuantity { get; init; }
    public int InitialTargetAheadQuantity { get; init; }
    public int RepriceAheadThreshold { get; init; }
    public int RepriceTargetAheadQuantity { get; init; }
    public int SamePriceAheadQuantity { get; init; }
    public int BetterPriceQuantity { get; init; }
    public decimal LastPrice { get; init; }
    public DateTime? MarketReceivedAt { get; init; }
    public decimal[] BidPrices { get; init; } = Array.Empty<decimal>();
    public int[] BidQuantities { get; init; } = Array.Empty<int>();
    public decimal[] AskPrices { get; init; } = Array.Empty<decimal>();
    public int[] AskQuantities { get; init; } = Array.Empty<int>();
    public OrderStatus? OrderStatus { get; init; }
    public decimal ReportedOrderPrice { get; init; }
    public int ReportedOrderQuantity { get; init; }
    public int ReportedFilledQuantity { get; init; }
    public int ReportedRemainingQuantity { get; init; }
    public string Cnt { get; init; } = string.Empty;
    public string OrgCnt { get; init; } = string.Empty;
    public string OrdNo { get; init; } = string.Empty;
    public string WebId { get; init; } = string.Empty;
    public string ErrorCode { get; init; } = string.Empty;
}

public interface IQueueFollowHistorySink
{
    void Record(QueueFollowHistoryEntry entry);
}

public sealed class NullQueueFollowHistorySink : IQueueFollowHistorySink
{
    public static NullQueueFollowHistorySink Instance { get; } = new();
    private NullQueueFollowHistorySink() { }
    public void Record(QueueFollowHistoryEntry entry) { }
}

/// <summary>
/// 非同步 JSON Lines 記錄器。一行是一個完整事件，依本機日期分檔，方便後續串流分析與策略重播。
/// </summary>
public sealed class JsonLineQueueFollowHistorySink : IQueueFollowHistorySink, IDisposable
{
    private readonly Channel<QueueFollowHistoryEntry> _channel = Channel.CreateUnbounded<QueueFollowHistoryEntry>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly Task _writerTask;
    private readonly Action<string>? _error;
    private bool _disposed;
    private volatile bool _failed;

    public string DirectoryPath { get; }

    public JsonLineQueueFollowHistorySink(string directoryPath, Action<string>? error = null)
    {
        if (string.IsNullOrWhiteSpace(directoryPath)) throw new ArgumentException("歷程目錄不可空白。", nameof(directoryPath));
        DirectoryPath = Path.GetFullPath(directoryPath);
        _error = error;
        _writerTask = Task.Run(WriterLoopAsync);
    }

    public void Record(QueueFollowHistoryEntry entry)
    {
        if (_disposed || _failed) return;
        if (!_channel.Writer.TryWrite(entry))
            _error?.Invoke("價量跟隨歷程佇列已關閉，事件未寫入。");
    }

    private async Task WriterLoopAsync()
    {
        StreamWriter? writer = null;
        DateTime currentDate = DateTime.MinValue;
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            await foreach (QueueFollowHistoryEntry entry in _channel.Reader.ReadAllAsync())
            {
                DateTime localDate = entry.Timestamp.LocalDateTime.Date;
                if (writer == null || localDate != currentDate)
                {
                    if (writer != null) await writer.DisposeAsync();
                    currentDate = localDate;
                    string path = Path.Combine(DirectoryPath, $"queue-follow-{currentDate:yyyyMMdd}.jsonl");
                    var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 65536, useAsync: true);
                    writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
                }
                await writer.WriteLineAsync(JsonSerializer.Serialize(entry, _jsonOptions));
            }
        }
        catch (Exception ex)
        {
            _failed = true;
            _error?.Invoke("價量跟隨歷程寫入失敗：" + ex.Message);
        }
        finally
        {
            if (writer != null) await writer.DisposeAsync();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _channel.Writer.TryComplete();
        try
        {
            if (!_writerTask.Wait(TimeSpan.FromSeconds(5)))
                _error?.Invoke("價量跟隨歷程尚未於五秒內完成寫入。");
        }
        catch (Exception ex)
        {
            _error?.Invoke("價量跟隨歷程關閉失敗：" + ex.GetBaseException().Message);
        }
    }
}
