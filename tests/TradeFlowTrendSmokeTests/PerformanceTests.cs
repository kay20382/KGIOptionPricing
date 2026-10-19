using KGIOptionPricing.StrategyFramework;
using KGIOptionPricing.StrategyFramework.Performance;
using KGIOptionPricing.OrderManagement.Core;
using static TradeFlowTrendSmokeTests.TestSupport;
namespace TradeFlowTrendSmokeTests;
internal static class PerformanceTests
{
    public static void Run()
    {
        var ledger=new PaperPortfolioLedger();ledger.RegisterProduct("tf",new(){Symbol="MC",ContractMultiplier=10,TickSize=5});long n=0;
        void Fill(BuySell side,decimal price)=>ledger.ApplyExecution(new(){StrategyInstanceId="tf",Symbol="MC",Side=side,Price=price,Quantity=1,Commission=2,Sequence=++n});
        Check(ledger.GetPosition("tf","MC").WinRate==null,"no completed trades");
        Fill(BuySell.Buy,100);Fill(BuySell.Sell,110);Fill(BuySell.Sell,110);Fill(BuySell.Buy,120);
        var p=ledger.GetPosition("tf","MC");Check(p.WinRate==50 && p.AverageWinningPoints==9.6m && p.AverageLosingPoints==-10.4m,"net complete trades");
        Check(p.UserDefinedProfitFactor==9.6m/10.4m,"user factor");
    }
}
