using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using KGIOptionPricing.OrderManagement.Core;

namespace KGIOptionPricing
{
    public partial class PositionForm : Form
    {
        private readonly KGITradeService _tradeService = null!;
        private readonly OrderManager _orderManager = null!;
        private readonly MarketDataService _marketDataService = null!;
        private readonly System.Windows.Forms.Timer _autoRefreshTimer = null!;
        private List<TradePositionItem> _domesticPositions = new();
        private List<TradePositionItem> _foreignPositions = new();
        private PositionQueryStage _queryStage;
        private DateTime _queryStartedAt;

        private enum PositionQueryStage
        {
            None,
            DomesticSummary,
            DomesticDetail,
            ForeignSummary,
            ForeignDetail
        }

        public PositionForm() => InitializeComponent();

        public PositionForm(KGITradeService tradeService, OrderManager orderManager, MarketDataService marketDataService) : this()
        {
            _tradeService = tradeService ?? throw new ArgumentNullException(nameof(tradeService));
            _orderManager = orderManager ?? throw new ArgumentNullException(nameof(orderManager));
            _marketDataService = marketDataService;


            _tradeService.OnPositionsByMarket += TradeService_OnPositionsByMarket;
            _tradeService.OnPositionDetailsByMarket += TradeService_OnPositionDetailsByMarket;

            _autoRefreshTimer = new System.Windows.Forms.Timer { Interval = 10000 };
            _autoRefreshTimer.Tick += (s, e) =>
            {
                if (_tradeService.IsLoggedIn)
                {
                    if (_queryStage != PositionQueryStage.None && DateTime.Now - _queryStartedAt > TimeSpan.FromSeconds(30))
                        FinishPositionQuery("部位查詢逾時");
                    BeginPositionQuery();
                }
            };
            _autoRefreshTimer.Start();

            this.FormClosing += PositionForm_FormClosing;
            this.Shown += (s, e) => btnRefresh_Click(this, EventArgs.Empty);
        }

        private void PositionForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            _autoRefreshTimer.Stop();
            _autoRefreshTimer.Dispose();
            _tradeService.OnPositionsByMarket -= TradeService_OnPositionsByMarket;
            _tradeService.OnPositionDetailsByMarket -= TradeService_OnPositionDetailsByMarket;
        }

        private void TradeService_OnPositionsByMarket(string market, List<TradePositionItem> positions)
        {
            if (IsDisposed || !IsHandleCreated || positions == null) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => TradeService_OnPositionsByMarket(market, positions)));
                return;
            }

            if (market == "O")
            {
                RenderForeignSummary(positions);
                if (_queryStage == PositionQueryStage.ForeignSummary)
                {
                    _queryStage = PositionQueryStage.ForeignDetail;
                    lblStatus.Text = "國外彙總部位已更新，正在查詢國外明細…";
                    if (!_tradeService.RequestPositionDetail("O"))
                        FinishPositionQuery("國外彙總部位已更新；國外明細查詢送出失敗");
                }
                return;
            }

            RenderSummaryPositions(positions);
            if (_queryStage == PositionQueryStage.DomesticSummary)
            {
                _queryStage = PositionQueryStage.DomesticDetail;
                lblStatus.Text = "國內彙總部位已更新，正在查詢國內明細…";
                if (!_tradeService.RequestPositionDetail("I"))
                    StartForeignSummaryQuery();
            }
        }

        private void TradeService_OnPositionDetailsByMarket(string market, List<TradePositionDetailItem> details)
        {
            if (IsDisposed || !IsHandleCreated || details == null) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => TradeService_OnPositionDetailsByMarket(market, details)));
                return;
            }

            if (market == "O")
            {
                RenderForeignDetail(details);
                if (_queryStage == PositionQueryStage.ForeignDetail)
                    FinishPositionQuery($"國內、國外部位最後更新時間: {DateTime.Now:HH:mm:ss}");
                return;
            }

            RenderDetailPositions(details);
            if (_queryStage == PositionQueryStage.DomesticDetail)
                StartForeignSummaryQuery();
        }

        private void RenderSummaryPositions(List<TradePositionItem> positions)
        {
            _domesticPositions = positions.ToList();
            dgvSummary.SuspendLayout();

            if (dgvSummary.Rows.Count != positions.Count)
            {
                dgvSummary.Rows.Clear();
                if (positions.Count > 0)
                {
                    dgvSummary.Rows.Add(positions.Count);
                }
            }

            int longQty = 0;
            int shortQty = 0;
            double totalPnLPoints = 0;
            double totalDelta = 0;

            for (int i = 0; i < positions.Count; i++)
            {
                var p = positions[i];
                var row = dgvSummary.Rows[i];
                row.Tag = p;

                int qty = p.OpenInterest;
                bool isBuy = p.BuySell.StartsWith("B", StringComparison.OrdinalIgnoreCase);
                if (isBuy) longQty += qty; else shortQty += qty;

                // 未實現點數損益 = (即時價 - 成本價) * (買: +1, 賣: -1) * 口數
                double mPrice = p.MarketPrice > 0 ? p.MarketPrice : p.AveragePrice;
                double pnlPoints = (mPrice - p.AveragePrice) * (isBuy ? 1.0 : -1.0) * qty;
                totalPnLPoints += pnlPoints;

                // 粗估 Delta (選擇權若無 Greeks 暫以 0.5 估算，期貨為 1.0)
                double delta = (p.ComType == "F" ? 1.0 : 0.5) * (isBuy ? 1.0 : -1.0) * qty;
                totalDelta += delta;

                row.Cells[0].Value = p.Commodity;
                row.Cells[1].Value = p.SettleMonth;
                row.Cells[2].Value = p.StrikePrice > 0 ? p.StrikePrice.ToString("F0") : "-";
                row.Cells[3].Value = p.CallPut;
                row.Cells[4].Value = isBuy ? "多 (Buy)" : "空 (Sell)";
                row.Cells[5].Value = qty;
                row.Cells[6].Value = p.AveragePrice.ToString("F1");
                row.Cells[7].Value = p.MarketPrice.ToString("F1");
                row.Cells[8].Value = pnlPoints.ToString("+0.0;-0.0;0.0");
                row.Cells[9].Value = delta.ToString("+0.00;-0.00;0.00");

                // 配色
                row.Cells[4].Style.ForeColor = isBuy ? Color.FromArgb(255, 80, 80) : Color.FromArgb(80, 220, 80);
                row.Cells[4].Style.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);

                row.Cells[8].Style.ForeColor = pnlPoints > 0 ? Color.FromArgb(255, 80, 80) : (pnlPoints < 0 ? Color.FromArgb(80, 220, 80) : Color.White);
                row.Cells[8].Style.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
            }

            dgvSummary.ResumeLayout();

            UpdateHeaderForSelectedMarket();
            lblStatus.Text = $"部位最後更新時間: {DateTime.Now:HH:mm:ss}";
        }

        private void RenderDetailPositions(List<TradePositionDetailItem> details)
        {
            dgvDetail.SuspendLayout();

            if (dgvDetail.Rows.Count != details.Count)
            {
                dgvDetail.Rows.Clear();
                if (details.Count > 0)
                {
                    dgvDetail.Rows.Add(details.Count);
                }
            }

            for (int i = 0; i < details.Count; i++)
            {
                var d = details[i];
                var row = dgvDetail.Rows[i];
                row.Tag = d;

                bool isBuy = d.BS.StartsWith("B", StringComparison.OrdinalIgnoreCase);

                row.Cells[0].Value = d.TradeDate;
                row.Cells[1].Value = d.OrdNo;
                row.Cells[2].Value = d.MarketNo;
                row.Cells[3].Value = $"{d.ComID} {d.ComYM} {d.StrikePrice:F0}{d.CP}";
                row.Cells[4].Value = isBuy ? "多" : "空";
                row.Cells[5].Value = d.Qty;
                row.Cells[6].Value = d.DealPrice.ToString("F1");
                row.Cells[7].Value = d.MPrice.ToString("F1");
                row.Cells[8].Value = d.PRTLOS.ToString("N0");
                row.Cells[9].Value = d.InitialMargin.ToString("N0");
                row.Cells[10].Value = d.SpKey > 0 ? d.SpKey.ToString() : "-";

                row.Cells[4].Style.ForeColor = isBuy ? Color.FromArgb(255, 80, 80) : Color.FromArgb(80, 220, 80);
                row.Cells[8].Style.ForeColor = d.PRTLOS > 0 ? Color.FromArgb(255, 80, 80) : (d.PRTLOS < 0 ? Color.FromArgb(80, 220, 80) : Color.White);
            }

            dgvDetail.ResumeLayout();
        }

        private void RenderForeignSummary(List<TradePositionItem> positions)
        {
            _foreignPositions = positions.ToList();
            _foreignSummary.Rows.Clear();
            foreach (TradePositionItem p in positions)
            {
                bool isBuy = p.BuySell.StartsWith("B", StringComparison.OrdinalIgnoreCase);
                int rowIndex = _foreignSummary.Rows.Add(
                    p.Exchange,
                    p.Commodity,
                    p.SettleMonth,
                    p.StrikePrice > 0 ? FormatPrice(p.StrikePrice) : "-",
                    p.CallPut,
                    isBuy ? "多 (Buy)" : "空 (Sell)",
                    p.OpenInterest,
                    FormatPrice(p.AveragePrice),
                    FormatPrice(p.MarketPrice),
                    p.UnrealizedPnL,
                    p.Currency);
                DataGridViewRow row = _foreignSummary.Rows[rowIndex];
                row.Tag = p;
                row.Cells[5].Style.ForeColor = isBuy ? Color.FromArgb(255, 80, 80) : Color.FromArgb(80, 220, 80);
                row.Cells[9].Style.ForeColor = ProfitColor(p.UnrealizedPnL);
            }
            UpdateHeaderForSelectedMarket();
        }

        private void RenderForeignDetail(List<TradePositionDetailItem> details)
        {
            _foreignDetail.Rows.Clear();
            foreach (TradePositionDetailItem d in details)
            {
                bool isBuy = d.BS.StartsWith("B", StringComparison.OrdinalIgnoreCase);
                int rowIndex = _foreignDetail.Rows.Add(
                    d.TradeDate,
                    d.Exchange,
                    d.OrdNo,
                    d.MarketNo,
                    $"{d.ComID} {d.ComYM} {(d.StrikePrice > 0 ? FormatPrice(d.StrikePrice) : "")} {d.CP}".Trim(),
                    isBuy ? "多" : "空",
                    d.Qty,
                    FormatPrice(d.DealPrice),
                    FormatPrice(d.MPrice),
                    d.PRTLOS,
                    d.InitialMargin,
                    d.MTMargin,
                    d.Currency);
                DataGridViewRow row = _foreignDetail.Rows[rowIndex];
                row.Cells[5].Style.ForeColor = isBuy ? Color.FromArgb(255, 80, 80) : Color.FromArgb(80, 220, 80);
                row.Cells[9].Style.ForeColor = ProfitColor(d.PRTLOS);
            }
        }

        private void UpdateHeaderForSelectedMarket()
        {
            bool foreign = _marketTabs.SelectedIndex == 1;
            List<TradePositionItem> positions = foreign ? _foreignPositions : _domesticPositions;
            int longQty = positions.Where(p => p.BuySell.StartsWith("B", StringComparison.OrdinalIgnoreCase)).Sum(p => p.OpenInterest);
            int shortQty = positions.Where(p => !p.BuySell.StartsWith("B", StringComparison.OrdinalIgnoreCase)).Sum(p => p.OpenInterest);
            double delta = positions.Sum(p => (p.ComType == "F" ? 1.0 : 0.5) * (p.BuySell.StartsWith("B", StringComparison.OrdinalIgnoreCase) ? 1.0 : -1.0) * p.OpenInterest);

            lblTotalQty.Text = $"總持倉: {longQty + shortQty} 口 (多 {longQty} / 空 {shortQty})";
            lblDelta.Text = $"部位 Delta: {delta:+0.00;-0.00;0.00} 口";
            if (foreign)
            {
                lblTotalPnL.Text = "國外未實現損益：依幣別欄位顯示";
                lblTotalPnL.ForeColor = Color.Gold;
                btnCloseAll.Enabled = false;
                btnCloseSelected.Enabled = false;
            }
            else
            {
                double points = positions.Sum(p => ((p.MarketPrice > 0 ? p.MarketPrice : p.AveragePrice) - p.AveragePrice) * (p.BuySell.StartsWith("B", StringComparison.OrdinalIgnoreCase) ? 1.0 : -1.0) * p.OpenInterest);
                double money = points * 50.0;
                lblTotalPnL.Text = $"未實現總損益: NT$ {money:N0} ({points:+0.0;-0.0;0.0} 點)";
                lblTotalPnL.ForeColor = ProfitColor(points);
                btnCloseAll.Enabled = true;
                btnCloseSelected.Enabled = true;
            }
        }

        private void MarketTabs_SelectedIndexChanged(object? sender, EventArgs e) => UpdateHeaderForSelectedMarket();

        private void BeginPositionQuery()
        {
            if (_queryStage != PositionQueryStage.None || !_tradeService.IsLoggedIn) return;
            _queryStage = PositionQueryStage.DomesticSummary;
            _queryStartedAt = DateTime.Now;
            btnRefresh.Enabled = false;
            lblStatus.Text = "正在查詢國內彙總部位…";
            if (!_tradeService.RequestPositions("I"))
                FinishPositionQuery("國內彙總部位查詢送出失敗");
        }

        private void StartForeignSummaryQuery()
        {
            _queryStage = PositionQueryStage.ForeignSummary;
            lblStatus.Text = "國內部位已更新，正在查詢國外彙總部位…";
            if (!_tradeService.RequestPositions("O"))
                FinishPositionQuery("國內部位已更新；國外彙總部位查詢送出失敗");
        }

        private void FinishPositionQuery(string status)
        {
            _queryStage = PositionQueryStage.None;
            btnRefresh.Enabled = true;
            lblStatus.Text = status;
        }

        private static string FormatPrice(double value) => value.ToString("0.######");
        private static Color ProfitColor(double value) => value > 0 ? Color.FromArgb(255, 80, 80) : value < 0 ? Color.FromArgb(80, 220, 80) : Color.White;

        private void btnRefresh_Click(object sender, EventArgs e)
        {
            if (_tradeService != null && _tradeService.IsLoggedIn)
            {
                BeginPositionQuery();
            }
            else
            {
                MessageBox.Show(this, "交易伺服器尚未連線或登入。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void btnCloseAll_Click(object sender, EventArgs e)
        {
            var positions = _tradeService.CurrentPositions;
            if (positions == null || positions.Count == 0)
            {
                MessageBox.Show(this, "目前無任何未平倉部位。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (MessageBox.Show(this, $"【危險操作警告】\n確定要將目前所有 {positions.Count} 檔未平倉部位【市價全平】嗎？", "市價全平確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                foreach (var p in positions)
                {
                    if (p.OpenInterest <= 0) continue;

                    BuySell reverseSide = p.BuySell.StartsWith("B", StringComparison.OrdinalIgnoreCase)
                        ? BuySell.Sell
                        : BuySell.Buy;

                    var req = new OrderRequest
                    {
                        OrderId = $"CLOSE_{DateTime.Now.Ticks}",
                        Instrument = new InstrumentKey
                        {
                            Market = p.ComType == "F" ? MarketType.DomesticFuture : MarketType.DomesticOption,
                            Exchange = "TAIFEX",
                            Symbol = p.Commodity,
                            ContractMonth = p.SettleMonth
                        },
                        BuySell = reverseSide,
                        Price = 0,
                        Quantity = p.OpenInterest,
                        TimeInForce = TimeInForce.ROD
                    };

                    _orderManager.PlaceOrder(req);
                }

                MessageBox.Show(this, "已送出全部部位之反向平倉委託！", "平倉送出", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void btnCloseSelected_Click(object sender, EventArgs e)
        {
            if (dgvSummary.CurrentRow?.Tag is TradePositionItem p)
            {
                if (p.OpenInterest <= 0)
                {
                    MessageBox.Show(this, "該部位口數為 0，無需平倉。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                BuySell reverseSide = p.BuySell.StartsWith("B", StringComparison.OrdinalIgnoreCase)
                    ? BuySell.Sell
                    : BuySell.Buy;

                string instrument = p.ComType == "F"
                    ? $"{p.Commodity}"
                    : $"{p.Commodity}-{p.SettleMonth}-{p.StrikePrice:F0}-{p.CallPut}";

                if (MessageBox.Show(this, $"確定平倉部位: {instrument} 反向 {reverseSide} {p.OpenInterest} 口 (市價單) 嗎？", "選取平倉確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    var req = new OrderRequest
                    {
                        OrderId = $"CLOSE_{DateTime.Now.Ticks}",
                        Instrument = new InstrumentKey
                        {
                            Market = p.ComType == "F" ? MarketType.DomesticFuture : MarketType.DomesticOption,
                            Exchange = "TAIFEX",
                            Symbol = p.Commodity,
                            ContractMonth = p.SettleMonth
                        },
                        BuySell = reverseSide,
                        Price = 0,
                        Quantity = p.OpenInterest,
                        TimeInForce = TimeInForce.ROD
                    };

                    _orderManager.PlaceOrder(req);
                }
            }
            else
            {
                MessageBox.Show(this, "請先在彙總列表中選取一筆要平倉的部位。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }
}
