using System.Globalization;
using KGIOptionPricing.Strategies.NikkeiSequenceTrend;

try
{
    if(args.Length==0)
    {
        var t=DateTimeOffset.Parse("2026-10-06T08:00:00+08:00");
        var ticks=new[]{new Tick(t.AddMilliseconds(2),10000,1),new Tick(t.AddSeconds(9),9999,2),new Tick(t.AddSeconds(180),10050,3),new Tick(t.AddSeconds(189),10070,4),new Tick(t.AddSeconds(189).AddMilliseconds(2),10080,5)};
        var (a,b)=Probe(t,10000,10005,ticks,0);
        if(a.completedTrades!=1||a.netPnl!=728||b.completedTrades!=0||b.openQuantity!=0)throw new Exception("Holding clock must start at actual entry; expired short must not fill");
        var timed=new SequenceResearchExecution("225MC-Z26",0,stopPoints:50,maximumHoldSeconds:180);
        timed.Decide(t,10000,10005,true,new[]{0.0,0.0,1.0},1000,1,1,.5,true);
        foreach(var tick in ticks)timed.Trade(tick.At,tick.Price,tick.Sequence);
        if(timed.Result().completedTrades!=1||timed.Result().netPnl!=728)throw new Exception("Known holding deadline must execute without a new 5s forecast decision");
        Console.WriteLine("PASS entry-based 180s labels and execution timer:728JPY; expired short unfilled");return;
    }
    if(args.Length!=3)throw new ArgumentException("trades.csv targets.csv labels.csv");
    var culture=CultureInfo.InvariantCulture;var readRate=new Rate(20*1024*1024);var writeRate=new Rate(5*1024*1024);
    DateTimeOffset Stamp(string s)=>DateTimeOffset.UnixEpoch.AddTicks((long)(decimal.Parse(s,culture)*TimeSpan.TicksPerSecond));
    IEnumerable<string[]> Rows(string path)
    {
        if(new FileInfo(path).Length>256L*1024*1024)throw new Exception("Input quota");using var reader=new StreamReader(path);reader.ReadLine();string? line;
        while((line=reader.ReadLine())!=null){readRate.Consume(line.Length+2);yield return line.Split(',');}
    }
    var tape=Rows(args[0]).Select(r=>new Tick(Stamp(r[0]),decimal.Parse(r[1],culture),(long)decimal.Parse(r[2],culture))).ToArray();
    for(int i=1;i<tape.Length;i++)if(tape[i].At<tape[i-1].At)throw new Exception("Unordered tape");
    int Lower(DateTimeOffset time){int lo=0,hi=tape.Length;while(lo<hi){int m=(lo+hi)/2;if(tape[m].At<time)lo=m+1;else hi=m;}return lo;}
    using var output=new StreamWriter(args[2],false,new System.Text.UTF8Encoding(false));output.WriteLine("index,longNet,longStatus,shortNet,shortStatus");long bytes=0;int count=0;
    foreach(var row in Rows(args[1]))
    {
        var at=Stamp(row[1]);var (longResult,shortResult)=Probe(at,decimal.Parse(row[2],culture),decimal.Parse(row[3],culture),tape,Lower(at.AddMilliseconds(1)));
        (string Net,string Status) Value(SequenceResearchExecution.ResultRow r)=>r.completedTrades==1&&r.openQuantity==0?(r.netPnl.ToString(culture),"closed"):r.openQuantity!=0?("","censored"):("","unfilled");
        var a=Value(longResult);var b=Value(shortResult);var text=$"{row[0]},{a.Net},{a.Status},{b.Net},{b.Status}\n";writeRate.Consume(text.Length);bytes+=text.Length;if(bytes>256L*1024*1024)throw new Exception("Output quota");output.Write(text);count++;
    }
    output.Flush();Console.WriteLine($"LABELS 180s {count}");
}
catch(Exception error){Console.Error.WriteLine(error.Message);Environment.ExitCode=1;}

static (SequenceResearchExecution.ResultRow Long,SequenceResearchExecution.ResultRow Short) Probe(DateTimeOffset at,decimal bid,decimal ask,Tick[] tape,int start)
{
    var engines=new[]{new SequenceResearchExecution("225MC-Z26",0,stopPoints:50),new SequenceResearchExecution("225MC-Z26",0,stopPoints:50)};
    engines[0].Decide(at,bid,ask,true,new[]{0.0,0.0,1.0},1000,1,1,.5,true);engines[1].Decide(at,bid,ask,true,new[]{1.0,0.0,0.0},-1000,-1,-1,.5,true);
    bool expired=false;
    for(int j=start;j<tape.Length&&tape[j].At<=at.AddSeconds(195);j++)
    {
        var tick=tape[j];if(!expired&&tick.At>=at.AddSeconds(10)){foreach(var e in engines)e.CancelUnfilledEntry(at.AddSeconds(10));expired=true;}
        foreach(var e in engines)
        {
            if(e.EntryTimestamp.HasValue&&tick.At>=e.EntryTimestamp.Value.AddSeconds(180))e.ForceExit(e.EntryTimestamp.Value.AddSeconds(180));
            e.Trade(tick.At,tick.Price,tick.Sequence);
        }
        if(expired&&engines.All(e=>e.Position==0))break;
    }
    return (engines[0].Result(),engines[1].Result());
}
readonly record struct Tick(DateTimeOffset At,decimal Price,long Sequence);
sealed class Rate
{
    readonly double speed;const double Capacity=256*1024;double tokens=Capacity;long last=System.Diagnostics.Stopwatch.GetTimestamp();public Rate(double speed){this.speed=speed;}
    public void Consume(int n){var now=System.Diagnostics.Stopwatch.GetTimestamp();tokens=Math.Min(Capacity,tokens+(now-last)/(double)System.Diagnostics.Stopwatch.Frequency*speed);last=now;if(tokens<n){Thread.Sleep(TimeSpan.FromSeconds((n-tokens)/speed));var later=System.Diagnostics.Stopwatch.GetTimestamp();tokens=Math.Max(0,Math.Min(Capacity,tokens+(later-last)/(double)System.Diagnostics.Stopwatch.Frequency*speed)-n);last=later;}else tokens-=n;}
}