using System.Globalization;
using System.IO;
using KGIOptionPricing.Strategies.MeanReversionScalper;
using KGIOptionPricing.Strategies.MiniLeadMicro;
using KGIOptionPricing.Strategies.NikkeiMicrostructureAlpha;
using KGIOptionPricing.Strategies.OrderBookTrend;
using KGIOptionPricing.Strategies.PeriodicQueueCycle;
using KGIOptionPricing.Strategies.TradeFlowTrend;
using KGIOptionPricing.Strategies.WaveletScalper;
using KGIOptionPricing.StrategyFramework;
using KGIOptionPricing.StrategyFramework.Storage;

namespace KGIOptionPricing;

/// <summary>One complete fixed-width tab per strategy type.</summary>
public sealed partial class StrategyCenterForm : Form
{
    private readonly StrategySimulationService _service = null!;
    private readonly NikkeiRlPanel rlPanel = new();
    private readonly MultiFactorMeanR17Panel r17Panel = new();
    private readonly Dictionary<string, string> _strategyTypesByInstance = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HistoricalReplayControl> _historicalReplays = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _historicalBacktestOutputs = new(StringComparer.OrdinalIgnoreCase);
    private readonly TextBox _mrDayStart = new() { Name = "mrDayStartTime", Text = "07:45", Width = 56, MaxLength = 5 };
    private readonly TextBox _mrDayStop = new() { Name = "mrDayStopTime", Text = "14:40", Width = 56, MaxLength = 5 };
    private readonly TextBox _mrNightStart = new() { Name = "mrNightStartTime", Text = "16:00", Width = 56, MaxLength = 5 };
    private readonly TextBox _mrNightStop = new() { Name = "mrNightStopTime", Text = "04:55", Width = 56, MaxLength = 5 };

    public StrategyCenterForm()
    {
        InitializeComponent();
        var rlTab=new TabPage("日經RL（研究模擬）");rlTab.Controls.Add(rlPanel);strategyTabs.TabPages.Add(rlTab);
        tradeFlowPanel.ConfigureLive(false);
        powerReversalPanel.ConfigureLive(false);
        r17Panel.ConfigureLive(false);
        var r17Tab=new TabPage("多因子均價回歸 R17");r17Tab.Controls.Add(r17Panel);strategyTabs.TabPages.Add(r17Tab);
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

    public StrategyCenterForm(StrategySimulationService service) : this()
    {
        _service = service;
        buySellPowerPanel.Bind(service);
        _mrInstance.Text = NewInstance("MR", _mrTradable.Text);
        _wsInstance.Text = NewInstance("WS", _wsTradable.Text);
        _naInstance.Text = NewInstance("NA", _naTradable.Text);
        _mlInstance.Text = NewInstance("ML", "225MC");
        _obtInstance.Text = NewInstance("OBT", _obtTradable.Text);
        _pqcControls.InstanceId.Text = NewInstance("PQC", _pqcControls.ProductId.Text);
        WireStrategyButtons();
        _service.SnapshotChanged += OnSnapshot;
        _service.ExecutionOccurred += OnExecution;
        FormClosed += (_, _) =>
        {
            _service.SnapshotChanged -= OnSnapshot;
            _service.ExecutionOccurred -= OnExecution;
        };
    }

    private void WireStrategyButtons()
    {
        r17Panel.StartButton.Click+=(_,_)=>r17Panel.Run(()=>AddLive(r17Panel.CreateDefinition(),r17Panel.Commission.Value,r17Panel.Instance,"MF-R17",r17Panel.Product,r17Panel.Status));
        r17Panel.PauseButton.Click+=(_,_)=>TogglePauseSelected(r17Panel.Instances,r17Panel.Status);
        r17Panel.CloseButton.Click+=(_,_)=>CloseSelected(r17Panel.Instances,r17Panel.Status);
        r17Panel.DeleteButton.Click+=(_,_)=>DeleteSelected(r17Panel.Instances,r17Panel.Executions,r17Panel.Status);
        r17Panel.ReplayButton.Click+=async(_,_)=>{try{await ReplayAsync(r17Panel.CreateDefinition("-BT-"+DateTime.Now.ToString("HHmmss")),r17Panel.Commission.Value,r17Panel.Status);}catch(Exception ex){r17Panel.Status.Text=ex.Message;}};
        r17Panel.ReplayPauseButton.Click+=(_,_)=>PauseReplay("MultiFactorMeanR17",r17Panel.Status);
        r17Panel.ReplayExportButton.Click+=(_,_)=>ExportBacktest("MultiFactorMeanR17",r17Panel.Status);
        rlPanel.StartButton.Click+=(_,_)=>AddLive(rlPanel.CreateDefinition(),36,rlPanel.Instance,"RL",rlPanel.Product,rlPanel.Status);
        rlPanel.PauseButton.Click+=(_,_)=>TogglePauseSelected(rlPanel.Instances,rlPanel.Status);
        rlPanel.CloseButton.Click+=(_,_)=>CloseSelected(rlPanel.Instances,rlPanel.Status);
        rlPanel.DeleteButton.Click+=(_,_)=>DeleteSelected(rlPanel.Instances,rlPanel.Executions,rlPanel.Status);
        rlPanel.ReplayButton.Click+=async(_,_)=>{try{await ReplayAsync(rlPanel.CreateDefinition("-BT-"+DateTime.Now.ToString("HHmmss")),36,rlPanel.Status);}catch(Exception ex){rlPanel.Status.Text=ex.Message;}};
        rlPanel.ReplayPauseButton.Click+=(_,_)=>PauseReplay("NikkeiRlDirection",rlPanel.Status);
        rlPanel.ReplayExportButton.Click+=(_,_)=>ExportBacktest("NikkeiRlDirection",rlPanel.Status);
        tradeFlowPanel.StartButton.Click += (_,_) => tradeFlowPanel.Run(() => AddLive(tradeFlowPanel.CreateDefinition(),tradeFlowPanel.Commission.Value,tradeFlowPanel.Instance,"TF",tradeFlowPanel.Product,tradeFlowPanel.Status));
        tradeFlowPanel.PauseButton.Click += (_,_) => TogglePauseSelected(tradeFlowPanel.Instances,tradeFlowPanel.Status);
        tradeFlowPanel.CloseButton.Click += (_,_) => CloseSelected(tradeFlowPanel.Instances,tradeFlowPanel.Status);
        tradeFlowPanel.DeleteButton.Click += (_,_) => DeleteSelected(tradeFlowPanel.Instances,tradeFlowPanel.Executions,tradeFlowPanel.Status);
        tradeFlowPanel.ReplayButton.Click += async (_,_) => { try { await ReplayAsync(tradeFlowPanel.CreateDefinition("-BT-"+DateTime.Now.ToString("HHmmss")),tradeFlowPanel.Commission.Value,tradeFlowPanel.Status); } catch(Exception ex) { tradeFlowPanel.Status.Text=ex.Message; } };
        tradeFlowPanel.ReplayPauseButton.Click += (_, _) => PauseReplay(TradeFlowTrendFactory.TypeName, tradeFlowPanel.Status);
        tradeFlowPanel.ReplayExportButton.Click += (_, _) => ExportBacktest(TradeFlowTrendFactory.TypeName, tradeFlowPanel.Status);
        powerReversalPanel.StartButton.Click += (_,_) => powerReversalPanel.Run(() => AddLive(powerReversalPanel.CreateDefinition(),powerReversalPanel.Commission.Value,powerReversalPanel.Instance,"POWER-TRADE",powerReversalPanel.Product,powerReversalPanel.Status));
        powerReversalPanel.PauseButton.Click += (_,_) => TogglePauseSelected(powerReversalPanel.Instances,powerReversalPanel.Status);
        powerReversalPanel.CloseButton.Click += (_,_) => CloseSelected(powerReversalPanel.Instances,powerReversalPanel.Status);
        powerReversalPanel.DeleteButton.Click += (_,_) => DeleteSelected(powerReversalPanel.Instances,powerReversalPanel.Executions,powerReversalPanel.Status);
        powerReversalPanel.ReplayButton.Click += async (_,_) => {try{await ReplayAsync(powerReversalPanel.CreateDefinition("-BT-"+DateTime.Now.ToString("HHmmss")),powerReversalPanel.Commission.Value,powerReversalPanel.Status);}catch(Exception ex){powerReversalPanel.Status.Text=ex.Message;}};
        powerReversalPanel.ReplayPauseButton.Click += (_,_) => PauseReplay("PowerReversal",powerReversalPanel.Status);
        powerReversalPanel.ReplayExportButton.Click += (_,_) => ExportBacktest("PowerReversal",powerReversalPanel.Status);
        mrStartButton.Click += (_, _) => AddLive(MeanReversionDefinition(), _mrCommission.Value, _mrInstance, "MR", _mrTradable, _mrStatus);
        mrPauseButton.Click += (_, _) => TogglePauseSelected(_mrGrid, _mrStatus);
        mrCloseButton.Click += (_, _) => CloseSelected(_mrGrid, _mrStatus);
        mrDeleteButton.Click += (_, _) => DeleteSelected(_mrGrid, _mrExecutions, _mrStatus);
        mrReplayButton.Click += async (_, _) => await ReplayAsync(MeanReversionDefinition("-BT-" + DateTime.Now.ToString("HHmmss")), _mrCommission.Value, _mrStatus);
        mrReplayPauseButton.Click += (_, _) => PauseReplay(MeanReversionScalperFactory.TypeName, _mrStatus);
        mrReplayExportButton.Click += (_, _) => ExportBacktest(MeanReversionScalperFactory.TypeName, _mrStatus);
        wsStartButton.Click += (_, _) => AddLive(WaveletDefinition(), _wsCommission.Value, _wsInstance, "WS", _wsTradable, _wsStatus);
        wsPauseButton.Click += (_, _) => TogglePauseSelected(_wsGrid, _wsStatus);
        wsCloseButton.Click += (_, _) => CloseSelected(_wsGrid, _wsStatus);
        wsDeleteButton.Click += (_, _) => DeleteSelected(_wsGrid, _wsExecutions, _wsStatus);
        wsReplayButton.Click += async (_, _) => await ReplayAsync(WaveletDefinition("-BT-" + DateTime.Now.ToString("HHmmss")), _wsCommission.Value, _wsStatus);
        wsReplayPauseButton.Click += (_, _) => PauseReplay(WaveletScalperFactory.TypeName, _wsStatus);
        wsReplayExportButton.Click += (_, _) => ExportBacktest(WaveletScalperFactory.TypeName, _wsStatus);
        naStartButton.Click += (_, _) => AddLive(NikkeiAlphaDefinition(), _naCommission.Value, _naInstance, "NA", _naTradable, _naStatus);
        naPauseButton.Click += (_, _) => TogglePauseSelected(_naGrid, _naStatus);
        naCloseButton.Click += (_, _) => CloseSelected(_naGrid, _naStatus);
        naDeleteButton.Click += (_, _) => DeleteSelected(_naGrid, _naExecutions, _naStatus);
        naReplayButton.Click += async (_, _) => await ReplayAsync(NikkeiAlphaDefinition("-BT-" + DateTime.Now.ToString("HHmmss")), _naCommission.Value, _naStatus);
        naReplayPauseButton.Click += (_, _) => PauseReplay(NikkeiMicrostructureAlphaFactory.TypeName, _naStatus);
        naReplayExportButton.Click += (_, _) => ExportBacktest(NikkeiMicrostructureAlphaFactory.TypeName, _naStatus);
        mlStartButton.Click += (_, _) => AddLive(MiniLeadDefinition(), _mlCommission.Value, _mlInstance, "ML", new TextBox { Text = "225MC" }, _mlStatus);
        mlPauseButton.Click += (_, _) => TogglePauseSelected(_mlGrid, _mlStatus);
        mlCloseButton.Click += (_, _) => CloseSelected(_mlGrid, _mlStatus);
        mlDeleteButton.Click += (_, _) => DeleteSelected(_mlGrid, _mlExecutions, _mlStatus);
        mlReplayButton.Click += async (_, _) => await ReplayAsync(MiniLeadDefinition("-BT-" + DateTime.Now.ToString("HHmmss")), _mlCommission.Value, _mlStatus);
        mlReplayPauseButton.Click += (_, _) => PauseReplay(MiniLeadMicroFactory.TypeName, _mlStatus);
        mlReplayExportButton.Click += (_, _) => ExportBacktest(MiniLeadMicroFactory.TypeName, _mlStatus);
        obtStartButton.Click += (_, _) => AddLive(OrderBookTrendDefinition(), _obtCommission.Value, _obtInstance, "OBT", _obtTradable, _obtStatus);
        obtPauseButton.Click += (_, _) => TogglePauseSelected(_obtGrid, _obtStatus);
        obtCloseButton.Click += (_, _) => CloseSelected(_obtGrid, _obtStatus);
        obtDeleteButton.Click += (_, _) => DeleteSelected(_obtGrid, _obtExecutions, _obtStatus);
        obtReplayButton.Click += async (_, _) => await ReplayAsync(OrderBookTrendDefinition("-BT-" + DateTime.Now.ToString("HHmmss")), _obtCommission.Value, _obtStatus);
        obtReplayPauseButton.Click += (_, _) => PauseReplay(OrderBookTrendFactory.TypeName, _obtStatus);
        obtReplayExportButton.Click += (_, _) => ExportBacktest(OrderBookTrendFactory.TypeName, _obtStatus);
        pqcStartButton.Click += (_, _) => AddLive(_pqcControls.CreateDefinition(), _pqcControls.Commission.Value, _pqcControls.InstanceId, "PQC", _pqcControls.ProductId, _pqcStatus);
        pqcPauseButton.Click += (_, _) => TogglePauseSelected(_pqcGrid, _pqcStatus);
        pqcCloseButton.Click += (_, _) => CloseSelected(_pqcGrid, _pqcStatus);
        pqcDeleteButton.Click += (_, _) => DeleteSelected(_pqcGrid, _pqcExecutions, _pqcStatus);
        pqcReplayButton.Click += async (_, _) => await ReplayAsync(PeriodicQueueCycleDefinition("-BT-" + DateTime.Now.ToString("HHmmss")), _pqcControls.Commission.Value, _pqcStatus);
        pqcReplayPauseButton.Click += (_, _) => PauseReplay(PeriodicQueueCycleFactory.TypeName, _pqcStatus);
        pqcReplayExportButton.Click += (_, _) => ExportBacktest(PeriodicQueueCycleFactory.TypeName, _pqcStatus);
    }

    private StrategyDefinition MeanReversionDefinition(string suffix = "")
    {
        string[] references = _mrReferences.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["HorizonSeconds"] = Value(_mrH), ["F"] = Value(_mrF), ["K"] = Value(_mrK),
            ["MinimumRangeTicks"] = Value(_mrMinRange), ["MaximumRangeTicks"] = Value(_mrMaxRange),
            ["MaximumSpreadTicks"] = Value(_mrMaxSpread),
            ["FreshnessSeconds"] = Value(_mrFreshness),
            ["UseMovingAverage"] = _mrUseMovingAverage.Checked.ToString(),
            ["TrendShiftTicks"] = Value(_mrTrendShiftTicks),
            ["MovingAverageReferenceProductId"] = _mrMovingAverageReference.Text.Trim()
        };
        MeanReversionSessionParameterWriter.Add(parameters,
            _mrDayStart.Text, _mrDayStop.Text, _mrNightStart.Text, _mrNightStop.Text);
        return new StrategyDefinition
        {
            StrategyType = MeanReversionScalperFactory.TypeName,
            InstanceId = _mrInstance.Text.Trim() + suffix,
            Universe = new StrategyUniverse { Tradable = new ProductSelection(_mrTradable.Text.Trim()), References = references.Select(x => new ProductSelection(x)).ToArray() },
            Parameters = parameters
        };
    }

    private StrategyDefinition WaveletDefinition(string suffix = "") => new()
    {
        StrategyType = WaveletScalperFactory.TypeName,
        InstanceId = _wsInstance.Text.Trim() + suffix,
        Universe = new StrategyUniverse { Tradable = new ProductSelection(_wsTradable.Text.Trim()), References = Array.Empty<ProductSelection>() },
        Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["WaveletWindowTicks"] = Value(_wsWindow), ["EntrySigma"] = Value(_wsEntrySigma), ["ObiThreshold"] = Value(_wsObi),
            ["StopTicks"] = Value(_wsStopTicks), ["EnergyMultiplier"] = Value(_wsEnergy), ["MaximumSpreadTicks"] = Value(_wsMaxSpread),
            ["FreshnessSeconds"] = Value(_wsFreshness), ["MaxHoldingSeconds"] = Value(_wsMaxHolding), ["CooldownSeconds"] = Value(_wsCooldown)
        }
    };

    private StrategyDefinition NikkeiAlphaDefinition(string suffix = "") => new()
    {
        StrategyType = NikkeiMicrostructureAlphaFactory.TypeName,
        InstanceId = _naInstance.Text.Trim() + suffix,
        Universe = new StrategyUniverse
        {
            Tradable = new ProductSelection(_naTradable.Text.Trim()),
            References = _naReferences.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => new ProductSelection(x)).ToArray()
        },
        Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["WarmupSeconds"] = Value(_naWarmup), ["FlowWindowSeconds"] = Value(_naFlow), ["VolatilityWindowSeconds"] = Value(_naVol),
            ["EntryAlpha"] = Value(_naAlpha), ["ContinuationAlpha"] = Value(_naContinueAlpha), ["MinimumDepth"] = Value(_naDepth),
            ["MaximumSpreadTicks"] = Value(_naSpread), ["MinimumMoveTicks"] = Value(_naMove), ["MinimumNetEdge"] = Value(_naEdge),
            ["BaseStopTicks"] = Value(_naStop), ["BaseTakeProfitTicks"] = Value(_naTarget), ["MinimumRewardRisk"] = Value(_naRr),
            ["StopSlippageTicks"] = Value(_naStopSlip), ["EntryTimeoutMilliseconds"] = Value(_naWait), ["MaxHoldingSeconds"] = Value(_naHold),
            ["FreshnessSeconds"] = Value(_naFresh), ["RoundTripCost"] = Value(_naCost), ["CooldownSeconds"] = Value(_naCooldown)
        }
    };

    private StrategyDefinition MiniLeadDefinition(string suffix = "") => new()
    {
        StrategyType = MiniLeadMicroFactory.TypeName, InstanceId = _mlInstance.Text.Trim() + suffix,
        Universe = new StrategyUniverse { Tradable = new ProductSelection("225MC"), References = new[] { new ProductSelection("225M"), new ProductSelection("225") } },
        Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["StrategyMode"] = _mlMode.Text.Trim(), ["WindowMs"] = Value(_mlWindow), ["LeaderMoveTicks"] = Value(_mlLeader), ["MinPathEfficiency"] = Value(_mlPath), ["MinTradeIntensity"] = Value(_mlIntensity),
            ["LargeWindowMs"] = Value(_mlLargeWindow), ["LargeConfirmMoveTicks"] = Value(_mlLargeMove), ["MinLargePathEfficiency"] = Value(_mlLargePath), ["LargeConfirmationTimeoutMs"] = Value(_mlLargeTimeout),
            ["MaxLargeMiniDispersionTicks"] = Value(_mlDispersion), ["MaxCatchUpRatio"] = Value(_mlCatch), ["MinExecutableGapTicks"] = Value(_mlGap), ["MinExpectedMoveTicks"] = Value(_mlExpected),
            ["MinExpectedNetValueYen"] = Value(_mlExpectedNet), ["MinWinProbability"] = Value(_mlWin), ["MaxQuoteAgeMs"] = Value(_mlQuoteAge), ["MaxLargeQuoteAgeMs"] = Value(_mlQuoteAge),
            ["MaxCrossSymbolSkewMs"] = Value(_mlSkew), ["MaxMicroSpreadTicks"] = Value(_mlSpread), ["WarmupSeconds"] = Value(_mlWarmup), ["ProfitTargetTicks"] = Value(_mlProfit),
            ["StopLossTicks"] = Value(_mlStop), ["LeaderReversalTicks"] = Value(_mlLeaderReversal), ["LargeReversalTicks"] = Value(_mlLargeReversal), ["EmergencyLeaderDispersionTicks"] = "4",
            ["MaxHoldingMs"] = Value(_mlHold), ["CooldownMs"] = Value(_mlCooldown), ["MaxTradesPerSession"] = Value(_mlMaxTrades), ["MaxConsecutiveLosses"] = Value(_mlMaxLosses),
            ["CommissionPerSideYen"] = Value(_mlCommission), ["MicroYenPerPoint"] = "10"
        }
    };

    private StrategyDefinition OrderBookTrendDefinition(string suffix = "") => new()
    {
        StrategyType = OrderBookTrendFactory.TypeName, InstanceId = _obtInstance.Text.Trim() + suffix,
        Universe = new StrategyUniverse { Tradable = new ProductSelection(_obtTradable.Text.Trim()), References = Array.Empty<ProductSelection>() },
        Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["MomentumEvents"] = Value(_obtMomN), ["MomentumEntryTicks"] = Value(_obtMom), ["ObiEntry"] = Value(_obtObi),
            ["OfiWindow"] = Value(_obtOfiN), ["OfiZEntry"] = Value(_obtOfiZ), ["OfiMean"] = Value(_obtOfiMean), ["OfiStd"] = Value(_obtOfiStd),
            ["TiTrades"] = Value(_obtTiN), ["TiEntry"] = Value(_obtTi), ["MicropriceEntryTicks"] = Value(_obtMp),
            ["ConfirmEvents"] = Value(_obtConfirmN), ["ConfirmPasses"] = Value(_obtConfirmPass), ["MaximumSpreadTicks"] = Value(_obtSpread),
            ["MinimumWeightedDepth"] = Value(_obtDepth), ["VolatilityWindow"] = Value(_obtRvN), ["VolatilityLowTicks"] = Value(_obtRvLow), ["VolatilityHighTicks"] = Value(_obtRvHigh),
            ["ExpectedMfeTicks"] = Value(_obtMfe), ["MinimumExpectedMoveTicks"] = Value(_obtMinMove), ["MaxParticipationRate"] = Value(_obtParticipation),
            ["MaxContracts"] = Value(_obtMaxContracts), ["EntryTimeoutEvents"] = Value(_obtEntryTimeout), ["StopLossTicks"] = Value(_obtStop),
            ["BreakevenTriggerTicks"] = Value(_obtBeTrigger), ["CostBufferTicks"] = Value(_obtCostBuffer), ["TrailingDrawdownTicks"] = Value(_obtTrail),
            ["ObiExit"] = Value(_obtObiExit), ["OfiZExit"] = Value(_obtOfiExit), ["TiExit"] = Value(_obtTiExit),
            ["MaxHoldEvents"] = Value(_obtMaxHold), ["MinProgressTicks"] = Value(_obtProgress), ["PreCloseMinutes"] = Value(_obtPreClose),
            ["FreshnessSeconds"] = Value(_obtFresh), ["MaxDailyTrades"] = Value(_obtMaxTrades), ["MaxConsecutiveLosses"] = Value(_obtMaxLosses),
            ["LossCooldownMinutes"] = Value(_obtLossCooldown)
        }
    };

    private StrategyDefinition PeriodicQueueCycleDefinition(string suffix = "")
    {
        StrategyDefinition definition = _pqcControls.CreateDefinition();
        return new StrategyDefinition
        {
            StrategyType = definition.StrategyType,
            InstanceId = definition.InstanceId + suffix,
            Universe = definition.Universe,
            Parameters = definition.Parameters
        };
    }

    private void AddLive(StrategyDefinition definition, decimal commission, TextBox instance, string prefix, TextBox tradable, Label status)
    {
        try
        {
            _service.AddLive(definition, commission);
            _strategyTypesByInstance[definition.InstanceId] = definition.StrategyType;
            instance.Text = NewInstance(prefix, tradable.Text);
            status.Text = "SIMULATION ONLY／不會送出真實委託";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "無法新增策略", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private async Task ReplayAsync(StrategyDefinition definition, decimal commission, Label status)
    {
        if (_historicalReplays.TryGetValue(definition.StrategyType, out HistoricalReplayControl? existing))
        {
            if (existing.Resume()) status.Text = "歷史回測已繼續";
            else status.Text = "歷史回測執行中";
            return;
        }
        using var dialog = new OpenFileDialog { Filter = "行情事件|market-events.jsonl|JSON Lines|*.jsonl|所有檔案|*.*" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var replayControl = new HistoricalReplayControl();
        _historicalReplays[definition.StrategyType] = replayControl;
        try
        {
            _strategyTypesByInstance[definition.InstanceId] = definition.StrategyType;
            status.Text = "歷史重播執行中…";
            string output = await _service.RunBacktestAsync(dialog.FileName, definition, commission,
                replayControl: replayControl);
            _historicalBacktestOutputs[definition.StrategyType] = output;
            status.Text = $"回測完成：{output}";
        }
        catch (Exception ex)
        {
            status.Text = "SIMULATION ONLY／不會送出真實委託";
            MessageBox.Show(this, ex.Message, "回測失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            if (_historicalReplays.TryGetValue(definition.StrategyType, out HistoricalReplayControl? current) &&
                ReferenceEquals(current, replayControl))
                _historicalReplays.Remove(definition.StrategyType);
        }
    }

    private void PauseReplay(string strategyType, Label status)
    {
        if (_historicalReplays.TryGetValue(strategyType, out HistoricalReplayControl? replay) && replay.Pause())
            status.Text = "歷史回測暫停中（停在目前行情事件完成後）";
        else if (replay?.IsPaused == true)
            status.Text = "歷史回測已暫停；按啟動／繼續回測可接續進度";
        else
            status.Text = "目前沒有執行中的歷史回測";
    }

    private void ExportBacktest(string strategyType, Label status)
    {
        if (!_historicalBacktestOutputs.TryGetValue(strategyType, out string? outputDirectory))
        {
            status.Text = "尚無可匯出的回測明細，請先完成歷史回測";
            return;
        }

        string source = Path.Combine(outputDirectory, "executions.csv");
        if (!File.Exists(source))
        {
            status.Text = "找不到回測成交明細 executions.csv";
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Filter = "CSV 交易明細 (*.csv)|*.csv",
            DefaultExt = "csv",
            AddExtension = true,
            FileName = $"{Path.GetFileName(outputDirectory)}-executions.csv",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.Copy(source, dialog.FileName, overwrite: true);
            status.Text = $"成交明細已匯出：{dialog.FileName}";
        }
        catch (Exception ex)
        {
            status.Text = $"匯出失敗：{ex.Message}";
            MessageBox.Show(this, ex.Message, "匯出回測明細失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void TogglePauseSelected(DataGridView grid, Label status)
    {
        if (!TryGetSelected(grid, out string id, out DataGridViewRow row)) return;
        bool paused = Equals(row.Cells["State"].Value?.ToString(), StrategyLifecycleState.Suspended.ToString()) &&
                      (grid==rlPanel.Instances || grid==tradeFlowPanel.Instances || grid==powerReversalPanel.Instances || row.Cells["Reason"].Value?.ToString()?.StartsWith("使用者暫停", StringComparison.Ordinal) == true);
        bool changed = paused ? _service.Resume(id) : _service.Pause(id);
        status.Text = changed
            ? paused ? $"{id} 已繼續模擬" : $"{id} 已暫停；停止處理新行情"
            : $"{id} 無法{(paused ? "繼續" : "暫停")}（可能已關閉或不是即時實例）";
    }

    private void CloseSelected(DataGridView grid, Label status)
    {
        if (!TryGetSelected(grid, out string id, out _)) return;
        status.Text = _service.Stop(id) ? grid==rlPanel.Instances || grid==tradeFlowPanel.Instances || grid==powerReversalPanel.Instances ? $"{id} 已要求收尾，部位與委託歸零後才完成關閉" : $"{id} 已關閉；紀錄保留" : $"{id} 不是可關閉的即時實例";
    }

    private void DeleteSelected(DataGridView grid, DataGridView executions, Label status)
    {
        if (!TryGetSelected(grid, out string id, out DataGridViewRow row)) return;
        if ((grid==powerReversalPanel.Instances || grid==rlPanel.Instances) && !_service.Remove(id))
        {
            status.Text=$"{id} 尚未完成收尾，請先關閉並等待部位與委託歸零";
            return;
        }
        if (grid!=powerReversalPanel.Instances && grid!=rlPanel.Instances) _service.Remove(id);
        grid.Rows.Remove(row);
        for (int index = executions.Rows.Count - 1; index >= 0; index--)
        {
            if (string.Equals(executions.Rows[index].Cells["Instance"].Value?.ToString(), id, StringComparison.OrdinalIgnoreCase))
                executions.Rows.RemoveAt(index);
        }
        _strategyTypesByInstance.Remove(id);
        status.Text = $"{id} 已從畫面與執行中清單刪除；歷史行情檔未刪除";
    }

    private static bool TryGetSelected(DataGridView grid, out string instanceId, out DataGridViewRow row)
    {
        DataGridViewRow? current = grid.CurrentRow;
        instanceId = current?.Cells["Instance"].Value?.ToString() ?? string.Empty;
        row = current!;
        return current != null && instanceId.Length > 0;
    }

    private void OnSnapshot(StrategySnapshot snapshot)
    {
        if (snapshot.StrategyType == "BuySellPower") return; // Panel coalesces on its UI timer.
        if (InvokeRequired) { BeginInvoke(new Action(() => Upsert(snapshot))); return; }
        Upsert(snapshot);
    }

    private void Upsert(StrategySnapshot s)
    {
        _strategyTypesByInstance[s.InstanceId] = s.StrategyType;
        if(s.StrategyType=="NikkeiRlDirection"){rlPanel.ApplySnapshot(s);return;}
        if(s.StrategyType=="TradeFlowTrend"){tradeFlowPanel.ApplySnapshot(s);return;}
        if(s.StrategyType=="PowerReversal"){powerReversalPanel.ApplySnapshot(s);return;}
        if(s.StrategyType=="MultiFactorMeanR17"){r17Panel.ApplySnapshot(s);return;}
        DataGridView grid = s.StrategyType == MiniLeadMicroFactory.TypeName ? _mlGrid :
            s.StrategyType == PeriodicQueueCycleFactory.TypeName ? _pqcGrid :
            s.StrategyType == OrderBookTrendFactory.TypeName ? _obtGrid :
            s.StrategyType == NikkeiMicrostructureAlphaFactory.TypeName ? _naGrid :
            s.StrategyType == WaveletScalperFactory.TypeName ? _wsGrid : _mrGrid;
        DataGridViewRow? row = grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(x => Equals(x.Cells["Instance"].Value, s.InstanceId));
        if (row == null) { int index = grid.Rows.Add(); row = grid.Rows[index]; }
        object[] values = { s.InstanceId, s.StrategyType, s.ParameterSummary, s.TradableProductId, s.TradableSymbol, s.ReferenceSummary,
            s.State, s.Reason, s.LastPrice, s.CenterPrice, s.Range, s.BuyEntry, s.SellEntry, s.StopPrice,
            s.Position, s.RealizedPnl, s.UnrealizedPnl, s.NetPnl, s.CommissionPerContract, s.CompletedTrades, s.UpdatedAt.ToString("HH:mm:ss.fff") };
        for (int i = 0; i < values.Length; i++) row.Cells[i].Value = values[i];
    }

    private void OnExecution(ExecutionEvent e)
    {
        if (InvokeRequired) { BeginInvoke(new Action(() => AddExecution(e))); return; }
        AddExecution(e);
    }

    private void AddExecution(ExecutionEvent e)
    {
        if(_strategyTypesByInstance.GetValueOrDefault(e.StrategyInstanceId)=="NikkeiRlDirection")return;
        if(_strategyTypesByInstance.GetValueOrDefault(e.StrategyInstanceId)=="TradeFlowTrend"){tradeFlowPanel.AddExecution(e);return;}
        if(_strategyTypesByInstance.GetValueOrDefault(e.StrategyInstanceId)=="PowerReversal"){powerReversalPanel.AddExecution(e);return;}
        if(_strategyTypesByInstance.GetValueOrDefault(e.StrategyInstanceId)=="MultiFactorMeanR17"){r17Panel.AddExecution(e);return;}
        DataGridView grid = _strategyTypesByInstance.TryGetValue(e.StrategyInstanceId, out string? type) && type == MiniLeadMicroFactory.TypeName
            ? _mlExecutions : type == PeriodicQueueCycleFactory.TypeName ? _pqcExecutions : type == OrderBookTrendFactory.TypeName ? _obtExecutions : type == NikkeiMicrostructureAlphaFactory.TypeName ? _naExecutions : type == WaveletScalperFactory.TypeName ? _wsExecutions : _mrExecutions;
        grid.Rows.Insert(0, e.Timestamp.ToString("HH:mm:ss.fff"), e.StrategyInstanceId, e.Symbol,
            e.Side, e.Role, e.Price, e.Quantity, e.TriggerMarketPrice, e.Commission, e.MarketSequence);
        while (grid.Rows.Count > 2000) grid.Rows.RemoveAt(grid.Rows.Count - 1);
    }

    private static string Value(NumericUpDown control) => control.Value.ToString(CultureInfo.InvariantCulture);
    private static string NewInstance(string prefix, string tradable) => $"{prefix}-{tradable.Trim()}-{DateTime.Now:HHmmss}";
}
