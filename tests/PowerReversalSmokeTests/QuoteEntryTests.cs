using KGIOptionPricing;
using KGIOptionPricing.OrderManagement.Core;
using KGIOptionPricing.Strategies.PowerReversal;
using KGIOptionPricing.StrategyFramework;
using KGIOptionPricing.StrategyFramework.Execution;

static class QuoteEntryTests
{
    static readonly DateTimeOffset T=new(2026,10,8,8,0,0,TimeSpan.FromHours(8));
    static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
    static MarketEvent Book(decimal bid,decimal ask,int bq=9,int aq=1)=>new(){Timestamp=T,Kind=MarketEventKind.Book,BidPrices=new[]{bid},AskPrices=new[]{ask},BidQuantities=new[]{bq},AskQuantities=new[]{aq}};
    public static void Run()
    {
        var micro=Book(100,110);var large=Book(100,110,1,9);
        var high=WeightedMidEntryPolicy.Evaluate(T,micro,large,5,10,2,1,0,1);
        Check(high.Ready && high.MicroMid==109 && high.LargeMid==101 && !high.UseMarket,"cross-weight higher micro buy selects resting limit");
        var low=WeightedMidEntryPolicy.Evaluate(T,micro,large,5,10,2,1,1,1);
        Check(low.Ready && low.MicroMid==101 && low.LargeMid==109 && low.UseMarket,"same-side lower micro buy selects market");
        Check(WeightedMidEntryPolicy.Evaluate(T,micro,micro,5,10,2,1,0,1).UseMarket,"equal weighted mid buy uses market");
        Check(!WeightedMidEntryPolicy.Evaluate(T,micro,micro,5,10,2,1,0,-1).UseMarket,"equal weighted mid sell uses resting limit");
        Check(WeightedMidEntryPolicy.Evaluate(T,micro,large,5,10,2,1,0,-1).UseMarket,"higher micro sell uses market");
        Check(!WeightedMidEntryPolicy.Evaluate(T,micro,large,5,10,2,1,1,-1).UseMarket,"lower micro sell uses resting limit");
        Check(!WeightedMidEntryPolicy.Evaluate(T,micro,large with{Timestamp=T.AddSeconds(-3)},5,10,2,1,0).Ready,"stale large book blocks");
        Check(!WeightedMidEntryPolicy.Evaluate(T,micro,large with{Timestamp=T.AddSeconds(1)},5,10,2,1,0).Ready,"future book blocks");
        Check(!WeightedMidEntryPolicy.Evaluate(T,micro,large with{Timestamp=T.AddSeconds(-1.5)},5,10,2,1,0).Ready,"cross-product skew blocks");
        foreach(var invalid in new[]{Book(110,100),Book(100,110,0,0),Book(100,110,-1,9),Book(100,110) with{AskQuantities=Array.Empty<int>()},Book(101,110)})
            Check(!WeightedMidEntryPolicy.Evaluate(T,micro,invalid,5,10,2,1,0).Ready,"invalid top book blocks");
        var huge=Book(100,110,int.MaxValue,int.MaxValue);
        Check(WeightedMidEntryPolicy.Evaluate(T,huge,huge,5,10,2,1,0).MicroMid==105,"quantity sum does not overflow int");
        Check(!WeightedMidEntryPolicy.Evaluate(T,micro,large with{ConnectionGeneration=2},5,10,2,1,0).Ready,"different feed generations block");
        MiniDisplay();
        Flow(1,1,false);Flow(-1,1,true);
        Flow(1,-1,true);Flow(-1,-1,false);
        Flow(1,0,true);Flow(-1,0,false);
        Flow(1,1,false,"book");Flow(1,1,false,"contract");
        Console.WriteLine("PASS weighted-mid formulas, gates, hybrid long/short, resting timeout retry and late-fill protection");
    }
    sealed class View:IStrategyPerformanceView{public PositionSnapshot Position=new();public PositionSnapshot GetPosition(string id,string symbol)=>Position;}
    static void MiniDisplay()
    {
        foreach(int formula in new[]{0,1}){
            var u=new ResolvedStrategyUniverse{Tradable=new(){ProductId="225MC",Symbol="MC",TickSize=5},
                References=new[]{new ResolvedProduct{ProductId="225M",Symbol="M",TickSize=5},new ResolvedProduct{ProductId="225",Symbol="L",TickSize=10}}};
            var s=new PowerReversalStrategy(new(){StrategyType="PowerReversal",InstanceId="mini-display",Parameters=new Dictionary<string,string>{["WeightedMidMode"]=formula.ToString(),["MaxDataAgeSeconds"]="5"}});
            s.Start(new(u,new SystemStrategyClock(),new View(),1));
            decimal? Mid()=>s.GetSnapshot().Features.GetValueOrDefault("225M.WeightedMid");
            Check(Mid()==null,"missing mini book stays unavailable");
            var book=Book(100,110) with{Symbol="M",ProductId="225M",Sequence=1};s.OnMarketEvent(book);
            Check(Mid()==(formula==0?109:101),"mini uses its own quantities and selected formula without other product books");
            s.OnMarketEvent(book with{Timestamp=T.AddSeconds(1),Sequence=2,BidQuantities=new[]{0}});
            Check(Mid()==null,"invalid mini book replaces previous good value");
            s.OnMarketEvent(book with{Timestamp=T.AddSeconds(2),Sequence=3});
            Check(Mid().HasValue,"fresh mini book restores value");
            s.OnHeartbeat(T.AddSeconds(8));Check(Mid()==null,"stale mini book clears display");
        }
        Check(WeightedMidEntryPolicy.Calculate(T,Book(100,110) with{Timestamp=T.AddSeconds(1)},5,5,0)==null,"future mini book unavailable");
    }
    static void Flow(int direction,int comparison,bool market,string invalid="")
    {
        var p=new Dictionary<string,string>{["SignalMode"]="1",["EntryExecutionMode"]="1",["WeightedMidMode"]="0",["PowerWindowSeconds"]="10",["ShortAverageSeconds"]="1",["LongAverageSeconds"]="1",["PowerThreshold"]="0",["BaisThreshold"]="0",["ConfirmSeconds"]="1",["MinQuantity"]="1",["MinTradeCount"]="1",["MaxDataAgeSeconds"]="5",["MaxSkewSeconds"]="5",["CommissionPerSide"]="0",["SlippageTicks"]="0",["CooldownSeconds"]="20"};
        var u=new ResolvedStrategyUniverse{Tradable=new(){ProductId="225MC",Symbol="MC",TickSize=5,ContractMultiplier=10,ContractMonth="202612"},References=new[]{new ResolvedProduct{ProductId="225M",Symbol="M",TickSize=5,ContractMultiplier=100,ContractMonth="202612"},new ResolvedProduct{ProductId="225",Symbol="L",TickSize=10,ContractMultiplier=1000,ContractMonth=invalid=="contract"?"202609":"202612"}}};
        var view=new View();var s=new PowerReversalStrategy(new(){StrategyType="PowerReversal",InstanceId="quote-test",Parameters=p});
        var orders=new List<PaperOrderRequest>();var cancels=new List<string>();DateTimeOffset sent=T;
        s.OrderRequested+=r=>{orders.Add(r);sent=s.GetSnapshot().UpdatedAt;s.OnOrderSubmitted(r);s.OnOrderLifecycle(new(){StrategyInstanceId=r.StrategyInstanceId,StrategyRunGeneration=r.StrategyRunGeneration,ClientOrderId=r.ClientOrderId,CycleId=r.CycleId,State=OrderLifecycleState.Working});};
        s.CancelOrderRequested+=cancels.Add;s.Start(new(u,new SystemStrategyClock(),view,1));long sequence=0;
        decimal ask=direction>0?100:200;
        void Feed(int sec){foreach(var product in u.References.Append(u.Tradable)){
            bool big=product.ProductId=="225";decimal bid=big?ask+(comparison>0?-20:comparison<0?0:-10):ask-5;
            s.OnMarketEvent(new(){Timestamp=T.AddSeconds(sec),Sequence=++sequence,Symbol=product.Symbol,ProductId=product.ProductId,Kind=big?MarketEventKind.Book:MarketEventKind.Trade,LastPrice=big?0:direction>0?ask:ask-5,TradeQuantity=big?0:11,BidPrices=new[]{bid},AskPrices=new[]{big?bid+10:ask},BidQuantities=new[]{big && invalid=="book" && sec>=10?0:big && comparison==0?3:10},AskQuantities=new[]{big && comparison==0?1:10}});
        }}
        for(int sec=0;sec<=12;sec++)Feed(sec);
        if(invalid.Length>0){Check(orders.Count==0,"invalid latest book or mismatched contract blocks actual strategy");return;}
        Check(orders.Count==1,"one entry while working");var first=orders[0];
        Check(first.Type==(market?PaperOrderType.Market:PaperOrderType.BrokerLimit),"directional weighted-mid rule chooses expected actual order type");
        Check(first.Price==(market?(direction>0?ask:ask-5):(direction>0?ask-5:ask)),"passive buy at bid / sell at ask");
        Check(first.TimeInForce==TimeInForce.ROD,"resting ROD intent");
        ExecutionPaths(first,u.Tradable);
        Check(s.GetSnapshot().Features["EntryWeightedMidDifference"].HasValue,"comparison visible in snapshot");
        Check(s.GetSnapshot().Features.GetValueOrDefault("225M.WeightedMid")==ask-2.5m,"225M weighted mid visible in snapshot from its own top book");
        if(market)return;
        DateTimeOffset expiry=sent.AddSeconds(10);s.OnHeartbeat(expiry);s.OnHeartbeat(expiry.AddMilliseconds(1));
        Check(cancels.Count==1 && orders.Count==1,"timeout cancels once without duplicate entry");
        Feed((int)(expiry-T).TotalSeconds+1);Check(orders.Count==1,"wait for cancel confirmation");
        s.OnOrderLifecycle(new(){StrategyInstanceId=first.StrategyInstanceId,StrategyRunGeneration=1,ClientOrderId=first.ClientOrderId,CycleId=first.CycleId,State=OrderLifecycleState.Cancelled});
        Check(s.State!=StrategyLifecycleState.Suspended,"normal no-fill cancellation does not stop strategy");
        int retry=(int)(expiry-T).TotalSeconds+22;Feed(retry);Feed(retry+1);Feed(retry+2);
        Check(orders.Count==2 && orders[1].Type==PaperOrderType.BrokerLimit,"retry only after cancellation and cooldown");
        var second=orders[1];view.Position=new(){NetQuantity=direction,AveragePrice=first.Price};
        s.OnExecution(new(){StrategyInstanceId=first.StrategyInstanceId,StrategyRunGeneration=1,ClientOrderId=first.ClientOrderId,CycleId=first.CycleId,Role=first.Role,Symbol="MC",Quantity=1,Price=first.Price,Timestamp=T.AddSeconds(retry+3),ExecutionId="late-old-entry"});
        Check(cancels.Contains(second.ClientOrderId),"late old fill cancels newer working entry");
        Check(orders.Count==3 && orders[2].Role==PaperOrderRole.Flatten,"late old fill protected with one flatten");
        Check(s.State!=StrategyLifecycleState.Stopped,"cannot stop while newer entry cancellation remains unconfirmed");
        // A cancellation can lose the race: both entries fill before the first protective exit.
        view.Position=new(){NetQuantity=2*direction,AveragePrice=first.Price};
        s.OnOrderLifecycle(new(){StrategyInstanceId=second.StrategyInstanceId,StrategyRunGeneration=1,ClientOrderId=second.ClientOrderId,CycleId=second.CycleId,State=OrderLifecycleState.Filled});
        s.OnExecution(new(){StrategyInstanceId=second.StrategyInstanceId,StrategyRunGeneration=1,ClientOrderId=second.ClientOrderId,CycleId=second.CycleId,Role=second.Role,Symbol="MC",Quantity=1,Price=second.Price,Timestamp=T.AddSeconds(retry+4),ExecutionId="late-new-entry"});
        Check(orders.Count==3,"no duplicate protection while first exit working");
        void FillExit(PaperOrderRequest r,int remaining,string id){view.Position=new(){NetQuantity=remaining*direction,AveragePrice=remaining==0?0:first.Price};
            s.OnOrderLifecycle(new(){StrategyInstanceId=r.StrategyInstanceId,StrategyRunGeneration=1,ClientOrderId=r.ClientOrderId,CycleId=r.CycleId,State=OrderLifecycleState.Filled});
            s.OnExecution(new(){StrategyInstanceId=r.StrategyInstanceId,StrategyRunGeneration=1,ClientOrderId=r.ClientOrderId,CycleId=r.CycleId,Role=r.Role,Symbol="MC",Quantity=1,Price=r.Price,Timestamp=T.AddSeconds(retry+5),ExecutionId=id});}
        FillExit(orders[2],1,"protection1");
        Check(orders.Count==4 && orders[3].Role==PaperOrderRole.Flatten && orders[3].Quantity==1,"overfill protection closes one lot at a time");
        FillExit(orders[3],0,"protection2");Check(s.State==StrategyLifecycleState.Stopped,"stop only after all actual exposure and orders cleared");
    }
    static void ExecutionPaths(PaperOrderRequest request,ResolvedProduct product)
    {
        var gateway=new Gateway();using var live=new KgiLiveStrategyExecutionService(gateway,request.StrategyInstanceId,product,
            new ContractInfo{ExchangeNm="JPX",ProductId="225MC",Symbol="MC",ComYM2="202612",CPF=CPFEnum.Futures,ComType='0'},36,now:()=>T,strategyRunGeneration:1);
        live.Submit(request,T);Check(gateway.Requests.Count==1,"real adapter dispatches immediately");var broker=gateway.Requests[0];
        Check(broker.TimeInForce==(request.Type==PaperOrderType.BrokerLimit?TimeInForce.ROD:TimeInForce.IOC),"real limit ROD versus market IOC");
        Check(broker.Price==(request.Type==PaperOrderType.BrokerLimit?(double)request.Price:0),"broker receives actual resting price versus market flag");
        var fills=new List<ExecutionEvent>();live.Filled+=fills.Add;gateway.Fill(broker,request.Price);
        Check(fills.Count==1 && fills[0].Price==request.Price && fills[0].Commission==36,"fake broker fill routed once at actual price and cost");
        if(request.Type!=PaperOrderType.BrokerLimit)return;
        var paper=new CrossPricePaperExecutionService(36);var paperFills=new List<ExecutionEvent>();paper.Filled+=paperFills.Add;paper.Submit(request,T);
        paper.OnMarketEvent(new(){Symbol=request.Symbol,Kind=MarketEventKind.Trade,Timestamp=T.AddSeconds(1),Sequence=1,LastPrice=request.Price,TradeQuantity=11});
        Check(paperFills.Count==0,"touch does not invent queue fill");
        paper.OnMarketEvent(new(){Symbol=request.Symbol,Kind=MarketEventKind.Trade,Timestamp=T.AddSeconds(2),Sequence=2,LastPrice=request.Price+(request.Side==BuySell.Buy?-5:5),TradeQuantity=11});
        Check(paperFills.Count==1 && paperFills[0].Price==request.Price && paperFills[0].Commission==36,"shared strict-cross fill at resting price");
    }
    sealed class Gateway:ILiveStrategyOrderGateway
    {
        public bool IsLoggedIn=>true;public string BrokerId=>"FAKE";public string Account=>"FAKE";public List<OrderRequest> Requests=new();
        public event Action<OrderInfo>? OrderStateChanged;public event Action<OrderInfo,int,double>? OrderFilled;
        public event Action<OrderInfo,string>? OrderFailed{add{} remove{}} public event Action<OrderInfo,string>? CancelFailed{add{} remove{}}
        public void PlaceOrder(OrderRequest r){Requests.Add(r);OrderStateChanged?.Invoke(Info(r,OrderStatus.Accepted));}
        public void CancelOrder(string id){var r=Requests.Single(r=>r.OrderId==id);OrderStateChanged?.Invoke(Info(r,OrderStatus.Cancelled));}
        public void Fill(OrderRequest r,decimal price){var info=Info(r,OrderStatus.Filled);info.FilledQty=r.Quantity;info.LeavesQty=0;OrderFilled?.Invoke(info,r.Quantity,(double)price);}
        static OrderInfo Info(OrderRequest r,OrderStatus state)=>new(){OrderId=r.OrderId,Instrument=r.Instrument,BuySell=r.BuySell,OrderPrice=r.Price,OrderQty=r.Quantity,LeavesQty=state==OrderStatus.Cancelled?0:r.Quantity,Status=state,BrokerId=r.BrokerId,Account=r.Account,Identity=new ExternalOrderIdentity{OrdNo="FAKE-"+r.OrderId}};
    }
}
