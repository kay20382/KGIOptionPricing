using System.Globalization;
using KGIOptionPricing.OrderManagement.Core;

using KGIOptionPricing.StrategyFramework;
namespace KGIOptionPricing.Strategies.MultiFactorMeanR17;

public sealed class MultiFactorMeanR17Factory : IStrategyFactory
{
    public const string TypeName="MultiFactorMeanR17";
    public string StrategyType=>TypeName;
    public string DisplayName=>"多因子均價回歸 R17";
    public ISimulationStrategy Create(StrategyDefinition definition)=>new MultiFactorMeanR17Strategy(definition);
}

public sealed class MultiFactorMeanR17Strategy : ISimulationStrategy,IManagedOrderStrategy,IManagedOrderSubmissionObserver,
    IStrategyHeartbeat,IStrategyScheduledPulseSource,IStrategyHeartbeatWhilePaused,IStrategyRiskExit,IStrategyCloseRequest,IStrategyResumeGate,IStrategyDecisionSource
{
    private sealed record Plan(int Direction,decimal Stop,decimal Target,string Cycle,DateTimeOffset Sent,decimal HoldingSeconds=0);
    private readonly StrategyDefinition _definition;
    private readonly R17BaseParameters _p;
    private readonly BuySellPowerStrategy _indicator;
    private readonly ReversalSignalGate _gate;
    private readonly AdaptiveParameterState _adaptive;
    private readonly IndicatorExitPolicy _indicatorExit;
    private readonly MultiFactorSignal _factorSignal;
    private readonly FactorState _factorState=new();
    private readonly bool _fillAnchoredRisk;
    private decimal _holdingLimit;
    private readonly Dictionary<string,MarketEvent> _books=new();
    private readonly Dictionary<string,MarketEvent> _entryBooks=new();
    private readonly HashSet<string> _retiredEntries=new();
    private WeightedMidEntryAssessment? _entryQuoteDecision;
    private PaperOrderType? _selectedEntryType;
    private bool _retryEntryAfterCancel;
    private readonly Dictionary<string,DateTimeOffset> _trades=new();
    private readonly Dictionary<string,long> _sequences=new();
    private readonly Dictionary<string,Plan> _plans=new();
    private readonly HashSet<string> _fills=new();
    private StrategyRuntimeContext? _context;
    private ManagedStrategyOrderBook? _orders;
    private DateTimeOffset _now,_entryAt;
    private DateTimeOffset? _oppositeSince,_publishedAt;
    private DateTimeOffset? _cooldownUntil;
    private long? _generation;
    private string _reason="尚未啟動",_reasonCode="CREATED",_cycle="",_entryClient="",_candidateId="";
    private int _armed,_cycleNumber,_entryDirection;
    private bool _paused,_closing,_entryWorking,_exitWorking,_entryExpired,_cancelSent;
    private decimal _stop,_target;
    private decimal? _validMicroPrice;
    private string _observationError="";
    private List<ThresholdObservation> _thresholds=new();
    public string StrategyType=>MultiFactorMeanR17Factory.TypeName;
    public string InstanceId=>_definition.InstanceId;
    public StrategyLifecycleState State {get;private set;}=StrategyLifecycleState.Created;
    public event Action<PaperOrderRequest>? OrderRequested;
    public event Action? CancelAllRequested {add{} remove{}}
    public event Action<string>? CancelOrderRequested;
    public event Action<StrategySnapshot>? SnapshotChanged;
    public event Action<StrategyDecisionObservation>? DecisionObserved;
    public bool ShouldPulseWhilePaused=>Position().NetQuantity!=0 || _entryWorking || _exitWorking;
    public bool CanResume=>!_closing && _observationError.Length==0 && Position().NetQuantity==0 && !_entryWorking && !_exitWorking && (_orders?.ActiveClientOrderIds.Count??0)==0;
    public MultiFactorMeanR17Strategy(StrategyDefinition definition)
    {
        _definition=definition=definition with{Parameters=R17Parameters.Parse(definition.Parameters).ToDictionary()};_p=R17BaseParameters.Parse(definition.Parameters);_gate=new(_p);_adaptive=new(_p);_indicatorExit=new(_p);_factorSignal=new(definition.Parameters,_factorState);_holdingLimit=_p["MaxHoldingSeconds"];
        decimal anchor=definition.Parameters.TryGetValue("ResearchAnchorRiskToFill",out var flag)?decimal.Parse(flag,CultureInfo.InvariantCulture):0;
        if(anchor is not (0 or 1))throw new ArgumentOutOfRangeException(nameof(definition),"Fill anchor must be zero or one");
        _fillAnchoredRisk=anchor==1;
        if(_fillAnchoredRisk && !_factorSignal.Enabled)throw new ArgumentException("Fill anchor is restricted to enabled research factors",nameof(definition));
        var parameters=_p.IndicatorParameters();
        if(definition.Parameters.TryGetValue("HistoryRoot",out var root))parameters["HistoryRoot"]=root;
        _indicator=new(definition with{StrategyType=BuySellPowerFactory.TypeName,Parameters=parameters});
    }
    public void Start(StrategyRuntimeContext context)
    {
        if(State!=StrategyLifecycleState.Created)throw new InvalidOperationException("策略已啟動");
        if(context.Universe.Tradable.TickSize!=5 || context.Universe.Tradable.ContractMultiplier!=10 || context.Universe.References.SingleOrDefault(p=>p.ProductId=="225")?.TickSize!=10 || context.Universe.References.SingleOrDefault(p=>p.ProductId=="225M")?.TickSize!=5)throw new ArgumentException("R17需標準225MC/225M/225商品規格");
        if(context.Universe.Tradable.ProductId!="225MC" || context.Universe.Tradable.TickSize<=0 || context.Universe.Tradable.ContractMultiplier<=0)throw new ArgumentException("交易商品必須為具有有效規格的225MC");
        _context=context;_orders=new(InstanceId,context.StrategyRunGeneration);_indicator.Start(context);
        State=StrategyLifecycleState.WarmingUp;SetReason("WARMUP","等待有效行情與歷史樣本");Publish(true);
    }
    private PowerSnapshot Core(string product)=>_indicator.GetPowerSnapshot(product);
    private PositionSnapshot Position()=>_context==null?new():_context.Performance.GetPosition(InstanceId,_context.Universe.Tradable.Symbol);
    private decimal Tick=>_context!.Universe.Tradable.TickSize;
    private static bool Valid(MarketEvent e)=>e.LastPrice>0 && e.TradeQuantity>0 && e.TradeQuantity%10!=0 && e.BestBid>0 && e.BestAsk>e.BestBid;
    public void OnMarketEvent(MarketEvent e)
    {
        if(_context==null || State==StrategyLifecycleState.Stopped)return;
        var product=_context.Universe.AllProducts.SingleOrDefault(x=>x.Symbol==e.Symbol);if(product==null)return;
        if(e.Timestamp<_now || (_generation.HasValue && e.ConnectionGeneration<_generation) ||
            (e.Sequence>0 && e.ConnectionGeneration==_generation && e.Sequence<=_sequences.GetValueOrDefault(e.Symbol)) || e.ProvenanceWarning=="DuplicateOrOutOfOrder")
        { _armed=0;_gate.Reset();SetReason("INVALID_ORDER","跳過晚到／重複行情");return; }
        bool reconnect=_generation.HasValue && e.ConnectionGeneration!=_generation || e.Kind==MarketEventKind.Connection;
        if(reconnect){_adaptive.Reset();_books.Clear();_entryBooks.Clear();_entryQuoteDecision=null;_selectedEntryType=null;_trades.Clear();_sequences.Clear();_validMicroPrice=null;_armed=0;_gate.Reset();if(Position().NetQuantity!=0 || _entryWorking)RequestRiskExit("行情重新連線");}
        _generation=e.ConnectionGeneration;_sequences[e.Symbol]=e.Sequence;_now=e.Timestamp;
        var before=Core("225MC");string oldSession=_indicator.GetPowerSession("225MC");
        _factorState.OnMarket(e with{ProductId=product.ProductId});
        _indicator.OnMarketEvent(e);
        var after=Core("225MC");string session=_indicator.GetPowerSession("225MC");
        if(session!=oldSession) {_adaptive.Reset();_armed=0;_gate.Reset();_trades.Clear();_books.Clear();_entryBooks.Clear();_entryQuoteDecision=null;_selectedEntryType=null;_validMicroPrice=null;if(Position().NetQuantity!=0 || _entryWorking)RequestRiskExit("跨交易時段");}
        if(e.BestBid>0 && e.BestAsk>e.BestBid)_books[product.ProductId]=e;
        // Never fabricate current depth quantities from a trade without them.
        // Keep invalid book updates too, so they block instead of hiding behind a last-good book.
        if(e.Kind==MarketEventKind.Book || e.BidQuantities.Length>0 && e.AskQuantities.Length>0)_entryBooks[product.ProductId]=e;
        bool valid=e.Kind==MarketEventKind.Trade && Valid(e);
        if(valid)_trades[product.ProductId]=e.Timestamp;
        if(valid && product.ProductId=="225MC")_validMicroPrice=e.LastPrice;
        _gate.Adaptive=_adaptive.GetThresholds(_now);
        if(valid && product.ProductId=="225MC" && _adaptive.NeedsSample(_now) && Healthy(true))_adaptive.Observe(_now,Core("225M"),after);
        if(Position().NetQuantity!=0) {ManagePosition();Publish();return;}
        if(_entryWorking){SetReason("ENTRY_WORKING","進場意圖等待穿價／成交");CheckEntryTimeout();Publish();return;}
        if(_paused || _closing || _exitWorking) {TryFinishClose();Publish();return;}
        // Multifactors make decisions only on fresh eligible micro trades.
        // Books were already updated; protective exits/working-order expiry above
        // still run on every event. This skips redundant flat book health scans.
        if(_factorSignal.Enabled && (!valid || product.ProductId!="225MC")){Publish();return;}
        bool healthy=Healthy();
        if(_factorSignal.Enabled)
        {
            bool cooldown=!_cooldownUntil.HasValue || _now>=_cooldownUntil;
            if(valid && product.ProductId=="225MC")
            {
                int direction=_factorSignal.Evaluate(_now,Core("225M"),after,healthy && cooldown);
                _thresholds.AddRange(_factorSignal.LastThresholds);
                if(direction!=0){_candidateId=InstanceId+":factors:"+_now.UtcTicks;TryContinuousEntry(direction);_factorSignal.Reset();}
                else if(healthy)SetReason(cooldown?"WAIT_FACTOR":"COOLDOWN","研究多因子訊號等待成立");
            }
            State=_entryWorking?StrategyLifecycleState.Running:after.IsWarm?StrategyLifecycleState.Ready:StrategyLifecycleState.WarmingUp;Publish();return;
        }
        if(_p["SignalMode"]==1)
        {
            bool cooldown=!_cooldownUntil.HasValue || _now>=_cooldownUntil;
            _thresholds.Add(new("CooldownRemaining",_cooldownUntil.HasValue?Math.Max(0,(decimal)(_cooldownUntil.Value-_now).TotalSeconds):0,0,"seconds",cooldown));
            _armed=_gate.Update(_now,Core("225M"),after,healthy && cooldown,_validMicroPrice,Tick);
            int direction=after.BuyPower>=0?1:-1;DescribeSignal(direction,after);
            if(_armed!=0 && valid && product.ProductId=="225MC")
            {
                _candidateId=InstanceId+":power:"+_now.UtcTicks;TryContinuousEntry(_armed);_armed=0;_gate.Reset();
            }
            else if(healthy)SetReason(cooldown?"WAIT_POWER":"COOLDOWN",cooldown?"等待兩商品連續力道與有效微型成交":"平倉後冷卻中");
            State=_entryWorking?StrategyLifecycleState.Running:after.IsWarm?StrategyLifecycleState.Ready:StrategyLifecycleState.WarmingUp;Publish();return;
        }
        bool reversed=valid && product.ProductId=="225MC" && before.CurrentMovement?.Direction is "上漲" or "下跌" &&
            after.CurrentMovement?.Direction!=before.CurrentMovement?.Direction && after.CurrentMovement?.Direction is "上漲" or "下跌";
        if(reversed)
        {
            int direction=after.CurrentMovement!.Direction=="上漲"?1:-1;
            DescribeSignal(direction,before);
            int armed=_armed;_armed=0;_gate.Reset();
            if(healthy && armed==direction && _gate.PowerMatches(Core("225M"),direction) && _gate.PowerMatches(after,direction) && _gate.ConsensusMatches(Core("225M"),after,direction))
                TryEntry(direction,before,after);
            else SetReason("REVERSAL_UNARMED","價格反轉但力道持續／資料條件未完成");
        }
        else
        {
            // A qualifying fresh trade is necessary; a timer or ignored quantity cannot arm/enter.
            _armed=_gate.Update(_now,Core("225M"),after,healthy);
            int direction=after.CurrentMovement?.Direction=="下跌"?1:-1;DescribeSignal(direction,after);
            if(after.CurrentMovement?.Direction is "上漲" or "下跌")_candidateId=InstanceId+":"+after.CurrentMovement.StartTime.UtcTicks+":"+direction;
            if(_armed!=0)SetReason("ARMED","力道持續成立，等待價格反轉確認");
            else if(healthy)SetReason("WAIT_SIGNAL","等待段長、方向與持續力道條件");
        }
        State=_entryWorking?StrategyLifecycleState.Running:after.IsWarm?StrategyLifecycleState.Ready:StrategyLifecycleState.WarmingUp;Publish(reversed);
    }
    private bool Healthy(bool sampleOnly=false)
    {
        if(!sampleOnly)_thresholds=new();
        bool Check(string name,decimal? observed,decimal threshold,string unit,bool pass){if(!sampleOnly)_thresholds.Add(new(name,observed,threshold,unit,pass));return pass;}
        bool ok=true;
        foreach(string id in new[]{"225M","225MC"})
        {
            var s=Core(id);decimal? age=_books.TryGetValue(id,out var b)?(decimal)(_now-b.Timestamp).TotalSeconds:null;
            decimal? tradeAge=_trades.TryGetValue(id,out var t)?(decimal)(_now-t).TotalSeconds:null;
            ok &= Check(id+".Warm",s.IsWarm?1:0,1,"bool",s.IsWarm);
            ok &= Check(id+".BookAge",age,_p["MaxDataAgeSeconds"],"seconds",age>=0 && age<=_p["MaxDataAgeSeconds"]);
            ok &= Check(id+".TradeAge",tradeAge,_p["MaxDataAgeSeconds"],"seconds",tradeAge>=0 && tradeAge<=_p["MaxDataAgeSeconds"]);
            ok &= Check(id+".Quantity",s.TotalQuantity,_p["MinQuantity"],"lots",s.TotalQuantity>=_p["MinQuantity"]);
            decimal count=_indicator.GetPowerTradeCount(id);
            ok &= Check(id+".TradeCount",count,_p["MinTradeCount"],"trades",count>=_p["MinTradeCount"]);
        }
        decimal? skew=_books.ContainsKey("225M") && _books.ContainsKey("225MC")?(decimal)Math.Abs((_books["225M"].Timestamp-_books["225MC"].Timestamp).TotalSeconds):null;
        ok &= Check("ReceiveSkew",skew,_p["MaxSkewSeconds"],"seconds",skew<=_p["MaxSkewSeconds"]);
        decimal? spread=_books.TryGetValue("225MC",out var micro)?(micro.BestAsk-micro.BestBid)/Tick:null;
        ok &= Check("Spread",spread,_p["MaxSpreadTicks"],"ticks",spread>0 && spread<=_p["MaxSpreadTicks"]);
        bool session=SessionRemaining()>_p["CloseBeforeSeconds"];
        ok &= Check("SessionRemaining",SessionRemaining(),_p["CloseBeforeSeconds"],"seconds",session);
        if(_p["EntryExecutionMode"]==1 && !sampleOnly){var q=AssessEntryQuotes();
            ok &= Check("WeightedEntryQuotes",q.Ready?1:0,1,"bool",q.Ready);}
        if(!ok && !sampleOnly)SetReason("DATA_GATE","暖機、成交量、新鮮度、價差或時段未達門檻");
        return ok;
    }
    private decimal SessionRemaining()
    {
        var local=_now.ToOffset(TimeSpan.FromHours(8));var time=local.TimeOfDay;DateTime end;
        if(time>=new TimeSpan(7,45,0)&&time<new TimeSpan(14,45,0))end=local.Date.AddHours(14).AddMinutes(45);
        else if(time>=new TimeSpan(16,0,0))end=local.Date.AddDays(1).AddHours(5);
        else if(time<new TimeSpan(5,0,0))end=local.Date.AddHours(5);
        else return 0;
        return (decimal)(new DateTimeOffset(end,TimeSpan.FromHours(8))-_now).TotalSeconds;
    }
    private void DescribeSignal(int direction,PowerSnapshot micro)
    {
        var mini=Core("225M");
        decimal? joint=(mini.BuyPower+micro.BuyPower)*direction/2;
        decimal? difference=ReversalSignalGate.PowerDifference(mini,micro);
        if(_gate.JointThreshold>0)_thresholds.Add(new("JointBuyPower",joint,_gate.JointThreshold,"percent",joint>=_gate.JointThreshold));
        if(_gate.DifferenceThreshold<200)_thresholds.Add(new("PowerDifference",difference,_gate.DifferenceThreshold,"percentage points",difference<=_gate.DifferenceThreshold));
        if(_p["MinShortAveragePower"]>0)foreach(string id in new[]{"225M","225MC"}){
            decimal? sustained=(id=="225M"?mini:micro).ShortAverage*direction;
            _thresholds.Add(new(id+".SustainedPower",sustained,_p["MinShortAveragePower"],"percent",sustained>=_p["MinShortAveragePower"]));
        }
        if(_p["SignalMode"]==1 && _p["RequireOppositeMovement"]==1){
            int movement=micro.CurrentMovement?.Direction=="上漲"?1:micro.CurrentMovement?.Direction=="下跌"?-1:0;
            _thresholds.Add(new("MovementOpposesPower",movement*direction,-1,"direction",movement*direction==-1));
        }
        if(_p["SignalMode"]==1 && _p["RequireAlignedMovement"]==1){
            int movement=micro.CurrentMovement?.Direction=="上漲"?1:micro.CurrentMovement?.Direction=="下跌"?-1:0;
            _thresholds.Add(new("MovementAlignedPower",movement*direction,1,"direction",movement*direction==1));
        }
        if(_p["SignalMode"]==1 && _p["PriceRecoveryTicks"]>0){
            decimal? recovery=ReversalSignalGate.RecoveryTicks(micro,direction,_validMicroPrice,Tick);
            _thresholds.Add(new("PriceRecoveryTicks",recovery,_p["PriceRecoveryTicks"],"ticks",recovery>=_p["PriceRecoveryTicks"]));
        }
        foreach(string id in new[]{"225M","225MC"}){
            var s=id=="225MC"?micro:Core(id);
            _thresholds.Add(new(id+".BuyPower",s.BuyPower*direction,_gate.PowerThreshold,"percent",s.BuyPower*direction>=_gate.PowerThreshold));
            if(_p["RequireShortAverageDirection"]==1)
                _thresholds.Add(new(id+".ShortAverage",s.ShortAverage*direction,0,"percent",s.ShortAverage*direction>0));
            _thresholds.Add(new(id+".BaisPower",s.BaisPower*direction,_p["BaisThreshold"],"percentage points",s.BaisPower*direction>=_p["BaisThreshold"]));
        }
        if(_p["SignalMode"]==0){
        _thresholds.Add(new("UpSamples",micro.UpSamples,_p["MinMovementSamples"],"segments",micro.UpSamples>=_p["MinMovementSamples"]));
        _thresholds.Add(new("DownSamples",micro.DownSamples,_p["MinMovementSamples"],"segments",micro.DownSamples>=_p["MinMovementSamples"]));
        decimal? required=(direction>0?micro.AverageDownPoints:micro.AverageUpPoints)*_p["MovementRatio"];
        _thresholds.Add(new("MovementPoints",micro.CurrentMovement?.Points,required,"points",required.HasValue && micro.CurrentMovement?.Points>=required));
        }
        decimal seconds=_gate.ElapsedSeconds(_now);_thresholds.Add(new("ConfirmSeconds",seconds,_p.ConfirmSeconds,"seconds",seconds>=_p.ConfirmSeconds));
    }
    private void TryEntry(int direction,PowerSnapshot before,PowerSnapshot after)
    {
        var book=_books["225MC"];decimal extreme=after.CurrentMovement!.StartPrice;
        decimal average=(direction>0?before.AverageUpPoints:before.AverageDownPoints)??0;
        decimal price=Align(direction>0?book.BestAsk:book.BestBid,direction>0);
        decimal stop=Align(extreme-direction*_p["StopBufferTicks"]*Tick,direction<0);
        decimal target=Align(extreme+direction*average,direction<0);
        decimal risk=(price-stop)*direction;
        decimal costs=2*_p["CommissionPerSide"]/_context!.Universe.Tradable.ContractMultiplier+_p["SlippageTicks"]*Tick;
        decimal reward=(target-price)*direction-costs;
        if(risk>0 && reward<risk*_p.MinRewardRisk && _p["AllowRiskRewardPullback"]==1)
        {
            decimal boundary=(target-direction*costs+_p.MinRewardRisk*stop)/(1+_p.MinRewardRisk);
            // Round toward a better entry, preserving fixed stop/target and net reward/risk.
            price=Align(boundary,direction<0);
            risk=(price-stop)*direction;reward=(target-price)*direction-costs;
        }
        _thresholds.Add(new("NetRewardRisk",risk>0?reward/risk:null,_p.MinRewardRisk,"ratio",risk>0 && reward>=risk*_p.MinRewardRisk));
        if(stop<=0 || target<=0 || risk<=0 || reward<risk*_p.MinRewardRisk){SetReason("INSUFFICIENT_ROOM","扣成本後剩餘空間不足，跳過此段");return;}
        _cycle="reversal-"+(++_cycleNumber);_entryDirection=direction;_stop=stop;_target=target;_entryExpired=false;_cancelSent=false;_entryWorking=true;
        SetReason("ENTRY_ACCEPT","力道與價格反轉確認，發出符合淨報酬風險比的一口限價進場意圖");
        Emit(direction>0?BuySell.Buy:BuySell.Sell,PaperOrderType.Limit,direction>0?PaperOrderRole.EntryLong:PaperOrderRole.EntryShort,price,1,new(direction,stop,target,_cycle,_now,AdaptiveParameterState.HoldingSeconds(Core("225MC"),direction,_p)));
    }
    private decimal Align(decimal value,bool up)=> (up?Math.Ceiling(value/Tick):Math.Floor(value/Tick))*Tick;
    private WeightedMidEntryAssessment AssessEntryQuotes(int direction=0)
    {
        var large=_context!.Universe.References.FirstOrDefault(p=>p.ProductId=="225");
        if(large==null || string.IsNullOrWhiteSpace(large.ContractMonth) ||
            !string.Equals(large.ContractMonth,_context.Universe.Tradable.ContractMonth,StringComparison.OrdinalIgnoreCase))
            return new(false,false,null,null,"ENTRY_QUOTE_CONTRACT");
        return WeightedMidEntryPolicy.Evaluate(_now,_entryBooks.GetValueOrDefault("225MC"),_entryBooks.GetValueOrDefault("225"),
            Tick,large.TickSize,_p["MaxDataAgeSeconds"],_p["MaxSkewSeconds"],(int)_p["WeightedMidMode"],direction);
    }
    private void TryContinuousEntry(int direction)
    {
        var book=_books["225MC"];var type=PaperOrderType.Market;
        if(_p["EntryExecutionMode"]==1){
            _entryQuoteDecision=AssessEntryQuotes(direction);
            if(!_entryQuoteDecision.Ready){SetReason(_entryQuoteDecision.ReasonCode,"大型／微型加權中價資料尚未就緒");return;}
            book=_entryBooks["225MC"];type=_entryQuoteDecision.UseMarket?PaperOrderType.Market:PaperOrderType.BrokerLimit;
        }
        bool passive=type==PaperOrderType.BrokerLimit;
        decimal price=Align(direction>0?(passive?book.BestBid:book.BestAsk):(passive?book.BestAsk:book.BestBid),direction>0);
        decimal risk=_p["StopTicks"]*Tick,targetDistance=_p["TargetTicks"]*Tick;
        if(_factorSignal.Enabled && _definition.Parameters.TryGetValue("ResearchVolatilityRiskMultiplier",out var volatilityMultiplier))
        {
            if(_p["AdaptiveExitMode"]==1)throw new InvalidOperationException("Choose one research risk model");
            decimal Param(string name,decimal fallback)=>_definition.Parameters.TryGetValue(name,out var text)?decimal.Parse(text,System.Globalization.CultureInfo.InvariantCulture):fallback;
            var distances=ResearchRiskDistances.Calculate(_factorState.Read("225MC",_now,Tick)?.TickVolatility30,Tick,
                decimal.Parse(volatilityMultiplier,System.Globalization.CultureInfo.InvariantCulture),Param("ResearchVolatilityRewardRatio",1.5m),
                (int)Param("ResearchVolatilityMinStopTicks",4),(int)Param("ResearchVolatilityMaxStopTicks",30),(int)Param("ResearchVolatilityMaxTargetTicks",60));
            if(!distances.HasValue){SetReason("RESEARCH_VOLATILITY_WARMUP","固定30秒波動尚未就緒或為零");return;}
            risk=distances.Value.Stop;targetDistance=distances.Value.Target;
        }
        if(_p["AdaptiveExitMode"]==1){
            var distances=AdaptiveExitDistances.Calculate(Core("225MC"),direction,Tick,_p);
            if(!distances.HasValue){SetReason("ADAPTIVE_SAMPLES","完成漲跌段樣本或停利距離不足");return;}
            risk=distances.Value.Stop;targetDistance=distances.Value.Target;
        }
        if(_factorSignal.Enabled && (risk<=0 || risk>150))throw new InvalidOperationException("Research stop exceeds 150 points");
        decimal stop=price-direction*risk,target=price+direction*targetDistance;
        decimal costs=2*_p["CommissionPerSide"]/_context!.Universe.Tradable.ContractMultiplier+_p["SlippageTicks"]*Tick;
        decimal reward=targetDistance-costs;
        _thresholds.Add(new("NetRewardRisk",reward/risk,_p.MinRewardRisk,"ratio",reward>=risk*_p.MinRewardRisk));
        if(stop<=0 || target<=0 || reward<risk*_p.MinRewardRisk){SetReason("INSUFFICIENT_ROOM","停利扣成本後不足報酬風險比");return;}
        _cycle="power-"+(++_cycleNumber);_entryDirection=direction;_stop=stop;_target=target;_entryExpired=false;_cancelSent=false;_entryWorking=true;
        _retryEntryAfterCancel=false;_selectedEntryType=type;
        SetReason("ENTRY_ACCEPT",passive?(direction>0?"微型加權中價高於大型，掛買一價買進等待成交":"微型加權中價低於或等於大型，掛賣一價賣出等待成交"):(direction>0?"微型加權中價低於或等於大型或原模式，市價買進":"微型加權中價高於大型或原模式，市價賣出"));
        Emit(direction>0?BuySell.Buy:BuySell.Sell,type,direction>0?PaperOrderRole.EntryLong:PaperOrderRole.EntryShort,price,1,new(direction,stop,target,_cycle,_now,AdaptiveParameterState.HoldingSeconds(Core("225MC"),direction,_p)));
    }
    private void Emit(BuySell side,PaperOrderType type,PaperOrderRole role,decimal price,int quantity,Plan? plan=null)
    {
        string client=_orders!.NextClientOrderId(role.ToString(),_cycle);
        var request=new PaperOrderRequest{StrategyInstanceId=InstanceId,StrategyRunGeneration=_context!.StrategyRunGeneration,ClientOrderId=client,CycleId=_cycle,
            Symbol=_context.Universe.Tradable.Symbol,Side=side,Type=type,Role=role,Price=price,Quantity=quantity};
        if(plan!=null){_entryClient=client;_plans[client]=plan;}
        Publish(true);OrderRequested?.Invoke(request);
    }
    public void OnOrderSubmitted(PaperOrderRequest request)=>_orders!.Track(request);
    public void OnOrderLifecycle(OrderLifecycleEvent e)
    {
        if(_orders==null || !_orders.TryGet(e.ClientOrderId,out var request) || request==null || !_orders.Apply(e))return;
        if(e.State==OrderLifecycleState.Filled){TryFinishClose();Publish(true);return;}
        if(e.State is OrderLifecycleState.Cancelled or OrderLifecycleState.Rejected)
        {
            if(request.Role is PaperOrderRole.EntryLong or PaperOrderRole.EntryShort){
                _retiredEntries.Add(e.ClientOrderId);
                if(e.ClientOrderId==_entryClient){
                    _entryWorking=false;
                    bool retry=e.State==OrderLifecycleState.Cancelled && _retryEntryAfterCancel && !_paused && !_closing && Position().NetQuantity==0;
                    _retryEntryAfterCancel=false;
                    if(retry){_gate.Reset();_armed=0;_cooldownUntil=(e.Timestamp>_now?e.Timestamp:_now).AddSeconds((double)_p["CooldownSeconds"]);
                        State=Core("225MC").IsWarm?StrategyLifecycleState.Ready:StrategyLifecycleState.WarmingUp;SetReason("ENTRY_EXPIRED","第一檔未成交已確認撤單，冷卻後重新評估");}
                    else {_paused=true;State=StrategyLifecycleState.Suspended;SetReason("ENTRY_TERMINAL","進場已撤銷／拒絕，重新暖機後才可恢復");}
                }
            }
            else {_exitWorking=false;_paused=true;State=StrategyLifecycleState.Faulted;SetReason("EXIT_REJECTED","出場撤銷／拒絕，保留風控並需核對部位");}
            TryFinishClose();Publish(true);
        }
    }
    public void OnExecution(ExecutionEvent e)
    {
        if(_context==null || e.StrategyInstanceId!=InstanceId || e.Symbol!=_context.Universe.Tradable.Symbol || e.StrategyRunGeneration!=_context.StrategyRunGeneration ||
            !_orders!.TryGet(e.ClientOrderId,out var request) || request==null || request.CycleId!=e.CycleId || request.Role!=e.Role)return;
        string key=e.ExecutionId.Length>0?e.ExecutionId:e.ClientOrderId+":"+e.CumulativeFilledQuantity+":"+e.Quantity;
        if(!_fills.Add(key))return;
        if(e.Role is PaperOrderRole.EntryLong or PaperOrderRole.EntryShort)
        {
            var plan=_plans[e.ClientOrderId];bool late=_retiredEntries.Contains(e.ClientOrderId) || _entryExpired || _paused || _closing || e.Timestamp>plan.Sent.AddSeconds((double)_p["EntryTimeoutSeconds"]);
            bool newerEntryWorking=_entryWorking && e.ClientOrderId!=_entryClient;
            if(!newerEntryWorking)_entryWorking=false;
            _cycle=plan.Cycle;_stop=plan.Stop;_target=plan.Target;_entryAt=e.Timestamp;_holdingLimit=plan.HoldingSeconds>0?plan.HoldingSeconds:_p["MaxHoldingSeconds"];_oppositeSince=null;
            bool invalidFillRisk=false;
            if(_factorSignal.Enabled)
            {
                // Fill slippage must not widen the declared maximum trigger distance.
                int direction=plan.Direction;decimal entry=Position().AveragePrice;
                if(_fillAnchoredRisk)
                {
                    var anchored=FillAnchoredRisk.Calculate(entry,request.Price,plan.Stop,plan.Target,direction,Tick);
                    if(anchored.HasValue){_stop=anchored.Value.Stop;_target=anchored.Value.Target;}
                    else invalidFillRisk=true;
                }
                if(direction*(entry-_stop)>150)_stop=entry-direction*150;
                _factorSignal.Reset();
            }
            _indicatorExit.Reset(Position().AveragePrice,_stop);
            State=_paused?StrategyLifecycleState.Suspended:StrategyLifecycleState.Running;
            SetReason("ENTRY_FILLED","進場成交，固定停損／停利生效");
            if(invalidFillRisk)Exit(PaperOrderRole.Flatten,"INVALID_FILL_RISK","成交後無法建立有效停損距離，保護平倉");
            if(late){
                if(newerEntryWorking || _p["EntryExecutionMode"]==1 && request.Type==PaperOrderType.BrokerLimit){_closing=true;_paused=true;CheckEntryTimeout();}
                Exit(PaperOrderRole.Flatten,"LATE_ENTRY","晚到／暫停期間進場，保護平倉");
            }
        }
        else {_exitWorking=false;_oppositeSince=null;_cooldownUntil=e.Timestamp.AddSeconds((double)_p["CooldownSeconds"]);_armed=0;_gate.Reset();SetReason("EXIT_FILLED","出場成交");if(_closing && Position().NetQuantity!=0)ManagePosition();TryFinishClose();}
        Publish(true);
    }
    private void ManagePosition()
    {
        int quantity=Position().NetQuantity;if(quantity==0 || _exitWorking)return;
        int direction=Math.Sign(quantity);var b=_books.GetValueOrDefault("225MC");
        decimal? executable=b==null?null:direction>0?b.BestBid:b.BestAsk;
        if(_closing){Exit(PaperOrderRole.Flatten,"CLOSE","關閉／風險退出平倉");return;}
        if(executable.HasValue && direction*(executable.Value-_stop)<=0){Exit(PaperOrderRole.StopLoss,"STOP","固定價格停損");return;}
        if(executable.HasValue && direction*(executable.Value-_target)>=0){Exit(PaperOrderRole.TakeProfit,"TARGET","固定目標停利");return;}
        if(SessionRemaining()<=_p["CloseBeforeSeconds"] || _now>=_entryAt.AddSeconds((double)_holdingLimit)){Exit(PaperOrderRole.Flatten,"TIME_EXIT","持倉時間或收盤前平倉");return;}
        bool Fresh(string product)=>_trades.TryGetValue(product,out var at) && (_now-at).TotalSeconds<=(double)_p["MaxDataAgeSeconds"];
        bool freshBook=b!=null && (_now-b.Timestamp).TotalSeconds<=(double)_p["MaxDataAgeSeconds"] && b.BestBid>0 && b.BestAsk>b.BestBid;
        if(executable.HasValue && _indicatorExit.TrailHit(direction,executable.Value,Tick,freshBook))
        {Exit(PaperOrderRole.Flatten,"TRAIL_EXIT","獲利後移動保護出場");return;}
        bool powerDue=_indicatorExit.PowerDue(_now,direction,Core("225MC"),Core("225M"),Fresh("225MC"),Fresh("225M"));
        _oppositeSince=_indicatorExit.PowerSince;
        if(powerDue)Exit(PaperOrderRole.Flatten,"POWER_EXIT",_p["PowerExitMode"] switch{1=>"兩商品反向力道持續成立",2=>"聯合力道轉弱持續成立",_=>"反向力道持續成立"});
    }
    private void Exit(PaperOrderRole role,string code,string reason)
    {
        int quantity=Position().NetQuantity;if(quantity==0 || _exitWorking)return;
        _exitWorking=true;SetReason(code,reason);
        decimal price=_books.TryGetValue("225MC",out var b)?quantity>0?b.BestBid:b.BestAsk:Position().AveragePrice;
        // A cancel/fill race can leave more than one lot. Respect the one-lot broker
        // whitelist and continue closing after each confirmed fill.
        Emit(quantity>0?BuySell.Sell:BuySell.Buy,PaperOrderType.Market,role,Align(price,quantity<0),Math.Min(1,Math.Abs(quantity)));
    }
    private void CheckEntryTimeout()
    {
        if(!_entryWorking || !_plans.TryGetValue(_entryClient,out var plan))return;
        if(_closing || _paused || SessionRemaining()<=_p["CloseBeforeSeconds"] || _now>=plan.Sent.AddSeconds((double)_p["EntryTimeoutSeconds"]))
        {
            if(!_cancelSent){
                _retryEntryAfterCancel=!_closing && !_paused && SessionRemaining()>_p["CloseBeforeSeconds"] &&
                    _p["EntryExecutionMode"]==1 && _orders!.TryGet(_entryClient,out var request) && request!.Type==PaperOrderType.BrokerLimit;
                _entryExpired=true;_retiredEntries.Add(_entryClient);if(!_retryEntryAfterCancel)_paused=true;
                State=_paused?StrategyLifecycleState.Suspended:StrategyLifecycleState.Running;
                SetReason("ENTRY_CANCEL_PENDING","進場逾時／停止，等待撤單確認");_cancelSent=true;CancelOrderRequested?.Invoke(_entryClient);
            }
        }
    }
    public void OnHeartbeat(DateTimeOffset now)
    {
        if(_context==null || State==StrategyLifecycleState.Stopped || now<_now)return;
        _now=now;_indicator.OnHeartbeat(now);_gate.Adaptive=_adaptive.GetThresholds(_now);
        _armed=_gate.Update(_now,Core("225M"),Core("225MC"),Healthy() && !_paused && !_closing && !_entryWorking && Position().NetQuantity==0 && (_p["SignalMode"]==0 || !_cooldownUntil.HasValue || _now>=_cooldownUntil),_validMicroPrice,Tick);
        if(_entryWorking && _reasonCode=="ENTRY_ACCEPT")SetReason("ENTRY_WORKING","進場意圖等待穿價／成交");
        CheckEntryTimeout();if(Position().NetQuantity!=0)ManagePosition();TryFinishClose();Publish();
    }
    public IReadOnlyList<DateTimeOffset> GetPulseTimes(DateTimeOffset start,DateTimeOffset end)
    {
        var times=_indicator.GetPulseTimes(start,end).ToList();
        // Match the live heartbeat cadence while inputs remain fresh; skip long idle gaps.
        DateTimeOffset fresh=!_trades.ContainsKey("225M") || !_trades.ContainsKey("225MC")?start:new[]{_trades["225M"],_trades["225MC"]}.Min().AddSeconds((double)_p["MaxDataAgeSeconds"]);
        for(var at=start.AddMilliseconds(250);at<=end && at<=fresh;at=at.AddMilliseconds(250))times.Add(at);
        void Add(DateTimeOffset at){if(at>start && at<=end)times.Add(at);}
        Add(fresh.AddTicks(1));
        if(_entryWorking && _plans.TryGetValue(_entryClient,out var plan))Add(plan.Sent.AddSeconds((double)_p["EntryTimeoutSeconds"]));
        if(Position().NetQuantity!=0){Add(_entryAt.AddSeconds((double)_holdingLimit));if(_oppositeSince.HasValue)Add(_oppositeSince.Value.AddSeconds((double)_p["ExitConfirmSeconds"]));}
        foreach(var boundary in _indicator.GetPulseTimes(start,end.AddSeconds((double)_p["CloseBeforeSeconds"])))Add(boundary.AddSeconds(-(double)_p["CloseBeforeSeconds"]));
        return times.Distinct().Order().ToArray();
    }
    public void Pause(string reason){_paused=true;_armed=0;_gate.Reset();State=StrategyLifecycleState.Suspended;SetReason("PAUSED",reason);CheckEntryTimeout();Publish(true);}
    public void Resume(){if(!CanResume)throw new InvalidOperationException("需空手且委託歸零才可恢復");_paused=false;_entryExpired=false;_indicator.Pause("恢復前重置");_indicator.Resume();_books.Clear();_entryBooks.Clear();_entryQuoteDecision=null;_selectedEntryType=null;_trades.Clear();_validMicroPrice=null;_armed=0;_gate.Reset();State=StrategyLifecycleState.WarmingUp;SetReason("WARMUP","恢復後重新暖機");Publish(true);}
    public void Stop(string reason)=>RequestClose(reason);
    public void NotifyRecordingFailure(Exception error)
    {_observationError=error.Message;if(State!=StrategyLifecycleState.Stopped)Pause("執行紀錄保存異常，停止新倉但保留部位風控："+error.Message);else Publish(true);}
    public void RequestRiskExit(string reason)=>RequestClose(reason);
    public void RequestClose(string reason){_closing=true;_paused=true;_armed=0;_gate.Reset();SetReason("CLOSE",reason);CheckEntryTimeout();ManagePosition();TryFinishClose();Publish(true);}
    private void TryFinishClose(){if(_closing && Position().NetQuantity==0 && !_entryWorking && !_exitWorking && (_orders?.ActiveClientOrderIds.Count??0)==0){_indicator.Stop(_reason);State=StrategyLifecycleState.Stopped;}}
    private void SetReason(string code,string reason){_reasonCode=code;_reason=reason;}
    public StrategySnapshot GetSnapshot()
    {
        var s=_indicator.GetSnapshot();var quotes=_context!=null && _p["EntryExecutionMode"]==1?AssessEntryQuotes():null;
        var mini=_context?.Universe.References.SingleOrDefault(p=>p.ProductId=="225M");
        var miniMid=mini==null?null:WeightedMidEntryPolicy.Calculate(_now,_entryBooks.GetValueOrDefault("225M"),
            mini.TickSize,_p["MaxDataAgeSeconds"],(int)_p["WeightedMidMode"]);
        var features=new Dictionary<string,decimal?>(s.Features){["StopTrigger"]=_stop,["TargetTrigger"]=_target,["ArmedDirection"]=_armed,["EffectivePowerThreshold"]=_gate.PowerThreshold,["EffectiveJointPower"]=_gate.JointThreshold,["EffectivePowerDifference"]=_gate.DifferenceThreshold,["AdaptiveSamples"]=_adaptive.SampleCount,["EffectiveHoldingSeconds"]=_holdingLimit,
            ["225.WeightedMid"]=quotes?.LargeMid,["225M.WeightedMid"]=miniMid,["225MC.WeightedMid"]=quotes?.MicroMid,["EntryWeightedMidDifference"]=_entryQuoteDecision?.Difference,
            ["EntryDecisionMicroWeightedMid"]=_entryQuoteDecision?.MicroMid,["EntryDecisionLargeWeightedMid"]=_entryQuoteDecision?.LargeMid};
        foreach(string product in new[]{"225","225M","225MC"})
        {
            var f=_factorState.Read(product,_now,product=="225"?10:5);
            features[product+".ResearchQueueImbalance"]=f?.QueueImbalance;
            features[product+".ResearchDepthVariance"]=f?.DepthVariance;
            features[product+".ResearchObservedOFI10"]=f?.Ofi;
            features[product+".ResearchMomentumTicks"]=f?.MomentumTicks;
            features[product+".ResearchMeanDeviationTicks"]=f?.MeanDeviationTicks;
            features[product+".ResearchEfficiency"]=f?.Efficiency;
            features[product+".ResearchWeightedMid"]=f?.WeightedMid;
            features[product+".ResearchBestDepthShareDifference"]=f?.BestDepthShareDifference;
            features[product+".ResearchVWAP30DeviationTicks"]=f?.VwapDeviationTicks;
            features[product+".ResearchLogRV30"]=f?.LogRealizedVariance30;
            features[product+".ResearchDownsideShare30"]=f?.DownsideVarianceShare30;
            features[product+".ResearchTickVolatility30"]=f?.TickVolatility30;
            features[product+".ResearchLastReturnTicks"]=f?.LastReturnTicks;
            features[product+".ResearchTradeIntervalCV200"]=f?.TradeIntervalCV200;
            features[product+".ResearchPrintRate30"]=f?.PrintRate30;
        }
        var diagnostics=new Dictionary<string,string>(s.Diagnostics){["PrimaryReasonCode"]=_reasonCode,["BaisPowerUnits"]="百分點；短期減長期",["ModelVersion"]="round17/mean_mini_return_minus05_score025",["ResearchStatus"]=R17Parameters.MatchesFrozenProfile(_definition.Parameters)?"六日研究僅三日達標，尚未全日合格":"自訂參數，未經原六日驗證",["SignalMode"]="多因子均價回歸"};
        if(_observationError.Length>0)diagnostics["ObservationError"]=_observationError;
        diagnostics["WeightedMidFormula"]=_p["WeightedMidMode"]==0?"第一檔交叉權重：(買價×賣量＋賣價×買量)／總量":"第一檔同側權重：(買價×買量＋賣價×賣量)／總量";
        diagnostics["EntrySelectedOrderType"]=_selectedEntryType?.ToString()??"尚未選擇";
        diagnostics["EntryQuoteGate"]=quotes?.ReasonCode??"停用";
        return s with{StrategyType=StrategyType,State=State,Reason=_reason,ParameterSummary=string.Join(";",_definition.Parameters.OrderBy(x=>x.Key,StringComparer.Ordinal).Select(x=>x.Key+"="+x.Value)),StopPrice=_stop,UpdatedAt=_now,
            Features=features,Diagnostics=diagnostics,Position=Position().NetQuantity,NetPnl=Position().NetPnl,WinRate=Position().WinRate,
            AverageWinningPoints=Position().AverageWinningPoints,AverageLosingPoints=Position().AverageLosingPoints,UserDefinedProfitFactor=Position().UserDefinedProfitFactor};
    }
    private void Publish(bool force=false)
    {
        if(!force && _publishedAt.HasValue && _now<_publishedAt.Value.AddSeconds(1))return;
        _publishedAt=_now;var s=GetSnapshot();SnapshotChanged?.Invoke(s);
        DecisionObserved?.Invoke(new(){StrategyType=StrategyType,StrategyInstanceId=InstanceId,Timestamp=_now,Symbol=s.TradableSymbol,State=State,
            Sequence=_sequences.GetValueOrDefault(s.TradableSymbol),CandidateId=_candidateId,CycleId=_cycle,IsCandidate=_armed!=0 || _reasonCode=="ENTRY_ACCEPT",
            Direction=_armed>0?"Long":_armed<0?"Short":_reasonCode is "ENTRY_ACCEPT" or "ENTRY_WORKING"?(_entryDirection>0?"Long":"Short"):Position().NetQuantity>0?"Long":Position().NetQuantity<0?"Short":"",Decision=_reasonCode=="ENTRY_ACCEPT"?StrategyDecision.Accept:Position().NetQuantity!=0?StrategyDecision.Hold:State==StrategyLifecycleState.WarmingUp?StrategyDecision.Warmup:StrategyDecision.Reject,
            PrimaryReasonCode=_reasonCode,Features=s.Features,Thresholds=_thresholds,SecondaryReasonCodes=_thresholds.Where(t=>!t.Passed).Select(t=>t.Name).ToArray(),
            UnavailableReasons=new Dictionary<string,string>{{"ExchangeTimestamp","接收時間；來源時刻尚未具完整日期時區"}}});
    }
}
