namespace NikkeiFactorExport;
public sealed record Minute(double End,double Amount,double Volume,bool Complete,double? Open,double? High,double? Low,double? Close,double? Vwar);
public sealed record FactorSnapshot(double[][]? Book,double? Ofi5,double? SignedVolume5,double Count5,double? Cv200,double? SignAc200,double? Run200,double? Hhi200,double UnknownShare200,double? Vwap30,double Coverage30,double GridVolume,Minute[] Bars);
public sealed class FactorAccumulator
{
    record Trade(double At,double Price,double Qty,int Sign);
    readonly Queue<Trade> trades=new();readonly Queue<(double At,double Value)> ofi=new();readonly List<Minute> emitted=new();
    double? lastReceived,minuteStart,bookAt,bboAt,flowKnown,lastSnapshot,lastTrade,priorGridPrice;
    double[][]? book;DateTimeOffset? lastExact;double oldBid,oldAsk,oldBq,oldAq;int sign,run;
    double amount,volume,gridVolume,vwarWeighted,vwarVolume;bool complete;double? opening,high,low,closing;
    public Minute? LastClosed {get;private set;}
    static double Seconds(DateTimeOffset at)=>at.ToUnixTimeMilliseconds()/1000d+(at.Ticks%TimeSpan.TicksPerMillisecond)/1e7;
    void CloseUntil(double at)
    {
        if(!minuteStart.HasValue)return;
        while(minuteStart+60<=at)
        {
            bool healthy=complete&&lastReceived.HasValue&&minuteStart+60-lastReceived<=10;
            LastClosed=new(minuteStart.Value+60,amount,volume,healthy,opening,high,low,closing,vwarVolume>0?vwarWeighted/vwarVolume:null);emitted.Add(LastClosed);
            minuteStart+=60;amount=volume=vwarWeighted=vwarVolume=0;opening=high=low=closing=null;complete=lastReceived.HasValue&&minuteStart-lastReceived<=10;
        }
    }
    public void Event(DateTimeOffset at,string product,string kind,double price,double qty,double[]? bid,double[]? ask,double[]? bq,double[]? aq)
    {
        double t=Seconds(at);if(lastReceived.HasValue&&t<lastReceived)throw new Exception("Out of order factor event");if(lastSnapshot.HasValue&&t<lastSnapshot)throw new Exception("Late factor event");
        if(!minuteStart.HasValue){minuteStart=Math.Floor(t/60)*60;complete=minuteStart==t;}
        if(lastReceived.HasValue&&t-lastReceived>10){complete=false;trades.Clear();ofi.Clear();flowKnown=null;book=null;bookAt=null;priorGridPrice=null;lastTrade=null;sign=run=0;}
        CloseUntil(t);lastReceived=t;lastExact=at;if(product!="225MC")return;
        if(kind.StartsWith("depth"))
        {
            bool bbo=bid is {Length:>0}&&ask is {Length:>0}&&bq is {Length:>0}&&aq is {Length:>0}&&bid[0]>0&&ask[0]>bid[0]&&bq[0]>=0&&aq[0]>=0&&bq[0]+aq[0]>0;
            if(bbo)
            {
                if(bboAt.HasValue&&t-bboAt<=2&&flowKnown.HasValue){double v=(bid![0]>=oldBid?bq![0]:0)-(bid[0]<=oldBid?oldBq:0)-(ask![0]<=oldAsk?aq![0]:0)+(ask[0]>=oldAsk?oldAq:0);ofi.Enqueue((t,v));}
                else{ofi.Clear();flowKnown=t;}
                oldBid=bid![0];oldAsk=ask![0];oldBq=bq![0];oldAq=aq![0];
            }
            else{ofi.Clear();flowKnown=null;}
            bboAt=bbo?t:null;
            bool valid=bbo&&new[]{bid,ask,bq,aq}.All(a=>a is {Length:5})&&bid!.All(p=>p>0)&&ask!.All(p=>p>0)&&bq!.All(p=>p>=0)&&aq!.All(p=>p>=0)&&bq!.Sum()>0&&aq!.Sum()>0&&Enumerable.Range(1,4).All(i=>bid![i]<bid[i-1]&&ask![i]>ask[i-1]);
            book=valid?[bid!,ask!,bq!,aq!]:null;bookAt=valid?t:null;return;
        }
        if(kind!="trade")return;if(price<=0||qty<=0||!double.IsFinite(price+qty)){complete=false;return;}
        bool fresh=bboAt.HasValue&&t-bboAt<=2;int s=fresh&&price>=oldAsk?1:fresh&&price<=oldBid?-1:lastTrade.HasValue?Math.Sign(price-lastTrade.Value):0;
        if(s==0)s=sign;if(s!=0){run=s==sign?run+1:1;sign=s;}
        lastTrade=price;trades.Enqueue(new(t,price,qty,s));gridVolume+=qty;amount+=price*qty;volume+=qty;
        opening??=price;high=high.HasValue?Math.Max(high.Value,price):price;low=low.HasValue?Math.Min(low.Value,price):price;closing=price;
        while(trades.Count>201&&trades.Peek().At<t-30)trades.Dequeue();
    }
    static double? Corr(double[] x,double[] y)
    {if(x.Length<3)return null;double mx=x.Average(),my=y.Average(),xx=x.Sum(v=>(v-mx)*(v-mx)),yy=y.Sum(v=>(v-my)*(v-my));return xx>0&&yy>0?x.Zip(y).Sum(p=>(p.First-mx)*(p.Second-my))/Math.Sqrt(xx*yy):null;}
    public FactorSnapshot Snapshot(DateTimeOffset at)
    {
        double t=Seconds(at);if(lastExact.HasValue&&lastExact>=at)throw new Exception("Future/boundary factor event");if(lastSnapshot.HasValue&&t-lastSnapshot!=3){complete=false;priorGridPrice=null;}
        if(priorGridPrice.HasValue&&lastTrade.HasValue&&gridVolume>0){vwarWeighted+=gridVolume*(lastTrade.Value/priorGridPrice.Value-1);vwarVolume+=gridVolume;}
        priorGridPrice=lastTrade;CloseUntil(t);lastSnapshot=t;while(ofi.Count>0&&ofi.Peek().At<=t-5)ofi.Dequeue();
        var recent=trades.Where(e=>e.At>t-5).ToArray();var thirty=trades.Where(e=>e.At>t-30).ToArray();var n=trades.TakeLast(200).ToArray();var intervals=trades.TakeLast(201).Select(e=>e.At).Zip(trades.TakeLast(201).Select(e=>e.At).Skip(1)).Select(p=>p.Second-p.First).ToArray();
        double? cv=null;if(intervals.Length==200&&intervals.Average()>0){double mean=intervals.Average();cv=Math.Sqrt(intervals.Sum(v=>(v-mean)*(v-mean))/200)/mean;}
        double total=thirty.Sum(e=>e.Qty),total5=recent.Sum(e=>e.Qty),nqty=n.Sum(e=>e.Qty);bool signed=n.Length==200&&n.All(e=>e.Sign!=0);
        var snapshots=emitted.ToArray();emitted.Clear();double grid=gridVolume;gridVolume=0;
        return new(bookAt.HasValue&&t-bookAt<=2?book:null,flowKnown.HasValue&&t-flowKnown>=5&&bboAt.HasValue&&t-bboAt<=2?ofi.Sum(e=>e.Value):null,
            total5>0&&recent.All(e=>e.Sign!=0)?recent.Sum(e=>e.Qty*e.Sign)/total5:null,recent.Length,cv,
            signed?Corr(n.Skip(1).Select(e=>(double)e.Sign).ToArray(),n.Take(n.Length-1).Select(e=>(double)e.Sign).ToArray()):null,
            signed?sign*Math.Log(1+Math.Min(run,200)):null,n.Length==200&&nqty>0?(200*n.Sum(e=>Math.Pow(e.Qty/nqty,2))-1)/199:null,
            n.Length>0?n.Count(e=>e.Sign==0)/(double)n.Length:1,total>0?thirty.Sum(e=>e.Price*e.Qty)/total:null,
            thirty.Select(e=>Math.Floor(e.At/3)).Distinct().Count()/10d,grid,snapshots);
    }
}
