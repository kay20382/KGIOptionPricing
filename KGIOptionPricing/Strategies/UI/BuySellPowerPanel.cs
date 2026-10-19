using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text.Json;
using KGIOptionPricing.Strategies.BuySellPower;
using KGIOptionPricing.StrategyFramework;
using KGIOptionPricing.StrategyFramework.Storage;

namespace KGIOptionPricing;

public sealed partial class BuySellPowerPanel : UserControl
{
    private readonly ConcurrentDictionary<string,StrategySnapshot> _latest = new(StringComparer.OrdinalIgnoreCase);
    private StrategySimulationService? _service;
    private StrategySnapshot? _rendered;
    private HistoricalReplayControl? _replay;
    private string? _replayId;
    private string _historySignature = "";
    private readonly string _settingsPath = Path.Combine(AppContext.BaseDirectory,"buy-sell-power-settings.json");
    private static readonly (string Key,string Label,bool Diagnostic)[] Metrics = {
        ("Symbol","合約",true),("Session","交易時段",true),("Status","指標狀態",true),
        ("UpPercent","UpTick (%)",false),("DownPercent","DownTick (%)",false),("ZeroPercent","ZeroTick (%)",false),
        ("BuyPower","BuyPower (%)",false),("ShortAverage","短期時間加權平均 (%)",false),("LongAverage","長期時間加權平均 (%)",false),("BaisPower","BaisPower (百分點)",false),
        ("Direction","目前行情方向",true),("StartPrice","起點價格",false),("ExtremePrice","極值價格",false),("Points","連續點數",false),("DurationSeconds","目前行情持續秒數",false),
        ("AverageUpPoints","平均上漲點數",false),("UpSamples","上漲實際樣本數",false),("AverageDownPoints","平均下跌點數",false),("DownSamples","下跌實際樣本數",false),
        ("Skipped","跳過成交／事件數",false),("TimeSource","時間來源",true)
    };
    public BuySellPowerPanel()
    {
        InitializeComponent();
        foreach(string product in BuySellPowerStrategy.Products) parametersGrid.Rows.Add(product,30,60,180,30,20);
        foreach(var metric in Metrics) metricsGrid.Rows.Add(metric.Label,"尚無資料","尚無資料","尚無資料");
        startButton.Click += (_,_) => Run(StartObservation);
        applyButton.Click += (_,_) => Run(ApplyParameters);
        pauseButton.Click += (_,_) => Run(TogglePause);
        stopButton.Click += (_,_) => Run(StopObservation);
        replayButton.Click += async (_,_) => await ReplayAsync();
        replayPauseButton.Click += (_,_) => { if(_replay == null)return; if(_replay.IsPaused)_replay.Resume();else _replay.Pause(); };
        instances.SelectedIndexChanged += (_,_) => { _rendered=null; _historySignature=""; };
        refreshTimer.Tick += (_,_) => RefreshLatest(); refreshTimer.Start();
        SetBoundState(false);
    }
    public void Bind(StrategySimulationService service)
    {
        Unbind(); _service=service; _service.SnapshotChanged+=Receive;
        foreach(var s in service.GetSnapshots()) Receive(s);
        try { LoadSettings(_settingsPath); } catch(Exception ex) { status.Text="參數讀取異常："+ex.Message; }
        SetBoundState(true);
    }
    private void SetBoundState(bool bound)
    { startButton.Enabled=applyButton.Enabled=pauseButton.Enabled=stopButton.Enabled=replayButton.Enabled=bound; }
    private void Unbind() { if(_service!=null)_service.SnapshotChanged-=Receive; _service=null; }
    private void Receive(StrategySnapshot snapshot)
    { if(snapshot.StrategyType==BuySellPowerFactory.TypeName) _latest[snapshot.InstanceId]=snapshot; }

    public StrategyDefinition CreateDefinition(string id)
    {
        parametersGrid.EndEdit(); var values=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(DataGridViewRow row in parametersGrid.Rows)
        {
            string product=Convert.ToString(row.Cells[0].Value,CultureInfo.InvariantCulture) ?? throw new InvalidDataException("缺少商品。");
            for(int col=1;col<parametersGrid.Columns.Count;col++)
                values[product+"."+parametersGrid.Columns[col].Name]=Convert.ToString(row.Cells[col].Value,CultureInfo.InvariantCulture)??"";
            _=PowerParameters.Parse(values,product);
        }
        return new(){StrategyType=BuySellPowerFactory.TypeName,InstanceId=id,
            Universe=new(){Tradable=new("225MC"),References=new[]{new ProductSelection("225"),new ProductSelection("225M")}},Parameters=values};
    }
    public void SetParameters(IReadOnlyDictionary<string,string> values)
    {
        var parsed=BuySellPowerStrategy.Products.ToDictionary(p=>p,p=>PowerParameters.Parse(values,p));
        foreach(DataGridViewRow row in parametersGrid.Rows)
        {
            string product=Convert.ToString(row.Cells[0].Value,CultureInfo.InvariantCulture) ?? throw new InvalidDataException("缺少商品。");
            var p=parsed[product];
            row.SetValues(product,p.PowerWindowSeconds,p.ShortAverageSeconds,p.LongAverageSeconds,p.ReversalPoints,p.MovementAveragePeriods);
        }
    }
    public void SaveSettings(string path)
    {
        string temp=path+".tmp";
        File.WriteAllText(temp,JsonSerializer.Serialize(CreateDefinition("settings").Parameters,new JsonSerializerOptions{WriteIndented=true}));
        File.Move(temp,path,overwrite:true);
    }
    public void LoadSettings(string path)
    { if(File.Exists(path))SetParameters(JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(path))??throw new InvalidDataException("參數檔無效。")); }
    private string? SelectedId => instances.SelectedItem as string;
    private void StartObservation()
    {
        if(_service==null)return;
        var d=CreateDefinition("POWER-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..6]);
        SaveSettings(_settingsPath); _service.AddLive(d,0); instances.Items.Add(d.InstanceId); instances.SelectedItem=d.InstanceId;
        status.Text="觀測已啟動，三商品重新暖機；不產生委託。";
    }
    private void ApplyParameters()
    {
        if(_replay!=null)throw new InvalidOperationException("歷史重播執行中，請待重播完成後套用參數。");
        _=CreateDefinition("validate"); SaveSettings(_settingsPath);
        if(SelectedId!=null && _service!=null)
        {
            string id=SelectedId; _service.Stop(id);
            var stopped=_service.GetSnapshots().FirstOrDefault(s=>s.InstanceId==id);
            if(stopped?.Diagnostics.GetValueOrDefault("SaveError") is {Length:>0} error)
                throw new IOException("舊實例保存異常，已停止："+error);
        }
        StartObservation();
    }
    private void TogglePause()
    {
        if(SelectedId==null||_service==null)return;
        if(_latest.TryGetValue(SelectedId,out var s)&&s.State==StrategyLifecycleState.Suspended)_service.Resume(SelectedId);
        else _service.Pause(SelectedId);
    }
    private void StopObservation()
    {
        if(SelectedId==_replayId && _replay!=null)throw new InvalidOperationException("請先暫停或等待歷史重播完成。");
        if(SelectedId!=null)_service?.Stop(SelectedId);
    }
    private async Task ReplayAsync()
    {
        if(_service==null||_replay!=null)return;
        using var dialog=new OpenFileDialog{Filter="行情事件|*.jsonl|所有檔案|*.*"};
        if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        try
        {
            var d=CreateDefinition("POWER-REPLAY-"+Guid.NewGuid().ToString("N")[..8]);
            _replayId=d.InstanceId; instances.Items.Add(d.InstanceId); instances.SelectedItem=d.InstanceId;
            _replay=new(); status.Text="歷史重播執行中…";
            string output=await _service.RunBacktestAsync(dialog.FileName,d,0,replayControl:_replay);
            if(!IsDisposed)status.Text="歷史重播完成："+output;
        }
        catch(Exception ex){if(!IsDisposed)status.Text="歷史重播異常："+ex.Message;}
        finally{_replay=null;_replayId=null;}
    }
    private void Run(Action action)
    { try{action();}catch(Exception ex){status.Text=ex.Message;} }
    private void RefreshLatest()
    {
        foreach(string id in _latest.Keys.Order()) if(!instances.Items.Contains(id))instances.Items.Add(id);
        if(instances.SelectedIndex<0 && instances.Items.Count>0)instances.SelectedIndex=instances.Items.Count-1;
        if(SelectedId==null || !_latest.TryGetValue(SelectedId,out var s)||ReferenceEquals(s,_rendered))return;
        _rendered=s;
        for(int row=0;row<Metrics.Length;row++)for(int col=0;col<BuySellPowerStrategy.Products.Length;col++)
        {
            string key=BuySellPowerStrategy.Products[col]+"."+Metrics[row].Key;
            string text;
            if(Metrics[row].Diagnostic)text=s.Diagnostics.GetValueOrDefault(key,"尚無資料");
            else if(s.Features.GetValueOrDefault(key) is decimal value)text=value.ToString("0.####",CultureInfo.InvariantCulture)+(IsPercentageMetric(Metrics[row].Key)?"%":"");
            else if(Metrics[row].Key is "UpPercent" or "DownPercent" or "ZeroPercent" or "BuyPower") text="尚無資料";
            else text=Metrics[row].Key=="BaisPower" && s.Diagnostics.GetValueOrDefault(BuySellPowerStrategy.Products[col]+".Status")=="無法計算" ? "無法計算" : "—";
            metricsGrid.Rows[row].Cells[col+1].Value=text;
        }
        string error=s.Diagnostics.GetValueOrDefault("SaveError","");
        status.Text=(error.Length>0?error:s.State+"／"+s.Reason)+"　待保存："+s.Diagnostics.GetValueOrDefault("PendingHistory","0")+"　"+s.Diagnostics.GetValueOrDefault("HistoryRoot","");
        string signature=string.Join("|",BuySellPowerStrategy.Products.Select(p=>s.Diagnostics.GetValueOrDefault(p+".History","")));
        if(signature==_historySignature)return;
        _historySignature=signature; historyGrid.Rows.Clear();
        foreach(string product in BuySellPowerStrategy.Products)
            if(s.Diagnostics.TryGetValue(product+".History",out string? json))
                foreach(var segment in JsonSerializer.Deserialize<MovementSegment[]>(json)??Array.Empty<MovementSegment>())
                    historyGrid.Rows.Add(product,segment.Direction,segment.StartPrice,segment.ExtremePrice,segment.Points,
                        Time(segment.StartTime),Time(segment.ExtremeTime),Time(segment.ConfirmedAt),segment.DurationSeconds,segment.IsCompleted?"完成":"未完成",segment.EndReason);
    }
    private static string Time(DateTimeOffset at)=>at.ToOffset(TimeSpan.FromHours(8)).ToString("MM-dd HH:mm:ss.fff");
    private static bool IsPercentageMetric(string key) => key is "UpPercent" or "DownPercent" or "ZeroPercent" or "BuyPower" or "ShortAverage" or "LongAverage";
}
