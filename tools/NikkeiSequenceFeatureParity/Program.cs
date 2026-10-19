using System.Globalization;
using System.Text.Json;

try
{
    if(args.Length!=3)throw new ArgumentException("raw.jsonl requested-indices.json output.json");
    var pipeline=new Pipeline();var rate=new InputRate();
    string sourceSha;
    using var hash=System.Security.Cryptography.SHA256.Create();
    using var input=File.OpenRead(args[0]);
    using var hashing=new System.Security.Cryptography.CryptoStream(input,hash,System.Security.Cryptography.CryptoStreamMode.Read);
    using(var reader=new StreamReader(hashing))
    {
        string? line;while((line=reader.ReadLine())!=null)
        {
            rate.Consume(System.Text.Encoding.UTF8.GetByteCount(line)+2);
            if(!line.Contains("225M",StringComparison.Ordinal))continue;
            using var doc=JsonDocument.Parse(line);pipeline.Push(doc.RootElement);
        }
        sourceSha=Convert.ToHexString(hash.Hash!).ToLowerInvariant();
    }
    using var request=JsonDocument.Parse(File.ReadAllText(args[1]));var output=new List<object>();
    foreach(var session in request.RootElement.EnumerateObject())
    foreach(var index in session.Value.EnumerateArray())
    {
        int i=index.GetInt32();var frames=pipeline.Frames[session.Name];if(i<119||i>=frames.Count)throw new ArgumentException("Window outside recorded history");
        var x=frames.Skip(i-119).Take(120).SelectMany(f=>f.Values).ToArray();var a=Pipeline.Background(frames,i);
        output.Add(new{session=session.Name,index=i,valid=frames[i].Valid,historyEligible=frames.Skip(i-119).Take(120).All(f=>f.Valid)&&Math.Abs(frames[i].At-frames[i-119].At-595)<1e-6,rawSourceSha256=sourceSha,sequence=x,background=a,pooled=Pipeline.Pooled(x,a)});
    }
    var bytes=JsonSerializer.SerializeToUtf8Bytes(output);if(bytes.Length>256*1024*1024)throw new ArgumentException("Output quota");
    var writeRate=new InputRate(5*1024*1024);using var target=File.Create(args[2]);
    for(int at=0;at<bytes.Length;at+=256*1024){int n=Math.Min(256*1024,bytes.Length-at);writeRate.Consume(n);target.Write(bytes,at,n);}
    Console.WriteLine($"PASS native feature extraction {output.Count} requested windows");
}
catch(Exception error){Console.Error.WriteLine(error.Message);Environment.ExitCode=1;}

sealed class Micro
{
    double volume,signedVolume,count,signedCount; (double B,double A)? depth,anchor;
    public void Reset(){volume=signedVolume=count=signedCount=0;depth=anchor=null;}
    public void Trade(double price,double quantity,double bid,double ask,double age)
    {
        if(quantity<=0)return;double side=0;
        if(age>=0&&age<=2&&bid>0&&ask>bid)side=price>=ask?1:price<=bid?-1:0;
        volume+=quantity;signedVolume+=side*quantity;count++;signedCount+=side;
    }
    public void Book(double bid,double ask){depth=(bid,ask);anchor??=depth;}
    public double[] Drain()
    {
        double change=depth.HasValue&&anchor.HasValue?((depth.Value.B-anchor.Value.B)-(depth.Value.A-anchor.Value.A))/Math.Max(anchor.Value.B+anchor.Value.A,1):0;
        var value=new[]{signedVolume/Math.Max(volume,1),signedCount/Math.Max(count,1),change};
        volume=signedVolume=count=signedCount=0;anchor=depth;return value;
    }
}
sealed record Book(double Mid,double[] Values);
sealed class Market
{
    public Book? Book;public double? At,Anchor,LastMid,Low,High;public double Variance,Volume;public readonly Micro Micro=new();
    public void Gap(){Anchor=LastMid=Low=High=null;Variance=Volume=0;Micro.Reset();}
}
sealed record Frame(double At,double Mid,bool Valid,float[] Values);
sealed class Pipeline
{
    readonly Market[] market={new(),new()};double? previous,boundary;long? sequence;string? current;
    public readonly Dictionary<string,List<Frame>> Frames=new();
    public void Push(JsonElement row)
    {
        if(!row.TryGetProperty("Product",out var product))return;int p=product.GetString()=="225MC"?0:product.GetString()=="225M"?1:-1;if(p<0)return;
        string? kind=row.GetProperty("Kind").GetString();if(kind!="trade"&&!(kind?.StartsWith("depth")??false))return;
        if(row.TryGetProperty("Dropped",out var dropped)&&dropped.GetDouble()>0)throw new ArgumentException("Dropped events");
        var stamp=DateTimeOffset.Parse(row.GetProperty("Time").GetString()!,CultureInfo.InvariantCulture);
        long ticks=stamp.UtcTicks-DateTimeOffset.UnixEpoch.UtcTicks;ticks-=ticks%10;double at=ticks/(double)TimeSpan.TicksPerSecond;
        long? seq=row.TryGetProperty("Sequence",out var number)?number.GetInt64():null;
        if(previous.HasValue&&(at<previous||seq.HasValue&&sequence.HasValue&&seq<=sequence))throw new ArgumentException("Strict feature source ordering defect");
        boundary??=(Math.Floor(at/5)+1)*5;
        while(at>=boundary.Value){Emit(boundary.Value);boundary+=5;}
        if(previous.HasValue&&at-previous>10)foreach(var state in market)state.Gap();
        previous=at;sequence=seq;var s=market[p];
        if(kind=="trade")
        {
            double qty=row.GetProperty("TradeQty").GetDouble(),price=row.GetProperty("Last").GetDouble();if(qty<0||price<=0)throw new ArgumentException("Invalid trade");if(qty==0)return;
            s.Volume+=qty;
            if(s.Book is not null&&s.At.HasValue)s.Micro.Trade(price,qty,s.Book.Mid-s.Book.Values[0]*2.5,s.Book.Mid+s.Book.Values[0]*2.5,at-s.At.Value);
            else s.Micro.Trade(price,qty,0,0,3);
            return;
        }
        double[] Read(string name)=>row.GetProperty(name).EnumerateArray().Select(x=>x.GetDouble()).Take(5).ToArray();
        var b=Read("Bid");var a=Read("Ask");var bq=Read("BidQty");var aq=Read("AskQty");
        if(b.Length<5||a.Length<5||bq.Length<5||aq.Length<5||b.Min()<=0||a.Min()<=0||a[0]<=b[0]||bq.Min()<0||aq.Min()<0||bq.Sum()+aq.Sum()<=0||bq[0]+aq[0]<=0){s.Book=null;s.At=null;return;}
        double mid=(b[0]+a[0])/2,qb=bq.Sum(),qa=aq.Sum(),total=qb+qa,wm=(a[0]*bq[0]+b[0]*aq[0])/(bq[0]+aq[0]);
        double curve=Enumerable.Range(0,3).Sum(i=>bq[i]-2*bq[i+1]+bq[i+2]-aq[i]+2*aq[i+1]-aq[i+2])/total;
        s.Micro.Book(qb,qa);if(s.LastMid.HasValue)s.Variance+=Math.Pow((mid-s.LastMid.Value)/5,2);
        s.Book=new(mid,new[]{(a[0]-b[0])/5,(bq[0]-aq[0])/(bq[0]+aq[0]),(qb-qa)/total,Math.Log(1+total),(wm-mid)/5,curve});s.At=at;s.LastMid=mid;s.Low=s.Low.HasValue?Math.Min(s.Low.Value,mid):mid;s.High=s.High.HasValue?Math.Max(s.High.Value,mid):mid;
    }
    void Emit(double end)
    {
        var at=DateTimeOffset.UnixEpoch.AddSeconds(end);string? key=Context.Session(at);if(key is null)return;
        bool healthy=true;var values=new List<double>();var times=new List<double>();
        foreach(var s in market)
        {
            healthy&=s.Book is not null&&s.At.HasValue&&end-s.At>=0&&end-s.At<=2&&s.Book.Values[0]<=2;times.Add(s.At??0);
            if(s.Book is not null){double mid=s.Book.Mid;values.AddRange(new[]{s.Anchor.HasValue?(mid-s.Anchor.Value)/5:0,( (s.High??mid)-(s.Low??mid))/5,s.Variance,Math.Log(1+s.Volume)});values.AddRange(s.Book.Values);healthy&=s.Anchor.HasValue;s.Anchor=mid;}
            else values.AddRange(new double[10]);
            s.Low=s.High=null;s.Variance=s.Volume=0;
        }
        healthy&=times.Max()-times.Min()<=1&&previous.HasValue&&end-previous<=10;
        if(current!=key){healthy=false;current=key;}
        values.AddRange(Context.Features(at));foreach(var s in market)values.AddRange(s.Micro.Drain());
        if(!Frames.TryGetValue(key,out var frames))Frames[key]=frames=new();if(frames.Count>=20000)throw new ArgumentException("Session frame quota");
        frames.Add(new(end,market[0].Book?.Mid??0,healthy,values.Select(x=>(float)x).ToArray()));
    }
    public static double Std(double[] x){if(x.Length==0)return double.NaN;double mean=x.Average();return Math.Sqrt(x.Sum(v=>(v-mean)*(v-mean))/x.Length);}
    public static float[] Background(List<Frame> frames,int i)
    {
        var output=new List<double>();foreach(int length in new[]{360,1440})
        {
            int start=Math.Max(0,i-length+1);var rows=frames.Skip(start).Take(i-start+1).ToArray();var good=rows.Where(f=>f.Valid).ToArray();
            if(rows.Length>1&&good.Length>0)
            {
                var mid=good.Select(f=>f.Mid).ToArray();double span=Math.Max(mid.Max()-mid.Min(),5);var diff=mid.Zip(mid.Skip(1),(a,b)=>(b-a)/5).ToArray();
                output.AddRange(new[]{(mid[^1]-mid[0])/5,Std(diff),(mid[^1]-mid.Min())/span,good.Average(f=>(double)f.Values[3]),good.Average(f=>(double)f.Values[6]),good.Average(f=>(double)f.Values[4]),rows.Length/(double)length,good.Length/(double)rows.Length});
            }
            else output.AddRange(new double[8]);
        }
        return output.Select(v=>(float)v).ToArray();
    }
    public static double[] Pooled(float[] x,float[] a)
    {
        int[] market=Enumerable.Range(0,20).Concat(Enumerable.Range(39,6)).ToArray(),dynamic={0,6,8,10,16,18,39,42};var result=x.Skip(119*45).Select(v=>(double)v).Concat(a.Select(v=>(double)v)).ToList();
        foreach(int length in new[]{6,12,24,60,120})
        {
            var indices=Enumerable.Range(120-length,length).ToArray();
            result.AddRange(market.Select(column=>indices.Average(j=>(double)x[j*45+column])));
            result.AddRange(dynamic.Select(column=>Std(indices.Select(j=>(double)x[j*45+column]).ToArray())));
            result.AddRange(dynamic.Select(column=>(double)x[119*45+column]-x[(120-length)*45+column]));
        }
        return result.ToArray();
    }
}
static class Context
{
    static readonly TimeZoneInfo Ny=TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"),Chicago=TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");
    static readonly HashSet<string> Closed=new(){"2026-01-01","2026-01-19","2026-02-16","2026-04-03","2026-05-25","2026-06-19","2026-07-03","2026-09-07","2026-11-26","2026-12-25"},Early=new(){"2026-11-27","2026-12-24"};
    public static string? Session(DateTimeOffset at)
    {
        var t=at.ToOffset(TimeSpan.FromHours(8));var time=t.TimeOfDay;
        if(time>=TimeSpan.FromMinutes(465)&&time<TimeSpan.FromMinutes(880))return t.ToString("yyyy-MM-dd")+"-day";
        if(time>=TimeSpan.FromHours(16)||time<TimeSpan.FromMinutes(295))return (time<TimeSpan.FromHours(5)?t.AddDays(-1):t).ToString("yyyy-MM-dd")+"-night";
        return null;
    }
    public static double[] Features(DateTimeOffset at)
    {
        var local=at.ToOffset(TimeSpan.FromHours(8));var et=TimeZoneInfo.ConvertTime(at,Ny);var ct=TimeZoneInfo.ConvertTime(at,Chicago);
        DateTimeOffset Local(DateTime d,int h,int m=0)=>new(d.Date.AddHours(h).AddMinutes(m),TimeSpan.FromHours(8));
        var dayOpen=Local(local.Date,7,45);var dayClose=Local(local.Date,14,45);var nightDate=local.Hour<5?local.Date.AddDays(-1):local.Date;var nightOpen=Local(nightDate,16);var nightClose=Local(nightDate.AddDays(1),5);
        string date=et.ToString("yyyy-MM-dd");int weekday=((int)et.DayOfWeek+6)%7,cweek=((int)ct.DayOfWeek+6)%7;bool usValid=weekday<5&&!Closed.Contains(date);
        DateTimeOffset Eastern(int h,int m=0){var t=et.Date.AddHours(h).AddMinutes(m);return new(t,Ny.GetUtcOffset(t));}
        var usOpen=Eastern(9,30);var usClose=Eastern(Early.Contains(date)?13:16);bool regular=usValid&&at>=usOpen&&at<usClose;
        bool electronic=(cweek<4||cweek==4&&ct.Hour<16||cweek==6&&ct.Hour>=17)&&ct.Hour!=16;
        double Elapsed(DateTimeOffset open)=>(at-open).TotalMinutes;double Remaining(DateTimeOffset close)=>(close-at).TotalMinutes;
        bool[] flags={Elapsed(dayOpen)>=0&&Elapsed(dayOpen)<15,Remaining(dayClose)>0&&Remaining(dayClose)<=15,Elapsed(nightOpen)>=0&&Elapsed(nightOpen)<15,Remaining(nightClose)>0&&Remaining(nightClose)<=15,false,usValid&&Elapsed(usOpen)>=0&&Elapsed(usOpen)<30,regular,electronic&&!regular};
        double minutes=local.Hour*60+local.Minute+local.Second/60.0;var result=flags.Select(v=>v?1.0:0.0).ToList();result.Add(Math.Sin(2*Math.PI*minutes/1440));result.Add(Math.Cos(2*Math.PI*minutes/1440));
        result.AddRange(new[]{Elapsed(dayOpen),Remaining(dayClose),Elapsed(nightOpen),Remaining(nightClose),Elapsed(usOpen),Remaining(usClose)}.Select(v=>Math.Clamp(v/1440,-1,1)));result.AddRange(new[]{1.0,1.0,0.0});return result.ToArray();
    }
}
sealed class InputRate
{
    readonly double speed;const double Capacity=256*1024;double tokens=Capacity;long last=System.Diagnostics.Stopwatch.GetTimestamp();public InputRate(double speed=20*1024*1024){this.speed=speed;}
    public void Consume(int n){var now=System.Diagnostics.Stopwatch.GetTimestamp();tokens=Math.Min(Capacity,tokens+(now-last)/(double)System.Diagnostics.Stopwatch.Frequency*speed);last=now;if(tokens<n){Thread.Sleep(TimeSpan.FromSeconds((n-tokens)/speed));var later=System.Diagnostics.Stopwatch.GetTimestamp();tokens=Math.Max(0,Math.Min(Capacity,tokens+(later-last)/(double)System.Diagnostics.Stopwatch.Frequency*speed)-n);last=later;}else tokens-=n;}
}