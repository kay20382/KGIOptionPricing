using System;
using System.Collections.Generic;
using System.Linq;
using KGIOptionPricing.OrderManagement.Core;

namespace KGIOptionPricing.StrategyFramework.Performance;

/// <summary>
/// Shared paper-trading ledger. Strategies receive this object only through the read-only
/// IStrategyPerformanceView interface; executions are applied by StrategyInstanceRunner.
/// </summary>
public sealed class PaperPortfolioLedger : IStrategyPerformanceView
{
    private sealed class Position
    {
        public int Quantity;
        public decimal AveragePrice;
        public decimal MarkPrice;
        public decimal Realized;
        public decimal Costs;
        public decimal Multiplier = 1m;
        public int CompletedTrades;
    }

    private readonly object _sync = new();
    private readonly Dictionary<(string Instance, long Generation, string Symbol), Position> _positions = new();
    private readonly HashSet<string> _processedExecutionIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _executionWatermarks = new(StringComparer.Ordinal);

    public void RegisterProduct(string instanceId, ResolvedProduct product)
        => RegisterProduct(instanceId, 0, product);

    public void RegisterProduct(string instanceId, long strategyRunGeneration, ResolvedProduct product)
    {
        lock (_sync)
        {
            Position position = GetOrCreate(instanceId, strategyRunGeneration, product.Symbol);
            position.Multiplier = product.ContractMultiplier > 0m ? product.ContractMultiplier : 1m;
        }
    }

    public void ApplyExecution(ExecutionEvent execution)
    {
        lock (_sync)
        {
            string fillKey = execution.ExecutionId;
            string watermarkKey = $"{execution.StrategyInstanceId}|{execution.StrategyRunGeneration}|{execution.ClientOrderId}";
            if (!string.IsNullOrWhiteSpace(fillKey))
            {
                if (!_processedExecutionIds.Add($"{watermarkKey}|exec:{fillKey}")) return;
            }
            else if (execution.CumulativeFilledQuantity > 0)
            {
                int previous = _executionWatermarks.GetValueOrDefault(watermarkKey);
                if (execution.CumulativeFilledQuantity <= previous) return;
                int accepted = execution.CumulativeFilledQuantity - previous;
                _executionWatermarks[watermarkKey] = execution.CumulativeFilledQuantity;
                if (accepted < execution.Quantity)
                    execution = execution with { Quantity = accepted, Commission = execution.Commission * accepted / Math.Max(1, execution.Quantity) };
            }
            else if (execution.Sequence > 0)
            {
                if (!_processedExecutionIds.Add($"{watermarkKey}|seq:{execution.Sequence}")) return;
            }
            Position position = GetOrCreate(execution.StrategyInstanceId, execution.StrategyRunGeneration, execution.Symbol);
            int signed = execution.Side == BuySell.Buy ? execution.Quantity : -execution.Quantity;
            int oldQuantity = position.Quantity;

            if (oldQuantity == 0 || Math.Sign(oldQuantity) == Math.Sign(signed))
            {
                int newQuantity = oldQuantity + signed;
                decimal oldNotional = position.AveragePrice * Math.Abs(oldQuantity);
                decimal newNotional = execution.Price * Math.Abs(signed);
                position.Quantity = newQuantity;
                position.AveragePrice = newQuantity == 0 ? 0m : (oldNotional + newNotional) / Math.Abs(newQuantity);
            }
            else
            {
                int closingQuantity = Math.Min(Math.Abs(oldQuantity), Math.Abs(signed));
                decimal points = oldQuantity > 0
                    ? execution.Price - position.AveragePrice
                    : position.AveragePrice - execution.Price;
                position.Realized += points * closingQuantity * position.Multiplier;
                int newQuantity = oldQuantity + signed;
                position.Quantity = newQuantity;
                if (newQuantity == 0 || Math.Sign(newQuantity) != Math.Sign(oldQuantity))
                    position.CompletedTrades++;
                if (newQuantity == 0)
                    position.AveragePrice = 0m;
                else if (Math.Sign(newQuantity) != Math.Sign(oldQuantity))
                    position.AveragePrice = execution.Price;
            }

            position.Costs += execution.Commission;
            position.MarkPrice = execution.Price;
        }
    }

    public void ApplyMark(string symbol, decimal price)
    {
        if (price <= 0m) return;
        lock (_sync)
        {
            foreach (KeyValuePair<(string Instance, long Generation, string Symbol), Position> pair in _positions)
                if (string.Equals(pair.Key.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
                    pair.Value.MarkPrice = price;
        }
    }

    public PositionSnapshot GetPosition(string strategyInstanceId, string symbol)
    {
        lock (_sync)
        {
            var match = _positions.Where(x => string.Equals(x.Key.Instance, strategyInstanceId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(x.Key.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.Key.Generation).FirstOrDefault();
            if (match.Value is not Position position)
                return new PositionSnapshot { StrategyInstanceId = strategyInstanceId, Symbol = symbol };
            return Snapshot(strategyInstanceId, match.Key.Generation, symbol, position);
        }
    }

    public PositionSnapshot GetPosition(string strategyInstanceId, long strategyRunGeneration, string symbol)
    {
        lock (_sync)
            return _positions.TryGetValue((strategyInstanceId, strategyRunGeneration, symbol), out Position? position)
                ? Snapshot(strategyInstanceId, strategyRunGeneration, symbol, position)
                : new PositionSnapshot { StrategyInstanceId = strategyInstanceId, StrategyRunGeneration = strategyRunGeneration, Symbol = symbol };
    }

    public int GetStrategyNetQuantity(string symbol)
    {
        lock (_sync) return _positions.Where(x => string.Equals(x.Key.Symbol, symbol, StringComparison.OrdinalIgnoreCase)).Sum(x => x.Value.Quantity);
    }

    public IReadOnlyList<PositionSnapshot> GetAllPositions(string symbol)
    {
        lock (_sync) return _positions.Where(x => string.Equals(x.Key.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
            .Select(x => Snapshot(x.Key.Instance, x.Key.Generation, x.Key.Symbol, x.Value)).ToArray();
    }

    public PaperPortfolioCheckpoint Capture()
    {
        lock (_sync) return new PaperPortfolioCheckpoint(
            _positions.Select(x => new PaperPositionCheckpoint(x.Key.Instance, x.Key.Generation, x.Key.Symbol,
                x.Value.Quantity, x.Value.AveragePrice, x.Value.MarkPrice, x.Value.Realized, x.Value.Costs,
                x.Value.Multiplier, x.Value.CompletedTrades)).ToArray(),
            _processedExecutionIds.ToArray(), new Dictionary<string, int>(_executionWatermarks));
    }

    public void Restore(PaperPortfolioCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        lock (_sync)
        {
            _positions.Clear(); _processedExecutionIds.Clear(); _executionWatermarks.Clear();
            foreach (PaperPositionCheckpoint saved in checkpoint.Positions)
                _positions[(saved.StrategyInstanceId, saved.StrategyRunGeneration, saved.Symbol)] = new Position
                {
                    Quantity = saved.Quantity, AveragePrice = saved.AveragePrice, MarkPrice = saved.MarkPrice,
                    Realized = saved.Realized, Costs = saved.Costs, Multiplier = saved.Multiplier, CompletedTrades = saved.CompletedTrades
                };
            foreach (string id in checkpoint.ProcessedExecutionIds) _processedExecutionIds.Add(id);
            foreach (var item in checkpoint.ExecutionWatermarks) _executionWatermarks[item.Key] = item.Value;
        }
    }

    public IReadOnlyList<PositionSnapshot> GetStrategyPositions(string strategyInstanceId)
    {
        lock (_sync)
        {
            return _positions
                .Where(x => string.Equals(x.Key.Instance, strategyInstanceId, StringComparison.OrdinalIgnoreCase))
                .Select(x => Snapshot(x.Key.Instance, x.Key.Generation, x.Key.Symbol, x.Value))
                .ToArray();
        }
    }

    internal void RemoveFlatInstance(string strategyInstanceId)
        => RemoveFlatInstance(strategyInstanceId, null);

    internal void RemoveFlatInstance(string strategyInstanceId, long strategyRunGeneration)
        => RemoveFlatInstance(strategyInstanceId, (long?)strategyRunGeneration);

    private void RemoveFlatInstance(string strategyInstanceId, long? strategyRunGeneration)
    {
        lock (_sync)
        {
            if (_positions.Any(x => string.Equals(x.Key.Instance, strategyInstanceId, StringComparison.OrdinalIgnoreCase) &&
                                    (!strategyRunGeneration.HasValue || x.Key.Generation == strategyRunGeneration.Value) &&
                                    x.Value.Quantity != 0))
                throw new InvalidOperationException("策略 ledger 尚有部位，禁止移除 incarnation。");
            foreach (var key in _positions.Keys
                         .Where(x => string.Equals(x.Instance, strategyInstanceId, StringComparison.OrdinalIgnoreCase) &&
                                     (!strategyRunGeneration.HasValue || x.Generation == strategyRunGeneration.Value))
                         .ToArray())
                _positions.Remove(key);
        }
    }

    private Position GetOrCreate(string instanceId, long generation, string symbol)
    {
        var key = (instanceId, generation, symbol);
        if (!_positions.TryGetValue(key, out Position? position))
        {
            position = new Position();
            _positions[key] = position;
        }
        return position;
    }

    private static PositionSnapshot Snapshot(string instanceId, long generation, string symbol, Position position)
    {
        decimal unrealized = position.Quantity switch
        {
            > 0 => (position.MarkPrice - position.AveragePrice) * position.Quantity * position.Multiplier,
            < 0 => (position.AveragePrice - position.MarkPrice) * Math.Abs(position.Quantity) * position.Multiplier,
            _ => 0m
        };
        return new PositionSnapshot
        {
            StrategyInstanceId = instanceId,
            StrategyRunGeneration = generation,
            Symbol = symbol,
            NetQuantity = position.Quantity,
            AveragePrice = position.AveragePrice,
            MarkPrice = position.MarkPrice,
            RealizedPnl = position.Realized,
            UnrealizedPnl = unrealized,
            Costs = position.Costs,
            CompletedTrades = position.CompletedTrades
        };
    }
}

public sealed record PaperPositionCheckpoint(string StrategyInstanceId, long StrategyRunGeneration, string Symbol,
    int Quantity, decimal AveragePrice, decimal MarkPrice, decimal Realized, decimal Costs, decimal Multiplier, int CompletedTrades);
public sealed record PaperPortfolioCheckpoint(IReadOnlyList<PaperPositionCheckpoint> Positions,
    IReadOnlyList<string> ProcessedExecutionIds, IReadOnlyDictionary<string, int> ExecutionWatermarks);
