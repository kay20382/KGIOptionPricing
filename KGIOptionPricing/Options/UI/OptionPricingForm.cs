using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
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
    public partial class OptionPricingForm : Form
    {
        private readonly KGIQuoteService _quoteService = null!;
        private readonly IMPVStorageManager _storageManager = null!;
        private readonly MarketDataService _marketDataService = null!;
        private readonly TradingCalendar _calendar = null!;
        private readonly System.Windows.Forms.Timer _autoSaveTimer = null!;

        private bool _hasSavedMorning = false;
        private bool _isFirstLoad = true;
        private int _quoteDisplayMode = 10;
        private Dictionary<string, OptionChain> _currentChains = new Dictionary<string, OptionChain>();

        public OptionPricingForm() => InitializeComponent();

        public OptionPricingForm(KGIQuoteService quoteService, IMPVStorageManager storageManager, MarketDataService marketDataService, TradingCalendar calendar) : this()
        {
            _quoteService = quoteService ?? throw new ArgumentNullException(nameof(quoteService));
            _storageManager = storageManager ?? throw new ArgumentNullException(nameof(storageManager));
            _marketDataService = marketDataService;
            _calendar = calendar;

            ApplyCustomStyles();

            _quoteService.OnOptionsDataUpdated += QuoteService_OnOptionsDataUpdated;
            _quoteService.OnLogMessage += QuoteService_OnLogMessage;
            _quoteService.OnSymbolRepositoryLoaded += QuoteService_OnSymbolRepositoryLoaded;
            _currentChains = _quoteService.OptionChains;
            UpdateOptionSubscriptionButton();

            _autoSaveTimer = new System.Windows.Forms.Timer { Interval = 10 * 60 * 1000 };
            _autoSaveTimer.Tick += AutoSaveTimer_Tick;
            _autoSaveTimer.Start();

            this.FormClosing += OptionPricingForm_FormClosing;
            this.Shown += (_, _) =>
            {
                UpdateOptionSubscriptionButton();
                if (_quoteService.IsOptionPricingSubscriptionStarted && _currentChains.Count > 0)
                    QuoteService_OnOptionsDataUpdated(_currentChains);
            };
        }

        private void OptionPricingForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            _autoSaveTimer.Stop();
            _autoSaveTimer.Dispose();
            _quoteService.OnOptionsDataUpdated -= QuoteService_OnOptionsDataUpdated;
            _quoteService.OnLogMessage -= QuoteService_OnLogMessage;
            _quoteService.OnSymbolRepositoryLoaded -= QuoteService_OnSymbolRepositoryLoaded;
        }

        private void ApplyCustomStyles()
        {
            foreach (SmoothingMethod method in Enum.GetValues<SmoothingMethod>())
            {
                _cboModel.Items.Add(method.ToString());
            }
            if (_cboModel.Items.Count > 0)
                _cboModel.SelectedItem = SmoothingMethod.CubicSpline.ToString();

            _chkShift.CheckedChanged += (s, e) => UpdateChart();
            _dgvTQuote.CellDoubleClick += DgvTQuote_CellDoubleClick;

            _formsPlot.Plot.FigureBackground.Color = ScottPlot.Color.FromHex("#121212");
            _formsPlot.Plot.DataBackground.Color = ScottPlot.Color.FromHex("#121212");
            _formsPlot.Plot.Axes.Color(ScottPlot.Color.FromHex("#d7d7d7"));
            _formsPlot.Plot.Grid.LineColor = ScottPlot.Color.FromHex("#2d2d2d");
            _formsPlot.Plot.Title("Implied Volatility Curve");
            _formsPlot.Plot.XLabel("Strike Price");
            _formsPlot.Plot.YLabel("Implied Volatility (%)");
        }

        private void QuoteService_OnOptionsDataUpdated(Dictionary<string, OptionChain> chains)
        {
            _currentChains = chains;
            if (IsDisposed || !IsHandleCreated) return;

            BeginInvoke(new Action(() =>
            {
                UpdateOptionSubscriptionButton();
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

        private void QuoteService_OnLogMessage(string msg)
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke(new Action(() =>
            {
                _lblStatus.Text = msg;
                UpdateOptionSubscriptionButton();
            }));
        }

        private void QuoteService_OnSymbolRepositoryLoaded()
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke(new Action(UpdateOptionSubscriptionButton));
        }

        private void BtnStartQuotes_Click(object sender, EventArgs e)
        {
            if (_quoteService.StartOptionPricingSubscriptions())
                _lblStatus.Text = "[選擇權行情] 正在啟動 TXO 訂閱...";

            UpdateOptionSubscriptionButton();
        }

        private void UpdateOptionSubscriptionButton()
        {
            if (!SymbolRepository.Instance.IsInitialized)
            {
                _btnStartQuotes.Enabled = false;
                _btnStartQuotes.Text = "商品載入中";
                _btnStartQuotes.BackColor = Color.FromArgb(70, 70, 70);
            }
            else if (_quoteService.IsOptionPricingSubscriptionStarted)
            {
                _btnStartQuotes.Enabled = false;
                _btnStartQuotes.Text = "行情已啟動";
                _btnStartQuotes.BackColor = Color.FromArgb(40, 110, 65);
            }
            else if (_quoteService.IsOptionPricingSubscriptionStarting)
            {
                _btnStartQuotes.Enabled = false;
                _btnStartQuotes.Text = "啟動中...";
                _btnStartQuotes.BackColor = Color.FromArgb(120, 90, 20);
            }
            else
            {
                _btnStartQuotes.Enabled = true;
                _btnStartQuotes.Text = "啟動行情";
                _btnStartQuotes.BackColor = Color.FromArgb(0, 90, 150);
            }
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

        private void AutoSaveTimer_Tick(object? sender, EventArgs e)
        {
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

            string selectedKey = _cboExpiration.SelectedItem.ToString() ?? "";

            if (_currentChains.TryGetValue(selectedKey, out OptionChain? chain) && chain != null)
            {
                _formsPlot.Plot.Clear();

                _formsPlot.Plot.Title($"Implied Volatility Curve (Futures Price: {chain.UnderlyingPrice:F0})");

                var vline = _formsPlot.Plot.Add.VerticalLine(chain.UnderlyingPrice);
                vline.Color = ScottPlot.Color.FromHex("#FFD700");
                vline.LinePattern = LinePattern.Dashed;
                vline.LineWidth = 2;

                var allOptions = chain.Calls.Concat(chain.Puts)
                                      .Where(o => o.ImpliedVolatility > 0 && o.ImpliedVolatility <= 2.0)
                                      .OrderBy(o => o.StrikePrice)
                                      .ToList();

                if (allOptions.Count == 0) return;

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
                scatter.Color = ScottPlot.Color.FromHex("#00BFFF");

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
                line.Color = ScottPlot.Color.FromHex("#FF6347");
                line.LinePattern = LinePattern.Solid;

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
                    yestLine.Color = ScottPlot.Color.FromHex("#AAAAAA");
                    yestLine.LinePattern = LinePattern.Dashed;
                }

                ScottPlot.TickGenerators.NumericAutomatic tickGenY = new ScottPlot.TickGenerators.NumericAutomatic();
                tickGenY.LabelFormatter = x => x.ToString("P2");
                _formsPlot.Plot.Axes.Left.TickGenerator = tickGenY;

                _formsPlot.Plot.Axes.AutoScale();
                _formsPlot.Refresh();

                UpdateTQuote(chain);
            }
        }

        private void UpdateTQuote(OptionChain chain)
        {
            var strikes = chain.Calls.Select(c => c.StrikePrice)
                                     .Union(chain.Puts.Select(p => p.StrikePrice))
                                     .OrderBy(s => s)
                                     .ToList();

            var validStrikes = strikes.Where(k =>
                (chain.Calls.FirstOrDefault(c => c.StrikePrice == k)?.HasLastPrice ?? false) ||
                (chain.Puts.FirstOrDefault(p => p.StrikePrice == k)?.HasLastPrice ?? false)
            ).ToList();

            if (validStrikes.Count == 0) validStrikes = strikes;

            int scrollIndex = _dgvTQuote.FirstDisplayedScrollingRowIndex;

            _dgvTQuote.SuspendLayout();

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

                double callBid = _quoteDisplayMode == 0 ? (call?.Bid ?? 0) : (call?.TheoreticalBid ?? 0);
                double callAsk = _quoteDisplayMode == 0 ? (call?.Ask ?? 0) : (call?.TheoreticalAsk ?? 0);
                double putBid = _quoteDisplayMode == 0 ? (put?.Bid ?? 0) : (put?.TheoreticalBid ?? 0);
                double putAsk = _quoteDisplayMode == 0 ? (put?.Ask ?? 0) : (put?.TheoreticalAsk ?? 0);

                row.Cells[0].Value = call?.Delta.ToString("F2") ?? "-";
                row.Cells[1].Value = ivString;
                row.Cells[2].Value = (callBid > 0) ? callBid.ToString("F1") : "-";
                row.Cells[3].Value = (callAsk > 0) ? callAsk.ToString("F1") : "-";
                row.Cells[4].Value = (call?.LastPrice > 0) ? call.LastPrice.ToString("F1") : "-";

                row.Cells[5].Value = k.ToString("F0");

                row.Cells[6].Value = (put?.LastPrice > 0) ? put.LastPrice.ToString("F1") : "-";
                row.Cells[7].Value = (putBid > 0) ? putBid.ToString("F1") : "-";
                row.Cells[8].Value = (putAsk > 0) ? putAsk.ToString("F1") : "-";
                row.Cells[9].Value = ivString;
                row.Cells[10].Value = put?.Delta.ToString("F2") ?? "-";

                if (k == atmStrike)
                {
                    row.DefaultCellStyle.BackColor = Color.FromArgb(0, 100, 0);
                    row.DefaultCellStyle.ForeColor = Color.White;
                    row.Cells[5].Style.BackColor = Color.FromArgb(0, 100, 0);
                }
                else
                {
                    row.DefaultCellStyle.BackColor = Color.FromArgb(18, 18, 18);
                    row.DefaultCellStyle.ForeColor = Color.FromArgb(220, 220, 220);
                    row.Cells[5].Style.BackColor = Color.FromArgb(0, 0, 100);
                }
            }

            _dgvTQuote.ResumeLayout();

            if (scrollIndex >= 0 && scrollIndex < _dgvTQuote.Rows.Count)
            {
                try
                {
                    _dgvTQuote.FirstDisplayedScrollingRowIndex = scrollIndex;
                }
                catch { }
            }
        }

        private void DgvTQuote_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;

            var row = _dgvTQuote.Rows[e.RowIndex];
            if (row == null) return;

            var strikeVal = row.Cells["colStrike"]?.Value?.ToString();
            if (string.IsNullOrEmpty(strikeVal) || !double.TryParse(strikeVal, out double strikePrice)) return;

            string month = _cboExpiration.SelectedItem?.ToString() ?? "";
            string colName = _dgvTQuote.Columns[e.ColumnIndex].Name;

            bool isCall = colName.StartsWith("colCall");
            string cp = isCall ? "Call" : "Put";

            KGIOptionPricing.OrderManagement.Core.BuySell? bs = null;
            double? price = null;

            if (colName == "colCallBid" || colName == "colPutBid")
            {
                bs = KGIOptionPricing.OrderManagement.Core.BuySell.Sell;
                if (double.TryParse(row.Cells[colName]?.Value?.ToString(), out double p)) price = p;
            }
            else if (colName == "colCallAsk" || colName == "colPutAsk")
            {
                bs = KGIOptionPricing.OrderManagement.Core.BuySell.Buy;
                if (double.TryParse(row.Cells[colName]?.Value?.ToString(), out double p)) price = p;
            }
            else if (colName == "colCallLast" || colName == "colPutLast")
            {
                if (double.TryParse(row.Cells[colName]?.Value?.ToString(), out double p)) price = p;
            }

            string symbol = "";
            if (_currentChains != null && _currentChains.TryGetValue(month, out OptionChain? chain) && chain != null)
            {
                var optList = isCall ? chain.Calls : chain.Puts;
                var opt = optList.FirstOrDefault(o => Math.Abs(o.StrikePrice - strikePrice) < 0.01);
                if (opt != null && !string.IsNullOrEmpty(opt.Symbol))
                {
                    symbol = opt.Symbol;
                }
            }

            var mainForm = Application.OpenForms.OfType<Form1>().FirstOrDefault();
            mainForm?.OpenOrderPlacementWithOption(symbol, month, cp, strikePrice, bs, price);
        }
    }
}
