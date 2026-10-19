using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using KGIOptionPricing.OrderManagement.Core;
using KGIOptionPricing.ProductSpreads;
using KGIOptionPricing.Strategies;
using KGIOptionPricing.StrategyFramework;

namespace KGIOptionPricing
{
    /// <summary>
    /// 系統主控制台：負責中央服務生命週期管理、API 登入狀態監控、即時日誌以及獨立浮動子視窗的調度。
    /// </summary>
    public partial class Form1 : Form
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(typeof(Form1));

        // Core Services
        // These services are initialized synchronously by InitializeServices()
        // before the form can be used. The null-forgiving initializers document
        // that lifecycle for nullable reference analysis.
        private KGIQuoteService _quoteService = null!;
        private IMPVStorageManager _storageManager = null!;
        private MarketDataService _marketDataService = null!;
        private TradingCalendar _calendar = null!;
        private KGITradeService _tradeService = null!;
        private OrderManager _orderManager = null!;
        private QueueFollowStrategyEngine _queueFollowEngine = null!;
        private SpreadConfigEvaluator _spreadEvaluator = null!;
        private ProductSpreadService _productSpreadService = null!;
        private Research.NikkeiResearchRecorder _researchRecorder = null!;
        private StrategySimulationService _strategySimulation = null!;
        private LiveStrategyService _liveStrategyService = null!;
        private System.Windows.Forms.Timer _riskDataTimer = null!;
        private string _riskDataOutputPath = "C:\\temp\\RiskData.json";
        private Dictionary<string, OptionChain> _currentChains = new Dictionary<string, OptionChain>();

        // Floating Subforms (Singleton Instances)
        private OptionPricingForm? _pricingForm;
        private OrderPlacementForm? _orderPlacementForm;
        private readonly List<LightningOrderForm> _lightningOrderForms = new();
        private OrderReportForm? _orderReportForm;
        private DealReportForm? _dealReportForm;
        private PositionForm? _positionForm;
        private MarginEquityForm? _marginEquityForm;
        private QueueFollowStrategyForm? _queueFollowForm;
        private StrategyCenterForm? _strategyCenterForm;
        private LiveStrategyCenterForm? _liveStrategyCenterForm;

        public Form1()
        {
            InitializeComponent();
            if (System.ComponentModel.LicenseManager.UsageMode == System.ComponentModel.LicenseUsageMode.Designtime)
                return;
            log.Info("Form Init");
            InitializeServices();
        }

        private void InitializeServices()
        {
            _calendar = new TradingCalendar();
            log.Info("QuoteService Init");
            _quoteService = new KGIQuoteService();
            _quoteService.SetCalendar(_calendar);
            _researchRecorder = new Research.NikkeiResearchRecorder(_quoteService,
                Path.Combine(AppContext.BaseDirectory, "NikkeiResearch"), message =>
                {
                    log.Info(message);
                    AppendLog(message, Color.LightSkyBlue);
                });
            _strategySimulation = new StrategySimulationService(
                _quoteService,
                Path.Combine(AppContext.BaseDirectory, "StrategySimulation"));

            log.Info("IMPV Object Init");
            _storageManager = new IMPVStorageManager();

            log.Info("MarketDataService Init");
            _marketDataService = new MarketDataService(_quoteService, _storageManager);

            string spreadConfigPath = File.Exists("config.json")
                ? Path.GetFullPath("config.json")
                : Path.Combine(AppContext.BaseDirectory, "config.json");
            IReadOnlyList<ProductSpreadDefinition> productSpreads;
            try
            {
                productSpreads = ProductSpreadService.LoadDefinitions(spreadConfigPath);
            }
            catch (Exception ex)
            {
                productSpreads = Array.Empty<ProductSpreadDefinition>();
                log.Error($"[價差設定錯誤] {ex.Message}", ex);
            }
            _productSpreadService = new ProductSpreadService(
                _quoteService,
                productSpreads,
                Path.GetDirectoryName(spreadConfigPath) ?? AppContext.BaseDirectory,
                message =>
                {
                    log.Info(message);
                    AppendLog(message, message.Contains("錯誤") ? Color.OrangeRed : Color.MediumPurple);
                });

            // 行情日誌與狀態事件
            _quoteService.OnLogMessage += (msg) =>
            {
                AppendLog($"[Quote API] {msg}", Color.LightSkyBlue);
                if (msg.Contains("連線成功") || msg.Contains("就緒") || msg.Contains("Login Success"))
                {
                    UpdateQuoteStatus("🟢 已連線就緒", Color.LimeGreen);
                }
                else if (msg.Contains("失敗") || msg.Contains("斷線") || msg.Contains("中斷"))
                {
                    UpdateQuoteStatus("🔴 連線中斷", Color.Crimson);
                }
            };

            _quoteService.OnOptionsDataUpdated += (chains) =>
            {
                _currentChains = chains;
                if (chains.Values.Any(c => c.UnderlyingPrice > 0))
                {
                    double futPrice = chains.Values.First(c => c.UnderlyingPrice > 0).UnderlyingPrice;
                    UpdateFuturesPrice(futPrice);
                }
            };

            // TradeCom Service & Risk Exporter from Config
            log.Info("TradeService Init");
            InitializeTradeService();

            this.Load += Form1_Load;
            this.FormClosing += Form1_FormClosing;
            log.Info("Initialize Finish");
        }

        private void InitializeTradeService()
        {
            string host = "itrade.kgi.com.tw";
            ushort port = 8000;
            string userId = "";
            string password = "";
            string brokerID = "F004000";
            string account = "";
            int intervalSec = 15;
            string queueFollowHistoryPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "KGIOptionPricing",
                "StrategyHistory");
            List<SpreadRule>? spreadRules = null;

            try
            {
                if (File.Exists("config.json"))
                {
                    string json = File.ReadAllText("config.json");
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    _calendar.LoadConfigFromJson(root);

                    if (root.TryGetProperty("KGI_USER_ID", out var idElem)) userId = idElem.GetString() ?? userId;
                    if (root.TryGetProperty("KGI_PASSWORD", out var pwdElem)) password = pwdElem.GetString() ?? password;
                    if (root.TryGetProperty("TradeCom_Host", out var hostElem)) host = hostElem.GetString() ?? host;
                    if (root.TryGetProperty("TradeCom_BrokerID", out var brokerIDElem)) brokerID = brokerIDElem.GetString() ?? brokerID;
                    if (root.TryGetProperty("TradeCom_Account", out var accountElem)) account = accountElem.GetString() ?? account;
                    if (root.TryGetProperty("TradeCom_Port", out var portElem)) port = (ushort)portElem.GetInt32();
                    if (root.TryGetProperty("RiskDataOutputPath", out var pathElem)) _riskDataOutputPath = pathElem.GetString() ?? _riskDataOutputPath;
                    if (root.TryGetProperty("AutoExportIntervalSeconds", out var intervalElem)) intervalSec = intervalElem.GetInt32();
                    if (root.TryGetProperty("QueueFollowHistoryPath", out var historyPathElem) && !string.IsNullOrWhiteSpace(historyPathElem.GetString()))
                        queueFollowHistoryPath = historyPathElem.GetString()!;

                    if (root.TryGetProperty("spreadconfig", out var spreadElem))
                    {
                        spreadRules = System.Text.Json.JsonSerializer.Deserialize<List<SpreadRule>>(spreadElem.GetRawText());
                    }
                }
            }
            catch { }

            _spreadEvaluator = new SpreadConfigEvaluator(spreadRules);
            _tradeService = new KGITradeService(host, port, userId, password, brokerID, account);
            _orderManager = new OrderManager(_tradeService);
            _liveStrategyService = new LiveStrategyService(_quoteService, _orderManager, _tradeService);
            var queueFollowHistory = new JsonLineQueueFollowHistorySink(
                queueFollowHistoryPath,
                message => log.Error(message));
            _queueFollowEngine = new QueueFollowStrategyEngine(
                new KgiQueueFollowMarketDataSource(_quoteService),
                new OrderManagerQueueFollowOrderService(_orderManager, _tradeService),
                new KgiQueueFollowTickProvider(),
                queueFollowHistory);
            log.Info($"Queue-follow strategy history: {queueFollowHistory.DirectoryPath}");

            _tradeService.OnLogMessage += (msg) =>
            {
                AppendLog($"[Trade API] {msg}", Color.LightYellow);
            };

            _tradeService.OnStatusChanged += (statusText, isConn) =>
            {
                UpdateTradeStatus(isConn ? $"🟢 {statusText}" : $"🔴 {statusText}", isConn ? Color.LimeGreen : Color.Crimson);
            };

            _tradeService.OnPositionsUpdated += (positions) =>
            {
                ExportRiskData(positions);
            };

            _riskDataTimer = new System.Windows.Forms.Timer
            {
                Interval = Math.Max(1000, intervalSec * 1000)
            };
            _riskDataTimer.Tick += (s, e) =>
            {
                if (_tradeService != null && _tradeService.IsLoggedIn)
                {
                    _tradeService.RequestPositions("I");
                }
                else
                {
                    ExportRiskData();
                }
            };
        }

        private void ExportRiskData(List<TradePositionItem>? positions = null)
        {
            if (_tradeService != null)
            {
                double futuresPrice = _marketDataService != null ? _marketDataService.CurrentFuturesPrice : 21000.0;
                var positionsToExport = positions ?? _tradeService.CurrentPositions;

                RiskDataExporter.ExportToJsonFile(_riskDataOutputPath, positionsToExport, _currentChains, futuresPrice, _spreadEvaluator);

                if (IsHandleCreated && !IsDisposed)
                {
                    BeginInvoke(new Action(() =>
                    {
                        lblRiskDataStatus.Text = $"已匯出 ({DateTime.Now:HH:mm:ss})";
                        lblRiskDataStatus.ForeColor = Color.LimeGreen;
                    }));
                }
            }
        }

        private void Form1_Load(object? sender, EventArgs e)
        {
            AppendLog($"[系統啟動] KGIOptionPricing 核心管理員啟動中... ({DateTime.Now:yyyy/MM/dd HH:mm:ss})", Color.Gold);
            _productSpreadService.Start();
            _researchRecorder.Start();
            _quoteService.Connect();
            _marketDataService.Start();
            _tradeService.Start();
            _riskDataTimer.Start();
        }

        private void Form1_FormClosing(object? sender, FormClosingEventArgs e)
        {
            IReadOnlyList<string> liveRisks = _liveStrategyService?.GetShutdownWarnings() ?? Array.Empty<string>();
            if (liveRisks.Count > 0)
            {
                e.Cancel = true;
                MessageBox.Show(this,
                    "偵測到實盤策略仍有市場委託或部位，為避免失去成交回報監控，禁止直接關閉程式：\n\n" +
                    string.Join("\n", liveRisks) + "\n\n請至實盤策略中心按「關閉／平倉」，等待市場委託與部位皆為 0。",
                    "實盤風控：禁止關閉", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                OpenLiveStrategyCenterForm();
                return;
            }
            log.Info("[Form1] Application closing, stopping services...");
            try { _researchRecorder?.Dispose(); } catch (Exception ex) { log.Error("研究記錄停止失敗", ex); }
            try { _riskDataTimer?.Stop(); _riskDataTimer?.Dispose(); } catch { }
            try { _queueFollowEngine?.StopAll(); } catch { }
            try { _queueFollowForm?.Close(); } catch { }
            try { _queueFollowEngine?.Dispose(); } catch { }
            try { _strategyCenterForm?.Close(); } catch { }
            try { _strategySimulation?.Dispose(); } catch (Exception ex) { log.Error("模擬策略服務停止失敗", ex); }
            try { _liveStrategyCenterForm?.Close(); } catch { }
            try { _liveStrategyService?.Dispose(); } catch (Exception ex) { log.Error("實盤策略服務停止失敗", ex); }
            try { _productSpreadService?.Dispose(); } catch (Exception ex) { log.Error("[價差] 停止服務失敗", ex); }
            try { _marketDataService?.Stop(); } catch { }
            try { _tradeService?.Stop(); } catch { }
            try { _quoteService?.Disconnect(); } catch { }

            // 關閉所有子視窗
            try { _pricingForm?.Close(); } catch { }
            try { _orderPlacementForm?.Close(); } catch { }
            foreach (var lightningForm in _lightningOrderForms.ToArray())
            {
                try { lightningForm.Close(); } catch { }
            }
            _lightningOrderForms.Clear();
            try { _orderReportForm?.Close(); } catch { }
            try { _dealReportForm?.Close(); } catch { }
            try { _positionForm?.Close(); } catch { }
            try { _marginEquityForm?.Close(); } catch { }

            log4net.LogManager.Shutdown();
            Environment.Exit(0);
        }

        #region UI State & Logging

        private void UpdateQuoteStatus(string text, Color color)
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke(new Action(() =>
            {
                lblQuoteStatus.Text = text;
                lblQuoteStatus.ForeColor = color;
            }));
        }

        private void UpdateTradeStatus(string text, Color color)
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke(new Action(() =>
            {
                lblTradeStatus.Text = text;
                lblTradeStatus.ForeColor = color;
            }));
        }

        private void UpdateFuturesPrice(double price)
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke(new Action(() =>
            {
                lblFuturesPrice.Text = price > 0 ? $"{price:N0}" : "--";
            }));
        }

        private void AppendLog(string message, Color? color = null)
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke(new Action(() =>
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                string logLine = $"[{timestamp}] {message}\n";

                rtbLog.SelectionStart = rtbLog.TextLength;
                rtbLog.SelectionLength = 0;
                rtbLog.SelectionColor = color ?? Color.LightGray;
                rtbLog.AppendText(logLine);
                rtbLog.SelectionColor = rtbLog.ForeColor;

                if (chkAutoScroll.Checked)
                {
                    rtbLog.ScrollToCaret();
                }
            }));
        }

        private void btnClearLog_Click(object? sender, EventArgs e)
        {
            rtbLog.Clear();
        }

        #endregion

        #region Floating Subforms Dispatcher (Option B - 獨立浮動視窗)

        private void ShowOrActivateForm<T>(ref T? formInstance, Func<T> factory) where T : Form
        {
            if (formInstance == null || formInstance.IsDisposed)
            {
                formInstance = factory();
                formInstance.Show(this); // 浮動顯示
            }
            else
            {
                if (formInstance.WindowState == FormWindowState.Minimized)
                {
                    formInstance.WindowState = FormWindowState.Normal;
                }
                formInstance.Activate();
                formInstance.BringToFront();
            }
        }

        public void OpenOrderPlacementForm()
        {
            ShowOrActivateForm(ref _orderPlacementForm, () => new OrderPlacementForm(_orderManager, _tradeService, _marketDataService, this));
        }

        public void OpenLightningOrderForm()
        {
            var form = new LightningOrderForm(_orderManager, _tradeService, _marketDataService);
            _lightningOrderForms.Add(form);
            form.FormClosed += (_, _) => _lightningOrderForms.Remove(form);
            form.Show(this);
        }

        public void OpenQueueFollowStrategyForm()
        {
            ShowOrActivateForm(ref _queueFollowForm, () => new QueueFollowStrategyForm(_queueFollowEngine, _tradeService, _quoteService));
        }

        private void tsmQueueFollow_Click(object? sender, EventArgs e) => OpenQueueFollowStrategyForm();

        public void OpenStrategyCenterForm()
        {
            ShowOrActivateForm(ref _strategyCenterForm, () => new StrategyCenterForm(_strategySimulation));
        }

        public void OpenLiveStrategyCenterForm()
        {
            ShowOrActivateForm(ref _liveStrategyCenterForm, () => new LiveStrategyCenterForm(_liveStrategyService, _strategySimulation));
        }

        private void tsmSimulation_Click(object? sender, EventArgs e) => OpenStrategyCenterForm();

        private void tsmLiveStrategy_Click(object? sender, EventArgs e) => OpenLiveStrategyCenterForm();

        public void OpenOrderReportForm()
        {
            ShowOrActivateForm(ref _orderReportForm, () => new OrderReportForm(_orderManager, _tradeService));
        }

        public void OpenOrderPlacementWithOption(string symbol, string? month = null, string? callPut = null, double? strike = null, BuySell? bs = null, double? price = null)
        {
            OpenOrderPlacementForm();
            _orderPlacementForm?.SetDomesticOption(symbol, month, callPut, strike, bs, price);
        }

        public void OpenOrderPlacementWithFuture(string prod = "TXF", string? month = null, BuySell? bs = null, double? price = null)
        {
            OpenOrderPlacementForm();
            _orderPlacementForm?.SetDomesticFuture(prod, month, bs, price);
        }

        public void OpenOrderPlacementWithForeign(string exchange, string symbol, string month, BuySell? bs = null, double? price = null)
        {
            OpenOrderPlacementForm();
            _orderPlacementForm?.SetForeignFuture(exchange, symbol, month, bs, price);
        }

        private void btnTsPricing_Click(object? sender, EventArgs e)
        {
            ShowOrActivateForm(ref _pricingForm, () => new OptionPricingForm(_quoteService, _storageManager, _marketDataService, _calendar));
        }

        private void btnTsOrderPlacement_Click(object? sender, EventArgs e)
        {
            OpenOrderPlacementForm();
        }

        private void btnTsLightningOrder_Click(object? sender, EventArgs e)
        {
            OpenLightningOrderForm();
        }

        private void btnTsOrderReport_Click(object? sender, EventArgs e)
        {
            OpenOrderReportForm();
        }

        private void btnTsDealReport_Click(object? sender, EventArgs e)
        {
            ShowOrActivateForm(ref _dealReportForm, () => new DealReportForm(_orderManager, _tradeService));
        }

        private void btnTsPosition_Click(object? sender, EventArgs e)
        {
            ShowOrActivateForm(ref _positionForm, () => new PositionForm(_tradeService, _orderManager, _marketDataService));
        }

        private void btnTsMargin_Click(object? sender, EventArgs e)
        {
            ShowOrActivateForm(ref _marginEquityForm, () => new MarginEquityForm(_tradeService));
        }

        private void tsmExit_Click(object? sender, EventArgs e)
        {
            Close();
        }

        #endregion
    }
}
