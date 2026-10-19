using KGIOptionPricing.StrategyFramework;

namespace MeanReversionBacktest;

// Research-only execution policy. The shared broker still owns all orders and fills.
internal sealed class PqcResearchExecution(IManagedStrategyExecutionService inner) : IManagedStrategyExecutionService
{
    public event Action<ExecutionEvent>? Filled {add=>inner.Filled+=value;remove=>inner.Filled-=value;}
    public event Action<string>? Failed {add=>inner.Failed+=value;remove=>inner.Failed-=value;}
    public event Action<OrderLifecycleEvent>? OrderLifecycleChanged {add=>inner.OrderLifecycleChanged+=value;remove=>inner.OrderLifecycleChanged-=value;}
    public PaperOrder Submit(PaperOrderRequest request,DateTimeOffset now)=>inner.Submit(
        request.Role==PaperOrderRole.StopLoss ? request with {Type=PaperOrderType.Market} : request,now);
    public bool Cancel(string id)=>inner.Cancel(id);
    public int CancelAll(string id)=>inner.CancelAll(id);
    public bool CancelByClientOrderId(string id,string client)=>inner.CancelByClientOrderId(id,client);
    public IReadOnlyList<PaperOrder> GetWorkingOrders(string id)=>inner.GetWorkingOrders(id);
    public void OnMarketEvent(MarketEvent item)=>inner.OnMarketEvent(item);
}
