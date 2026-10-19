using System.Text.Json;
using NikkeiRl;

static long Compare(ReplayFrame[] before,ReplayFrame[] after)
{
    if(before.Length!=after.Length)throw new InvalidDataException("Frame count changed");
    long values=0;
    for(int i=0;i<before.Length;i++)
    {
        var a=before[i];var b=after[i];
        if(a.At!=b.At||a.Symbol!=b.Symbol||a.Bid!=b.Bid||a.Ask!=b.Ask||a.DataGap!=b.DataGap||!a.Trades.SequenceEqual(b.Trades)||a.Valid&&!b.Valid)
            throw new InvalidDataException($"Causal market/trade/admission mismatch at {i}");
        foreach(var pair in new[]{(a.Features,b.Features),(a.ResearchExtras,b.ResearchExtras)})
        {
            if(pair.Item1.Length!=pair.Item2.Length)throw new InvalidDataException("Feature width changed");
            for(int j=0;j<pair.Item1.Length;j++)
            {
                if(!double.IsFinite(pair.Item1[j])||!double.IsFinite(pair.Item2[j])||Math.Abs(pair.Item1[j]-pair.Item2[j])>1e-9)throw new InvalidDataException($"Feature changed at {i}/{j}");
                values++;
            }
        }
    }
    return values;
}

try
{
    if(args.Length==1&&args[0]=="self-test")
    {
        var frame=new ReplayFrame{At=DateTimeOffset.UnixEpoch,Features=new double[30],ResearchExtras=new double[20],Valid=true,Trades=[new(DateTimeOffset.UnixEpoch,1,100)]};
        if(Compare([frame],[frame])!=50)throw new Exception("Positive fixture failed");
        int rejected=0;
        foreach(var bad in new[]{frame with{ResearchExtras=Enumerable.Repeat(1d,20).ToArray()},frame with{Trades=[]},frame with{Valid=false}})
            try{Compare([frame],[bad]);}catch(InvalidDataException){rejected++;}
        if(rejected!=3)throw new Exception("Negative fixtures failed");
        Console.WriteLine("Cache content audit PASS4");return;
    }
    if(args.Length!=3)throw new ArgumentException("OLD_CACHE NEW_CACHE NEW_REPORT_JSON");
    if(File.Exists(args[2]))throw new IOException("Immutable report exists");
    var old=ReplayCache.Read(args[0]);var current=ReplayCache.Read(args[1]);
    var before=JsonSerializer.Deserialize<CacheManifest>(File.ReadAllText(args[0]+".manifest.json"),ReplayCache.Json)!;
    var after=JsonSerializer.Deserialize<CacheManifest>(File.ReadAllText(args[1]+".manifest.json"),ReplayCache.Json)!;
    if(before.FeatureProfile!="mini-micro-research"||after.FeatureProfile!=before.FeatureProfile||before.ResearchReadinessProfile!="all-research"||after.ResearchReadinessProfile!="exclude-flow")throw new InvalidDataException("Wrong audit modes");
    long values=Compare(old,current);
    File.WriteAllText(args[2],JsonSerializer.Serialize(new{legacy=args[0],rebuilt=args[1],legacySha256=EvaluationReplay.Hash(args[0]),rebuiltSha256=EvaluationReplay.Hash(args[1]),frames=old.Length,comparedFeatureValues=values,maxAllowedFeatureError=1e-9,legacyValidFrames=old.Count(f=>f.Valid),newValidFrames=current.Count(f=>f.Valid),allCausalTradesEqual=true,legacyValidSubset=true,scope="All model market inputs (30+20), timestamps, bid/ask, symbol, gaps and causal trades. PaperExtras/MarketExtras are unused by research60 and excluded from this audit."},ReplayCache.Json));
    Console.WriteLine($"frames={old.Length} modelValues={values} legacyValid={old.Count(f=>f.Valid)} newValid={current.Count(f=>f.Valid)}");
}
catch(Exception e){Console.Error.WriteLine(e.Message);Environment.ExitCode=1;}
