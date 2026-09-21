using KGIOptionPricing.StrategyFramework;

namespace KGIOptionPricing.Strategies.PeriodicQueueCycle;

public sealed class PeriodicQueueCycleFactory : IStrategyFactory
{
    public const string TypeName = "PeriodicQueueCycle";

    public string StrategyType => TypeName;
    public string DisplayName => "週期雙邊完成單";

    public ISimulationStrategy Create(StrategyDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        PeriodicQueueCycleParameters parameters = PeriodicQueueCycleParameters.Parse(definition.Parameters);
        return new PeriodicQueueCycleStrategy(definition, parameters);
    }
}
