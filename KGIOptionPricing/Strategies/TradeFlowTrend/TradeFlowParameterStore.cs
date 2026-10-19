using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
namespace KGIOptionPricing.Strategies.TradeFlowTrend;
public static class TradeFlowParameterStore
{
    private static string Canonical(TradeFlowParameters p)=>JsonSerializer.Serialize(p.ToDictionary().OrderBy(x=>x.Key,StringComparer.Ordinal).ToDictionary(x=>x.Key,x=>x.Value));
    public static string Hash(TradeFlowParameters p)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Canonical(p))));
    public static void Save(string path,TradeFlowParameters p)
    {
        p.Validate();string full=Path.GetFullPath(path);Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var document=JsonSerializer.Serialize(new{schemaVersion=1,savedAt=DateTimeOffset.UtcNow,parameterHash=Hash(p),parameters=p.ToDictionary()},new JsonSerializerOptions{WriteIndented=true});
        File.AppendAllText(full+".history.jsonl",JsonSerializer.Serialize(new{at=DateTimeOffset.UtcNow,oldValue=File.Exists(full)?File.ReadAllText(full):null,newValue=document})+Environment.NewLine);
        File.WriteAllText(full+".tmp",document);File.Move(full+".tmp",full,true);
    }
    public static TradeFlowParameters Load(string path)
    {
        using var doc=JsonDocument.Parse(File.ReadAllText(path));var root=doc.RootElement;
        if(root.GetProperty("schemaVersion").GetInt32()!=1)throw new InvalidDataException("Parameter schema");
        var p=TradeFlowParameters.Parse(JsonSerializer.Deserialize<Dictionary<string,string>>(root.GetProperty("parameters"))!);
        if(root.GetProperty("parameterHash").GetString()!=Hash(p))throw new InvalidDataException("Parameter hash mismatch");return p;
    }
}
