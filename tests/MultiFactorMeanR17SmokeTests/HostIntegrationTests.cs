using KGIOptionPricing.StrategyFramework;
using KGIOptionPricing.Strategies.MultiFactorMeanR17;

static class HostIntegrationTests
{
    sealed class Clock:IStrategyClock{public DateTimeOffset Now{get;set;}}
    sealed class Market:IMarketEventSource
    {
        public event Action<MarketEvent>? MarketEventReceived;
        public void Subscribe(IEnumerable<ResolvedProduct> products){}
        public void Emit(MarketEvent e)=>MarketEventReceived?.Invoke(e);
        public void Dispose()=>MarketEventReceived=null;
    }
    static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
    public static void Run()
    {WeightedMidCase();Case(1);Case(-1);}
    static void WeightedMidCase()
    {
        var at=DateTimeOffset.Parse("2026-09-28T08:00:00+08:00");
        var book=new MarketEvent{Timestamp=at,BidPrices=new[]{1000m},AskPrices=new[]{1005m},BidQuantities=new[]{75},AskQuantities=new[]{25}};
        Check(WeightedMidEntryPolicy.Calculate(at,book,5,2,0)==1003.75m,"225M交叉權重計算");
        Check(WeightedMidEntryPolicy.Calculate(at,book,5,2,1)==1001.25m,"225M同側權重計算");
        Check(WeightedMidEntryPolicy.Calculate(at.AddSeconds(3),book,5,2,0)==null,"過期225M報價不得顯示舊中價");
        Check(WeightedMidEntryPolicy.Calculate(at.AddSeconds(-1),book,5,2,0)==null,"未來報價不得顯示中價");
        Check(WeightedMidEntryPolicy.Calculate(at,book with{BidQuantities=new[]{0}},5,2,0)==null,"無委託量不得顯示中價");
        Check(WeightedMidEntryPolicy.Calculate(at,book with{AskPrices=new[]{1000m}},5,2,0)==null,"鎖定報價不得顯示中價");
    }
    static void Case(int direction)
    {
        string root=Path.Combine(Path.GetTempPath(),"KGI-R17IntegrationTests",Guid.NewGuid().ToString("N"));
        try
        {
            var at=DateTimeOffset.Parse("2026-09-28T08:00:00+08:00");var clock=new Clock{Now=at};
            using var source=new Market();using var host=new StrategyHost(source,clock);
            var universe=new ResolvedStrategyUniverse{Tradable=new(){ProductId="225MC",Symbol="225MC-Z26",ContractMonth="202612",TickSize=5,ContractMultiplier=10},References=new[]{new ResolvedProduct{ProductId="225M",Symbol="225M-Z26",ContractMonth="202612",TickSize=5,ContractMultiplier=100},new ResolvedProduct{ProductId="225",Symbol="225-Z26",ContractMonth="202612",TickSize=10,ContractMultiplier=1000}}};
            MultiFactorMeanR17Strategy Make(string id)=>new(new(){StrategyType="MultiFactorMeanR17",InstanceId=id,Parameters=new Dictionary<string,string>{{"HistoryRoot",Path.Combine(root,id)}}});
            var a=Make("warm");var runnerA=host.Add(a,universe,36);a.Pause("isolation fixture");
            long sequence=0;int lastSecond=0;
            void Feed(int second)
            {
                lastSecond=second;
                foreach(bool trade in new[]{false,true})foreach(var product in new[]{"225","225M","225MC"})
                {
                    decimal tick=product=="225"?10:5;
                    decimal bid=product=="225"?(direction>0?2000+second*10:5000-second*10):product=="225MC"?1000+(second%2)*10:1000;
                    clock.Now=at.AddSeconds(second).AddMilliseconds(trade?100:0);
                    source.Emit(new(){ProductId=product,Symbol=product+"-Z26",Timestamp=clock.Now,Sequence=++sequence,ConnectionGeneration=1,Kind=trade?MarketEventKind.Trade:MarketEventKind.Book,
                        LastPrice=trade?(direction>0?bid+tick:bid):0,TradeQuantity=trade?11:0,
                        BidPrices=new[]{bid,bid-tick,bid-2*tick},AskPrices=new[]{bid+tick,bid+2*tick,bid+3*tick},BidQuantities=new[]{50,30,20},AskQuantities=new[]{50,30,20}});
                }
            }
            for(int i=0;i<=80;i++)Feed(i);
            Check(runnerA.GetSnapshot().Features["225MC.ResearchMeanDeviationTicks"].HasValue,"暖機實例必須自行維護已完成秒視窗");
            var b=Make("cold");var runnerB=host.Add(b,universe,36);b.Pause("isolation fixture");Feed(81);
            Check(runnerA.GetSnapshot().Features.TryGetValue("225M.WeightedMid",out var miniMid) && miniMid==1002.5m,
                "R17快照需發布225M第一檔量加權中價供UI顯示");
            Check(!runnerB.GetSnapshot().Features["225MC.ResearchMeanDeviationTicks"].HasValue,"新實例不得讀取其他實例歷史");
            Check(runnerA.GetSnapshot().Features["225MC.ResearchMeanDeviationTicks"].HasValue,"建立新實例不得清空舊實例視窗");
            PaperOrderRequest? entry=null;a.OrderRequested+=r=>{if(r.Role is PaperOrderRole.EntryLong or PaperOrderRole.EntryShort)entry??=r;};
            var observed=new List<StrategyDecisionObservation>();a.DecisionObserved+=observed.Add;
            a.Resume();for(int i=82;i<180 && entry==null;i++)Feed(i);
            Check(entry!=null,"固定R17預設值與有效多商品行情應能經共用host進場："+System.Text.Json.JsonSerializer.Serialize(runnerA.GetSnapshot()));
            Check(entry!.Quantity==1 && entry.StrategyInstanceId=="warm","新策略須保持一口與實例歸屬");
            Check(entry.Role==(direction>0?PaperOrderRole.EntryLong:PaperOrderRole.EntryShort),"多空方向需一致");
            Check(observed.Any(o=>o.Thresholds.Any(t=>t.Name=="R17.ScoreMagnitude") && o.Thresholds.Any(t=>t.Name=="R17.MiniSignedCompletedReturn")),"決策紀錄須包含R17訊號及Mini報酬門檻");
            for(int i=0;i<3 && host.Ledger.GetPosition("warm",runnerA.StrategyRunGeneration,universe.Tradable.Symbol).NetQuantity==0;i++)Feed(lastSecond+1);
            var position=host.Ledger.GetPosition("warm",runnerA.StrategyRunGeneration,universe.Tradable.Symbol);
            Check(position.NetQuantity==direction,"共用broker應回報正確多空進場成交");
            var s=runnerA.GetSnapshot();decimal distance=Math.Abs(position.AveragePrice-s.StopPrice);
            Check(distance>0 && distance<=150 && distance%5==0,"實際成交後停損必須對齊跳動點且上限150點");
            a.Pause("protection fixture");Check(a.ShouldPulseWhilePaused,"有部位暫停須保留保護心跳");
            host.RequestClose("warm","fixture close");Feed(lastSecond+1);Feed(lastSecond+1);
            Check(host.Ledger.GetPosition("warm",runnerA.StrategyRunGeneration,universe.Tradable.Symbol).NetQuantity==0,"關閉須由共用broker平倉");
            Check(host.Ledger.GetPosition("cold",runnerB.StrategyRunGeneration,universe.Tradable.Symbol).NetQuantity==0,"關閉不得影響其他實例");
            foreach(var invalid in new[]{"31","0","1.5"})
            {
                bool rejected=false;try{R17Parameters.Parse(new Dictionary<string,string>{{"ResearchVolatilityMaxStopTicks",invalid}});}catch(ArgumentException){rejected=true;}
                Check(rejected,"停損跳數超限、零與非整數必須拒絕");
            }
            Console.WriteLine("PASS R17 host/isolation/fill-risk/pause/close");
        }
        finally{if(!Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.Combine(Path.GetTempPath(),"KGI-R17IntegrationTests"))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new Exception("fixture cleanup containment");if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
