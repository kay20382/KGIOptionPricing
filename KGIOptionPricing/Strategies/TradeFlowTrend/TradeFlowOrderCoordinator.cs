using KGIOptionPricing.OrderManagement.Core;
using KGIOptionPricing.StrategyFramework;
namespace KGIOptionPricing.Strategies.TradeFlowTrend;
[Flags] public enum TradeFlowExitReason {None=0,StopLoss=1,TakeProfit=2,Sar=4,Trend=8,SessionEnd=16,RiskLimit=32}
public enum FlowOrderPhase {Flat,EntryWorking,Cancelling,ExitRod,ExitIoc,ReconciliationRequired}
public abstract record FlowOrderAction;
public sealed record SubmitFlowOrder(PaperOrderRequest Request):FlowOrderAction;
public sealed record CancelFlowOrder(string ClientOrderId):FlowOrderAction;
public sealed record FlowCoordinatorSnapshot(string InstanceId,long RunGeneration,long NextCycleSequence,long NextOrderSequence,
    FlowOrderPhase Phase,string? WorkingClientOrderId,int EntryDirection,int TargetQuantity,TradeFlowExitReason ExitLatch,
    IReadOnlyList<ManagedOrderRecord> Orders,bool AwaitFreshBook,long LastBookSequence,bool EntryReplacementPending=false);

public sealed class TradeFlowOrderCoordinator
{
    private readonly string _id;private readonly long _generation;private readonly ResolvedProduct _product;private readonly TradeFlowParameters _p;
    private readonly ManagedStrategyOrderBook _orders;
    private PaperOrderRequest? _working;private OrderLifecycleEvent? _latest;private bool _cancel,_awaitBook,_reconcile;
    private long _cycle,_sequence,_lastBook;private int _direction;
    public TradeFlowExitReason ExitLatch{get;private set;}
    public FlowOrderPhase Phase{get;private set;}
    public bool HasUnsettledOrders=>_working!=null || _reconcile;
    public bool EntryReplacementPending {get;private set;}
    public TradeFlowOrderCoordinator(string instanceId,long runGeneration,ResolvedProduct product,TradeFlowParameters p)
    {_id=instanceId;_generation=runGeneration;_product=product;_p=p;_orders=new(instanceId,runGeneration);}
    public void OnSubmitted(PaperOrderRequest request)
    {if(!_orders.TryGet(request.ClientOrderId,out _))_orders.Track(request);}
    public static bool IsSettled(OrderLifecycleEvent e)=>e.State is OrderLifecycleState.Filled or OrderLifecycleState.Cancelled &&
        e.HasFinalCumulativeFillWatermark && e.BrokerFinalCumulativeFilledQuantity==e.DeliveredFilledQuantity;
    public void OnLifecycle(OrderLifecycleEvent e)
    {
        if(_working is null || e.StrategyInstanceId!=_id || e.StrategyRunGeneration!=_generation || e.ClientOrderId!=_working.ClientOrderId || e.CycleId!=_working.CycleId)return;
        if(_latest!=null && e.DeliveredFilledQuantity<_latest.DeliveredFilledQuantity)return;
        _orders.Apply(e);_latest=e;
        if(e.State==OrderLifecycleState.Rejected){_reconcile=true;Phase=FlowOrderPhase.ReconciliationRequired;return;}
        if(IsSettled(e))
        {_awaitBook=e.ReasonCode=="IocAwaitFreshBook";_working=null;_latest=null;_cancel=false;Phase=FlowOrderPhase.Flat;}
    }
    public IReadOnlyList<FlowOrderAction> Evaluate(DateTimeOffset now,BookReference? book,PositionSnapshot position,int entryDirection,bool entryAllowed,TradeFlowExitReason exits)
    {
        ExitLatch|=exits;
        if(_reconcile)return [];
        bool valid=book!=null && book.Symbol==_product.Symbol && book.Bid>0 && book.Ask>book.Bid && book.Timestamp<=now &&
            now-book.Timestamp<=TimeSpan.FromMilliseconds(_p.MaxBookAgeMs) && book.Bid%_product.TickSize==0 && book.Ask%_product.TickSize==0;
        bool exit=ExitLatch!=0;
        bool workingEntry=_working?.Role is PaperOrderRole.EntryLong or PaperOrderRole.EntryShort;
        if(_working!=null)
        {
            if(_cancel || _latest?.State is OrderLifecycleState.Filled or OrderLifecycleState.Cancelled)return [];
            bool cancel=workingEntry && (!entryAllowed || entryDirection!=_direction || exit || !valid);
            if(valid)
            {
                var target=workingEntry?new ExitPricePlan(_working.Side==BuySell.Buy?book!.Bid:book!.Ask,TimeInForce.ROD):TradeFlowExitPricing.Plan(_working.Side,book!.Bid,book.Ask,_product.TickSize,_p);
                cancel|=_working.TimeInForce==TimeInForce.ROD && (target.TimeInForce!=_working.TimeInForce ||
                    (_working.Side==BuySell.Buy?target.Price>_working.Price:target.Price<_working.Price));
            }
            if(cancel){EntryReplacementPending=workingEntry && entryAllowed && entryDirection==_direction && !exit && valid;
                _cancel=true;Phase=FlowOrderPhase.Cancelling;return [new CancelFlowOrder(_working.ClientOrderId)];}
            return [];
        }
        if(exit && position.NetQuantity==0)
        {
            if(!entryAllowed)return [];
            ExitLatch=0;exit=false;_direction=0;
        }
        if(!valid || (_awaitBook && book!.Sequence==_lastBook))return [];
        _awaitBook=false;_lastBook=book!.Sequence;
        if(exit || !entryAllowed)EntryReplacementPending=false;
        BuySell side;int quantity;PaperOrderRole role;ExitPricePlan plan;
        if(exit && position.NetQuantity!=0)
        {
            side=position.NetQuantity>0?BuySell.Sell:BuySell.Buy;quantity=Math.Abs(position.NetQuantity);
            role=(ExitLatch&(TradeFlowExitReason.SessionEnd|TradeFlowExitReason.RiskLimit))!=0?PaperOrderRole.Flatten:
                ExitLatch.HasFlag(TradeFlowExitReason.StopLoss)?PaperOrderRole.StopLoss:ExitLatch.HasFlag(TradeFlowExitReason.TakeProfit)?PaperOrderRole.TakeProfit:PaperOrderRole.Flatten;
            plan=TradeFlowExitPricing.Plan(side,book.Bid,book.Ask,_product.TickSize,_p);
        }
        else
        {
            if(!entryAllowed || entryDirection==0 || (position.NetQuantity!=0 && (_direction==0 || Math.Sign(position.NetQuantity)!=entryDirection)) || Math.Abs(position.NetQuantity)>=_p.Quantity)return [];
            if(position.NetQuantity==0){_cycle++;_direction=entryDirection;}
            side=entryDirection>0?BuySell.Buy:BuySell.Sell;quantity=_p.Quantity-Math.Abs(position.NetQuantity);
            role=entryDirection>0?PaperOrderRole.EntryLong:PaperOrderRole.EntryShort;plan=new(side==BuySell.Buy?book.Bid:book.Ask,TimeInForce.ROD);
        }
        _working=new PaperOrderRequest{StrategyInstanceId=_id,StrategyRunGeneration=_generation,Symbol=_product.Symbol,
            ClientOrderId=$"{_id}:run-{_generation}:flow-{_cycle}:{++_sequence}",CycleId=$"flow-{_cycle}",Side=side,Role=role,
            Quantity=quantity,Type=PaperOrderType.BrokerLimit,TimeInForce=plan.TimeInForce,Price=plan.Price};
        OnSubmitted(_working);Phase=exit?(plan.TimeInForce==TimeInForce.IOC?FlowOrderPhase.ExitIoc:FlowOrderPhase.ExitRod):FlowOrderPhase.EntryWorking;
        EntryReplacementPending=false;
        return [new SubmitFlowOrder(_working)];
    }
    public FlowCoordinatorSnapshot Export()=>new(_id,_generation,_cycle,_sequence,Phase,_working?.ClientOrderId,_direction,_p.Quantity,ExitLatch,_orders.Snapshot().Orders,_awaitBook,_lastBook,EntryReplacementPending);
    public void Restore(FlowCoordinatorSnapshot state)
    {
        if(state.InstanceId!=_id || state.RunGeneration!=_generation || state.TargetQuantity!=_p.Quantity || _sequence!=0)throw new ArgumentException("Checkpoint identity");
        foreach(var r in state.Orders){_orders.Track(r.Request);if(r.LastLifecycle!=null)_orders.Apply(r.LastLifecycle);}
        _cycle=state.NextCycleSequence;_sequence=state.NextOrderSequence;_direction=state.EntryDirection;ExitLatch=state.ExitLatch;
        _working=state.Orders.FirstOrDefault(r=>r.Request.ClientOrderId==state.WorkingClientOrderId)?.Request;
        _awaitBook=state.AwaitFreshBook;_lastBook=state.LastBookSequence;_reconcile=true;Phase=FlowOrderPhase.ReconciliationRequired;
        EntryReplacementPending=state.EntryReplacementPending;
    }
}
