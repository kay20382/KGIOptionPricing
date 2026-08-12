using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using ScottPlot;
using ScottPlot.WinForms;
using Color = System.Drawing.Color;
using Font = System.Drawing.Font;
using Point = System.Drawing.Point;
using Size = System.Drawing.Size;
using Label = System.Windows.Forms.Label;
using FontStyle = System.Drawing.FontStyle;

namespace KGIOptionPricing
{
    public partial class Form1 : Form
    {
        private System.Windows.Forms.Timer _autoSaveTimer;
        private bool _hasSavedMorning = false;
        private bool _hasSavedNight = false;
        private bool _isFirstLoad = true;

        private KGIQuoteService _quoteService;
        private IMPVStorageManager _storageManager;
        private MarketDataService _marketDataService;
        private TradingCalendar _calendar;
        private KGITradeService _tradeService;
        private System.Windows.Forms.Timer _riskDataTimer;
        private SpreadConfigEvaluator _spreadEvaluator;
        private string _riskDataOutputPath = "C:\\temp\\RiskData.json";

        private Dictionary<string, OptionChain> _currentChains;

        public Form1()
        {
            InitializeComponent();
            ApplyCustomStyles();
            InitializeServices();
        }
        private void Form1_Load_1(object sender, EventArgs e)
        {

        }
        private void ApplyCustomStyles()
        {
            // Advanced Font and Style configurations
            this.Font = new Font("Segoe UI", 10F, FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            _dgvTQuote.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            _dgvTQuote.DefaultCellStyle.Font = new Font("Consolas", 10F);
            colStrike.DefaultCellStyle.Font = new Font("Consolas", 10F, FontStyle.Bold);
            colStrike.DefaultCellStyle.BackColor = Color.FromArgb(0, 0, 100); // Dark Blue
            colStrike.DefaultCellStyle.ForeColor = Color.Gold;

            // Load Enums for IV Model
            foreach (var method in Enum.GetValues(typeof(SmoothingMethod)))
            {
                _cboModel.Items.Add(method.ToString());
            }
            if (_cboModel.Items.Count > 0)
                _cboModel.SelectedItem = SmoothingMethod.CubicSpline.ToString(); // Default to CubicSpline

            // Wire UI Event
            _chkShift.CheckedChanged += (s, e) => UpdateChart();

            // Configure Dark Theme for the ScottPlot
            _formsPlot.Plot.FigureBackground.Color = ScottPlot.Color.FromHex("#121212");
            _formsPlot.Plot.DataBackground.Color = ScottPlot.Color.FromHex("#121212");
            _formsPlot.Plot.Axes.Color(ScottPlot.Color.FromHex("#d7d7d7"));
            _formsPlot.Plot.Grid.LineColor = ScottPlot.Color.FromHex("#2d2d2d");
            _formsPlot.Plot.Title("Implied Volatility Curve");
            _formsPlot.Plot.XLabel("Strike Price");
            _formsPlot.Plot.YLabel("Implied Volatility (%)");
        }


        private void InitializeServices()
        {
            _calendar = new TradingCalendar();
            _quoteService = new KGIQuoteService();
            _storageManager = new IMPVStorageManager();
            _marketDataService = new MarketDataService(_quoteService, _storageManager);

            _quoteService.SetCalendar(_calendar);

            _quoteService.OnOptionsDataUpdated += (chains) =>
            {
                _currentChains = chains;
                if (this.IsHandleCreated)
                {
                    this.Invoke(new Action(() =>
                    {
                        if (_isFirstLoad && _currentChains.Values.Any(c => c.UnderlyingPrice > 0))
                        {
                            _storageManager.LoadLatestSnapshot(_currentChains.Values);
                            _isFirstLoad = false;
                        }

                        bool cboChanged = false;
                        foreach (var key in chains.Keys)
                        {
                            if (!_cboExpiration.Items.Contains(key))
                            {
                                _cboExpiration.Items.Add(key);
                                cboChanged = true;
                            }
                        }

                        if (cboChanged && _cboExpiration.Items.Count > 0 && _cboExpiration.SelectedIndex == -1)
                        {
                            _cboExpiration.SelectedIndex = 0;
                        }

                        UpdateChart();
                    }));
                }
            };

            _quoteService.OnLogMessage += (msg) =>
            {
                if (this.IsHandleCreated)
                {
                    this.BeginInvoke(new Action(() => { _lblStatus.Text = msg; }));
                }
            };

            _autoSaveTimer = new System.Windows.Forms.Timer();
            _autoSaveTimer.Interval = 10*60*1000;
            _autoSaveTimer.Tick += AutoSaveTimer_Tick;

            // Initialize TradeCom Service & Risk Exporter from Config
            InitializeTradeService();

            this.Load += Form1_Load;
            this.FormClosing += Form1_FormClosing;
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
                if (System.IO.File.Exists("config.json"))
                {
                    string json = System.IO.File.ReadAllText("config.json");
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("KGI_USER_ID", out var idElem)) userId = idElem.GetString() ?? userId;
                    if (root.TryGetProperty("KGI_PASSWORD", out var pwdElem)) password = pwdElem.GetString() ?? password;
                    if (root.TryGetProperty("TradeCom_Host", out var hostElem)) host = hostElem.GetString() ?? host;
                    if (root.TryGetProperty("TradeCom_BrokerID", out var brokerIDElem)) host = hostElem.GetString() ?? brokerID;
                    if (root.TryGetProperty("TradeCom_Account", out var accountElem)) host = hostElem.GetString() ?? account;
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
            _tradeService = new KGITradeService(host, port, userId, password,brokerID,account);
            _tradeService.OnStatusChanged += (statusText, isConn) =>
            {
                if (this.IsHandleCreated)
                {
                    this.BeginInvoke(new Action(() =>
                    {
                        _lblTradeStatus.Text = $"[TradeCom: {statusText}]";
                        _lblTradeStatus.ForeColor = isConn ? Color.LimeGreen : Color.Orange;
                    }));
                }
            };

            // 2. 收到 INVENTORY_TRADER (P001616) 並解析完成後，自動執行風險計算與匯出
            _tradeService.OnPositionsUpdated += (positions) =>
            {
                if (this.IsHandleCreated)
                {
                    this.BeginInvoke(new Action(() =>
                    {
                        ExportRiskData(positions);
                    }));
                }
            };

            _riskDataTimer = new System.Windows.Forms.Timer();
            _riskDataTimer.Interval = Math.Max(1000, intervalSec * 1000);
            _riskDataTimer.Tick += (s, e) => 
            {
                if (_tradeService != null && _tradeService.IsLoggedIn)
                {
                    // 1. 定時向 TradeServer 查詢部位 (發送 RetrivePositionSum)
                    _tradeService.RequestPositions();
                }
                else
                {
                    // 若尚未登入完成，則使用現有持倉重新導出風控資料
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

                // 3. 配合 3-Tier 隱含波動率 (IV) 判定機制與 currentChains 計算
                // 4. 寫入 RiskData JSON 檔案
                RiskDataExporter.ExportToJsonFile(_riskDataOutputPath, positionsToExport, _currentChains, futuresPrice, _spreadEvaluator);
            }
        }

        private void Form1_Load(object? sender, EventArgs e)
        {
            _quoteService.Connect();
            _marketDataService.Start();
            _autoSaveTimer.Start();
            _tradeService.Start();
            _riskDataTimer.Start();
        }

        private void Form1_FormClosing(object? sender, FormClosingEventArgs e)
        {
            try { _autoSaveTimer?.Stop(); } catch { }
            try { _riskDataTimer?.Stop(); } catch { }
            try { _marketDataService?.Stop(); } catch { }
            try { _tradeService?.Stop(); } catch { }
            try { _quoteService?.Disconnect(); } catch { }
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            if (_currentChains != null && _currentChains.Values.Count > 0)
            {
                try
                {
                    _storageManager.SaveSnapshot(_currentChains.Values);
                    _lblStatus.Text = $"Status: Saved at {DateTime.Now:HH:mm:ss}";
                }
                catch { }
            }
        }


        private void _chkAutoSaveMorning_CheckedChanged(object? sender, EventArgs e)
        {
            _dtpAutoSaveMorning.Enabled = !_chkAutoSaveMorning.Checked;
            if (_chkAutoSaveMorning.Checked == true && DateTime.Now.TimeOfDay < _dtpAutoSaveMorning.Value.TimeOfDay.Subtract(TimeSpan.FromMinutes(1)))
                _hasSavedMorning = false;
        }

        private void _dtpAutoSaveMorning_ValueChanged(object? sender, EventArgs e)
        {
            if (DateTime.Now.TimeOfDay < _dtpAutoSaveMorning.Value.TimeOfDay.Subtract(TimeSpan.FromMinutes(1)))
                _hasSavedMorning = false;
        }

        private void _chkAutoSaveNight_CheckedChanged(object? sender, EventArgs e)
        {
            _dtpAutoSaveNight.Enabled = !_chkAutoSaveNight.Checked;
            if (_chkAutoSaveNight.Checked == true && DateTime.Now.TimeOfDay < _dtpAutoSaveNight.Value.TimeOfDay.Subtract(TimeSpan.FromMinutes(1)))
                _hasSavedNight = false;
        }

        private void _dtpAutoSaveNight_ValueChanged(object? sender, EventArgs e)
        {
            if (DateTime.Now.TimeOfDay < _dtpAutoSaveNight.Value.TimeOfDay.Subtract(TimeSpan.FromMinutes(1)))
                _hasSavedNight = false;
        }

        private void AutoSaveTimer_Tick(object sender, EventArgs e)
        {
            // Morning Check
            if (DateTime.Now.TimeOfDay < _dtpAutoSaveMorning.Value.TimeOfDay.Subtract(TimeSpan.FromMinutes(1)))
            {
                _hasSavedMorning = false;
            }

            if (_chkAutoSaveMorning.Checked && !_hasSavedMorning)
            {
                if (DateTime.Now.TimeOfDay >= _dtpAutoSaveMorning.Value.TimeOfDay)
                {
                    BtnSave_Click(this, EventArgs.Empty);
                    _hasSavedMorning = true;
                }
            }

            // Night Check
            //if (DateTime.Now.TimeOfDay < _dtpAutoSaveNight.Value.TimeOfDay.Subtract(TimeSpan.FromMinutes(1)))
            //{
            //    _hasSavedNight = false;
            //}

            //if (_chkAutoSaveNight.Checked && !_hasSavedNight)
            //{
            //    if (DateTime.Now.TimeOfDay >= _dtpAutoSaveNight.Value.TimeOfDay)
            //    {
            //        BtnSave_Click(this, EventArgs.Empty);
            //        _hasSavedNight = true;
            //    }
            //}
        }

        private void CboExpiration_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateChart();
        }

        private void CboModel_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_cboModel.SelectedItem != null && _currentChains != null)
            {
                if (Enum.TryParse(_cboModel.SelectedItem.ToString(), out SmoothingMethod selectedMethod))
                {
                    foreach (var chain in _currentChains.Values)
                    {
                        chain.VolatilityCurve.Method = selectedMethod;
                    }
                    UpdateChart();
                }
            }
        }

        private void UpdateChart()
        {
            if (_cboExpiration.SelectedItem == null || _currentChains == null) return;

            string selectedKey = _cboExpiration.SelectedItem.ToString();

            if (_currentChains.TryGetValue(selectedKey, out OptionChain chain))
            {
                _formsPlot.Plot.Clear();

                // Display Futures Price in Title
                _formsPlot.Plot.Title($"Implied Volatility Curve (Futures Price: {chain.UnderlyingPrice:F0})");

                // Draw a vertical line for the Futures Price
                var vline = _formsPlot.Plot.Add.VerticalLine(chain.UnderlyingPrice);
                vline.Color = ScottPlot.Color.FromHex("#FFD700"); // Gold
                vline.LinePattern = LinePattern.Dashed;
                vline.LineWidth = 2;

                var allOptions = chain.Calls.Concat(chain.Puts)
                                      .Where(o => o.ImpliedVolatility > 0 && o.ImpliedVolatility <= 2.0)
                                      .OrderBy(o => o.StrikePrice)
                                      .ToList();

                if (allOptions.Count == 0) return;

                // Scatter Plot for Raw IMPV (Use OTM Option for Volatility: Call for K >= S, Put for K <= S)
                var rawData = allOptions.GroupBy(o => o.StrikePrice)
                                        .Select(g => new
                                        {
                                            K = g.Key,
                                            V = g.Where(o => (o.IsCall && o.StrikePrice >= chain.UnderlyingPrice) || (!o.IsCall && o.StrikePrice <= chain.UnderlyingPrice))
                                                 .Select(o => o.ImpliedVolatility)
                                                 .DefaultIfEmpty(g.Average(o => o.ImpliedVolatility))
                                                 .Average()
                                        })
                                        .ToList();

                double[] strikes = rawData.Select(x => x.K).ToArray();
                double[] impvs = rawData.Select(x => x.V).ToArray();

                var scatter = _formsPlot.Plot.Add.ScatterPoints(strikes, impvs);
                scatter.MarkerSize = 8;
                scatter.Color = ScottPlot.Color.FromHex("#00BFFF"); // DeepSkyBlue

                // Smoothed Curve Line (using VolatilityCurve object)
                double minStrike = strikes.Min();
                double maxStrike = strikes.Max();
                int curvePoints = 100;
                double[] curveX = new double[curvePoints];
                double[] curveY = new double[curvePoints];

                for (int i = 0; i < curvePoints; i++)
                {
                    double k = minStrike + i * (maxStrike - minStrike) / (curvePoints - 1);
                    curveX[i] = k;
                    curveY[i] = chain.VolatilityCurve.GetSmoothedVolatility(k);
                }

                var line = _formsPlot.Plot.Add.ScatterLine(curveX, curveY);
                line.LineWidth = 3;
                line.Color = ScottPlot.Color.FromHex("#FF6347"); // Tomato Red
                line.LinePattern = LinePattern.Solid;

                // Yesterday's Curve
                var yestOptions = allOptions.Where(o => o.PreviousCloseIMPV > 0).ToList();
                if (yestOptions.Count > 0)
                {
                    double ivShift = 0;
                    if (_chkShift.Checked)
                    {
                        var atmOption = yestOptions.OrderBy(o => Math.Abs(o.StrikePrice - chain.UnderlyingPrice)).FirstOrDefault();
                        if (atmOption != null)
                        {
                            ivShift = atmOption.SmoothedIMPV - atmOption.PreviousCloseIMPV;
                        }
                    }

                    // Group by strike to avoid duplicate X values (Call/Put at same strike)
                    var yestData = yestOptions.GroupBy(o => o.StrikePrice)
                                              .Select(g => new
                                              {
                                                  K = g.Key,
                                                  V = Math.Max(0.001, g.Average(o => o.PreviousCloseIMPV) + ivShift)
                                              })
                                              .OrderBy(x => x.K)
                                              .ToList();

                    double[] yestX = yestData.Select(x => x.K).ToArray();
                    double[] yestY = yestData.Select(x => x.V).ToArray();

                    var yestLine = _formsPlot.Plot.Add.ScatterLine(yestX, yestY);
                    yestLine.LineWidth = 2;
                    yestLine.Color = ScottPlot.Color.FromHex("#AAAAAA"); // Light Gray
                    yestLine.LinePattern = LinePattern.Dashed;
                }

                ScottPlot.TickGenerators.NumericAutomatic tickGenY = new ScottPlot.TickGenerators.NumericAutomatic();
                tickGenY.LabelFormatter = x => x.ToString("P2");
                _formsPlot.Plot.Axes.Left.TickGenerator = tickGenY;

                _formsPlot.Plot.Axes.AutoScale();
                _formsPlot.Refresh();

                // Update T-Quote DataGridView
                UpdateTQuote(chain);
            }
        }

        private void UpdateTQuote(OptionChain chain)
        {
            var strikes = chain.Calls.Select(c => c.StrikePrice)
                                     .Union(chain.Puts.Select(p => p.StrikePrice))
                                     .OrderBy(s => s)
                                     .ToList();

            // We only show strikes that have some quote data to avoid massive empty tables
            var validStrikes = strikes.Where(k =>
                (chain.Calls.FirstOrDefault(c => c.StrikePrice == k)?.HasLastPrice ?? false) ||
                (chain.Puts.FirstOrDefault(p => p.StrikePrice == k)?.HasLastPrice ?? false)
            ).ToList();

            if (validStrikes.Count == 0) validStrikes = strikes; // Fallback if no quotes

            // Save scroll position
            int scrollIndex = _dgvTQuote.FirstDisplayedScrollingRowIndex;

            // Suspend layout to prevent flickering
            _dgvTQuote.SuspendLayout();

            // Match row count
            if (_dgvTQuote.Rows.Count != validStrikes.Count)
            {
                _dgvTQuote.Rows.Clear();
                if (validStrikes.Count > 0)
                {
                    _dgvTQuote.Rows.Add(validStrikes.Count);
                }
            }

            double underlying = chain.UnderlyingPrice;
            double atmStrike = validStrikes.OrderBy(k => Math.Abs(k - underlying)).FirstOrDefault();

            for (int i = 0; i < validStrikes.Count; i++)
            {
                double k = validStrikes[i];
                var call = chain.Calls.FirstOrDefault(c => c.StrikePrice == k);
                var put = chain.Puts.FirstOrDefault(p => p.StrikePrice == k);

                double otmIV = (k >= chain.UnderlyingPrice) ?
                    (call?.ImpliedVolatility ?? 0) :
                    (put?.ImpliedVolatility ?? 0);
                string ivString = otmIV > 0 ? otmIV.ToString("P2") : "-";

                var row = _dgvTQuote.Rows[i];

                // Call side
                row.Cells[0].Value = call?.Delta.ToString("F2") ?? "-";
                row.Cells[1].Value = ivString;
                row.Cells[2].Value = (call?.TheoreticalBid > 0) ? call.TheoreticalBid.ToString("F1") : "-";
                row.Cells[3].Value = (call?.TheoreticalAsk > 0) ? call.TheoreticalAsk.ToString("F1") : "-";
                row.Cells[4].Value = (call?.LastPrice > 0) ? call.LastPrice.ToString("F1") : "-";

                // Strike
                row.Cells[5].Value = k.ToString("F0");

                // Put side
                row.Cells[6].Value = (put?.LastPrice > 0) ? put.LastPrice.ToString("F1") : "-";
                row.Cells[7].Value = (put?.TheoreticalBid > 0) ? put.TheoreticalBid.ToString("F1") : "-";
                row.Cells[8].Value = (put?.TheoreticalAsk > 0) ? put.TheoreticalAsk.ToString("F1") : "-";
                row.Cells[9].Value = ivString;
                row.Cells[10].Value = put?.Delta.ToString("F2") ?? "-";

                // Highlight ATM row and Strike column
                if (k == atmStrike)
                {
                    row.DefaultCellStyle.BackColor = Color.FromArgb(0, 100, 0); // Dark Green for ATM
                    row.DefaultCellStyle.ForeColor = Color.White;
                    row.Cells[5].Style.BackColor = Color.FromArgb(0, 100, 0);
                }
                else
                {
                    row.DefaultCellStyle.BackColor = Color.FromArgb(18, 18, 18);
                    row.DefaultCellStyle.ForeColor = Color.FromArgb(220, 220, 220);
                    row.Cells[5].Style.BackColor = Color.FromArgb(0, 0, 100); // Dark Blue for non-ATM strike
                }
            }

            _dgvTQuote.ResumeLayout();

            // Restore scroll position
            if (scrollIndex >= 0 && scrollIndex < _dgvTQuote.Rows.Count)
            {
                try
                {
                    _dgvTQuote.FirstDisplayedScrollingRowIndex = scrollIndex;
                }
                catch { }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            //if (_currentChains != null && _currentChains.Values.Count > 0)
            //{
            //    try
            //    {
            //        _storageManager.SaveSnapshot(_currentChains.Values);
            //    }
            //    catch { } // Prevent crash during close if file is locked
            //}

            _marketDataService.Stop();
            _quoteService.Disconnect();
            base.OnFormClosing(e);
        }

   
    }
}