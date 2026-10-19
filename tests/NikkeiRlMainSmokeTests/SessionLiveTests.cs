using KGIOptionPricing;
using KGIOptionPricing.Strategies.NikkeiRlDirection;
using KGIOptionPricing.StrategyFramework;
using KGIOptionPricing.StrategyFramework.Execution;
using KGIOptionPricing.OrderManagement.Core;

static class SessionLiveTests
{
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static readonly ResolvedStrategyUniverse Universe=new(){Tradable=new(){ProductId="225MC",Symbol="MC",TickSize=5,ContractMultiplier=10},References=[new(){ProductId="225M",Symbol="M",TickSize=5,ContractMultiplier=100}]};
    static StrategyDefinition Definition(string id,bool live=false)=>new(){StrategyType=NikkeiRlFactory.TypeName,InstanceId=id,Universe=new(){Tradable=new("225MC"),References=[new("225M")]},Parameters=live?new Dictionary<string,string>{["LiveTradingEnabled"]="true"}:new Dictionary<string,string>()};
    static MarketEvent Book(string product,DateTimeOffset at)=>new(){ProductId=product,Symbol=product=="225MC"?"MC":"M",Kind=MarketEventKind.Book,Timestamp=at,BidPrices=[40000,39995,39990,39985,39980],AskPrices=[40005,40010,40015,40020,40025],BidQuantities=[10,10,10,10,10],AskQuantities=[10,10,10,10,10]};
    static MarketEvent Trade(string product,DateTimeOffset at)=>new(){ProductId=product,Symbol=product=="225MC"?"MC":"M",Kind=MarketEventKind.Trade,Timestamp=at,LastPrice=40005,TradeQuantity=1};
    public static void Run()
    {
        foreach(var text in new[]{"2026-10-09T16:00:00+08:00","2026-10-10T00:00:00+08:00"}){
            var start=DateTimeOffset.Parse(text);var clock=new TestClock{Now=start};var policy=new ForcedPolicy();
            var s=new NikkeiRlStrategy(Definition("night"),_=>policy);using var runner=new StrategyInstanceRunner(s,Universe,new KGIOptionPricing.StrategyFramework.Performance.PaperPortfolioLedger(),clock,36);
            long seq=0;void Feed(MarketEvent e){clock.Now=e.Timestamp;runner.OnMarketEvent(e with{Sequence=++seq});}
            for(int sec=0;sec<216;sec++){
                Feed(Book("225MC",start.AddSeconds(sec)));Feed(Book("225M",start.AddSeconds(sec)));
                Feed(Trade("225MC",start.AddSeconds(sec).AddMilliseconds(1)));Feed(Trade("225M",start.AddSeconds(sec).AddMilliseconds(1)));
                if(sec<210)Check(policy.LastMask==null||!policy.LastMask[1],"night cannot enter before 210-second warmup");
            }
            Check(s.State==StrategyLifecycleState.Running&&policy.LastMask![1],"night and after-midnight sessions must become trade-ready");
            Check(policy.Observation![39]>0,"night hours-to-close must use next 05:00");
            runner.RequestClose("test");
        }
        Check(!LiveStrategyService.TryValidateResearchStrategy(Definition("default"),out _),"live requires explicit opt-in");
        var live=Definition("live",true);
        Check(LiveStrategyService.TryValidateResearchStrategy(live,out _),"explicit RL live opt-in must pass service gate");
        var strategy=new NikkeiRlStrategy(live,_=>new ForcedPolicy());StrategyLiveCapabilityGate.EnsureLiveCapable(strategy);
        using(var panel=new NikkeiRlPanel()){
            panel.ConfigureLive(true);Check(panel.StartButton.Enabled,"live start control enabled");
            Check(!panel.ReplayButton.Visible,"live replay hidden");
            Check(LiveStrategyService.TryValidateResearchStrategy(panel.CreateDefinition(),out _),"live panel creates opted-in definition");
            panel.ConfigureLive(false);Check(!LiveStrategyService.TryValidateResearchStrategy(panel.CreateDefinition(),out _),"simulation definition cannot opt in to live");
        }
        BoundariesAndReset();FakeLive();FakeLive(true);FakeLive(true,true);CancelAcknowledgement();
        Console.WriteLine("PASS night/midnight sessions, 210-second warmup, explicit live opt-in and live panel");
    }
    static void BoundariesAndReset()
    {
        DateTimeOffset At(string time)=>DateTimeOffset.Parse("2026-10-10T"+time+"+08:00");
        Check(NikkeiRlSession.At(At("04:54:49"))!.AllowsEntry(At("04:54:49")),"night accepts until cutoff");
        Check(!NikkeiRlSession.At(At("04:54:50"))!.AllowsEntry(At("04:54:50")),"night cutoff inclusive");
        Check(NikkeiRlSession.At(At("05:00:00"))==null&&NikkeiRlSession.At(At("15:00:00"))==null,"breaks block entry");
        Check(NikkeiRlSession.At(At("16:00:00"))!.Close==At("05:00:00").AddDays(1),"night close rolls next date");
        Check(!NikkeiRlSession.At(At("14:39:50"))!.AllowsEntry(At("14:39:50")),"day cutoff preserved");
        Check(NikkeiRlSession.At(At("16:00:00"),false)==null,"night flag can disable session");
        var start=At("04:50:00");var clock=new TestClock{Now=start};var policy=new ForcedPolicy();
        var s=new NikkeiRlStrategy(Definition("night-close"),_=>policy);var ledger=new KGIOptionPricing.StrategyFramework.Performance.PaperPortfolioLedger();
        using var runner=new StrategyInstanceRunner(s,Universe,ledger,clock,36);long seq=0;
        void Feed(MarketEvent e){clock.Now=e.Timestamp;runner.OnMarketEvent(e with{Sequence=++seq});}
        for(int sec=0;sec<240;sec++){Feed(Book("225MC",start.AddSeconds(sec)));Feed(Book("225M",start.AddSeconds(sec)));Feed(Trade("225MC",start.AddSeconds(sec).AddMilliseconds(1)));Feed(Trade("225M",start.AddSeconds(sec).AddMilliseconds(1)));}
        policy.Action=1;Feed(Trade("225MC",start.AddSeconds(240)));Feed(Trade("225MC",start.AddSeconds(240).AddMilliseconds(1)) with{LastPrice=39995});
        Check(ledger.GetPosition(s.InstanceId,"MC").NetQuantity==1,"night position entered before cutoff");
        Feed(Trade("225MC",At("04:54:51")));
        Check(ledger.GetPosition(s.InstanceId,"MC").NetQuantity==0&&s.GetSnapshot().Reason.Contains("夜盤停止"),"night cutoff flattens and blocks entry");
        Feed(Book("225MC",At("07:45:00")));Feed(Book("225M",At("07:45:00")));
        Feed(Trade("225MC",At("07:45:03")));
        Check(!policy.LastMask![1]&&s.State==StrategyLifecycleState.WarmingUp,"new session restarts warmup");
        runner.RequestClose("test");
    }
    static void CancelAcknowledgement()
    {
        var at=DateTimeOffset.Parse("2026-10-09T16:00:00+08:00");var policy=new ForcedPolicy();
        var s=new NikkeiRlStrategy(Definition("cancel-ack"),_=>policy);
        s.Start(new(Universe,new TestClock{Now=at},new EmptyView(),1));
        var request=new PaperOrderRequest{StrategyInstanceId=s.InstanceId,StrategyRunGeneration=1,ClientOrderId="entry",CycleId="cycle",Symbol="MC",Side=BuySell.Buy,Role=PaperOrderRole.EntryLong,Price=40000,Quantity=1};
        s.OnOrderSubmitted(request);s.RequestClose("test");
        Check(s.State!=StrategyLifecycleState.Stopped&&!policy.Disposed,"close waits for all outstanding order acknowledgements");
        s.OnOrderLifecycle(new(){StrategyInstanceId=s.InstanceId,StrategyRunGeneration=1,ClientOrderId="entry",CycleId="cycle",State=OrderLifecycleState.Cancelled});
        Check(s.State==StrategyLifecycleState.Stopped&&policy.Disposed,"cancel acknowledgement completes close");
    }
    static void FakeLive(bool closeWhileOpen=false,bool riskExit=false)
    {
        var start=DateTimeOffset.Parse("2026-10-09T16:00:00+08:00");var clock=new TestClock{Now=start};var policy=new ForcedPolicy();var gateway=new Gateway();
        using var source=new Source();using var host=new StrategyHost(source,clock);
        var recordingRoot=Path.Combine(AppContext.BaseDirectory,"StrategyLiveRuns");
        var priorRuns=Directory.Exists(recordingRoot)?Directory.GetDirectories(recordingRoot).ToHashSet():new HashSet<string>();
        NikkeiRlStrategy? actual=null;
        using var service=new LiveStrategyService(host,()=>gateway.IsLoggedIn,()=>"FAKE",_=>Universe,d=>actual=new NikkeiRlStrategy(d,_=>policy),
            (d,u,fee)=>new KgiLiveStrategyExecutionService(gateway,d.InstanceId,u.Tradable,new ContractInfo{ExchangeNm="JPX",ProductId="225MC",Symbol="MC",ComYM2="202612",CPF=CPFEnum.Futures,ComType='0'},fee,now:()=>clock.Now,
                strategyNetPosition:g=>host.Ledger.GetPosition(d.InstanceId,g,"MC").NetQuantity,strategyRunGeneration:1),false);
        var definition=Definition(riskExit?"fake-risk-live":closeWhileOpen?"fake-close-live":"fake-live",true);service.Add(definition,36,10000);long seq=0;
        void Feed(MarketEvent e){clock.Now=e.Timestamp;source.Emit(e with{Sequence=++seq});}
        for(int sec=0;sec<240;sec++){Feed(Book("225MC",start.AddSeconds(sec)));Feed(Book("225M",start.AddSeconds(sec)));Feed(Trade("225MC",start.AddSeconds(sec).AddMilliseconds(1)));Feed(Trade("225M",start.AddSeconds(sec).AddMilliseconds(1)));}
        policy.Action=1;Feed(Trade("225MC",start.AddSeconds(240)));
        Feed(Trade("225MC",start.AddSeconds(240).AddMilliseconds(1)) with{LastPrice=40000});
        Check(gateway.Requests.Count==0,"fake live does not trigger at touch");
        Feed(Trade("225MC",start.AddSeconds(240).AddMilliseconds(2)) with{LastPrice=39995});
        Check(gateway.Requests.Count==1&&gateway.Requests[0].TimeInForce==TimeInForce.IOC&&gateway.Requests[0].Quantity==1,"strict-cross triggers one broker market IOC");
        gateway.Fill(gateway.Requests[0],40000);
        Check(service.GetSnapshots().Single().Position==1,"actual fake broker fill updates shared live ledger");
        if(closeWhileOpen){
            if(riskExit)Check(host.RequestRiskExit(definition.InstanceId,"test risk"),"risk exit starts protection");
            else Check(!service.RequestClose(definition.InstanceId,true,out _),"close while holding waits for actual fill");
            Check(gateway.Requests.Count==2&&service.GetSnapshots().Single().State!=StrategyLifecycleState.Stopped,"close submits one flatten and waits for broker fill");
            gateway.Fill(gateway.Requests[1],40005);service.Pulse(clock.Now);
            Check(actual!.State==StrategyLifecycleState.Stopped,"close/risk exit releases model only after settlement");
            var path=Directory.GetDirectories(recordingRoot).Where(dir=>!priorRuns.Contains(dir)&&File.Exists(Path.Combine(dir,"run-manifest.json"))&&File.ReadAllText(Path.Combine(dir,"run-manifest.json")).Contains("\"instanceId\":\""+definition.InstanceId+"\""))
                .OrderByDescending(dir=>Directory.GetCreationTimeUtc(dir)).First();
            Check(!File.Exists(Path.Combine(path,"executions.csv")),"performance-only live recording must not create execution detail files");
            Check(!Directory.EnumerateFiles(path,"*.jsonl").Any(),"performance-only live recording must not create event, decision or order logs");
            Check(File.Exists(Path.Combine(path,"run-summary.json")),"close completes common recording summary");
            using var performance=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(path,"run-summary.json")));
            Check(performance.RootElement.GetProperty("completedTrades").GetInt64()==1,"performance-only summary must retain the completed broker round trip");
            Console.WriteLine("PASS live close waits for broker fill and retains final execution in common recording");return;
        }
        service.Pause(definition.InstanceId);
        Feed(Trade("225MC",start.AddSeconds(240).AddMilliseconds(3)) with{LastPrice=39845});
        Check(gateway.Requests.Count==2&&gateway.Requests[1].BuySell==BuySell.Sell,"paused live stop retained");
        gateway.Fill(gateway.Requests[1],39840);
        Check(service.GetSnapshots().Single().Position==0&&service.GetSnapshots().Single().NetPnl==-1672,"broker fill price and costs retained");
        Check(service.RequestClose(definition.InstanceId,true,out _),"fake live closes after settlement");
        Console.WriteLine("PASS fake live service, IOC trigger, broker fills, paused stop, shared costs and shutdown");
    }
    sealed class EmptyView:IStrategyPerformanceView{public PositionSnapshot GetPosition(string id,string symbol)=>new();}
    sealed class Source:IMarketEventSource{public event Action<MarketEvent>? MarketEventReceived;public void Subscribe(IEnumerable<ResolvedProduct> products){}public void Emit(MarketEvent e)=>MarketEventReceived?.Invoke(e);public void Dispose(){}}
    sealed class Gateway:ILiveStrategyOrderGateway
    {
        public bool IsLoggedIn=>true;public string BrokerId=>"FAKE";public string Account=>"FAKE";public List<OrderRequest> Requests=new();
        public event Action<OrderInfo>? OrderStateChanged;public event Action<OrderInfo,int,double>? OrderFilled;
        public event Action<OrderInfo,string>? OrderFailed{add{}remove{}}public event Action<OrderInfo,string>? CancelFailed{add{}remove{}}
        public void PlaceOrder(OrderRequest r){Requests.Add(r);OrderStateChanged?.Invoke(Info(r,OrderStatus.Accepted));}
        public void CancelOrder(string id)=>OrderStateChanged?.Invoke(Info(Requests.Single(r=>r.OrderId==id),OrderStatus.Cancelled));
        public void Fill(OrderRequest r,decimal price){var info=Info(r,OrderStatus.Filled);info.FilledQty=r.Quantity;info.LeavesQty=0;OrderFilled?.Invoke(info,r.Quantity,(double)price);}
        static OrderInfo Info(OrderRequest r,OrderStatus state)=>new(){OrderId=r.OrderId,Instrument=r.Instrument,BuySell=r.BuySell,OrderPrice=r.Price,OrderQty=r.Quantity,LeavesQty=state==OrderStatus.Cancelled?0:r.Quantity,Status=state,BrokerId=r.BrokerId,Account=r.Account,Identity=new ExternalOrderIdentity{OrdNo="FAKE-"+r.OrderId}};
    }
}
