using KGIOptionPricing.StrategyFramework;
using System.Reflection;

try
{
    Run("execution-state");Run("close-status");
    Console.WriteLine("PASS live snapshot concurrency / heartbeat / close guards");return 0;
}
catch(Exception e){Console.Error.WriteLine(e);return 1;}

static void Run(string mode)
{
    var host=new StrategyHost(new Source());
    var universe=new ResolvedStrategyUniverse{Tradable=new(){ProductId="225MC",Symbol="225MC-Z26",TickSize=5,ContractMultiplier=10}};
    LiveStrategyService? service=null;
    using var barrier=new Barrier(2);
    bool enabled=true;int entered=0,returned=0;
    var errors=new System.Collections.Concurrent.ConcurrentQueue<Exception>();
    var onState=typeof(LiveStrategyService).GetMethod("OnExecutionStateChanged",BindingFlags.NonPublic|BindingFlags.Instance)!;
    void Callback(string id)
    {
        if(!enabled)return;
        Interlocked.Increment(ref entered);
        if(!barrier.SignalAndWait(TimeSpan.FromSeconds(5)))throw new Exception("fixture barrier timed out");
        if(mode=="execution-state")onState.Invoke(service,new object?[]{id,null});
        else if(!service!.GetCloseStatus(id).Exists)throw new Exception("callback lost instance");
        Interlocked.Increment(ref returned);
    }
    var a=host.Add(new ProbeStrategy("a",Callback),universe);
    var b=host.Add(new ProbeStrategy("b",Callback),universe);
    service=new LiveStrategyService(host,()=>true,()=>"fixture",_=>universe,_=>throw new Exception("no new strategies"),
        (_,_,_)=>throw new Exception("broker must never be created"),enableHeartbeat:false);
    for(int i=0;i<20;i++)
    {
        var e=new MarketEvent{Symbol="225MC-Z26",ProductId="225MC",Kind=MarketEventKind.Book,Timestamp=DateTimeOffset.Now};
        Thread Worker(Action work)=>new(()=>{try{work();}catch(Exception error){errors.Enqueue(error);}}){IsBackground=true};
        var ta=Worker(()=>a.OnMarketEvent(e));var tb=Worker(()=>b.Pulse(e.Timestamp));
        ta.Start();tb.Start();
        if(!ta.Join(2000)||!tb.Join(2000))throw new Exception($"{mode}: cross-strategy callback deadlock; entered={entered}, returned={returned}");
        if(errors.TryDequeue(out var error))throw error;
    }
    enabled=false;
    if(entered!=40||returned!=40)throw new Exception("missing callback transitions");
    if(service.GetCloseStatus("missing").Exists)throw new Exception("unknown instance appeared");
    if(!host.Pause("a")||!host.Resume("a"))throw new Exception("target lifecycle failed");
    host.Pulse(DateTimeOffset.Now);
    if(host.GetSnapshots().Count!=2||service.GetShutdownWarnings().Count!=0)throw new Exception("flat close guards changed");
    host.StopAll("fixture complete");
    if(host.GetSnapshots().Any(s=>s.State!=StrategyLifecycleState.Stopped))throw new Exception("stop failed");
    service.Dispose();
    Console.WriteLine($"PASS {mode}: 20 concurrent market/heartbeat rounds");
}

sealed class Source:IMarketEventSource
{
    public event Action<MarketEvent>? MarketEventReceived {add{}remove{}}
    public void Subscribe(IEnumerable<ResolvedProduct> products){}public void Dispose(){}
}
sealed class ProbeStrategy(string id,Action<string> callback):ISimulationStrategy,IStrategyHeartbeat
{
    public string StrategyType=>"SnapshotConcurrencyProbe";public string InstanceId=>id;
    public StrategyLifecycleState State{get;private set;}=StrategyLifecycleState.Created;
    public event Action<PaperOrderRequest>? OrderRequested {add{}remove{}}
    public event Action? CancelAllRequested {add{}remove{}}
    public event Action<StrategySnapshot>? SnapshotChanged {add{}remove{}}
    public void Start(StrategyRuntimeContext context)=>State=StrategyLifecycleState.Running;
    public void OnMarketEvent(MarketEvent e)=>callback(id);public void OnHeartbeat(DateTimeOffset at)=>callback(id);
    public void OnExecution(ExecutionEvent e){}
    public void Pause(string reason)=>State=StrategyLifecycleState.Suspended;
    public void Resume()=>State=StrategyLifecycleState.Running;
    public void Stop(string reason)=>State=StrategyLifecycleState.Stopped;
    public StrategySnapshot GetSnapshot()=>new(){InstanceId=id,StrategyType=StrategyType,TradableSymbol="225MC-Z26",State=State};
}
