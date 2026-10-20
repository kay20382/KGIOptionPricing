using System.Globalization;
using KGIOptionPricing.Strategies.MeanReversionScalper;
using KGIOptionPricing.Strategies.MiniLeadMicro;
using KGIOptionPricing.Strategies.NikkeiMicrostructureAlpha;
using KGIOptionPricing.Strategies.OrderBookTrend;
using KGIOptionPricing.Strategies.PeriodicQueueCycle;
using KGIOptionPricing.Strategies.WaveletScalper;
using KGIOptionPricing.StrategyFramework;

namespace KGIOptionPricing;

/// <summary>Visually distinct real-account strategy control surface.</summary>
public sealed partial class LiveStrategyCenterForm : Form
{
    private readonly LiveStrategyService _service = null!;
    private readonly NikkeiRlPanel _rlPanel=new();
    private readonly MultiFactorMeanR17Panel _r17Panel=new();
    private readonly Dictionary<string, string> _types = new(StringComparer.OrdinalIgnoreCase);
    private readonly TextBox _mrDayStart = new() { Name = "mrDayStartTime", Text = "07:45", Width = 56, MaxLength = 5 };
    private readonly TextBox _mrDayStop = new() { Name = "mrDayStopTime", Text = "14:40", Width = 56, MaxLength = 5 };
    private readonly TextBox _mrNightStart = new() { Name = "mrNightStartTime", Text = "16:00", Width = 56, MaxLength = 5 };
    private readonly TextBox _mrNightStop = new() { Name = "mrNightStopTime", Text = "04:55", Width = 56, MaxLength = 5 };

    public LiveStrategyCenterForm()
    {
        InitializeComponent();
        _rlPanel.ConfigureLive(true);
        _r17Panel.ConfigureLive(true);
        var r17Tab=new TabPage("多因子均價回歸 R17（實盤）");r17Tab.Controls.Add(_r17Panel);strategyTabs.TabPages.Add(r17Tab);
        var rlTab=new TabPage("日經模型 RL／Grid15（實盤）");rlTab.Controls.Add(_rlPanel);strategyTabs.TabPages.Add(rlTab);
        tradeFlowPanel.ConfigureLive(true);
        powerReversalPanel.ConfigureLive(true);
        AddSessionTimeControls();
    }

    private void AddSessionTimeControls()
    {
        Add("日盤啟動", _mrDayStart);
        Add("日盤停止", _mrDayStop);
        Add("夜盤啟動", _mrNightStart);
        Add("夜盤停止", _mrNightStop);
        mrSettingsPanel.Height = Math.Max(mrSettingsPanel.Height, 172);
        void Add(string label, Control input)
        {
            mrSettingsPanel.Controls.Add(new Label { Text = label, AutoSize = false, Width = 72, Height = 24,
                TextAlign = ContentAlignment.MiddleRight });
            mrSettingsPanel.Controls.Add(input);
        }
    }

    public LiveStrategyCenterForm(LiveStrategyService service) : this()
    {
        _service = service;
        liveWarningBanner.Text = $"⚠ 實盤交易區／帳號 {_service.Account}：成交會產生真實損益；內外盤策略依確認口數，其餘策略維持 1 口限制";
        WireLiveButtons();
        _rlPanel.Instance.Text=NewId("LIVE-RL","225MC");
        _mrId.Text = NewId("LIVE-MR", _mrProduct.Text); _wsId.Text = NewId("LIVE-WS", _wsProduct.Text); _naId.Text = NewId("LIVE-NA", _naProduct.Text); _mlId.Text = NewId("LIVE-ML", _mlProduct.Text); _obtId.Text = NewId("LIVE-OBT", _obtProduct.Text); _pqcControls.InstanceId.Text = NewId("LIVE-PQC", _pqcControls.ProductId.Text);
        _service.SnapshotChanged += Snapshot; _service.ExecutionOccurred += Fill;
        _service.StrategyRetired += Retired;
        foreach (var snapshot in _service.GetSnapshots()) Upsert(snapshot);
        FormClosed += (_, _) => { _service.SnapshotChanged -= Snapshot; _service.ExecutionOccurred -= Fill; _service.StrategyRetired -= Retired; };
    }

    public LiveStrategyCenterForm(LiveStrategyService service, StrategySimulationService observations) : this(service)
    {
        buySellPowerPanel.Bind(observations);
    }

    private StrategyDefinition MeanReversionDefinition()
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["HorizonSeconds"] = V(_mrH), ["F"] = V(_mrF), ["K"] = V(_mrK),
            ["MinimumRangeTicks"] = V(_mrMinR), ["MaximumRangeTicks"] = V(_mrMaxR),
            ["MaximumSpreadTicks"] = V(_mrSpread),
            ["FreshnessSeconds"] = V(_mrFresh),
            ["UseMovingAverage"] = _mrUseMovingAverage.Checked.ToString(),
            ["TrendShiftTicks"] = V(_mrTrendShiftTicks),
            ["MovingAverageReferenceProductId"] = _mrMovingAverageReference.Text.Trim()
        };
        MeanReversionSessionParameterWriter.Add(parameters,
            _mrDayStart.Text, _mrDayStop.Text, _mrNightStart.Text, _mrNightStop.Text);
        return new StrategyDefinition
        {
            StrategyType = MeanReversionScalperFactory.TypeName,
            InstanceId = _mrId.Text.Trim(),
            Universe = new StrategyUniverse
            {
                Tradable = new ProductSelection(_mrProduct.Text.Trim()),
                References = _mrRefs.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(x => new ProductSelection(x)).ToArray()
            },
            Parameters = parameters
        };
    }

    private StrategyDefinition WaveletDefinition() => new()
    {
        StrategyType = WaveletScalperFactory.TypeName, InstanceId = _wsId.Text.Trim(),
        Universe = new StrategyUniverse { Tradable = new ProductSelection(_wsProduct.Text.Trim()), References = Array.Empty<ProductSelection>() },
        Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["WaveletWindowTicks"] = V(_wsWindow), ["EntrySigma"] = V(_wsSigma), ["ObiThreshold"] = V(_wsObi), ["StopTicks"] = V(_wsStop), ["EnergyMultiplier"] = V(_wsEnergy), ["MaximumSpreadTicks"] = V(_wsSpread), ["FreshnessSeconds"] = V(_wsFresh), ["MaxHoldingSeconds"] = V(_wsHold), ["CooldownSeconds"] = V(_wsCooldown) }
    };

    private StrategyDefinition NikkeiAlphaDefinition() => new()
    {
        StrategyType = NikkeiMicrostructureAlphaFactory.TypeName,
        InstanceId = _naId.Text.Trim(),
        Universe = new StrategyUniverse
        {
            Tradable = new ProductSelection(_naProduct.Text.Trim()),
            References = _naRefs.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => new ProductSelection(x)).ToArray()
        },
        Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["WarmupSeconds"] = V(_naWarm), ["FlowWindowSeconds"] = V(_naFlow), ["VolatilityWindowSeconds"] = V(_naVol),
            ["EntryAlpha"] = V(_naAlpha), ["ContinuationAlpha"] = V(_naContinue), ["MinimumDepth"] = V(_naDepth),
            ["MaximumSpreadTicks"] = V(_naSpread), ["MinimumMoveTicks"] = V(_naMove), ["MinimumNetEdge"] = V(_naEdge),
            ["BaseStopTicks"] = V(_naStop), ["BaseTakeProfitTicks"] = V(_naTarget), ["MinimumRewardRisk"] = V(_naRr),
            ["StopSlippageTicks"] = V(_naStopSlip), ["EntryTimeoutMilliseconds"] = V(_naWait), ["MaxHoldingSeconds"] = V(_naHold),
            ["FreshnessSeconds"] = V(_naFresh), ["RoundTripCost"] = V(_naCost), ["CooldownSeconds"] = V(_naCooldown)
        }
    };

    private StrategyDefinition MiniLeadDefinition() => new()
    {
        StrategyType = MiniLeadMicroFactory.TypeName, InstanceId = _mlId.Text.Trim(),
        Universe = new StrategyUniverse { Tradable = new ProductSelection(_mlProduct.Text.Trim()), References = new[] { new ProductSelection("225M"), new ProductSelection("225") } },
        Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["StrategyMode"] = _mlMode.Text.Trim(), ["WindowMs"] = V(_mlWindow), ["LeaderMoveTicks"] = V(_mlLeader), ["MinPathEfficiency"] = V(_mlPath), ["MinTradeIntensity"] = V(_mlIntensity),
            ["LargeWindowMs"] = V(_mlLargeWindow), ["LargeConfirmMoveTicks"] = V(_mlLargeMove), ["MinLargePathEfficiency"] = V(_mlLargePath), ["LargeConfirmationTimeoutMs"] = V(_mlLargeTimeout),
            ["MaxLargeMiniDispersionTicks"] = V(_mlDispersion), ["MaxCatchUpRatio"] = V(_mlCatch), ["MinExecutableGapTicks"] = V(_mlGap), ["MinExpectedMoveTicks"] = V(_mlExpected),
            ["MinExpectedNetValueYen"] = V(_mlExpectedNet), ["MinWinProbability"] = V(_mlWin), ["MaxQuoteAgeMs"] = V(_mlQuoteAge), ["MaxLargeQuoteAgeMs"] = V(_mlQuoteAge),
            ["MaxCrossSymbolSkewMs"] = V(_mlSkew), ["MaxMicroSpreadTicks"] = V(_mlSpread), ["WarmupSeconds"] = V(_mlWarm), ["ProfitTargetTicks"] = V(_mlProfit),
            ["StopLossTicks"] = V(_mlStop), ["LeaderReversalTicks"] = V(_mlLeaderRev), ["LargeReversalTicks"] = V(_mlLargeRev), ["EmergencyLeaderDispersionTicks"] = "4",
            ["MaxHoldingMs"] = V(_mlHold), ["CooldownMs"] = V(_mlCooldown), ["MaxTradesPerSession"] = V(_mlMaxTrades), ["MaxConsecutiveLosses"] = V(_mlMaxLosses),
            ["CommissionPerSideYen"] = V(_mlFee), ["MicroYenPerPoint"] = "10"
            , ["ResearchValidated"] = _mlValidated.Checked.ToString(CultureInfo.InvariantCulture)
        }
    };

    private StrategyDefinition OrderBookTrendDefinition() => new()
    {
        StrategyType = OrderBookTrendFactory.TypeName, InstanceId = _obtId.Text.Trim(),
        Universe = new StrategyUniverse { Tradable = new ProductSelection(_obtProduct.Text.Trim()), References = Array.Empty<ProductSelection>() },
        Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["MomentumEvents"] = V(_obtMomN), ["MomentumEntryTicks"] = V(_obtMom), ["ObiEntry"] = V(_obtObi), ["OfiWindow"] = V(_obtOfiN),
            ["OfiZEntry"] = V(_obtOfiZ), ["OfiMean"] = V(_obtOfiMean), ["OfiStd"] = V(_obtOfiStd), ["TiTrades"] = V(_obtTiN), ["TiEntry"] = V(_obtTi),
            ["MicropriceEntryTicks"] = V(_obtMp), ["ConfirmEvents"] = V(_obtConfirmN), ["ConfirmPasses"] = V(_obtConfirmPass), ["MaximumSpreadTicks"] = V(_obtSpread),
            ["MinimumWeightedDepth"] = V(_obtDepth), ["VolatilityWindow"] = V(_obtRvN), ["VolatilityLowTicks"] = V(_obtRvLow), ["VolatilityHighTicks"] = V(_obtRvHigh),
            ["ExpectedMfeTicks"] = V(_obtMfe), ["MinimumExpectedMoveTicks"] = V(_obtMinMove), ["MaxParticipationRate"] = V(_obtParticipation), ["MaxContracts"] = "1",
            ["EntryTimeoutEvents"] = V(_obtEntryTimeout), ["StopLossTicks"] = V(_obtStop), ["BreakevenTriggerTicks"] = V(_obtBeTrigger), ["CostBufferTicks"] = V(_obtCostBuffer),
            ["TrailingDrawdownTicks"] = V(_obtTrail), ["ObiExit"] = V(_obtObiExit), ["OfiZExit"] = V(_obtOfiExit), ["TiExit"] = V(_obtTiExit),
            ["MaxHoldEvents"] = V(_obtMaxHold), ["MinProgressTicks"] = V(_obtProgress), ["PreCloseMinutes"] = V(_obtPreClose), ["FreshnessSeconds"] = V(_obtFresh),
            ["MaxDailyTrades"] = V(_obtMaxTrades), ["MaxConsecutiveLosses"] = V(_obtMaxLosses), ["LossCooldownMinutes"] = V(_obtLossCooldown)
        }
    };

    private void WireLiveButtons()
    {
        _r17Panel.StartButton.Click+=(_,_)=>_r17Panel.Run(()=>Start(_r17Panel.CreateDefinition(),_r17Panel.Commission.Value,_r17Panel.Loss.Value,_r17Panel.Confirm,_r17Panel.Instance,"LIVE-MF-R17",_r17Panel.Product,_r17Panel.Status));
        _r17Panel.PauseButton.Click+=(_,_)=>Toggle(_r17Panel.Instances,_r17Panel.Status);
        _r17Panel.CloseButton.Click+=(_,_)=>{
            if(!Selected(_r17Panel.Instances,out var id,out _))return;
            var s=_service.GetCloseStatus(id);
            if(MessageBox.Show(this,$"關閉 {id}：停止新倉、撤進場單，並平倉此策略 {s.Position} 口。","確認關閉與平倉",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
            _service.RequestClose(id,true,out var message);_r17Panel.Status.Text=message;
        };
        _r17Panel.DeleteButton.Click+=(_,_)=>{
            if(!Selected(_r17Panel.Instances,out var id,out _))return;
            var s=_service.GetCloseStatus(id);
            if(s.Position!=0 || s.ActiveBrokerOrders!=0 || s.QuarantinedBrokerOrders!=0 || s.LocalConditions!=0){Warn("請先關閉，待部位與委託歸零後刪除。");return;}
            Delete(_r17Panel.Instances,_r17Panel.Executions,_r17Panel.Status);
        };
        _rlPanel.StartButton.Click+=(_,_)=>Start(_rlPanel.CreateDefinition(),36,_rlPanel.Loss.Value,_rlPanel.Confirm,_rlPanel.Instance,"LIVE-RL",_rlPanel.Product,_rlPanel.Status);
        _rlPanel.PauseButton.Click+=(_,_)=>Toggle(_rlPanel.Instances,_rlPanel.Status);
        _rlPanel.CloseButton.Click+=(_,_)=>CloseRl();
        _rlPanel.DeleteButton.Click+=(_,_)=>{
            if(!Selected(_rlPanel.Instances,out var id,out _))return;
            var status=_service.GetCloseStatus(id);
            if(status.Position!=0||status.ActiveBrokerOrders!=0||status.QuarantinedBrokerOrders!=0||status.LocalConditions!=0){Warn("請先關閉／平倉，待部位與委託歸零後刪除。");return;}
            Delete(_rlPanel.Instances,_rlPanel.Executions,_rlPanel.Status);
        };
        tradeFlowPanel.StartButton.Click += (_,_) => tradeFlowPanel.Run(() => Start(tradeFlowPanel.CreateDefinition(),tradeFlowPanel.Commission.Value,tradeFlowPanel.Loss.Value,tradeFlowPanel.Confirm,tradeFlowPanel.Instance,"LIVE-TF",tradeFlowPanel.Product,tradeFlowPanel.Status));
        powerReversalPanel.StartButton.Click += (_,_) => powerReversalPanel.Run(() => Start(powerReversalPanel.CreateDefinition(),powerReversalPanel.Commission.Value,powerReversalPanel.Loss.Value,powerReversalPanel.Confirm,powerReversalPanel.Instance,"LIVE-POWER",powerReversalPanel.Product,powerReversalPanel.Status));
        powerReversalPanel.PauseButton.Click += (_,_) => Toggle(powerReversalPanel.Instances,powerReversalPanel.Status);
        powerReversalPanel.CloseButton.Click += (_,_) => ClosePowerReversal();
        powerReversalPanel.DeleteButton.Click += (_,_) => {if(Selected(powerReversalPanel.Instances,out var id,out _)){var s=_service.GetCloseStatus(id);if(s.Position!=0 || s.ActiveBrokerOrders!=0 || s.QuarantinedBrokerOrders!=0){Warn("請先關閉，待部位與委託歸零後刪除。");return;}Delete(powerReversalPanel.Instances,powerReversalPanel.Executions,powerReversalPanel.Status);}};
        tradeFlowPanel.PauseButton.Click += (_,_) => Toggle(tradeFlowPanel.Instances,tradeFlowPanel.Status);
        tradeFlowPanel.CloseButton.Click += (_,_) => CloseTradeFlow();
        tradeFlowPanel.DeleteButton.Click += (_,_) => { if(Selected(tradeFlowPanel.Instances,out var id,out _)){var s=_service.GetCloseStatus(id);if(s.Position!=0 || s.ActiveBrokerOrders!=0 || s.QuarantinedBrokerOrders!=0){Warn("請先關閉，待部位與委託歸零後刪除。");return;}Delete(tradeFlowPanel.Instances,tradeFlowPanel.Executions,tradeFlowPanel.Status);} };
        mrStartButton.Click += (_, _) => Start(MeanReversionDefinition(), _mrFee.Value, _mrLoss.Value, _mrConfirm, _mrId, "LIVE-MR", _mrProduct, _mrStatus);
        mrPauseButton.Click += (_, _) => Toggle(_mrGrid, _mrStatus);
        mrCloseButton.Click += (_, _) => Close(_mrGrid, _mrStatus);
        mrReconcileButton.Click += (_, _) => ReconcileExternalCancellation(_mrGrid, _mrStatus);
        mrDeleteButton.Click += (_, _) => Delete(_mrGrid, _mrFills, _mrStatus);
        wsStartButton.Click += (_, _) => Start(WaveletDefinition(), _wsFee.Value, _wsLoss.Value, _wsConfirm, _wsId, "LIVE-WS", _wsProduct, _wsStatus);
        wsPauseButton.Click += (_, _) => Toggle(_wsGrid, _wsStatus);
        wsCloseButton.Click += (_, _) => Close(_wsGrid, _wsStatus);
        wsReconcileButton.Click += (_, _) => ReconcileExternalCancellation(_wsGrid, _wsStatus);
        wsDeleteButton.Click += (_, _) => Delete(_wsGrid, _wsFills, _wsStatus);
        naStartButton.Click += (_, _) => Start(NikkeiAlphaDefinition(), _naFee.Value, _naLoss.Value, _naConfirm, _naId, "LIVE-NA", _naProduct, _naStatus);
        naPauseButton.Click += (_, _) => Toggle(_naGrid, _naStatus);
        naCloseButton.Click += (_, _) => Close(_naGrid, _naStatus);
        naReconcileButton.Click += (_, _) => ReconcileExternalCancellation(_naGrid, _naStatus);
        naDeleteButton.Click += (_, _) => Delete(_naGrid, _naFills, _naStatus);
        mlStartButton.Click += (_, _) => Start(MiniLeadDefinition(), _mlFee.Value, _mlLoss.Value, _mlConfirm, _mlId, "LIVE-ML", _mlProduct, _mlStatus);
        mlPauseButton.Click += (_, _) => Toggle(_mlGrid, _mlStatus);
        mlCloseButton.Click += (_, _) => Close(_mlGrid, _mlStatus);
        mlReconcileButton.Click += (_, _) => ReconcileExternalCancellation(_mlGrid, _mlStatus);
        mlDeleteButton.Click += (_, _) => Delete(_mlGrid, _mlFills, _mlStatus);
        obtStartButton.Click += (_, _) => Start(OrderBookTrendDefinition(), _obtFee.Value, _obtLoss.Value, _obtConfirm, _obtId, "LIVE-OBT", _obtProduct, _obtStatus);
        obtPauseButton.Click += (_, _) => Toggle(_obtGrid, _obtStatus);
        obtCloseButton.Click += (_, _) => Close(_obtGrid, _obtStatus);
        obtReconcileButton.Click += (_, _) => ReconcileExternalCancellation(_obtGrid, _obtStatus);
        obtDeleteButton.Click += (_, _) => Delete(_obtGrid, _obtFills, _obtStatus);
        pqcStartButton.Click += (_, _) => Start(_pqcControls.CreateDefinition(), _pqcControls.Commission.Value, _pqcLoss.Value, _pqcConfirm, _pqcControls.InstanceId, "LIVE-PQC", _pqcControls.ProductId, _pqcStatus);
        pqcPauseButton.Click += (_, _) => Toggle(_pqcGrid, _pqcStatus);
        pqcCloseButton.Click += (_, _) => Close(_pqcGrid, _pqcStatus);
        pqcReconcileButton.Click += (_, _) => ReconcileExternalCancellation(_pqcGrid, _pqcStatus);
        pqcDeleteButton.Click += (_, _) => Delete(_pqcGrid, _pqcFills, _pqcStatus);
    }

    private void Start(StrategyDefinition definition, decimal fee, decimal loss, CheckBox confirm, TextBox id, string prefix, TextBox product, Label status)
    {
        if (!confirm.Checked) { Warn("請先勾選「我確認這是真實帳戶委託」。"); return; }
        if (!_service.IsLoggedIn) { Warn("交易服務尚未登入，禁止啟動實盤策略。"); return; }
        int quantity=definition.StrategyType=="TradeFlowTrend"?Strategies.TradeFlowTrend.TradeFlowParameters.Parse(definition.Parameters).Quantity:1;
        string prompt = $"帳號：{_service.Account}\n策略：{definition.StrategyType}\n實例：{definition.InstanceId}\n商品：{definition.Universe.Tradable.ProductId}\n口數：{quantity}\n每口每次成交費用：¥{fee}\n單日最大虧損：¥{loss}\n\n確定啟動真實委託？";
        if (definition.StrategyType == MeanReversionScalperFactory.TypeName)
            prompt += "\n\n警告：雙側 Auto 開平、快速雙向成交及重啟回補尚未經券商實際通道認證。可能發生未預期部位或無法自動保護的風險；此操作不代表已通過認證。";
        if(definition.StrategyType==Strategies.NikkeiRlDirection.NikkeiRlFactory.TypeName)
            prompt+=$"\n\n模型：{definition.Parameters.GetValueOrDefault("ModelId")}\n門檻JPY：{definition.Parameters.GetValueOrDefault("SelectionThresholdJPY")}\n夜盤：{definition.Parameters.GetValueOrDefault("EnableNightSession")}；實盤額外延遲0ms。\n績效尚未通過驗收。本機穿價後送市價IOC，成交價依券商回報。";
        if (MessageBox.Show(this, prompt, "最後確認：啟動實盤", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        try { _service.Add(definition, fee, loss); _types[definition.InstanceId] = definition.StrategyType; id.Text = NewId(prefix, product.Text); confirm.Checked = false; status.Text = $"{definition.InstanceId} 已啟動真實委託"; }
        catch (Exception ex) { Warn(ex.Message); }
    }

    private void Toggle(DataGridView grid, Label status)
    {
        if (!Selected(grid, out string id, out var row)) return;
        bool paused = row.Cells["State"].Value?.ToString() == StrategyLifecycleState.Suspended.ToString();
        bool ok = paused ? _service.Resume(id) : _service.Pause(id);
        status.Text = ok ? paused ? $"{id} 已恢復實盤訊號" : $"{id} 已暫停新訊號；既有部位仍接收行情並保留風控" : $"{id} 無法切換（可能已關閉）";
    }

    private void CloseTradeFlow()
    {
        if(!Selected(tradeFlowPanel.Instances,out var id,out _))return;
        var s=_service.GetCloseStatus(id);
        if(MessageBox.Show(this,$"關閉 {id}：停止新倉、撤進場單，依設定 IOC／ROD 規則平倉 {s.Position} 口。", "確認關閉與平倉",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
        _service.RequestClose(id,true,out var message);tradeFlowPanel.Status.Text=message;
    }
    private void CloseRl()
    {
        if(!Selected(_rlPanel.Instances,out var id,out _))return;
        var status=_service.GetCloseStatus(id);
        if(MessageBox.Show(this,$"關閉 {id}：停止新倉、撤進場單，並平倉此策略 {status.Position} 口。","確認關閉與平倉",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
        _service.RequestClose(id,true,out var message);_rlPanel.Status.Text=message;
    }

    private void Close(DataGridView grid, Label status)
    {
        if (!Selected(grid, out string id, out _)) return; var s = _service.GetCloseStatus(id); if (!s.Exists) return;
        if (MessageBox.Show(this, FormatOperatorClosePrompt(id, s), "確認關閉實盤策略", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        _service.RequestOperatorClose(id, out string message); status.Text = $"{id}：{message}";
    }

    private void ClosePowerReversal()
    {
        if(!Selected(powerReversalPanel.Instances,out var id,out _))return;
        var s=_service.GetCloseStatus(id);
        if(MessageBox.Show(this,$"關閉 {id}：停止新倉、撤進場單，並平倉此策略 {s.Position} 口。", "確認關閉與平倉",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
        _service.RequestClose(id,true,out var message);powerReversalPanel.Status.Text=message;
    }

    public static string FormatOperatorClosePrompt(string id, LiveStrategyCloseStatus s) =>
        $"策略實例：{id}\n市場有效委託：{s.ActiveBrokerOrders:N0} 筆\n本機待觸發：{s.LocalConditions:N0} 筆\n" +
        $"待晚到成交觀察：{s.QuarantinedBrokerOrders:N0} 筆\n策略帳本部位：{s.Position} 口\n\n" +
        "將停止新單，僅撤銷此策略的有效委託。此操作不會平倉；有部位請自行核對券商帳戶。是否繼續？";

    private void ReconcileExternalCancellation(DataGridView grid, Label status)
    {
        if (!Selected(grid, out string id, out _))
        {
            var symbols = _service.GetPriorProcessSymbols();
            if (symbols.Count == 0)
            {
                status.Text = "目前帳號沒有待人工核對的舊程序委託。";
                return;
            }
            foreach (string symbol in symbols)
            {
                var owners = _service.GetPriorProcessOrdersForSymbol(symbol);
                if (owners.Count == 0) continue;
                string account = _service.Account;
                string emptyListPrompt = $"帳號：{account}\n合約：{symbol}\n舊程序委託：{owners.Count} 筆\n" +
                    string.Join("\n", owners.Select(x => $"{x.StrategyInstanceId} / {x.ClientOrderId}")) +
                    "\n\n列表空白不代表券商已無委託或部位。只有已確認券商無此合約工作委託、帳戶部位為零，" +
                    "且上述舊策略成交與部位均已核對處理完成，才可確認。\n" +
                    "此操作只保存這份清單的人工核對紀錄；保留原歸屬與成交，不會送單或啟動策略。";
                if (MessageBox.Show(this, emptyListPrompt, "確認舊程序委託與部位已核對", MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) continue;
                if (!string.Equals(account, _service.Account, StringComparison.OrdinalIgnoreCase))
                {
                    Warn("帳號已改變，請重新核對。");
                    return;
                }
                bool confirmed = _service.ConfirmPriorProcessOrdersForSymbol(symbol, owners, out string emptyListMessage);
                status.Text = $"{symbol}：{emptyListMessage}";
                if (!confirmed) { Warn(emptyListMessage); return; }
            }
            return;
        }
        LiveStrategyCloseStatus close = _service.GetCloseStatus(id);
        if (!close.Exists) return;
        var priorOrders = _service.GetPriorProcessOrders(id);
        if (priorOrders.Count > 0 && close.ActiveBrokerOrders == 0)
        {
            string priorPrompt = $"帳號：{_service.Account}\n舊程序委託：{priorOrders.Count} 筆\n" +
                string.Join("\n", priorOrders.Select(x => $"{x.StrategyInstanceId} / {x.ClientOrderId}")) +
                "\n\n請先關閉同商品策略。只有已核對券商目前無該商品工作委託、帳戶部位為零，" +
                "且上述舊策略的成交與部位均已核對處理完成，才可確認。\n" +
                "此操作會保存人工核對紀錄，解除上述舊程序的新單阻擋；不會送單或自動啟動策略。";
            if (MessageBox.Show(this, priorPrompt, "確認舊程序委託與部位已核對", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            bool confirmed = _service.ConfirmPriorProcessOrders(id, priorOrders, out string priorMessage);
            status.Text = priorMessage;
            if (!confirmed) Warn(priorMessage);
            return;
        }
        if (!close.ClosePending)
        {
            Warn("請先按「關閉策略（撤單）」，停止新訊號並啟動撤單流程。");
            return;
        }
        if (close.ManualReconciliationRequired)
        {
            Warn("已偵測成交數量協定異常；不得以人工同步刪單取代券商成交與實際部位核對。");
            return;
        }
        if (close.ActiveBrokerOrders == 0)
        {
            Warn("本機沒有待同步的市場委託；請確認部位與 late-fill 觀察狀態。");
            return;
        }

        string prompt = $"帳號：{_service.Account}\n策略實例：{id}\n本機仍記錄市場委託：{close.ActiveBrokerOrders} 筆\n實際部位：{close.Position} 口\n\n"
            + "請只在券商委託畫面已確認此策略所有委託皆不存在時選擇「是」。\n"
            + "系統會把本機委託標記為人工核對取消；不會平倉，仍保留 late-fill 觀察紀錄。";
        if (MessageBox.Show(this, prompt, "最後確認：人工同步券商刪單", MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        bool applied = _service.ReconcileExternallyCancelledOrders(id, out string message);
        status.Text = $"{id}：{message}";
        if (!applied) Warn(message);
    }

    private void Delete(DataGridView grid, DataGridView fills, Label status)
    {
        if (!Selected(grid, out string id, out _)) return;
        LiveStrategyCloseStatus s = _service.GetCloseStatus(id);
        if (!s.Exists) { Warn("找不到實盤策略實例。"); return; }
        string prompt = FormatOperatorClosePrompt(id, s) + "\n\n撤單確認後將自動從活動列表刪除；歷史成交與歸屬紀錄仍保留，非零部位的損益標記待對帳。";
        if (MessageBox.Show(this, prompt, "確認刪除實盤策略", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        _service.RequestOperatorDelete(id, out string message);
        status.Text = $"{id}：{message}";
    }

    private void Retired(string id)
    {
        if (IsDisposed || !IsHandleCreated) return;
        if (InvokeRequired) { BeginInvoke(new Action(() => Retired(id))); return; }
        foreach (DataGridView grid in new[] { _mrGrid, _wsGrid, _naGrid, _mlGrid, _obtGrid, _pqcGrid, tradeFlowPanel.Instances, powerReversalPanel.Instances,_rlPanel.Instances,_r17Panel.Instances })
        {
            DataGridViewRow? row = grid.Rows.Cast<DataGridViewRow>()
                .FirstOrDefault(x => Equals(x.Cells["Instance"].Value, id));
            if (row != null) grid.Rows.Remove(row);
        }
    }

    private void Snapshot(StrategySnapshot s) { if (InvokeRequired) BeginInvoke(new Action(() => Upsert(s))); else Upsert(s); }
    private void Upsert(StrategySnapshot s)
    {
        if(s.StrategyType==Strategies.NikkeiRlDirection.NikkeiRlFactory.TypeName){_types[s.InstanceId]=s.StrategyType;_rlPanel.ApplySnapshot(s);return;}
        if(s.StrategyType=="TradeFlowTrend"){_types[s.InstanceId]=s.StrategyType;tradeFlowPanel.ApplySnapshot(s);return;}
        if(s.StrategyType=="PowerReversal"){_types[s.InstanceId]=s.StrategyType;powerReversalPanel.ApplySnapshot(s);return;}
        if(s.StrategyType=="MultiFactorMeanR17"){_types[s.InstanceId]=s.StrategyType;_r17Panel.ApplySnapshot(s);return;}
        if (!_service.GetCloseStatus(s.InstanceId).Exists) return;
        _types[s.InstanceId] = s.StrategyType; var grid = s.StrategyType == PeriodicQueueCycleFactory.TypeName ? _pqcGrid : s.StrategyType == OrderBookTrendFactory.TypeName ? _obtGrid : s.StrategyType == MiniLeadMicroFactory.TypeName ? _mlGrid : s.StrategyType == NikkeiMicrostructureAlphaFactory.TypeName ? _naGrid : s.StrategyType == WaveletScalperFactory.TypeName ? _wsGrid : _mrGrid;
        var row = grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(x => Equals(x.Cells["Instance"].Value, s.InstanceId)); if (row == null) row = grid.Rows[grid.Rows.Add()];
        var close = _service.GetCloseStatus(s.InstanceId);
        object[] values = { s.InstanceId, s.StrategyRunGeneration, s.OcoStatusSummary, s.StrategyType, s.ParameterSummary, s.TradableProductId, s.TradableSymbol, s.ReferenceSummary, _service.Account, s.State,
            close.ClosePending ? "關閉中／" + (string.IsNullOrWhiteSpace(close.CloseReason) ? s.Reason : close.CloseReason) : s.Reason, close.ActiveBrokerOrders, close.QuarantinedBrokerOrders, close.LocalConditions, s.Position, s.LastPrice, s.RealizedPnl, s.UnrealizedPnl, s.NetPnl, s.CommissionPerContract, s.CompletedTrades, s.UpdatedAt.ToString("HH:mm:ss.fff") };
        for (int i = 0; i < values.Length; i++) row.Cells[i].Value = values[i]; row.DefaultCellStyle.BackColor = close.ClosePending ? Color.LemonChiffon : Color.White;
    }

    private void Fill(ExecutionEvent e) { if (InvokeRequired) BeginInvoke(new Action(() => AddFill(e))); else AddFill(e); }
    private void AddFill(ExecutionEvent e)
    {
        if(_types.GetValueOrDefault(e.StrategyInstanceId)==Strategies.NikkeiRlDirection.NikkeiRlFactory.TypeName){_rlPanel.AddExecution(e);return;}
        if(_types.GetValueOrDefault(e.StrategyInstanceId)=="TradeFlowTrend"){tradeFlowPanel.AddExecution(e);return;}
        if(_types.GetValueOrDefault(e.StrategyInstanceId)=="PowerReversal"){powerReversalPanel.AddExecution(e);return;}
        if(_types.GetValueOrDefault(e.StrategyInstanceId)=="MultiFactorMeanR17"){_r17Panel.AddExecution(e);return;}
        var grid = _types.TryGetValue(e.StrategyInstanceId, out string? type) && type == PeriodicQueueCycleFactory.TypeName ? _pqcFills : type == OrderBookTrendFactory.TypeName ? _obtFills : type == MiniLeadMicroFactory.TypeName ? _mlFills : type == NikkeiMicrostructureAlphaFactory.TypeName ? _naFills : type == WaveletScalperFactory.TypeName ? _wsFills : _mrFills;
        grid.Rows.Insert(0, e.Timestamp.ToString("HH:mm:ss.fff"), e.StrategyInstanceId, e.Symbol, e.Side, e.Role, e.Price, e.Quantity, e.TriggerMarketPrice, e.Commission, e.MarketSequence);
        while (grid.Rows.Count > 2000) grid.Rows.RemoveAt(grid.Rows.Count - 1);
    }

    private static bool Selected(DataGridView g, out string id, out DataGridViewRow row) { row=g.CurrentRow!; id=row?.Cells["Instance"].Value?.ToString() ?? ""; return row != null && id.Length>0; }
    private void Warn(string text) => MessageBox.Show(this,text,"實盤策略中心",MessageBoxButtons.OK,MessageBoxIcon.Warning);
    private static string V(NumericUpDown c) => c.Value.ToString(CultureInfo.InvariantCulture);
    private static string NewId(string prefix, string product) => $"{prefix}-{product.Trim()}-{DateTime.Now:HHmmss}";
}
