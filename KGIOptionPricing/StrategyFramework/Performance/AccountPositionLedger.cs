using System;
using System.Collections.Generic;
using System.Linq;
using KGIOptionPricing.OrderManagement.Core;

namespace KGIOptionPricing.StrategyFramework.Performance;

public sealed record BrokerPositionRow(string Account, string Symbol, int NetQuantity, decimal MarkPrice = 0m);
public sealed record AccountFill(string Account, string Symbol, string ExecutionId, BuySell Side, int Quantity);
public sealed record AccountProductSnapshot(string Account, string Symbol, int BrokerNetQuantity,
    int StrategyNetQuantity, int ManualExternalNetQuantity, int DifferenceQuantity, decimal MarkPrice);

public interface IAccountPositionLedger
{
    void ReplaceBrokerSnapshot(string account, IReadOnlyList<BrokerPositionRow> positions);
    void ApplyManualExternalFill(AccountFill fill);
    AccountProductSnapshot Reconcile(string account, string symbol, IReadOnlyList<PositionSnapshot> strategies);
}

/// <summary>Account-level reconciliation only; it never assigns broker fills to a strategy.</summary>
public sealed class AccountPositionLedger : IAccountPositionLedger
{
    private readonly object _sync = new();
    private readonly Dictionary<(string Account, string Symbol), (int Quantity, decimal Mark)> _broker = new();
    private readonly Dictionary<(string Account, string Symbol), int> _manual = new();
    private readonly HashSet<string> _manualExecutionIds = new(StringComparer.Ordinal);

    public void ReplaceBrokerSnapshot(string account, IReadOnlyList<BrokerPositionRow> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);
        lock (_sync)
        {
            foreach (var key in _broker.Keys.Where(x => Eq(x.Account, account)).ToArray()) _broker.Remove(key);
            foreach (BrokerPositionRow row in positions)
            {
                if (!Eq(row.Account, account)) throw new ArgumentException("券商持倉快照帳號不一致。", nameof(positions));
                _broker[(row.Account, row.Symbol)] = (row.NetQuantity, row.MarkPrice);
            }
        }
    }

    public void ApplyManualExternalFill(AccountFill fill)
    {
        if (string.IsNullOrWhiteSpace(fill.Account) || string.IsNullOrWhiteSpace(fill.Symbol) ||
            string.IsNullOrWhiteSpace(fill.ExecutionId) || fill.Quantity <= 0)
            throw new ArgumentException("人工／外部成交必須具備帳號、商品、成交識別與正數口數。", nameof(fill));
        lock (_sync)
        {
            if (!_manualExecutionIds.Add(fill.ExecutionId)) return;
            var key = (fill.Account, fill.Symbol);
            _manual[key] = _manual.GetValueOrDefault(key) + (fill.Side == BuySell.Buy ? fill.Quantity : -fill.Quantity);
        }
    }

    public AccountProductSnapshot Reconcile(string account, string symbol, IReadOnlyList<PositionSnapshot> strategies)
    {
        ArgumentNullException.ThrowIfNull(strategies);
        lock (_sync)
        {
            var key = (account, symbol);
            int strategyNet = strategies.Where(x => Eq(x.Symbol, symbol)).Sum(x => x.NetQuantity);
            int brokerNet = _broker.GetValueOrDefault(key).Quantity;
            int manualNet = _manual.GetValueOrDefault(key);
            decimal mark = _broker.GetValueOrDefault(key).Mark;
            return new AccountProductSnapshot(account, symbol, brokerNet, strategyNet, manualNet,
                brokerNet - strategyNet - manualNet, mark);
        }
    }

    private static bool Eq(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}

public static class AccountPositionEffectResolver
{
    public static PositionEffect Resolve(int accountNetQuantity, BuySell side, int orderQuantity)
    {
        if (orderQuantity <= 0) throw new ArgumentOutOfRangeException(nameof(orderQuantity));
        int signed = side == BuySell.Buy ? orderQuantity : -orderQuantity;
        if (accountNetQuantity == 0 || Math.Sign(accountNetQuantity) == Math.Sign(signed)) return PositionEffect.Open;
        if (orderQuantity <= Math.Abs(accountNetQuantity)) return PositionEffect.Close;
        throw new InvalidOperationException("委託同時跨越平倉與新倉，必須拆成兩筆券商委託。");
    }
}
