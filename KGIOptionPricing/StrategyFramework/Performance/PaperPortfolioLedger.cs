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
        public int TotalTradedQuantity;
        public decimal CompletedNetPnl;
        public decimal CycleGross, CycleCosts;
        public int CycleClosedQuantity;
        public readonly List<decimal> CompletedNetPoints = new();
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
            if(execution.Quantity<=0) throw new ArgumentOutOfRangeException(nameof(execution.Quantity));
            position.TotalTradedQuantity+=execution.Quantity;
            if(oldQuantity==0 || Math.Sign(oldQuantity)==Math.Sign(signed)) position.CycleCosts+=execution.Commission;
            else
            {
                int closed=Math.Min(Math.Abs(oldQuantity),execution.Quantity);
                decimal closingCost=execution.Commission*closed/execution.Quantity;
                position.CycleGross+=(execution.Price-position.AveragePrice)*Math.Sign(oldQuantity)*closed*position.Multiplier;
                position.CycleCosts+=closingCost;position.CycleClosedQuantity+=closed;
                if(execution.Quantity>=Math.Abs(oldQuantity))
                {
                    position.CompletedNetPnl+=position.CycleGross-position.CycleCosts;
                    position.CompletedNetPoints.Add((position.CycleGross-position.CycleCosts)/(position.CycleClosedQuantity*position.Multiplier));
                    position.CycleGross=0;position.CycleClosedQuantity=0;position.CycleCosts=execution.Commission-closingCost;
                }
            }

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

    internal void RemoveRetiredInstance(string strategyInstanceId, long strategyRunGeneration)
    {
        lock (_sync)
        {
            foreach (var key in _positions.Keys
                         .Where(x => string.Equals(x.Instance, strategyInstanceId, StringComparison.OrdinalIgnoreCase) &&
                                     x.Generation == strategyRunGeneration).ToArray())
                _positions.Remove(key);
        }
    }

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
            TotalTradedQuantity = position.TotalTradedQuantity,
            ExpectedPnlPerTrade = position.CompletedTrades==0?null:position.CompletedNetPnl/position.CompletedTrades,
            CompletedTrades = position.CompletedTrades
            ,WinRate = position.CompletedNetPoints.Count==0?null:100m*position.CompletedNetPoints.Count(x=>x>0)/position.CompletedNetPoints.Count
            ,AverageWinningPoints = position.CompletedNetPoints.Where(x=>x>0).Cast<decimal?>().DefaultIfEmpty(null).Average()
            ,AverageLosingPoints = position.CompletedNetPoints.Where(x=>x<0).Cast<decimal?>().DefaultIfEmpty(null).Average()
            ,UserDefinedProfitFactor = position.CompletedNetPoints.Any(x=>x>0) && position.CompletedNetPoints.Any(x=>x<0)
                ? -position.CompletedNetPoints.Where(x=>x>0).Average()/position.CompletedNetPoints.Where(x=>x<0).Average():null
        };
    }
}
