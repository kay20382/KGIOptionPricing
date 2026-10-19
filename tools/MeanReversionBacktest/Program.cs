using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KGIOptionPricing.Strategies.MeanReversionScalper;
using KGIOptionPricing.StrategyFramework;
using KGIOptionPricing.StrategyFramework.Storage;
using KGIOptionPricing.StrategyFramework.Execution;
using KGIOptionPricing.OrderManagement.Core;

namespace MeanReversionBacktest;

internal static class Program
{
    private static readonly int[] MinimumRanges = [4, 6, 8, 10];
    private sealed record ReplayOptions(bool UseMovingAverage, string ReferenceProductId,
        int ShiftTicks, int MaximumRangeTicks, int[] MinimumRangeTicks)
    {
        public string Mode => UseMovingAverage ? "paired-ma" : "paired-ma-off";
    }
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private sealed class ReplayClock : IEventDrivenStrategyClock
    {
        public DateTimeOffset Now { get; private set; }
        public void Advance(DateTimeOffset time) => Now = time;
    }
    private sealed record Fill(ExecutionEvent Execution, string Regime);
    private sealed record Trade(string File, string Mode, int MinRTicks, string CycleId, string Regime,
        DateTimeOffset EntryTime, DateTimeOffset ExitTime, string Direction,
        decimal EntryPrice, decimal ExitPrice, decimal GrossYen, decimal FeesYen, decimal NetYen,
        string ExitRole);
    private sealed record RunResult(string File, string Mode, int MinRTicks, long InputBytes,
        long MarketEvents, DateTimeOffset? FirstEvent, DateTimeOffset? LastEvent,
        int Fills, int CompletedTrades, int OpenQuantity, decimal LedgerNetYen,
        decimal ClosedTradeNetYen, string Status);

    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        if (args.Any(x => x.StartsWith("--pqc-", StringComparison.Ordinal)))
        {
            try { return args.Contains("--pqc-self-test") ? PqcResearchSmoke.Run() : await PqcResearch.Run(args); }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
        if (args.Contains("--self-test")) return BacktestSmoke.Run();
        string? input = Value(args, "--input");
        string? output = Value(args, "--output");
        if (input == null || output == null)
        {
            Console.Error.WriteLine("Usage: MeanReversionBacktest --input <NikkeiResearch directory> --output <new result directory> [--max-files N] [--use-ma] [--ma-reference 225M] [--shift-ticks 1] [--min-range 8] [--max-range 20]");
            return 2;
        }
        int shift = int.TryParse(Value(args, "--shift-ticks"), out int parsedShift) ? parsedShift : 1;
        int maximumRange = int.TryParse(Value(args, "--max-range"), out int parsedMaximum) ? parsedMaximum : 20;
        int[] minimumRanges = int.TryParse(Value(args, "--min-range"), out int parsedMinimum)
            ? [parsedMinimum] : MinimumRanges;
        if (shift is < 0 or > 20 || maximumRange <= 0 || minimumRanges.Any(x => x <= 0 || x > maximumRange))
            throw new ArgumentOutOfRangeException(nameof(args), "Invalid N or R tick bounds.");
        var options = new ReplayOptions(args.Contains("--use-ma"),
            Value(args, "--ma-reference") ?? "225M", shift, maximumRange, minimumRanges);
        input = Path.GetFullPath(input);
        output = Path.GetFullPath(output);
        if (!Directory.Exists(input)) throw new DirectoryNotFoundException(input);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new IOException("Output directory must be new or empty: " + output);
        Directory.CreateDirectory(output);
        int max = int.TryParse(Value(args, "--max-files"), out int count) ? count : int.MaxValue;
        string[] files = Directory.GetFiles(input, "*.jsonl").OrderBy(x => x, StringComparer.Ordinal).Take(max).ToArray();
        if (files.Length == 0) throw new InvalidDataException("No JSONL archives found.");
        var results = new List<RunResult>();
        var trades = new List<Trade>();
        foreach (string file in files)
        {
            Console.WriteLine($"REPLAY {Path.GetFileName(file)} ({new FileInfo(file).Length:N0} bytes)");
            try { await ReplayOne(file, output, results, trades, options); }
            catch (Exception error)
            {
                Console.Error.WriteLine($"FAILED {Path.GetFileName(file)}: {error}");
                foreach (int r in options.MinimumRangeTicks)
                    results.Add(new RunResult(Path.GetFileName(file), options.Mode, r, new FileInfo(file).Length,
                        0, null, null, 0, 0, 0, 0, 0, error.GetType().Name + ": " + error.Message));
            }
            WriteOutputs(output, results, trades);
        }
        return results.Any(x => x.Status != "complete") ? 1 : 0;
    }

    private static async Task ReplayOne(string path, string output, List<RunResult> results, List<Trade> trades,
        ReplayOptions options)
    {
        var fileResults = new List<RunResult>();
        var fileTrades = new List<Trade>();
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in File.ReadLines(path))
        {
            if (!MarketDataArchiveCodec.TryDeserialize(line, out MarketEvent item)) continue;
            if (item.ProductId is "225MC" or "225M" or "225" ||
                string.Equals(item.ProductId, options.ReferenceProductId, StringComparison.OrdinalIgnoreCase))
                names.TryAdd(item.ProductId, item.Symbol);
            if (names.ContainsKey("225MC") &&
                (!options.UseMovingAverage || names.ContainsKey(options.ReferenceProductId))) break;
        }
        if (!names.ContainsKey("225MC")) throw new InvalidDataException("Missing tradable 225MC in archive.");
        if (options.UseMovingAverage && !names.ContainsKey(options.ReferenceProductId))
            throw new InvalidDataException($"Missing MA reference {options.ReferenceProductId} in archive.");
        ResolvedProduct Product(string id) => new()
        {
            ProductId = id, Symbol = names[id], ContractMonth = "ARCHIVE",
            TickSize = StrategyProductSpecifications.ResolveTickSize(id),
            ContractMultiplier = StrategyProductSpecifications.ResolveContractMultiplier(id)
        };
        var universe = new ResolvedStrategyUniverse
        {
            Tradable = Product("225MC"), References = names.Keys.Where(id => id != "225MC")
                .Select(Product).ToArray()
        };
        var clock = new ReplayClock();
        var regime = new RegimeClassifier();
        using var source = new ArchiveReplayMarketDataSource(path);
        // Subscribe first: labels are updated from only data known at execution time.
        source.MarketEventReceived += regime.Observe;
        using var host = new StrategyHost(source, clock);
        var schedule = MeanReversionSessionSchedule.Parse(new Dictionary<string, string>());
        DateTimeOffset? previous = null, first = null, last = null;
        long events = 0;
        source.BeforeMarketEvent += time =>
        {
            if (previous.HasValue)
                foreach (DateTimeOffset stop in schedule.StopsBetween(previous.Value, time)) host.Pulse(stop);
            previous = time;
        };
        source.MarketEventReceived += item =>
        {
            events++;
            first ??= item.Timestamp;
            last = item.Timestamp;
        };

        string fileName = Path.GetFileName(path);
        var recorders = new Dictionary<string, SimulationRunRecorder>();
        var allFills = new Dictionary<string, List<Fill>>();
        var definitions = new Dictionary<string, StrategyDefinition>();
        try
        {
            foreach (int r in options.MinimumRangeTicks)
            {
                string instance = "MR-R" + r.ToString(CultureInfo.InvariantCulture);
                var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["HorizonSeconds"] = "600", ["F"] = "0.6", ["K"] = "1.2",
                    ["MinimumRangeTicks"] = r.ToString(CultureInfo.InvariantCulture),
                    ["MaximumRangeTicks"] = options.MaximumRangeTicks.ToString(CultureInfo.InvariantCulture),
                    ["UseMovingAverage"] = options.UseMovingAverage.ToString(),
                    ["MovingAverageReferenceProductId"] = options.ReferenceProductId,
                    ["TrendShiftTicks"] = options.ShiftTicks.ToString(CultureInfo.InvariantCulture),
                    ["MaximumSpreadTicks"] = "1", ["FreshnessSeconds"] = "2", ["CooldownSeconds"] = "5"
                };
                var definition = new StrategyDefinition
                {
                    StrategyType = MeanReversionScalperFactory.TypeName,
                    InstanceId = instance, Parameters = parameters
                };
                definitions[instance] = definition;
                allFills[instance] = [];
                var recorder = new SimulationRunRecorder(Path.Combine(output,
                    Path.GetFileNameWithoutExtension(path), instance));
                recorders[instance] = recorder;
                host.Add(new MeanReversionScalperStrategy(definition), universe, 36m,
                    new CrossPricePaperExecutionService(36m, clock, fillMarketOnBook: true));
            }
            host.ExecutionOccurred += fill =>
            {
                recorders[fill.StrategyInstanceId].Record(fill);
                allFills[fill.StrategyInstanceId].Add(new Fill(fill, regime.Label(fill.Timestamp)));
                StrategySnapshot? snapshot = host.GetSnapshots().FirstOrDefault(x => x.InstanceId == fill.StrategyInstanceId);
                if (snapshot != null) recorders[fill.StrategyInstanceId].Record(snapshot);
            };
            host.OrderLifecycleOccurred += item => recorders[item.StrategyInstanceId].Record(item);
            // Decisions are not emitted for every quote; shared recorder deduplicates repeats.
            host.DecisionObserved += item => recorders[item.StrategyInstanceId].Record(item);
            await source.ReplayAsync();
            // Incomplete captures are not force-filled at the last print. Open P/L stays marked separately.
            host.StopAll("歷史資料結束（不假造成交）");
            foreach (StrategySnapshot snapshot in host.GetSnapshots()) recorders[snapshot.InstanceId].Record(snapshot);
            foreach (int r in options.MinimumRangeTicks)
            {
                string instance = "MR-R" + r.ToString(CultureInfo.InvariantCulture);
                PositionSnapshot position = host.Ledger.GetPosition(instance, names["225MC"]);
                List<Trade> paired = Pair(fileName, options.Mode, r, allFills[instance]);
                fileTrades.AddRange(paired);
                decimal closed = paired.Sum(x => x.NetYen);
                bool lateExit = paired.Any(x =>
                {
                    MeanReversionSessionWindow? window = schedule.Resolve(x.EntryTime);
                    return window == null || x.ExitTime >= window.MarketClose;
                });
                string status = position.NetQuantity != 0 ? "open-position-end-of-file" :
                    closed != position.NetPnl ? "ledger-pair-mismatch" :
                    lateExit ? "late-exit-after-market-close" : "complete";
                fileResults.Add(new RunResult(fileName, options.Mode, r, new FileInfo(path).Length, events, first, last,
                    allFills[instance].Count, paired.Count, position.NetQuantity,
                    position.NetPnl, closed, status));
                var info = new FileInfo(path);
                DateTimeOffset now = DateTimeOffset.Now;
                string paramText = JsonSerializer.Serialize(definitions[instance].Parameters);
                recorders[instance].FinalizeManifest(new RunManifest
                {
                    RunId = Path.GetFileNameWithoutExtension(path) + "-" + instance,
                    StrategyType = MeanReversionScalperFactory.TypeName, InstanceId = instance,
                    Mode = StrategyRunMode.HistoricalReplay, StartedAt = first ?? now, EndedAt = now,
                    RawParameters = definitions[instance].Parameters,
                    EffectiveParameters = definitions[instance].Parameters,
                    NormalizedParameters = paramText,
                    ParameterSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(paramText))),
                    InputArchive = new InputArchiveIdentity
                    {
                        AbsolutePath = Path.GetFullPath(path), SizeBytes = info.Length,
                        LastWriteTimeUtc = info.LastWriteTimeUtc, FirstEventAt = first, LastEventAt = last,
                        MissingFields = ["Sha256"]
                    },
                    Universe = universe.AllProducts.Select(x => new RunProductIdentity(x.ProductId, x.Symbol,
                        x.ContractMonth, x.TickSize, x.ContractMultiplier)).ToArray(),
                    CommissionPerContract = 36m,
                    ExecutionAssumptions = new Dictionary<string, string>
                    {
                        ["fillModel"] = "strict-cross limit/stop at order price; market on next valid contra book or trade",
                        ["slippageModel"] = "base=0; stress computed in report",
                        ["latencyModel"] = "none"
                    },
                    MissingProvenanceFields = ["GitCommit", "InputSha256", "LatencyModel", "RealSlippage"]
                });
                recorders[instance].FinalizeSummary(now);
            }
        }
        finally { foreach (SimulationRunRecorder recorder in recorders.Values) recorder.Dispose(); }
        results.AddRange(fileResults);
        trades.AddRange(fileTrades);
    }

    private static List<Trade> Pair(string file, string mode, int r, List<Fill> fills)
    {
        var trades = new List<Trade>();
        foreach (IGrouping<string, Fill> cycle in fills.GroupBy(x => x.Execution.CycleId))
        {
            Fill? entry = cycle.FirstOrDefault(x => x.Execution.Role is PaperOrderRole.EntryLong or PaperOrderRole.EntryShort);
            Fill? exit = cycle.FirstOrDefault(x => x.Execution.Role is PaperOrderRole.TakeProfit or PaperOrderRole.StopLoss or PaperOrderRole.Flatten);
            if (entry == null || exit == null || entry.Execution.Quantity != exit.Execution.Quantity) continue;
            bool longSide = entry.Execution.Side == BuySell.Buy;
            decimal gross = (longSide ? exit.Execution.Price - entry.Execution.Price :
                entry.Execution.Price - exit.Execution.Price) * entry.Execution.Quantity * 10m;
            decimal fees = entry.Execution.Commission + exit.Execution.Commission;
            trades.Add(new Trade(file, mode, r, cycle.Key, entry.Regime, entry.Execution.Timestamp,
                exit.Execution.Timestamp, longSide ? "long" : "short", entry.Execution.Price,
                exit.Execution.Price, gross, fees, gross - fees, exit.Execution.Role.ToString()));
        }
        return trades;
    }

    private static void WriteOutputs(string output, List<RunResult> results, List<Trade> trades)
    {
        File.WriteAllText(Path.Combine(output, "daily-summary.json"), JsonSerializer.Serialize(results, Json));
        File.WriteAllText(Path.Combine(output, "trades.json"), JsonSerializer.Serialize(trades, Json));
        var regimeSummary = trades.GroupBy(x => (x.MinRTicks, x.Regime)).Select(group => new
        {
            group.Key.MinRTicks, group.Key.Regime, Trades = group.Count(),
            WinRate = group.Count(x => x.NetYen > 0) / (decimal)group.Count(),
            NetYen = group.Sum(x => x.NetYen), AverageYen = group.Average(x => x.NetYen),
            ZeroSlippageYen = group.Sum(x => x.NetYen),
            OneTickEachFillYen = group.Sum(x => x.NetYen - 100m),
            TwoTicksEachFillYen = group.Sum(x => x.NetYen - 200m)
        }).OrderBy(x => x.MinRTicks).ThenBy(x => x.Regime).ToArray();
        File.WriteAllText(Path.Combine(output, "regime-summary.json"), JsonSerializer.Serialize(regimeSummary, Json));
    }

    private static string? Value(string[] args, string key)
    {
        int at = Array.IndexOf(args, key);
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }
}
