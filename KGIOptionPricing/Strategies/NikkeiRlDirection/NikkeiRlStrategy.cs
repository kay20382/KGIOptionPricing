using KGIOptionPricing.StrategyFramework;
using KGIOptionPricing.OrderManagement.Core;

namespace KGIOptionPricing.Strategies.NikkeiRlDirection;

public sealed class NikkeiRlFactory:IStrategyFactory
{
    public const string TypeName="NikkeiRlDirection";
    public string StrategyType=>TypeName;
    public string DisplayName=>"日經RL（twelve-training-control／日夜盤）";
    public ISimulationStrategy Create(StrategyDefinition definition)=>new NikkeiRlStrategy(definition);
}

public sealed class NikkeiRlStrategy:ISimulationStrategy,IManagedOrderStrategy,IStrategyHeartbeat,
    IStrategyScheduledPulseSource,IStrategyBeforeMarketExecution,IStrategyCloseRequest,IStrategyRiskExit,
    IManagedOrderSubmissionObserver,IStrategyDecisionSource,IStrategyHeartbeatWhilePaused
{
    readonly NikkeiRlParameters parameters;
    readonly Func<NikkeiRlParameters,IRlMainPolicy> policyFactory;
    IRlMainPolicy? actor;
    StrategyRuntimeContext? context;
    NikkeiRlFeatureStream? stream;
    DateTimeOffset? boundary,enteredAt,cooldownUntil,pendingAt,actionAt,lastEvent;
    PaperOrderRequest? pending;
    string? entryId,exitId;
    int delayedAction,serial;
    bool paused,closing,faulted;
    bool entryCancelRequested;
    ManagedStrategyOrderBook? orders;
    readonly HashSet<string> awaitingFills=new(),executedOrders=new();
    DateTimeOffset? sessionOpen;
    DateTimeOffset observedAt;
    readonly Dictionary<string,decimal?> features=new();
    string sessionName="休市";
    public bool ShouldPulseWhilePaused=>closing||entryId!=null||exitId!=null;
    decimal peak,stopPrice;
    string reason="等待手動啟動";
    int action;
    double inferenceMs;
    public string StrategyType=>NikkeiRlFactory.TypeName;
    public string InstanceId {get;}
    public StrategyLifecycleState State {get;private set;}=StrategyLifecycleState.Created;
    public event Action<PaperOrderRequest>? OrderRequested;
    public event Action? CancelAllRequested;
    public event Action<string>? CancelOrderRequested;
    public event Action<StrategySnapshot>? SnapshotChanged;
    public event Action<StrategyDecisionObservation>? DecisionObserved;
    PositionSnapshot Position=>context!.Performance.GetPosition(InstanceId,context.Universe.Tradable.Symbol);
    public NikkeiRlStrategy(StrategyDefinition definition,Func<NikkeiRlParameters,IRlMainPolicy>? factory=null)
    {
        if(definition.Universe.Tradable.ProductId!="225MC"||definition.Universe.References.Count!=1||definition.Universe.References[0].ProductId!="225M")throw new ArgumentException("日經RL只允許225MC及225M參考");
        InstanceId=definition.InstanceId;parameters=NikkeiRlParameters.Parse(definition.Parameters);policyFactory=factory??(p=>p.IsGrid?new FrozenGrid15Policy(p):new FrozenRlMainPolicy(p));
    }
    public void Start(StrategyRuntimeContext value)
    {
        if(value.Universe.Tradable.ProductId!="225MC"||value.Universe.Tradable.TickSize!=5||value.Universe.Tradable.ContractMultiplier!=10||value.Universe.References.Count!=1||value.Universe.References[0].ProductId!="225M")throw new ArgumentException("模型商品規格不符");
        context=value;orders=new(InstanceId,value.StrategyRunGeneration);
        try{actor=policyFactory(parameters);stream=new(value.Universe.Tradable.TickSize);State=StrategyLifecycleState.WarmingUp;reason="等待兩商品、力道及完整研究特徵暖機";Publish();}
        catch{State=StrategyLifecycleState.Faulted;reason="模型載入失敗";actor?.Dispose();actor=null;throw;}
    }
    public bool BeforeMarketExecution(MarketEvent e)
    {
        if(context==null||stream==null||State==StrategyLifecycleState.Stopped)return false;
        if(!stream.CanAccept(e.Timestamp)){reason="OutOfOrderEvent：略過倒序行情與成交";return false;}
        var resolved=context.Universe.AllProducts.FirstOrDefault(p=>p.ProductId==e.ProductId);
        if(resolved==null||resolved.Symbol!=e.Symbol){Fault("商品合約不符");return false;}
        if(e.Kind is MarketEventKind.Connection or MarketEventKind.Session)
        {CancelEntry();stream=new(context.Universe.Tradable.TickSize);boundary=null;lastEvent=e.Timestamp;reason="連線／時段重置，重新暖機";return true;}
        Advance(e.Timestamp);
        if(e.ProductId=="225MC"&&e.Kind==MarketEventKind.Trade)
        {
            if(pending!=null&&pendingAt.HasValue&&e.Timestamp>=pendingAt)
            {var request=pending;pending=null;pendingAt=null;entryId=request.ClientOrderId;entryCancelRequested=false;OrderRequested?.Invoke(request);}
            if(actionAt.HasValue&&e.Timestamp>=actionAt)
            {var next=delayedAction;actionAt=null;delayedAction=0;if(next==3)Flatten();else CancelEntry();}
        }
        return true;
    }
    public void OnMarketEvent(MarketEvent e)
    {
        if(stream==null||NikkeiRlSession.At(e.Timestamp,parameters.EnableNightSession)==null||!stream.Accept(e))return;
        lastEvent=e.Timestamp;
        if(context!=null)peak=Math.Max(peak,Position.NetPnl);
        // Do not persist or publish an unchanged snapshot on every tick.
    }
    static DateTimeOffset GridAfter(DateTimeOffset at)=>new DateTimeOffset(at.Ticks-at.Ticks%TimeSpan.FromSeconds(3).Ticks,at.Offset).AddSeconds(3);
    void Advance(DateTimeOffset now)
    {
        if(stream==null||context==null)return;
        var current=NikkeiRlSession.At(now,parameters.EnableNightSession);
        if(current?.Open!=sessionOpen){CancelEntry();stream=new(context.Universe.Tradable.TickSize);boundary=null;sessionOpen=current?.Open;}
        sessionName=current?.Name??"休市";
        boundary??=GridAfter(now);
        // A gap of hours cannot create an unbounded catch-up loop or stale trading.
        if((now-boundary.Value).TotalSeconds>600){CancelEntry();stream=new(context.Universe.Tradable.TickSize);boundary=GridAfter(now);reason="DataGap：重啟暖機";if(Position.NetQuantity!=0)Flatten();Publish();return;}
        while(boundary<=now)
        {
            var at=boundary.Value;boundary=at.AddSeconds(3);
            var frame=stream.Emit(at);
            observedAt=at;
            features["WarmupElapsedSeconds"]=stream.WarmupElapsedSeconds(at);
            features["MinimumWarmupSeconds"]=NikkeiRlFeatureStream.MinimumWarmupSeconds;
            features["DataReady"]=frame.Valid?1:0;
            if(entryId!=null&&pendingAt==null&&pendingSubmitted.HasValue&&(at-pendingSubmitted.Value).TotalSeconds>=10)CancelEntry();
            if(pending!=null&&pendingAt.HasValue&&(at-pendingAt.Value).TotalSeconds>=10)CancelEntry();
            var p=Position;peak=Math.Max(peak,p.NetPnl);
            var session=NikkeiRlSession.At(at,parameters.EnableNightSession);
            if(session==null||!session.AllowsEntry(at)){
                CancelEntry();actionAt=null;delayedAction=0;if(p.NetQuantity!=0)Flatten();
                reason=$"SESSION_CLOSE：{session?.Name??"休市"}停止進場／平倉";Publish();continue;
            }
            if(closing||faulted){if(p.NetQuantity!=0)Flatten();else FinishIfFlat();Publish();continue;}
            if(paused){reason="暫停：保留保護停損";Publish();continue;}
            bool enter=frame.Valid&&p.NetQuantity==0&&entryId==null&&pending==null&&exitId==null&&(cooldownUntil==null||at>=cooldownUntil);
            bool direction=frame.Valid&&p.NetQuantity!=0&&exitId==null&&delayedAction!=3;
            bool[] mask=[true,enter||direction,enter||direction,p.NetQuantity!=0&&exitId==null&&delayedAction!=3,(entryId!=null||pending!=null)&&delayedAction!=4];
            decimal tickValue=context.Universe.Tradable.TickSize*context.Universe.Tradable.ContractMultiplier;
            double[] obs=frame.Market.Concat(new[]{(double)p.NetQuantity,enteredAt.HasValue?(at-enteredAt.Value).TotalSeconds/60:0,(double)(p.UnrealizedPnl/tickValue),(double)(p.NetPnl/tickValue),(double)((peak-p.NetPnl)/tickValue),cooldownUntil.HasValue?Math.Max(0,(cooldownUntil.Value-at).TotalSeconds)/30:0,(entryId!=null||pending!=null)?1d:0,exitId!=null||delayedAction==3?1d:0,frame.Valid?1d:0,(session.Close-at).TotalSeconds/3600}).Concat(frame.Research).ToArray();
            for(int i=0;i<obs.Length;i++)features[$"Raw{i}"]=(decimal)obs[i];
            try
            {
                if(actor is FrozenGrid15Policy grid)grid.SetContext(at,session.Open,frame.Valid);
                var result=actor!.Predict(obs,mask);action=result.Action;inferenceMs=result.InferenceMs;
                if(actor is FrozenGrid15Policy detail){
                    features["EntryThresholdJPY"]=(decimal)detail.ThresholdJPY;
                    features["LongNetScoreJPY"]=double.IsFinite(detail.LongNetJPY)?(decimal)detail.LongNetJPY:null;
                    features["ShortNetScoreJPY"]=double.IsFinite(detail.ShortNetJPY)?(decimal)detail.ShortNetJPY:null;
                    features["HoldingAdvantageJPY"]=detail.HoldingAdvantageJPY.HasValue?(decimal)detail.HoldingAdvantageJPY.Value:null;
                }
                if(action is <0 or >4||!mask[action])throw new InvalidDataException("模型動作不合法");
                reason=frame.Valid?$"{session.Name}模型動作={action}，推論={inferenceMs:F4}ms":$"WARMUP {features["WarmupElapsedSeconds"]:F0}/210秒；兩商品／力道／研究特徵尚未全部就緒";
                State=frame.Valid?StrategyLifecycleState.Running:StrategyLifecycleState.WarmingUp;
                if(action is 1 or 2)
                {
                    int directionSign=action==1?1:-1;
                    if(p.NetQuantity!=0){if(Math.Sign(p.NetQuantity)==directionSign)reason="SAME_DIRECTION_HOLD";else{ScheduleModelAction(3,at);reason="REVERSE_SIGNAL";}}
                    else{ScheduleEntry(Request(action==1?BuySell.Buy:BuySell.Sell,PaperOrderType.Limit,action==1?PaperOrderRole.EntryLong:PaperOrderRole.EntryShort,action==1?frame.Bid:frame.Ask),at);}
                }
                if(action is 3 or 4)ScheduleModelAction(action,at);
            }
            catch(Exception ex){Fault("推論失敗："+ex.Message);}
            Publish();
        }
    }
    DateTimeOffset? pendingSubmitted;
    void ScheduleEntry(PaperOrderRequest request,DateTimeOffset at)
    {
        if(parameters.LiveTradingEnabled){pendingSubmitted=at;entryId=request.ClientOrderId;entryCancelRequested=false;OrderRequested?.Invoke(request);}
        else{pending=request;pendingAt=at.AddMilliseconds(parameters.ActionLatencyMs);pendingSubmitted=pendingAt;}
    }
    void ScheduleModelAction(int next,DateTimeOffset at)
    {
        if(parameters.LiveTradingEnabled){if(next==3)Flatten();else CancelEntry();}
        else{delayedAction=next;actionAt=at.AddMilliseconds(parameters.ActionLatencyMs);}
    }
    PaperOrderRequest Request(BuySell side,PaperOrderType type,PaperOrderRole role,decimal price)=>new(){StrategyInstanceId=InstanceId,Symbol=context!.Universe.Tradable.Symbol,Side=side,Type=type,Role=role,Price=decimal.Round(price/context.Universe.Tradable.TickSize,0)*context.Universe.Tradable.TickSize,Quantity=1,ClientOrderId=$"{InstanceId}-RL-{++serial}",CycleId=$"{InstanceId}-RL-{serial}"};
    void CancelEntry(){pending=null;pendingAt=null;if(entryId!=null&&!entryCancelRequested){entryCancelRequested=true;CancelOrderRequested?.Invoke(entryId);}if(delayedAction==4){delayedAction=0;actionAt=null;}}
    void Flatten()
    {
        CancelEntry();if(context==null||Position.NetQuantity==0||exitId!=null)return;
        var p=Position;var request=Request(p.NetQuantity>0?BuySell.Sell:BuySell.Buy,PaperOrderType.Market,PaperOrderRole.Flatten,p.MarkPrice>0?p.MarkPrice:p.AveragePrice);
        exitId=request.ClientOrderId;CancelAllRequested?.Invoke();OrderRequested?.Invoke(request);
    }
    void Fault(string message){faulted=true;paused=true;State=StrategyLifecycleState.Faulted;reason=message;CancelEntry();if(context!=null&&Position.NetQuantity!=0)Flatten();}
    public void OnExecution(ExecutionEvent e)
    {
        if(e.StrategyInstanceId!=InstanceId||context==null||(e.StrategyRunGeneration!=0&&e.StrategyRunGeneration!=context.StrategyRunGeneration))return;
        executedOrders.Add(e.ClientOrderId);awaitingFills.Remove(e.ClientOrderId);
        if(e.Role is PaperOrderRole.EntryLong or PaperOrderRole.EntryShort)
        {
            if(entryId!=null&&entryId!=e.ClientOrderId){Fault("舊進場單晚到成交，停止新倉並保護部位");}
            else{entryId=null;entryCancelRequested=false;pending=null;pendingAt=null;pendingSubmitted=null;}
            enteredAt=e.Timestamp;
            int side=Math.Sign(Position.NetQuantity);stopPrice=e.Price-side*150;
            if(side!=0&&!(paused||closing||faulted))OrderRequested?.Invoke(Request(side>0?BuySell.Sell:BuySell.Buy,PaperOrderType.Stop,PaperOrderRole.StopLoss,stopPrice));
            if(paused||closing||faulted||NikkeiRlSession.At(e.Timestamp,parameters.EnableNightSession)?.AllowsEntry(e.Timestamp)!=true){State=StrategyLifecycleState.Suspended;Flatten();}
        }
        else if(Position.NetQuantity==0)
        {CancelAllRequested?.Invoke();exitId=null;CancelEntry();enteredAt=null;actionAt=null;delayedAction=0;stopPrice=0;cooldownUntil=e.Timestamp.AddSeconds(10);FinishIfFlat();}
        Publish();
    }
    public void OnOrderLifecycle(OrderLifecycleEvent e)
    {
        if(e.StrategyInstanceId!=InstanceId||orders==null||!orders.Apply(e))return;
        var previousState=State;
        if((e.State==OrderLifecycleState.Filled||e.FilledQuantity>e.DeliveredFilledQuantity)&&!executedOrders.Contains(e.ClientOrderId))awaitingFills.Add(e.ClientOrderId);
        if(e.State is OrderLifecycleState.Cancelled or OrderLifecycleState.Rejected)
        {if(e.ClientOrderId==entryId){entryId=null;entryCancelRequested=false;pendingSubmitted=null;}if(e.ClientOrderId==exitId){exitId=null;if(e.State==OrderLifecycleState.Rejected)Fault("平倉遭拒");}}
        FinishIfFlat();
        if(State!=previousState)Publish();
    }
    public void OnOrderSubmitted(PaperOrderRequest request)=>orders?.Track(request);
    public void NotifyRecordingFailure(Exception error){Fault("執行紀錄保存失敗："+error.Message);Publish();}
    public void OnHeartbeat(DateTimeOffset now){if(State!=StrategyLifecycleState.Stopped)Advance(now);}
    public IReadOnlyList<DateTimeOffset> GetPulseTimes(DateTimeOffset from,DateTimeOffset to)
    {if(to<from)return [];var result=new List<DateTimeOffset>();if((to-from).TotalSeconds>600)return [to];for(var at=GridAfter(from);at<=to;at=at.AddSeconds(3))result.Add(at);return result;}
    public void Pause(string message){paused=true;State=StrategyLifecycleState.Suspended;reason=message;CancelEntry();Publish();}
    public void Resume(){if(closing||faulted)return;paused=false;State=StrategyLifecycleState.WarmingUp;reason="恢復，依資料健康決策";Publish();}
    public void Stop(string message)=>RequestClose(message);
    public void RequestRiskExit(string message)=>RequestClose(message);
    public void RequestClose(string message){closing=paused=true;reason=message;CancelEntry();actionAt=null;delayedAction=0;if(context!=null&&Position.NetQuantity!=0){State=StrategyLifecycleState.Suspended;Flatten();}else FinishIfFlat();Publish();}
    void FinishIfFlat(){if(closing&&State!=StrategyLifecycleState.Stopped&&context!=null&&Position.NetQuantity==0&&entryId==null&&exitId==null&&awaitingFills.Count==0){
        if((orders?.ActiveClientOrderIds.Count??0)>0){CancelAllRequested?.Invoke();if((orders?.ActiveClientOrderIds.Count??0)>0)return;}
        State=StrategyLifecycleState.Stopped;actor?.Dispose();actor=null;
    }}
    void Publish(){
        var s=GetSnapshot();SnapshotChanged?.Invoke(s);
        DecisionObserved?.Invoke(new(){StrategyType=StrategyType,StrategyInstanceId=InstanceId,Timestamp=s.UpdatedAt,Symbol=s.TradableSymbol,State=State,
            Decision=State==StrategyLifecycleState.WarmingUp?StrategyDecision.Warmup:closing||faulted?StrategyDecision.Halt:reason.StartsWith("SESSION_CLOSE")?StrategyDecision.Reject:pending!=null?StrategyDecision.Accept:StrategyDecision.Hold,
            PrimaryReasonCode=reason.Split('：',' ')[0],Features=s.Features});
    }
    public StrategySnapshot GetSnapshot()=>new(){StrategyType=StrategyType,InstanceId=InstanceId,State=State,Reason=reason,ParameterSummary=string.Join(";",parameters.ToDictionary().Select(x=>$"{x.Key}={x.Value}")),TradableProductId="225MC",TradableSymbol=context?.Universe.Tradable.Symbol??"",ReferenceSummary="225M",StopPrice=stopPrice,UpdatedAt=observedAt==default?context?.Clock.Now??DateTimeOffset.MinValue:observedAt,Features=new Dictionary<string,decimal?>(features),Diagnostics=new Dictionary<string,string>{["ModelId"]=parameters.EffectiveModelId,["Readiness"]=NikkeiRlParameters.ReadinessProfile,["Session"]=sessionName,["ActionLatencyMs"]=parameters.ActionLatencyMs.ToString(System.Globalization.CultureInfo.InvariantCulture),["InferenceMs"]=inferenceMs.ToString("F4",System.Globalization.CultureInfo.InvariantCulture),["SummaryOnly"]="true",["LiveTradingEnabled"]=parameters.LiveTradingEnabled.ToString(),["DeploymentEligible"]="false"}};
}
