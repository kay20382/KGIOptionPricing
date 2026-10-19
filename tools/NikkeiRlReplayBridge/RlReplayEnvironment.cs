using KGIOptionPricing.StrategyFramework;
using KGIOptionPricing.StrategyFramework.Execution;
using KGIOptionPricing.StrategyFramework.Performance;
using KGIOptionPricing.OrderManagement.Core;

namespace NikkeiRl;

/// <summary>Offline only. All executions and accounting use the shared paper engine.</summary>
public sealed class RlReplayEnvironment
{
    private sealed class Clock:IStrategyClock { public DateTimeOffset Now {get;set;} }
    private const string Instance="NikkeiRlBacktest";
    private readonly ReplayFrame[] frames;
    private readonly RlConfig config;
    private readonly Clock clock=new();
    private CrossPricePaperExecutionService execution=null!;
    private PaperPortfolioLedger ledger=null!;
    private int index,orderId;
    private decimal peak,maxDrawdown,totalReward;
    private decimal entryPrice,mfe,mae;
    private decimal openEntryCosts;
    private decimal pendingEntryStopPoints,activeStopPrice,activeStopDistance;
    private readonly Queue<(DateTimeOffset At,decimal Price)> stopPrices=new();
    private DateTimeOffset? stopObservedFrom;
    private decimal? stopVolatility;
    private bool stopWarm;
    private int stopAdjustments;
    private DateTimeOffset? enteredAt,cooldownUntil;
    private PaperOrderRequest? delayed;
    private DateTimeOffset? readyAt;
    private int? delayedAction;
    private DateTimeOffset? actionReadyAt;
    private readonly List<ExecutionEvent> fills=[];
    private PositionSnapshot? cachedPosition;
    private bool done;
    public decimal TotalDeltaEquity {get;private set;}
    private ReplayFrame Current=>frames[index];
    private decimal TickValue=>config.TickSize*config.Multiplier;
    private PositionSnapshot Position=>cachedPosition??=ledger.GetPosition(Instance,Current.Symbol);

    public RlReplayEnvironment(ReplayFrame[] frames,RlConfig config)
    {
        ReplayCache.ValidateReadinessProfile(config.ResearchReadinessProfile,config.FeatureProfile,config.DecisionIntervalSeconds);
        if(frames.Length<2 || frames.Any(x=>x.Features.Length!=30))throw new ArgumentException("At least two 30-feature frames required");
        if(config.DecisionIntervalSeconds is not (3 or 5))throw new ArgumentException("Invalid decision interval");
        if(config.EntryPriceMode is not ("same-side" or "opposite-touch"))throw new ArgumentException("Invalid entry price mode");
        if(config.FeatureProfile is not ("three-product" or "mini-micro-power" or "mini-micro-research")||config.FeatureProfile=="mini-micro-power"&&(config.ObservationSize!=40||!config.DirectionDrivenHolding||config.MaxHoldingSeconds!=0)||config.FeatureProfile=="mini-micro-research"&&(config.ObservationSize!=60||!config.DirectionDrivenHolding||config.MaxHoldingSeconds!=0||config.DecisionIntervalSeconds!=3))throw new ArgumentException("Invalid feature/holding profile");
        if(config.Commission<0 || config.ExtraCostTicksPerSide<0 || config.DecisionLatencyMs<0 || config.DecisionLatencyMs>5000 || config.StopTicks<1 || config.TickSize<=0 || config.Multiplier<=0 || config.MaxHoldingSeconds<0 || config.CooldownSeconds<0 || config.DrawdownPenalty<0)throw new ArgumentException("Invalid RL config");
        if(frames.Any(f=>f.Symbol!=frames[0].Symbol))throw new ArgumentException("An episode cannot cross contracts");
        if(config.RewardCostWeight<0 || config.RewardCostWeight>1)throw new ArgumentException("Invalid reward cost weight");
        if(config.StopMode is not ("legacy-fixed" or "fixed" or "adaptive-volatility"))throw new ArgumentException("Invalid stop mode");
        if(config.MaxStopPoints.HasValue&&config.MaxStopPoints.Value<config.TickSize)throw new ArgumentException("Maximum stop must cover one tick");
        if(config.StopMode!="legacy-fixed"&&(config.MaxStopPoints==null||config.MaxStopPoints<config.TickSize||config.FixedStopPoints<=0||config.MinStopPoints<config.TickSize||decimal.Ceiling(config.MinStopPoints/config.TickSize)*config.TickSize>decimal.Floor(config.MaxStopPoints.Value/config.TickSize)*config.TickSize||config.StopVolatilitySeconds is <10 or >600||config.StopVolatilityMultiplier is <=0 or >10))throw new ArgumentException("Invalid capped stop parameters");
        if(config.ObservationSize is not (40 or 48 or 50 or 56 or 60)||config.ObservationSize==60&&(config.FeatureProfile!="mini-micro-research"||frames.Any(f=>f.ResearchExtras.Length!=20||f.ResearchExtras.Any(x=>!double.IsFinite(x))))||config.ObservationSize==50&&frames.Any(f=>f.MarketExtras.Length!=8||f.MarketExtras.Any(x=>!double.IsFinite(x))))throw new ArgumentException("Invalid observation schema/cache");
        if(config.ObservationSize is (48 or 56)&&frames.Any(f=>f.PaperExtras.Length!=8||f.PaperExtras.Any(x=>!double.IsFinite(x))))throw new ArgumentException("Invalid paper schema/cache");
        this.frames=frames;this.config=config;
    }

    public RlState Reset()
    {
        index=orderId=0;peak=maxDrawdown=totalReward=TotalDeltaEquity=0;done=false;enteredAt=cooldownUntil=readyAt=actionReadyAt=null;delayed=null;delayedAction=null;fills.Clear();
        entryPrice=mfe=mae=openEntryCosts=0;
        cachedPosition=null;ledger=new();ledger.RegisterProduct(Instance,new ResolvedProduct{ProductId="225MC",Symbol=Current.Symbol,TickSize=config.TickSize,ContractMultiplier=config.Multiplier});
        pendingEntryStopPoints=activeStopPrice=activeStopDistance=0;stopAdjustments=0;stopPrices.Clear();stopObservedFrom=Current.At;stopWarm=false;stopVolatility=null;
        if(config.StopMode=="adaptive-volatility")foreach(var trade in Current.Trades.Where(t=>t.At<Current.At))ObserveStopPrice(trade);
        execution=new(config.Commission+config.ExtraCostTicksPerSide*TickValue,clock);
        execution.Filled+=OnFill;
        clock.Now=Current.At;
        return Snapshot();
    }

    private bool EntryPending=>delayed?.Role is PaperOrderRole.EntryLong or PaperOrderRole.EntryShort || execution.GetWorkingOrders(Instance).Any(x=>x.Role is PaperOrderRole.EntryLong or PaperOrderRole.EntryShort);
    private bool ExitPending=>delayedAction==3 || delayed?.Role is PaperOrderRole.Flatten || execution.GetWorkingOrders(Instance).Any(x=>x.Role==PaperOrderRole.Flatten);
    private DateTimeOffset SessionCloseAt
    {
        get { var local=Current.At.ToOffset(TimeSpan.FromHours(8));var date=local.Date;var tod=local.TimeOfDay;
            if(tod>=TimeSpan.FromHours(16))return new DateTimeOffset(date.AddDays(1).AddHours(5),TimeSpan.FromHours(8));
            if(tod<=TimeSpan.FromHours(5))return new DateTimeOffset(date.AddHours(5),TimeSpan.FromHours(8));
            return new DateTimeOffset(date.AddHours(14.75),TimeSpan.FromHours(8)); }
    }
    private bool[] Mask()
    {
        bool closeWindow=(SessionCloseAt-Current.At).TotalSeconds<=310;
        bool enter=Current.Valid && Position.NetQuantity==0 && !EntryPending && !ExitPending && !closeWindow && (cooldownUntil==null || Current.At>=cooldownUntil);
        bool direction=config.DirectionDrivenHolding&&Current.Valid&&Position.NetQuantity!=0&&!ExitPending&&!closeWindow;
        return [true,enter||direction,enter||direction,Position.NetQuantity!=0 && !ExitPending,EntryPending && delayedAction!=4];
    }

    private void OnFill(ExecutionEvent fill)
    {
        decimal costsBefore=Position.Costs;
        ledger.ApplyExecution(fill);cachedPosition=null;fills.Add(fill);
        if(fill.Role is PaperOrderRole.EntryLong or PaperOrderRole.EntryShort)
        {
            enteredAt=fill.Timestamp;
            openEntryCosts=Position.Costs-costsBefore;
            entryPrice=fill.Price;mfe=mae=0;
            // Protective stop is submitted after the entry fill, never counted as another entry.
            int side=Position.NetQuantity;
            activeStopDistance=pendingEntryStopPoints;activeStopPrice=fill.Price-side*activeStopDistance;
            Submit(side>0?BuySell.Sell:BuySell.Buy,PaperOrderType.Stop,PaperOrderRole.StopLoss,activeStopPrice,false);
        }
        else if(Position.NetQuantity==0)
        {
            execution.CancelAll(Instance);delayed=null;delayedAction=null;readyAt=actionReadyAt=null;enteredAt=null;
            cooldownUntil=fill.Timestamp.AddSeconds(config.CooldownSeconds);
            entryPrice=mfe=mae=0;
            openEntryCosts=0;
            activeStopPrice=activeStopDistance=pendingEntryStopPoints=0;
        }
    }

    private void Submit(BuySell side,PaperOrderType type,PaperOrderRole role,decimal price,bool withLatency)
    {
        var request=new PaperOrderRequest{StrategyInstanceId=Instance,Symbol=Current.Symbol,Side=side,Type=type,Role=role,Price=price,Quantity=1,ClientOrderId=$"RL-{++orderId}",CycleId=$"RL-{orderId}"};
        if(withLatency && config.DecisionLatencyMs>0){delayed=request;readyAt=clock.Now.AddMilliseconds(config.DecisionLatencyMs);}
        else execution.Submit(request,clock.Now);
    }

    private void CancelEntry()
    {
        if(delayed?.Role is PaperOrderRole.EntryLong or PaperOrderRole.EntryShort){delayed=null;readyAt=null;}
        foreach(var o in execution.GetWorkingOrders(Instance).Where(o=>o.Role is PaperOrderRole.EntryLong or PaperOrderRole.EntryShort))execution.Cancel(o.OrderId);
    }

    private void Flatten(bool withLatency)
    {
        CancelEntry();
        if(Position.NetQuantity==0 || ExitPending)return;
        execution.CancelAll(Instance);
        Submit(Position.NetQuantity>0?BuySell.Sell:BuySell.Buy,PaperOrderType.Market,PaperOrderRole.Flatten,Position.MarkPrice>0?Position.MarkPrice:Position.AveragePrice,withLatency);
    }

    private void ReleaseDelayed(DateTimeOffset at)
    {
        if(delayed!=null && readyAt!=null && at>=readyAt)
        {
            var request=delayed;delayed=null;var submitted=readyAt.Value;readyAt=null;execution.Submit(request,submitted);
        }
        if(delayedAction!=null && actionReadyAt!=null && at>=actionReadyAt)
        {
            int action=delayedAction.Value;delayedAction=null;actionReadyAt=null;
            if(action==3)Flatten(false);else CancelEntry();
        }
    }

    private void ObserveStopPrice(ReplayTrade trade)
    {
        if(trade.Price>0)stopPrices.Enqueue((trade.At,trade.Price));
    }
    private decimal StopDistance()
    {
        if(config.StopMode=="legacy-fixed")return config.MaxStopPoints.HasValue?Math.Min(config.StopTicks*config.TickSize,decimal.Floor(config.MaxStopPoints.Value/config.TickSize)*config.TickSize):config.StopTicks*config.TickSize;
        while(stopPrices.TryPeek(out var old)&&old.At<Current.At.AddSeconds(-config.StopVolatilitySeconds))stopPrices.Dequeue();
        stopWarm=stopObservedFrom.HasValue&&(Current.At-stopObservedFrom.Value).TotalSeconds>=config.StopVolatilitySeconds&&stopPrices.Count>=2;
        stopVolatility=null;
        if(config.StopMode=="adaptive-volatility"&&stopWarm)
        {
            double mean=stopPrices.Average(t=>(double)t.Price);
            stopVolatility=(decimal)Math.Sqrt(stopPrices.Average(t=>Math.Pow((double)t.Price-mean,2)));
        }
        decimal cap=decimal.Floor(config.MaxStopPoints!.Value/config.TickSize)*config.TickSize;
        decimal minimum=decimal.Ceiling(config.MinStopPoints/config.TickSize)*config.TickSize;
        decimal requested=config.StopMode=="fixed"?config.FixedStopPoints:stopVolatility.HasValue?Math.Max(minimum,stopVolatility.Value*config.StopVolatilityMultiplier):cap;
        return Math.Max(config.TickSize,decimal.Floor(Math.Min(requested,cap)/config.TickSize)*config.TickSize);
    }
    private void TightenStop()
    {
        decimal distance=StopDistance();
        if(config.StopMode!="adaptive-volatility"||Position.NetQuantity==0||ExitPending||distance>=activeStopDistance)return;
        var existing=execution.GetWorkingOrders(Instance).SingleOrDefault(o=>o.Role==PaperOrderRole.StopLoss);
        if(existing==null)return;
        // Synchronous paper cancellation only. Live adapters need acknowledged replace semantics.
        if(!execution.Cancel(existing.OrderId))return;
        int side=Position.NetQuantity;activeStopDistance=distance;activeStopPrice=entryPrice-side*distance;stopAdjustments++;
        Submit(side>0?BuySell.Sell:BuySell.Buy,PaperOrderType.Stop,PaperOrderRole.StopLoss,activeStopPrice,false);
    }

    public RlState Step(int action)
    {
        if(done)throw new InvalidOperationException("Reset required after episode end");
        decimal before=Position.NetPnl,beforeCosts=Position.Costs,ddBefore=peak-before;fills.Clear();
        var mask=Mask();int effective=action;string reason="";
        if(action<0 || action>=mask.Length || !mask[action]){effective=0;reason="INVALID_ACTION";}
        if(config.DirectionDrivenHolding&&effective is (1 or 2)&&Position.NetQuantity!=0)
        {
            if((effective==1?1:-1)==Math.Sign(Position.NetQuantity)){effective=0;reason="SAME_DIRECTION_HOLD";}
            else {effective=3;reason="REVERSE_SIGNAL";}
        }
        clock.Now=Current.At;
        TightenStop();
        if(effective is 1 or 2)
        {
            pendingEntryStopPoints=StopDistance();
            decimal price=config.EntryPriceMode=="opposite-touch"?(effective==1?Current.Ask:Current.Bid):(effective==1?Current.Bid:Current.Ask);
            Submit(effective==1?BuySell.Buy:BuySell.Sell,PaperOrderType.Limit,effective==1?PaperOrderRole.EntryLong:PaperOrderRole.EntryShort,price,true);
        }
        if(effective is 3 or 4)
        {
            if(config.DecisionLatencyMs>0){delayedAction=effective;actionReadyAt=clock.Now.AddMilliseconds(config.DecisionLatencyMs);}
            else if(effective==3)Flatten(false);else CancelEntry();
        }
        if((SessionCloseAt-Current.At).TotalSeconds<=310){delayedAction=null;actionReadyAt=null;CancelEntry();Flatten(false);reason="SESSION_CLOSE";}
        var next=frames[index+1];
        if(next.DataGap || next.At<=Current.At || Math.Abs((next.At-Current.At).TotalSeconds-config.DecisionIntervalSeconds)>.001){done=true;return Snapshot() with{Truncated=true,Status="IncompleteDataGap",ProposedAction=action,EffectiveAction=effective};}
        foreach(var trade in next.Trades)
        {
            clock.Now=trade.At;
            if(config.StopMode=="adaptive-volatility")ObserveStopPrice(trade);
            ReleaseDelayed(trade.At);
            foreach(var o in execution.GetWorkingOrders(Instance).Where(o=>o.Role is PaperOrderRole.EntryLong or PaperOrderRole.EntryShort))if((trade.At-o.CreatedAt).TotalSeconds>=10)execution.Cancel(o.OrderId);
            if(config.MaxHoldingSeconds>0&&enteredAt.HasValue && (trade.At-enteredAt.Value).TotalSeconds>=config.MaxHoldingSeconds){Flatten(false);reason="MAX_HOLDING";}
            execution.OnMarketEvent(new MarketEvent{Sequence=trade.Sequence,Timestamp=trade.At,Symbol=Current.Symbol,ProductId="225MC",Kind=MarketEventKind.Trade,LastPrice=trade.Price,TradeQuantity=1});
            // ApplyExecution marks at the requested fill price. Restore the actual
            // event price after all fills before exposing equity/reward to PPO.
            ledger.ApplyMark(Current.Symbol,trade.Price);
            // A flat book's equity is unchanged by a mark; reuse its authoritative snapshot.
            if(Position.NetQuantity!=0)cachedPosition=null;
            if(Position.NetQuantity!=0)
            {
                decimal move=Position.NetQuantity*(trade.Price-entryPrice)/config.TickSize;
                mfe=Math.Max(mfe,move);mae=Math.Max(mae,-move);
            }
            peak=Math.Max(peak,Position.NetPnl);maxDrawdown=Math.Max(maxDrawdown,peak-Position.NetPnl);
        }
        index++;clock.Now=Current.At;
        // Expire idle pending entries at decision boundaries as well.
        if(delayed!=null && readyAt!=null && (Current.At-readyAt.Value).TotalSeconds>=10){delayed=null;readyAt=null;}
        foreach(var o in execution.GetWorkingOrders(Instance).Where(o=>o.Role is PaperOrderRole.EntryLong or PaperOrderRole.EntryShort))if((Current.At-o.CreatedAt).TotalSeconds>=10)execution.Cancel(o.OrderId);
        var after=Position;peak=Math.Max(peak,after.NetPnl);maxDrawdown=Math.Max(maxDrawdown,peak-after.NetPnl);
        decimal delta=after.NetPnl-before;
        decimal penalty=config.DrawdownPenalty*Math.Max(0,peak-after.NetPnl-ddBefore)/TickValue;
        decimal trainingDelta=delta+(1-config.RewardCostWeight)*(after.Costs-beforeCosts);
        decimal reward=trainingDelta/TickValue-penalty;totalReward+=reward;TotalDeltaEquity+=delta;
        bool last=index==frames.Length-1;done=last;
        bool closed=after.NetQuantity==0 && execution.GetWorkingOrders(Instance).Count==0 && delayed==null && delayedAction==null;
        bool complete=closed && (SessionCloseAt-Current.At).TotalSeconds<=config.DecisionIntervalSeconds+.001;
        return Snapshot() with {Reward=reward,DeltaEquity=delta,DrawdownPenalty=penalty,ProposedAction=action,EffectiveAction=effective,OverrideReason=reason,Terminated=last&&complete,Truncated=last&&!complete,Status=last?(complete?"CompleteEpisode":closed?"IncompleteRecording":"IncompleteUnclosedPosition"):"Running",Fills=fills.ToArray()};
    }

    private RlState Snapshot()
    {
        var p=Position;var working=execution.GetWorkingOrders(Instance);
        double[] observation=Current.Features.Concat(new[]{(double)p.NetQuantity,enteredAt.HasValue?(Current.At-enteredAt.Value).TotalSeconds/60:0,(double)(p.UnrealizedPnl/TickValue),(double)(p.NetPnl/TickValue),(double)((peak-p.NetPnl)/TickValue),cooldownUntil.HasValue?Math.Max(0,(cooldownUntil.Value-Current.At).TotalSeconds)/30:0,EntryPending?1d:0,ExitPending?1d:0,Current.Valid?1d:0,(SessionCloseAt-Current.At).TotalSeconds/3600}).ToArray();
        if(config.ObservationSize is (48 or 56))observation=observation.Concat(Current.PaperExtras).ToArray();
        if(config.ObservationSize==56)observation=observation.Concat(PredictiveFeatureTransform.Compute(Current.Features)).ToArray();
        if(config.ObservationSize==60)observation=observation.Concat(Current.ResearchExtras).ToArray();
        if(config.ObservationSize==50)observation=observation.Concat(Current.MarketExtras).Concat(new[]{(double)mfe,(double)mae}).ToArray();
        // One-contract cycles: subtract only the actual shared-ledger entry fee of
        // the still-open cycle. No separate P&L engine or synthetic terminal fill.
        decimal completedCosts=p.Costs-openEntryCosts;
        decimal completedNet=p.RealizedPnl-completedCosts;
        return new(){At=Current.At,Observation=observation,Mask=Mask(),NetPnl=p.NetPnl,Costs=p.Costs,CompletedGrossPnl=p.RealizedPnl,CompletedNetPnl=completedNet,CompletedCosts=completedCosts,UnrealizedPnl=p.UnrealizedPnl,OpenPositionCosts=p.Costs-completedCosts,ExcludedOpenNetPnl=p.NetPnl-completedNet,MaxDrawdown=maxDrawdown,Position=p.NetQuantity,CompletedTrades=p.CompletedTrades,PendingEntry=EntryPending,TotalReward=totalReward,WinRate=p.WinRate,AverageWinningPoints=p.AverageWinningPoints,AverageLosingPoints=p.AverageLosingPoints,UserDefinedProfitFactor=p.UserDefinedProfitFactor,ActiveStopPrice=activeStopPrice,ActiveStopDistancePoints=activeStopDistance,StopVolatilityPoints=stopVolatility,StopVolatilityWarm=stopWarm,StopAdjustmentCount=stopAdjustments};
    }
}

