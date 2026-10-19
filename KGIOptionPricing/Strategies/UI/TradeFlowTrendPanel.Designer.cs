#nullable enable
namespace KGIOptionPricing;
public sealed partial class TradeFlowTrendPanel
{
    private System.ComponentModel.IContainer? components;
    public Button StartButton=null!,PauseButton=null!,CloseButton=null!,DeleteButton=null!,ReplayButton=null!,ReplayPauseButton=null!,ReplayExportButton=null!;
    private Button SaveButton=null!,LoadButton=null!;
    public TextBox Instance=null!,Product=null!;
    public NumericUpDown Commission=null!,Loss=null!;
    public CheckBox Confirm=null!;
    public Label Status=null!;
    public DataGridView Instances=null!,Executions=null!;
    private DataGridView Parameters=null!,table30=null!,table60=null!,table180=null!;
    private TextBox Effective=null!;
    protected override void Dispose(bool disposing){if(disposing)components?.Dispose();base.Dispose(disposing);}
    private void InitializeComponent()
    {
        components=new System.ComponentModel.Container();Name="tradeFlowPanel";Dock=DockStyle.Fill;
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=5};
        root.RowStyles.Add(new(SizeType.Absolute,78));root.RowStyles.Add(new(SizeType.Absolute,295));root.RowStyles.Add(new(SizeType.Absolute,130));root.RowStyles.Add(new(SizeType.Percent,100));root.RowStyles.Add(new(SizeType.Absolute,45));
        var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=true};
        StartButton=new(){Text="啟動",Width=70};PauseButton=new(){Text="暫停／繼續",Width=100};CloseButton=new(){Text="關閉",Width=70};DeleteButton=new(){Text="刪除",Width=70};ReplayButton=new(){Text="啟動／繼續回測",Width=125};ReplayPauseButton=new(){Text="暫停回測",Width=85};ReplayExportButton=new(){Text="匯出成交明細",Width=105};
        SaveButton=new(){Text="儲存參數組",Width=95};LoadButton=new(){Text="載入參數組",Width=95};
        Instance=new(){Width=220};Product=new(){Text="225MC",ReadOnly=true,Width=65};
        Commission=new(){Minimum=0,Maximum=100000,DecimalPlaces=2,Value=36,Width=75};Loss=new(){Minimum=1,Maximum=10000000,Value=3000,Width=90};
        Confirm=new(){Text="我確認這是真實帳戶委託",Width=205};
        actions.Controls.AddRange([StartButton,PauseButton,CloseButton,DeleteButton,ReplayButton,ReplayPauseButton,ReplayExportButton,SaveButton,LoadButton,
            new Label{Text="實例",Width=35},Instance,Product,new Label{Text="單邊費用",Width=65},Commission,new Label{Text="單日虧損上限",Width=95},Loss,Confirm]);
        var upper=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoScroll=true,WrapContents=false};
        Parameters=Grid("parameters",350,280,false);Parameters.Columns.Add(new DataGridViewTextBoxColumn{Name="Parameter",HeaderText="參數（可調整）",Width=210,ReadOnly=true});Parameters.Columns.Add(new DataGridViewTextBoxColumn{Name="Value",HeaderText="設定值",Width=110});
        upper.Controls.Add(Parameters);
        table30=FlowTable("30 秒");table60=FlowTable("60 秒");table180=FlowTable("180 秒");
        foreach(var table in new[]{table30,table60,table180}){var group=new GroupBox{Text=table.Name,Width=350,Height=285};table.Dock=DockStyle.Fill;group.Controls.Add(table);upper.Controls.Add(group);}
        Status=new(){Dock=DockStyle.Fill,Text="Real 為移動窗格；Avg/Max/Min 為固定 Ri 歷史；缺值顯示 --",AutoEllipsis=true};
        var monitor=new TabControl{Dock=DockStyle.Fill};var stateTab=new TabPage("策略與共用績效");var fillTab=new TabPage("選定實例成交");
        Instances=Grid("flowInstances",1000,200,true);Instances.Dock=DockStyle.Fill;
        foreach(var item in new[]{("Instance","實例",210),("State","狀態",100),("Reason","原因",340),("Position","部位",70),("NetPnl","淨損益",90),("WinRate","勝率 %",85),("AverageWinningPoints","平均獲利點",100),("AverageLosingPoints","平均虧損點",100),("ProfitFactor","使用者獲利因子",120)})Instances.Columns.Add(new DataGridViewTextBoxColumn{Name=item.Item1,HeaderText=item.Item2,Width=item.Item3});
        Executions=Grid("flowExecutions",1000,200,true);Executions.Dock=DockStyle.Fill;
        foreach(var item in new[]{("Time","時間"),("Instance","實例"),("Client","Client ID"),("Broker","Broker ID"),("Symbol","商品"),("Side","買賣"),("Role","角色"),("Price","價格"),("Quantity","口數"),("Commission","費用")})Executions.Columns.Add(new DataGridViewTextBoxColumn{Name=item.Item1,HeaderText=item.Item2,Width=item.Item1 is "Instance" or "Client" or "Broker"?200:90});
        stateTab.Controls.Add(Instances);fillTab.Controls.Add(Executions);monitor.TabPages.AddRange([stateTab,fillTab]);
        Effective=new(){Dock=DockStyle.Fill,ReadOnly=true,Multiline=true,ScrollBars=ScrollBars.Vertical};
        root.Controls.Add(actions,0,0);root.Controls.Add(upper,0,1);root.Controls.Add(Status,0,2);root.Controls.Add(monitor,0,3);root.Controls.Add(Effective,0,4);Controls.Add(root);
    }
    private static DataGridView Grid(string name,int width,int height,bool readOnly)=>new(){Name=name,Width=width,Height=height,ReadOnly=readOnly,AllowUserToAddRows=false,AllowUserToDeleteRows=false,RowHeadersVisible=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.None,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false,BackgroundColor=Color.White,RowTemplate={Height=29}};
    private static DataGridView FlowTable(string name)
    {
        var grid=Grid(name,340,255,true);grid.Columns.Add(new DataGridViewTextBoxColumn{Name="Metric",HeaderText="指標",Width=115});
        foreach(var id in new[]{"225","225M","225MC"})grid.Columns.Add(new DataGridViewTextBoxColumn{Name=id,HeaderText=id,Width=72});return grid;
    }
}
