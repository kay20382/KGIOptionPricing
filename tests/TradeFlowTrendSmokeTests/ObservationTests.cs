using KGIOptionPricing.Strategies.TradeFlowTrend;
using KGIOptionPricing.StrategyFramework;
using KGIOptionPricing.StrategyFramework.Storage;
using KGIOptionPricing.StrategyFramework.Execution;
using static TradeFlowTrendSmokeTests.TestSupport;
namespace TradeFlowTrendSmokeTests;
internal static class ObservationTests
{
    public static void Run()
    {
        var dir=Path.Combine(Path.GetTempPath(),"flow-observation-"+Guid.NewGuid().ToString("N"));var t=At("2026-09-25T08:00:30+08:00");
        using(var r=new SimulationRunRecorder(dir))
        {
            var item=new StrategyDecisionObservation{StrategyType="TradeFlowTrend",StrategyInstanceId="tf",Kind=StrategyObservationKind.RiClosed,SampleKey="M:30:1",Timestamp=t,PrimaryReasonCode="RiClosed"};
            r.Record(item);r.Record(item);r.Record(item with{Timestamp=t.AddSeconds(1),SampleKey="M:30:2"});
        }
        Check(File.ReadAllLines(Path.Combine(dir,"strategy-decisions.jsonl")).Length==2,"sample keys not heartbeat compressed");
        var path=Path.Combine(dir,"profiles","one.json");var p=new TradeFlowParameters();
        TradeFlowParameterStore.Save(path,p);Check(TradeFlowParameterStore.Load(path)==p,"profile roundtrip");
        File.WriteAllText(path,File.ReadAllText(path).Replace("\"Quantity\": \"1\"","\"Quantity\": \"2\""));
        bool rejected=false;try{TradeFlowParameterStore.Load(path);}catch(InvalidDataException){rejected=true;}Check(rejected,"profile hash");
        var reg=new LiveOrderOwnershipRegistry(Path.Combine(dir,"ownership"));
        var checkpoint=new LiveStrategyCheckpoint("tf",1,1,"TradeFlowTrend",TradeFlowParameterStore.Hash(p),System.Text.Json.JsonSerializer.SerializeToElement(new{PendingIoc=true}),t);
        reg.UpdateStrategyCheckpoint(checkpoint);
        Check(new LiveOrderOwnershipRegistry(Path.Combine(dir,"ownership")).Snapshot().StrategyCheckpoints.Single().State.GetProperty("PendingIoc").GetBoolean(),"journal recovery");
    }
}
