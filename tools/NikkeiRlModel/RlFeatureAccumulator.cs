namespace NikkeiRl;

/// <summary>Causal receive-time market features. No broker or portfolio dependency.</summary>
public sealed class RlFeatureAccumulator
{
    public static readonly string[] Names=["ofi5s_mc","ofi5s_mini","ofi5s_large","lead1s_mini_minus_mc",
        "lead1s_large_minus_mc","quote_signed_volume5s_mc","mid_path_vol30s_mc","spread_change5s_mc"];
    private readonly record struct Quote(DateTimeOffset At,decimal Bid,decimal Ask,int Bq,int Aq)
    { public decimal Mid=>(Bid+Ask)/2;public double Depth=>(Bq+(double)Aq)/2; }
    private sealed class State
    {
        public string Symbol="";public Quote? Latest,Floor;
        public Queue<Quote> Quotes=new();
        public Queue<(DateTimeOffset At,double Flow,double Depth)> Ofi=new();
        public Queue<(DateTimeOffset At,int Qty,int Signed)> Volume=new();
        public Queue<(DateTimeOffset At,double Square)> Changes=new();
        public double FlowSum,DepthSum,SquareSum;public long QtySum,SignedSum;
        public void Trim(DateTimeOffset at)
        {
            var cutoff=at.AddSeconds(-5);
            while(Quotes.TryPeek(out var q)&&q.At<cutoff)Floor=Quotes.Dequeue();
            while(Ofi.TryPeek(out var f)&&f.At<=cutoff){Ofi.Dequeue();FlowSum-=f.Flow;DepthSum-=f.Depth;}
            while(Volume.TryPeek(out var v)&&v.At<=cutoff){Volume.Dequeue();QtySum-=v.Qty;SignedSum-=v.Signed;}
            while(Changes.TryPeek(out var c)&&c.At<=at.AddSeconds(-30)){Changes.Dequeue();SquareSum-=c.Square;}
        }
        public Quote? AtOrBefore(DateTimeOffset at)
        {
            Quote? found=Floor.HasValue&&Floor.Value.At<=at?Floor:null;
            foreach(var q in Quotes){if(q.At>at)break;found=q;}return found;
        }
    }
    private readonly Dictionary<string,State> states=new(){["225MC"]=new(),["225M"]=new(),["225"]=new()};
    private DateTimeOffset? lastAt;private long lastSequence;private string? expiry;
    private State Accept(string product,DateTimeOffset at,long sequence)
    {
        if(!states.TryGetValue(product,out var state))throw new ArgumentException("Unknown feature product");
        if(lastAt.HasValue&&(at<lastAt || sequence<=lastSequence))throw new ArgumentException("Out-of-order feature event");
        lastAt=at;lastSequence=sequence;state.Trim(at);return state;
    }
    public void OnBook(string product,DateTimeOffset at,long sequence,string symbol,decimal bid,decimal ask,int bidQty,int askQty,bool validBook=true)
    {
        var suffix=symbol[(symbol.LastIndexOf('-')+1)..];
        if(!symbol.StartsWith(product+"-",StringComparison.Ordinal)||suffix==symbol||(expiry!=null&&suffix!=expiry))
            throw new ArgumentException("Cross-product expiry mismatch");
        var s=Accept(product,at,sequence);expiry??=suffix;
        if(s.Symbol.Length>0&&s.Symbol!=symbol)throw new ArgumentException("Contract switch inside feature stream");
        s.Symbol=symbol;
        if(!validBook||bid<=0||ask<=bid||ask-bid>10||bidQty<0||askQty<0){s.Latest=null;return;}
        var q=new Quote(at,bid,ask,bidQty,askQty);double flow=0;
        if(s.Latest is Quote previous)
        {
            flow=(bid>=previous.Bid?bidQty:0)-(bid<=previous.Bid?previous.Bq:0)
                -(ask<=previous.Ask?askQty:0)+(ask>=previous.Ask?previous.Aq:0);
            double change=(double)((q.Mid-previous.Mid)/5);var square=change*change;
            s.Changes.Enqueue((at,square));s.SquareSum+=square;
        }
        s.Ofi.Enqueue((at,flow,q.Depth));s.FlowSum+=flow;s.DepthSum+=q.Depth;
        s.Quotes.Enqueue(q);s.Latest=q;
    }
    public void OnTrade(string product,DateTimeOffset at,long sequence,decimal price,int quantity)
    {
        var s=Accept(product,at,sequence);int qty=Math.Max(0,quantity),sign=0;
        if(s.Latest is Quote q&&(at-q.At).TotalSeconds<=2)
            sign=price>=q.Ask?1:price<=q.Bid?-1:0;
        s.Volume.Enqueue((at,qty,qty*sign));s.QtySum+=qty;s.SignedSum+=qty*sign;
    }
    public double[] Snapshot(DateTimeOffset at)
    {
        if(lastAt.HasValue&&at<lastAt)throw new ArgumentException("Snapshot precedes received event");
        foreach(var s in states.Values)s.Trim(at);
        double Ofi(string product){var s=states[product];return s.Ofi.Count>0?s.FlowSum/Math.Max(1,s.DepthSum/s.Ofi.Count):0;}
        double Return(string product)
        {
            var s=states[product];var past=s.AtOrBefore(at.AddSeconds(-1));
            return s.Latest is Quote q&&past.HasValue?(double)((q.Mid-past.Value.Mid)/5):0;
        }
        var mc=states["225MC"];var previous=mc.AtOrBefore(at.AddSeconds(-5));
        return [Ofi("225MC"),Ofi("225M"),Ofi("225"),Return("225M")-Return("225MC"),Return("225")-Return("225MC"),
            mc.QtySum>0?mc.SignedSum/(double)mc.QtySum:0,Math.Sqrt(Math.Max(0,mc.SquareSum)),
            mc.Latest is Quote current&&previous.HasValue?(double)((current.Ask-current.Bid-previous.Value.Ask+previous.Value.Bid)/5):0];
    }
}
