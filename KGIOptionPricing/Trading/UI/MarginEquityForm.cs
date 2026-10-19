using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace KGIOptionPricing
{
    public partial class MarginEquityForm : Form
    {
        private readonly KGITradeService _tradeService = null!;
        private readonly System.Windows.Forms.Timer _timer = null!;
        private bool _queryInProgress;
        private DateTime _queryStartedAt;

        public MarginEquityForm() => InitializeComponent();

        public MarginEquityForm(KGITradeService tradeService) : this()
        {
            _tradeService = tradeService ?? throw new ArgumentNullException(nameof(tradeService));


            _tradeService.OnFinancialByMarket += TradeService_OnFinancialByMarket;

            _timer = new System.Windows.Forms.Timer { Interval = 15000 };
            _timer.Tick += (s, e) =>
            {
                if (_tradeService.IsLoggedIn)
                {
                    if (_queryInProgress && DateTime.Now - _queryStartedAt > TimeSpan.FromSeconds(30))
                        FinishQuery("權益查詢逾時，將重新查詢");
                    BeginMarginQuery();
                }
            };
            _timer.Start();

            this.FormClosing += MarginEquityForm_FormClosing;
            this.Shown += (s, e) => btnRefresh_Click(this, EventArgs.Empty);
        }

        private void MarginEquityForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            _timer.Stop();
            _timer.Dispose();
            _tradeService.OnFinancialByMarket -= TradeService_OnFinancialByMarket;
            _tradeService.ResetMarginRequestTracking();
        }

        private void TradeService_OnFinancialByMarket(string market, List<Financial_TradernItem> items)
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => TradeService_OnFinancialByMarket(market, items)));
                return;
            }

            if (market == "I")
            {
                RenderDomestic(items);
                lblStatus.Text = $"國內權益已更新，正在查詢國外權益… {DateTime.Now:HH:mm:ss}";
                if (!_tradeService.RequestMargin("O"))
                    FinishQuery("國內權益已更新；國外權益查詢送出失敗");
                return;
            }

            if (market == "O")
            {
                RenderForeign(items);
                FinishQuery($"國內、國外權益最後更新時間: {DateTime.Now:HH:mm:ss}");
            }
        }

        private void RenderDomestic(List<Financial_TradernItem> items)
        {
            var item = items.FirstOrDefault();
            if (item == null)
            {
                ClearDomesticValues();
                return;
            }

            lblAccount.Text = $"交易帳號: {item.Account} (國內幣別: {(!string.IsNullOrEmpty(item.Currency) ? item.Currency : "TWD")})";

                // 1. 權益總值
                lblEquityVal.Text = $"NT$ {item.EQUITY:N0}";

                // 2. 風險指標
                double riskRatio = item.FULLMTRISK;
                if (riskRatio == 0 && item.MAMT > 0 && item.EQUITY > 0)
                {
                    riskRatio = (item.EQUITY / item.MAMT) * 100.0;
                }
                lblRiskVal.Text = $"{riskRatio:F2}%";
                if (riskRatio >= 150)
                {
                    lblRiskVal.ForeColor = Color.LimeGreen;
                }
                else if (riskRatio >= 100)
                {
                    lblRiskVal.ForeColor = Color.Gold;
                }
                else
                {
                    lblRiskVal.ForeColor = Color.FromArgb(255, 80, 80); // 紅色警報
                }

                // 3. 可用保證金
                lblExcessVal.Text = $"NT$ {item.EXCESS:N0}";
                lblExcessVal.ForeColor = item.EXCESS >= 0 ? Color.White : Color.FromArgb(255, 80, 80);

                // 4. 未實現損益
                double floatingPnL = item.FloatProfit != 0 ? item.FloatProfit : item.PRTLOS;
                lblFloatingVal.Text = $"NT$ {floatingPnL:N0}";
                lblFloatingVal.ForeColor = floatingPnL > 0 ? Color.FromArgb(255, 80, 80) : (floatingPnL < 0 ? Color.FromArgb(80, 220, 80) : Color.White);

                // 5. 當日已實現損益
                //double realizedPnL = item.CLOSEAMT != 0 ? item.CLOSEAMT : item.ExProfit;
                double realizedPnL = item.TPREMIUM - item.ORIGNFEE - item.CTAXAMT;
                lblRealizedVal.Text = $"NT$ {realizedPnL:N0}";
                lblRealizedVal.ForeColor = realizedPnL > 0 ? Color.FromArgb(255, 80, 80) : (realizedPnL < 0 ? Color.FromArgb(80, 220, 80) : Color.White);

                // 6. 帳戶餘額
                lblBalanceVal.Text = $"NT$ {item.Balance:N0}";

                // 7. 原始保證金
                lblInitMarginVal.Text = $"NT$ {item.IAMT:N0}";

                // 8. 維持保證金
                lblMaintMarginVal.Text = $"NT$ {item.MAMT:N0}";

                // 9. 追繳保證金
                lblMarginCallVal.Text = $"NT$ {item.MarginCall:N0}";
                lblMarginCallVal.ForeColor = item.MarginCall > 0 ? Color.FromArgb(255, 80, 80) : Color.White;

                // 10. 可出金金額
                lblWithdrawVal.Text = $"NT$ {item.WithdrawMnt:N0}";

                // 11. 手續費 / 稅金
                double feeTax = item.ORIGNFEE + item.CTAXAMT;
                lblFeeTaxVal.Text = $"NT$ {feeTax:N0} (手續費: ${item.ORIGNFEE:N0} / 稅: ${item.CTAXAMT:N0})";

                // 12. 淨權利金
                double premium = item.Premium != 0 ? item.Premium : item.OPREMIUM;
                lblPremiumVal.Text = $"NT$ {premium:N0}";

                lblStatus.Text = $"權益最後更新時間: {DateTime.Now:HH:mm:ss}";
        }

        private void RenderForeign(List<Financial_TradernItem> items)
        {
            _foreignGrid.Rows.Clear();
            foreach (Financial_TradernItem item in items.OrderBy(x => x.Currency).ThenBy(x => x.Account))
            {
                double riskRatio = item.FULLMTRISK;
                if (riskRatio == 0 && item.MAMT > 0 && item.EQUITY > 0)
                    riskRatio = item.EQUITY / item.MAMT * 100.0;
                double floatingPnL = item.FloatProfit != 0 ? item.FloatProfit : item.PRTLOS;
                double realizedPnL = item.TPREMIUM - item.ORIGNFEE - item.CTAXAMT;
                int rowIndex = _foreignGrid.Rows.Add(
                    item.Account,
                    string.IsNullOrWhiteSpace(item.Currency) ? "--" : item.Currency,
                    item.EQUITY,
                    item.EXCESS,
                    item.IAMT,
                    item.MAMT,
                    riskRatio,
                    floatingPnL,
                    realizedPnL,
                    item.WithdrawMnt,
                    item.MarginCall);

                DataGridViewRow row = _foreignGrid.Rows[rowIndex];
                row.Cells[6].Style.ForeColor = riskRatio >= 150 ? Color.LimeGreen : riskRatio >= 100 ? Color.Gold : Color.FromArgb(255, 80, 80);
                row.Cells[7].Style.ForeColor = ProfitColor(floatingPnL);
                row.Cells[8].Style.ForeColor = ProfitColor(realizedPnL);
                if (item.EXCESS < 0) row.Cells[3].Style.ForeColor = Color.FromArgb(255, 80, 80);
                if (item.MarginCall > 0) row.Cells[10].Style.ForeColor = Color.FromArgb(255, 80, 80);
            }
        }

        private static Color ProfitColor(double value) => value > 0
            ? Color.FromArgb(255, 80, 80)
            : value < 0 ? Color.FromArgb(80, 220, 80) : Color.White;

        private void ClearDomesticValues()
        {
            foreach (Label label in new[] { lblEquityVal, lblRiskVal, lblExcessVal, lblFloatingVal, lblRealizedVal, lblBalanceVal, lblInitMarginVal, lblMaintMarginVal, lblMarginCallVal, lblWithdrawVal, lblFeeTaxVal, lblPremiumVal })
            {
                label.Text = "--";
                label.ForeColor = Color.White;
            }
        }

        private void BeginMarginQuery()
        {
            if (_queryInProgress || !_tradeService.IsLoggedIn) return;
            _tradeService.ResetMarginRequestTracking();
            _queryInProgress = true;
            _queryStartedAt = DateTime.Now;
            btnRefresh.Enabled = false;
            lblStatus.Text = "正在查詢國內權益…";
            if (!_tradeService.RequestMargin("I"))
                FinishQuery("國內權益查詢送出失敗");
        }

        private void FinishQuery(string status)
        {
            _queryInProgress = false;
            btnRefresh.Enabled = true;
            lblStatus.Text = status;
        }

        private void btnRefresh_Click(object sender, EventArgs e)
        {
            if (_tradeService != null && _tradeService.IsLoggedIn)
            {
                BeginMarginQuery();
            }
            else
            {
                MessageBox.Show(this, "交易主機尚未連線或登入。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
