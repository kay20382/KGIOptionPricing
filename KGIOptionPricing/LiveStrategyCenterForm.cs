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
public sealed class LiveStrategyCenterForm : Form
{
    private readonly LiveStrategyService _service;
    private readonly DataGridView _mrGrid = Grid(), _mrFills = Grid(), _wsGrid = Grid(), _wsFills = Grid(), _naGrid = Grid(), _naFills = Grid(), _mlGrid = Grid(), _mlFills = Grid(), _obtGrid = Grid(), _obtFills = Grid(), _pqcGrid = Grid(), _pqcFills = Grid();
    private readonly Label _mrStatus = Status(), _wsStatus = Status(), _naStatus = Status(), _mlStatus = Status(), _obtStatus = Status(), _pqcStatus = Status();
    private readonly Dictionary<string, string> _types = new(StringComparer.OrdinalIgnoreCase);
    private readonly TextBox _mrId = Input("LIVE-MR-225MC"), _mrProduct = Input("225MC", 72), _mrRefs = Input("225,225M", 100);
    private readonly NumericUpDown _mrH = Num(150, 10, 86400), _mrF = Num(.8m, .01m, 4, 2), _mrK = Num(1.35m, .02m, 8, 2),
        _mrMinR = Num(4, 1, 100000), _mrSpread = Num(1, 1, 1000), _mrFresh = Num(2, 1, 60), _mrFee = Num(36, 0, 100000, 2), _mrLoss = Num(2000, 1, 10000000);
    private readonly CheckBox _mrConfirm = Confirm();
    private readonly TextBox _wsId = Input("LIVE-WS-225MC"), _wsProduct = Input("225MC", 72);
    private readonly NumericUpDown _wsWindow = Num(64, 8, 10000), _wsSigma = Num(2, .1m, 10, 2), _wsObi = Num(.6m, .01m, .99m, 2),
        _wsStop = Num(3, 1, 1000), _wsEnergy = Num(1.5m, 1.01m, 10, 2), _wsSpread = Num(1, 1, 1000), _wsFresh = Num(2, 1, 60),
        _wsHold = Num(10, 1, 300), _wsCooldown = Num(2, 0, 300), _wsFee = Num(36, 0, 100000, 2), _wsLoss = Num(2000, 1, 10000000);
    private readonly CheckBox _wsConfirm = Confirm();
    private readonly TextBox _naId = Input("LIVE-NA-225MC"), _naProduct = Input("225MC", 72), _naRefs = Input("225M,225", 100);
    private readonly NumericUpDown _naWarm = Num(60, 1, 3600), _naFlow = Num(30, 1, 300), _naVol = Num(300, 10, 3600),
        _naAlpha = Num(.70m, .10m, .99m, 2), _naContinue = Num(.65m, .05m, .99m, 2), _naDepth = Num(10, 1, 1000000),
        _naSpread = Num(1, 1, 2), _naMove = Num(5, 1, 100), _naEdge = Num(100, 0, 1000000), _naStop = Num(2, 1, 100),
        _naTarget = Num(6, 1, 100), _naRr = Num(1, .1m, 10, 2), _naStopSlip = Num(1, 0, 20, 1), _naWait = Num(1500, 100, 10000),
        _naHold = Num(180, 1, 3600), _naFresh = Num(2, 1, 60), _naCost = Num(72, 0, 1000000, 2), _naCooldown = Num(5, 0, 3600),
        _naFee = Num(36, 0, 100000, 2), _naLoss = Num(2000, 1, 10000000);
    private readonly CheckBox _naConfirm = Confirm();
    private readonly TextBox _mlId = Input("LIVE-ML-225MC"), _mlProduct = Input("225MC", 72), _mlMode = Input("DelayedResponse", 112);
    private readonly NumericUpDown _mlWindow = Num(1000, 50, 60000), _mlLeader = Num(3, .5m, 100, 1), _mlPath = Num(.65m, 0, 1, 2),
        _mlIntensity = Num(2.5m, 0, 100, 2), _mlLargeWindow = Num(1500, 50, 60000), _mlLargeMove = Num(2, .5m, 100, 1),
        _mlLargePath = Num(.5m, 0, 1, 2), _mlLargeTimeout = Num(500, 0, 10000), _mlDispersion = Num(2, 0, 100, 1), _mlCatch = Num(.5m, -2, 2, 2),
        _mlGap = Num(1, 0, 100, 1), _mlExpected = Num(4, 0, 100, 1), _mlExpectedNet = Num(50, -100000, 100000), _mlWin = Num(.55m, 0, 1, 2),
        _mlQuoteAge = Num(250, 1, 60000), _mlSkew = Num(100, 0, 60000), _mlSpread = Num(1, .5m, 100, 1), _mlWarm = Num(300, 1, 86400),
        _mlProfit = Num(5, 1, 1000), _mlStop = Num(2, 1, 1000), _mlLeaderRev = Num(2, 1, 1000), _mlLargeRev = Num(2, 1, 1000),
        _mlHold = Num(5000, 100, 3600000), _mlCooldown = Num(10000, 0, 3600000), _mlMaxTrades = Num(20, 1, 10000), _mlMaxLosses = Num(3, 1, 1000),
        _mlFee = Num(36, 0, 100000, 2), _mlLoss = Num(1500, 1, 10000000);
    private readonly CheckBox _mlConfirm = Confirm();
    private readonly CheckBox _mlValidated = new() { Text = "已通過規格上線門檻", AutoSize = false, Width = 180, Height = 28, ForeColor = Color.DarkRed, Margin = new Padding(3) };
    private readonly TextBox _obtId = Input("LIVE-OBT-225MC"), _obtProduct = Input("225MC", 72);
    private readonly NumericUpDown _obtMomN = Num(120, 2, 10000), _obtMom = Num(3, .1m, 100, 1), _obtObi = Num(.22m, .01m, .99m, 2),
        _obtOfiN = Num(100, 2, 10000), _obtOfiZ = Num(1.25m, .01m, 20, 2), _obtOfiMean = Num(0, -1000000, 1000000, 2), _obtOfiStd = Num(100, .0001m, 1000000, 2),
        _obtTiN = Num(50, 2, 10000), _obtTi = Num(.18m, .01m, .99m, 2), _obtMp = Num(.20m, .01m, 1, 2), _obtConfirmN = Num(5, 1, 100), _obtConfirmPass = Num(4, 1, 100),
        _obtSpread = Num(1, 1, 100), _obtDepth = Num(20, 0, 1000000), _obtRvN = Num(100, 2, 10000), _obtRvLow = Num(.05m, 0, 100, 2), _obtRvHigh = Num(5, .01m, 1000, 2),
        _obtMfe = Num(4.5m, 0, 100, 1), _obtMinMove = Num(4.5m, 0, 100, 1), _obtParticipation = Num(.10m, .01m, 1, 2), _obtEntryTimeout = Num(10, 1, 10000),
        _obtStop = Num(4, 1, 100, 1), _obtBeTrigger = Num(4, .1m, 100, 1), _obtCostBuffer = Num(1.5m, 0, 100, 1), _obtTrail = Num(3, .1m, 100, 1),
        _obtObiExit = Num(-.05m, -.99m, .99m, 2), _obtOfiExit = Num(-.25m, -20, 20, 2), _obtTiExit = Num(-.05m, -.99m, .99m, 2),
        _obtMaxHold = Num(1200, 1, 1000000), _obtProgress = Num(3, 0, 100, 1), _obtPreClose = Num(10, 1, 60), _obtFresh = Num(2, 1, 60),
        _obtMaxTrades = Num(20, 1, 10000), _obtMaxLosses = Num(3, 1, 100), _obtLossCooldown = Num(15, 1, 1440), _obtFee = Num(36, 0, 100000, 2), _obtLoss = Num(2000, 1, 10000000);
    private readonly CheckBox _obtConfirm = Confirm();
    private readonly PeriodicQueueCycleControls _pqcControls = new(live: true);
    private readonly NumericUpDown _pqcLoss = Num(2000, 1, 10000000);
    private readonly CheckBox _pqcConfirm = Confirm();

    public LiveStrategyCenterForm(LiveStrategyService service)
    {
        _service = service;
        Text = "⚠ 實盤策略中心－真實帳戶委託"; Width = 1600; Height = 900; BackColor = Color.MistyRose;
        _mrId.Text = NewId("LIVE-MR", _mrProduct.Text); _wsId.Text = NewId("LIVE-WS", _wsProduct.Text); _naId.Text = NewId("LIVE-NA", _naProduct.Text); _mlId.Text = NewId("LIVE-ML", _mlProduct.Text); _obtId.Text = NewId("LIVE-OBT", _obtProduct.Text); _pqcControls.InstanceId.Text = NewId("LIVE-PQC", _pqcControls.ProductId.Text);
        BuildGrids(); BuildLayout();
        _service.SnapshotChanged += Snapshot; _service.ExecutionOccurred += Fill;
        foreach (var snapshot in _service.GetSnapshots()) Upsert(snapshot);
        FormClosed += (_, _) => { _service.SnapshotChanged -= Snapshot; _service.ExecutionOccurred -= Fill; };
    }

    private void BuildLayout()
    {
        var banner = new Label { Dock = DockStyle.Top, Height = 58, BackColor = Color.DarkRed, ForeColor = Color.White,
            Font = new Font(Font.FontFamily, 13, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter,
            Text = $"⚠ 實盤交易區／帳號 {_service.Account}：成交會產生真實損益；每個策略固定最多 1 口" };
        var tabs = new TabControl { Dock = DockStyle.Fill }; tabs.TabPages.Add(MeanReversionTab()); tabs.TabPages.Add(WaveletTab()); tabs.TabPages.Add(NikkeiAlphaTab()); tabs.TabPages.Add(MiniLeadTab()); tabs.TabPages.Add(OrderBookTrendTab()); tabs.TabPages.Add(PeriodicQueueCycleTab());
        Controls.Add(tabs); Controls.Add(banner);
    }

    private TabPage MeanReversionTab()
    {
        var page = Page("實盤策略一：超短週期均值回歸"); var p = Settings();
        AddButtons(p, _mrGrid, _mrFills, _mrStatus, _mrConfirm, () => MeanReversionDefinition(), _mrFee, _mrLoss, _mrId, "LIVE-MR", _mrProduct);
        Add(p, "實例", _mrId); Add(p, "交易商品", _mrProduct); Add(p, "參考商品", _mrRefs); Add(p, "H 秒", _mrH); Add(p, "F", _mrF); Add(p, "K", _mrK);
        Add(p, "最小 R ticks", _mrMinR); Add(p, "最大價差 ticks", _mrSpread); Add(p, "行情新鮮秒", _mrFresh); Add(p, "每口每次費(日圓)", _mrFee); Add(p, "單日最大虧損", _mrLoss);
        p.Controls.Add(LockedQuantity()); page.Controls.Add(Monitor(_mrGrid, _mrFills, _mrStatus)); page.Controls.Add(p); return page;
    }

    private TabPage WaveletTab()
    {
        var page = Page("實盤策略二：MODWT 剝頭皮"); var p = Settings();
        AddButtons(p, _wsGrid, _wsFills, _wsStatus, _wsConfirm, () => WaveletDefinition(), _wsFee, _wsLoss, _wsId, "LIVE-WS", _wsProduct);
        Add(p, "實例", _wsId); Add(p, "交易商品", _wsProduct); Add(p, "MODWT 窗 ticks", _wsWindow); Add(p, "進場 σ", _wsSigma); Add(p, "OBI 門檻", _wsObi);
        Add(p, "停損 ticks", _wsStop); Add(p, "D1 能量倍數", _wsEnergy); Add(p, "最大價差 ticks", _wsSpread); Add(p, "行情新鮮秒", _wsFresh);
        Add(p, "最長持倉秒", _wsHold); Add(p, "冷卻秒", _wsCooldown); Add(p, "每口每次費(日圓)", _wsFee); Add(p, "單日最大虧損", _wsLoss);
        p.Controls.Add(LockedQuantity()); page.Controls.Add(Monitor(_wsGrid, _wsFills, _wsStatus)); page.Controls.Add(p); return page;
    }

    private TabPage NikkeiAlphaTab()
    {
        var page = Page("實盤策略三：OSE 微型日經 Alpha"); var p = Settings(154);
        AddButtons(p, _naGrid, _naFills, _naStatus, _naConfirm, () => NikkeiAlphaDefinition(), _naFee, _naLoss, _naId, "LIVE-NA", _naProduct);
        Add(p, "實例", _naId); Add(p, "交易商品", _naProduct); Add(p, "參考商品", _naRefs); Add(p, "暖機秒", _naWarm); Add(p, "成交流窗秒", _naFlow); Add(p, "波動窗秒", _naVol);
        Add(p, "進場 Alpha", _naAlpha); Add(p, "續單 Alpha", _naContinue); Add(p, "最低五檔量", _naDepth); Add(p, "最大價差 ticks", _naSpread); Add(p, "最低行情 ticks", _naMove); Add(p, "最低淨邊際", _naEdge);
        Add(p, "基準停損 ticks", _naStop); Add(p, "基準停利 ticks", _naTarget); Add(p, "最低 RR", _naRr); Add(p, "停損滑價 ticks", _naStopSlip); Add(p, "掛單等待 ms", _naWait); Add(p, "最長持倉秒", _naHold);
        Add(p, "行情新鮮秒", _naFresh); Add(p, "來回成本(日圓)", _naCost); Add(p, "冷卻秒", _naCooldown); Add(p, "每口每次費(日圓)", _naFee); Add(p, "單日最大虧損", _naLoss);
        p.Controls.Add(LockedQuantity()); page.Controls.Add(Monitor(_naGrid, _naFills, _naStatus)); page.Controls.Add(p); return page;
    }

    private TabPage MiniLeadTab()
    {
        var page = Page("實盤策略四：Mini 領先 Micro"); var p = Settings(192);
        AddButtons(p, _mlGrid, _mlFills, _mlStatus, _mlConfirm, () => MiniLeadDefinition(), _mlFee, _mlLoss, _mlId, "LIVE-ML", _mlProduct);
        Add(p, "實例", _mlId); Add(p, "交易商品", _mlProduct); Add(p, "模式", _mlMode); Add(p, "mini 窗 ms", _mlWindow); Add(p, "mini 衝擊 t", _mlLeader);
        Add(p, "路徑效率", _mlPath); Add(p, "成交強度", _mlIntensity); Add(p, "大型窗 ms", _mlLargeWindow); Add(p, "大型確認 t", _mlLargeMove); Add(p, "大型效率", _mlLargePath);
        Add(p, "確認期限 ms", _mlLargeTimeout); Add(p, "最大離散 t", _mlDispersion); Add(p, "最大跟進比", _mlCatch); Add(p, "可成交差 t", _mlGap); Add(p, "預期空間 t", _mlExpected);
        Add(p, "預期淨值 ¥", _mlExpectedNet); Add(p, "最低勝率", _mlWin); Add(p, "報價時效 ms", _mlQuoteAge); Add(p, "跨商品差 ms", _mlSkew); Add(p, "micro 價差 t", _mlSpread);
        Add(p, "暖機秒", _mlWarm); Add(p, "停利 t", _mlProfit); Add(p, "停損 t", _mlStop); Add(p, "mini 反轉 t", _mlLeaderRev); Add(p, "大型反轉 t", _mlLargeRev);
        Add(p, "最長持有 ms", _mlHold); Add(p, "冷卻 ms", _mlCooldown); Add(p, "最多交易", _mlMaxTrades); Add(p, "連虧上限", _mlMaxLosses); Add(p, "每口每次費", _mlFee); Add(p, "單日最大虧損", _mlLoss);
        p.Controls.Add(_mlValidated); p.Controls.Add(LockedQuantity()); page.Controls.Add(Monitor(_mlGrid, _mlFills, _mlStatus)); page.Controls.Add(p); return page;
    }

    private TabPage OrderBookTrendTab()
    {
        var page = Page("實盤策略五：五檔順勢"); var p = Settings(230);
        AddButtons(p, _obtGrid, _obtFills, _obtStatus, _obtConfirm, () => OrderBookTrendDefinition(), _obtFee, _obtLoss, _obtId, "LIVE-OBT", _obtProduct);
        Add(p, "實例", _obtId); Add(p, "交易商品", _obtProduct); Add(p, "動能事件", _obtMomN); Add(p, "動能門檻 t", _obtMom); Add(p, "OBI 門檻", _obtObi);
        Add(p, "OFI 事件", _obtOfiN); Add(p, "OFI z 門檻", _obtOfiZ); Add(p, "OFI 前日均值", _obtOfiMean); Add(p, "OFI 前日標準差", _obtOfiStd);
        Add(p, "TI 成交筆", _obtTiN); Add(p, "TI 門檻", _obtTi); Add(p, "微價格 t", _obtMp); Add(p, "確認事件", _obtConfirmN); Add(p, "確認通過", _obtConfirmPass);
        Add(p, "最大價差 t", _obtSpread); Add(p, "最低加權深度", _obtDepth); Add(p, "波動事件", _obtRvN); Add(p, "波動下限 t", _obtRvLow); Add(p, "波動上限 t", _obtRvHigh);
        Add(p, "校準 MFE t", _obtMfe); Add(p, "最低預期 t", _obtMinMove); Add(p, "參與率", _obtParticipation); Add(p, "進場逾時事件", _obtEntryTimeout);
        Add(p, "停損 t", _obtStop); Add(p, "保本觸發 t", _obtBeTrigger); Add(p, "成本緩衝 t", _obtCostBuffer); Add(p, "移動回撤 t", _obtTrail);
        Add(p, "OBI 出場", _obtObiExit); Add(p, "OFI z 出場", _obtOfiExit); Add(p, "TI 出場", _obtTiExit); Add(p, "最長事件", _obtMaxHold); Add(p, "最低進展 t", _obtProgress);
        Add(p, "收盤前分鐘", _obtPreClose); Add(p, "行情新鮮秒", _obtFresh); Add(p, "單日最多交易", _obtMaxTrades); Add(p, "連虧上限", _obtMaxLosses); Add(p, "連虧冷卻分鐘", _obtLossCooldown);
        Add(p, "每口每次費", _obtFee); Add(p, "單日最大虧損", _obtLoss); p.Controls.Add(LockedQuantity());
        page.Controls.Add(Monitor(_obtGrid, _obtFills, _obtStatus)); page.Controls.Add(p); return page;
    }

    private TabPage PeriodicQueueCycleTab()
    {
        var page = Page("實盤策略六：週期雙邊完成單");
        var actions = Settings(42);
        AddButtons(actions, _pqcGrid, _pqcFills, _pqcStatus, _pqcConfirm, () => _pqcControls.CreateDefinition(),
            _pqcControls.Commission, _pqcLoss, _pqcControls.InstanceId, "LIVE-PQC", _pqcControls.ProductId);
        Add(actions, "單日最大虧損", _pqcLoss);
        actions.Controls.Add(LockedQuantity());
        page.Controls.Add(Monitor(_pqcGrid, _pqcFills, _pqcStatus));
        page.Controls.Add(_pqcControls);
        page.Controls.Add(actions);
        return page;
    }

    private void AddButtons(FlowLayoutPanel p, DataGridView grid, DataGridView fills, Label status, CheckBox confirm,
        Func<StrategyDefinition> definition, NumericUpDown fee, NumericUpDown loss, TextBox id, string prefix, TextBox product)
    {
        var start = Button("啟動實盤", 96); start.Click += (_, _) => Start(definition(), fee.Value, loss.Value, confirm, id, prefix, product, status);
        var pause = Button("暫停／繼續", 96); pause.Click += (_, _) => Toggle(grid, status);
        var close = Button("關閉／平倉", 100); close.Click += (_, _) => Close(grid, status);
        var reconcile = Button("人工同步刪單", 118); reconcile.Click += (_, _) => ReconcileExternalCancellation(grid, status);
        var delete = Button("刪除所選", 88); delete.Click += (_, _) => Delete(grid, fills, status);
        p.Controls.Add(start); p.Controls.Add(pause); p.Controls.Add(close); p.Controls.Add(reconcile); p.Controls.Add(delete); p.Controls.Add(confirm);
    }

    private StrategyDefinition MeanReversionDefinition() => new()
    {
        StrategyType = MeanReversionScalperFactory.TypeName, InstanceId = _mrId.Text.Trim(),
        Universe = new StrategyUniverse { Tradable = new ProductSelection(_mrProduct.Text.Trim()), References = _mrRefs.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => new ProductSelection(x)).ToArray() },
        Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["HorizonSeconds"] = V(_mrH), ["F"] = V(_mrF), ["K"] = V(_mrK), ["MinimumRangeTicks"] = V(_mrMinR), ["MaximumSpreadTicks"] = V(_mrSpread), ["FreshnessSeconds"] = V(_mrFresh) }
    };

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

    private void Start(StrategyDefinition definition, decimal fee, decimal loss, CheckBox confirm, TextBox id, string prefix, TextBox product, Label status)
    {
        if (!confirm.Checked) { Warn("請先勾選「我確認這是真實帳戶委託」。"); return; }
        if (!_service.IsLoggedIn) { Warn("交易服務尚未登入，禁止啟動實盤策略。"); return; }
        string prompt = $"帳號：{_service.Account}\n策略：{definition.StrategyType}\n實例：{definition.InstanceId}\n商品：{definition.Universe.Tradable.ProductId}\n口數：1（鎖定）\n每次成交費用：¥{fee}\n單日最大虧損：¥{loss}\n\n確定啟動真實委託？";
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

    private void Close(DataGridView grid, Label status)
    {
        if (!Selected(grid, out string id, out _)) return; var s = _service.GetCloseStatus(id); if (!s.Exists) return;
        bool flatten = s.Position != 0;
        string prompt = flatten
            ? $"{id} 尚有實盤部位 {s.Position} 口。\n市場有效委託 {s.ActiveBrokerOrders} 筆，本機待觸發 {s.LocalConditions} 筆。\n\n將先撤單，確認無委託後送市價平倉；平倉成交後才關閉。是否繼續？"
            : $"{id} 部位為 0。\n市場有效委託 {s.ActiveBrokerOrders} 筆，本機待觸發 {s.LocalConditions} 筆。\n\n將先撤單，確認無委託後才關閉；撤單競速若成交會自動平倉。是否繼續？";
        if (MessageBox.Show(this, prompt, "確認關閉實盤策略", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        _service.RequestClose(id, flatten, out string message); status.Text = $"{id}：{message}";
    }

    private void ReconcileExternalCancellation(DataGridView grid, Label status)
    {
        if (!Selected(grid, out string id, out _)) return;
        LiveStrategyCloseStatus close = _service.GetCloseStatus(id);
        if (!close.Exists) return;
        if (!close.ClosePending)
        {
            Warn("請先按「關閉／平倉」，停止新訊號並啟動受控關閉流程。");
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
            + "系統會把本機委託標記為人工核對取消，之後才可送出既有部位的平倉單；仍保留 10 分鐘 late-fill 觀察期。";
        if (MessageBox.Show(this, prompt, "最後確認：人工同步券商刪單", MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        bool applied = _service.ReconcileExternallyCancelledOrders(id, out string message);
        status.Text = $"{id}：{message}";
        if (!applied) Warn(message);
    }

    private void Delete(DataGridView grid, DataGridView fills, Label status)
    {
        if (!Selected(grid, out string id, out var row)) return; var s = _service.GetCloseStatus(id);
        if (s.Position != 0 || s.ActiveBrokerOrders != 0 || s.LocalConditions != 0 || row.Cells["State"].Value?.ToString() != StrategyLifecycleState.Stopped.ToString()) { Warn("只能刪除已關閉、無市場委託、無待觸發條件且部位為 0 的策略。請先按「關閉／平倉」。"); return; }
        if (!_service.Remove(id)) { Warn("策略尚未符合安全刪除條件。"); return; }
        grid.Rows.Remove(row); for (int i = fills.Rows.Count - 1; i >= 0; i--) if (Equals(fills.Rows[i].Cells["Instance"].Value, id)) fills.Rows.RemoveAt(i);
        _types.Remove(id); status.Text = $"{id} 已刪除";
    }

    private void Snapshot(StrategySnapshot s) { if (InvokeRequired) BeginInvoke(new Action(() => Upsert(s))); else Upsert(s); }
    private void Upsert(StrategySnapshot s)
    {
        _types[s.InstanceId] = s.StrategyType; var grid = s.StrategyType == PeriodicQueueCycleFactory.TypeName ? _pqcGrid : s.StrategyType == OrderBookTrendFactory.TypeName ? _obtGrid : s.StrategyType == MiniLeadMicroFactory.TypeName ? _mlGrid : s.StrategyType == NikkeiMicrostructureAlphaFactory.TypeName ? _naGrid : s.StrategyType == WaveletScalperFactory.TypeName ? _wsGrid : _mrGrid;
        var row = grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(x => Equals(x.Cells["Instance"].Value, s.InstanceId)); if (row == null) row = grid.Rows[grid.Rows.Add()];
        var close = _service.GetCloseStatus(s.InstanceId);
        object[] values = { s.InstanceId, s.StrategyRunGeneration, s.RecoveryState, s.RecoveryReason, s.OcoStatusSummary, s.StrategyType, s.ParameterSummary, s.TradableProductId, s.TradableSymbol, s.ReferenceSummary, _service.Account, s.State,
            close.ClosePending ? "關閉中／" + s.Reason : s.Reason, close.ActiveBrokerOrders, close.LocalConditions, s.Position, s.LastPrice, s.RealizedPnl, s.UnrealizedPnl, s.NetPnl, s.CommissionPerContract, s.CompletedTrades, s.UpdatedAt.ToString("HH:mm:ss.fff") };
        for (int i = 0; i < values.Length; i++) row.Cells[i].Value = values[i]; row.DefaultCellStyle.BackColor = close.ClosePending ? Color.LemonChiffon : Color.White;
    }

    private void Fill(ExecutionEvent e) { if (InvokeRequired) BeginInvoke(new Action(() => AddFill(e))); else AddFill(e); }
    private void AddFill(ExecutionEvent e)
    {
        var grid = _types.TryGetValue(e.StrategyInstanceId, out string? type) && type == PeriodicQueueCycleFactory.TypeName ? _pqcFills : type == OrderBookTrendFactory.TypeName ? _obtFills : type == MiniLeadMicroFactory.TypeName ? _mlFills : type == NikkeiMicrostructureAlphaFactory.TypeName ? _naFills : type == WaveletScalperFactory.TypeName ? _wsFills : _mrFills;
        grid.Rows.Insert(0, e.Timestamp.ToString("HH:mm:ss.fff"), e.StrategyInstanceId, e.Symbol, e.Side, e.Role, e.Price, e.Quantity, e.TriggerMarketPrice, e.Commission, e.MarketSequence);
        while (grid.Rows.Count > 2000) grid.Rows.RemoveAt(grid.Rows.Count - 1);
    }

    private void BuildGrids()
    {
        foreach (var g in new[] { _mrGrid, _wsGrid, _naGrid, _mlGrid, _obtGrid, _pqcGrid }) { Col(g,"Instance","實例",170); Col(g,"RunGeneration","執行世代",80); Col(g,"RecoveryState","回補狀態",105); Col(g,"RecoveryReason","回補／隔離原因",240); Col(g,"OcoStatus","OCO 狀態／原因",320); Col(g,"Strategy","策略",170); Col(g,"Parameters","實際參數",520); Col(g,"Product","商品",72); Col(g,"Symbol","合約",120); Col(g,"References","參考商品",150); Col(g,"Account","實盤帳號",115); Col(g,"State","狀態",90); Col(g,"Reason","原因／風控",300); Col(g,"BrokerOrders","市場委託",75); Col(g,"LocalOrders","待觸發",65); Col(g,"Position","部位",55); Col(g,"Last","成交價",75); Col(g,"Realized","已實現",85); Col(g,"Unrealized","未實現",85); Col(g,"Net","淨損益",85); Col(g,"Fee","每口費用",75); Col(g,"Trades","完成交易",70); Col(g,"Updated","更新時間",105); }
        foreach (var g in new[] { _mrFills, _wsFills, _naFills, _mlFills, _obtFills, _pqcFills }) { Col(g,"Time","時間",105); Col(g,"Instance","實例",170); Col(g,"Symbol","合約",125); Col(g,"Side","買賣",65); Col(g,"Role","角色",105); Col(g,"Price","真實成交價",95); Col(g,"Qty","口數",55); Col(g,"Trigger","觸發行情",85); Col(g,"Cost","費用",75); Col(g,"MarketSequence","行情序號",95); }
    }

    private static Panel Monitor(DataGridView strategies, DataGridView fills, Label status) { var p = new Panel { Dock = DockStyle.Fill }; var tabs = new TabControl { Dock = DockStyle.Fill }; var a = Page("實盤策略狀態"); a.Controls.Add(strategies); var b = Page("真實成交紀錄"); b.Controls.Add(fills); tabs.TabPages.Add(a); tabs.TabPages.Add(b); p.Controls.Add(tabs); p.Controls.Add(status); return p; }
    private static TabPage Page(string text) => new(text) { BackColor = Color.MistyRose };
    private static FlowLayoutPanel Settings(int height = 116) => new() { Dock = DockStyle.Top, Height = height, Padding = new Padding(8), AutoScroll = true, WrapContents = true };
    private static DataGridView Grid() => new() { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None, BackgroundColor = Color.White, GridColor = Color.LightCoral };
    private static Label Status() => new() { Dock = DockStyle.Bottom, Height = 30, AutoSize = false, Padding = new Padding(8, 6, 0, 0), BackColor = Color.FromArgb(255,225,225), ForeColor = Color.DarkRed, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold), Text = "⚠ REAL TRADING／等待啟動" };
    private static CheckBox Confirm() => new() { Text = "我確認這是真實帳戶委託", AutoSize = false, Width = 205, Height = 28, ForeColor = Color.DarkRed, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold), Margin = new Padding(8,3,10,3) };
    private static Button Button(string text, int width) => new() { Text = text, Width = width, Height = 30, AutoSize = false, BackColor = Color.DarkRed, ForeColor = Color.White, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold), Margin = new Padding(3,3,8,3) };
    private static TextBox Input(string text, int width = 165) => new() { Text = text, Width = width, Height = 24, AutoSize = false };
    // NumericUpDown starts with Maximum=100. Set bounds before Value so values such as H=150
    // do not fail during form construction.
    private static NumericUpDown Num(decimal value, decimal min, decimal max, int decimals = 0) => new() { Minimum = min, Maximum = max, DecimalPlaces = decimals, Value = Math.Clamp(value, min, max), Width = 76, Height = 24 };
    private static Label LockedQuantity() => new() { Text = "下單口數：1（鎖定）", Width = 150, Height = 24, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.DarkRed, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold), Margin = new Padding(3,5,8,3) };
    private static void Add(FlowLayoutPanel p, string label, Control c) { p.Controls.Add(new Label { Text=label, Width=104, Height=24, AutoSize=false, TextAlign=ContentAlignment.MiddleRight, Margin=new Padding(3,5,2,3) }); c.Margin=new Padding(0,3,8,3); p.Controls.Add(c); }
    private static void Col(DataGridView g, string name, string title, int width) { int i=g.Columns.Add(name,title); g.Columns[i].Width=width; }
    private static bool Selected(DataGridView g, out string id, out DataGridViewRow row) { row=g.CurrentRow!; id=row?.Cells["Instance"].Value?.ToString() ?? ""; return row != null && id.Length>0; }
    private void Warn(string text) => MessageBox.Show(this,text,"實盤策略中心",MessageBoxButtons.OK,MessageBoxIcon.Warning);
    private static string V(NumericUpDown c) => c.Value.ToString(CultureInfo.InvariantCulture);
    private static string NewId(string prefix, string product) => $"{prefix}-{product.Trim()}-{DateTime.Now:HHmmss}";
}
