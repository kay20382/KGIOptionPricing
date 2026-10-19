using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using NikkeiRl;

internal static class ReadinessProbe
{
    internal sealed record Readiness(bool BookFresh,bool PathReady,bool FlowReady,bool SnapshotReady);
    static object? Field(ResearchFeatureAccumulator state,string name)=>typeof(ResearchFeatureAccumulator)
        .GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)?.GetValue(state);
    internal static Readiness Inspect(ResearchFeatureAccumulator state,DateTimeOffset at)
    {
        var book=(DateTimeOffset?)Field(state,"bookAt");
        var known=(DateTimeOffset?)Field(state,"flowKnownFrom");
        var flowAt=(DateTimeOffset?)Field(state,"flowBookAt");
        bool flowBook=(bool)(Field(state,"flowBookReady")??throw new InvalidOperationException("Missing frozen flow field"));
        var mids=(IEnumerable<(DateTimeOffset At,double Mid)>)(Field(state,"mids")??throw new InvalidOperationException("Missing frozen path field"));
        var path=mids.Where(p=>p.At>=at.AddSeconds(-30)).ToArray();
        bool fresh=state.BookReady&&book.HasValue&&(at-book.Value).TotalSeconds is >=0 and <=2;
        bool continuous=path.Length==11&&path[0].At==at.AddSeconds(-30)&&path.Zip(path.Skip(1)).All(p=>(p.Second.At-p.First.At).TotalSeconds==3);
        bool flow=flowBook&&known.HasValue&&(at-known.Value).TotalSeconds>=5&&flowAt.HasValue&&(at-flowAt.Value).TotalSeconds is >=0 and <=2;
        if(state.SnapshotReady!=(fresh&&continuous&&flow))throw new InvalidOperationException("Readiness decomposition differs from frozen C#");
        return new(fresh,continuous,flow,state.SnapshotReady);
    }
    sealed record Frame(DateTimeOffset At,bool Valid,double[] Features,double[] ResearchExtras);
    static string Hash(string path){using var reader=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(reader));}
    internal static void Audit(string[] args)
    {
        if(args.Length!=5)throw new ArgumentException("SOURCE CACHE YYYYMMDD SOURCE_SHA256 OUTPUT_JSON");
        string source=args[0],cache=args[1],date=args[2],expected=args[3],output=args[4];
        if(File.Exists(output))throw new IOException("Immutable diagnostic output already exists");
        var before=new FileInfo(source);long size=before.Length;var modified=before.LastWriteTimeUtc;
        using var manifest=JsonDocument.Parse(File.ReadAllText(cache+".manifest.json"));
        string cacheHash=Hash(cache);
        if(cacheHash!=manifest.RootElement.GetProperty("cacheSha256").GetString()||manifest.RootElement.GetProperty("dataGapFrames").GetInt64()!=0)
            throw new InvalidDataException("Verified continuous cache required");
        var frames=new List<Frame>();var json=new JsonSerializerOptions{PropertyNameCaseInsensitive=true};
        using(var file=File.OpenRead(cache))using(var zip=new BrotliStream(file,CompressionMode.Decompress))using(var reader=new StreamReader(zip))
        {string? line;while((line=reader.ReadLine())!=null)frames.Add(JsonSerializer.Deserialize<Frame>(line,json)!);}
        if(frames.Count!=manifest.RootElement.GetProperty("frames").GetInt64())throw new InvalidDataException("Frame count differs");
        var state=new ResearchFeatureAccumulator(5);int index=0;bool seenValid=false;
        long marketRows=0,valid=0,flowOnlyResearch=0,flowOnlyTrading=0,bookFailed=0,pathFailed=0,flowFailed=0,resets=0,activeReady=0;
        var activeMethod=typeof(ResearchFeatureAccumulator).GetMethod("IsReadyForActorFeatures");
        int[] excludedFlow=[48,49,50,51,52,53];
        double maxFeatureError=0;DateTimeOffset? previous=null;
        string datePrefix=date[..4]+"-"+date[4..6]+"-"+date[6..];
        void Emit(Frame frame)
        {
            var actual=state.Snapshot(frame.At);
            if(actual.Length!=20||frame.ResearchExtras.Length!=20)throw new InvalidDataException("Expected20 research factors");
            double error=actual.Zip(frame.ResearchExtras).Max(p=>Math.Abs(p.First-p.Second));maxFeatureError=Math.Max(maxFeatureError,error);
            if(error>1e-9)throw new InvalidDataException("Frozen C# features differ from immutable cache at "+frame.At);
            var probe=Inspect(state,frame.At);if(frame.Valid&&!probe.SnapshotReady)throw new InvalidDataException("Valid cache violates frozen readiness");
            if(activeMethod!=null)
            {
                bool ready=(bool)activeMethod.Invoke(state,[excludedFlow])!;
                if(ready!=(probe.BookFresh&&probe.PathReady))throw new InvalidDataException("Active input readiness mismatch");
                if(ready)activeReady++;
            }
            if(frame.Valid){valid++;seenValid=true;}
            if(!probe.BookFresh)bookFailed++;if(!probe.PathReady)pathFailed++;if(!probe.FlowReady)flowFailed++;
            if(probe.BookFresh&&probe.PathReady&&!probe.FlowReady)
            {
                flowOnlyResearch++;
                var f=frame.Features;
                bool quotes=f[4]>0&&f[4]<=2&&f[14]>0&&f[14]<=2&&f[7]<=2&&f[17]<=2&&Math.Abs(f[7]-f[17])<=1;
                if(seenValid&&!frame.Valid&&quotes)flowOnlyTrading++;
            }
        }
        using(var reader=new StreamReader(source))
        {
            string? line;while((line=reader.ReadLine())!=null)
            {
                if(!(line.Contains("\"Product\":\"225MC\"",StringComparison.Ordinal)||line.Contains("\"Product\":\"225M\"",StringComparison.Ordinal)))continue;
                using var doc=JsonDocument.Parse(line);var row=doc.RootElement;string kind=row.GetProperty("Kind").GetString()!;
                if(kind!="trade"&&!kind.StartsWith("depth",StringComparison.Ordinal))continue;
                string timestamp=row.GetProperty("Time").GetString()!;if(!timestamp.StartsWith(datePrefix,StringComparison.Ordinal))continue;
                var at=DateTimeOffset.Parse(timestamp,CultureInfo.InvariantCulture);
                if(at.Offset!=TimeSpan.FromHours(8))throw new InvalidDataException("Expected UTC+08");
                if(at.TimeOfDay<TimeSpan.FromHours(7.75)||at.TimeOfDay>TimeSpan.FromHours(14.75))continue;
                if(previous.HasValue&&at<previous.Value)throw new InvalidDataException("Out-of-order market event");
                if(row.TryGetProperty("Dropped",out var dropped)&&dropped.GetInt64()!=0)throw new InvalidDataException("Dropped market event");
                while(index<frames.Count&&frames[index].At<=at)Emit(frames[index++]);
                if(previous.HasValue&&(at-previous.Value).TotalSeconds>10){state=new(5);seenValid=false;resets++;}
                previous=at;marketRows++;
                if(row.GetProperty("Product").GetString()!="225MC")continue;
                if(kind=="trade")state.OnTrade(at,row.GetProperty("Last").GetDecimal(),row.GetProperty("TradeQty").GetInt32());
                else
                {
                    decimal[] Prices(string key)=>row.GetProperty(key).EnumerateArray().Select(v=>v.GetDecimal()).ToArray();
                    int[] Quantities(string key)=>row.GetProperty(key).EnumerateArray().Select(v=>v.GetInt32()).ToArray();
                    state.OnBook(at,Prices("Bid"),Prices("Ask"),Quantities("BidQty"),Quantities("AskQty"));
                }
            }
        }
        if(index!=frames.Count||resets!=0)throw new InvalidDataException("Incomplete or interrupted source cannot support this diagnostic");
        var after=new FileInfo(source);string sourceHash=Hash(source);
        if(after.Length!=size||after.LastWriteTimeUtc!=modified||sourceHash!=expected)throw new IOException("Source changed or hash differs");
        var result=new{date,source,sourceSha256=sourceHash,cache,cacheSha256=cacheHash,frozenModelAssemblySha256=Hash(typeof(ResearchFeatureAccumulator).Assembly.Location),
            frames=index,validFrames=valid,marketRows,sourceResets=resets,comparedFeatureValues=index*20,maxFeatureError,
            bookNotFreshFrames=bookFailed,pathNotReadyFrames=pathFailed,flowNotReadyFrames=flowFailed,flowOnlyResearchReadinessFailures=flowOnlyResearch,
            flowOnlyTradingBlockersAfterFirstHealthy=flowOnlyTrading,causesMayOverlap=true,
            activeReadinessApiAvailable=activeMethod!=null,activeBookPathReadyFrames=activeMethod==null?(long?)null:activeReady,
            auditedAssemblyMode=activeMethod==null?"FrozenParentBinary":"WorkingTreeSource",
            scope=activeMethod==null?"Frozen original C# accumulator; strictly decision-prior events; no trading, no gate modification, no detailed output":
                "Current source with active-factor readiness API; compare legacy values to immutable frozen cache; no new trading or cache admission"};
        File.WriteAllText(output,JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(JsonSerializer.Serialize(result));
    }
}
