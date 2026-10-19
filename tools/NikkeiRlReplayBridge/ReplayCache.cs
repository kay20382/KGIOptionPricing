using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Diagnostics;
using KGIOptionPricing.StrategyFramework;
using KGIOptionPricing.StrategyFramework.Storage;
using KGIOptionPricing.Strategies.BuySellPower;

namespace NikkeiRl;

public sealed record CacheManifest(string Schema,string InputPath,string InputSha256,string CacheSha256,long Rows,long BadRows,long OutOfOrder,long Dropped,long Frames,long ValidFrames,string[] Symbols,string Session,string Usage,long DataGapFrames=0,double? FeatureComputeP99Ms=null,
    double? FeatureEventUpdateP99Ms=null,int FeatureEventTimingSampleStride=0,DateTimeOffset? RetainedFrom=null,string? ParentCacheSha256=null,string[]? MarketExtraNames=null,bool LegacyParityVerified=false,long? PaperHealthyValidFrames=null,long? PaperDerivativeAnchorValidFrames=null,int DecisionIntervalSeconds=5,string FeatureProfile="three-product",PowerParameters? PowerParameters=null,string[]? ResearchExtraNames=null,string ResearchReadinessProfile="all-research");

public static class ReplayCache
{
    public const string PrunedCacheSchema="nikkei-rl-cache-pruned-flow-v1";
    public static string SchemaForReadiness(string legacy,string profile)=>profile=="exclude-flow"?legacy switch
    {
        "nikkei-rl-cache-v5-evaluation"=>PrunedCacheSchema+"-evaluation",
        "nikkei-rl-cache-v2-derived"=>PrunedCacheSchema+"-derived",
        _=>PrunedCacheSchema
    }:legacy;
    public static void ValidateSchemaReadiness(CacheManifest manifest)
    {
        bool isolated=manifest.Schema is PrunedCacheSchema or "nikkei-rl-cache-pruned-flow-v1-evaluation" or "nikkei-rl-cache-pruned-flow-v1-derived";
        if(isolated!=(manifest.ResearchReadinessProfile=="exclude-flow"))throw new InvalidDataException("Cache schema/readiness mismatch");
    }
    public static void ValidateInterval(CacheManifest manifest,RlConfig config)
    {
        ValidateSchemaReadiness(manifest);
        ValidateReadinessProfile(manifest.ResearchReadinessProfile,manifest.FeatureProfile,manifest.DecisionIntervalSeconds);
        ValidateReadinessProfile(config.ResearchReadinessProfile,config.FeatureProfile,config.DecisionIntervalSeconds);
        if(manifest.ResearchReadinessProfile!=config.ResearchReadinessProfile)throw new InvalidDataException("Cache/policy research readiness mismatch");
        if(manifest.DecisionIntervalSeconds is not (3 or 5)||manifest.DecisionIntervalSeconds!=config.DecisionIntervalSeconds||manifest.FeatureProfile!=config.FeatureProfile)
            throw new InvalidDataException("Cache/policy decision interval mismatch");
        if(config.FeatureProfile=="mini-micro-research"&&!(manifest.ResearchExtraNames??[]).SequenceEqual(ResearchFeatureAccumulator.Names))throw new InvalidDataException("Research cache feature names mismatch");
    }
    public static void ValidateManifest(CacheManifest manifest,string actualHash,bool allowIncompleteEvaluation=false)
    {
        ValidateSchemaReadiness(manifest);
        ValidateReadinessProfile(manifest.ResearchReadinessProfile,manifest.FeatureProfile,manifest.DecisionIntervalSeconds);
        if(manifest.Schema is "nikkei-rl-cache-v5-evaluation" or "nikkei-rl-cache-pruned-flow-v1-evaluation")
        {
            if(!allowIncompleteEvaluation||manifest.Usage!="EvaluationOnly"||actualHash!=manifest.CacheSha256||manifest.OutOfOrder!=0||manifest.BadRows!=0||manifest.Dropped!=0||manifest.DataGapFrames!=0)
                throw new InvalidDataException("Evaluation cache opt-in/integrity/quality failed");
            return;
        }
        if(manifest.Schema is not ("nikkei-rl-cache-v2" or "nikkei-rl-cache-v2-derived" or "nikkei-rl-cache-v3" or "nikkei-rl-cache-v4" or PrunedCacheSchema or "nikkei-rl-cache-pruned-flow-v1-derived")||actualHash!=manifest.CacheSha256||manifest.OutOfOrder!=0||manifest.BadRows!=0||manifest.Dropped!=0||manifest.DataGapFrames!=0
            ||manifest.Schema is ("nikkei-rl-cache-v3" or "nikkei-rl-cache-v4")&&(!manifest.LegacyParityVerified||manifest.ParentCacheSha256?.Length!=64))throw new InvalidDataException("Cache integrity/quality/parity failed");
    }
    public static readonly JsonSerializerOptions Json=new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase,PropertyNameCaseInsensitive=true};
    public static void ValidateReadinessProfile(string profile,string features,int interval)
    {
        if(profile is not ("all-research" or "exclude-flow")||profile=="exclude-flow"&&(features!="mini-micro-research"||interval!=3))
            throw new InvalidDataException("Unknown or incompatible research readiness profile");
    }
    public static ReplayFrame[] Read(string path)
    {
        using var input=File.OpenRead(path);using var compressed=new BrotliStream(input,CompressionMode.Decompress);using var reader=new StreamReader(compressed);
        var result=new List<ReplayFrame>();string? row;
        while((row=reader.ReadLine())!=null)result.Add(JsonSerializer.Deserialize<ReplayFrame>(row,Json)!);
        return result.ToArray();
    }
    public static void PrepareTrainingSegment(string input,string output)
    {
        if(File.Exists(output)||File.Exists(output+".manifest.json"))throw new IOException("Immutable training cache already exists");
        var parent=JsonSerializer.Deserialize<CacheManifest>(File.ReadAllText(input+".manifest.json"),Json)!;
        ValidateManifest(parent,EvaluationReplay.Hash(input),true);
        if(parent.Schema!=SchemaForReadiness("nikkei-rl-cache-v5-evaluation",parent.ResearchReadinessProfile)||EvaluationReplay.Hash(parent.InputPath)!=parent.InputSha256)throw new InvalidDataException("Verified evaluation segment/source required");
        var frames=Read(input);
        var close=new DateTimeOffset(frames[^1].At.Date.AddHours(14.75),frames[^1].At.Offset);
        if(frames.Length<2||frames.Any(f=>f.DataGap)||Math.Abs((close-frames[^1].At).TotalSeconds)>parent.DecisionIntervalSeconds+.001||frames.Zip(frames.Skip(1)).Any(p=>Math.Abs((p.Second.At-p.First.At).TotalSeconds-parent.DecisionIntervalSeconds)>.001))throw new InvalidDataException("Training segment must be continuous and reach session close");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        using(var file=new FileStream(output,FileMode.CreateNew))using(var zip=new BrotliStream(file,CompressionLevel.Fastest))using(var writer=new StreamWriter(zip))
            foreach(var frame in frames)writer.WriteLine(JsonSerializer.Serialize(frame,Json));
        var manifest=parent with{Schema=SchemaForReadiness("nikkei-rl-cache-v2-derived",parent.ResearchReadinessProfile),Usage="TrainingPreviouslyInspected",CacheSha256=EvaluationReplay.Hash(output),ParentCacheSha256=parent.CacheSha256};
        File.WriteAllText(output+".manifest.json",JsonSerializer.Serialize(manifest,Json));
        ValidateManifest(manifest,manifest.CacheSha256);
    }

    // An already-audited, monotonic recorder file permits bounded timestamp probing.
    // Used only to distinguish stale/crossed books from actual receive-event gaps.
    public static DateTimeOffset SourceEventAtOrBefore(string path,DateTimeOffset target)
    {
        using var stream=File.OpenRead(path);long low=0,high=stream.Length;
        DateTimeOffset Timestamp(string row){using var doc=JsonDocument.Parse(row);return doc.RootElement.GetProperty("Time").GetDateTimeOffset();}
        while(high-low>65536)
        {
            long middle=low+(high-low)/2;stream.Seek(middle,SeekOrigin.Begin);
            using var reader=new StreamReader(stream,System.Text.Encoding.UTF8,true,4096,true);
            reader.ReadLine();string? row=reader.ReadLine();
            if(row==null || Timestamp(row)>target)high=middle;else low=middle;
        }
        stream.Seek(low,SeekOrigin.Begin);using var tail=new StreamReader(stream);
        if(low>0)tail.ReadLine();DateTimeOffset? result=null,previous=null;string? line;
        while((line=tail.ReadLine())!=null)
        {
            var time=Timestamp(line);if(previous.HasValue&&time<previous)throw new InvalidDataException("Timestamp probe found non-monotonic source");previous=time;
            if(time>target)break;result=time;
        }
        return result??throw new InvalidDataException("Cannot verify source gap");
    }

    public static CacheManifest Build(string input,string output,string session,bool extended=false,DateTimeOffset? retainedFrom=null,bool paper=false,int decisionIntervalSeconds=5,string featureProfile="three-product",string researchReadinessProfile="all-research")
    {
        if(File.Exists(output) || File.Exists(output+".manifest.json"))throw new IOException("Output already exists; immutable cache required");
        if(session is not ("day" or "night"))throw new ArgumentException("Session must be day or night");
        if(decisionIntervalSeconds is not (3 or 5))throw new ArgumentException("Decision interval must be three or five seconds");
        if(featureProfile is not ("three-product" or "mini-micro-power" or "mini-micro-research"))throw new ArgumentException("Unknown feature profile");
        ValidateReadinessProfile(researchReadinessProfile,featureProfile,decisionIntervalSeconds);
        bool powerProfile=featureProfile is "mini-micro-power" or "mini-micro-research";
        var research=featureProfile=="mini-micro-research"?new ResearchFeatureAccumulator(5):null;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        var products=powerProfile?new[]{"225MC","225M"}:new[]{"225MC","225M","225"};var states=products.ToDictionary(x=>x,_=>new ProductState());
        var power=powerProfile?products.ToDictionary(p=>p,_=>new PowerIndicator(new PowerParameters())):null;
        var trades=new List<ReplayTrade>();DateTimeOffset? boundary=null,previous=null,first=null;
        long rows=0,bad=0,order=0,dropped=0,frames=0,valid=0,gaps=0,paperHealthy=0,paperAnchors=0;var symbols=new HashSet<string>();
        var featureTimes=new List<double>();
        var eventTimes=new List<double>();var extras=extended&&!paper?new RlFeatureAccumulator():null;var paperExtras=paper?new PaperFeatureAccumulator():null;long events=0;
        using var file=File.OpenRead(input);using var hasher=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var outputFile=new FileStream(output,FileMode.CreateNew,FileAccess.Write);
        using var compressed=new BrotliStream(outputFile,CompressionLevel.Fastest);
        using var writer=new StreamWriter(compressed);
        // Hash the exact bytes separately after reading; source must remain unchanged.
        long initialSize=file.Length;DateTime initialWrite=File.GetLastWriteTimeUtc(input);
        using var reader=new StreamReader(file);string? line;
        void Emit(DateTimeOffset at)
        {
            var mc=states["225MC"];
            if(mc.Symbol.Length==0){trades.Clear();return;}
            long featureStart=Stopwatch.GetTimestamp();
            double[] features=products.SelectMany(p=>states[p].Features(at,decisionIntervalSeconds==3)).ToArray();
            bool powerWarm=true;
            if(power!=null)
            {
                var snapshots=products.Select(p=>{power[p].Advance(at);return power[p].Snapshot();}).ToArray();
                powerWarm=snapshots.All(s=>s.IsWarm);
                features=features.Concat(snapshots.SelectMany(s=>new[]{(double)(s.BuyPower??0)/100,(double)(s.ShortAverage??0)/100,(double)(s.LongAverage??0)/100,(double)(s.BaisPower??0)/100,s.CurrentMovement?.Direction=="上漲"?1d:s.CurrentMovement?.Direction=="下跌"?-1d:0d})).ToArray();
            }
            double[] marketExtras=extras?.Snapshot(at)??[];
            double[] paperValues=paperExtras?.Snapshot(at)??[];
            double[] researchValues=research?.Snapshot(at)??[];
            bool healthy=powerWarm && states.Values.All(s=>s.Healthy(at)) && states.Values.All(s=>(at-s.BookAt).TotalSeconds<=2) && (states.Values.Max(s=>s.BookAt)-states.Values.Min(s=>s.BookAt)).TotalSeconds<=1 && first.HasValue && (at-first.Value).TotalSeconds>=60;
            if(research!=null)healthy&=researchReadinessProfile=="exclude-flow"?research.IsReadyForActorFeatures([48,49,50,51,52,53]):research.SnapshotReady;
            bool retained=retainedFrom==null||at>=retainedFrom;
            if(retained)featureTimes.Add(Stopwatch.GetElapsedTime(featureStart).TotalMilliseconds);
            bool gap=previous.HasValue && (at-previous.Value).TotalSeconds>10;
            var frame=new ReplayFrame{At=at,Symbol=mc.Symbol,Bid=mc.Bid,Ask=mc.Ask,Features=features,MarketExtras=marketExtras,PaperExtras=paperValues,ResearchExtras=researchValues,Valid=healthy,DataGap=gap,Trades=trades.ToArray()};
            if(retained){if(gap)gaps++;writer.WriteLine(JsonSerializer.Serialize(frame,Json));frames++;if(healthy){valid++;if(paperExtras?.LastSnapshotHealthy==true)paperHealthy++;if(paperExtras?.LastDerivativeAnchorAvailable==true)paperAnchors++;}}
            trades.Clear();
            foreach(var s in states.Values)s.Advance(at);
        }
        while((line=reader.ReadLine())!=null)
        {
            rows++;
            if(!MarketDataArchiveCodec.TryDeserialize(line,out var e) || e.Timestamp==DateTimeOffset.MinValue){bad++;continue;}
            if(!states.TryGetValue(e.ProductId,out var s))continue;
            var local=e.Timestamp.ToOffset(TimeSpan.FromHours(8));var tod=local.TimeOfDay;
            bool inSession=session=="day"?tod>=TimeSpan.FromHours(7.75)&&tod<=TimeSpan.FromHours(14.75):tod>=TimeSpan.FromHours(16)||tod<=TimeSpan.FromHours(5);
            if(!inSession)continue;
            if(previous.HasValue && e.Timestamp<previous){order++;continue;}
            first??=e.Timestamp;
            boundary??=new DateTimeOffset(e.Timestamp.Ticks-e.Timestamp.Ticks%TimeSpan.FromSeconds(decisionIntervalSeconds).Ticks,e.Timestamp.Offset).AddSeconds(decisionIntervalSeconds);
            // At the decision boundary, emit BEFORE consuming the boundary/future event.
            while(e.Timestamp>=boundary.Value){Emit(boundary.Value);boundary=boundary.Value.AddSeconds(decisionIntervalSeconds);}
            // Missing source time cannot contribute zero power to a trusted average.
            // Restart both product indicators after the interruption, then fully warm up.
            if(power!=null&&previous.HasValue&&(e.Timestamp-previous.Value).TotalSeconds>10)
            {
                foreach(var product in products)power[product]=new PowerIndicator(new PowerParameters());
                if(research!=null)research=new ResearchFeatureAccumulator(5);
            }
            previous=e.Timestamp;
            if(s.Symbol.Length>0 && s.Symbol!=e.Symbol)throw new InvalidDataException("Contract switch inside episode");
            s.Symbol=e.Symbol;symbols.Add(e.Symbol);
            // Capture recorder loss explicitly when available.
            if(line.Contains("\"Dropped\":",StringComparison.Ordinal)) {using var doc=JsonDocument.Parse(line);if(doc.RootElement.TryGetProperty("Dropped",out var d)&&d.TryGetInt64(out var n))dropped=Math.Max(dropped,n);}
            if(extras!=null||paperExtras!=null||research!=null)
            {
                bool sample=++events%1000==0;long tick=sample?Stopwatch.GetTimestamp():0;
                if(e.Kind==MarketEventKind.Book&&extras!=null)extras.OnBook(e.ProductId,e.Timestamp,e.Sequence,e.Symbol,e.BestBid,e.BestAsk,
                    e.BidQuantities.FirstOrDefault(),e.AskQuantities.FirstOrDefault(),
                    e.BidPrices.Length>=5&&e.AskPrices.Length>=5&&e.BidQuantities.Length>=5&&e.AskQuantities.Length>=5&&e.BidPrices.Take(5).All(p=>p>0)&&e.AskPrices.Take(5).All(p=>p>0));
                if(e.Kind==MarketEventKind.Trade&&e.LastPrice>0&&extras!=null)extras.OnTrade(e.ProductId,e.Timestamp,e.Sequence,e.LastPrice,e.TradeQuantity);
                if(e.Kind==MarketEventKind.Book&&e.ProductId=="225MC"&&paperExtras!=null)paperExtras.OnBook(e.Timestamp,e.Sequence,e.Symbol,e.BidPrices,e.AskPrices,e.BidQuantities,e.AskQuantities);
                if(e.ProductId=="225MC"&&research!=null)
                {
                    if(e.Kind==MarketEventKind.Book)research.OnBook(e.Timestamp,e.BidPrices,e.AskPrices,e.BidQuantities,e.AskQuantities);
                    if(e.Kind==MarketEventKind.Trade)research.OnTrade(e.Timestamp,e.LastPrice,e.TradeQuantity);
                }
                if(sample)eventTimes.Add(Stopwatch.GetElapsedTime(tick).TotalMilliseconds);
            }
            if(e.Kind==MarketEventKind.Book)s.OnBook(e);
            if(e.Kind==MarketEventKind.Trade && e.LastPrice>0){s.OnTrade(e);if(power!=null)power[e.ProductId].OnTrade(e.Timestamp,e.LastPrice,e.TradeQuantity,e.BestBid>0&&e.BestAsk>e.BestBid?e.BestBid:s.Bid,e.BestBid>0&&e.BestAsk>e.BestBid?e.BestAsk:s.Ask);if(e.ProductId=="225MC")trades.Add(new(e.Timestamp,e.Sequence,e.LastPrice));}
            if(rows%1000000==0)Console.Error.WriteLine($"cache {session}: {rows:N0} rows, {frames:N0} frames");
        }
        // Only complete intervals; never pad the final frame with unseen market events.
        writer.Flush();compressed.Flush();writer.Dispose();compressed.Dispose();outputFile.Dispose();
        if(file.Length!=initialSize || File.GetLastWriteTimeUtc(input)!=initialWrite)throw new IOException("Source mutated during extraction");
        using var hashInput=File.OpenRead(input);string hash=Convert.ToHexString(SHA256.HashData(hashInput));
        using var hashCache=File.OpenRead(output);string cacheHash=Convert.ToHexString(SHA256.HashData(hashCache));
        featureTimes.Sort();double featureP99=featureTimes.Count==0?0:featureTimes[(int)Math.Ceiling((featureTimes.Count-1)*.99)];
        eventTimes.Sort();double? eventP99=eventTimes.Count>0?eventTimes[(int)Math.Ceiling((eventTimes.Count-1)*.99)]:null;
        var manifest=new CacheManifest(SchemaForReadiness(paper?"nikkei-rl-cache-v4":extended?"nikkei-rl-cache-v3":"nikkei-rl-cache-v2",researchReadinessProfile),Path.GetFullPath(input),hash,cacheHash,rows,bad,order,dropped,frames,valid,symbols.Order().ToArray(),session,"DevelopmentPreviouslyInspected",gaps,featureP99,
            eventP99,extended||paper?1000:0,retainedFrom,MarketExtraNames:paper?PaperFeatureAccumulator.Names:extended?RlFeatureAccumulator.Names:null,
            PaperHealthyValidFrames:paper?paperHealthy:null,PaperDerivativeAnchorValidFrames:paper?paperAnchors:null,DecisionIntervalSeconds:decisionIntervalSeconds,FeatureProfile:featureProfile,PowerParameters:powerProfile?new PowerParameters():null,ResearchExtraNames:research!=null?ResearchFeatureAccumulator.Names:null,ResearchReadinessProfile:researchReadinessProfile);
        File.WriteAllText(output+".manifest.json",JsonSerializer.Serialize(manifest,Json));return manifest;
    }

    private sealed class ProductState
    {
        public string Symbol="";public decimal Bid,Ask,Last;public DateTimeOffset BookAt;
        private readonly Queue<(DateTimeOffset At,decimal Price)> history=new();
        private readonly Queue<(DateTimeOffset At,decimal Price)> tradeHistory=new();
        private readonly Queue<(DateTimeOffset At,int Qty,int Sign)> volumes=new();
        private double obi1,obi5;
        public void OnBook(MarketEvent e)
        {
            Bid=e.BestBid;Ask=e.BestAsk;BookAt=e.Timestamp;
            static double Obi(int[] b,int[] a,int count){double x=b.Take(count).Sum(),y=a.Take(count).Sum();return x+y>0?(x-y)/(x+y):0;}
            obi1=Obi(e.BidQuantities,e.AskQuantities,1);obi5=Obi(e.BidQuantities,e.AskQuantities,5);
            if(e.BidPrices.Length<5 || e.AskPrices.Length<5 || e.BidQuantities.Length<5 || e.AskQuantities.Length<5 || e.BidPrices.Take(5).Any(p=>p<=0) || e.AskPrices.Take(5).Any(p=>p<=0))BookAt=DateTimeOffset.MinValue;
        }
        public void OnTrade(MarketEvent e){int sign=Last==0?0:Math.Sign(e.LastPrice-Last);Last=e.LastPrice;tradeHistory.Enqueue((e.Timestamp,e.LastPrice));volumes.Enqueue((e.Timestamp,Math.Max(0,e.TradeQuantity),sign));}
        public bool Healthy(DateTimeOffset at)=>Bid>0 && Ask>Bid && Ask-Bid<=10 && BookAt!=DateTimeOffset.MinValue && (at-BookAt).TotalSeconds is >=0 and <=2 && Last>0;
        public double[] Features(DateTimeOffset at,bool exactTradeHistory=false)
        {
            double Ret(int secs){var past=exactTradeHistory?tradeHistory.LastOrDefault(x=>x.At<at.AddSeconds(-secs)):history.LastOrDefault(x=>x.At<=at.AddSeconds(-secs));return past.Price>0?(double)((Last-past.Price)/5):0;}
            var recent=volumes.Where(x=>x.At>at.AddSeconds(-5)).ToArray();double qty=recent.Sum(x=>(double)x.Qty);
            return [Ret(5),Ret(15),Ret(30),Ret(60),(double)((Ask-Bid)/5),obi1,obi5,Math.Clamp((at-BookAt).TotalSeconds,0,60),Math.Log(1+qty),qty>0?recent.Sum(x=>(double)x.Qty*x.Sign)/qty:0];
        }
        public void Advance(DateTimeOffset at){history.Enqueue((at,Last));while(history.Count>0&&history.Peek().At<at.AddSeconds(-65))history.Dequeue();while(tradeHistory.Count>1&&tradeHistory.ElementAt(1).At<at.AddSeconds(-65))tradeHistory.Dequeue();while(volumes.Count>0&&volumes.Peek().At<=at.AddSeconds(-30))volumes.Dequeue();}
    }
}
