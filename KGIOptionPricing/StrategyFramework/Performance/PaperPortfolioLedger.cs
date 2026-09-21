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
    }

    private readonly object _sync = new();
    private readonly Dictionary<(string Instance, string Symbol), Position> _positions = new();

    public void RegisterProduct(string instanceId, ResolvedProduct product)
    {
        lock (_sync)
        {
            Position position = GetOrCreate(instanceId, product.Symbol);
            position.Multiplier = product.ContractMultiplier > 0m ? product.ContractMultiplier : 1m;
        }
    }

    public void ApplyExecution(ExecutionEvent execution)
    {
        lock (_sync)
        {
            Position position = GetOrCreate(execution.StrategyInstanceId, execution.Symbol);
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
            foreach (KeyValuePair<(string Instance, string Symbol), Position> pair in _positions)
                if (string.Equals(pair.Key.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
                    pair.Value.MarkPrice = price;
        }
    }

    public PositionSnapshot GetPosition(string strategyInstanceId, string symbol)
    {
        lock (_sync)
        {
            if (!_positions.TryGetValue((strategyInstanceId, symbol), out Position? position))
                return new PositionSnapshot { StrategyInstanceId = strategyInstanceId, Symbol = symbol };
            return Snapshot(strategyInstanceId, symbol, position);
        }
    }

    public IReadOnlyList<PositionSnapshot> GetStrategyPositions(string strategyInstanceId)
    {
        lock (_sync)
        {
            return _positions
                .Where(x => string.Equals(x.Key.Instance, strategyInstanceId, StringComparison.OrdinalIgnoreCase))
                .Select(x => Snapshot(x.Key.Instance, x.Key.Symbol, x.Value))
                .ToArray();
        }
    }

    internal void RemoveFlatInstance(string strategyInstanceId)
    {
        lock (_sync)
        {
            if (_positions.Any(x => string.Equals(x.Key.Instance, strategyInstanceId, StringComparison.OrdinalIgnoreCase) &&
                                    x.Value.Quantity != 0))
                throw new InvalidOperationException("策略 ledger 尚有部位，禁止移除 incarnation。");
            foreach (var key in _positions.Keys
                         .Where(x => string.Equals(x.Instance, strategyInstanceId, StringComparison.OrdinalIgnoreCase))
                         .ToArray())
                _positions.Remove(key);
        }
    }

    private Position GetOrCreate(string instanceId, string symbol)
    {
        var key = (instanceId, symbol);
        if (!_positions.TryGetValue(key, out Position? position))
        {
            position = new Position();
            _positions[key] = position;
        }
        return position;
    }

    private static PositionSnapshot Snapshot(string instanceId, string symbol, Position position)
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
            Symbol = symbol,
            NetQuantity = position.Quantity,
            AveragePrice = position.AveragePrice,
            MarkPrice = position.MarkPrice,
            RealizedPnl = position.Realized,
            UnrealizedPnl = unrealized,
            Costs = position.Costs
        };
    }
}
