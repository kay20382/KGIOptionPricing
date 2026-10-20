namespace KGIOptionPricing.Strategies.NikkeiRlDirection;
using System.Text.Json;
internal sealed class GridTree
{
    private sealed record Node(int Feature,double Threshold,int Left,int Right,double Value);
    private readonly Node[][] trees;private readonly int size;
    public GridTree(JsonElement model)
    {
        if(model.GetProperty("schema").GetString()!="supervised-return-v1"||model.GetProperty("kind").GetString()!="lightgbm")throw new ArgumentException("Model schema");
        size=model.GetProperty("inputSize").GetInt32();
        trees=model.GetProperty("trees").EnumerateArray().Select(t=>t.EnumerateArray().Select(n=>n.TryGetProperty("value",out var v)?new Node(-1,0,0,0,v.GetDouble()):new Node(n.GetProperty("feature").GetInt32(),n.GetProperty("threshold").GetDouble(),n.GetProperty("left").GetInt32(),n.GetProperty("right").GetInt32(),0)).ToArray()).ToArray();
        if(size<1||trees.Length>1000||trees.Any(t=>t.Length==0||t.Length>1000||t.Any(n=>!double.IsFinite(n.Value)||!double.IsFinite(n.Threshold)||n.Feature>=size||n.Feature>=0&&(n.Left<0||n.Right<0||n.Left>=t.Length||n.Right>=t.Length))))throw new ArgumentException("Tree bounds");
    }
    public double Predict(double[] x)
    {
        if(x.Length!=size||x.Any(v=>!double.IsFinite(v)))throw new ArgumentException("Model input");
        double sum=0;
        foreach(var tree in trees){int i=0,budget=tree.Length;while(tree[i].Feature>=0){if(--budget<0)throw new ArgumentException("Cyclic tree");var n=tree[i];i=x[n.Feature]<=n.Threshold?n.Left:n.Right;}sum+=tree[i].Value;}
        return sum;
    }
}
