using System;

namespace KGIOptionPricing.OrderManagement.Core
{
    public enum MarketType
    {
        DomesticFuture,
        DomesticOption,
        ForeignFuture,
        ForeignOption
    }

    public enum OrderSource
    {
        Internal,    // 本系統送出之委託單
        External,    // 由外部系統 (手機APP/網頁/人工) 送出之委託單
        Recovered    // 系統啟動/重連後回補之委託單
    }

    public enum OrderOperationType
    {
        New,
        Modify,
        Cancel
    }

    public enum OrderEventType
    {
        Ack,             // First/Second reply acknowledgment (e.g. 2002/3302)
        Accepted,        // Order successfully accepted by exchange (2010/3310 New)
        Rejected,        // Order rejected (2010/3310 Error)
        Cancelled,       // Order cancelled (2010/3310 Cancel)
        Modified,        // Order modified (2010/3310 Modify)
        CancelFailed,    // Cancel rejected
        ModifyFailed     // Modify rejected
    }

    public enum OrderStatus
    {
        PendingSubmit,
        Submitted,
        Accepted,
        PartiallyFilled,
        Filled,
        CancelPending,
        ModifyPending,
        Cancelled,
        Rejected
    }

    public enum BuySell
    {
        Buy,
        Sell
    }

    public enum TimeInForce
    {
        ROD, // Rest of Day
        IOC, // Immediate or Cancel
        FOK  // Fill or Kill
    }

    public enum PositionEffect
    {
        Auto,
        Open,
        Close,
        DayTrade
    }
}
