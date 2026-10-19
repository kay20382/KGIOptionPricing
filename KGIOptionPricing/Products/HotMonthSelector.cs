using System.Globalization;

namespace KGIOptionPricing;

public sealed record HotMonthContractDateInput
{
    public string ProductID { get; init; } = string.Empty;
    public string ContractMonth { get; init; } = string.Empty;
    public string FirstNoticeDate { get; init; } = string.Empty;
    public string LastTradeDate { get; init; } = string.Empty;
    public string ExpiryDate { get; init; } = string.Empty;
}

internal enum HotMonthRollKind
{
    LastTradeDate,
    FirstNoticeDate,
    CrudeOil,
    NaturalGas,
    Brent
}

internal sealed record HotMonthProductRule(HashSet<int> ActiveMonths, HotMonthRollKind RollKind);

internal static class HotMonthRuleCatalog
{
    private static readonly int[] Quarterly = { 3, 6, 9, 12 };
    private static readonly int[] Monthly = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };
    private static readonly Dictionary<string, HotMonthProductRule> Rules = BuildRules();

    public static bool TryGet(string productId, out HotMonthProductRule? rule) =>
        Rules.TryGetValue((productId ?? string.Empty).Trim(), out rule);

    private static Dictionary<string, HotMonthProductRule> BuildRules()
    {
        var result = new Dictionary<string, HotMonthProductRule>(StringComparer.OrdinalIgnoreCase);

        Add(result, Quarterly, HotMonthRollKind.LastTradeDate,
            "MES", "ES", "MNQ", "NQ", "MYM", "YM", "M2K", "RTY",
            "DX", "DXY", "FDXM", "FDAX", "FSTX", "FESX",
            "225", "225M", "225MC", "TPX", "TPXM");
        Add(result, Monthly, HotMonthRollKind.LastTradeDate,
            "MHI", "HSI", "HTI", "HSTECH", "TWN", "STW", "CN", "A50", "FCH",
            "MKS2", "MK2", "105", "FUS11", "005930", "FUS50", "000660",
            "MTX", "MXF", "TXF");

        Add(result, Quarterly, HotMonthRollKind.FirstNoticeDate,
            "TU", "ZT", "2YR", "FV", "ZF", "5YR", "TY", "ZN", "10YR",
            "US", "ZB", "30YR", "UB", "UBE", "TN", "YR",
            "6E", "EUR", "EC", "6J", "JPY", "JY", "6B", "GBP", "BP",
            "6A", "AUD", "AD", "6C", "CAD", "CD", "HG", "MHG");
        Add(result, new[] { 2, 4, 6, 8, 12 }, HotMonthRollKind.FirstNoticeDate,
            "MGC", "GC", "1OZ");
        Add(result, new[] { 3, 5, 7, 9, 12 }, HotMonthRollKind.FirstNoticeDate,
            "SIL", "SI", "QI", "KC", "CC");
        Add(result, new[] { 1, 3, 5, 7, 8, 9, 11 }, HotMonthRollKind.FirstNoticeDate,
            "S", "ZS", "YK", "MZS");
        Add(result, new[] { 3, 5, 7, 9, 12 }, HotMonthRollKind.FirstNoticeDate,
            "C", "ZC", "MZC", "W", "ZW", "XW");
        Add(result, new[] { 3, 5, 7, 10 }, HotMonthRollKind.FirstNoticeDate, "SB", "SB11");

        Add(result, Monthly, HotMonthRollKind.CrudeOil, "CL", "QM", "MCL");
        Add(result, Monthly, HotMonthRollKind.NaturalGas, "NG", "QG", "MNG");
        Add(result, Monthly, HotMonthRollKind.Brent, "B", "BZ", "BRN");
        return result;
    }

    private static void Add(
        IDictionary<string, HotMonthProductRule> target,
        IEnumerable<int> months,
        HotMonthRollKind rollKind,
        params string[] productIds)
    {
        var rule = new HotMonthProductRule(months.ToHashSet(), rollKind);
        foreach (string productId in productIds) target[productId] = rule;
    }
}

internal static class HotMonthSelector
{
    public static ContractInfo? Select(IEnumerable<ContractInfo> contracts, DateTime tradingDate)
    {
        List<ContractInfo> futures = contracts
            .Where(contract => !string.IsNullOrWhiteSpace(contract.Symbol))
            .Where(IsFuturesContract)
            .ToList();
        if (futures.Count == 0) return null;

        string productId = futures.Select(contract => contract.ProductId)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
        bool hasRule = HotMonthRuleCatalog.TryGet(productId, out HotMonthProductRule? rule);

        if (hasRule && rule is not null)
        {
            List<ContractInfo> cycleContracts = futures
                .Where(contract => TryGetContractYearMonth(contract, out _, out int month) && rule.ActiveMonths.Contains(month))
                .ToList();
            if (cycleContracts.Count > 0) futures = cycleContracts;
        }

        List<ContractInfo> eligible = futures
            .Where(contract => IsEligible(contract, tradingDate.Date, rule))
            .OrderBy(ContractMonthSortKey)
            .ThenBy(contract => contract.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (eligible.Count == 0) return null;

        if (hasRule) return eligible[0];
        return eligible.FirstOrDefault(contract => contract.Hot) ?? eligible[0];
    }

    public static string ContractMonth(ContractInfo contract)
    {
        foreach (string value in new[] { contract.ComYM2, contract.ComYM, contract.Month })
        {
            string digits = new((value ?? string.Empty).Where(char.IsDigit).ToArray());
            if (digits.Length >= 6) return digits.Substring(0, 6);
        }
        return string.Empty;
    }

    private static bool IsEligible(ContractInfo contract, DateTime tradingDate, HotMonthProductRule? rule)
    {
        if (rule is not null && TryResolveSwitchDate(contract, rule, out DateTime switchDate))
            return tradingDate < switchDate.Date;

        if (TryResolveDate(contract.FirstNoticeDate, out DateTime firstNoticeDate))
            return tradingDate < firstNoticeDate.Date.AddDays(-1);
        if (TryResolveDate(contract.LastTradeDate, out DateTime lastTradeDate))
            return tradingDate < lastTradeDate.Date.AddDays(-1);
        if (TryResolveDate(contract.ExpiryDate, out DateTime expiryDate))
            return tradingDate < expiryDate.Date.AddDays(-1);
        if (TryResolveDate(contract.EndDate, out DateTime legacyEndDate))
            return tradingDate < legacyEndDate.Date.AddDays(-1);

        return TryGetContractYearMonth(contract, out int year, out int month) &&
               year * 100 + month >= tradingDate.Year * 100 + tradingDate.Month;
    }

    private static bool TryResolveSwitchDate(ContractInfo contract, HotMonthProductRule rule, out DateTime switchDate)
    {
        switchDate = default;

        // 明確輸入的第一通知日優先；實物交割商品沒有 FND 才依 LTD、Expiry 回退。
        if (TryResolveDate(contract.FirstNoticeDate, out DateTime firstNoticeDate))
        {
            switchDate = firstNoticeDate.AddDays(-1);
            return true;
        }

        if (rule.RollKind is HotMonthRollKind.CrudeOil or HotMonthRollKind.NaturalGas or HotMonthRollKind.Brent &&
            TryGetContractYearMonth(contract, out int energyYear, out int energyMonth))
        {
            DateTime deliveryMonth = new(energyYear, energyMonth, 1);
            switchDate = rule.RollKind == HotMonthRollKind.Brent
                ? deliveryMonth.AddMonths(-2).AddDays(19)
                : deliveryMonth.AddMonths(-1).AddDays(19);
            return true;
        }

        if (TryResolveDate(contract.LastTradeDate, out DateTime lastTradeDate))
        {
            switchDate = lastTradeDate.AddDays(-1);
            return true;
        }
        if (TryResolveDate(contract.ExpiryDate, out DateTime expiryDate))
        {
            switchDate = expiryDate.AddDays(-1);
            return true;
        }
        if (TryResolveDate(contract.EndDate, out DateTime legacyEndDate))
        {
            switchDate = legacyEndDate.AddDays(-1);
            return true;
        }
        return false;
    }

    private static bool IsFuturesContract(ContractInfo contract)
    {
        if (contract.CPF is CPFEnum.Call or CPFEnum.Put) return false;
        char comType = char.ToUpperInvariant(contract.ComType);
        return contract.CPF == CPFEnum.Futures || comType is 'F' or '0' or '\0';
    }

    private static long ContractMonthSortKey(ContractInfo contract)
    {
        return TryGetContractYearMonth(contract, out int year, out int month)
            ? year * 100L + month
            : long.MaxValue;
    }

    private static bool TryGetContractYearMonth(ContractInfo contract, out int year, out int month)
    {
        year = 0;
        month = 0;
        string value = ContractMonth(contract);
        return value.Length == 6 &&
               int.TryParse(value.Substring(0, 4), out year) &&
               int.TryParse(value.Substring(4, 2), out month) &&
               month is >= 1 and <= 12;
    }

    private static bool TryResolveDate(string? value, out DateTime date)
    {
        date = default;
        string digits = new((value ?? string.Empty).Where(char.IsDigit).ToArray());
        return digits.Length >= 8 && DateTime.TryParseExact(
            digits.Substring(0, 8),
            "yyyyMMdd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);
    }
}
