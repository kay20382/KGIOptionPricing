using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using NikkeiRl;

namespace KGIOptionPricing.Strategies.NikkeiRlDirection;

/// <summary>Frozen round12 tree policy. Uses the existing raw60 stream and order/ledger host.</summary>
public sealed class FrozenGrid15Policy : IRlMainPolicy
{
    readonly GridTree longNet, shortNet, holding;
    readonly double threshold;
    readonly Queue<(DateTimeOffset At,bool Healthy)> history=new();
    DateTimeOffset at, open;
    bool healthy, contextPending;
    public string ModelId {get;}
    public double LongNetJPY {get;private set;}
    public double ShortNetJPY {get;private set;}
    public double? HoldingAdvantageJPY {get;private set;}
    public double ThresholdJPY=>threshold;

    public FrozenGrid15Policy(NikkeiRlParameters p)
    {
        if(!p.IsGrid)throw new ArgumentException("Grid15 variant required");
        var bytes=File.ReadAllBytes(Path.Combine(p.ResolvedModelDirectory,"model.json"));
        if(Convert.ToHexString(SHA256.HashData(bytes))!=p.EffectiveModelSha)throw new InvalidDataException("Grid15模型SHA-256不符");
        using var doc=JsonDocument.Parse(bytes);var m=doc.RootElement;
        ModelId=m.GetProperty("id").GetString()!;
        string expected=p.PolicyVariant=="grid15-positive-net"?"entry-grid15-early-entry-h120-holding-h120-positive-net":"entry-grid15-early-entry-h120-holding-h120-q90-research";
        threshold=m.GetProperty("selectionThresholdJPY").GetDouble();
        if(ModelId!=expected||m.GetProperty("schema").GetString()!="paper-temporal-holding-v1"||m.GetProperty("kind").GetString()!="paper"||m.GetProperty("featureSize").GetInt32()!=44||m.GetProperty("entryGridDecisions").GetInt32()!=5||!m.GetProperty("holdingAgeAware").GetBoolean()||!m.GetProperty("holdingPolicy").GetBoolean())throw new InvalidDataException("Grid15 schema不符");
        longNet=new(m.GetProperty("longNet"));shortNet=new(m.GetProperty("shortNet"));holding=new(m.GetProperty("holding"));
    }

    public void SetContext(DateTimeOffset timestamp,DateTimeOffset sessionOpen,bool valid)
    {
        if(contextPending)throw new InvalidOperationException("未消耗的Grid15時點");
        if(history.Count>0&&(timestamp-history.Last().At).TotalSeconds!=3)history.Clear();
        if(open!=sessionOpen)history.Clear();
        at=timestamp;open=sessionOpen;healthy=valid;contextPending=true;
    }

    public static double[] Market44(double[] raw)
    {
        if(raw.Length!=60||raw.Any(v=>!double.IsFinite(v)))throw new ArgumentException("Grid15 raw60 schema");
        return raw.Take(30).Concat(raw.Skip(40).Take(8)).Concat(raw.Skip(54).Take(6)).ToArray();
    }

    public static bool EntryGridAllows(DateTimeOffset timestamp,DateTimeOffset sessionOpen)
    {
        // Replay frame zero is the first completed 3-second frame: session open + 3s.
        long ticks=(timestamp-sessionOpen).Ticks;
        return ticks>=TimeSpan.TicksPerSecond*3 && ticks%TimeSpan.FromSeconds(3).Ticks==0 && (ticks/TimeSpan.FromSeconds(3).Ticks-1)%5==0;
    }

    public RlPolicyPrediction Predict(double[] observation,bool[] mask)
    {
        var sw=Stopwatch.StartNew();
        if(!contextPending||mask.Length!=5||!mask[0])throw new ArgumentException("Grid15 context/action mask");
        contextPending=false;var x=Market44(observation);
        history.Enqueue((at,healthy));while(history.Count>11)history.Dequeue();
        int position=Math.Sign(observation[30]);bool pending=observation[36]>0;
        HoldingAdvantageJPY=null;LongNetJPY=ShortNetJPY=double.NaN;
        int action;
        if(history.Count<11||history.Any(h=>!h.Healthy))action=position!=0?3:pending?4:0;
        else {
            LongNetJPY=longNet.Predict(x);ShortNetJPY=shortNet.Predict(x);
            if(position!=0){
                var hx=x.Concat(new[]{(double)position,observation[32],observation[31]}).ToArray();
                HoldingAdvantageJPY=holding.Predict(hx);action=HoldingAdvantageJPY>0?0:3;
            } else {
                bool eligible=Math.Max(LongNetJPY,ShortNetJPY)>threshold;
                action=pending?(eligible?0:4):eligible?(LongNetJPY>=ShortNetJPY?1:2):0;
                if(!pending&&action is 1 or 2&&!EntryGridAllows(at,open))action=0;
            }
        }
        if(!mask[action])action=0;
        var probabilities=new double[5];probabilities[action]=1;
        return new(action,probabilities,sw.Elapsed.TotalMilliseconds);
    }
    public void Dispose() { }
}
