namespace NikkeiRl;

/// <summary>Snapshot-computable paper features; no order identity or future data.</summary>
public sealed class PaperFeatureAccumulator
{
    public static readonly string[] Names=["mc_mlofi5s_level2","mc_mlofi5s_level3","mc_mlofi5s_level4","mc_mlofi5s_level5",
        "mc_log_mean_bid_depth5","mc_log_mean_ask_depth5","mc_bid_depth_derivative5s","mc_ask_depth_derivative5s"];
    public bool LastSnapshotHealthy {get;private set;}
    public bool LastDerivativeAnchorAvailable {get;private set;}
    private sealed record Book(DateTimeOffset At,decimal[] Bid,decimal[] Ask,int[] Bq,int[] Aq)
    { public double MeanBid=>Bq.Average();public double MeanAsk=>Aq.Average(); }
    private sealed record Flow(DateTimeOffset At,double[] Values,double[] Depth);
    private readonly Queue<Book> books=new();private readonly Queue<Flow> flows=new();
    private readonly double[] sums=new double[4],depths=new double[4];
    private Book? latest,floor;private DateTimeOffset? frontier;private long sequence;private string? symbol;
    private void Trim(DateTimeOffset at)
    {
        var cutoff=at.AddSeconds(-5);
        while(books.TryPeek(out var b)&&b.At<cutoff)floor=books.Dequeue();
        while(flows.TryPeek(out var f)&&f.At<=cutoff)
        {flows.Dequeue();for(int i=0;i<4;i++){sums[i]-=f.Values[i];depths[i]-=f.Depth[i];}}
    }
    public void OnBook(DateTimeOffset at,long seq,string contract,decimal[] bid,decimal[] ask,int[] bq,int[] aq)
    {
        if(frontier.HasValue&&at<frontier||seq<=sequence)throw new ArgumentException("Out-of-order paper feature event");
        if(!contract.StartsWith("225MC-",StringComparison.Ordinal)||symbol!=null&&symbol!=contract)throw new ArgumentException("Paper contract switch");
        frontier=at;sequence=seq;symbol=contract;Trim(at);
        bool valid=bid.Length>=5&&ask.Length>=5&&bq.Length>=5&&aq.Length>=5;
        if(valid)valid=bid.Take(5).All(x=>x>0)&&ask.Take(5).All(x=>x>0)&&bq.Take(5).All(x=>x>=0)&&aq.Take(5).All(x=>x>=0)
            &&ask[0]>bid[0]&&ask[0]-bid[0]<=10&&Enumerable.Range(1,4).All(i=>bid[i]<bid[i-1]&&ask[i]>ask[i-1]);
        if(!valid){latest=null;return;}
        var book=new Book(at,bid.Take(5).ToArray(),ask.Take(5).ToArray(),bq.Take(5).ToArray(),aq.Take(5).ToArray());
        double[] flow=new double[4],depth=new double[4];
        for(int i=0;i<4;i++)
        {
            int rank=i+1;depth[i]=(book.Bq[rank]+(double)book.Aq[rank])/2;
            if(latest is Book previous&&(at-previous.At).TotalSeconds<=2)
                flow[i]=(book.Bid[rank]>=previous.Bid[rank]?book.Bq[rank]:0)-(book.Bid[rank]<=previous.Bid[rank]?previous.Bq[rank]:0)
                    -(book.Ask[rank]<=previous.Ask[rank]?book.Aq[rank]:0)+(book.Ask[rank]>=previous.Ask[rank]?previous.Aq[rank]:0);
            sums[i]+=flow[i];depths[i]+=depth[i];
        }
        flows.Enqueue(new(at,flow,depth));books.Enqueue(book);latest=book;
    }
    public double[] Snapshot(DateTimeOffset at)
    {
        if(frontier.HasValue&&at<frontier)throw new ArgumentException("Snapshot precedes paper event");
        frontier=at;Trim(at);var result=new double[8];LastSnapshotHealthy=false;LastDerivativeAnchorAvailable=false;
        if(latest is not Book current||(at-current.At).TotalSeconds>2)return result;
        LastSnapshotHealthy=true;
        for(int i=0;i<4;i++)result[i]=flows.Count>0?sums[i]/Math.Max(1,depths[i]/flows.Count):0;
        result[4]=Math.Log(1+current.MeanBid);result[5]=Math.Log(1+current.MeanAsk);
        var cutoff=at.AddSeconds(-5);Book? past=floor?.At<=cutoff?floor:null;
        foreach(var b in books){if(b.At>cutoff)break;past=b;}
        if(past!=null&&(cutoff-past.At).TotalSeconds<=2)
        {LastDerivativeAnchorAvailable=true;result[6]=(current.MeanBid-past.MeanBid)/5;result[7]=(current.MeanAsk-past.MeanAsk)/5;}
        return result;
    }
}
