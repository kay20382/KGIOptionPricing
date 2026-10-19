using KGIOptionPricing.Strategies.BuySellPower;
using KGIOptionPricing.StrategyFramework;
using KGIOptionPricing.StrategyFramework.Performance;
using KGIOptionPricing.StrategyFramework.Storage;
using KGIOptionPricing;
using System.Windows.Forms;
using System.Text.Json;

static class Program
{
    static readonly DateTimeOffset T = new(2026, 10, 6, 8, 0, 0, TimeSpan.FromHours(8));
    static int count;
    static void Eq<TValue>(TValue expected, TValue actual, string why)
    { if (!Equals(expected, actual)) throw new Exception($"{why}: expected {expected}, got {actual}"); }
    static void Test(string name, Action run) { run(); count++; Console.WriteLine($"PASS {name}"); }
    static PowerIndicator Core(int window = 10, int shortSeconds = 5, int longSeconds = 10, int n = 2) =>
        new(new PowerParameters(window, shortSeconds, longSeconds, 5m, n));
    static void Trade(PowerIndicator c, int sec, decimal price, int qty = 11, decimal? bid = null, decimal? ask = null) =>
        c.OnTrade(T.AddSeconds(sec), price, qty, bid ?? price - 1, ask ?? price);

    [STAThread]
    static int Main()
    {
        try
        {
            Test("預設反轉點數 30 且保存自訂設定", () => {
                Eq(30m, new PowerParameters().ReversalPoints, "constructor default");
                Eq(30m, PowerParameters.Parse(new Dictionary<string,string>(), "225").ReversalPoints, "parsed default");
                Eq(5m, PowerParameters.Parse(new Dictionary<string,string>{{"225.ReversalPoints","5"}}, "225").ReversalPoints, "explicit setting retained");
            });
            Test("10 倍數與無效報價不影響力道或行情", () => {
                var c = Core(); foreach (int q in new[] {10,20,30}) Trade(c, 0, 100, q);
                c.OnTrade(T, 100, 11, 101, 100); c.OnTrade(T, 100, 11, 0, 101);
                Eq(0L, c.Snapshot().UpQuantity, "volume"); Eq(false, c.Snapshot().HasData, "warmup");
                Eq<MovementSegment?>(null, c.Snapshot().CurrentMovement, "movement");
            });
            Test("11 21 分類與邊界到期", () => {
                var c = Core(); Trade(c, 0, 101, 11, 100, 101); Trade(c, 1, 100, 21, 100, 101);
                Trade(c, 2, 100.5m, 3, 100, 101);
                Eq<decimal?>(-28.5714m, c.Snapshot().BuyPower, "percentage power"); Eq(3L, c.Snapshot().ZeroQuantity, "zero");
                c.Advance(T.AddSeconds(10)); Eq(0L, c.Snapshot().UpQuantity, "expire exactly");
                Eq<decimal?>(-87.5m, c.Snapshot().BuyPower, "remaining shares"); c.Advance(T.AddSeconds(12));
                Eq<decimal?>(null, c.Snapshot().BuyPower, "empty percentage undefined"); Eq(0L, c.Snapshot().ZeroQuantity, "zero expired");
            });
            Test("三類百分比按口數計算且合計 100", () => {
                var c=Core(); Trade(c,0,101,11,100,101); Trade(c,0,100,21,100,101); Trade(c,0,100.5m,3,100,101);
                var s=c.Snapshot(); Eq<decimal?>(31.4286m,s.UpPercent,"up share"); Eq<decimal?>(60m,s.DownPercent,"down share");
                Eq<decimal?>(8.5714m,s.ZeroPercent,"zero share"); Eq<decimal?>(100m,s.UpPercent+s.DownPercent+s.ZeroPercent,"total");
                Eq(s.UpPercent-s.DownPercent,s.BuyPower,"signed difference");
                Trade(c,0,101,30,100,101); Eq(35L,c.Snapshot().TotalQuantity,"10 multiple excluded from denominator");
                c.Advance(T.AddSeconds(10)); Eq<decimal?>(null,c.Snapshot().UpPercent,"no numerator or denominator");
                Eq<decimal?>(null,c.Snapshot().DownPercent,"empty down"); Eq<decimal?>(null,c.Snapshot().ZeroPercent,"empty zero");
            });
            Test("三等比例尾差補足與負 100 力道", () => {
                var c=Core(); Trade(c,0,101,11,100,101); Trade(c,0,100,11,100,101); Trade(c,0,100.5m,11,100,101);
                var s=c.Snapshot(); Eq<decimal?>(33.3333m,s.UpPercent,"up rounded"); Eq<decimal?>(33.3333m,s.DownPercent,"down rounded");
                Eq<decimal?>(33.3334m,s.ZeroPercent,"rounding remainder"); Eq<decimal?>(100m,s.UpPercent+s.DownPercent+s.ZeroPercent,"display total");
                Eq<decimal?>(0m,s.BuyPower,"neutral");
                var down=Core(); Trade(down,0,100,11,100,101); Eq<decimal?>(-100m,down.Snapshot().BuyPower,"all down");
            });
            Test("ZeroTick 到期改變分母與百分比平均", () => {
                var c=Core(10,2,4); Trade(c,0,100.5m,11,100,101); Trade(c,1,101,11,100,101); Trade(c,2,100,21,100,101);
                Eq<decimal?>(-23.2558m,c.Snapshot().BuyPower,"including zero volume");
                c.Advance(T.AddSeconds(10)); Eq<decimal?>(-31.25m,c.Snapshot().BuyPower,"zero expired changes denominator");
                c.Advance(T.AddSeconds(12)); Eq<decimal?>(-65.625m,c.Snapshot().ShortAverage,"(-31.25-100)/2");
                c.Advance(T.AddSeconds(14)); Eq<decimal?>(-32.8125m,c.Snapshot().LongAverage,"percentage integral");
            });
            Test("到期精確積分與無成交時零分母", () => {
                var c = Core(2, 2, 4); Trade(c, 0, 100); Trade(c, 1, 100);
                c.Advance(T.AddSeconds(4));
                // 完整 power 從 t=2 起：100% 持續一秒、空視窗的0持續一秒。
                Eq<decimal?>(50m, c.Snapshot().ShortAverage, "percentage expiry integral");
                Eq<decimal?>(null, c.Snapshot().LongAverage, "long warming");
                c.Advance(T.AddSeconds(6)); Eq<decimal?>(25m, c.Snapshot().LongAverage, "long percentage integral");
                Eq<decimal?>(-25m, c.Snapshot().BaisPower, "short minus long in percentage points");
                c.Advance(T.AddSeconds(8)); Eq<decimal?>(0m, c.Snapshot().BaisPower, "zero long remains calculable");
                Eq("就緒", c.Snapshot().Status, "status");
            });
            Test("成交密度不改變時間加權", () => {
                var a = Core(100, 2, 4); var b = Core(100, 2, 4);
                Trade(a, 0, 100); Trade(b, 0, 100); a.Advance(T.AddSeconds(100)); b.Advance(T.AddSeconds(100));
                Trade(a, 101, 100); Trade(b, 101, 100); // 第一筆已到期，第二筆使 power=100%
                for (int s = 102; s <= 103; s++) b.Advance(T.AddSeconds(s));
                a.Advance(T.AddSeconds(104)); b.Advance(T.AddSeconds(104));
                Eq<decimal?>(75m, a.Snapshot().LongAverage, "100*3/4");
                Eq(a.Snapshot().LongAverage, b.Snapshot().LongAverage, "same path");
            });
            Test("暖機完整 W 加長期秒數", () => {
                var c = Core(); Trade(c, 0, 100); c.Advance(T.AddSeconds(9));
                Eq("暖機中", c.Snapshot().Status, "power warm"); c.Advance(T.AddSeconds(19));
                Eq<decimal?>(null, c.Snapshot().LongAverage, "long warm");
                c.Advance(T.AddSeconds(20)); Eq(true, c.Snapshot().IsWarm, "all warm");
            });
            Test("12 點上漲 14 點下跌與反向點數接續", () => {
                var c = Core(); Trade(c,0,100); Trade(c,1,112); Trade(c,2,107);
                var up = c.Snapshot().History.Single(); Eq(12m,up.Points,"up");
                Eq(T.AddSeconds(1),up.ExtremeTime,"extreme"); Eq(T.AddSeconds(2),up.ConfirmedAt,"confirmation");
                Eq(1m,up.DurationSeconds,"duration"); Eq(112m,c.Snapshot().CurrentMovement!.StartPrice,"down start");
                Eq(5m,c.Snapshot().CurrentMovement!.Points,"reverse retained");
                Trade(c,3,98); Trade(c,4,103); var s = c.Snapshot();
                Eq(14m,s.History.Last().Points,"down"); Eq(98m,s.CurrentMovement!.StartPrice,"new up");
                Eq(T.AddSeconds(3),s.CurrentMovement.StartTime,"pivot time"); Eq(5m,s.CurrentMovement.Points,"up five");
                Eq<decimal?>(12m,s.AverageUpPoints,"mean up"); Eq<decimal?>(14m,s.AverageDownPoints,"mean down");
                Eq(1,s.UpSamples,"actual samples"); Eq(1,s.DownSamples,"actual samples");
            });
            Test("剛好達反轉門檻的新段不會卡住，漲跌對稱", () => {
                foreach (int sign in new[] { 1, -1 })
                {
                    var c = new PowerIndicator(new PowerParameters(ReversalPoints: 30));
                    decimal Price(decimal offset) => 70000m + sign * offset;
                    Trade(c, 0, Price(0)); Trade(c, 1, Price(65)); Trade(c, 2, Price(35));
                    Eq(1, c.Snapshot().History.Count, "65 point leg completed");
                    Trade(c, 3, Price(65));
                    var s = c.Snapshot();
                    Eq(2, s.History.Count, "30 point reversal leg must complete");
                    Eq(30m, s.History.Last().Points, "threshold equality included");
                    Eq(T.AddSeconds(2), s.CurrentMovement!.StartTime, "new leg starts at pivot time");
                    Eq(sign == 1 ? "上漲" : "下跌", s.CurrentMovement.Direction, "switch back");
                    Trade(c, 4, Price(300));
                    Eq(265m, c.Snapshot().CurrentMovement!.Points, "continued market updates extreme");
                    Eq(1, c.Snapshot().UpSamples, "one up sample");
                    Eq(1, c.Snapshot().DownSamples, "one down sample");
                }
            });
            Test("方向以第一筆起點初始化，達門檻即確立", () => {
                var c = Core(); Trade(c,0,100); Trade(c,1,104); Trade(c,2,99);
                Eq(0,c.Snapshot().History.Count,"short ignored"); Eq("未定",c.Snapshot().CurrentMovement!.Direction,"no anchor threshold crossed");
                Trade(c,3,98); Eq("未定",c.Snapshot().CurrentMovement!.Direction,"range alone does not determine direction");
                Trade(c,4,105); Eq("上漲",c.Snapshot().CurrentMovement!.Direction,"X+Y inclusive");
                Eq(100m,c.Snapshot().CurrentMovement!.StartPrice,"fixed first trade anchor");
                Eq(T,c.Snapshot().CurrentMovement!.StartTime,"first trade time"); Eq(5m,c.Snapshot().CurrentMovement!.Points,"threshold distance");
            });
            Test("30 點初始化先觸上界或下界，忽略無效起點", () => {
                var up=new PowerIndicator(new()); Trade(up,0,100); Trade(up,1,120); Trade(up,2,80);
                Eq("未定",up.Snapshot().CurrentMovement!.Direction,"within X±30 despite 40 point range");
                Trade(up,3,129); Eq("未定",up.Snapshot().CurrentMovement!.Direction,"29 points");
                Trade(up,4,130); Eq("上漲",up.Snapshot().CurrentMovement!.Direction,"upper reached first");
                Eq(100m,up.Snapshot().CurrentMovement!.StartPrice,"up anchor");
                var down=new PowerIndicator(new()); Trade(down,0,100); Trade(down,1,120); Trade(down,2,71); Trade(down,3,70);
                Eq("下跌",down.Snapshot().CurrentMovement!.Direction,"lower reached first"); Eq(100m,down.Snapshot().CurrentMovement!.StartPrice,"down anchor");
                var valid=new PowerIndicator(new()); Trade(valid,0,100,10); Trade(valid,1,110); Trade(valid,2,140);
                Eq(110m,valid.Snapshot().CurrentMovement!.StartPrice,"first valid trade"); Eq(T.AddSeconds(1),valid.Snapshot().CurrentMovement!.StartTime,"valid anchor time");
            });
            Test("分方向僅保留最近 N 完成段", () => {
                var c = Core(n:2); foreach(var x in new[]{(0,100m),(1,112m),(2,107m),(3,98m),(4,103m),(5,120m),(6,115m),(7,100m),(8,105m),(9,140m),(10,135m)}) Trade(c,x.Item1,x.Item2);
                Eq(2,c.Snapshot().UpSamples,"N"); Eq<decimal?>(31m,c.Snapshot().AverageUpPoints,"(22+40)/2");
                Eq<decimal?>(17m,c.Snapshot().AverageDownPoints,"(14+20)/2");
            });
            Test("晚到成交不改寫狀態", () => {
                var c = Core(); Trade(c,5,100); c.Advance(T.AddSeconds(7)); Trade(c,6,200);
                Eq<decimal?>(100m,c.Snapshot().BuyPower,"late ignored"); Eq(11L,c.Snapshot().UpQuantity,"late volume ignored"); Eq(100m,c.Snapshot().CurrentMovement!.StartPrice,"late pivot");
            });
            Test("參數非法範圍阻擋", () => {
                bool threw=false; try { _=new PowerIndicator(new(0,5,10,5,2)); } catch(ArgumentException){threw=true;}
                Eq(true,threw,"validation");
            });
            Test("策略三商品隔離、重複過濾、無委託", () => {
                string root = Path.Combine(Path.GetTempPath(), "power-" + Guid.NewGuid());
                var definition = Definition("three",root);
                var strategy = new BuySellPowerStrategy(definition);
                var clock = new TestClock(T);
                using var runner = new StrategyInstanceRunner(strategy, Universe(), new PaperPortfolioLedger(), clock, 0);
                int orders=0; strategy.OrderRequested += _ => orders++;
                runner.OnMarketEvent(Event("225",0,100,11,1)); runner.OnMarketEvent(Event("225M",1,200,21,2));
                runner.OnMarketEvent(Event("225MC",2,300,3,3)); runner.OnMarketEvent(Event("225",0,500,11,1));
                var s=runner.GetSnapshot(); Eq<decimal?>(11,s.Features["225.UpQuantity"],"large volume");
                Eq<decimal?>(21,s.Features["225M.UpQuantity"],"mini volume"); Eq<decimal?>(3,s.Features["225MC.UpQuantity"],"micro volume");
                foreach(string product in BuySellPowerStrategy.Products) Eq<decimal?>(100,s.Features[product+".BuyPower"],"percentage " + product);
                runner.Pulse(T.AddSeconds(10)); Eq<decimal?>(null,runner.GetSnapshot().Features["225.BuyPower"],"large expire");
                Eq<decimal?>(100,runner.GetSnapshot().Features["225M.BuyPower"],"mini independent expiry"); Eq(0,orders,"orders");
                runner.Stop("驗收"); Eq(StrategyLifecycleState.Stopped,runner.GetSnapshot().State,"stop");
            });
            Test("跨時段未完成段保存、午夜不重置", () => {
                string root=Path.Combine(Path.GetTempPath(),"power-session-"+Guid.NewGuid());
                var strategy=new BuySellPowerStrategy(Definition("session",root));
                var clock=new TestClock(T);
                strategy.Start(new(Universe(),clock,new PaperPortfolioLedger()));
                strategy.OnMarketEvent(Event("225",0,100,11,1)); strategy.OnMarketEvent(Event("225",1,112,11,2));
                strategy.OnMarketEvent(Event("225",2,107,11,3));
                strategy.OnHeartbeat(new DateTimeOffset(2026,10,6,14,45,0,TimeSpan.FromHours(8)));
                Eq<decimal?>(0,strategy.GetSnapshot().Features["225.UpSamples"],"reset averages");
                Eq("尚無資料",strategy.GetSnapshot().Diagnostics["225.Status"],"reset data");
                var night=Event("225",0,200,11,4) with {Timestamp=new(2026,10,6,23,59,59,TimeSpan.FromHours(8))};
                strategy.OnMarketEvent(night); strategy.OnHeartbeat(night.Timestamp.AddSeconds(2));
                Eq<decimal?>(100,strategy.GetSnapshot().Features["225.BuyPower"],"same night midnight");
                strategy.Stop("驗收");
                var files=Directory.GetFiles(root,"行情段紀錄.csv",SearchOption.AllDirectories);
                Eq(2,files.Length,"day/night split"); var text=string.Join("\n",files.Select(File.ReadAllText));
                Eq(true,text.Contains("反向確認"),"completed saved"); Eq(true,text.Contains("跨交易時段"),"unfinished saved");
                Eq(true,text.Contains("false"),"incomplete marker");
            });
            Test("歷史保存異常可見", () => {
                string root=Path.GetTempFileName();
                var strategy=new BuySellPowerStrategy(Definition("error",root)); strategy.Start(new(Universe(),new TestClock(T),new PaperPortfolioLedger()));
                strategy.OnMarketEvent(Event("225",0,100,11,1)); strategy.OnMarketEvent(Event("225",1,112,11,2));
                strategy.OnMarketEvent(Event("225",2,107,11,3)); strategy.Stop("驗收");
                Eq(true,strategy.GetSnapshot().Diagnostics["SaveError"].Length>0,"visible error");
            });
            Test("暫停恢復重新暖機且忽略舊序號", () => {
                var strategy=new BuySellPowerStrategy(Definition("pause",Path.Combine(Path.GetTempPath(),"power-"+Guid.NewGuid())));
                strategy.Start(new(Universe(),new TestClock(T),new PaperPortfolioLedger()));
                strategy.OnMarketEvent(Event("225",0,100,11,1)); strategy.Pause("暫停"); strategy.Resume();
                Eq<decimal?>(null,strategy.GetSnapshot().Features["225.BuyPower"],"re-warm");
                strategy.OnMarketEvent(Event("225",1,100,11,1)); Eq<decimal?>(null,strategy.GetSnapshot().Features["225.BuyPower"],"old sequence");
                strategy.OnMarketEvent(Event("225",2,100,21,2)); Eq<decimal?>(100,strategy.GetSnapshot().Features["225.BuyPower"],"fresh");
                strategy.Stop("驗收");
            });
            Test("連線世代更新允許序號重啟", () => {
                var strategy=new BuySellPowerStrategy(Definition("generation",Path.Combine(Path.GetTempPath(),"power-"+Guid.NewGuid())));
                strategy.Start(new(Universe(),new TestClock(T),new PaperPortfolioLedger()));
                strategy.OnMarketEvent(Event("225",0,100,11,100) with {ConnectionGeneration=1});
                strategy.OnMarketEvent(Event("225",1,200,21,1) with {ConnectionGeneration=2});
                Eq<decimal?>(100,strategy.GetSnapshot().Features["225.BuyPower"],"new generation re-warms");
                Eq<decimal?>(21,strategy.GetSnapshot().Features["225.UpQuantity"],"new generation volume");
                strategy.OnMarketEvent(Event("225",2,300,31,101) with {ConnectionGeneration=1});
                Eq<decimal?>(21,strategy.GetSnapshot().Features["225.UpQuantity"],"old generation ignored");
                strategy.Stop("驗收");
            });
            Test("封存重播與 CSV 範例", () => {
                string root=Path.GetFullPath("docs/superpowers/evidence/buy-sell-power"); Directory.CreateDirectory(root);
                string fixture=Path.Combine(root,"synthetic-market-events.jsonl");
                var sequence=new[]{Event("225",0,100,11,1) with{SourceTradeTime="08:00:00.000"},Event("225M",0,200,21,2),Event("225MC",0,300,3,3),
                    Event("225",1,112,11,4),Event("225",2,107,11,5),Event("225",3,98,11,6),Event("225",4,103,11,7),
                    Event("225",35,103,0,8) with{Kind=MarketEventKind.Book}};
                var json=new JsonSerializerOptions(JsonSerializerDefaults.Web);
                File.WriteAllLines(fixture,sequence.Select(e=>JsonSerializer.Serialize(e,json)));
                var d=Definition("synthetic-replay",Path.Combine(root,"sample-history",Guid.NewGuid().ToString("N")));
                var strategy=new BuySellPowerStrategy(d); using var source=new ArchiveReplayMarketDataSource(fixture);
                using var host=new StrategyHost(source,new EventClock());
                using var recorder=new SimulationRunRecorder(Path.Combine(root,"shared-run"));
                host.SnapshotChanged+=recorder.Record; int executions=0; host.ExecutionOccurred+=_=>executions++;
                var runner=host.Add(strategy,Universe(),0);
                DateTimeOffset? last=null;
                source.BeforeMarketEvent+=at=>{if(last.HasValue)foreach(var p in strategy.GetPulseTimes(last.Value,at))host.Pulse(p);last=at;};
                source.ReplayAsync().GetAwaiter().GetResult();
                var s=runner.GetSnapshot(); Eq<decimal?>(12,s.Features["225.AverageUpPoints"],"replay up");
                Eq<decimal?>(14,s.Features["225.AverageDownPoints"],"replay down"); Eq<decimal?>(null,s.Features["225.BuyPower"],"replay expire");
                Eq(true,s.Diagnostics["225.TimeSource"].Contains("來源成交時刻")||s.Diagnostics["225.TimeSource"].Contains("無來源成交時間"),"explicit source");
                Eq(0,executions,"no trades"); host.StopAll("範例重播完成");
                var csv=Directory.GetFiles(d.Parameters["HistoryRoot"],"行情段紀錄.csv",SearchOption.AllDirectories);
                Eq(3,csv.Length,"three product exports");
                File.Copy(csv.Single(file=>file.Contains("large")),Path.Combine(root,"行情段紀錄.csv"),overwrite:true);
            });
            Test("已完成行情很多時快照仍快速且歷史更新與重置正確", () => {
                var strategy=new BuySellPowerStrategy(Definition("history-snapshot",Path.Combine(Path.GetTempPath(),"power-cache-"+Guid.NewGuid())));
                strategy.Start(new(Universe(),new TestClock(T),new PaperPortfolioLedger()));
                long seq=0;
                void Send(int sec){foreach(string product in BuySellPowerStrategy.Products)strategy.OnMarketEvent(Event(product,sec,sec%2==0?100:112,11,++seq));}
                for(int i=0;i<=505;i++)Send(i);
                string original=strategy.GetSnapshot().Diagnostics["225.History"];
                Eq(500,JsonDocument.Parse(original).RootElement.GetArrayLength(),"bounded completed history");
                var watch=System.Diagnostics.Stopwatch.StartNew();
                for(int i=0;i<1000;i++)Eq(original,strategy.GetSnapshot().Diagnostics["225.History"],"unchanged history exact JSON");
                watch.Stop();Console.WriteLine("1000 populated history snapshots ms="+watch.ElapsedMilliseconds);
                Eq(true,watch.ElapsedMilliseconds<1000,"high-frequency UI populated snapshot budget");
                Send(506);var updated=strategy.GetSnapshot().Diagnostics["225.History"];
                Eq(false,updated==original,"new segment visible even when bounded count stays500");
                Eq(500,JsonDocument.Parse(updated).RootElement.GetArrayLength(),"bounded count retained");
                strategy.Pause("reset");strategy.Resume();Eq("[]",strategy.GetSnapshot().Diagnostics["225.History"],"no old history after reset");
                strategy.Stop("test complete");
            });
            Test("高頻長視窗快照不重掃成交歷史", () => {
                var c=Core(1,60,180);
                for(int i=0;i<100000;i++)c.OnTrade(T.AddMilliseconds(i*2),100,1+i%97,99,100);
                var watch=System.Diagnostics.Stopwatch.StartNew();
                for(int i=0;i<1000;i++)_ = c.Snapshot();
                watch.Stop(); Console.WriteLine("1000 snapshots ms="+watch.ElapsedMilliseconds);
                Eq(true,watch.Elapsed<TimeSpan.FromSeconds(1),"bounded snapshot cost");
            });
            Test("UI 三商品參數保存 roundtrip", () => {
                string file=Path.Combine(Path.GetTempPath(),"power-settings-"+Guid.NewGuid()+".json");
                using var panel=new BuySellPowerPanel();
                var defaults = panel.CreateDefinition("default-ui");
                foreach(string product in BuySellPowerStrategy.Products)
                    Eq("30", defaults.Parameters[product+".ReversalPoints"], "UI default " + product);
                var parameters=new Dictionary<string,string>();
                foreach(var p in new PowerParameters(10,5,10,5,2).ToDictionary("225.")) parameters[p.Key]=p.Value;
                foreach(var p in new PowerParameters(20,6,12,7,3).ToDictionary("225M.")) parameters[p.Key]=p.Value;
                foreach(var p in new PowerParameters(30,7,14,9,4).ToDictionary("225MC.")) parameters[p.Key]=p.Value;
                panel.SetParameters(parameters); panel.SaveSettings(file);
                using var restored=new BuySellPowerPanel(); restored.LoadSettings(file);
                var d=restored.CreateDefinition("test-ui");
                Eq("10",d.Parameters["225.PowerWindowSeconds"],"large retained");
                Eq("7",d.Parameters["225M.ReversalPoints"],"mini retained");
                Eq("4",d.Parameters["225MC.MovementAveragePeriods"],"micro retained");
                Eq(2,d.Universe.References.Count,"references");
            });
            Test("共用執行資訊清單保存所有生效參數", () => {
                var d=new StrategyDefinition{StrategyType=BuySellPowerFactory.TypeName,InstanceId="defaults"};
                var snapshot=new BuySellPowerStrategy(d).GetSnapshot();
                var method=typeof(StrategySimulationService).GetMethod("ResolveParameters",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!;
                object resolution=method.Invoke(null,new object[]{snapshot,d})!;
                var effective=(IReadOnlyDictionary<string,string>)resolution.GetType().GetProperty("Effective")!.GetValue(resolution)!;
                Eq(15,effective.Count,"resolved defaults"); Eq("30",effective["225.PowerWindowSeconds"],"default window");
            });
            Test("策略中心新增指標頁籤", () => {
                using var sim=new StrategyCenterForm(); using var live=new LiveStrategyCenterForm();
                TabControl Tabs(Form f) => f.Controls.OfType<TabControl>().Single();
                Eq(true,Tabs(sim).TabPages.Cast<TabPage>().Any(p=>p.Text=="買賣力道"),"simulation tab");
                Eq(true,Tabs(live).TabPages.Cast<TabPage>().Any(p=>p.Text=="買賣力道"),"live observer tab");
            });
            Console.WriteLine($"BuySellPower PASS {count}"); return 0;
        }
        catch(Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    static StrategyDefinition Definition(string id, string root) {
        var p=new Dictionary<string,string>{{"HistoryRoot",root}};
        foreach(string product in new[]{"225","225M","225MC"}) foreach(var pair in new PowerParameters(10,5,10,5,2).ToDictionary(product+".")) p.Add(pair.Key,pair.Value);
        return new(){StrategyType=BuySellPowerFactory.TypeName,InstanceId=id,Parameters=p};
    }
    static ResolvedStrategyUniverse Universe() => new(){Tradable=new(){ProductId="225MC",Symbol="micro"},References=new[]{new ResolvedProduct{ProductId="225",Symbol="large"},new ResolvedProduct{ProductId="225M",Symbol="mini"}}};
    static MarketEvent Event(string product,int sec,decimal price,int qty,long seq) => new(){ProductId=product,Symbol=product=="225"?"large":product=="225M"?"mini":"micro",Timestamp=T.AddSeconds(sec),Sequence=seq,Kind=MarketEventKind.Trade,LastPrice=price,TradeQuantity=qty,BidPrices=new[]{price-1},AskPrices=new[]{price}};
    sealed class TestClock(DateTimeOffset at) : IStrategyClock {public DateTimeOffset Now => at;}
    sealed class EventClock : IEventDrivenStrategyClock {public DateTimeOffset Now {get;private set;} public void Advance(DateTimeOffset at)=>Now=at;}
}
