using System;

namespace KGIOptionPricing.OrderManagement.Core
{
    public sealed record StrategyOrderRouteMetadata(
        string StrategyInstanceId,
        long StrategyRunGeneration,
        string ClientOrderId,
        string CycleId,
        string Symbol,
        string OrderRole,
        string OcoGroupId = "");

    public class InstrumentKey
    {
        public MarketType Market { get; set; }
        public string Exchange { get; set; } = string.Empty;
        public string Symbol { get; set; } = string.Empty;
        public string ContractMonth { get; set; } = string.Empty;

        public override string ToString()
        {
            return $"{Market}-{Exchange}-{Symbol}-{ContractMonth}";
        }
    }

    public class ExternalOrderIdentity
    {
        /// <summary>內部傳送序號 (NewOrder/ModifyOrder 等 API 呼叫時產生的 RequestId)</summary>
        public string RequestId { get; set; } = string.Empty;
        /// <summary>網路電子單號 (KGI 2002 或 3302 帶回的 CNT / TradeNo)</summary>
        public string Cnt { get; set; } = string.Empty;
        /// <summary>第一次成功新單的 CNT；國外刪改單全程固定使用此值。</summary>
        public string OrgCnt { get; set; } = string.Empty;
        /// <summary>最近一次新單、改價或刪單操作所產生的 CNT。</summary>
        public string LastOperationCnt { get; set; } = string.Empty;
        /// <summary>最近一次由本系統送出的 RequestId。</summary>
        public string LastRequestId { get; set; } = string.Empty;
        /// <summary>委託書號 (KGI 2010 或 3310 帶回的 ORDNO)</summary>
        public string OrdNo { get; set; } = string.Empty;
        /// <summary>網路單號 / 來源識別代號 (WebId)</summary>
        public string WebId { get; set; } = string.Empty;
        /// <summary>國外交易所/上手單號等擴充</summary>
        public string ExternalOrderId { get; set; } = string.Empty;
        /// <summary>成交編號 </summary>
        public string ExecID { get; set; } = string.Empty;
    }

    /// <summary>
    /// 國外期貨/選擇權刪改單需要沿用第一次成功委託回報的上手資料。
    /// 這些欄位不放進內期流程，避免兩種市場的 API 規則互相污染。
    /// </summary>
    public class ForeignOrderContext
    {
        public string Fcm { get; set; } = string.Empty;
        public string ClearingAccount { get; set; } = string.Empty;
        public string TradeDate { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string KeyIn { get; set; } = string.Empty;
        /// <summary>F=期貨、C=Call、P=Put；改刪單須沿用第一次 3310。</summary>
        public char Cp { get; set; }
        /// <summary>Y=當沖、N=非當沖；TradeCom 不接受舊程式使用的 '0'。</summary>
        public char DayTrade { get; set; } = 'N';
        public char PositionEffect { get; set; } = 'A';
        public char TimeInForce { get; set; } = 'R';
        public char PriceFlag { get; set; } = '2';
    }

    public class OrderOperationRecord
    {
        public string RequestId { get; set; } = string.Empty;
        public string Cnt { get; set; } = string.Empty;
        public string OrgCnt { get; set; } = string.Empty;
        public OrderOperationType OperationType { get; set; }
        public OrderEventType EventType { get; set; }
        public bool IsSuccess { get; set; }
        public string ErrorCode { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public double ReportedPrice { get; set; }
        public int ReportedQuantity { get; set; }
        public int ReportedLeavesQuantity { get; set; }
        public DateTime EventTime { get; set; }
    }

    public class OrderModifyRecord
    {
        public double OldPrice { get; set; }
        public int OldQty { get; set; }
        public double NewPrice { get; set; }
        public int NewQty { get; set; }
        public DateTime ModifyTime { get; set; }
    }

    public class FillInfo
    {
        public string ExecID { get; set; } = string.Empty; // Domestic/Foreign specific unique identity
        public string OrderId { get; set; } = string.Empty;
        public string Cnt { get; set; } = string.Empty;
        public string OrdNo { get; set; } = string.Empty;
        public string Exchange { get; set; } = string.Empty;
        public string Symbol { get; set; } = string.Empty;
        public double DealPrice { get; set; }
        public int DealQty { get; set; }
        public int CumQty { get; set; }
        public int LeavesQty { get; set; }
        public DateTime TradeTime { get; set; }
    }
}
