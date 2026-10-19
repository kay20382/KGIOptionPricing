using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NikkeiRl;
using NikkeiFactorExport;
try
{
    if(args.Length==1&&args[0]=="selftest")
    {
        var at=new DateTimeOffset(2026,9,15,7,45,0,TimeSpan.FromHours(8));var a=new FactorAccumulator();
        var events=new[]{(at,0d),(at.AddSeconds(.5),7d),(at.AddSeconds(3),99d)};
        foreach(var e in events.Where(e=>e.Item1<at.AddSeconds(3)))a.Event(e.Item1,e.Item2>0?"225MC":"225M","trade",100,e.Item2,null,null,null,null);
        var first=a.Snapshot(at.AddSeconds(3));if(first.GridVolume!=7||first.Bars.Length!=0)throw new Exception("causal boundary/true quantity");
        a.Event(at.AddSeconds(30),"225M","trade",100,0,null,null,null,null);a.Snapshot(at.AddSeconds(57));var end=a.Snapshot(at.AddSeconds(60));
        if(end.Bars.Length!=1||end.Bars[0].Complete||end.Bars[0].Volume!=7)throw new Exception("minute gap/closure");
        var b=new FactorAccumulator();b.Event(at,"225M","trade",100,0,null,null,null,null);
        for(int i=1;i<=20;i++){b.Event(at.AddSeconds(i*3-1),"225MC","trade",100+i,2,null,null,null,null);b.Snapshot(at.AddSeconds(i*3));}
        if(b.LastClosed is not {Complete:true,Volume:40})throw new Exception("complete minute quantity");
        using var nullableRow=JsonDocument.Parse("{\"Last\":null,\"TradeQty\":null,\"Bid\":null}");
        if(ReadNumber(nullableRow.RootElement,"Last")!=0||ReadVector(nullableRow.RootElement,"Bid")!=null)throw new Exception("nullable unused fields");
        CheckIdentity(true,"RAW","FILTERED","RAW","RAW");
        CheckIdentity(false,"RAW","FILTERED","FILTERED","RAW");
        bool rejected=false;try{CheckIdentity(false,"RAW","CHANGED","FILTERED","RAW");}catch{rejected=true;}
        if(!rejected)throw new Exception("filtered source tamper accepted");
        Console.WriteLine("PASS7: causal boundary/quantity; gap-invalid minute; complete minute; nullable unused fields; direct identity; filtered identity; filtered tamper rejection");return 0;
    }
    if(args.Length!=4||args[0]!="export")throw new ArgumentException("export cache source expectedSourceSHA");
    var manifest=JsonSerializer.Deserialize<CacheManifest>(File.ReadAllText(args[1]+".manifest.json"),ReplayCache.Json)!;
    ReplayCache.ValidateManifest(manifest,EvaluationReplay.Hash(args[1]),true);
    if(manifest.ResearchReadinessProfile!="exclude-flow"||manifest.DecisionIntervalSeconds!=3)throw new Exception("Wrong source/cache profile");
    var frames=ReplayCache.Read(args[1]);var open=new DateTimeOffset(frames[0].At.Date.AddHours(7.75),frames[0].At.Offset);var close=open.AddHours(7);
    if(frames.Any(f=>f.DataGap)||frames.Zip(frames.Skip(1)).Any(p=>(p.Second.At-p.First.At).TotalSeconds!=3))throw new Exception("Gap/sequence");
    var before=new FileInfo(args[2]);long initialSize=before.Length;var initialWrite=before.LastWriteTimeUtc;
    byte[] newline;using(var probe=File.OpenRead(args[2])){byte[] header=new byte[65536];int n=probe.Read(header);int lf=System.Array.IndexOf(header,(byte)10,0,n);if(lf<0)throw new Exception("Unknown source newline");newline=lf>0&&header[lf-1]==13?[13,10]:[10];}
    using var source=File.OpenRead(args[2]);using var hash=SHA256.Create();using var crypto=new CryptoStream(source,hash,CryptoStreamMode.Read);using var reader=new StreamReader(crypto,new UTF8Encoding(false,true));
    using var normalized=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    DateTimeOffset? previous=null;long rows=0;JsonDocument? pending=null;
    JsonDocument? Next()
    {
        string? line;
        while((line=reader.ReadLine())!=null)
        {
            var doc=JsonDocument.Parse(line);var e=doc.RootElement;
            if(e.TryGetProperty("Dropped",out var dropped)&&dropped.GetInt64()!=0)throw new Exception("Dropped events");
            var kind=e.GetProperty("Kind").GetString()!;
            if(kind!="trade"&&!kind.StartsWith("depth")){if(kind is not("session" or "status" or "contract" or "api_packet"))throw new Exception("Unknown event kind");doc.Dispose();continue;}
            var product=e.GetProperty("Product").GetString();if(product is not("225MC" or "225M")){doc.Dispose();continue;}
            var at=e.GetProperty("Time").GetDateTimeOffset();if(at<open||at>close){doc.Dispose();continue;}
            if(previous.HasValue&&at<previous)throw new Exception("Out of order source");previous=at;rows++;
            normalized.AppendData(Encoding.UTF8.GetBytes(line));normalized.AppendData(newline);return doc;
        }
        return null;
    }
    var accumulator=new FactorAccumulator();pending=Next();
    var config=new RlConfig{AllowIncompleteEvaluation=true,FeatureProfile="mini-micro-research",ResearchReadinessProfile="exclude-flow",ObservationSize=60,DecisionIntervalSeconds=3,DirectionDrivenHolding=true,MaxHoldingSeconds=0,StopMode="fixed",FixedStopPoints=150,MaxStopPoints=150};
    var environment=new RlReplayEnvironment(frames,config);var state=environment.Reset();double maximumBookError=0;long compared=0;
    for(int i=0;i<frames.Length;i++)
    {
        var frame=frames[i];
        while(pending!=null&&pending.RootElement.GetProperty("Time").GetDateTimeOffset()<frame.At)
        {
            var e=pending.RootElement;string product=e.GetProperty("Product").GetString()!,kind=e.GetProperty("Kind").GetString()!;
            accumulator.Event(e.GetProperty("Time").GetDateTimeOffset(),product,kind,ReadNumber(e,"Last"),ReadNumber(e,"TradeQty"),ReadVector(e,"Bid"),ReadVector(e,"Ask"),ReadVector(e,"BidQty"),ReadVector(e,"AskQty"));pending.Dispose();pending=Next();
        }
        var stats=accumulator.Snapshot(frame.At);
        if(frame.Valid)
        {
            if(stats.Book==null)throw new Exception("Missing healthy cache book");
            double error=Math.Max(Math.Abs(stats.Book[0][0]-(double)frame.Bid),Math.Abs(stats.Book[1][0]-(double)frame.Ask));maximumBookError=Math.Max(maximumBookError,error);compared++;if(error>1e-8)throw new Exception("Source/cache BBO parity failed");
        }
        Console.WriteLine(JsonSerializer.Serialize(new{at=frame.At,bid=frame.Bid,ask=frame.Ask,valid=frame.Valid,entryAllowed=state.Mask[1]&&state.Mask[2],features=frame.Features,extras=frame.ResearchExtras,stats},ReplayCache.Json));
        if(i+1<frames.Length)state=environment.Step(0);
    }
    pending?.Dispose();while((pending=Next())!=null)pending.Dispose();
    var rawSHA=Convert.ToHexString(hash.Hash!);var normalizedSHA=Convert.ToHexString(normalized.GetHashAndReset());var after=new FileInfo(args[2]);
    if(initialSize!=after.Length||initialWrite!=after.LastWriteTimeUtc)throw new Exception("Source changed while read");
    bool directInput=string.Equals(Path.GetFullPath(args[2]),Path.GetFullPath(manifest.InputPath),StringComparison.OrdinalIgnoreCase);
    CheckIdentity(directInput,rawSHA,normalizedSHA,manifest.InputSha256,args[3].ToUpperInvariant());
    Console.WriteLine(JsonSerializer.Serialize(new{summary=true,sourceSHA=rawSHA,normalizedSHA,cacheInputSHA=manifest.InputSha256,sourceIdentityMode=directInput?"DirectCacheInput":"FilteredOriginal",rows,compared,maximumBookError},ReplayCache.Json));return 0;
}
catch(Exception ex){Console.Error.WriteLine("FAIL: "+ex);return 1;}
static double ReadNumber(JsonElement e,string key)=>e.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.Number?v.GetDouble():0;
static double[]? ReadVector(JsonElement e,string key)
{
    if(!e.TryGetProperty(key,out var v)||v.ValueKind!=JsonValueKind.Array)return null;
    var levels=v.EnumerateArray().Take(5).ToArray();
    return levels.All(x=>x.ValueKind==JsonValueKind.Number)?levels.Select(x=>x.GetDouble()).ToArray():null;
}
static void CheckIdentity(bool direct,string raw,string filtered,string expectedInput,string expectedSource)
{
    if(raw!=expectedSource||(direct?raw:filtered)!=expectedInput)throw new Exception("Source identity mismatched");
}
