using System;
using System.Collections.Generic;

namespace KGIOptionPricing.OrderManagement.Core
{
    public class OrderInfo
    {
        public string OrderId { get; set; } = string.Empty;
        public OrderSource Source { get; set; } = OrderSource.Internal;
        public InstrumentKey Instrument { get; set; } = new InstrumentKey();
        public ExternalOrderIdentity Identity { get; set; } = new ExternalOrderIdentity();
        public ForeignOrderContext? ForeignContext { get; set; }
        public StrategyOrderRouteMetadata? StrategyRoute { get; set; }
        public BuySell BuySell { get; set; }
        
        public double OrderPrice { get; set; }
        public int OrderQty { get; set; }
        
        public int FilledQty { get; set; }
        public int LeavesQty { get; set; }
        public double AvgFilledPrice { get; set; }
        
        public OrderStatus Status { get; set; }
        public OrderStatus PreviousStatus { get; set; }
        
        public List<OrderModifyRecord> ModifyHistory { get; set; } = new List<OrderModifyRecord>();
        public List<OrderOperationRecord> OperationHistory { get; set; } = new List<OrderOperationRecord>();
        
        public DateTime SubmitTime { get; set; }
        public DateTime UpdateTime { get; set; }
        public string BrokerId { get; set; } = string.Empty;
        public string Account { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string ErrorCode { get; set; } = string.Empty;
    }
}
