using KGIOptionPricing;
using KGIOptionPricing.OrderManagement.Core;
using KGIOptionPricing.StrategyFramework;
using KGIOptionPricing.StrategyFramework.Performance;

static class PerformanceSummaryTests
{
    public static void Run()
    {
        void Check(bool ok,string reason){if(!ok)throw new Exception(reason);}
        var ledger=new PaperPortfolioLedger();
        ledger.RegisterProduct("summary",new(){Symbol="MICRO",ProductId="225MC",ContractMultiplier=10,TickSize=5});
        var at=new DateTimeOffset(2026,10,8,8,0,0,TimeSpan.FromHours(8));
        ExecutionEvent Fill(string id,BuySell side,decimal price,int qty)=>new(){StrategyInstanceId="summary",Symbol="MICRO",ClientOrderId=id,ExecutionId=id,Timestamp=at,Side=side,Price=price,Quantity=qty,Commission=36*qty};
        var empty=ledger.GetPosition("summary","MICRO");
        Check(empty.TotalTradedQuantity==0 && empty.ExpectedPnlPerTrade==null,"empty performance has no expectancy");
        var entry=Fill("entry",BuySell.Buy,100,2);ledger.ApplyExecution(entry);ledger.ApplyExecution(entry);
        ledger.ApplyMark("MICRO",120);
        var open=ledger.GetPosition("summary","MICRO");
        Check(open.TotalTradedQuantity==2 && open.GrossPnl==400 && open.ExpectedPnlPerTrade==null,"deduplicated open fills count quantity; floating profit is excluded from expectancy");
        ledger.ApplyExecution(Fill("partial",BuySell.Sell,110,1));
        Check(ledger.GetPosition("summary","MICRO").ExpectedPnlPerTrade==null,"partial close is not a completed roundtrip");
        ledger.ApplyExecution(Fill("close",BuySell.Sell,90,1));
        var closed=ledger.GetPosition("summary","MICRO");
        Check(closed.TotalTradedQuantity==4 && closed.CompletedTrades==1 && closed.GrossPnl==0 && closed.ExpectedPnlPerTrade==-144,"multi-lot completed expectancy uses both-side costs");
        ledger.ApplyExecution(Fill("short",BuySell.Sell,100,1));ledger.ApplyExecution(Fill("cover",BuySell.Buy,80,1));
        ledger.ApplyExecution(Fill("new-open",BuySell.Buy,100,1));ledger.ApplyMark("MICRO",1000);
        var current=ledger.GetPosition("summary","MICRO");
        Check(current.TotalTradedQuantity==7 && current.CompletedTrades==2 && current.GrossPnl==9200 && current.ExpectedPnlPerTrade==-8,"completed monetary expectancy ignores new open profit and its entry cost");
        using var panel=new PowerReversalPanel();
        panel.ApplySnapshot(new(){InstanceId="summary",TotalTradedQuantity=current.TotalTradedQuantity,CompletedTrades=current.CompletedTrades,GrossPnl=current.GrossPnl,ExpectedPnlPerTrade=current.ExpectedPnlPerTrade});
        var row=panel.Instances.Rows[0];
        Check(Convert.ToInt32(row.Cells["TotalTradedQuantity"].Value)==7 && Convert.ToInt32(row.Cells["CompletedTrades"].Value)==2 && Convert.ToDecimal(row.Cells["GrossPnl"].Value)==9200 && Equals(row.Cells["ExpectedPnlPerTrade"].Value,"-8.00"),"shared snapshot performance is rendered by the simulation/live panel");
        panel.ApplySnapshot(new(){InstanceId="empty"});
        Check(Equals(panel.Instances.Rows[1].Cells["ExpectedPnlPerTrade"].Value,"--"),"UI shows unavailable expectancy before first roundtrip");
        Console.WriteLine("PASS performance quantity, gross, completed expectancy and shared UI");
    }
}
