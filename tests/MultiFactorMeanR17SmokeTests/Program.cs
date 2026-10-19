using KGIOptionPricing.StrategyFramework;
using System.Reflection;
using KGIOptionPricing;
using KGIOptionPricing.Strategies.MultiFactorMeanR17;

class Program
{
    static void Check(bool ok,string reason){if(!ok)throw new Exception(reason);}
    [STAThread]
    static int Main()
    {
        try
        {
            var assembly=typeof(StrategyHost).Assembly;
            var type=assembly.GetType("KGIOptionPricing.Strategies.MultiFactorMeanR17.MultiFactorMeanR17Factory");
            Check(type!=null,"主程式缺少多因子均價回歸 R17 工廠");
            var factory=(IStrategyFactory)Activator.CreateInstance(type!)!;
            Check(factory.StrategyType=="MultiFactorMeanR17","策略識別必須獨立於既有PowerReversal");
            var strategy=factory.Create(new(){StrategyType=factory.StrategyType,InstanceId="r17-smoke"});
            Check(strategy.State==StrategyLifecycleState.Created,"建立策略不得自動啟動或送單");
            var snapshot=strategy.GetSnapshot();
            foreach(var parameter in new[]{"ResearchScoreThreshold=0.25","ResearchMinSignedMiniLastReturnTicks=-0.5","ResearchAnchorRiskToFill=1","ResearchVolatilityMaxStopTicks=30","TrailingActivateRisk=0.8"})
                Check(snapshot.ParameterSummary.Contains(parameter),"快照須揭露生效參數："+parameter);
            foreach(var service in new[]{typeof(StrategySimulationService),typeof(LiveStrategyService)})
            {
                var factories=(IEnumerable<IStrategyFactory>)service.GetMethod("CreateDefaultFactories",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,null)!;
                Check(factories.Count(f=>f.StrategyType==factory.StrategyType)==1,"模擬與實盤需各註冊一次新策略");
                Check(factories.Any(f=>f.StrategyType=="PowerReversal"),"原買賣力道策略必須保留");
            }
            FactorTests.Run();
            HostIntegrationTests.Run();
            using var simulatedForm=new StrategyCenterForm();
            using var liveForm=new LiveStrategyCenterForm();
            IEnumerable<Control> Descendants(Control parent)=>parent.Controls.Cast<Control>().SelectMany(c=>new[]{c}.Concat(Descendants(c)));
            foreach(var form in new Form[]{simulatedForm,liveForm})
            {
                var panels=Descendants(form).OfType<MultiFactorMeanR17Panel>().ToArray();
                Check(panels.Length==1,"模擬／實盤表單各有一個新策略頁籤");
                var panel=panels[0];var definition=panel.CreateDefinition();
                Check(definition.StrategyType=="MultiFactorMeanR17" && definition.Parameters["ResearchScoreThreshold"]=="0.25","UI不可漏掉研究參數");
                Check(!panel.Confirm.Checked,"實盤確認不得預先勾選");
                foreach(var key in new[]{"CompletedTrades","TotalTradedQuantity","GrossPnl","ExpectedPnlPerTrade","WinRate"})Check(panel.Instances.Columns.Contains(key),"共用績效欄位缺少："+key);
            }
            Console.WriteLine("PASS R17 factory/defaults/registration/UI");return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
