using KGIOptionPricing;
using KGIOptionPricing.Strategies.NikkeiRlDirection;
using KGIOptionPricing.StrategyFramework;
using System.Text.Json;
public static class Grid15IntegrationTests {
 public static void Render(string output){
  Exception? failure=null;
  var thread=new System.Threading.Thread(()=>{try{RenderSta(output);}catch(Exception ex){failure=ex;}});
  thread.IsBackground=true;thread.SetApartmentState(System.Threading.ApartmentState.STA);thread.Start();
  if(!thread.Join(10000))throw new TimeoutException("UI render timeout");if(failure!=null)throw failure;
 }
 static void RenderSta(string output){
  var fontType=typeof(NikkeiRlPanel).Assembly.GetType("KGIOptionPricing.Infrastructure.WindowsFontBootstrap")!;
  using var fonts=(IDisposable)fontType.GetMethod("Load")!.Invoke(null,new object?[]{null})!;
  System.Windows.Forms.Application.EnableVisualStyles();
  using var form=new System.Windows.Forms.Form{ClientSize=new System.Drawing.Size(1450,720),Opacity=0,ShowInTaskbar=false};
  using var panel=new NikkeiRlPanel();panel.ConfigureLive(true);panel.ModelSelector.SelectedIndex=2;form.Controls.Add(panel);
  form.Shown+=(_,_)=>form.BeginInvoke(new Action(()=>{
   using var bitmap=new System.Drawing.Bitmap(form.ClientSize.Width,form.ClientSize.Height);
   panel.DrawToBitmap(bitmap,new System.Drawing.Rectangle(System.Drawing.Point.Empty,form.ClientSize));bitmap.Save(output);form.Close();
  }));
  System.Windows.Forms.Application.Run(form);
 }
 public static void Run(){
  int checks=0;void Check(bool ok,string reason){checks++;if(!ok)throw new Exception(reason);}
  void Reject(Action f,string reason){bool rejected=false;try{f();}catch(ArgumentException){rejected=true;}catch(InvalidDataException){rejected=true;}Check(rejected,reason);}
  Check(typeof(KGIOptionPricing.StrategyFramework.Storage.MarketDataArchiveWriter).GetConstructors().Any(c=>c.GetParameters().Any(p=>p.Name=="enabled")),"performance-only archive mode is missing");
  var archiveRoot=Path.Combine(Path.GetTempPath(),"grid-no-archive-"+Guid.NewGuid().ToString("N"));
  using(var archive=new KGIOptionPricing.StrategyFramework.Storage.MarketDataArchiveWriter(archiveRoot,"test",enabled:false)){
   archive.Record(new MarketEvent());Check(archive.EventFilePath=="","disabled archive exposed fake file");
   bool unavailable=false;try{archive.CaptureBarrier();}catch(InvalidOperationException){unavailable=true;}Check(unavailable,"disabled archive claimed continuity evidence");
  }
  Check(!Directory.Exists(archiveRoot),"performance-only mode wrote raw market files");
  var serviceRoot=Path.Combine(Path.GetTempPath(),"grid-simulation-service-"+Guid.NewGuid().ToString("N"));
  var quote=new KGIQuoteService();using(var service=new StrategySimulationService(quote,serviceRoot)){Check(!Directory.Exists(serviceRoot),"simulation service created a raw archive");}
  Check(!Directory.Exists(serviceRoot),"simulation shutdown created a raw archive");
  var open=new DateTimeOffset(2026,10,6,7,45,0,TimeSpan.FromHours(8));
  Check(!FrozenGrid15Policy.EntryGridAllows(open,open),"open is not frame zero");
  for(int i=0;i<30;i++)Check(FrozenGrid15Policy.EntryGridAllows(open.AddSeconds(3+3*i),open)==(i%5==0),"phase differs from frozen replay");
  var raw=Enumerable.Range(0,60).Select(i=>(double)i).ToArray();
  var projected=FrozenGrid15Policy.Market44(raw);
  Check(projected.SequenceEqual(raw.Take(30).Concat(raw.Skip(40).Take(8)).Concat(raw.Skip(54).Take(6))),"flow/position inputs leaked into market44");
  Reject(()=>FrozenGrid15Policy.Market44(new double[59]),"bad input schema accepted");
  Reject(()=>NikkeiRlParameters.Parse(new Dictionary<string,string>{{"PolicyVariant","unknown"}}),"unknown variant accepted");
  foreach(var variant in new[]{"grid15-positive-net","grid15-q90-research"}){
   var p=NikkeiRlParameters.Parse(new Dictionary<string,string>{{"PolicyVariant",variant}});
   Check(!p.EnableNightSession&&!p.LiveTradingEnabled&&p.ActionLatencyMs==1,"default simulation/day boundary");
   Check(NikkeiRlParameters.Parse(p.ToDictionary()).EffectiveModelId==variant,"parameters roundtrip");
   var live=p with{LiveTradingEnabled=true};Check(NikkeiRlParameters.Parse(live.ToDictionary()).ActionLatencyMs==0,"live artificial delay");
   Check(LiveStrategyService.TryValidateResearchStrategy(new(){StrategyType=NikkeiRlFactory.TypeName,Parameters=live.ToDictionary()},out _),"live catalog opt-in");
   Check(!LiveStrategyService.TryValidateResearchStrategy(new(){StrategyType=NikkeiRlFactory.TypeName,Parameters=p.ToDictionary()},out _),"simulation leaked into live");
   Reject(()=>NikkeiRlParameters.Parse(new Dictionary<string,string>{{"PolicyVariant",variant},{"EnableNightSession","true"}}),"unvalidated night accepted");
   var tampered=p.ToDictionary();tampered["SelectionThresholdJPY"]="999";Reject(()=>NikkeiRlParameters.Parse(tampered),"frozen threshold changed");
   using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(p.ResolvedModelDirectory,"model.json")));var model=doc.RootElement;
   using var policy=new FrozenGrid15Policy(p);double threshold=model.GetProperty("selectionThresholdJPY").GetDouble();
   Check(policy.ThresholdJPY==threshold,"wrong variant threshold");
   var random=new Random(67);bool[] mask=[true,true,true,true,true];
   for(int i=0;i<120;i++){
    var at=open.AddSeconds(3+3*i);var obs=Enumerable.Range(0,60).Select(_=>random.NextDouble()*10-5).ToArray();
    obs[30]=i<40?0:i<80?1:-1;obs[31]=i/20.0;obs[32]=i-60;obs[36]=i%13==0?1:0;
    bool valid=i!=57;policy.SetContext(at,open,valid);int got=policy.Predict(obs,mask).Action;
    bool historyValid=i>=10&&!(i>=57&&i<=67);int pos=Math.Sign(obs[30]);bool pending=obs[36]>0;
    int expected;
    if(!historyValid)expected=pos!=0?3:pending?4:0;
    else{
     var x=FrozenGrid15Policy.Market44(obs);var l=Score(model.GetProperty("longNet"),x);var sh=Score(model.GetProperty("shortNet"),x);
     Check(Math.Abs(policy.LongNetJPY-l)<1e-9&&Math.Abs(policy.ShortNetJPY-sh)<1e-9,"model tree score mismatch");
     if(pos!=0)expected=Score(model.GetProperty("holding"),x.Concat(new[]{(double)pos,obs[32],obs[31]}).ToArray())>0?0:3;
     else {bool eligible=Math.Max(l,sh)>threshold;expected=pending?(eligible?0:4):eligible?(l>=sh?1:2):0;if(!pending&&expected is 1 or 2&&i%5!=0)expected=0;}
    }
    Check(got==expected,"frozen policy action mismatch");
   }
   policy.SetContext(open.AddSeconds(999),open,true);Check(policy.Predict(new double[60],mask).Action==0,"gap must reset 30-second health history");
   var temp=Path.Combine(Path.GetTempPath(),"grid-sha-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
   try{File.WriteAllText(Path.Combine(temp,"model.json"),"{}");Reject(()=>new FrozenGrid15Policy(p with{ModelDirectory=temp}),"tampered artifact loaded");}finally{Directory.Delete(temp,true);}
  }
  using(var panel=new NikkeiRlPanel()){
   Check(panel.ModelSelector.Items.Count==3,"missing model choices");var existing=panel.CreateDefinition();
   panel.ConfigureLive(true);panel.Confirm.Checked=true;panel.ModelSelector.SelectedIndex=1;
   Check(!panel.Confirm.Checked,"model change must clear broker confirmation");
   Check(panel.CreateDefinition().Parameters["PolicyVariant"]=="grid15-positive-net","first model UI binding");
   panel.ModelSelector.SelectedIndex=2;var definition=panel.CreateDefinition();
   Check(definition.Parameters["PolicyVariant"]=="grid15-q90-research"&&definition.Parameters["LiveTradingEnabled"]=="true"&&definition.Parameters["EnableNightSession"]=="false","second model live UI binding");
   Check(existing.Parameters["PolicyVariant"]=="earlier","selector mutated existing instance definition");
   panel.ConfigureLive(false);Check(panel.CreateDefinition().Parameters["DecisionLatencyMs"]=="1","simulation delay lost");
  }
  Console.WriteLine($"PASS Grid15 {checks} checks: exact trees/actions, raw44, age47, grid phase, history/gap, artifact tampering, live opt-in and UI");
 }
 static double Score(JsonElement model,double[] x){double total=0;foreach(var tree in model.GetProperty("trees").EnumerateArray()){int index=0;while(true){var n=tree[index];if(n.TryGetProperty("value",out var value)){total+=value.GetDouble();break;}index=x[n.GetProperty("feature").GetInt32()]<=n.GetProperty("threshold").GetDouble()?n.GetProperty("left").GetInt32():n.GetProperty("right").GetInt32();}}return total;}
}
