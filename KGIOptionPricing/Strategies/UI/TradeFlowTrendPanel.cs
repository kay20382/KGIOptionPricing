using System.Globalization;
using KGIOptionPricing.Strategies.TradeFlowTrend;
using KGIOptionPricing.StrategyFramework;
namespace KGIOptionPricing;
public sealed partial class TradeFlowTrendPanel:UserControl
{
    private readonly Dictionary<string,StrategySnapshot> _snapshots=new();
    private readonly List<ExecutionEvent> _fills=new();
    public IReadOnlyList<DataGridView> FlowTables=>new[]{table30,table60,table180};
    public TradeFlowTrendPanel()
    {
        InitializeComponent();SetParameters(new());Instance.Text=$"TF-225MC-{DateTime.Now:HHmmss}";
        foreach(var table in FlowTables)foreach(var label in new[]{"Up (%)","Zero (%)","Dn (%)","Real (百分點)","Avg (百分點)","Max (百分點)","Min (百分點)","未知 (%)"})table.Rows.Add(label,"--","--","--");
        Instances.SelectionChanged+=(_,_)=>RenderSelected();
        SaveButton.Click+=(_,_)=>Profile(true);LoadButton.Click+=(_,_)=>Profile(false);
    }
    private void Profile(bool save)
    {
        try
        {
            if(save){using var dialog=new SaveFileDialog{Filter="策略參數|*.json",FileName="trade-flow.json"};if(dialog.ShowDialog(this)==DialogResult.OK)TradeFlowParameterStore.Save(dialog.FileName,ReadParameters());}
            else{using var dialog=new OpenFileDialog{Filter="策略參數|*.json"};if(dialog.ShowDialog(this)==DialogResult.OK)SetParameters(TradeFlowParameterStore.Load(dialog.FileName));}
            Status.Text="參數組已處理；本次執行參數固定，新組於空手且委託歸零後重新啟動生效";
        }catch(Exception ex){MessageBox.Show(this,ex.Message,"參數組",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
    }
    public void SetParameters(TradeFlowParameters p){Parameters.Rows.Clear();foreach(var kv in p.ToDictionary())Parameters.Rows.Add(kv.Key,kv.Value);}
    public TradeFlowParameters ReadParameters()
    {Parameters.EndEdit();return TradeFlowParameters.Parse(Parameters.Rows.Cast<DataGridViewRow>().ToDictionary(r=>r.Cells[0].Value!.ToString()!,r=>r.Cells[1].Value?.ToString()??""));}
    public StrategyDefinition CreateDefinition(string suffix="")=>new(){StrategyType=TradeFlowTrendFactory.TypeName,InstanceId=Instance.Text.Trim()+suffix,Parameters=ReadParameters().ToDictionary(),Universe=new()};
    public void ApplySnapshot(StrategySnapshot s)
    {
        _snapshots[s.InstanceId]=s;
        var row=Instances.Rows.Cast<DataGridViewRow>().FirstOrDefault(r=>Equals(r.Cells["Instance"].Value,s.InstanceId));
        if(row==null)row=Instances.Rows[Instances.Rows.Add()];
        row.SetValues(s.InstanceId,s.State,s.Reason,s.Position,s.NetPnl,Format(s.WinRate),Format(s.AverageWinningPoints),Format(s.AverageLosingPoints),Format(s.UserDefinedProfitFactor));
        RenderSelected();
    }
    public void SelectInstance(string id)
    {foreach(DataGridViewRow row in Instances.Rows)if(Equals(row.Cells["Instance"].Value,id)){Instances.CurrentCell=row.Cells[0];RenderSelected();return;}}
    private static string Format(decimal? value)=>value?.ToString("F2",CultureInfo.InvariantCulture)??"--";
    private void RenderSelected()
    {
        string? id=Instances.CurrentRow?.Cells["Instance"].Value?.ToString();if(id==null || !_snapshots.TryGetValue(id,out var s))return;
        int ti=0;foreach(var w in new[]{30,60,180})
        {
            var table=FlowTables[ti++];int ri=0;
            foreach(var field in new[]{"Up","Zero","Dn","Real","Avg","Max","Min","UnknownPercent"})
            {foreach(var product in new[]{"225","225M","225MC"})table.Rows[ri].Cells[product].Value=Format(s.Features.GetValueOrDefault($"{product}.{w}.{field}"));ri++;}
        }
        Status.Text=$"{id}／{s.UpdatedAt:HH:mm:ss}／{s.Reason}\n"+string.Join("  ",s.Diagnostics.Select(x=>$"{x.Key}={x.Value}"))+"\n"+
            string.Join("  ",new[]{"225","225M","225MC"}.Select(p=>$"{p} 快/慢={Format(s.Features.GetValueOrDefault(p+".Fast"))}/{Format(s.Features.GetValueOrDefault(p+".Slow"))} Book年齡={Format(s.Features.GetValueOrDefault(p+".BookAgeMs"))}ms"))+"\n"+
            string.Join("  ",new[]{30,60,180}.Select(w=>$"{w}s 有效Ri（未滿為暫估）："+string.Join(" / ",new[]{"225","225M","225MC"}.Select(p=>$"{p} {Format(s.Features.GetValueOrDefault($"{p}.{w}.ValidSamples"))}/{Format(s.Features.GetValueOrDefault($"{p}.{w}.TargetSamples"))}"))))+"\n"+
            $"均價={Format(s.Features.GetValueOrDefault("PositionAverage"))} 停損={Format(s.Features.GetValueOrDefault("StopTrigger"))} 停利={Format(s.Features.GetValueOrDefault("ProfitTrigger"))} SAR={Format(s.Features.GetValueOrDefault("225MC.Sar"))} SAR狀態={s.Diagnostics.GetValueOrDefault("SarStatus","--")}";
        Effective.Text=s.ParameterSummary;
        Executions.Rows.Clear();foreach(var e in _fills.Where(e=>e.StrategyInstanceId==id).TakeLast(1000).Reverse())
            Executions.Rows.Add(e.Timestamp.ToString("HH:mm:ss.fff"),e.StrategyInstanceId,e.ClientOrderId,e.OrderId,e.Symbol,e.Side,e.Role,e.Price,e.Quantity,e.Commission);
    }
    public void AddExecution(ExecutionEvent e){_fills.Add(e);if(_fills.Count>10000)_fills.RemoveRange(0,1000);RenderSelected();}
    public void ConfigureLive(bool live){Confirm.Visible=live;Loss.Visible=live;ReplayButton.Visible=!live;ReplayPauseButton.Visible=!live;if(live)BackColor=Color.MistyRose;}
    public void Run(Action action){try{action();}catch(Exception ex){MessageBox.Show(this,ex.Message,"內外盤策略",MessageBoxButtons.OK,MessageBoxIcon.Warning);}}
}
