using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;

namespace KGIOptionPricing.Research;

// Records received events, not an exchange-complete order-by-order tape.
public sealed class NikkeiResearchRecorder : IDisposable
{
    private readonly KGIQuoteService _quotes;
    private readonly Action<string> _log;
    private readonly string _directory;
    private readonly string _session = Guid.NewGuid().ToString("N");
    private sealed record Entry(DateTime Date, object Row);
    private readonly Channel<Entry> _queue = Channel.CreateBounded<Entry>(
        new BoundedChannelOptions(100000) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _symbols = new();
    private Task? _writer;
    private long _sequence, _dropped;
    private int _started;
    public NikkeiResearchRecorder(KGIQuoteService quotes, string directory, Action<string> log)
    { _quotes = quotes; _directory = directory; _log = log; }
    public void Start()
    {
        if (Volatile.Read(ref _started) != 0 || !TickSavingEnabled()) return;
        if (Interlocked.Exchange(ref _started, 1) != 0) return;
        _writer = Task.Run(WriteAsync);
        _quotes.OnResearchMarketEvent += Record;
        _quotes.OnQuoteReceived += RecordPacket;
        _quotes.OnSymbolRepositoryLoaded += Subscribe;
        _quotes.OnLogMessage += Status;
        Enqueue(new { Kind = "session", Time = DateTimeOffset.Now, Session = _session,
            StopwatchFrequency = Stopwatch.Frequency, SchemaVersion = 1,
            BookAlgorithm = "price-keyed-v2" });
        if (SymbolRepository.Instance.IsInitialized) Subscribe();
        _log($"[TICK儲存] 已開啟：{Path.GetFullPath(_directory)}");
    }
    private bool TickSavingEnabled()
    {
        string config = Path.Combine(AppContext.BaseDirectory, "config.json");
        try
        {
            if (!File.Exists(config)) return true;
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(config));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new JsonException("config.json 必須是 JSON 物件");
            if (!document.RootElement.TryGetProperty("SaveTickData", out JsonElement value)) return true;
            if (value.ValueKind == JsonValueKind.True) return true;
            if (value.ValueKind != JsonValueKind.False)
                throw new JsonException("SaveTickData 必須是 true 或 false");
            _log("[TICK儲存] 已關閉（SaveTickData=false），不建立行情檔。");
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        {
            _log($"[TICK儲存] 設定讀取失敗，未啟動儲存：{error.Message}");
        }
        return false;
    }
    private void Subscribe()
    {
        foreach (string product in new[] { "225MC", "225M", "225" })
        {
            var contract = SymbolRepository.Instance.GetHotMonthContract(product);
            if (contract == null) { _log($"[研究] 找不到 {product} 熱門月"); continue; }
            _symbols[contract.Symbol] = product;
            Enqueue(new { Kind = "contract", Time = DateTimeOffset.Now, Product = product,
                contract.Symbol, contract.ComYM, contract.ContractValue, Tick = product == "225" ? 10m : 5m });
            _quotes.Subscribe(contract.Symbol);
        }
    }
    private void Status(string message)
    {
        if (message.Contains("行情狀態"))
            Enqueue(new { Kind = "status", Time = DateTimeOffset.Now, Message = message });
    }
    // API-decoded market messages, not TCP bytes. Never capture login/account packets.
    private void RecordPacket(Package.PackageBase packet)
    {
        object? payload = packet switch
        {
            Package.PI40080 p => new { p.Symbol, p.Side, p.Position, p.TopMark, p.Price, p.Quantity },
            Package.PI40090 p => new { p.Symbol,
                Bid = p.BUY_DEPTH.ToArray().Select(x => new { x.PRICE, x.QUANTITY }).ToArray(),
                Ask = p.SELL_DEPTH.ToArray().Select(x => new { x.PRICE, x.QUANTITY }).ToArray() },
            Package.PI40020 p => new { p.Symbol, p.Price, p.Quantity, p.TotalQty, p.TradeTime },
            _ => null
        };
        if (payload == null) return;
        Enqueue(new { Kind = "api_packet", Time = DateTimeOffset.Now,
            Monotonic = Stopwatch.GetTimestamp(), PacketType = packet.GetType().Name,
            RawSequence = Interlocked.Increment(ref _rawSequence),
            Dropped = Interlocked.Read(ref _dropped), Payload = payload });
    }
    private long _rawSequence;
    private void Record(string symbol, string kind, KGIQuoteService.MarketDataStruct data)
    {
        if (!_symbols.TryGetValue(symbol, out string? product)) return;
        Enqueue(new { Kind = kind, Time = DateTimeOffset.Now, Monotonic = Stopwatch.GetTimestamp(),
            Sequence = Interlocked.Increment(ref _sequence), Dropped = Interlocked.Read(ref _dropped),
            Product = product, Symbol = symbol, Tick = product == "225" ? 10m : 5m,
            SourceTimeRaw = data.iUpdateTime, SourceTime = data.UpdateTime.ToString(),
            Bid = data.BuyPx.ToArray(), Ask = data.SellPx.ToArray(),
            BidQty = data.BuyQty.ToArray(), AskQty = data.SellQty.ToArray(),
            Last = data.MatchcPx, TradeQty = kind == "trade" ? data.MatchQty : (int?)null,
            TotalQty = data.MatchTotalQty });
    }
    private void Enqueue(object row)
    {
        if (!_queue.Writer.TryWrite(new Entry(DateTime.Now.AddHours(-6).Date, row)))
            Interlocked.Increment(ref _dropped);
    }
    private async Task WriteAsync()
    {
        StreamWriter? writer = null;
        DateTime date = default;
        try
        {
            Directory.CreateDirectory(_directory);
            await foreach (Entry entry in _queue.Reader.ReadAllAsync())
            {
                DateTime tradingDate = entry.Date;
                if (writer == null || date != tradingDate)
                {
                    if (writer != null) await writer.DisposeAsync();
                    date = tradingDate;
                    writer = new StreamWriter(new FileStream(Path.Combine(_directory,
                        $"{date:yyyyMMdd}_nikkei_{_session}.jsonl"), FileMode.Append, FileAccess.Write, FileShare.Read));
                    writer.AutoFlush = true;
                }
                await writer.WriteLineAsync(JsonSerializer.Serialize(entry.Row));
            }
        }
        catch (Exception ex) { _log($"[研究記錄失敗] {ex.Message}; dropped={Interlocked.Read(ref _dropped)}"); }
        finally { if (writer != null) await writer.DisposeAsync(); }
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _started, 0) == 0) return;
        _quotes.OnResearchMarketEvent -= Record;
        _quotes.OnQuoteReceived -= RecordPacket;
        _quotes.OnSymbolRepositoryLoaded -= Subscribe;
        _quotes.OnLogMessage -= Status;
        _queue.Writer.TryComplete();
        _writer?.GetAwaiter().GetResult();
        _log($"[研究] 記錄結束，丟棄事件數={Interlocked.Read(ref _dropped)}");
    }
}
