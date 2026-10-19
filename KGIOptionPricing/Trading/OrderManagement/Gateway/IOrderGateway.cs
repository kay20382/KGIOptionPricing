using KGIOptionPricing.OrderManagement.Core;

namespace KGIOptionPricing.OrderManagement.Gateway
{
    public class OrderResult
    {
        public bool IsSuccess { get; set; }
        public string RequestId { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
    }

    public interface IOrderGateway
    {
        OrderResult NewOrder(OrderRequest request);
        OrderResult ModifyOrder(ModifyOrderRequest request);
        OrderResult CancelOrder(CancelOrderRequest request);

        event System.Action<OrderEvent> OrderEventReceived;
        event System.Action<FillEvent> FillEventReceived;
        event System.Action<RecoveryEvent> RecoveryEventReceived;
    }
}
