using KGIOptionPricing.StrategyFramework;
using KGIOptionPricing.Strategies.BuySellPower;
using NikkeiRl;
namespace KGIOptionPricing.Strategies.NikkeiRlDirection;

public sealed record RlMarketSnapshot(DateTimeOffset At,double[] Market,double[] Research,bool Valid,decimal Bid,decimal Ask);
/// <summary>Same formulas, ordering and exclude-flow readiness as ReplayCache research3s.</summary>
public sealed class NikkeiRlFeatureStream
{
    static readonly int[] ExcludedFlowInputs=[48,49,50,51,52,53];
    readonly decimal tick;
    readonly Dictionary<string,ProductState> products;
    Dictionary<string,PowerIndicator> power;
    ResearchFeatureAccumulator research;
    DateTimeOffset? first,last,watermark;
    public const int MinimumWarmupSeconds=210;
    public decimal WarmupElapsedSeconds(DateTimeOffset at)=>first.HasValue?Math.Max(0,(decimal)(at-first.Value).TotalSeconds):0;
    public NikkeiRlFeatureStream(decimal tickSize)
    {tick=tickSize;products=new[]{"225MC","225M"}.ToDictionary(x=>x,_=>new ProductState(tick));power=products.Keys.ToDictionary(x=>x,_=>new PowerIndicator(new PowerParameters()));research=new(tick);}
    public bool CanAccept(DateTimeOffset at)=>at!=DateTimeOffset.MinValue&&(!last.HasValue||at>=last)&&(!watermark.HasValue||at>=watermark);
    public bool Accept(MarketEvent e)
    {
        if(!products.TryGetValue(e.ProductId,out var s))return true;
        if(!CanAccept(e.Timestamp))return false;
        if(last.HasValue&&(e.Timestamp-last.Value).TotalSeconds>10){power=products.Keys.ToDictionary(x=>x,_=>new PowerIndicator(new PowerParameters()));research=new(tick);first=e.Timestamp;}
        first??=e.Timestamp;last=e.Timestamp;
        if(e.Kind==MarketEventKind.Book){s.OnBook(e);if(e.ProductId=="225MC")research.OnBook(e.Timestamp,e.BidPrices,e.AskPrices,e.BidQuantities,e.AskQuantities);}
        if(e.Kind==MarketEventKind.Trade&&e.LastPrice>0)
        {s.OnTrade(e);power[e.ProductId].OnTrade(e.Timestamp,e.LastPrice,e.TradeQuantity,e.BestBid>0&&e.BestAsk>e.BestBid?e.BestBid:s.Bid,e.BestBid>0&&e.BestAsk>e.BestBid?e.BestAsk:s.Ask);if(e.ProductId=="225MC")research.OnTrade(e.Timestamp,e.LastPrice,e.TradeQuantity);}
        return true;
    }
    public RlMarketSnapshot Emit(DateTimeOffset at)
    {
        if(watermark.HasValue&&at<watermark||last.HasValue&&at<last)throw new ArgumentException("決策時間倒退");watermark=at;
        var features=products.Values.SelectMany(s=>s.Features(at)).ToArray();
        var powers=power.Values.Select(p=>{p.Advance(at);return p.Snapshot();}).ToArray();
        features=features.Concat(powers.SelectMany(s=>new[]{(double)(s.BuyPower??0)/100,(double)(s.ShortAverage??0)/100,(double)(s.LongAverage??0)/100,(double)(s.BaisPower??0)/100,s.CurrentMovement?.Direction=="上漲"?1d:s.CurrentMovement?.Direction=="下跌"?-1d:0d})).ToArray();
        var extra=research.Snapshot(at);
        bool valid=powers.All(s=>s.IsWarm)&&products.Values.All(s=>s.Healthy(at))&&(products.Values.Max(s=>s.BookAt)-products.Values.Min(s=>s.BookAt)).TotalSeconds<=1&&first.HasValue&&(at-first.Value).TotalSeconds>=60&&research.IsReadyForActorFeatures(ExcludedFlowInputs);
        var mc=products["225MC"];foreach(var s in products.Values)s.Advance(at);return new(at,features,extra,valid,mc.Bid,mc.Ask);
    }
    private sealed class ProductState(decimal tickSize)
    {
        public decimal Bid,Ask,Last;public DateTimeOffset BookAt;
        readonly Queue<(DateTimeOffset At,decimal Price)> trades=new();
        readonly Queue<(DateTimeOffset At,int Qty,int Sign)> volumes=new();double obi1,obi5;
        public void OnBook(MarketEvent e)
        {
            Bid=e.BestBid;Ask=e.BestAsk;BookAt=e.Timestamp;
            static double Obi(int[] b,int[] a,int n){double x=b.Take(n).Sum(),y=a.Take(n).Sum();return x+y>0?(x-y)/(x+y):0;}
            obi1=Obi(e.BidQuantities,e.AskQuantities,1);obi5=Obi(e.BidQuantities,e.AskQuantities,5);
            if(e.BidPrices.Length<5||e.AskPrices.Length<5||e.BidQuantities.Length<5||e.AskQuantities.Length<5||e.BidPrices.Take(5).Any(x=>x<=0)||e.AskPrices.Take(5).Any(x=>x<=0))BookAt=DateTimeOffset.MinValue;
        }
        public void OnTrade(MarketEvent e){int sign=Last==0?0:Math.Sign(e.LastPrice-Last);Last=e.LastPrice;trades.Enqueue((e.Timestamp,e.LastPrice));volumes.Enqueue((e.Timestamp,Math.Max(0,e.TradeQuantity),sign));}
        public bool Healthy(DateTimeOffset at)=>Bid>0&&Ask>Bid&&Ask-Bid<=2*tickSize&&BookAt!=DateTimeOffset.MinValue&&(at-BookAt).TotalSeconds is >=0 and <=2&&Last>0;
        public double[] Features(DateTimeOffset at)
        {
            double Ret(int seconds){var past=trades.LastOrDefault(x=>x.At<at.AddSeconds(-seconds));return past.Price>0?(double)((Last-past.Price)/tickSize):0;}
            var recent=volumes.Where(x=>x.At>at.AddSeconds(-5)).ToArray();double qty=recent.Sum(x=>(double)x.Qty);
            return [Ret(5),Ret(15),Ret(30),Ret(60),(double)((Ask-Bid)/tickSize),obi1,obi5,Math.Clamp((at-BookAt).TotalSeconds,0,60),Math.Log(1+qty),qty>0?recent.Sum(x=>(double)x.Qty*x.Sign)/qty:0];
        }
        public void Advance(DateTimeOffset at){while(trades.Count>1&&trades.ElementAt(1).At<at.AddSeconds(-65))trades.Dequeue();while(volumes.Count>0&&volumes.Peek().At<=at.AddSeconds(-30))volumes.Dequeue();}
    }
}
