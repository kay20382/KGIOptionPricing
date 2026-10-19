using KGIOptionPricing.StrategyFramework;
namespace KGIOptionPricing.Strategies.MultiFactorMeanR17;

// Single observer shared by isolated strategy hosts. Reads do not mutate or fit.
// OFI is observed MBP proxy, not exchange-verified complete order flow.
public sealed class FactorState
{
    sealed class Product
    {
        public MarketEvent? Book;
        public long Sequence;
        public Queue<(DateTimeOffset At,decimal Mid)> Mids=new();
        public (DateTimeOffset At,decimal Mid)? OpenSample;
        public Queue<(DateTimeOffset At,decimal Value)> Flow=new();
        public Queue<(DateTimeOffset At,decimal Price,int Quantity)> Prints=new();
        public DateTimeOffset? FirstPrint;
        public Queue<DateTimeOffset> Arrivals=new();
        public DateTimeOffset? LastEvent;
    }
    public sealed record Snapshot(decimal Mid,decimal QueueImbalance,decimal DepthVariance,
        decimal Ofi,decimal? MomentumTicks,decimal? MeanDeviationTicks,decimal? Efficiency,
        decimal? DepthSkew,decimal? WeightedMid,decimal? BestDepthShareDifference=null,decimal? VwapDeviationTicks=null,
        decimal? LogRealizedVariance30=null,decimal? DownsideVarianceShare30=null,decimal? TickVolatility30=null,
        decimal? LastReturnTicks=null,decimal? TradeIntervalCV200=null,decimal? PrintRate30=null);
    readonly Dictionary<string,Product> _products=new();
    readonly Dictionary<string,(DateTimeOffset At,decimal Tick,Snapshot? Value)> _readCache=new();
    DateTimeOffset _watermark;
    string _session="";
    long? _generation;
    public static FactorState Shared {get;set;}=new();
    static string Session(DateTimeOffset at)
    {
        var t=at.ToOffset(TimeSpan.FromHours(8));
        if(t.TimeOfDay>=TimeSpan.FromHours(7.75) && t.TimeOfDay<TimeSpan.FromHours(14.75))return t.Date.ToString("yyyyMMdd")+"D";
        if(t.TimeOfDay>=TimeSpan.FromHours(16))return t.Date.ToString("yyyyMMdd")+"N";
        if(t.TimeOfDay<TimeSpan.FromHours(5))return t.Date.AddDays(-1).ToString("yyyyMMdd")+"N";
        return "closed";
    }
    public void OnMarket(MarketEvent e)
    {
        if(e.Timestamp<_watermark || _generation.HasValue && e.ConnectionGeneration<_generation || e.ProvenanceWarning=="DuplicateOrOutOfOrder")return;
        string session=Session(e.Timestamp);
        if(session!=_session || _generation.HasValue && e.ConnectionGeneration!=_generation || e.Kind==MarketEventKind.Connection){_products.Clear();_session=session;}
        _watermark=e.Timestamp;_generation=e.ConnectionGeneration;
        _readCache.Clear();
        if(e.ProductId is not ("225" or "225M" or "225MC"))return;
        if(!_products.TryGetValue(e.ProductId,out var p))_products[e.ProductId]=p=new();
        if(e.Sequence>0 && e.Sequence<=p.Sequence)return;
        p.Sequence=e.Sequence;
        if(p.LastEvent.HasValue && e.Timestamp>p.LastEvent.Value.AddSeconds(60))
        {p.Book=null;p.Mids.Clear();p.OpenSample=null;p.Flow.Clear();p.Prints.Clear();p.FirstPrint=null;p.Arrivals.Clear();}
        p.LastEvent=e.Timestamp;
        if(e.Kind==MarketEventKind.Trade && e.LastPrice>0 && e.TradeQuantity>0 && session!="closed")
        {
            p.FirstPrint??=e.Timestamp;
            p.Prints.Enqueue((e.Timestamp,e.LastPrice,e.TradeQuantity));
            p.Arrivals.Enqueue(e.Timestamp);
            while(p.Arrivals.Count>201)p.Arrivals.Dequeue();
        }
        while(p.Prints.Count>0 && p.Prints.Peek().At<=e.Timestamp.AddSeconds(-30))p.Prints.Dequeue();
        if(e.Kind!=MarketEventKind.Book && !(e.BidQuantities.Length>0 && e.AskQuantities.Length>0))return;
        var old=p.Book;p.Book=e;
        if(!Valid(e))return;
        decimal mid=(e.BestBid+e.BestAsk)/2;
        // Fixed-second samples are the final observation of each COMPLETED second.
        // Replace open sample; Read excludes the currently open second.
        if(p.OpenSample.HasValue && p.OpenSample.Value.At.ToUnixTimeSeconds()!=e.Timestamp.ToUnixTimeSeconds())p.Mids.Enqueue(p.OpenSample.Value);
        p.OpenSample=(e.Timestamp,mid);
        while(p.Mids.Count>0 && p.Mids.Peek().At<e.Timestamp.AddSeconds(-65))p.Mids.Dequeue();
        if(old!=null && Valid(old))
        {
            decimal ofi=(e.BestBid>=old.BestBid?e.BidQuantities[0]:0)-(e.BestBid<=old.BestBid?old.BidQuantities[0]:0)
                       -(e.BestAsk<=old.BestAsk?e.AskQuantities[0]:0)+(e.BestAsk>=old.BestAsk?old.AskQuantities[0]:0);
            p.Flow.Enqueue((e.Timestamp,ofi));
        }
        while(p.Flow.Count>0 && p.Flow.Peek().At<=e.Timestamp.AddSeconds(-10))p.Flow.Dequeue();
    }
    static bool Valid(MarketEvent b)=>b.BestBid>0 && b.BestAsk>b.BestBid && b.BidQuantities.Length>0 && b.AskQuantities.Length>0 && b.BidQuantities[0]>0 && b.AskQuantities[0]>0;
    public Snapshot? Read(string id,DateTimeOffset at,decimal tick)
    {
        if(_readCache.TryGetValue(id,out var cached) && cached.At==at && cached.Tick==tick)return cached.Value;
        var value=Calculate(id,at,tick);
        _readCache[id]=(at,tick,value);
        return value;
    }
    Snapshot? Calculate(string id,DateTimeOffset at,decimal tick)
    {
        if(at<_watermark)return null;
        if(!_products.TryGetValue(id,out var p) || p.Book is not {} b || !Valid(b) || tick<=0 || at<b.Timestamp || at>b.Timestamp.AddSeconds(2))return null;
        int n=Math.Min(5,new[]{b.BidPrices.Length,b.AskPrices.Length,b.BidQuantities.Length,b.AskQuantities.Length}.Min());
        if(n<3)return null;
        var bid=b.BidQuantities.Take(n).Select(x=>(decimal)x).ToArray();var ask=b.AskQuantities.Take(n).Select(x=>(decimal)x).ToArray();
        if(bid.Any(x=>x<=0) || ask.Any(x=>x<=0))return null;
        if(b.BidPrices.Take(n).Zip(b.BidPrices.Skip(1).Take(n-1)).Any(x=>x.First<=x.Second)
            || b.AskPrices.Take(n).Zip(b.AskPrices.Skip(1).Take(n-1)).Any(x=>x.First>=x.Second))return null;
        decimal Variance(decimal[] quantity,bool isBid)
        {
            var prices=isBid?b.BidPrices:b.AskPrices;var distance=prices.Take(n).Select(x=>Math.Abs(x-prices[0])/tick).ToArray();
            decimal total=quantity.Sum(),mean=Enumerable.Range(0,n).Sum(i=>quantity[i]*distance[i])/total;
            return Enumerable.Range(0,n).Sum(i=>quantity[i]*(distance[i]-mean)*(distance[i]-mean))/total;
        }
        var history=p.Mids.Where(x=>x.At.ToUnixTimeSeconds()<at.ToUnixTimeSeconds() && x.At>=at.AddSeconds(-60)).ToArray();
        decimal? momentum=null,deviation=null,efficiency=null,rv=null,downside=null,tickVolatility=null,lastReturn=null;
        if(history.Length>=30 && (history[^1].At-history[0].At).TotalSeconds>=29)
        {
            // Require consecutive completed samples; no interpolation across gaps.
            bool complete=history.Zip(history.Skip(1)).All(x=>x.Second.At.ToUnixTimeSeconds()-x.First.At.ToUnixTimeSeconds()==1);
            if(complete && at-history[^1].At<=TimeSpan.FromSeconds(2))
            {
                momentum=(history[^1].Mid-history[0].Mid)/tick;
                deviation=(history.Average(x=>x.Mid)-history[^1].Mid)/tick;
                decimal path=history.Zip(history.Skip(1)).Sum(x=>Math.Abs(x.Second.Mid-x.First.Mid));
                efficiency=path>0?Math.Abs(history[^1].Mid-history[0].Mid)/path:null;
                lastReturn=(history[^1].Mid-history[^2].Mid)/tick;
            }
        }
        // Fixed30 statistics use their own completed window; no missing-second interpolation.
        if(history.Length>=31)
        {
            var fixedWindow=history.TakeLast(31).ToArray();
            bool complete=fixedWindow.Zip(fixedWindow.Skip(1)).All(x=>x.Second.At.ToUnixTimeSeconds()-x.First.At.ToUnixTimeSeconds()==1);
            if(complete && at-fixedWindow[^1].At<=TimeSpan.FromSeconds(2))
            {
                var returns=fixedWindow.Zip(fixedWindow.Skip(1)).Select(x=>(decimal)Math.Log((double)(x.Second.Mid/x.First.Mid))).ToArray();
                rv=returns.Sum(x=>x*x);
                downside=rv>0?returns.Where(x=>x<0).Sum(x=>x*x)/rv:null;
                decimal tickSquares=fixedWindow.Zip(fixedWindow.Skip(1)).Sum(x=>{decimal move=(x.Second.Mid-x.First.Mid)/tick;return move*move;});
                tickVolatility=(decimal)Math.Sqrt((double)(tickSquares/30));
            }
        }
        decimal totalDepth=bid.Sum()+ask.Sum();
        decimal? vwapDeviation=null,printRate=null,arrivalCv=null;
        if(p.FirstPrint.HasValue && at>=p.FirstPrint.Value.AddSeconds(30))
        {
            var trades=p.Prints.Where(x=>x.At>at.AddSeconds(-30) && x.At<=at).ToArray();
            long volume=trades.Sum(x=>(long)x.Quantity);
            printRate=trades.Length/30m;
            if(volume>0)vwapDeviation=(trades.Sum(x=>x.Price*x.Quantity)/volume-(b.BestBid+b.BestAsk)/2)/tick;
        }
        if(p.Arrivals.Count==201)
        {
            var points=p.Arrivals.ToArray();
            var intervals=points.Zip(points.Skip(1)).Select(x=>(decimal)(x.Second-x.First).TotalSeconds).ToArray();
            decimal mean=intervals.Average();
            if(mean>0)arrivalCv=(decimal)Math.Sqrt((double)(intervals.Sum(x=>(x-mean)*(x-mean))/200))/mean;
        }
        return new((b.BestBid+b.BestAsk)/2,(bid[0]-ask[0])/(bid[0]+ask[0]),(Variance(bid,true)+Variance(ask,false))/2,
            p.Flow.Where(x=>x.At>at.AddSeconds(-10) && x.At<=at).Sum(x=>x.Value),momentum,deviation,efficiency,
            (bid.Sum()-ask.Sum())/totalDepth,(b.BestBid*ask[0]+b.BestAsk*bid[0])/(bid[0]+ask[0]),
            n==5?bid[0]/bid.Sum()-ask[0]/ask.Sum():null,vwapDeviation,rv,downside,tickVolatility,lastReturn,arrivalCv,printRate);
    }
}
