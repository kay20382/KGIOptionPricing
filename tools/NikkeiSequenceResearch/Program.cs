using KGIOptionPricing.Strategies.NikkeiSequenceTrend;
using System.Globalization;
using System.Text.Json;

try
{
if(args.Length==1&&args[0]=="self-test")
{
    var t=DateTimeOffset.Parse("2026-10-06T08:00:00+08:00");
    var e=new SequenceResearchExecution("225MC-Z26",0);
    e.Decide(t,10000,10005,true,new[]{0.1,0.1,0.8},10,1,1,.6,true);
    e.Trade(t.AddTicks(5000),9999,1); // .5ms: not eligible
    if(e.Position!=0)throw new Exception("Latency failed");
    e.Trade(t.AddMilliseconds(1),10000,2); // touch
    if(e.Position!=0)throw new Exception("Strict cross failed");
    e.Trade(t.AddMilliseconds(2),9999,3);
    if(e.Position!=1)throw new Exception("Entry failed");
    e.Decide(t.AddSeconds(5),10010,10015,true,new[]{.8,.1,.1},-10,-1,-1,.6,true);
    e.Trade(t.AddSeconds(5).AddMilliseconds(2),10010,4);
    var r=e.Result();
    if(r.completedTrades!=1||r.netPnl!=28||r.executionCosts!=72)throw new Exception("Shared ledger failed");
    if(e.Position!=0)throw new Exception("Close failed");
    var risk=new SequenceResearchExecution("225MC-Z26",0);
    for(int i=0;i<4;i++)
    {
        var at=i<2?t.AddSeconds(i*30):t.Date.AddHours(16).AddSeconds((i-2)*30);
        risk.Decide(at,10000,10005,true,new[]{.1,.1,.8},10,1,1,.6,true);
        risk.Trade(at.AddMilliseconds(2),9999,10+i*2);
        risk.Trade(at.AddMilliseconds(4),9849,11+i*2);
    }
    if(risk.Result().netPnl!=-6288)throw new Exception("Cross-session net loss carry failed");
    risk.Decide(t.Date.AddHours(16).AddMinutes(2),10000,10005,true,new[]{.1,.1,.8},10,1,1,.6,true);
    risk.Trade(t.Date.AddHours(16).AddMinutes(2).AddMilliseconds(2),9999,30);
    if(risk.Position!=0)throw new Exception("Remaining day-risk budget failed");
    var net=new SequenceResearchExecution("225MC-Z26",0,stopPoints:50,netForecast:true,minimumNetTicks:.25,exitThreshold:.35,maximumHoldSeconds:30);
    net.Decide(t,10000,10005,true,new[]{.1,.1,.8},.5,1,1,.6,true);
    net.Trade(t.AddMilliseconds(2),9999,1);
    if(net.Position!=1)throw new Exception("Net edge incorrectly charged base fees twice");
    net.Decide(t.AddSeconds(31),10010,10015,true,new[]{.1,.1,.8},.5,1,1,.6,true);
    net.Trade(t.AddSeconds(31).AddMilliseconds(2),10010,2);
    if(net.Result().netPnl!=28)throw new Exception("Net edge holding clock or shared fee mismatch");
    var stressed=new SequenceResearchExecution("225MC-Z26",1,netForecast:true,minimumNetTicks:.25);
    stressed.Decide(t,10000,10005,true,new[]{.1,.1,.8},.5,1,1,.6,true);
    stressed.Trade(t.AddMilliseconds(2),9999,1);
    if(stressed.Position!=0)throw new Exception("Net edge ignored extra cost stress");
    var marked=new SequenceResearchExecution("225MC-Z26",0);
    marked.Decide(t,10000,10005,true,new[]{.1,.1,.8},10,1,1,.6,true);
    marked.Trade(t.AddMilliseconds(2),9999,1);
    marked.Decide(t.AddSeconds(5),9000,9005,true,new[]{.1,.1,.8},10,1,1,.6,true);
    if(marked.Result().maximumIntradayLoss<10000||!marked.Result().dailyStopped)throw new Exception("Daily floating loss ignored a valid book move");
    var paused=new SequenceResearchExecution("225MC-Z26",0);
    paused.Decide(t,10000,10005,true,new[]{.1,.1,.8},10,1,1,.6,true);
    paused.Trade(t.AddMilliseconds(2),9999,1);
    paused.Decide(t.AddSeconds(5),9000,9005,false,new[]{0.0,1.0,0.0},0,0,0,.6,true);
    if(paused.Result().maximumIntradayLoss<10000)throw new Exception("Unavailable forecast hid book floating loss");
    var between=new SequenceResearchExecution("225MC-Z26",0);
    between.Decide(t,10000,10005,true,new[]{.1,.1,.8},10,1,1,.6,true);
    between.Trade(t.AddMilliseconds(2),9999,1);
    between.Book(t.AddSeconds(2),8990,9020);
    between.Book(t.AddSeconds(3),10000,10005);
    if(between.Result().maximumIntradayLoss<10000||!between.Result().dailyStopped)throw new Exception("Wide unsampled book loss was hidden");
    var entryBookRisk=new SequenceResearchExecution("225MC-Z26",0);
    entryBookRisk.Decide(t,10000,10005,true,new[]{.1,.1,.8},10,1,1,.6,true);
    entryBookRisk.Book(t.AddTicks(5000),9080,9085);
    entryBookRisk.Trade(t.AddMilliseconds(2),9999,1);
    entryBookRisk.Book(t.AddMilliseconds(3),10000,10005);
    if(entryBookRisk.Result().maximumIntradayLoss<9200||!entryBookRisk.Result().dailyStopped)throw new Exception("Entry ignored previously known liquidation book");
    Console.WriteLine("PASS research execution, net-edge and full-book risk controls");return;
}
if(args.Length!=5&&args.Length!=10&&args.Length!=11)throw new ArgumentException("quotes.csv trades.csv costTicks threshold trendFilter");
var culture=CultureInfo.InvariantCulture;
DateTimeOffset Stamp(string value)=>DateTimeOffset.UnixEpoch.AddTicks((long)(decimal.Parse(value,culture)*TimeSpan.TicksPerSecond));
var rate=new InputRate();
IEnumerable<string[]> Rows(string path)
{
    using var reader=new StreamReader(path);reader.ReadLine();string? line;
    while((line=reader.ReadLine())!=null){rate.Consume(line.Length+2);yield return line.Split(',');}
}
var engine=new SequenceResearchExecution("225MC-Z26",int.Parse(args[2]),args.Length>=10?decimal.Parse(args[5],culture):150,args.Length>=10&&bool.Parse(args[6]),args.Length>=10?double.Parse(args[7],culture):1,args.Length>=10?double.Parse(args[8],culture):.5,args.Length>=10?int.Parse(args[9]):0);
string? rawSourceSha=null;
void Decide(string[] row)
{
    double D(int i)=>double.Parse(row[i],culture);
    engine.Decide(Stamp(row[0]),(decimal)D(1),(decimal)D(2),D(3)>0,new[]{D(4),D(5),D(6)},D(7),D(8),D(9),double.Parse(args[3],culture),bool.Parse(args[4]));
}
if(args.Length==11)
{
    using var quotes=Rows(args[0]).GetEnumerator();bool hasQuote=quotes.MoveNext();
    using var hash=System.Security.Cryptography.SHA256.Create();
    using var input=File.OpenRead(args[10]);
    using var hashing=new System.Security.Cryptography.CryptoStream(input,hash,System.Security.Cryptography.CryptoStreamMode.Read);
    using var reader=new StreamReader(hashing);string? line;DateTimeOffset? previous=null;long? sequence=null;
    while((line=reader.ReadLine())!=null)
    {
        rate.Consume(System.Text.Encoding.UTF8.GetByteCount(line)+2);
        if(!line.Contains("225MC",StringComparison.Ordinal))continue;
        using var eventDoc=JsonDocument.Parse(line);var row=eventDoc.RootElement;
        if(!row.TryGetProperty("Product",out var product)||product.GetString()!="225MC")continue;
        string? kind=row.GetProperty("Kind").GetString();if(kind!="trade"&&!(kind?.StartsWith("depth")??false))continue;
        var at=DateTimeOffset.Parse(row.GetProperty("Time").GetString()!,culture);
        long seq=row.GetProperty("Sequence").GetInt64();
        if(previous.HasValue&&(at<previous||sequence.HasValue&&seq<=sequence))throw new ArgumentException("Raw risk replay ordering defect");
        previous=at;sequence=seq;
        while(hasQuote&&Stamp(quotes.Current[0])<=at){Decide(quotes.Current);hasQuote=quotes.MoveNext();}
        if(kind=="trade")
        {
            if(row.GetProperty("TradeQty").GetDecimal()>0)engine.Trade(at,row.GetProperty("Last").GetDecimal(),seq);
        }
        else
        {
            var bids=row.GetProperty("Bid");var asks=row.GetProperty("Ask");
            var bq=row.GetProperty("BidQty");var aq=row.GetProperty("AskQty");
            if(bids.GetArrayLength()<5||asks.GetArrayLength()<5||bq.GetArrayLength()<5||aq.GetArrayLength()<5||bids.EnumerateArray().Take(5).Any(x=>x.GetDecimal()<=0)||asks.EnumerateArray().Take(5).Any(x=>x.GetDecimal()<=0)||bq.EnumerateArray().Take(5).Any(x=>x.GetDecimal()<0)||aq.EnumerateArray().Take(5).Any(x=>x.GetDecimal()<0)||bq[0].GetDecimal()+aq[0].GetDecimal()<=0||bids[0].GetDecimal()>=asks[0].GetDecimal())throw new ArgumentException("Invalid raw risk book; cannot certify daily loss");
            engine.Book(at,bids[0].GetDecimal(),asks[0].GetDecimal());
        }
    }
    rawSourceSha=Convert.ToHexString(hash.Hash!).ToLowerInvariant();
    while(hasQuote){Decide(quotes.Current);hasQuote=quotes.MoveNext();}
}
else
{
    using var trades=Rows(args[1]).GetEnumerator();bool hasTrade=trades.MoveNext();
    foreach(var row in Rows(args[0]))
    {
        var at=Stamp(row[0]);
        while(hasTrade&&Stamp(trades.Current[0])<at)
        {
            var tr=trades.Current;engine.Trade(Stamp(tr[0]),decimal.Parse(tr[1],culture),(long)decimal.Parse(tr[2],culture));hasTrade=trades.MoveNext();
        }
        Decide(row);
    }
    while(hasTrade)
    {
        var tr=trades.Current;engine.Trade(Stamp(tr[0]),decimal.Parse(tr[1],culture),(long)decimal.Parse(tr[2],culture));hasTrade=trades.MoveNext();
    }
}
var result=JsonSerializer.SerializeToNode(engine.Result())!;
if(rawSourceSha is not null)result["rawSourceSha256"]=rawSourceSha;
Console.WriteLine(result.ToJsonString());
}
catch(Exception ex){Console.Error.WriteLine(ex.Message);Environment.ExitCode=1;}

sealed class InputRate
{
    const double Speed=20*1024*1024,Capacity=256*1024;
    double tokens=Capacity;long last=System.Diagnostics.Stopwatch.GetTimestamp();
    public void Consume(int bytes)
    {
        var now=System.Diagnostics.Stopwatch.GetTimestamp();tokens=Math.Min(Capacity,tokens+(now-last)/(double)System.Diagnostics.Stopwatch.Frequency*Speed);last=now;
        if(tokens<bytes){Thread.Sleep(TimeSpan.FromSeconds((bytes-tokens)/Speed));var later=System.Diagnostics.Stopwatch.GetTimestamp();tokens=Math.Max(0,Math.Min(Capacity,tokens+(later-last)/(double)System.Diagnostics.Stopwatch.Frequency*Speed)-bytes);last=later;}else tokens-=bytes;
    }
}
