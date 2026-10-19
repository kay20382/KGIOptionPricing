using System.Text.Json;
namespace NikkeiPredictionDiagnostics;
public sealed class BaselinePredictor
{
    private readonly JsonElement model;
    private readonly int[] columns;
    private readonly double[] means,scales;
    public BaselinePredictor(string json)
    {
        model=JsonDocument.Parse(json).RootElement.Clone();
        if(model.GetProperty("schema").GetString()!="nikkei-prediction-baseline-v1")throw new ArgumentException("Unknown baseline schema");
        columns=model.GetProperty("columns").EnumerateArray().Select(v=>v.GetInt32()).ToArray();
        means=Numbers("means");scales=Numbers("scales");
        if(columns.Length==0||columns.Distinct().Count()!=columns.Length||columns.Any(c=>c<0||c>=60||c is >=30 and <=38||c is >=48 and <=53)||means.Length!=columns.Length||scales.Length!=columns.Length||scales.Any(s=>!double.IsFinite(s)||s<=0))throw new ArgumentException("Invalid market-only normalizer");
        if(model.GetProperty("kind").GetString() is not ("ridge" or "gbr"))throw new ArgumentException("Unknown model kind");
    }
    private double[] Numbers(string key)=>model.GetProperty(key).EnumerateArray().Select(v=>v.GetDouble()).ToArray();
    public double Predict(double[] observation)
    {
        if(columns.Any(c=>c>=observation.Length)||observation.Any(v=>!double.IsFinite(v)))throw new ArgumentException("Invalid observation");
        double[] x=columns.Select((c,i)=>Math.Clamp((observation[c]-means[i])/scales[i],-8,8)).ToArray();
        if(model.GetProperty("kind").GetString()=="ridge")
        {
            var coefficients=Numbers("coefficients");
            if(coefficients.Length!=x.Length)throw new ArgumentException("Invalid coefficients");
            return model.GetProperty("intercept").GetDouble()+x.Select((v,i)=>v*coefficients[i]).Sum();
        }
        double result=model.GetProperty("initial").GetDouble(),rate=model.GetProperty("learningRate").GetDouble();
        foreach(var tree in model.GetProperty("trees").EnumerateArray())
        {
            int node=0,count=tree.GetProperty("left").GetArrayLength();
            for(int visited=0;tree.GetProperty("left")[node].GetInt32()!=-1;visited++)
            {
                if(visited>=count)throw new ArgumentException("Cyclic tree");
                int feature=tree.GetProperty("feature")[node].GetInt32();
                node=((float)x[feature]<=tree.GetProperty("threshold")[node].GetDouble()?tree.GetProperty("left"):tree.GetProperty("right"))[node].GetInt32();
            }
            result+=rate*tree.GetProperty("value")[node].GetDouble();
        }
        return result;
    }
}
