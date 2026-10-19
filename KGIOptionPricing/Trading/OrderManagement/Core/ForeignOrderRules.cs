using System.Linq;

namespace KGIOptionPricing.OrderManagement.Core
{
    /// <summary>國外委託刪改單的市場專屬驗證與回報成功判定。</summary>
    public static class ForeignOrderRules
    {
        public static bool IsForeign(InstrumentKey? instrument) =>
            instrument?.Market is MarketType.ForeignFuture or MarketType.ForeignOption;

        public static bool IsSuccessCode(string? errorCode)
        {
            string value = (errorCode ?? string.Empty).Trim();
            return value.Length == 0 || value.All(c => c == '0');
        }

        public static char ResolveCp(InstrumentKey? instrument, ForeignOrderContext? context)
        {
            if (instrument?.Market == MarketType.ForeignFuture)
                return 'F';

            char value = char.ToUpperInvariant(context?.Cp ?? '\0');
            return value is 'C' or 'P' ? value : ' ';
        }

        public static char ResolveDayTrade(ForeignOrderContext? context) =>
            char.ToUpperInvariant(context?.DayTrade ?? 'N') == 'Y' ? 'Y' : 'N';

        public static bool TryValidateAmend(OrderInfo order, out string error)
        {
            error = string.Empty;
            if (!IsForeign(order.Instrument))
                return true;

            ForeignOrderContext? context = order.ForeignContext;
            if (string.IsNullOrWhiteSpace(context?.Fcm))
                error = "上手券商不可為空";
            else if (string.IsNullOrWhiteSpace(context.ClearingAccount))
                error = "上手帳號不可為空";
            else if (string.IsNullOrWhiteSpace(order.Identity.OrdNo) || order.Identity.OrdNo.Trim() == "0")
                error = "上手委託書號不可為空或 0";
            else if (context.TradeDate.Length != 8 || !context.TradeDate.All(char.IsDigit))
                error = "交易日期必須為 8 碼數字";
            else if (string.IsNullOrWhiteSpace(order.Identity.OrgCnt))
                error = "原始委託 OrgCnt 不可為空";
            else if (string.IsNullOrWhiteSpace(order.Identity.WebId))
                error = "原始委託 WEBID 不可為空";

            return error.Length == 0;
        }
    }
}
