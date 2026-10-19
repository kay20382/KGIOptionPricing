using System.Text.Json;
using NikkeiRl;

try
{
    if(args.Length>0&&args[0]=="training-segment")
    {
        if(args.Length!=3)throw new ArgumentException("training-segment VERIFIED_EVALUATION_SEGMENT OUTPUT");
        ReplayCache.PrepareTrainingSegment(args[1],args[2]);return;
    }
    if(args.Length>0&&args[0] is ("counterfactual-labels" or "counterfactual-labels-3s15s" or "counterfactual-labels-power" or "counterfactual-labels-research"))
    {
        if(args.Length<3)throw new ArgumentException("counterfactual-labels TRAIN_DATASET OUTPUT_JSONL [TRAIN_EPISODES...]");
        bool research=args[0]=="counterfactual-labels-research";bool power=args[0]=="counterfactual-labels-power";bool three=args[0]!="counterfactual-labels";
        ExecutableLabels.Generate(args[1],args[2],args.Length>3?args[3..]:null,three?3:5,three?15:30,research?"mini-micro-research":power?"mini-micro-power":"three-product");return;
    }
    if(args.Length>0&&args[0] is ("cache-evaluation" or "cache-evaluation-power" or "cache-evaluation-research" or "cache-evaluation-research-pruned-flow"))
    {
        if(args.Length is not (3 or 4))throw new ArgumentException("cache-evaluation INPUT OUTPUT_DIRECTORY [INTERVAL_SECONDS]");
        Directory.CreateDirectory(args[2]);var full=Path.Combine(args[2],"full.br");
        bool pruned=args[0]=="cache-evaluation-research-pruned-flow";
        bool research=args[0]=="cache-evaluation-research"||pruned;bool power=args[0]=="cache-evaluation-power"||research;
        ReplayCache.Build(args[1],full,"day",paper:true,decisionIntervalSeconds:args.Length==4?int.Parse(args[3]):power?3:5,featureProfile:research?"mini-micro-research":power?"mini-micro-power":"three-product",researchReadinessProfile:pruned?"exclude-flow":"all-research");EvaluationReplay.Prepare(full,args[2]);return;
    }
    if(args.Length>0&&args[0]=="split-evaluation")
    {
        if(args.Length!=3)throw new ArgumentException("split-evaluation CACHE OUTPUT_DIRECTORY");
        EvaluationReplay.Prepare(args[1],args[2]);return;
    }
    if(args.Length>0&&args[0]=="evaluate-frozen")
    {
        if(args.Length!=5)throw new ArgumentException("evaluate-frozen MODEL MANIFEST DATASET OUTPUT_DIRECTORY");
        EvaluationReplay.Evaluate(args[1],args[2],args[3],args[4]);return;
    }
    if(args.Length>0&&args[0] is ("evaluate-risk" or "evaluate-risk-summary"))
    {
        if(args.Length!=6)throw new ArgumentException("evaluate-risk MODEL MANIFEST DATASET RISK_JSON OUTPUT_DIRECTORY");
        EvaluationReplay.Evaluate(args[1],args[2],args[3],args[5],args[4],summaryOnly:args[0]=="evaluate-risk-summary");return;
    }
    if(args.Length>0&&args[0]=="evaluate-entry-summary")
    {
        if(args.Length!=6)throw new ArgumentException("evaluate-entry-summary MODEL MANIFEST DATASET ENTRY_JSON OUTPUT_DIRECTORY");
        EvaluationReplay.Evaluate(args[1],args[2],args[3],args[5],summaryOnly:true,entryFile:args[4]);return;
    }
    if(args.Length>0&&args[0] is ("cache-v3" or "cache-v4"))
    {
        if(args.Length!=3)throw new ArgumentException("cache-v3 VERIFIED_PARENT_CACHE OUTPUT");
        var parent=JsonSerializer.Deserialize<CacheManifest>(File.ReadAllText(args[1]+".manifest.json"),ReplayCache.Json)!;
        string Hash(string path){using var file=File.OpenRead(path);return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(file));}
        if(parent.Schema is not ("nikkei-rl-cache-v2" or "nikkei-rl-cache-v2-derived")||Hash(args[1])!=parent.CacheSha256||Hash(parent.InputPath)!=parent.InputSha256||parent.BadRows!=0||parent.OutOfOrder!=0||parent.Dropped!=0||parent.DataGapFrames!=0)
            throw new InvalidDataException("Parent cache/raw integrity or quality failed");
        bool paper=args[0]=="cache-v4";
        var built=ReplayCache.Build(parent.InputPath,args[2],parent.Session,!paper,parent.RetainedFrom,paper,parent.DecisionIntervalSeconds,parent.FeatureProfile);
        var oldFrames=ReplayCache.Read(args[1]);var newFrames=ReplayCache.Read(args[2]);
        if(oldFrames.Length!=newFrames.Length||oldFrames.Zip(newFrames).Any(p=>p.First.At!=p.Second.At||p.First.Symbol!=p.Second.Symbol||p.First.Bid!=p.Second.Bid||p.First.Ask!=p.Second.Ask||p.First.Valid!=p.Second.Valid||p.First.DataGap!=p.Second.DataGap||!p.First.Features.SequenceEqual(p.Second.Features)||!p.First.Trades.SequenceEqual(p.Second.Trades)))
            throw new InvalidDataException("Legacy frame/feature/trade parity failed");
        if(built.BadRows!=0||built.OutOfOrder!=0||built.Dropped!=0||built.DataGapFrames!=0||newFrames.Any(f=>paper?(f.PaperExtras.Length!=8||f.PaperExtras.Any(x=>!double.IsFinite(x))||f.MarketExtras.Length!=0):(f.MarketExtras.Length!=8||f.MarketExtras.Any(x=>!double.IsFinite(x)))))throw new InvalidDataException("New cache integrity failed");
        built=built with{ParentCacheSha256=parent.CacheSha256,LegacyParityVerified=true};
        File.WriteAllText(args[2]+".manifest.json",JsonSerializer.Serialize(built,ReplayCache.Json));
        Console.WriteLine(JsonSerializer.Serialize(new{manifest=built,legacyParity=true},ReplayCache.Json));return;
    }
    if(args.Length>0 && args[0]=="upgrade-cache")
    {
        if(args.Length!=3)throw new ArgumentException("upgrade-cache VERIFIED_V1_CACHE OUTPUT");
        var manifest=JsonSerializer.Deserialize<CacheManifest>(File.ReadAllText(args[1]+".manifest.json"),ReplayCache.Json)!;
        using(var raw=File.OpenRead(manifest.InputPath))
            if(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(raw))!=manifest.InputSha256)
                throw new InvalidDataException("Raw source hash changed; cannot audit gap candidates");
        using(var source=File.OpenRead(args[1]))if(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(source))!=manifest.CacheSha256 || manifest.BadRows!=0 || manifest.OutOfOrder!=0 || manifest.Dropped!=0)throw new InvalidDataException("V1 cache not auditable; reject rather than silently repair");
        if(File.Exists(args[2]))throw new IOException("Output exists");
        var frames=ReplayCache.Read(args[1]);long gaps=0,lastTradeSequence=0;
        using(var output=File.Create(args[2]))using(var compressed=new System.IO.Compression.BrotliStream(output,System.IO.Compression.CompressionLevel.Fastest))using(var writer=new StreamWriter(compressed))
        {
            foreach(var frame in frames)
            {
                foreach(var trade in frame.Trades){if(trade.Sequence<=lastTradeSequence)throw new InvalidDataException("Trade sequence repeated/out of order");lastTradeSequence=trade.Sequence;}
                // Candidate gaps are checked against actual source receive timestamps.
                bool candidate=(frame.At-frames[0].At).TotalSeconds>=60 && frame.Trades.Length==0 && new[]{7,17,27}.All(i=>frame.Features[i]>10);
                bool gap=candidate && (frame.At-ReplayCache.SourceEventAtOrBefore(manifest.InputPath,frame.At)).TotalSeconds>10;
                if(gap)gaps++;writer.WriteLine(JsonSerializer.Serialize(frame with{DataGap=gap},ReplayCache.Json));
            }
        }
        using var cache=File.OpenRead(args[2]);var cacheHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(cache));
        var upgraded=manifest with{Schema="nikkei-rl-cache-v2-derived",CacheSha256=cacheHash,DataGapFrames=gaps};
        File.WriteAllText(args[2]+".manifest.json",JsonSerializer.Serialize(upgraded,ReplayCache.Json));Console.WriteLine(JsonSerializer.Serialize(upgraded,ReplayCache.Json));return;
    }
    if(args.Length>0 && args[0]=="benchmark")
    {
        if(args.Length!=5)throw new ArgumentException("benchmark MODEL MANIFEST PARITY OUTPUT");
        using var policy=new RlPolicyPredictor(args[1],args[2]);
        using var parity=JsonDocument.Parse(File.ReadAllText(args[3]));var times=new List<double>();double maxError=0;int count=0;
        foreach(var row in parity.RootElement.EnumerateArray())
        {
            var observation=row.GetProperty("observation").EnumerateArray().Select(x=>x.GetDouble()).ToArray();var mask=row.GetProperty("mask").EnumerateArray().Select(x=>x.GetBoolean()).ToArray();
            var result=policy.Predict(observation,mask);times.Add(result.InferenceMs);
            if(result.Action!=row.GetProperty("action").GetInt32())throw new InvalidOperationException("Python/C# action parity failure");
            var expectedProbabilities=row.GetProperty("probabilities").EnumerateArray().Select(x=>x.GetDouble()).ToArray();maxError=Math.Max(maxError,expectedProbabilities.Zip(result.Probabilities).Max(x=>Math.Abs(x.First-x.Second)));count++;
        }
        if(maxError>1e-6 || count<1000)throw new InvalidOperationException("Insufficient parity or probability mismatch");
        times.Sort();double Percentile(double q)=>times[(int)Math.Ceiling(q*(times.Count-1))];
        string Hash(string path)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
        var resultReport=new{schema=policy.Schema,observationSize=policy.ObservationSize,modelSha256=Hash(args[1]),manifestSha256=Hash(args[2]),paritySha256=Hash(args[3]),count,maxProbabilityError=maxError,allActionsMatch=true,policy.ColdLoadMs,p50Ms=Percentile(.50),p95Ms=Percentile(.95),p99Ms=Percentile(.99),maxMs=times.Max(),countAbove1Ms=times.Count(t=>t>1),percentAbove1Ms=100.0*times.Count(t=>t>1)/times.Count,timingScope="Warm actor normalization + ONNX inference + mask/softmax; feature extraction and broker/network excluded"};
        File.WriteAllText(args[4],JsonSerializer.Serialize(resultReport,ReplayCache.Json));Console.WriteLine(JsonSerializer.Serialize(resultReport,ReplayCache.Json));return;
    }
    if(args.Length>0 && args[0]=="cache")
    {
        if(args.Length is not (4 or 5))throw new ArgumentException("cache INPUT OUTPUT day|night [INTERVAL_SECONDS]");
        Console.WriteLine(JsonSerializer.Serialize(ReplayCache.Build(args[1],args[2],args[3],decisionIntervalSeconds:args.Length==5?int.Parse(args[4]):5),ReplayCache.Json));return;
    }
    if(args.Length!=2 || args[0]!="serve")throw new ArgumentException("serve dataset.json");
    var dataset=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(args[1]),ReplayCache.Json)!;
    var episodes=new Dictionary<string,ReplayFrame[]>();RlReplayEnvironment? env=null;long expected=0;
    string? line;
    while((line=Console.ReadLine())!=null)
    {
        try
        {
            using var doc=JsonDocument.Parse(line);var request=doc.RootElement;
            string op=request.GetProperty("op").GetString()!;
            if(op=="reset")
            {
                string id=request.GetProperty("episode").GetString()!;
                if(!dataset.ContainsKey(id))throw new ArgumentException("Unknown episode");
                var config=request.TryGetProperty("config",out var c)?c.Deserialize<RlConfig>(ReplayCache.Json)!:new RlConfig();
                var manifest=JsonSerializer.Deserialize<CacheManifest>(File.ReadAllText(dataset[id]+".manifest.json"),ReplayCache.Json)!;
                using var cache=File.OpenRead(dataset[id]);var hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(cache));
                ReplayCache.ValidateManifest(manifest,hash,config.AllowIncompleteEvaluation);
                ReplayCache.ValidateInterval(manifest,config);
                if(!episodes.TryGetValue(id,out var frames))
                {
                    episodes[id]=frames=ReplayCache.Read(dataset[id]);
                }
                env=new(frames,config);expected=0;Console.WriteLine(JsonSerializer.Serialize(env.Reset(),ReplayCache.Json));
            }
            else if(op=="step")
            {
                if(env==null || request.GetProperty("stepId").GetInt64()!=expected)throw new InvalidOperationException("Missing reset or mismatched step ID");
                var state=env.Step(request.GetProperty("action").GetInt32());expected++;Console.WriteLine(JsonSerializer.Serialize(state,ReplayCache.Json));
            }
            else if(op=="close")break;
            else throw new ArgumentException("Unknown op");
        }
        catch(Exception e){Console.WriteLine(JsonSerializer.Serialize(new{error=e.Message},ReplayCache.Json));}
    }
}
catch(Exception e){Console.Error.WriteLine(e);Environment.ExitCode=1;}
