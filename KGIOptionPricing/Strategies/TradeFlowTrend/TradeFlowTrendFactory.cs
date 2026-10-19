using KGIOptionPricing.StrategyFramework;
namespace KGIOptionPricing.Strategies.TradeFlowTrend;
public sealed class TradeFlowTrendFactory:IStrategyFactory
{
    public const string TypeName="TradeFlowTrend";
    public string StrategyType=>TypeName;
    public string DisplayName=>"內外盤順勢交易";
    public ISimulationStrategy Create(StrategyDefinition definition)=>new TradeFlowTrendStrategy(definition);
}
