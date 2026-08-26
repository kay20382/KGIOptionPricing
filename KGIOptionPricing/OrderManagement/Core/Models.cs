using System;

namespace KGIOptionPricing.OrderManagement.Core
{
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
        /// <summary>委託書號 (KGI 2010 或 3310 帶回的 ORDNO)</summary>
        public string OrdNo { get; set; } = string.Empty;
        /// <summary>網路單號 / 來源識別代號 (WebId)</summary>
        public string WebId { get; set; } = string.Empty;
        /// <summary>國外交易所/上手單號等擴充</summary>
        public string ExternalOrderId { get; set; } = string.Empty;
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
        public string FillId { get; set; } = string.Empty; // Domestic/Foreign specific unique identity
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
