using System.Globalization;
using System.Text.Json;
using KGIOptionPricing.Strategies.MultiFactorMeanR17;
using KGIOptionPricing.StrategyFramework;
namespace KGIOptionPricing;
public sealed partial class MultiFactorMeanR17Panel:UserControl
{
    private readonly Dictionary<string,StrategySnapshot> _snapshots=new();
    private readonly List<ExecutionEvent> _fills=new();
    private static readonly (string Key,string Label,bool Diagnostic)[] Fields={
        ("ResearchMeanDeviationTicks","均價偏離（跳）",false),("ResearchQueueImbalance","佇列不平衡",false),("ResearchLastReturnTicks","完成秒報酬（跳）",false),("ResearchTickVolatility30","30秒RMS波動（跳）",false),("ResearchDownsideShare30","下行變異占比",false),("ResearchPrintRate30","30秒成交強度（筆／秒）",false),("Status","暖機／狀態",true),("UpPercent","UpTick %",false),("DownPercent","DownTick %",false),("ZeroPercent","ZeroTick %",false),
        ("BuyPower","BuyPower %",false),("ShortAverage","短期平均 %",false),("LongAverage","長期平均 %",false),("BaisPower","BaisPower 百分點",false),
        ("WeightedMid","第一檔量加權中價",false),("Direction","目前行情",true),("Points","連續點數",false),("AverageUpPoints","平均上漲點數",false),("AverageDownPoints","平均下跌點數",false),
        ("UpSamples","上漲樣本數",false),("DownSamples","下跌樣本數",false),("TradeCount","有效成交筆數",false),("TimeSource","時間來源",true)};
    public MultiFactorMeanR17Panel()
    {
        InitializeComponent();SetParameters(R17Parameters.BestCandidateProfile());Status.Text="R17：均價回歸0.25跳＋Mini短期報酬濾網；波動停損上限150點、0.8R／0.5R移動出場。六日僅三日達標；等待手動啟動。";Instance.Text="MF-R17-"+DateTime.Now.ToString("HHmmss");
        foreach(var field in Fields)Metrics.Rows.Add(field.Label,"--","--","--");Instances.SelectionChanged+=(_,_)=>RenderSelected();
        SaveButton.Click+=(_,_)=>Profile(true);LoadButton.Click+=(_,_)=>Profile(false);
    }
    public void SetParameters(R17Parameters p)
    {Parameters.Rows.Clear();foreach(var pair in p.ToDictionary().Where(x=>x.Key!="CommissionPerSide")){
        int row=Parameters.Rows.Add(pair.Key,pair.Value);
        Parameters.Rows[row].Cells[0].ToolTipText=pair.Key switch{"SignalMode"=>"0：原價格反轉模式；1：兩商品力道持續成立即可進場", "RequireOppositeMovement"=>"1：買方力道僅在下跌段進場，賣方力道僅在上漲段進場；未知方向跳過", "RequireAlignedMovement"=>"1：買方力道僅在上漲段進場，賣方力道僅在下跌段進場；未知方向跳過，不可與反向行情濾網同時啟用", "StopTicks"=>"力道連續模式固定停損跳數", "TargetTicks"=>"力道連續模式固定停利跳數，仍檢查扣成本後報酬風險比", "CooldownSeconds"=>"力道連續模式平倉後冷卻秒數", _=>pair.Key};
        if(pair.Key.StartsWith("Adaptive"))Parameters.Rows[row].Cells[0].ToolTipText=pair.Key switch {"AdaptiveForceMode"=>"1：過去力道指定百分位設定聯合及個別力道門檻；只適用連續力道模式", "AdaptiveAgreementMode"=>"1：過去力道差90百分位設定差距上限，限制10至40百分點", "AdaptiveHoldingMode"=>"1：進場前同方向已完成行情持續時間平均乘倍率，進場時計畫固定", "AdaptiveLookbackSeconds"=>"每秒最多一筆有效樣本的回看秒數；跨時段及重新連線重置", "AdaptiveMinSamples"=>"足夠樣本後啟用；不足時沿用原門檻", "AdaptiveForcePercentile"=>"過去聯合力道絕對值百分位；聯合門檻限制20至60，個別門檻為一半且限制10至30", "AdaptiveHoldingFraction"=>"已完成同方向行情起點至極值的持續秒數平均倍率", "AdaptiveMinHoldingSeconds"=>"自適應持倉秒數下限；上限沿用MaxHoldingSeconds", _=>pair.Key};
        if(pair.Key=="AdaptiveExitMode")Parameters.Rows[row].Cells[0].ToolTipText="0：固定跳數；1：使用進場前微型完成行情段平均，成交後固定停損停利；樣本不足跳過。需連續力道模式";
        if(pair.Key=="StopMovementFraction")Parameters.Rows[row].Cells[0].ToolTipText="多單以平均下跌段、空單以平均上漲段乘此倍率，向上取整跳數並限制上下限";
        if(pair.Key=="TargetMovementFraction")Parameters.Rows[row].Cells[0].ToolTipText="多單以平均上漲段、空單以平均下跌段乘此倍率，向下取整跳數並限制上限；仍檢查扣費後報酬風險比";
        if(pair.Key=="PriceRecoveryTicks")Parameters.Rows[row].Cells[0].ToolTipText="0：停用；大於0：有效微型成交自反向行情極值回復至少指定跳數並完成確認後才可進場，需連續力道與反向行情模式";
        if(pair.Key=="MinJointPower")Parameters.Rows[row].Cells[0].ToolTipText="225M與225MC BuyPower的等權平均，依進場方向正規化後至少達到此百分比；0停用";
        if(pair.Key=="MaxPowerDifference")Parameters.Rows[row].Cells[0].ToolTipText="兩商品BuyPower的絕對差上限（百分點）；200等同停用";
        if(pair.Key=="MinShortAveragePower")Parameters.Rows[row].Cells[0].ToolTipText="兩商品短期時間加權平均依進場方向正規化後各自至少達到此百分比；0停用";
        if(pair.Key=="PowerExitMode")Parameters.Rows[row].Cells[0].ToolTipText="0：微型力道及短均反向；1：微型與小型力道及短均皆反向；2：聯合力道低於轉弱門檻且聯合BaisPower反向。均須持續ExitConfirmSeconds，預設0";
        if(pair.Key=="ExitWeakPowerThreshold")Parameters.Rows[row].Cells[0].ToolTipText="出場模式2：兩商品BuyPower依持倉方向正規化後的等權平均上限，百分點";
        if(pair.Key=="TrailingExitMode")Parameters.Rows[row].Cells[0].ToolTipText="1：啟用獲利後移動出場；使用可執行買賣價高水位，只收緊不放寬。本版新實例預設1；舊設定缺欄位仍為0";
        if(pair.Key=="TrailingActivateRisk")Parameters.Rows[row].Cells[0].ToolTipText="浮盈達實際成交價至原停損距離的此倍率後啟動；1表示1R";
        if(pair.Key=="TrailingDistanceRisk")Parameters.Rows[row].Cells[0].ToolTipText="高水位回撤距離為初始風險距離乘此倍率，向上取整跳數；不保證扣費獲利";
        if(pair.Key=="EntryExecutionMode")Parameters.Rows[row].Cells[0].ToolTipText="0：原市價進場；1：微型中價高於大型時，買進掛買一、賣出市價；低於或相等時，買進市價、賣出掛賣一。限價為ROD";
        if(pair.Key=="WeightedMidMode")Parameters.Rows[row].Cells[0].ToolTipText="0：交叉權重（買價×賣量＋賣價×買量）／總量；1：同側權重（買價×買量＋賣價×賣量）／總量。只用第一檔，資料無效跳過";
        if(pair.Key=="EntryTimeoutSeconds")Parameters.Rows[row].Cells[0].ToolTipText="第一檔正常未成交：逾時撤單、等確認後冷卻再評估。拒單、市價逾時、暫停或關閉仍停止新倉；舊單晚到成交保護平倉";
    }Commission.Value=p["CommissionPerSide"];foreach(DataGridViewRow r in Parameters.Rows)if(r.Cells[0].Value!.ToString()=="ResearchVolatilityMaxStopTicks")r.Cells[0].ToolTipText="最高30跳＝150點；限制停損觸發距離，非保證成交損失。";DescribeFactorParameters();}
    private void DescribeFactorParameters()
    {
        var tips=new Dictionary<string,string>{
            ["ResearchFactorMode"]="本版預設3：225MC已完成秒數視窗的均價回歸；變更模式後原六日績效不適用。",
            ["ResearchScoreThreshold"]="均價與最新已完成秒中價的偏離，單位跳；預設0.25。正值買進，負值賣出。",
            ["ResearchFlowFloor"]="225M/225MC BuyPower平均除以100，再乘進場方向；預設至少-0.2。",
            ["ResearchQueueFloor"]="225M/225MC第一檔佇列不平衡平均乘進場方向；預設至少-0.3。",
            ["ResearchMinSignedMiniLastReturnTicks"]="225M最近兩個已完成秒中價報酬乘進場方向，預設至少-0.5跳；缺樣本跳過。",
            ["ResearchMinLargeVariancePressure"]="大型方向變異壓力：方向×(1−2×下行變異占比)，預設至少-0.1；變異為零或缺值跳過。",
            ["ResearchMinLargePrintRate"]="大型最近30秒正口數成交筆數／30；預設至少0.3筆／秒。",
            ["ResearchVolatilityRiskMultiplier"]="225MC固定30秒RMS跳動波動×sqrt(30)×倍率；停損向上取整並限制4..30跳。",
            ["ResearchVolatilityRewardRatio"]="停利跳數約為停損跳數乘1.5，向上取整，上限60跳。",
            ["ResearchAnchorRiskToFill"]="1：成交後以實際成交價重新建立原規劃的停損停利距離；不擴大風險。",
            ["ResearchDecisionFrameMilliseconds"]="決策頻率250毫秒；UI每秒更新；只使用有效微型成交觸發進場。"
        };
        foreach(DataGridViewRow row in Parameters.Rows)if(tips.TryGetValue(row.Cells[0].Value!.ToString()!,out var tip))row.Cells[0].ToolTipText=tip;
    }
    public R17Parameters ReadParameters()
    {Parameters.EndEdit();var values=Parameters.Rows.Cast<DataGridViewRow>().ToDictionary(r=>r.Cells[0].Value!.ToString()!,r=>r.Cells[1].Value?.ToString()??"");values["CommissionPerSide"]=Commission.Value.ToString(CultureInfo.InvariantCulture);return R17Parameters.Parse(values);}
    public StrategyDefinition CreateDefinition(string suffix="")=>new(){StrategyType=MultiFactorMeanR17Factory.TypeName,InstanceId=Instance.Text.Trim()+suffix,Parameters=ReadParameters().ToDictionary(),Universe=new()};
    public void ConfigureLive(bool live){Confirm.Visible=Loss.Visible=live;ReplayButton.Visible=ReplayPauseButton.Visible=ReplayExportButton.Visible=!live;if(live)BackColor=Color.MistyRose;}
    private void Profile(bool save)
    {Run(()=>{if(save){using var d=new SaveFileDialog{Filter="策略參數|*.json",FileName="multifactor-r17.json"};if(d.ShowDialog(this)==DialogResult.OK)System.IO.File.WriteAllText(d.FileName,JsonSerializer.Serialize(ReadParameters().ToDictionary(),new JsonSerializerOptions{WriteIndented=true}));}else{using var d=new OpenFileDialog{Filter="策略參數|*.json"};if(d.ShowDialog(this)==DialogResult.OK)SetParameters(R17Parameters.Parse(JsonSerializer.Deserialize<Dictionary<string,string>>(System.IO.File.ReadAllText(d.FileName))??throw new ArgumentException("參數檔無效")));}Status.Text="參數組已處理；既有實例不變，空手並關閉後以新實例重新暖機。";});}
    public void ApplySnapshot(StrategySnapshot s)
    {
        _snapshots[s.InstanceId]=s;var row=Instances.Rows.Cast<DataGridViewRow>().FirstOrDefault(r=>Equals(r.Cells["Instance"].Value,s.InstanceId));if(row==null)row=Instances.Rows[Instances.Rows.Add()];
        row.SetValues(s.InstanceId,s.State,s.Reason,s.Position,s.CompletedTrades,s.TotalTradedQuantity,s.GrossPnl,Format(s.ExpectedPnlPerTrade),s.NetPnl,Format(s.WinRate),Format(s.AverageWinningPoints),Format(s.AverageLosingPoints),Format(s.UserDefinedProfitFactor));RenderSelected();
    }
    public void AddExecution(ExecutionEvent e){_fills.Add(e);if(_fills.Count>10000)_fills.RemoveRange(0,1000);RenderSelected();}
    private static string Format(decimal? value)=>value?.ToString("F2",CultureInfo.InvariantCulture)??"--";
    private void RenderSelected()
    {
        string? id=Instances.CurrentRow?.Cells["Instance"].Value?.ToString();if(id==null || !_snapshots.TryGetValue(id,out var s))return;
        for(int row=0;row<Fields.Length;row++)foreach(string product in new[]{"225","225M","225MC"})Metrics.Rows[row].Cells[product].Value=Fields[row].Diagnostic?s.Diagnostics.GetValueOrDefault(product+"."+Fields[row].Key,"--"):Format(s.Features.GetValueOrDefault(product+"."+Fields[row].Key));
        Status.Text=$"多因子均價回歸 R17｜{id}／{s.Reason}／{s.Diagnostics.GetValueOrDefault("PrimaryReasonCode")}　停損={Format(s.Features.GetValueOrDefault("StopTrigger"))}　目標={Format(s.Features.GetValueOrDefault("TargetTrigger"))}\n來回筆數={s.CompletedTrades}　交易口數（含進出）={s.TotalTradedQuantity}　毛損益JPY={Format(s.GrossPnl)}　期望值JPY／筆={Format(s.ExpectedPnlPerTrade)}\n生效力道門檻={Format(s.Features.GetValueOrDefault("EffectivePowerThreshold"))}　聯合={Format(s.Features.GetValueOrDefault("EffectiveJointPower"))}　差距={Format(s.Features.GetValueOrDefault("EffectivePowerDifference"))}　持倉上限秒={Format(s.Features.GetValueOrDefault("EffectiveHoldingSeconds"))}\n歷史：{s.Diagnostics.GetValueOrDefault("HistoryRoot")}　保存異常：{s.Diagnostics.GetValueOrDefault("SaveError")}　執行紀錄異常：{s.Diagnostics.GetValueOrDefault("ObservationError")}";Effective.Text=s.ParameterSummary;
        Status.Text+=$"\n模型狀態：{s.Diagnostics.GetValueOrDefault("ResearchStatus")}\n進場委託={s.Diagnostics.GetValueOrDefault("EntrySelectedOrderType")}　決策加權中差={Format(s.Features.GetValueOrDefault("EntryWeightedMidDifference"))}　資料={s.Diagnostics.GetValueOrDefault("EntryQuoteGate")}";
        Executions.Rows.Clear();foreach(var e in _fills.Where(e=>e.StrategyInstanceId==id).TakeLast(1000).Reverse())Executions.Rows.Add(e.Timestamp.ToString("HH:mm:ss.fff"),e.StrategyInstanceId,e.ClientOrderId,e.OrderId,e.Symbol,e.Side,e.Role,e.Price,e.Quantity,e.TriggerMarketPrice,e.Commission,e.MarketSequence);
    }
    public void Run(Action action){try{action();}catch(Exception ex){MessageBox.Show(this,ex.Message,"多因子均價回歸 R17",MessageBoxButtons.OK,MessageBoxIcon.Warning);}}
}
