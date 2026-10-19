using KGIOptionPricing.Strategies.NikkeiSequenceTrend;
try
{
if(args.Length==0){
var t=DateTimeOffset.Parse("2026-10-06T08:00:00+08:00");
var e=new SequenceResearchExecution("225MC-Z26",0);
e.Decide(t,10000,10005,true,new[]{.1,.1,.8},100,1,1,.6,true);
e.Trade(t.AddMilliseconds(2),9999,1);
e.ForceExit(t.AddSeconds(30));
e.Trade(t.AddSeconds(30).AddMilliseconds(2),10010,2);
if(e.Result().netPnl!=28||e.Result().completedTrades!=1)throw new Exception("Label net costs failed");
var unfilled=new SequenceResearchExecution("225MC-Z26",0);
unfilled.Decide(t,10000,10005,true,new[]{.1,.1,.8},100,1,1,.6,true);
unfilled.Trade(t.AddMilliseconds(2),10000,1);
unfilled.CancelUnfilledEntry(t.AddSeconds(10));
unfilled.Trade(t.AddSeconds(11),9999,2);
if(unfilled.Position!=0||unfilled.Result().completedTrades!=0)throw new Exception("Unfilled label became fake trade");
Console.WriteLine("PASS common ledger labels:28JPY net; touch and expired entry remain unfilled");
return;}
if(args.Length!=3)throw new ArgumentException("trades.csv targets.csv labels.csv");
var culture=System.Globalization.CultureInfo.InvariantCulture;
DateTimeOffset Stamp(string value)=>DateTimeOffset.UnixEpoch.AddTicks((long)(decimal.Parse(value,culture)*TimeSpan.TicksPerSecond));
var inputRate=new Rate(20*1024*1024);var outputRate=new Rate(5*1024*1024);
IEnumerable<string[]> Rows(string path){
    if(new FileInfo(path).Length>256L*1024*1024)throw new Exception("Input file quota");
    using var f=new StreamReader(path);f.ReadLine();string? line;
    while((line=f.ReadLine())!=null){inputRate.Consume(line.Length+2);yield return line.Split(',');}
}
var ticks=Rows(args[0]).Select(r=>new Tick(Stamp(r[0]),decimal.Parse(r[1],culture),(long)decimal.Parse(r[2],culture))).ToArray();
for(int j=1;j<ticks.Length;j++)if(ticks[j].Time<ticks[j-1].Time)throw new Exception("Unordered label trade stream");
int Lower(DateTimeOffset time){int lo=0,hi=ticks.Length;while(lo<hi){int m=(lo+hi)/2;if(ticks[m].Time<time)lo=m+1;else hi=m;}return lo;}
using var output=new StreamWriter(args[2],false,new System.Text.UTF8Encoding(false));
output.WriteLine("index,longNet,longStatus,shortNet,shortStatus");
long written=0;int count=0;
foreach(var row in Rows(args[1])){
    var at=Stamp(row[1]);var bid=decimal.Parse(row[2],culture);var ask=decimal.Parse(row[3],culture);
    var engines=new[]{new SequenceResearchExecution("225MC-Z26",0),new SequenceResearchExecution("225MC-Z26",0)};
    engines[0].Decide(at,bid,ask,true,new[]{0.0,0.0,1.0},1000,1,1,.5,true);
    engines[1].Decide(at,bid,ask,true,new[]{1.0,0.0,0.0},-1000,-1,-1,.5,true);
    bool expired=false,closed=false;
    for(int j=Lower(at.AddMilliseconds(1));j<ticks.Length&&ticks[j].Time<=at.AddSeconds(35);j++){
        var tr=ticks[j];
        if(!expired&&tr.Time>=at.AddSeconds(10)){foreach(var e in engines)e.CancelUnfilledEntry(at.AddSeconds(10));expired=true;}
        if(!closed&&tr.Time>=at.AddSeconds(30)){foreach(var e in engines)e.ForceExit(at.AddSeconds(30));closed=true;}
        foreach(var e in engines)e.Trade(tr.Time,tr.Price,tr.Sequence);
        if(expired&&engines.All(e=>e.Position==0))break;
    }
    (string Net,string State) Result(SequenceResearchExecution engine){
        var r=engine.Result();
        return r.completedTrades==1&&r.openQuantity==0?(r.netPnl.ToString(culture),"closed"):r.openQuantity!=0?("","censored"):("","unfilled");
    }
    var a=Result(engines[0]);var b=Result(engines[1]);var line=$"{row[0]},{a.Net},{a.State},{b.Net},{b.State}\n";
    outputRate.Consume(line.Length);output.Write(line);written+=line.Length;count++;
    if(written>256L*1024*1024)throw new Exception("Label output quota");
}
output.Flush();Console.WriteLine($"LABELS {count}");
}
catch(Exception ex){Console.Error.WriteLine(ex.Message);Environment.ExitCode=1;}
record struct Tick(DateTimeOffset Time,decimal Price,long Sequence);
sealed class Rate{
    readonly double speed;const double Capacity=256*1024;double tokens=Capacity;long last=System.Diagnostics.Stopwatch.GetTimestamp();
    public Rate(double speed){this.speed=speed;}
    public void Consume(int bytes){var now=System.Diagnostics.Stopwatch.GetTimestamp();tokens=Math.Min(Capacity,tokens+(now-last)/(double)System.Diagnostics.Stopwatch.Frequency*speed);last=now;if(tokens<bytes){Thread.Sleep(TimeSpan.FromSeconds((bytes-tokens)/speed));var later=System.Diagnostics.Stopwatch.GetTimestamp();tokens=Math.Max(0,Math.Min(Capacity,tokens+(later-last)/(double)System.Diagnostics.Stopwatch.Frequency*speed)-bytes);last=later;}else tokens-=bytes;}
}