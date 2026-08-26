using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using KGIOptionPricing.OrderManagement.Core;

namespace KGIOptionPricing
{
    /// <summary>
    /// 系統主控制台：負責中央服務生命週期管理、API 登入狀態監控、即時日誌以及獨立浮動子視窗的調度。
    /// </summary>
    public partial class Form1 : Form
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(typeof(Form1));

        // Core Services
        private KGIQuoteService _quoteService;
        private IMPVStorageManager _storageManager;
        private MarketDataService _marketDataService;
        private TradingCalendar _calendar;
        private KGITradeService _tradeService;
        private OrderManager _orderManager;
        private SpreadConfigEvaluator _spreadEvaluator;
        private System.Windows.Forms.Timer _riskDataTimer;
        private string _riskDataOutputPath = "C:\\temp\\RiskData.json";
        private Dictionary<string, OptionChain> _currentChains = new Dictionary<string, OptionChain>();

        // Floating Subforms (Singleton Instances)
        private OptionPricingForm? _pricingForm;
        private OrderPlacementForm? _orderPlacementForm;
        private OrderReportForm? _orderReportForm;
        private DealReportForm? _dealReportForm;
        private PositionForm? _positionForm;
        private MarginEquityForm? _marginEquityForm;

        public Form1()
        {
            log.Info("Form Init");
            InitializeComponent();
            InitializeServices();
        }

        private void InitializeServices()
        {
            _calendar = new TradingCalendar();
            log.Info("QuoteService Init");
            _quoteService = new KGIQuoteService();
            _quoteService.SetCalendar(_calendar);

            log.Info("IMPV Object Init");
            _storageManager = new IMPVStorageManager();

            log.Info("MarketDataService Init");
            _marketDataService = new MarketDataService(_quoteService, _storageManager);

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
            string userId = "A128905009";
            string password = "Itim3482";
            string brokerID = "F004000";
            string account = "9819113";
            int intervalSec = 15;
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
            _quoteService.Connect();
            _marketDataService.Start();
            _tradeService.Start();
            _riskDataTimer.Start();
        }

        private void Form1_FormClosing(object? sender, FormClosingEventArgs e)
        {
            log.Info("[Form1] Application closing, stopping services...");
            try { _riskDataTimer?.Stop(); _riskDataTimer?.Dispose(); } catch { }
            try { _marketDataService?.Stop(); } catch { }
            try { _tradeService?.Stop(); } catch { }
            try { _quoteService?.Disconnect(); } catch { }

            // 關閉所有子視窗
            try { _pricingForm?.Close(); } catch { }
            try { _orderPlacementForm?.Close(); } catch { }
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