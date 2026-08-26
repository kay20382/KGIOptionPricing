using System;

namespace KGIOptionPricing.OrderManagement.Core
{
    public class OrderEvent
    {
        public ExternalOrderIdentity Identity { get; set; } = new ExternalOrderIdentity();
        public OrderEventType EventType { get; set; }
        public OrderOperationType OperationType { get; set; }
        public OrderSource Source { get; set; } = OrderSource.Internal;
        public InstrumentKey? Instrument { get; set; }
        public BuySell BuySell { get; set; }
        public double OrderPrice { get; set; }
        public int OrderQty { get; set; }
        public int LeavesQty { get; set; }
        public int CumQty { get; set; }
        public string BrokerId { get; set; } = string.Empty;
        public string Account { get; set; } = string.Empty;
        public string ErrorCode { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public double UpdatedPrice { get; set; }
        public int UpdatedQty { get; set; }
        public DateTime EventTime { get; set; } = DateTime.Now;
    }

    public class FillEvent
    {
        public FillInfo Fill { get; set; } = new FillInfo();
        public BuySell BuySell { get; set; }
        public DateTime EventTime { get; set; } = DateTime.Now;
    }

    public class RecoveryEvent
    {
        public string OrderId { get; set; } = string.Empty;
        public ExternalOrderIdentity Identity { get; set; } = new ExternalOrderIdentity();
        public OrderSource Source { get; set; } = OrderSource.Recovered;
        public InstrumentKey? Instrument { get; set; }
        public BuySell BuySell { get; set; }
        public double OrderPrice { get; set; }
        public int OrderQty { get; set; }
        public int FilledQty { get; set; }
        public int LeavesQty { get; set; }
        public double AvgFilledPrice { get; set; }
        public OrderStatus Status { get; set; }
        public DateTime OrderTime { get; set; } = DateTime.Now;
        public string BrokerId { get; set; } = string.Empty;
        public string Account { get; set; } = string.Empty;
        public string ErrorCode { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}
