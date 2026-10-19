namespace KGIOptionPricing.Strategies.TradeFlowTrend;
public sealed class TradeFlowWindowSet
{
    private readonly Dictionary<(string,int),TradeFlowWindow> _windows=new();
    public event Action<string,int,RiSample>? RiClosed;
    public TradeFlowWindowSet(TradeFlowParameters p,DateTimeOffset start)
    { foreach(var id in new[]{"225","225M","225MC"}) foreach(var (w,n) in new[]{(30,p.History30),(60,p.History60),(180,p.History180)}) _windows[(id,w)]=new(w,n,start); }
    public void Add(string productId,ClassifiedTrade trade)
    {
        Advance(trade.Timestamp);
        foreach(var w in new[]{30,60,180}) Window(productId,w).Add(trade);
    }
    private TradeFlowWindow Window(string id,int w) => _windows.TryGetValue((id,w),out var value)?value:throw new ArgumentException("未知商品或窗格");
    public void Advance(DateTimeOffset cutoff)
    { foreach(var (key,w) in _windows) foreach(var sample in w.Advance(cutoff)) RiClosed?.Invoke(key.Item1,key.Item2,sample); }
    public FlowWindowSnapshot Get(string productId,int seconds,DateTimeOffset cutoff)=>Window(productId,seconds).Snapshot(cutoff);
}
