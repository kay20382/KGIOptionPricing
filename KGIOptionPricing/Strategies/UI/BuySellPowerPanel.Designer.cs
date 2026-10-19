#nullable enable
namespace KGIOptionPricing;

public sealed partial class BuySellPowerPanel
{
    private System.ComponentModel.IContainer? components;
    private TableLayoutPanel layout = null!;
    private FlowLayoutPanel commands = null!;
    private Button startButton = null!, applyButton = null!, pauseButton = null!, stopButton = null!, replayButton = null!, replayPauseButton = null!;
    private ComboBox instances = null!;
    private Label status = null!;
    private DataGridView parametersGrid = null!, metricsGrid = null!, historyGrid = null!;
    private System.Windows.Forms.Timer refreshTimer = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing) { Unbind(); components?.Dispose(); }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        layout = new TableLayoutPanel(); commands = new FlowLayoutPanel();
        startButton = new Button(); applyButton = new Button(); pauseButton = new Button(); stopButton = new Button();
        replayButton = new Button(); replayPauseButton = new Button(); instances = new ComboBox(); status = new Label();
        parametersGrid = new DataGridView(); metricsGrid = new DataGridView(); historyGrid = new DataGridView();
        refreshTimer = new System.Windows.Forms.Timer(components);
        SuspendLayout(); layout.SuspendLayout();
        Name = "BuySellPowerPanel"; Dock = DockStyle.Fill; Size = new Size(1200, 850);
        layout.Name = "powerLayout"; layout.Dock = DockStyle.Fill; layout.ColumnCount = 1; layout.RowCount = 5;
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,44));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,110));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,46));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,62));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,38));
        commands.Name = "powerCommands"; commands.Dock = DockStyle.Fill; commands.WrapContents = false; commands.AutoScroll = true;
        startButton.Name="powerStart"; startButton.Text="啟動觀測"; startButton.Size=new Size(100,32);
        applyButton.Name="powerApply"; applyButton.Text="套用／重新暖機"; applyButton.Size=new Size(140,32);
        pauseButton.Name="powerPause"; pauseButton.Text="暫停／繼續"; pauseButton.Size=new Size(110,32);
        stopButton.Name="powerStop"; stopButton.Text="停止觀測"; stopButton.Size=new Size(100,32);
        replayButton.Name="powerReplay"; replayButton.Text="歷史重播"; replayButton.Size=new Size(100,32);
        replayPauseButton.Name="powerReplayPause"; replayPauseButton.Text="重播暫停／繼續"; replayPauseButton.Size=new Size(140,32);
        instances.Name="powerInstances"; instances.Width=250; instances.DropDownStyle=ComboBoxStyle.DropDownList;
        commands.Controls.AddRange(new Control[]{startButton,applyButton,pauseButton,stopButton,replayButton,replayPauseButton,instances});
        parametersGrid.Name="powerParameters"; ConfigureGrid(parametersGrid,false);
        parametersGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="Product",HeaderText="商品",Width=110,ReadOnly=true});
        parametersGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="PowerWindowSeconds",HeaderText="力道視窗秒數",Width=180});
        parametersGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="ShortAverageSeconds",HeaderText="短期平均秒數",Width=180});
        parametersGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="LongAverageSeconds",HeaderText="長期平均秒數",Width=180});
        parametersGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="ReversalPoints",HeaderText="反向點數",Width=150});
        parametersGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="MovementAveragePeriods",HeaderText="平均段數",Width=150});
        status.Name="powerStatus"; status.Dock=DockStyle.Fill; status.Text="指標觀測／不產生委託。各商品參數獨立，套用後重新暖機。";
        status.ForeColor=Color.DarkBlue;
        metricsGrid.Name="powerMetrics"; ConfigureGrid(metricsGrid,true);
        metricsGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="Metric",HeaderText="指標／行情／統計",Width=220});
        metricsGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="225",HeaderText="225",Width=290});
        metricsGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="225M",HeaderText="225M",Width=290});
        metricsGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="225MC",HeaderText="225MC",Width=290});
        historyGrid.Name="powerHistory"; ConfigureGrid(historyGrid,true);
        historyGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="Product",HeaderText="商品",Width=80});
        historyGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="Direction",HeaderText="方向",Width=70});
        historyGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="StartPrice",HeaderText="起點",Width=90});
        historyGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="ExtremePrice",HeaderText="極值",Width=90});
        historyGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="Points",HeaderText="點數",Width=70});
        historyGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="StartTime",HeaderText="起始時間",Width=160});
        historyGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="ExtremeTime",HeaderText="極值時間",Width=160});
        historyGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="ConfirmedAt",HeaderText="確認結束時間",Width=160});
        historyGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="Duration",HeaderText="持續秒數",Width=100});
        historyGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="Completed",HeaderText="完成",Width=70});
        historyGrid.Columns.Add(new DataGridViewTextBoxColumn{Name="Reason",HeaderText="結束原因",Width=160});
        layout.Controls.Add(commands,0,0); layout.Controls.Add(parametersGrid,0,1); layout.Controls.Add(status,0,2);
        layout.Controls.Add(metricsGrid,0,3); layout.Controls.Add(historyGrid,0,4); Controls.Add(layout);
        refreshTimer.Interval=250;
        layout.ResumeLayout(false); ResumeLayout(false);
    }

    private static void ConfigureGrid(DataGridView grid,bool readOnly)
    {
        grid.Dock=DockStyle.Fill; grid.ReadOnly=readOnly; grid.AllowUserToAddRows=false; grid.AllowUserToDeleteRows=false;
        grid.RowHeadersVisible=false; grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.None;
        grid.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.None; grid.RowTemplate.Height=23;
        grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect; grid.MultiSelect=false;
    }
}
