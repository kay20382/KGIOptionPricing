namespace NikkeiRl;

/// <summary>Causal five-level snapshot/trade features. Aggregate depth is not individual order data.</summary>
public sealed class ResearchFeatureAccumulator
{
    public static readonly string[] Names=["mc_log_visible_depth5","mc_weighted_mid_offset_ticks","mc_gap_asymmetry",
        "mc_sweep5_cost_skew","mc_depth_centroid_asymmetry","mc_depth_spatial_dispersion_ticks",
        "mc_max_depth_position_asymmetry","mc_depth_curvature_asymmetry",
        "mc_ofi5s_depth_normalized","mc_quote_signed_volume5s","mc_log_arrival_rate5s",
        "mc_trade_sign_autocorrelation30s","mc_signed_trade_run_length","mc_trade_size_hhi30s",
        "mc_mid_momentum15s_ticks","mc_signed_path_efficiency30s","mc_log_realized_variance30s",
        "mc_downside_semivariance_share30s","mc_return_autocorrelation30s","mc_abs_return_autocorrelation30s"];
    readonly double tick;
    decimal[] bids=[],asks=[];int[] bq=[],aq=[];
    DateTimeOffset? lastEvent,lastSnapshot,bookAt,flowBookAt,flowKnownFrom;
    bool flowBookReady;decimal flowBid,flowAsk;int flowBidQty,flowAskQty;
    decimal lastTrade;int lastSign,run;
    bool snapshotCurrent;
    readonly Queue<(DateTimeOffset At,double Value)> ofi=new();
    readonly Queue<(DateTimeOffset At,double Qty,int Sign)> trades=new();
    readonly Queue<(DateTimeOffset At,double Mid)> mids=new();
    public bool BookReady {get;private set;}
    public bool BookPathReady {get;private set;}
    public bool FlowReady {get;private set;}
    public bool SnapshotReady {get;private set;}
    // Raw60 indices48..53 are the six flow features. Partial pruning retains
    // the original requirement; inactive features cannot waive active path health.
    public bool IsReadyForActorFeatures(IReadOnlyCollection<int> excludedRawIndices)
    {
        ArgumentNullException.ThrowIfNull(excludedRawIndices);
        if(excludedRawIndices.Any(i=>i<0||i>=60)||excludedRawIndices.Distinct().Count()!=excludedRawIndices.Count)
            throw new ArgumentException("Unique raw60 feature indices required",nameof(excludedRawIndices));
        if(!snapshotCurrent)return false;
        bool allFlowExcluded=Enumerable.Range(48,6).All(excludedRawIndices.Contains);
        return BookPathReady&&(allFlowExcluded||FlowReady);
    }
    public ResearchFeatureAccumulator(decimal tickSize){if(tickSize<=0)throw new ArgumentException("Positive tick required");tick=(double)tickSize;}
    void EventAt(DateTimeOffset at){if(lastEvent.HasValue&&at<lastEvent)throw new ArgumentException("Out of order feature event");lastEvent=at;snapshotCurrent=false;}
    public void OnBook(DateTimeOffset at,decimal[] bid,decimal[] ask,int[] bidQty,int[] askQty)
    {
        EventAt(at);
        // OFI only needs BBO, not sweep-size availability or deep-level shape.
        bool bboValid=bid.Length>0&&ask.Length>0&&bidQty.Length>0&&askQty.Length>0&&bid[0]>0&&ask[0]>bid[0]&&bidQty[0]>=0&&askQty[0]>=0&&bidQty[0]+askQty[0]>0;
        if(bboValid)
        {
            if(flowBookReady&&flowBookAt.HasValue&&(at-flowBookAt.Value).TotalSeconds<=2)
            {
                double flow=(bid[0]>=flowBid?bidQty[0]:0)-(bid[0]<=flowBid?flowBidQty:0)
                    -(ask[0]<=flowAsk?askQty[0]:0)+(ask[0]>=flowAsk?flowAskQty:0);
                ofi.Enqueue((at,flow/Math.Max(1,(bidQty[0]+askQty[0]+flowBidQty+flowAskQty)/2d)));
            }
            else {flowKnownFrom=at;ofi.Clear();}
            flowBid=bid[0];flowAsk=ask[0];flowBidQty=bidQty[0];flowAskQty=askQty[0];
        }
        else {flowKnownFrom=null;ofi.Clear();}
        flowBookReady=bboValid;flowBookAt=at;
        bool valid=bid.Length>=5&&ask.Length>=5&&bidQty.Length>=5&&askQty.Length>=5&&bid.Take(5).All(p=>p>0)&&ask.Take(5).All(p=>p>0)
            &&bidQty.Take(5).All(q=>q>=0)&&askQty.Take(5).All(q=>q>=0)&&bid[0]<ask[0]
            &&Enumerable.Range(1,4).All(i=>bid[i]<bid[i-1]&&ask[i]>ask[i-1])&&bidQty.Take(5).Sum()>=5&&askQty.Take(5).Sum()>=5&&bidQty[0]+askQty[0]>0;
        // Invalid deep shape is rejected for shape features, independently from BBO flow.
        BookReady=valid;
        bookAt=at;
        if(valid){bids=bid.Take(5).ToArray();asks=ask.Take(5).ToArray();bq=bidQty.Take(5).ToArray();aq=askQty.Take(5).ToArray();}
    }
    public void OnTrade(DateTimeOffset at,decimal price,int quantity)
    {
        EventAt(at);if(price<=0||quantity<=0)return;
        bool fresh=flowBookReady&&flowBookAt.HasValue&&(at-flowBookAt.Value).TotalSeconds<=2;
        int sign=fresh&&price>=flowAsk?1:fresh&&price<=flowBid?-1:lastTrade>0?Math.Sign(price-lastTrade):0;
        if(sign==0)sign=lastSign; // tick-rule carry is an estimate, never an exchange aggressor flag.
        if(sign!=0){run=sign==lastSign?run+1:1;lastSign=sign;}
        lastTrade=price;trades.Enqueue((at,quantity,sign));
        Trim(at);
    }
    void Trim(DateTimeOffset at)
    {
        while(ofi.Count>0&&ofi.Peek().At<=at.AddSeconds(-5))ofi.Dequeue();
        while(trades.Count>0&&trades.Peek().At<=at.AddSeconds(-30))trades.Dequeue();
        while(mids.Count>1&&mids.ElementAt(1).At<=at.AddSeconds(-30))mids.Dequeue();
    }
    static double Correlation(double[] values)
    {
        if(values.Length<4)return 0;
        var x=values[..^1];var y=values[1..];double mx=x.Average(),my=y.Average();
        double xx=x.Sum(v=>(v-mx)*(v-mx)),yy=y.Sum(v=>(v-my)*(v-my));
        return xx>1e-12&&yy>1e-12?x.Zip(y).Sum(p=>(p.First-mx)*(p.Second-my))/Math.Sqrt(xx*yy):0;
    }
    public double[] Snapshot(DateTimeOffset at)
    {
        if(lastEvent.HasValue&&lastEvent>=at||lastSnapshot.HasValue&&lastSnapshot>at)throw new ArgumentException("Snapshot must follow all known events");
        Trim(at);var result=new double[20];
        bool fresh=BookReady&&bookAt.HasValue&&(at-bookAt.Value).TotalSeconds<=2;
        if(fresh)
        {
            double mid=(double)(bids[0]+asks[0])/2,bd=bq.Sum(),ad=aq.Sum();
            var bx=bids.Select(p=>(mid-(double)p)/tick).ToArray();var ax=asks.Select(p=>((double)p-mid)/tick).ToArray();
            double bc=bx.Zip(bq).Sum(p=>p.First*p.Second)/bd,ac=ax.Zip(aq).Sum(p=>p.First*p.Second)/ad;
            double Sweep(decimal[] prices,int[] quantities,bool buy){double cost=0,left=5;for(int i=0;i<5&&left>0;i++){double n=Math.Min(left,quantities[i]);cost+=n*(buy?((double)prices[i]-mid):(mid-(double)prices[i]))/tick;left-=n;}return cost/5;}
            double buy=Sweep(asks,aq,true),sell=Sweep(bids,bq,false),bg=(double)(bids[0]-bids[1])/tick,ag=(double)(asks[1]-asks[0])/tick;
            result[0]=Math.Log(1+bd+ad);
            result[1]=((double)asks[0]*bq[0]+(double)bids[0]*aq[0])/(bq[0]+aq[0])/tick-mid/tick;
            result[2]=(ag-bg)/(ag+bg);result[3]=(sell-buy)/(sell+buy);result[4]=(ac-bc)/(ac+bc);
            result[5]=Math.Sqrt((bx.Zip(bq).Sum(p=>p.Second*Math.Pow(p.First-bc,2))+ax.Zip(aq).Sum(p=>p.Second*Math.Pow(p.First-ac,2)))/(bd+ad));
            result[6]=(Array.IndexOf(aq,aq.Max())-Array.IndexOf(bq,bq.Max()))/4d;
            double Curve(int[] q)=>(q[0]-2d*q[1]+2d*q[2]-2d*q[3]+q[4])/q.Sum();
            result[7]=Curve(bq)-Curve(aq);
            if(lastSnapshot!=at)mids.Enqueue((at,mid));
        }
        var recent=trades.Where(t=>t.At>at.AddSeconds(-5)).ToArray();double qty=recent.Sum(t=>t.Qty);
        result[8]=ofi.Sum(t=>t.Value);result[9]=qty>0?recent.Sum(t=>t.Qty*t.Sign)/qty:0;
        result[10]=Math.Log(1+recent.Length/5d);result[11]=Correlation(trades.Select(t=>(double)t.Sign).ToArray());
        result[12]=trades.Count>0?lastSign*Math.Min(run,100):0;
        double total=trades.Sum(t=>t.Qty);result[13]=total>0?trades.Sum(t=>t.Qty*t.Qty)/(total*total):0;
        var path=mids.Where(p=>p.At>=at.AddSeconds(-30)).ToArray();
        FlowReady=flowBookReady&&flowKnownFrom.HasValue&&(at-flowKnownFrom.Value).TotalSeconds>=5
            &&flowBookAt.HasValue&&(at-flowBookAt.Value).TotalSeconds<=2;
        BookPathReady=fresh&&path.Length==11&&path[0].At==at.AddSeconds(-30)
            &&path.Zip(path.Skip(1)).All(p=>(p.Second.At-p.First.At).TotalSeconds==3);
        SnapshotReady=BookPathReady&&FlowReady;
        // Avoid bridging invalid/missing decision snapshots in realized path statistics.
        if(fresh&&path.Length>=2&&path[^1].At==at&&path.Zip(path.Skip(1)).All(p=>(p.Second.At-p.First.At).TotalSeconds==3))
        {
            var anchor=path.LastOrDefault(p=>p.At<=at.AddSeconds(-15));
            if(anchor.At!=default)result[14]=(path[^1].Mid-anchor.Mid)/tick;
            var returns=path.Zip(path.Skip(1)).Select(p=>(p.Second.Mid-p.First.Mid)/tick).ToArray();
            double variation=returns.Sum(r=>r*r),length=returns.Sum(Math.Abs);
            result[15]=length>0?(path[^1].Mid-path[0].Mid)/tick/length:0;
            result[16]=Math.Log(1+variation);result[17]=variation>0?returns.Where(r=>r<0).Sum(r=>r*r)/variation:0;
            result[18]=Correlation(returns);result[19]=Correlation(returns.Select(Math.Abs).ToArray());
        }
        lastSnapshot=at;snapshotCurrent=true;return result;
    }
}
