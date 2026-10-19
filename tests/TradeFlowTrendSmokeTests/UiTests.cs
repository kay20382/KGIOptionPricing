using KGIOptionPricing;
using KGIOptionPricing.StrategyFramework;
using static TradeFlowTrendSmokeTests.TestSupport;
namespace TradeFlowTrendSmokeTests;
internal static class UiTests
{
    public static void Run()
    {
        Exception? failure=null;var thread=new Thread(()=>{try {
            using var panel=new TradeFlowTrendPanel();Check(panel.FlowTables.Count==3,"three separate windows");
            Check(panel.CreateDefinition().Parameters["ProfitTicks"]=="50","editable defaults");
            panel.ApplySnapshot(new(){InstanceId="one",StrategyType="TradeFlowTrend",Features=new Dictionary<string,decimal?>{{"225M.30.Real",12.5m},{"225M.30.UnknownPercent",4m},{"StopTrigger",100m},{"225MC.Sar",110m}}});
            panel.ApplySnapshot(new(){InstanceId="two",StrategyType="TradeFlowTrend",Features=new Dictionary<string,decimal?>{{"225M.30.Real",99m}}});
            panel.SelectInstance("one");Check(panel.FlowTables[0].Rows[3].Cells["225M"].Value?.ToString()=="12.50","instance isolated table");
            Check(panel.FlowTables[1].Rows[3].Cells["225M"].Value?.ToString()=="--","missing value");
            Check(panel.FlowTables[0].Rows.Count==8 && panel.FlowTables[0].Rows[7].Cells["225M"].Value?.ToString()=="4.00","unknown percentage visible");
            Check(panel.Status.Text.Contains("停損=100.00") && panel.Status.Text.Contains("SAR=110.00") && panel.Status.Text.Contains("暫估"),"protection and provisional metrics visible");
            using var sim=new StrategyCenterForm();using var live=new LiveStrategyCenterForm();
            Check(sim.Controls.Find("tradeFlowPanel",true).Length==1 && live.Controls.Find("tradeFlowPanel",true).Length==1,"both Designer tabs");
        }catch(Exception ex){failure=ex;}});thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();if(failure!=null)throw failure;
    }
}
