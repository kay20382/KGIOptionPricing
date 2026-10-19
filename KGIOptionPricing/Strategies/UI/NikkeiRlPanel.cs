using KGIOptionPricing.Strategies.NikkeiRlDirection;
using KGIOptionPricing.StrategyFramework;
using System.Globalization;
namespace KGIOptionPricing;
public sealed class NikkeiRlPanel:UserControl
{
    public Button StartButton {get;}=Button("啟動模擬");
    public Button PauseButton {get;}=Button("暫停／繼續");
    public Button CloseButton {get;}=Button("關閉／平倉");
    public Button DeleteButton {get;}=Button("刪除");
    public Button ReplayButton {get;}=Button("歷史重播");
    public Button ReplayPauseButton {get;}=Button("重播暫停");
    public Button ReplayExportButton {get;}=Button("匯出績效");
    public TextBox Instance {get;}=new(){Width=220,Text="RL-"+DateTime.Now.ToString("HHmmss")};
    public TextBox Product {get;}=new(){Width=70,Text="225MC",ReadOnly=true};
    public Label Status {get;}=new(){Dock=DockStyle.Bottom,Height=72,Text="研究模型尚未通過績效驗收；等待手動啟動。只保存績效摘要。"};
    public DataGridView Instances {get;}=Grid();
    public DataGridView Executions {get;}=Grid();
    public CheckBox Confirm {get;}=new(){Text="我確認這是真實帳戶委託",AutoSize=true,Visible=false};
    public NumericUpDown Loss {get;}=new(){Minimum=1,Maximum=10000000,Value=10000,Width=95,Visible=false};
    public NumericUpDown Commission {get;}=new(){Value=36,Enabled=false,Visible=false};
    readonly Label parameterInfo=new(){Dock=DockStyle.Top,Height=90};
    static string ParameterText(bool live)=>$"pruned-earlier／seed22／12個訓練時段｜exclude-flow（停用48–53流量因子）｜225MC一口／225M參考\n3秒判斷／15秒預測；{(live?"實盤不額外延遲（0ms）":"模擬動作延遲1ms")}；出場冷卻10秒；同向續抱；固定150點停損；每邊36JPY\n日盤07:45–14:45／夜盤16:00–隔日05:00；收盤前310秒停止新倉並平倉；暖機至少210秒\n同側本機strict-cross限價，碰價不成交；實盤穿價後送市價IOC。模型與夜盤績效尚未通過驗收。";
    bool liveMode;
    public NikkeiRlPanel()
    {
        Dock=DockStyle.Fill;
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Top,Height=42,WrapContents=false};
        buttons.Controls.AddRange([StartButton,PauseButton,CloseButton,DeleteButton,ReplayButton,ReplayPauseButton,ReplayExportButton,Instance,Product]);
        var liveSettings=new FlowLayoutPanel{Dock=DockStyle.Top,Height=32};
        var lossLabel=new Label{Text="單日虧損上限JPY",AutoSize=true,Visible=false};
        liveSettings.Controls.AddRange([Confirm,lossLabel,Loss]);
        ConfigureVisibility+=value=>{liveSettings.Visible=value;lossLabel.Visible=value;};
        liveSettings.Visible=false;
        var parameters=parameterInfo;parameters.Text=ParameterText(false);
        foreach(var item in new[]{("Instance","實例",180),("State","狀態",95),("Reason","原因",240),("Position","部位",70),("Trades","交易筆數",85),("Gross","毛損益JPY",105),("Net","淨損益JPY",105),("Expectancy","每筆毛期望JPY",130),("Win","淨勝率%",95),("AvgWin","平均淨獲利點數",135),("AvgLoss","平均淨損失點數",135),("Factor","獲利因子",95)})
            Instances.Columns.Add(new DataGridViewTextBoxColumn{Name=item.Item1,HeaderText=item.Item2,Width=item.Item3,AutoSizeMode=DataGridViewAutoSizeColumnMode.None});
        foreach(var column in new[]{"時間","實例","商品","買賣","角色","成交價","口數","費用JPY","委託ID"})Executions.Columns.Add(column,column);
        var split=new SplitContainer{Dock=DockStyle.Fill,Orientation=Orientation.Horizontal};split.Panel1.Controls.Add(Instances);split.Panel2.Controls.Add(Executions);
        Controls.Add(split);Controls.Add(parameters);Controls.Add(liveSettings);Controls.Add(buttons);Controls.Add(Status);
    }
    event Action<bool>? ConfigureVisibility;
    static Button Button(string text)=>new(){Text=text,Width=100,Height=30};
    static DataGridView Grid()=>new(){Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.None,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false};
    public void ConfigureLive(bool live)
    {
        parameterInfo.Text=ParameterText(live);
        liveMode=live;Confirm.Visible=Loss.Visible=live;ConfigureVisibility?.Invoke(live);
        StartButton.Text=live?"啟動實盤":"啟動模擬";
        foreach(var button in new[]{ReplayButton,ReplayPauseButton,ReplayExportButton})button.Visible=button.Enabled=!live;
        BackColor=live?Color.MistyRose:SystemColors.Control;
        Status.Text=live?"實盤交易；模型及夜盤績效尚未通過驗收。請核對帳戶、虧損上限並手動啟動。":"研究模擬；暖機至少210秒，等待手動啟動。";
    }
    public StrategyDefinition CreateDefinition(string suffix="")=>new(){StrategyType=NikkeiRlFactory.TypeName,InstanceId=Instance.Text.Trim()+suffix,Universe=new(){Tradable=new("225MC"),References=[new("225M")]},Parameters=new NikkeiRlParameters{LiveTradingEnabled=liveMode}.ToDictionary()};
    static string Format(decimal? x)=>x?.ToString("F2",CultureInfo.InvariantCulture)??"--";
    public void ApplySnapshot(StrategySnapshot s)
    {
        var row=Instances.Rows.Cast<DataGridViewRow>().FirstOrDefault(r=>Equals(r.Cells["Instance"].Value,s.InstanceId));if(row==null)row=Instances.Rows[Instances.Rows.Add()];
        row.SetValues(s.InstanceId,s.State,s.Reason,s.Position,s.CompletedTrades,Format(s.GrossPnl),Format(s.NetPnl),s.CompletedTrades>0?Format(s.GrossPnl/s.CompletedTrades):"--",Format(s.WinRate),Format(s.AverageWinningPoints),Format(s.AverageLosingPoints),Format(s.UserDefinedProfitFactor));
        Status.Text=$"{(liveMode?"實盤交易":"研究模擬")}｜{s.InstanceId}｜{s.Reason}\nactor推論={s.Diagnostics.GetValueOrDefault("InferenceMs","--")}ms（不含特徵、排程、網路）｜模型固定、不自動重訓\n{(liveMode?"成交依券商回報；完整紀錄保存於 StrategyLiveRuns":NikkeiRlParameters.SummaryRoot)}";
    }
    public void AddExecution(ExecutionEvent e){Executions.Rows.Insert(0,e.Timestamp.ToString("HH:mm:ss.fff"),e.StrategyInstanceId,e.Symbol,e.Side,e.Role,e.Price,e.Quantity,e.Commission,e.ClientOrderId);while(Executions.Rows.Count>2000)Executions.Rows.RemoveAt(Executions.Rows.Count-1);}
}
