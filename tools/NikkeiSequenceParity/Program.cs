using System.Text.Json;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

try
{
    if(args.Length!=4)throw new ArgumentException("model inputs.json native.dll output.json");
    using var doc=JsonDocument.Parse(File.ReadAllText(args[1]));
    var outputs=new List<object>();
    if(args[0].EndsWith(".tree.json",StringComparison.OrdinalIgnoreCase))
    {
        using var model=JsonDocument.Parse(File.ReadAllText(args[0]));
        string? schema=model.RootElement.GetProperty("schema").GetString();
        if(schema!="nikkei-net-alpha-v2"&&schema!="nikkei-action-alpha-v3")throw new ArgumentException("Unknown tree schema");
        int width=model.RootElement.GetProperty("pooledWidth").GetInt32();
        foreach(var item in doc.RootElement.EnumerateArray())
        {
            var row=item.GetProperty("pooled").EnumerateArray().Select(x=>x.GetDouble()).ToArray();
            if(row.Length!=width||row.Any(x=>!double.IsFinite(x)))throw new ArgumentException("Invalid pooled input");
            var watch=Stopwatch.StartNew();
            var net=model.RootElement.GetProperty("netHeads").EnumerateArray().Select(h=>Predict(h,row)).ToArray();
            var win=model.RootElement.GetProperty("winHeads").EnumerateArray().Select(h=>Predict(h,row)).ToArray();
            if(schema=="nikkei-action-alpha-v3"){var fore=Predict(model.RootElement.GetProperty("forecast30Head"),row);watch.Stop();outputs.Add(new{forecast30_ticks=new[]{fore},net_ticks=net,win_probabilities=win,inferenceMs=watch.Elapsed.TotalMilliseconds});}else{watch.Stop();outputs.Add(new{net_ticks=net,win_probabilities=win,inferenceMs=watch.Elapsed.TotalMilliseconds});}
        }
    }
    else
    {
        NativeLibrary.Load(Path.GetFullPath(args[2]));
        using var options=new SessionOptions{IntraOpNumThreads=2,InterOpNumThreads=1};
        using var session=new InferenceSession(args[0],options);
        int width=session.InputMetadata["sequence"].Dimensions[2];
        if(width!=39&&width!=45)throw new ArgumentException("Unsupported feature width");
        foreach(var item in doc.RootElement.EnumerateArray())
        {
            var sequence=item.GetProperty("sequence").EnumerateArray().Select(x=>x.GetSingle()).ToArray();
            var background=item.GetProperty("background").EnumerateArray().Select(x=>x.GetSingle()).ToArray();
            if(sequence.Length!=120*width||background.Length!=16||sequence.Any(x=>!float.IsFinite(x))||background.Any(x=>!float.IsFinite(x)))throw new ArgumentException("Invalid ONNX input");
            var inputs=new[]{NamedOnnxValue.CreateFromTensor("sequence",new DenseTensor<float>(sequence,new[]{1,120,width})),NamedOnnxValue.CreateFromTensor("background",new DenseTensor<float>(background,new[]{1,16}))};
            var watch=Stopwatch.StartNew();using var results=session.Run(inputs);watch.Stop();
            var values=results.ToDictionary(x=>x.Name,x=>x.AsEnumerable<float>().ToArray());
            if(width==39)outputs.Add(new{direction_logits=values["direction_logits"],delta_ticks=values["delta_ticks"],inferenceMs=watch.Elapsed.TotalMilliseconds});else outputs.Add(new{values,inferenceMs=watch.Elapsed.TotalMilliseconds});
        }
    }
    File.WriteAllText(args[3],JsonSerializer.Serialize(outputs));
    Console.WriteLine($"PASS C# model execution {outputs.Count} windows");
}
catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}

static double Predict(JsonElement head,double[] row)
{
    string kind=head.GetProperty("kind").GetString()!;
    if(kind=="constant")return head.GetProperty("value").GetDouble();
    if(kind!="regression"&&kind!="classification")throw new ArgumentException("Unknown head");
    double value=head.GetProperty("bias").GetDouble();
    foreach(var tree in head.GetProperty("trees").EnumerateArray())
    {
        int at=0,steps=0;
        while(!tree[at].GetProperty("leaf").GetBoolean())
        {
            if(++steps>tree.GetArrayLength())throw new ArgumentException("Cyclic tree");
            var node=tree[at];int feature=node.GetProperty("feature").GetInt32();
            if(feature<0||feature>=row.Length)throw new ArgumentException("Tree feature out of bounds");
            at=node.GetProperty(row[feature]<=node.GetProperty("threshold").GetDouble()?"left":"right").GetInt32();
        }
        value+=tree[at].GetProperty("value").GetDouble();
    }
    return kind=="classification"?1/(1+Math.Exp(-Math.Clamp(value,-40,40))):value;
}