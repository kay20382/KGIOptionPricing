using KGIOptionPricing.StrategyFramework;
using KGIOptionPricing.StrategyFramework.MarketData;
namespace KGIOptionPricing.Strategies.TradeFlowTrend;

public sealed class TradeFlowTrendStrategy:ISimulationStrategy,IManagedOrderStrategy,IManagedOrderSubmissionObserver,
    IStrategyHeartbeat,IStrategyHeartbeatWhilePaused,IStrategyScheduledPulseSource,IStrategyDecisionSource,IStrategyRiskExit,IStrategyCloseRequest,IStrategyResumeGate
{
    private readonly StrategyDefinition _definition;
    private readonly TradeFlowParameters _p;
    private readonly TradeFlowSessionSchedule _schedule;
    private CausalBookTracker _causal=new();
    private readonly Dictionary<string,BookReference> _books=new();
    private readonly Dictionary<string,(decimal Price,DateTimeOffset At)> _lastTrades=new();
    private readonly Dictionary<string,SampledPriceTrend> _trends=new();
    private TradeFlowWindowSet? _windows;
    private TradeBarBuilder _bars;
    private ParabolicSarTracker _sar;
    private StrategyRuntimeContext? _context;
    private TradeFlowOrderCoordinator? _coordinator;
    private DateTimeOffset? _lastPulse,_eventTime;
    private string? _sessionId;
    private TradeFlowSessionWindow? _session;
    private bool _paused,_stopping,_sessionEnded,_armed,_processing,_again,_eligible;
    private bool _recoveryRequired,_acceptedThisSample;
    private int _direction;
    private long _observation;
    private TradeFlowExitReason _externalExit;
    private string _reason="尚未啟動";
    private Dictionary<string,decimal?> _features=new();
    private DateTimeOffset _now;
    private string? _published;
    public event Action<StrategyFramework.Execution.LiveStrategyCheckpoint>? CheckpointChanged;
    public string StrategyType=>TradeFlowTrendFactory.TypeName;
    public string InstanceId=>_definition.InstanceId;
    public StrategyLifecycleState State{get;private set;}=StrategyLifecycleState.Created;
    public bool ShouldPulseWhilePaused=>true;
    public bool CanResume=>!_stopping && !_recoveryRequired;
    public event Action<PaperOrderRequest>? OrderRequested;
    public event Action? CancelAllRequested;
    public event Action<string>? CancelOrderRequested;
    public event Action<StrategySnapshot>? SnapshotChanged;
    public event Action<StrategyDecisionObservation>? DecisionObserved;
    public TradeFlowTrendStrategy(StrategyDefinition definition)
    {_definition=definition;_p=TradeFlowParameters.Parse(definition.Parameters);_schedule=new(_p);_bars=new(_p.SarBarSeconds);_sar=new(_p.SarStart,_p.SarStep,_p.SarMax);}
    public void Start(StrategyRuntimeContext context)
    {
        if(context.Universe.Tradable.ProductId!="225MC" || context.Universe.Tradable.TickSize<=0 ||
            !new[]{"225M","225"}.All(id=>context.Universe.References.Any(p=>p.ProductId==id)))throw new ArgumentException("需要 225MC、225M、225");
        _context=context;_coordinator=new(InstanceId,context.StrategyRunGeneration,context.Universe.Tradable,_p);
        State=StrategyLifecycleState.WarmingUp;_reason="等待行情暖機";Publish();
    }
    private PositionSnapshot Position=>_context!.Performance.GetPosition(InstanceId,_context.Universe.Tradable.Symbol);
    private void Initialize(DateTimeOffset at)
    {
        _windows=new(_p,at);_trends.Clear();_lastTrades.Clear();_books.Clear();_causal=new();
        foreach(var id in new[]{"225","225M","225MC"})_trends[id]=new(_p.AverageKind,_p.FastSeconds,_p.SlowSeconds);
        _bars=new(_p.SarBarSeconds);_sar.Reset();_lastPulse=TradeFlowWindow.Floor(at,1);_eligible=false;_direction=0;
        _windows.RiClosed+=(id,seconds,sample)=>DecisionObserved?.Invoke(new(){Sequence=++_observation,Timestamp=sample.End,
            StrategyType=StrategyType,StrategyInstanceId=InstanceId,Symbol=id,State=State,Decision=StrategyDecision.Hold,
            Kind=StrategyObservationKind.RiClosed,PrimaryReasonCode=sample.Partial?"PartialRi":"RiClosed",
            SampleKey=$"{id}:{seconds}:{sample.End:O}",
            Features=new Dictionary<string,decimal?>{{"WindowSeconds",seconds},{"Ri",sample.Value}}});
    }
    private void Session(DateTimeOffset at)
    {
        var w=_schedule.Resolve(at);
        if(w!=null && w.SessionId!=_sessionId)
        {
            bool pending=Position.NetQuantity!=0 || _coordinator!.HasUnsettledOrders;
            _session=w;_sessionId=w.SessionId;_sessionEnded=pending;
            Initialize(at);
        }
        if(_session!=null && at>=_session.Stop)_sessionEnded=true;
    }
    public void OnMarketEvent(MarketEvent e)
    {
        if(_context==null || State==StrategyLifecycleState.Stopped)return;
        if((_eventTime.HasValue && e.Timestamp<_eventTime) ||
           (_lastPulse.HasValue && e.Timestamp<_lastPulse.Value))
        {_eligible=false;_reason="OutOfOrderEvent";return;}
        _eventTime=e.Timestamp;_now=e.Timestamp;
        Session(e.Timestamp);if(_windows==null)Initialize(e.Timestamp);
        OnHeartbeat(e.Timestamp);
        if(e.Kind==MarketEventKind.Connection)
        {_paused=true;_recoveryRequired=true;Initialize(e.Timestamp);_reason="重連後需對帳並明確恢復";State=StrategyLifecycleState.Suspended;PersistCheckpoint();Publish();return;}
        e=_causal.Observe(e);
        if(e.ProvenanceWarning=="DuplicateOrOutOfOrder")return;
        if(!_trends.ContainsKey(e.ProductId))return;
        if(e.Kind==MarketEventKind.Book)
        {
            _books.Remove(e.ProductId);
            if(e.BestBid>0 && e.BestAsk>e.BestBid)_books[e.ProductId]=new(e.Symbol,e.Timestamp,e.Sequence,e.ConnectionGeneration,e.BestBid,e.BestAsk,e.BidQuantities.FirstOrDefault(),e.AskQuantities.FirstOrDefault());
        }
        if(e.Kind==MarketEventKind.Trade && e.TradeQuantity>0 && e.LastPrice>0)
        {
            _windows!.Add(e.ProductId,new(e.Timestamp,TradeFlowClassifier.Classify(e,TimeSpan.FromMilliseconds(_p.MaxBookAgeMs)),e.TradeQuantity));
            _lastTrades[e.ProductId]=(e.LastPrice,e.Timestamp);
            if(e.ProductId=="225MC")foreach(var bar in _bars.Add(e.Timestamp,e.LastPrice,e.TradeQuantity))_sar.Add(bar);
        }
        Evaluate(false);Publish();
    }
    public void OnHeartbeat(DateTimeOffset now)
    {
        if(_context==null || State==StrategyLifecycleState.Stopped || _windows==null)return;
        if(_lastPulse.HasValue && now<_lastPulse)return;
        var end=TradeFlowWindow.Floor(now,1);
        for(var at=_lastPulse!.Value.AddSeconds(1);at<=end;at=at.AddSeconds(1))
        {
            _now=at;Session(at);_windows!.Advance(at);
            foreach(var id in _trends.Keys)
            {
                var valid=_lastTrades.TryGetValue(id,out var trade) && trade.At<at && at-trade.At<=TimeSpan.FromSeconds(_p.MaxTradeAgeSeconds);
                _trends[id].Sample(at,valid?trade.Price:null);
            }
            foreach(var bar in _bars.Advance(at))_sar.Add(bar);
            BuildFeatures(at);
            _direction=TradeFlowSignalRules.EntryDirection(Inputs(_p.EntryWindowSeconds),_p);
            _eligible=DataReady(at) &&
                _session!=null && _schedule.Enabled(_session) && at>=_session.Start && at<_session.Stop && !_sessionEnded;
            _acceptedThisSample=false;Evaluate(true);Observe();_lastPulse=at;Publish(true);
        }
        _now=now;Session(now);Evaluate(false);
    }
    private bool Fresh(BookReference b,DateTimeOffset at)=>b.Timestamp<=at && at-b.Timestamp<=TimeSpan.FromMilliseconds(_p.MaxBookAgeMs);
    private bool DataReady(DateTimeOffset at,bool requireHistory=true)=>_books.Count==3 && _books.Values.All(b=>Fresh(b,at)) &&
        (_books.Values.Max(b=>b.Timestamp)-_books.Values.Min(b=>b.Timestamp)).TotalMilliseconds<=_p.MaxCrossBookSkewMs &&
        _trends["225M"].Current.Ready && (!requireHistory || !_p.RequireFullHistory || new[]{"225M","225"}.All(id=>_windows!.Get(id,_p.EntryWindowSeconds,at).FullHistory));
    private FlowInputs Inputs(int w)=>new(_trends["225M"].Current.Fast,_trends["225M"].Current.Slow,
        Feature($"225M.{w}.Real"),Feature($"225M.{w}.Avg"),Feature($"225.{w}.Real"),Feature($"225.{w}.Avg"));
    private decimal? Feature(string key)=>_features.GetValueOrDefault(key);
    private void BuildFeatures(DateTimeOffset at)
    {
        var values=new Dictionary<string,decimal?>();
        foreach(var id in new[]{"225","225M","225MC"})
        {
            foreach(var w in new[]{30,60,180})
            {
                var s=_windows!.Get(id,w,at);string key=$"{id}.{w}.";
                values[key+"Up"]=s.Up;values[key+"Zero"]=s.Zero;values[key+"Dn"]=s.Down;values[key+"Real"]=s.Real;
                values[key+"Avg"]=s.Average;values[key+"Max"]=s.Maximum;values[key+"Min"]=s.Minimum;
                values[key+"ValidSamples"]=s.ValidSamples;values[key+"TargetSamples"]=s.TargetSamples;values[key+"UnknownVolume"]=s.UnknownVolume;
                values[key+"ClassifiedVolume"]=s.ClassifiedVolume;
                values[key+"UnknownPercent"]=s.ClassifiedVolume+s.UnknownVolume==0?null:100m*s.UnknownVolume/(s.ClassifiedVolume+s.UnknownVolume);
            }
            values[id+".Fast"]=_trends[id].Current.Fast;values[id+".Slow"]=_trends[id].Current.Slow;
            values[id+".BookAgeMs"]=_books.TryGetValue(id,out var b)?(decimal)(at-b.Timestamp).TotalMilliseconds:null;
        }
        values["225MC.Sar"]=_sar.Current?.Value;values["225MC.SarArmed"]=_armed?1:0;
        var position=Position;values["PositionAverage"]=position.NetQuantity==0?null:position.AveragePrice;
        values["StopTrigger"]=position.NetQuantity==0?null:position.AveragePrice-Math.Sign(position.NetQuantity)*_p.StopTicks*_context!.Universe.Tradable.TickSize;
        values["ProfitTrigger"]=position.NetQuantity==0?null:position.AveragePrice+Math.Sign(position.NetQuantity)*_p.ProfitTicks*_context!.Universe.Tradable.TickSize;
        values["SarAgeSeconds"]=_sar.Current is {} sar?(decimal)(at-sar.BarEnd).TotalSeconds:null;_features=values;
    }
    private void Evaluate(bool sample)
    {
        if(_context==null || _coordinator==null || _windows==null)return;
        if(_processing){_again=true;return;}
        _processing=true;
        try
        {
            int attempts=0;bool exitStarted=false;
            do
            {
                _again=false;var position=Position;_books.TryGetValue("225MC",out var b);
                var exits=_externalExit|(_sessionEnded?TradeFlowExitReason.SessionEnd:0);
                if(position.NetQuantity==0)_armed=false;
                if(position.NetQuantity!=0 && b!=null && Fresh(b,_now))
                {
                    int sign=Math.Sign(position.NetQuantity);decimal px=sign>0?b.Bid:b.Ask;
                    decimal profit=sign*(px-position.AveragePrice)/_context.Universe.Tradable.TickSize;
                    if(profit<= -_p.StopTicks)exits|=TradeFlowExitReason.StopLoss;
                    if(profit>=_p.ProfitTicks)exits|=TradeFlowExitReason.TakeProfit;
                    if(profit>=_p.SarActivationTicks)_armed=true;
                    if(_armed && _sar.Current is {} sar && _now>=sar.BarEnd && _now-sar.BarEnd<=TimeSpan.FromSeconds(_p.SarBarSeconds*_p.SarMaxAgeBars) &&
                        TradeFlowSignalRules.SarExit(sign,px,sar))exits|=TradeFlowExitReason.Sar;
                    if(sample && DataReady(_now,false) && TradeFlowSignalRules.TrendExit(sign,Inputs(_p.ExitWindowSeconds),_p))exits|=TradeFlowExitReason.Trend;
                }
                // Synchronous fills may re-enter this loop; a completed exit needs a new signal sample.
                exitStarted|=position.NetQuantity!=0 && (exits!=0 || _coordinator.ExitLatch!=0);
                bool allowed=!exitStarted && !_paused && !_stopping && !_recoveryRequired && _eligible && DataReady(_now) && !_sessionEnded && _externalExit==0 &&
                    (sample || _coordinator.HasUnsettledOrders || _coordinator.EntryReplacementPending || position.NetQuantity!=0);
                var actions=_coordinator.Evaluate(_now,b,position,_direction,allowed,exits);
                _reason=_coordinator.ExitLatch!=0?$"出場 {_coordinator.ExitLatch} / {_coordinator.Phase}":
                    _recoveryRequired?"重連後待券商完整對帳，禁止新倉":_paused?"已暫停；持倉保護持續":
                    !_eligible?"暖機、資料或時段未就緒":_direction==0?"等待順勢訊號":$"順勢 {_direction} / {_coordinator.Phase}";
                State=_stopping && position.NetQuantity==0 && !_coordinator.HasUnsettledOrders?StrategyLifecycleState.Stopped:
                    _paused?StrategyLifecycleState.Suspended:_eligible?StrategyLifecycleState.Running:StrategyLifecycleState.WarmingUp;
                foreach(var action in actions)
                {
                    PersistCheckpoint();
                    if(action is SubmitFlowOrder send)
                    {if(sample && send.Request.Role is PaperOrderRole.EntryLong or PaperOrderRole.EntryShort)_acceptedThisSample=true;OrderRequested?.Invoke(send.Request);}
                    else if(action is CancelFlowOrder cancel)CancelOrderRequested?.Invoke(cancel.ClientOrderId);
                }
            }while(_again && ++attempts<8);
        }
        finally{_processing=false;}
    }
    private void Observe()
    {
        var quality=new List<string>();
        if(_books.Count<3)quality.Add("MissingBook");
        if(_books.Values.Any(b=>!Fresh(b,_now)))quality.Add("StaleBook");
        if(_features.Any(kv=>kv.Key.EndsWith(".UnknownVolume") && kv.Value>0))quality.Add("UnknownTradeClassification");
        if(!_trends["225M"].Current.Ready)quality.Add("MissingPriceSamples");
        if(_p.RequireFullHistory && new[]{"225","225M"}.Any(id=>_features.GetValueOrDefault($"{id}.{_p.EntryWindowSeconds}.ValidSamples")<_features.GetValueOrDefault($"{id}.{_p.EntryWindowSeconds}.TargetSamples")))quality.Add("IncompleteRiHistory");
        if(_recoveryRequired)quality.Add("BrokerReconciliationRequired");
        bool exit=_coordinator!.ExitLatch!=0;
        var decision=exit?StrategyDecision.Exit:_acceptedThisSample?StrategyDecision.Accept:
            _paused || _stopping || _recoveryRequired?StrategyDecision.Reject:
            Position.NetQuantity!=0 || _coordinator.HasUnsettledOrders?StrategyDecision.Hold:
            !_eligible?StrategyDecision.Warmup:StrategyDecision.Reject;
        var observation = new StrategyDecisionObservation {Sequence=++_observation,Timestamp=_now,StrategyType=StrategyType,
        StrategyInstanceId=InstanceId,Symbol=_context!.Universe.Tradable.Symbol,State=State,Features=new Dictionary<string,decimal?>(_features),
        Kind=StrategyObservationKind.FeatureSample,SampleKey=_now.ToString("O"),CandidateId=$"{InstanceId}:{_now:O}",Decision=decision,
        DataQualityFlags=quality.ToArray(),RequiredProductsPresent=_books.Count==3,
        IsCandidate=_direction!=0,PrimaryReasonCode=_reason,Direction=_direction.ToString(System.Globalization.CultureInfo.InvariantCulture),
        Thresholds=new[]{new ThresholdObservation("EntryDirection",Feature($"225M.{_p.EntryWindowSeconds}.Real"),_p.EntryDirectionThreshold,"percentage-point",_direction!=0),
            new ThresholdObservation("EntryAverageMargin",Feature($"225M.{_p.EntryWindowSeconds}.Real")-Feature($"225M.{_p.EntryWindowSeconds}.Avg"),_p.EntryAverageMargin,"percentage-point",_direction!=0),
            new ThresholdObservation("DataReady",_eligible?1:0,1,"bool",_eligible)}};
        DecisionObserved?.Invoke(observation);
        DecisionObserved?.Invoke(observation with { Kind=StrategyObservationKind.Decision, Sequence=++_observation });
    }
    public void OnSubmitted(PaperOrderRequest request)=>_coordinator!.OnSubmitted(request);
    public void OnOrderSubmitted(PaperOrderRequest request)=>OnSubmitted(request);
    public void OnOrderLifecycle(OrderLifecycleEvent e){_coordinator!.OnLifecycle(e);PersistCheckpoint();Evaluate(false);Publish();}
    public void OnExecution(ExecutionEvent e){PersistCheckpoint();Evaluate(false);Publish();}
    public void Pause(string reason){_paused=true;_reason=reason;Evaluate(false);Publish();}
    public void Resume(){if(!_stopping && !_recoveryRequired){_paused=false;_reason="重新檢查資料與時段";}Publish();}
    public void Stop(string reason){_stopping=true;_paused=true;_reason=reason;Evaluate(false);if(_coordinator is null)CancelAllRequested?.Invoke();Publish();}
    public void RequestRiskExit(string reason){_paused=true;_externalExit|=TradeFlowExitReason.RiskLimit;_reason=reason;Evaluate(false);Publish();}
    public void RequestClose(string reason){_stopping=true;RequestRiskExit(reason);}
    public IReadOnlyList<DateTimeOffset> GetPulseTimes(DateTimeOffset from,DateTimeOffset to)
    {var list=new List<DateTimeOffset>();for(var at=TradeFlowWindow.Floor(from,1).AddSeconds(1);at<=to;at=at.AddSeconds(1))list.Add(at);return list;}
    public FlowCoordinatorSnapshot? ExportCoordinator()=>_coordinator?.Export();
    public StrategySnapshot GetSnapshot()=>new(){StrategyType=StrategyType,InstanceId=InstanceId,State=State,Reason=_reason,
        StrategyRunGeneration=_context?.StrategyRunGeneration??0,TradableProductId="225MC",TradableSymbol=_context?.Universe.Tradable.Symbol??"",
        ReferenceSummary="225M、225",ParameterSummary=string.Join(";",_p.ToDictionary().Select(x=>$"{x.Key}={x.Value}")),
        Features=new Dictionary<string,decimal?>(_features),UpdatedAt=_now,LastPrice=_lastTrades.GetValueOrDefault("225MC").Price,
        Diagnostics=new Dictionary<string,string>{{"Session",_sessionId??"--"},{"Phase",_coordinator?.Phase.ToString()??"--"},
            {"ExitReasons",(_coordinator?.ExitLatch??0).ToString()},{"SarArmed",_armed.ToString()},
            {"SarStatus",!_armed?"尚未達啟動獲利":_sar.Current is not {} sar?"等待完整K棒":_now-sar.BarEnd>TimeSpan.FromSeconds(_p.SarBarSeconds*_p.SarMaxAgeBars)?"K棒過期":"有效"},
            {"ParameterHash",TradeFlowParameterStore.Hash(_p)},
            {"RemainingQuantity",(_context==null?0:Math.Abs(Position.NetQuantity)).ToString()}}};
    private void PersistCheckpoint()
    {
        if(_context==null || _coordinator==null || CheckpointChanged==null)return;
        CheckpointChanged.Invoke(new(InstanceId,_context.StrategyRunGeneration,1,StrategyType,TradeFlowParameterStore.Hash(_p),
            System.Text.Json.JsonSerializer.SerializeToElement(new{Parameters=_p.ToDictionary(),Coordinator=_coordinator.Export(),SarArmed=_armed,
                Sar=_sar.Current,Session=_session,SessionEnded=_sessionEnded,Position=Position,RequiresWarmup=true,RequiresBrokerReconciliation=true}),_now));
    }
    private void Publish(bool force=false)
    {
        string signature=$"{State}|{_reason}|{_coordinator?.Phase}|{(_context==null?0:Position.NetQuantity)}";
        if(!force && signature==_published)return;
        _published=signature;SnapshotChanged?.Invoke(GetSnapshot());
    }
}
