using NikkeiRl;

var at=new DateTimeOffset(2026,10,6,8,0,0,TimeSpan.FromHours(8));int passed=0;
void Check(bool x,string name){if(!x){Console.Error.WriteLine(name);Environment.Exit(1);}passed++;}
var f=new ResearchFeatureAccumulator(5);
decimal[] b=[100,95,90,85,80],a=[105,110,115,120,125];int[] q=[10,10,10,10,10];
f.OnBook(at,b,a,q,q);
var x=f.Snapshot(at.AddSeconds(1));
Check(x.Length==20&&x.All(double.IsFinite),"20 finite features");
Check(Math.Abs(x[1])+Math.Abs(x[2])+Math.Abs(x[3])+Math.Abs(x[4])+Math.Abs(x[6])+Math.Abs(x[7])<1e-10,"symmetric book");
Check(Math.Abs(x[0]-Math.Log(101))<1e-10,"visible depth");
f.OnBook(at.AddSeconds(1),b,a,[20,10,10,10,10],q);
x=f.Snapshot(at.AddSeconds(2));Check(x[8]>0,"OFI new bid pressure");
f.OnTrade(at.AddSeconds(2),105,3);f.OnTrade(at.AddSeconds(2.5),105,2);
x=f.Snapshot(at.AddSeconds(3));Check(x[9]==1&&x[12]==2,"quote direction and run");
Check(Math.Abs(x[13]-13d/25)<1e-10,"trade size HHI");
bool rejected=false;try{f.Snapshot(at.AddSeconds(2));}catch(ArgumentException){rejected=true;}Check(rejected,"future event cannot enter snapshot");
for(int i=2;i<=12;i++)f.Snapshot(at.AddSeconds(i*3));
x=f.Snapshot(at.AddSeconds(39));Check(x.Skip(14).All(v=>v==0),"constant midpoint neutral moments");
f.OnBook(at.AddSeconds(40),[100],[105],[1],[1]);Check(!f.BookReady,"insufficient book rejected");
var stale=new ResearchFeatureAccumulator(5);stale.OnBook(at,b,a,q,q);
Check(stale.Snapshot(at.AddSeconds(3)).Take(8).All(v=>v==0),"stale book cannot manufacture trusted depth samples");
var sparse=new ResearchFeatureAccumulator(5);
for(int i=0;i<=10;i++) {sparse.OnBook(at.AddSeconds(i*3),b.Select(p=>p+i*5).ToArray(),a.Select(p=>p+i*5).ToArray(),q,q);sparse.Snapshot(at.AddSeconds(i*3+1));}
Check(!sparse.SnapshotReady,"incomplete OFI window cannot pass research health");
int[] excludedFlow=[48,49,50,51,52,53];
Check(sparse.BookPathReady&&!sparse.FlowReady,"path readiness independent from OFI warmup");
Check(sparse.IsReadyForActorFeatures(excludedFlow),"fully disabled flow features do not impose flow warmup");
Check(!sparse.IsReadyForActorFeatures([]),"legacy all-feature requirement preserved");
Check(!sparse.IsReadyForActorFeatures([48,49,50,51,52]),"one active flow feature still requires warmup");
sparse.OnBook(at.AddSeconds(30.5),b,a,q,q);
Check(!sparse.IsReadyForActorFeatures(excludedFlow),"late delivered event also invalidates cached readiness");
foreach(int[] bad in new int[][]{[-1],[60],[48,48]})
{
    bool badRejected=false;try{sparse.IsReadyForActorFeatures(bad);}catch(ArgumentException){badRejected=true;}
    Check(badRejected,"invalid or duplicate raw feature mask rejected");
}
Check(!stale.IsReadyForActorFeatures(excludedFlow),"disabled flow does not permit stale depth");
var notWarm=new ResearchFeatureAccumulator(5);notWarm.OnBook(at,b,a,q,q);notWarm.Snapshot(at.AddSeconds(1));
Check(!notWarm.IsReadyForActorFeatures(excludedFlow),"disabled flow does not permit incomplete30s path");
var deep=new ResearchFeatureAccumulator(5);deep.OnBook(at,b,a,q,q);
deep.OnBook(at.AddSeconds(1),[100],[105],[20],[10]);deep.OnBook(at.AddSeconds(2),b,a,[20,10,10,10,10],q);
Check(deep.Snapshot(at.AddSeconds(3))[8]>0,"valid BBO transition survives unavailable deep shape");
var recover=new ResearchFeatureAccumulator(5);
for(int i=0;i<=32;i++){recover.OnBook(at.AddSeconds(i),b,a,q,q);if(i%3==2)recover.Snapshot(at.AddSeconds(i+1));}
Check(recover.SnapshotReady,"full path with complete OFI warmup ready");
Check(recover.BookPathReady&&recover.FlowReady&&recover.IsReadyForActorFeatures([]),"all-feature readiness equals legacy ready");
recover.OnBook(at.AddSeconds(33.1),[0],[105],[0],[10]);
Check(!recover.IsReadyForActorFeatures(excludedFlow),"new invalid event cannot reuse earlier snapshot readiness");
foreach(double second in new[]{33.2,34,35})recover.OnBook(at.AddSeconds(second),b,a,q,q);
recover.Snapshot(at.AddSeconds(36));Check(!recover.SnapshotReady,"BBO recovery waits five seconds for complete OFI");
foreach(int second in new[]{36,37,38})recover.OnBook(at.AddSeconds(second),b,a,q,q);
recover.Snapshot(at.AddSeconds(39));Check(recover.SnapshotReady,"BBO flow health restores after five seconds");
var g=new ResearchFeatureAccumulator(5);g.OnBook(at.AddSeconds(2),b,a,q,q);
g.Snapshot(at.AddSeconds(3));g.OnBook(at.AddSeconds(5),b.Select(p=>p+5).ToArray(),a.Select(p=>p+5).ToArray(),q,q);
g.Snapshot(at.AddSeconds(6));g.OnBook(at.AddSeconds(8),b.Select(p=>p+5).ToArray(),a.Select(p=>p+5).ToArray(),q,q);
x=g.Snapshot(at.AddSeconds(9));Check(x[15]>0&&x[16]>0,"rising path and variance");
Console.WriteLine($"PASS {passed} research feature checks");
