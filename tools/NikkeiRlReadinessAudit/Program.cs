using NikkeiRl;

if(args.Length==1&&args[0]=="self-test")
{
    var start=new DateTimeOffset(2026,9,17,12,0,0,TimeSpan.FromHours(8));
    decimal[] bids=[40000,39995,39990,39985,39980],asks=[40005,40010,40015,40020,40025];
    int[] qty=[5,5,5,5,5];
    var sparse=new ResearchFeatureAccumulator(5);
    for(int i=0;i<=10;i++)
    {
        sparse.OnBook(start.AddSeconds(i*3),bids,asks,qty,qty);
        sparse.Snapshot(start.AddSeconds(i*3+1));
    }
    var blocked=ReadinessProbe.Inspect(sparse,start.AddSeconds(31));
    if(!blocked.BookFresh||!blocked.PathReady||blocked.FlowReady||blocked.SnapshotReady)
        throw new Exception("Valid30s path with3s book gaps must expose flow-only readiness failure");
    var continuous=new ResearchFeatureAccumulator(5);
    for(int i=0;i<=30;i++)
    {
        continuous.OnBook(start.AddSeconds(i),bids,asks,qty,qty);
        if(i%3==0)continuous.Snapshot(start.AddSeconds(i+.5));
    }
    var ready=ReadinessProbe.Inspect(continuous,start.AddSeconds(30.5));
    if(!ready.BookFresh||!ready.PathReady||!ready.FlowReady||!ready.SnapshotReady)
        throw new Exception("Observed continuous valid books must be ready");
    continuous.OnBook(start.AddSeconds(31),[],[],[],[]);
    continuous.Snapshot(start.AddSeconds(33.5));
    var invalid=ReadinessProbe.Inspect(continuous,start.AddSeconds(33.5));
    if(invalid.BookFresh||invalid.SnapshotReady)throw new Exception("Invalid book must remain rejected");
    Console.WriteLine("PASS 3 frozen-C# readiness fixtures");
    return;
}
ReadinessProbe.Audit(args);
