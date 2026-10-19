using System.Text.Json;
using KGIOptionPricing;
using KGIOptionPricing.OrderManagement.Core;
using KGIOptionPricing.Strategies.PowerReversal;
using KGIOptionPricing.StrategyFramework;
using KGIOptionPricing.StrategyFramework.Execution;
using KGIOptionPricing.StrategyFramework.Storage;

static class IntegrationTests
{
    static readonly DateTimeOffset T=new(2026,10,6,8,0,0,TimeSpan.FromHours(8));
    static void Check(bool value,string why){if(!value)throw new Exception(why);}
    static readonly ResolvedStrategyUniverse U=new(){Tradable=new(){ProductId="225MC",Symbol="MC",TickSize=5,ContractMultiplier=10},References=new[]{new ResolvedProduct{ProductId="225M",Symbol="M",TickSize=5,ContractMultiplier=100},new ResolvedProduct{ProductId="225",Symbol="L",TickSize=10,ContractMultiplier=1000}}};
    static StrategyDefinition Definition(string root)=>new(){StrategyType="PowerReversal",InstanceId="integration",Parameters=new Dictionary<string,string>{
        ["HistoryRoot"]=Path.Combine(root,"history"),["PowerWindowSeconds"]="10",["ShortAverageSeconds"]="1",["LongAverageSeconds"]="1",["PowerThreshold"]="0",["BaisThreshold"]="0",["ConfirmSeconds"]="1",["MovementRatio"]="0",["MinMovementSamples"]="1",["MinQuantity"]="1",["MinTradeCount"]="1",["MaxDataAgeSeconds"]="5",["MaxSkewSeconds"]="5",["CommissionPerSide"]="0",["SlippageTicks"]="0"}};
    static List<MarketEvent> Events()
    {
        var rows=new List<MarketEvent>();long seq=0;
        void Add(int sec,decimal price){foreach(var p in U.AllProducts)rows.Add(new(){Symbol=p.Symbol,ProductId=p.ProductId,Timestamp=T.AddSeconds(sec),Sequence=++seq,Kind=MarketEventKind.Trade,LastPrice=price,TradeQuantity=11,BidPrices=new[]{price-5},AskPrices=new[]{price}});}
        for(int sec=0;sec<=12;sec++)Add(sec,100);
        decimal[] path={100,200,170,100,130,200,170,165,160,190,190,185,265,270};for(int i=0;i<path.Length;i++)Add(13+i,path[i]);return rows;
    }
    public static void Run()
    {
        string root=Path.GetFullPath("docs/superpowers/evidence/power-reversal");Directory.CreateDirectory(root);
        string file=Path.Combine(root,"synthetic-market-events.jsonl");File.WriteAllLines(file,Events().Select(e=>JsonSerializer.Serialize(e,new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        var d=Definition(root);var strategy=new PowerReversalStrategy(d);using var source=new ArchiveReplayMarketDataSource(file);using var host=new StrategyHost(source,new Clock());
        string run=Path.Combine(root,"synthetic-runs",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(run);File.WriteAllText(Path.Combine(root,"latest-run.txt"),run);
        using var recorder=new SimulationRunRecorder(run);
        var parameters=PowerReversalParameters.Parse(d.Parameters).ToDictionary();string normalized=JsonSerializer.Serialize(new SortedDictionary<string,string>(parameters,StringComparer.Ordinal));
        var manifest=new RunManifest{RunId="synthetic-reversal",StrategyType=d.StrategyType,InstanceId=d.InstanceId,Mode=StrategyRunMode.HistoricalReplay,StartedAt=T,EffectiveParameters=parameters,NormalizedParameters=normalized,
            ParameterSha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalized))),CommissionPerContract=0,
            Universe=U.AllProducts.Select(p=>new RunProductIdentity(p.ProductId,p.Symbol,"synthetic",p.TickSize,p.ContractMultiplier)).ToArray(),
            InputArchive=new(){AbsolutePath=file,Sha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file)))},ExecutionAssumptions=new Dictionary<string,string>{{"fillModel","strict-cross; market next trade"},{"synthetic","true"}},MissingProvenanceFields=new[]{"ExchangeTimestamp","RealMarketEvidence"}};
        recorder.PrepareManifest(manifest);host.SnapshotChanged+=recorder.Record;host.ExecutionOccurred+=recorder.Record;host.OrderLifecycleOccurred+=recorder.Record;host.DecisionObserved+=recorder.Record;
        int fills=0;host.ExecutionOccurred+=_=>fills++;
        var runner=host.Add(strategy,U,0);DateTimeOffset? last=null;
        source.BeforeMarketEvent+=at=>{if(last.HasValue)foreach(var pulse in strategy.GetPulseTimes(last.Value,at))host.Pulse(pulse);last=at;};
        source.ReplayAsync().GetAwaiter().GetResult();Check(fills==2,"replay entry and exit");Check(runner.GetSnapshot().Position==0,"replay flat");Check(runner.GetSnapshot().CompletedTrades==1,"shared round trip");
        host.StopAll("驗收完成");recorder.FinalizeManifest(manifest with{EndedAt=T.AddSeconds(26)});recorder.FinalizeSummary(T.AddSeconds(26));using(var risk=JsonDocument.Parse(File.ReadAllText(Path.Combine(run,"run-summary.json"))))Check(risk.RootElement.GetProperty("maximumTradeLoss").GetDecimal()==Math.Max(0m,-runner.GetSnapshot().NetPnl),"completed net trade loss from common recorder");Console.WriteLine("PASS archive replay, common ledger and artifacts");
        Live(root);Live(root,true);RejectedEntryClose(root,false);RejectedEntryClose(root,true);UnsettledTerminalStillBlocked();Registration(d);
    }
    static void Registration(StrategyDefinition definition)
    {
        foreach(var type in new[]{typeof(StrategySimulationService),typeof(LiveStrategyService)}) {
            var factories=(IEnumerable<IStrategyFactory>)type.GetMethod("CreateDefaultFactories",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!.Invoke(null,null)!;
            Check(factories.Any(f=>f.StrategyType=="PowerReversal"),"factory registry "+type.Name);
        }
        var s=new PowerReversalStrategy(definition);StrategyLiveCapabilityGate.EnsureLiveCapable(s);
        var resolver=typeof(StrategySimulationService).GetMethod("ResolveParameters",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!;
        object result=resolver.Invoke(null,new object[]{s.GetSnapshot(),definition})!;
        var effective=(IReadOnlyDictionary<string,string>)result.GetType().GetProperty("Effective")!.GetValue(result)!;Check(effective.Count==56 && effective["EntryExecutionMode"]=="0" && effective["ReversalPoints"]=="30" && effective["SignalMode"]=="0" && effective["RequireOppositeMovement"]=="0" && effective["RequireAlignedMovement"]=="0" && effective["PriceRecoveryTicks"]=="0" && effective["MinJointPower"]=="0" && effective["MaxPowerDifference"]=="200" && effective["MinShortAveragePower"]=="0","typed manifest defaults");
        Console.WriteLine("PASS simulation/live registration and effective parameters");
    }
    static void Live(string root,bool writeFault=false)
    {
        using var source=new Source();var clock=new Clock();using var host=new StrategyHost(source,clock);var gateway=new Gateway();bool login=false;
        PowerReversalStrategy? liveStrategy=null;var intents=new List<PaperOrderRequest>();
        using var service=new LiveStrategyService(host,()=>login,()=>"FAKE",_=>U,d=>{liveStrategy=new(d);liveStrategy.OrderRequested+=intents.Add;return liveStrategy;},(d,u,fee)=>new(gateway,d.InstanceId,u.Tradable,new ContractInfo{ExchangeNm="JPX",ProductId="225MC",Symbol="JPX.225MC-202610",ComYM2="202610",CPF=CPFEnum.Futures,ComType='0'},fee,now:()=>clock.Now,strategyRunGeneration:1),false);
        var d=Definition(Path.Combine(root,"fake-live"));bool blocked=false;try{service.Add(d,0,3000);}catch(InvalidOperationException){blocked=true;}Check(blocked && gateway.Requests.Count==0,"login gate no broker request");
        login=true;service.Add(d,0,3000);
        var recorders=(System.Collections.IEnumerable)typeof(LiveStrategyService).GetField("_flowRecorders",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(service)!;
        object pair=recorders.Cast<object>().Single();object tuple=pair.GetType().GetProperty("Value")!.GetValue(pair)!;
        var liveRecorder=(SimulationRunRecorder)tuple.GetType().GetField("Item1")!.GetValue(tuple)!;string liveRun=liveRecorder.DirectoryPath;
        foreach(var e in Events().Where(e=>e.Timestamp<=T.AddSeconds(23)))source.Send(e);
        Check(gateway.Requests.Count==0,"touch entry stays local");
        foreach(var e in Events().Where(e=>e.Timestamp==T.AddSeconds(24)))source.Send(e);
        Check(gateway.Requests.Count==1 && gateway.Requests[0].Quantity==1,"strict cross sends fake one lot");gateway.Fill(gateway.Requests[0],195);
        Check(service.GetSnapshots().Single().Position==1 && gateway.Requests.Count==1,"fake broker fill ledger without unintended exit");
        if(writeFault){liveRecorder.Dispose();source.Send(new(){Symbol="MC",Sequence=9999,Timestamp=T.AddSeconds(24.5),Kind=MarketEventKind.Book,BidPrices=new[]{185m},AskPrices=new[]{190m}});var s=service.GetSnapshots().Single();Check(s.Diagnostics.ContainsKey("ObservationError") && s.State==StrategyLifecycleState.Suspended,"recording failure visible and stops entries");}
        else service.Pause(d.InstanceId);
        source.Send(new(){Symbol="MC",ProductId="225MC",Sequence=10000,Timestamp=T.AddSeconds(25),Kind=MarketEventKind.Trade,LastPrice=150,TradeQuantity=11,BidPrices=new[]{145m},AskPrices=new[]{150m}});
        Check(gateway.Requests.Count==2,"paused live stop routed");
        liveStrategy!.RequestClose("closing fill regression");var last=intents[1];
        liveStrategy.OnOrderLifecycle(new(){StrategyInstanceId=d.InstanceId,StrategyRunGeneration=last.StrategyRunGeneration,ClientOrderId=last.ClientOrderId,CycleId=last.CycleId,State=OrderLifecycleState.Filled});
        gateway.Fill(gateway.Requests[1],145);
        Check(liveStrategy.State==StrategyLifecycleState.Stopped,"strategy closes after terminal exit");
        Check(service.GetSnapshots().Single().Position==0,"fake live flat");service.RequestClose(d.InstanceId,true,out _);service.Pulse(T.AddSeconds(26));
        if(!writeFault){Check(File.ReadAllLines(Path.Combine(liveRun,"executions.csv")).Length==3,"live recorder includes closing fill");
        Check(JsonDocument.Parse(File.ReadAllText(Path.Combine(liveRun,"run-summary.json"))).RootElement.GetProperty("completedTrades").GetInt32()==1,"live complete roundtrip persisted");}
        Console.WriteLine(writeFault?"PASS recording failure blocks entries and retains live protection":"PASS fake live gateway login, strict cross, fill, paused stop and closing record");
    }
    static void RejectedEntryClose(string root,bool failedCallback)
    {
        using var source=new Source();var clock=new Clock();using var host=new StrategyHost(source,clock);var gateway=new Gateway();
        using var service=new LiveStrategyService(host,()=>true,()=>"FAKE",_=>U,d=>new PowerReversalStrategy(d),(d,u,fee)=>new(gateway,d.InstanceId,u.Tradable,new ContractInfo{ExchangeNm="JPX",ProductId="225MC",Symbol="JPX.225MC-202610",ComYM2="202610",CPF=CPFEnum.Futures,ComType='0'},fee,now:()=>clock.Now,strategyRunGeneration:1),false);
        var d=Definition(Path.Combine(root,"reject-close-"+failedCallback));service.Add(d,0,3000);
        foreach(var e in Events().Where(e=>e.Timestamp<=T.AddSeconds(24)))source.Send(e);
        Check(gateway.Requests.Count==1,"reject-close sends exactly one fake entry");
        gateway.Reject(gateway.Requests[0],failedCallback);
        Check(service.GetSnapshots().Single().Position==0,"rejected entry never creates position");
        Check(service.GetCloseStatus(d.InstanceId).QuarantinedBrokerOrders==0,"confirmed zero-fill rejection must not block close for ten minutes");
        Check(service.RequestClose(d.InstanceId,true,out _),"zero-fill rejected strategy can close immediately");
        Check(service.GetSnapshots().Single().State==StrategyLifecycleState.Stopped && !service.GetCloseStatus(d.InstanceId).ClosePending,"rejected strategy fully stopped");
        Check(service.GetShutdownWarnings().Count==0,"zero-fill rejection must not block main application shutdown");
        Check(gateway.Requests.Count==1,"close never submits phantom flatten after rejection");
        Console.WriteLine("PASS zero-fill rejected entry closes strategy and releases shutdown gate: "+failedCallback);
    }
    static void UnsettledTerminalStillBlocked()
    {
        foreach(bool cancelled in new[]{false,true}) {
            var gateway=new Gateway();using var execution=new KgiLiveStrategyExecutionService(gateway,"reject-protection",U.Tradable,new ContractInfo{ExchangeNm="JPX",ProductId="225MC",Symbol="JPX.225MC-202610",ComYM2="202610",CPF=CPFEnum.Futures,ComType='0'},36,now:()=>T,strategyRunGeneration:1);
            execution.Submit(new(){StrategyInstanceId="reject-protection",StrategyRunGeneration=1,ClientOrderId="entry",CycleId="cycle",Symbol=U.Tradable.Symbol,Side=BuySell.Buy,Role=PaperOrderRole.EntryLong,Type=PaperOrderType.Market,Quantity=1,Price=195},T);
            if(cancelled)gateway.CancelOrder(gateway.Requests[0].OrderId);else gateway.Reject(gateway.Requests[0],false,1);
            Check(execution.QuarantinedBrokerOrderCount==1,"cancelled zero fill or rejected undelivered fill retains settlement protection");
            bool blocked=false;try{execution.Dispose();}catch(InvalidOperationException){blocked=true;}Check(blocked,"unsettled terminal must still prevent disposal");
            gateway.Fill(gateway.Requests[0],195);
            Check(execution.QuarantinedBrokerOrderCount==0,"late fill settles only after execution delivered");
        }
        Console.WriteLine("PASS unresolved terminal fills retain shutdown protection");
    }
    sealed class Clock:IEventDrivenStrategyClock {public DateTimeOffset Now{get;private set;} public void Advance(DateTimeOffset at)=>Now=at;}
    sealed class Source:IMarketEventSource {public event Action<MarketEvent>? MarketEventReceived;public void Send(MarketEvent e)=>MarketEventReceived?.Invoke(e);public void Subscribe(IEnumerable<ResolvedProduct> p){}public void Dispose(){}}
    sealed class Gateway:ILiveStrategyOrderGateway
    {
        public bool IsLoggedIn=>true;public string BrokerId=>"FAKE";public string Account=>"FAKE";public List<OrderRequest> Requests=new();
        public event Action<OrderInfo>? OrderStateChanged;public event Action<OrderInfo,int,double>? OrderFilled;
        public event Action<OrderInfo,string>? OrderFailed; public event Action<OrderInfo,string>? CancelFailed {add{} remove{}}
        public void PlaceOrder(OrderRequest r){Requests.Add(r);OrderStateChanged?.Invoke(Info(r,OrderStatus.Accepted));}
        public void CancelOrder(string id){var r=Requests.Single(r=>r.OrderId==id);OrderStateChanged?.Invoke(Info(r,OrderStatus.Cancelled));}
        public void Fill(OrderRequest r,decimal price){var info=Info(r,OrderStatus.Filled);info.FilledQty=r.Quantity;info.LeavesQty=0;OrderFilled?.Invoke(info,r.Quantity,(double)price);}
        public void Reject(OrderRequest r,bool failedCallback,int filled=0){var info=Info(r,OrderStatus.Rejected);info.FilledQty=filled;info.Message="保證金不足";if(failedCallback)OrderFailed?.Invoke(info,info.Message);else OrderStateChanged?.Invoke(info);}
        private static OrderInfo Info(OrderRequest r,OrderStatus state)=>new(){OrderId=r.OrderId,Instrument=r.Instrument,BuySell=r.BuySell,OrderPrice=r.Price,OrderQty=r.Quantity,LeavesQty=state==OrderStatus.Cancelled?0:r.Quantity,Status=state,BrokerId=r.BrokerId,Account=r.Account,Identity=new ExternalOrderIdentity{OrdNo="FAKE-"+r.OrderId}};
    }
}


