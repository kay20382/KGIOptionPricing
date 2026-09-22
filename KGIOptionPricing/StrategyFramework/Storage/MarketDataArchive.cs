using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace KGIOptionPricing.StrategyFramework.Storage;

public static class MarketDataArchiveCodec
{
    private static readonly JsonSerializerOptions CurrentJson = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Reads both the strategy archive schema and the existing NikkeiResearch normalized rows.</summary>
    public static bool TryDeserialize(string line, out MarketEvent marketEvent)
    {
        marketEvent = new MarketEvent();
        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            if (root.TryGetProperty("productId", out _))
            {
                MarketEvent? current = JsonSerializer.Deserialize<MarketEvent>(line, CurrentJson);
                if (current == null) return false;
                marketEvent = current;
                return true;
            }

            if (!root.TryGetProperty("Kind", out JsonElement kindElement) ||
                !root.TryGetProperty("Product", out JsonElement productElement) ||
                !root.TryGetProperty("Symbol", out JsonElement symbolElement)) return false;
            string kind = kindElement.GetString() ?? string.Empty;
            if (kind != "trade" && !kind.StartsWith("depth", StringComparison.Ordinal)) return false;
            DateTimeOffset timestamp = root.TryGetProperty("Time", out JsonElement timeElement) &&
                timeElement.TryGetDateTimeOffset(out DateTimeOffset parsedTime) ? parsedTime : DateTimeOffset.MinValue;
            marketEvent = new MarketEvent
            {
                Sequence = root.TryGetProperty("Sequence", out JsonElement sequence) && sequence.TryGetInt64(out long seq) ? seq : 0,
                Timestamp = timestamp,
                Symbol = symbolElement.GetString() ?? string.Empty,
                ProductId = productElement.GetString() ?? string.Empty,
                Kind = kind == "trade" ? MarketEventKind.Trade : MarketEventKind.Book,
                LastPrice = Decimal(root, "Last"),
                TradeQuantity = Int(root, "TradeQty"),
                BidPrices = Decimals(root, "Bid"),
                BidQuantities = Ints(root, "BidQty"),
                AskPrices = Decimals(root, "Ask"),
                AskQuantities = Ints(root, "AskQty")
            };
            return !string.IsNullOrWhiteSpace(marketEvent.Symbol);
        }
        catch (JsonException) { return false; }
    }

    private static decimal Decimal(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetDecimal(out decimal result) ? result : 0m;
    private static int Int(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out int result) ? result : 0;
    private static decimal[] Decimals(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(x => x.TryGetDecimal(out decimal item) ? item : 0m).ToArray()
            : Array.Empty<decimal>();
    private static int[] Ints(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(x => x.TryGetInt32(out int item) ? item : 0).ToArray()
            : Array.Empty<int>();
}

public sealed class MarketDataArchiveWriter : IDisposable
{
    private sealed record BarrierRequest(TaskCompletionSource<ArchiveWriterSnapshot> Completion);
    private readonly Channel<object> _channel = Channel.CreateBounded<object>(
        new BoundedChannelOptions(200000) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly Task _writer;
    private long _written;
    private long _dropped;
    private bool _disposed;

    public string DirectoryPath { get; }
    public string EventFilePath { get; }

    public MarketDataArchiveWriter(string rootDirectory, string runId)
    {
        DirectoryPath = Path.Combine(Path.GetFullPath(rootDirectory), DateTime.Now.ToString("yyyyMMdd"), runId);
        Directory.CreateDirectory(DirectoryPath);
        EventFilePath = Path.Combine(DirectoryPath, "market-events.jsonl");
        _writer = Task.Run(WriteLoopAsync);
    }

    public void Record(MarketEvent marketEvent)
    {
        if (_disposed || !_channel.Writer.TryWrite(marketEvent)) Interlocked.Increment(ref _dropped);
    }

    public ArchiveWriterSnapshot CaptureBarrier()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var completion = new TaskCompletionSource<ArchiveWriterSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _channel.Writer.WriteAsync(new BarrierRequest(completion)).AsTask().GetAwaiter().GetResult();
        return completion.Task.GetAwaiter().GetResult();
    }

    private async Task WriteLoopAsync()
    {
        await using var stream = new FileStream(EventFilePath, FileMode.Append, FileAccess.Write, FileShare.Read, 65536, true);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
        await foreach (object item in _channel.Reader.ReadAllAsync())
        {
            if (item is MarketEvent marketEvent)
            {
                await writer.WriteLineAsync(JsonSerializer.Serialize(marketEvent, _json));
                Interlocked.Increment(ref _written);
            }
            else if (item is BarrierRequest barrier)
            {
                try
                {
                    await writer.FlushAsync();
                    await stream.FlushAsync();
                    var info = new FileInfo(EventFilePath);
                    using var input = new FileStream(EventFilePath, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);
                    string hash = Convert.ToHexString(await SHA256.HashDataAsync(input));
                    barrier.Completion.TrySetResult(new ArchiveWriterSnapshot(
                        EventFilePath, info.Length, info.LastWriteTimeUtc, hash));
                }
                catch (Exception exception) { barrier.Completion.TrySetException(exception); }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _channel.Writer.TryComplete();
        try { _writer.Wait(TimeSpan.FromSeconds(10)); } catch { }
        var manifest = new
        {
            schemaVersion = 1,
            createdAt = DateTimeOffset.Now,
            eventFile = Path.GetFileName(EventFilePath),
            written = Interlocked.Read(ref _written),
            dropped = Interlocked.Read(ref _dropped),
            complete = Interlocked.Read(ref _dropped) == 0
        };
        File.WriteAllText(Path.Combine(DirectoryPath, "manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
    }
}

public sealed record ArchiveWriterSnapshot(
    string EventFilePath,
    long Length,
    DateTimeOffset LastWriteTimeUtc,
    string Sha256);

public sealed class RecordingMarketEventSource : IMarketEventSource
{
    private readonly IMarketEventSource _inner;
    private readonly MarketDataArchiveWriter _archive;
    public event Action<MarketEvent>? MarketEventReceived;
    public string ArchivePath => _archive.DirectoryPath;
    public ArchiveWriterSnapshot CaptureArchiveBarrier() => _archive.CaptureBarrier();

    public RecordingMarketEventSource(IMarketEventSource inner, MarketDataArchiveWriter archive)
    {
        _inner = inner;
        _archive = archive;
        _inner.MarketEventReceived += Forward;
    }

    private void Forward(MarketEvent marketEvent)
    {
        _archive.Record(marketEvent);
        MarketEventReceived?.Invoke(marketEvent);
    }

    public void Subscribe(IEnumerable<ResolvedProduct> products) => _inner.Subscribe(products);
    public void Dispose()
    {
        _inner.MarketEventReceived -= Forward;
        _archive.Dispose();
        _inner.Dispose();
    }
}

public sealed class ArchiveReplayMarketDataSource : IMarketEventSource
{
    private readonly string _eventFilePath;
    private bool _disposed;

    public event Action<MarketEvent>? MarketEventReceived;
    public ArchiveReplayMarketDataSource(string eventFilePath) => _eventFilePath = Path.GetFullPath(eventFilePath);
    public void Subscribe(IEnumerable<ResolvedProduct> products) { }

    public async Task ReplayAsync(double speed = 0d, CancellationToken cancellationToken = default)
    {
        DateTimeOffset? previous = null;
        using var stream = new FileStream(_eventFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (!_disposed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? line = await reader.ReadLineAsync(cancellationToken);
            if (line == null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (!MarketDataArchiveCodec.TryDeserialize(line, out MarketEvent marketEvent)) continue;
            if (speed > 0d && previous.HasValue)
            {
                double delay = (marketEvent.Timestamp - previous.Value).TotalMilliseconds / speed;
                if (delay > 1d) await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(delay, 10000d)), cancellationToken);
            }
            previous = marketEvent.Timestamp;
            MarketEventReceived?.Invoke(marketEvent);
        }
    }

    public void Dispose() => _disposed = true;
}
