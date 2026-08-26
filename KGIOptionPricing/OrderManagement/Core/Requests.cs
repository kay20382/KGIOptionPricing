using System;

namespace KGIOptionPricing.OrderManagement.Core
{
    public class OrderRequest
    {
        public string OrderId { get; set; } = Guid.NewGuid().ToString();
        public InstrumentKey Instrument { get; set; } = new InstrumentKey();
        public BuySell BuySell { get; set; }
        public double Price { get; set; }
        public int Quantity { get; set; }
        public TimeInForce TimeInForce { get; set; } = TimeInForce.ROD;
        public PositionEffect PositionEffect { get; set; } = PositionEffect.Auto;
        
        // Market-specific fields (Account, FCM, Broker, etc. could be injected or retrieved globally, 
        // but often attached here if multiple accounts are used).
        public string Account { get; set; } = string.Empty;
        public string BrokerId { get; set; } = string.Empty;
        public string FCM { get; set; } = string.Empty;
    }

    public class ModifyOrderRequest
    {
        public string OrderId { get; set; } = string.Empty;
        public ExternalOrderIdentity Identity { get; set; } = new ExternalOrderIdentity();
        public InstrumentKey Instrument { get; set; } = new InstrumentKey();
        public string Symbol { get; set; } = string.Empty;
        public string WebId { get; set; } = string.Empty;
        public BuySell BuySell { get; set; }
        public double NewPrice { get; set; }
        public int NewQuantity { get; set; }
        public string Account { get; set; } = string.Empty;
        public string BrokerId { get; set; } = string.Empty;
    }

    public class CancelOrderRequest
    {
        public string OrderId { get; set; } = string.Empty;
        public ExternalOrderIdentity Identity { get; set; } = new ExternalOrderIdentity();
        public InstrumentKey Instrument { get; set; } = new InstrumentKey();
        public string Symbol { get; set; } = string.Empty;
        public string WebId { get; set; } = string.Empty;
        public BuySell BuySell { get; set; }
        public string Account { get; set; } = string.Empty;
        public string BrokerId { get; set; } = string.Empty;
        public ushort Qty { get; set; } = 1;
    }
}
