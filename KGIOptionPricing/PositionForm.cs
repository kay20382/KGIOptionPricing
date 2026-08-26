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
        private readonly KGITradeService _tradeService;
        private readonly OrderManager _orderManager;
        private readonly MarketDataService _marketDataService;
        private readonly System.Windows.Forms.Timer _autoRefreshTimer;

        public PositionForm(KGITradeService tradeService, OrderManager orderManager, MarketDataService marketDataService)
        {
            _tradeService = tradeService ?? throw new ArgumentNullException(nameof(tradeService));
            _orderManager = orderManager ?? throw new ArgumentNullException(nameof(orderManager));
            _marketDataService = marketDataService;

            InitializeComponent();

            _tradeService.OnPositionsUpdated += TradeService_OnPositionsUpdated;
            _tradeService.OnPositionsDetailUpdated += TradeService_OnPositionsDetailUpdated;

            _autoRefreshTimer = new System.Windows.Forms.Timer { Interval = 10000 };
            _autoRefreshTimer.Tick += (s, e) =>
            {
                if (_tradeService.IsLoggedIn)
                {
                    _tradeService.RequestPositions("I");
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
            _tradeService.OnPositionsUpdated -= TradeService_OnPositionsUpdated;
            _tradeService.OnPositionsDetailUpdated -= TradeService_OnPositionsDetailUpdated;
        }

        private void TradeService_OnPositionsUpdated(List<TradePositionItem> positions)
        {
            if (IsDisposed || !IsHandleCreated || positions == null) return;
            BeginInvoke(new Action(() =>
            {
                RenderSummaryPositions(positions);
            }));
        }

        private void TradeService_OnPositionsDetailUpdated(List<TradePositionDetailItem> details)
        {
            if (IsDisposed || !IsHandleCreated || details == null) return;
            BeginInvoke(new Action(() =>
            {
                RenderDetailPositions(details);
            }));
        }

        private void RenderSummaryPositions(List<TradePositionItem> positions)
        {
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

            // 更新頂部資訊
            double totalPnLMoney = totalPnLPoints * 50.0;
            lblTotalPnL.Text = $"未實現總損益: NT$ {totalPnLMoney:N0} ({totalPnLPoints:+0.0;-0.0;0.0} 點)";
            lblTotalPnL.ForeColor = totalPnLPoints > 0 ? Color.FromArgb(255, 80, 80) : (totalPnLPoints < 0 ? Color.FromArgb(80, 220, 80) : Color.White);

            lblTotalQty.Text = $"總持倉: {longQty + shortQty} 口 (多 {longQty} / 空 {shortQty})";
            lblDelta.Text = $"部位 Delta: {totalDelta:+0.00;-0.00;0.00} 口";
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

        private void btnRefresh_Click(object sender, EventArgs e)
        {
            if (_tradeService != null && _tradeService.IsLoggedIn)
            {
                _tradeService.RequestPositions("I");
                _tradeService.RequestPositionDetail("I");
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
