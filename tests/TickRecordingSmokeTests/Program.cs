using System.Text.Json;
using KGIOptionPricing;
using KGIOptionPricing.Research;

string config = Path.Combine(AppContext.BaseDirectory, "config.json");
byte[]? original = File.Exists(config) ? File.ReadAllBytes(config) : null;
string root = Path.Combine(Path.GetTempPath(), "tick-config-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    Run("disabled", "{\"SaveTickData\":false}", false);
    Run("enabled", "{\"SaveTickData\":true}", true);
    Run("missing-key", "{}", true);
    Run("invalid-type", "{\"SaveTickData\":\"false\"}", false);
    Run("malformed", "{", false);
    File.Delete(config);
    Verify("missing-file", true);
    Console.WriteLine("PASS TICK config: false, true, missing key/file, invalid type and malformed JSON; no network connection");
}
finally
{
    if (original is null) File.Delete(config);
    else File.WriteAllBytes(config, original);
    Directory.Delete(root, recursive: true);
}

void Run(string name, string json, bool enabled)
{
    File.WriteAllText(config, json);
    Verify(name, enabled);
}

void Verify(string name, bool enabled)
{
    string output = Path.Combine(root, name);
    var messages = new List<string>();
    // Construction reads settings only; never call Connect or a broker gateway.
    var quotes = new KGIQuoteService();
    using (var recorder = new NikkeiResearchRecorder(quotes, output, messages.Add))
    {
        recorder.Start();
        recorder.Start(); // Repeated start must not duplicate the recording session.
    }
    if (!enabled)
    {
        if (Directory.Exists(output))
            throw new Exception($"{name}: disabled TICK recording created an output directory");
        return;
    }
    var files = Directory.GetFiles(output, "*_nikkei_*.jsonl");
    if (files.Length != 1) throw new Exception($"{name}: expected one recording file");
    var rows = File.ReadLines(files[0]).Select(x => JsonDocument.Parse(x)).ToArray();
    try
    {
        if (rows.Count(x => x.RootElement.GetProperty("Kind").GetString() == "session") != 1)
            throw new Exception($"{name}: expected one session header");
    }
    finally { foreach (var row in rows) row.Dispose(); }
}
