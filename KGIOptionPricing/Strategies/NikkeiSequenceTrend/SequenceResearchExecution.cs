using KGIOptionPricing.StrategyFramework;
using KGIOptionPricing.StrategyFramework.Execution;
using KGIOptionPricing.StrategyFramework.Performance;
using KGIOptionPricing.OrderManagement.Core;

namespace KGIOptionPricing.Strategies.NikkeiSequenceTrend;

/// <summary>Offline coordinator only; all fills and P&L belong to the common engine.</summary>
public sealed class SequenceResearchExecution
{
    sealed class Clock:IStrategyClock { public DateTimeOffset Now {get;set;} }
    readonly Clock clock=new();readonly PaperPortfolioLedger ledger=new();
    readonly CrossPricePaperExecutionService execution;
    readonly string symbol;readonly decimal sideCost,stopPoints;readonly bool netForecast;readonly double minimumNetTicks,exitThreshold;readonly int extraCostTicks,maximumHoldSeconds;DateTimeOffset? enteredAt;
    const string Instance="SequenceResearch";
    PaperOrderRequest? delayed;DateTimeOffset? ready,pendingAt,cooldown;
    decimal latestBid,latestAsk;
    decimal closedNet,closedCosts,positiveNet,negativeNet,minNet,peak,maxDrawdown;
    int wins,completed,weak,serial;bool dailyStopped;
    PositionSnapshot P=>ledger.GetPosition(Instance,symbol);
    public int Position=>P.NetQuantity;
    public DateTimeOffset? EntryTimestamp=>enteredAt;
    public SequenceResearchExecution(string symbol,int extraCostTicks,decimal stopPoints=150,bool netForecast=false,double minimumNetTicks=1,double exitThreshold=.5,int maximumHoldSeconds=0)
    {
        if(stopPoints<=0||minimumNetTicks<0||exitThreshold<0||exitThreshold>1||maximumHoldSeconds<0)throw new ArgumentException("Invalid research parameters");
        this.symbol=symbol;this.stopPoints=stopPoints;this.netForecast=netForecast;this.minimumNetTicks=minimumNetTicks;this.exitThreshold=exitThreshold;this.extraCostTicks=extraCostTicks;this.maximumHoldSeconds=maximumHoldSeconds;sideCost=36+50*extraCostTicks;
        ledger.RegisterProduct(Instance,new ResolvedProduct{ProductId="225MC",Symbol=symbol,TickSize=5,ContractMultiplier=10});
        execution=new(sideCost,clock);
        execution.Filled+=fill=>{
            ledger.ApplyExecution(fill);ObserveBookRisk();
            if(fill.Role is PaperOrderRole.EntryLong or PaperOrderRole.EntryShort)
            {
                pendingAt=null;enteredAt=fill.Timestamp;var side=P.NetQuantity;
                Submit(side>0?BuySell.Sell:BuySell.Buy,PaperOrderType.Stop,PaperOrderRole.StopLoss,fill.Price-side*stopPoints,false);
            }
            else if(P.NetQuantity==0)
            {
                var delta=P.NetPnl-closedNet;closedNet=P.NetPnl;closedCosts=P.Costs;completed++;
                if(delta>0){wins++;positiveNet+=delta;}else if(delta<0)negativeNet+=delta;
                execution.CancelAll(Instance);delayed=null;ready=null;
                cooldown=fill.Timestamp.AddSeconds(10);weak=0;enteredAt=null;
            }
            Observe();
        };
    }
    void Observe()
    {
        minNet=Math.Min(minNet,P.NetPnl);peak=Math.Max(peak,P.NetPnl);maxDrawdown=Math.Max(maxDrawdown,peak-P.NetPnl);
        if(P.NetPnl<=-7500)dailyStopped=true;
    }
    void ObserveBookRisk()
    {
        if(Position!=0&&latestBid>0&&latestAsk>latestBid)ledger.ApplyMark(symbol,Position>0?latestBid:latestAsk);
        Observe();
    }
    void Submit(BuySell side,PaperOrderType type,PaperOrderRole role,decimal price,bool latency)
    {
        var request=new PaperOrderRequest{StrategyInstanceId=Instance,ClientOrderId=$"SEQ-{++serial}",Symbol=symbol,Side=side,Type=type,Role=role,Price=price,Quantity=1};
        if(latency){delayed=request;ready=clock.Now.AddMilliseconds(1);}else execution.Submit(request,clock.Now);
    }
    bool HasExit=>delayed?.Role==PaperOrderRole.Flatten||execution.GetWorkingOrders(Instance).Any(o=>o.Role==PaperOrderRole.Flatten);
    void CancelEntries()
    {
        if(delayed?.Role is PaperOrderRole.EntryLong or PaperOrderRole.EntryShort){delayed=null;ready=null;}
        foreach(var o in execution.GetWorkingOrders(Instance).Where(o=>o.Role is PaperOrderRole.EntryLong or PaperOrderRole.EntryShort).ToArray())execution.Cancel(o.OrderId);
        pendingAt=null;
    }
    void Flatten()
    {
        CancelEntries();if(Position==0||HasExit)return;
        foreach(var o in execution.GetWorkingOrders(Instance).ToArray())execution.Cancel(o.OrderId);
        Submit(Position>0?BuySell.Sell:BuySell.Buy,PaperOrderType.Market,PaperOrderRole.Flatten,P.MarkPrice,true);
    }
    void AdvanceClock(DateTimeOffset at)
    {
        if(maximumHoldSeconds>0&&Position!=0&&enteredAt.HasValue&&!HasExit)
        {
            var deadline=enteredAt.Value.AddSeconds(maximumHoldSeconds);
            if(at>=deadline){clock.Now=deadline;Flatten();}
        }
        clock.Now=at;
    }
    public void Trade(DateTimeOffset at,decimal price,long sequence)
    {
        AdvanceClock(at);
        if(delayed!=null&&at>=ready){var request=delayed;var submitted=ready!.Value;delayed=null;ready=null;execution.Submit(request,submitted);}
        ledger.ApplyMark(symbol,price);Observe();ObserveBookRisk();
        if(dailyStopped)Flatten();
        execution.OnMarketEvent(new MarketEvent{Timestamp=at,Sequence=sequence,Symbol=symbol,ProductId="225MC",Kind=MarketEventKind.Trade,LastPrice=price,TradeQuantity=1});
        ledger.ApplyMark(symbol,price);Observe();ObserveBookRisk();
        if(dailyStopped)Flatten();
    }
    public void Book(DateTimeOffset at,decimal bid,decimal ask)
    {
        AdvanceClock(at);
        if(bid<=0||ask<=bid)return;
        // Risk uses the liquidation side, independently of the entry spread filter.
        latestBid=bid;latestAsk=ask;ledger.ApplyMark(symbol,Position>0?bid:Position<0?ask:(bid+ask)/2);Observe();
        if(dailyStopped)Flatten();
    }
    public void Decide(DateTimeOffset at,decimal bid,decimal ask,bool valid,double[] probabilities,double delta,double trend60,double trend300,double threshold,bool trendFilter)
    {
        Book(at,bid,ask);
        var local=at.ToOffset(TimeSpan.FromHours(8));var close=local.TimeOfDay>=TimeSpan.FromHours(16)?new DateTimeOffset(local.Date.AddDays(1).AddHours(5),local.Offset):local.TimeOfDay<TimeSpan.FromHours(5)?new DateTimeOffset(local.Date.AddHours(5),local.Offset):new DateTimeOffset(local.Date.AddHours(14.75),local.Offset);
        if(dailyStopped||!valid||close-at<=TimeSpan.FromSeconds(310)){Flatten();return;}
        if(pendingAt.HasValue&&at-pendingAt>=TimeSpan.FromSeconds(10))CancelEntries();
        int direction=probabilities[2]>=threshold&&delta>0?1:probabilities[0]>=threshold&&delta<0?-1:0;
        if(trendFilter&&(direction*trend60<=0||direction*trend300<=0))direction=0;
        if(Position!=0)
        {
            int pos=Math.Sign(Position);double same=pos>0?probabilities[2]:probabilities[0];
            weak=same<exitThreshold?weak+1:0;
            if(pos*delta<0||weak>=2||maximumHoldSeconds>0&&enteredAt.HasValue&&at-enteredAt.Value>=TimeSpan.FromSeconds(maximumHoldSeconds))Flatten();return;
        }
        if(direction==0){CancelEntries();return;}
        if(delayed!=null||execution.GetWorkingOrders(Instance).Count>0||cooldown.HasValue&&at<cooldown)return;
        if(bid<=0||ask<=bid||ask-bid>10||P.NetPnl-stopPoints*10-2*sideCost<=-7500)return;
        if(netForecast?Math.Abs(delta)-2*extraCostTicks<=minimumNetTicks:Math.Abs(delta)<=(double)((ask-bid)/5+2*sideCost/50+1))return;
        pendingAt=at;
        Submit(direction>0?BuySell.Buy:BuySell.Sell,PaperOrderType.Limit,direction>0?PaperOrderRole.EntryLong:PaperOrderRole.EntryShort,direction>0?bid:ask,true);
    }
    public void ForceExit(DateTimeOffset at){clock.Now=at;Flatten();}
    public void CancelUnfilledEntry(DateTimeOffset at){clock.Now=at;CancelEntries();}
    public object GetResult()=>Result();
    public ResultRow Result()=>new(completed,wins,closedNet,positiveNet,negativeNet,closedCosts,Math.Max(0,-minNet),maxDrawdown,Position,P.NetPnl-closedNet,dailyStopped);
    public sealed record ResultRow(int completedTrades,int winningTrades,decimal netPnl,decimal winningNet,decimal losingNet,decimal executionCosts,decimal maximumIntradayLoss,decimal maximumDrawdown,int openQuantity,decimal excludedOpenNetPnl,bool dailyStopped)
    {
        public decimal? winRate=>completedTrades>0?100m*winningTrades/completedTrades:null;
        public decimal? netProfitFactor=>losingNet<0?winningNet/-losingNet:null;
        public decimal grossPnl=>netPnl+executionCosts;
    }
}
