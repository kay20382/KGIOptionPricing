using System.Drawing;
using System.Windows.Forms;

namespace KGIOptionPricing
{
    partial class MarginEquityForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            pnlTop = new Panel();
            btnRefresh = new Button();
            lblAccount = new Label();
            pnlCards = new TableLayoutPanel();
            cardEquity = new Panel();
            lblEquityVal = new Label();
            lblEquityTitle = new Label();
            cardRisk = new Panel();
            lblRiskVal = new Label();
            lblRiskTitle = new Label();
            cardFloatingPnL = new Panel();
            lblFloatingVal = new Label();
            lblFloatingTitle = new Label();
            cardRealizedPnL = new Panel();
            lblRealizedVal = new Label();
            lblRealizedTitle = new Label();
            cardInitMargin = new Panel();
            lblInitMarginVal = new Label();
            lblInitMarginTitle = new Label();
            cardMaintMargin = new Panel();
            lblMaintMarginVal = new Label();
            lblMaintMarginTitle = new Label();
            cardExcess = new Panel();
            lblExcessVal = new Label();
            lblExcessTitle = new Label();
            cardMarginCall = new Panel();
            lblMarginCallVal = new Label();
            lblMarginCallTitle = new Label();
            cardBalance = new Panel();
            lblBalanceVal = new Label();
            lblBalanceTitle = new Label();
            cardWithdraw = new Panel();
            lblWithdrawVal = new Label();
            lblWithdrawTitle = new Label();
            cardFeeTax = new Panel();
            lblFeeTaxVal = new Label();
            lblFeeTaxTitle = new Label();
            cardPremium = new Panel();
            lblPremiumVal = new Label();
            lblPremiumTitle = new Label();
            pnlBottom = new Panel();
            lblStatus = new Label();
            pnlTop.SuspendLayout();
            pnlCards.SuspendLayout();
            cardEquity.SuspendLayout();
            cardRisk.SuspendLayout();
            cardFloatingPnL.SuspendLayout();
            cardRealizedPnL.SuspendLayout();
            cardInitMargin.SuspendLayout();
            cardMaintMargin.SuspendLayout();
            cardExcess.SuspendLayout();
            cardMarginCall.SuspendLayout();
            cardBalance.SuspendLayout();
            cardWithdraw.SuspendLayout();
            cardFeeTax.SuspendLayout();
            cardPremium.SuspendLayout();
            pnlBottom.SuspendLayout();
            SuspendLayout();
            // 
            // pnlTop
            // 
            pnlTop.BackColor = Color.FromArgb(28, 28, 28);
            pnlTop.Controls.Add(btnRefresh);
            pnlTop.Controls.Add(lblAccount);
            pnlTop.Dock = DockStyle.Top;
            pnlTop.Location = new Point(0, 0);
            pnlTop.Name = "pnlTop";
            pnlTop.Size = new Size(950, 55);
            pnlTop.TabIndex = 0;
            // 
            // btnRefresh
            // 
            btnRefresh.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnRefresh.BackColor = Color.FromArgb(0, 122, 204);
            btnRefresh.FlatStyle = FlatStyle.Flat;
            btnRefresh.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
            btnRefresh.ForeColor = Color.White;
            btnRefresh.Location = new Point(805, 10);
            btnRefresh.Name = "btnRefresh";
            btnRefresh.Size = new Size(130, 35);
            btnRefresh.TabIndex = 1;
            btnRefresh.Text = "🔄 立即查詢權益";
            btnRefresh.UseVisualStyleBackColor = false;
            btnRefresh.Click += btnRefresh_Click;
            // 
            // lblAccount
            // 
            lblAccount.AutoSize = true;
            lblAccount.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            lblAccount.ForeColor = Color.White;
            lblAccount.Location = new Point(20, 17);
            lblAccount.Name = "lblAccount";
            lblAccount.Size = new Size(183, 20);
            lblAccount.TabIndex = 0;
            lblAccount.Text = "交易帳號: 9819113 (TWD)";
            // 
            // pnlCards
            // 
            pnlCards.ColumnCount = 3;
            pnlCards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33333F));
            pnlCards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33333F));
            pnlCards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33333F));
            pnlCards.Controls.Add(cardEquity, 0, 0);
            pnlCards.Controls.Add(cardRisk, 1, 0);
            pnlCards.Controls.Add(cardExcess, 2, 0);
            pnlCards.Controls.Add(cardFloatingPnL, 0, 1);
            pnlCards.Controls.Add(cardRealizedPnL, 1, 1);
            pnlCards.Controls.Add(cardBalance, 2, 1);
            pnlCards.Controls.Add(cardInitMargin, 0, 2);
            pnlCards.Controls.Add(cardMaintMargin, 1, 2);
            pnlCards.Controls.Add(cardMarginCall, 2, 2);
            pnlCards.Controls.Add(cardWithdraw, 0, 3);
            pnlCards.Controls.Add(cardFeeTax, 1, 3);
            pnlCards.Controls.Add(cardPremium, 2, 3);
            pnlCards.Dock = DockStyle.Fill;
            pnlCards.Location = new Point(0, 55);
            pnlCards.Name = "pnlCards";
            pnlCards.Padding = new Padding(10);
            pnlCards.RowCount = 4;
            pnlCards.RowStyles.Add(new RowStyle(SizeType.Percent, 25F));
            pnlCards.RowStyles.Add(new RowStyle(SizeType.Percent, 25F));
            pnlCards.RowStyles.Add(new RowStyle(SizeType.Percent, 25F));
            pnlCards.RowStyles.Add(new RowStyle(SizeType.Percent, 25F));
            pnlCards.Size = new Size(950, 480);
            pnlCards.TabIndex = 1;
            // 
            // cardEquity
            // 
            cardEquity.BackColor = Color.FromArgb(32, 32, 35);
            cardEquity.Controls.Add(lblEquityVal);
            cardEquity.Controls.Add(lblEquityTitle);
            cardEquity.Dock = DockStyle.Fill;
            cardEquity.Location = new Point(13, 13);
            cardEquity.Name = "cardEquity";
            cardEquity.Size = new Size(304, 109);
            cardEquity.TabIndex = 0;
            // 
            // lblEquityVal
            // 
            lblEquityVal.AutoSize = true;
            lblEquityVal.Font = new Font("Segoe UI", 16F, FontStyle.Bold);
            lblEquityVal.ForeColor = Color.Gold;
            lblEquityVal.Location = new Point(15, 45);
            lblEquityVal.Name = "lblEquityVal";
            lblEquityVal.Size = new Size(68, 30);
            lblEquityVal.TabIndex = 1;
            lblEquityVal.Text = "NT$ 0";
            // 
            // lblEquityTitle
            // 
            lblEquityTitle.AutoSize = true;
            lblEquityTitle.Font = new Font("Segoe UI", 10F);
            lblEquityTitle.ForeColor = Color.DarkGray;
            lblEquityTitle.Location = new Point(15, 15);
            lblEquityTitle.Name = "lblEquityTitle";
            lblEquityTitle.Size = new Size(130, 19);
            lblEquityTitle.TabIndex = 0;
            lblEquityTitle.Text = "🏦 帳戶權益總值 (Equity)";
            // 
            // cardRisk
            // 
            cardRisk.BackColor = Color.FromArgb(32, 32, 35);
            cardRisk.Controls.Add(lblRiskVal);
            cardRisk.Controls.Add(lblRiskTitle);
            cardRisk.Dock = DockStyle.Fill;
            cardRisk.Location = new Point(323, 13);
            cardRisk.Name = "cardRisk";
            cardRisk.Size = new Size(304, 109);
            cardRisk.TabIndex = 1;
            // 
            // lblRiskVal
            // 
            lblRiskVal.AutoSize = true;
            lblRiskVal.Font = new Font("Segoe UI", 16F, FontStyle.Bold);
            lblRiskVal.ForeColor = Color.LimeGreen;
            lblRiskVal.Location = new Point(15, 45);
            lblRiskVal.Name = "lblRiskVal";
            lblRiskVal.Size = new Size(74, 30);
            lblRiskVal.TabIndex = 1;
            lblRiskVal.Text = "0.00%";
            // 
            // lblRiskTitle
            // 
            lblRiskTitle.AutoSize = true;
            lblRiskTitle.Font = new Font("Segoe UI", 10F);
            lblRiskTitle.ForeColor = Color.DarkGray;
            lblRiskTitle.Location = new Point(15, 15);
            lblRiskTitle.Name = "lblRiskTitle";
            lblRiskTitle.Size = new Size(142, 19);
            lblRiskTitle.TabIndex = 0;
            lblRiskTitle.Text = "🚨 風險指標 (Risk Ratio)";
            // 
            // cardExcess
            // 
            cardExcess.BackColor = Color.FromArgb(32, 32, 35);
            cardExcess.Controls.Add(lblExcessVal);
            cardExcess.Controls.Add(lblExcessTitle);
            cardExcess.Dock = DockStyle.Fill;
            cardExcess.Location = new Point(633, 13);
            cardExcess.Name = "cardExcess";
            cardExcess.Size = new Size(304, 109);
            cardExcess.TabIndex = 2;
            // 
            // lblExcessVal
            // 
            lblExcessVal.AutoSize = true;
            lblExcessVal.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
            lblExcessVal.ForeColor = Color.White;
            lblExcessVal.Location = new Point(15, 48);
            lblExcessVal.Name = "lblExcessVal";
            lblExcessVal.Size = new Size(60, 25);
            lblExcessVal.TabIndex = 1;
            lblExcessVal.Text = "NT$ 0";
            // 
            // lblExcessTitle
            // 
            lblExcessTitle.AutoSize = true;
            lblExcessTitle.Font = new Font("Segoe UI", 10F);
            lblExcessTitle.ForeColor = Color.DarkGray;
            lblExcessTitle.Location = new Point(15, 15);
            lblExcessTitle.Name = "lblExcessTitle";
            lblExcessTitle.Size = new Size(143, 19);
            lblExcessTitle.TabIndex = 0;
            lblExcessTitle.Text = "💵 可用保證金 (Excess)";
            // 
            // cardFloatingPnL
            // 
            cardFloatingPnL.BackColor = Color.FromArgb(32, 32, 35);
            cardFloatingPnL.Controls.Add(lblFloatingVal);
            cardFloatingPnL.Controls.Add(lblFloatingTitle);
            cardFloatingPnL.Dock = DockStyle.Fill;
            cardFloatingPnL.Location = new Point(13, 128);
            cardFloatingPnL.Name = "cardFloatingPnL";
            cardFloatingPnL.Size = new Size(304, 109);
            cardFloatingPnL.TabIndex = 3;
            // 
            // lblFloatingVal
            // 
            lblFloatingVal.AutoSize = true;
            lblFloatingVal.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
            lblFloatingVal.ForeColor = Color.White;
            lblFloatingVal.Location = new Point(15, 48);
            lblFloatingVal.Name = "lblFloatingVal";
            lblFloatingVal.Size = new Size(60, 25);
            lblFloatingVal.TabIndex = 1;
            lblFloatingVal.Text = "NT$ 0";
            // 
            // lblFloatingTitle
            // 
            lblFloatingTitle.AutoSize = true;
            lblFloatingTitle.Font = new Font("Segoe UI", 10F);
            lblFloatingTitle.ForeColor = Color.DarkGray;
            lblFloatingTitle.Location = new Point(15, 15);
            lblFloatingTitle.Name = "lblFloatingTitle";
            lblFloatingTitle.Size = new Size(160, 19);
            lblFloatingTitle.TabIndex = 0;
            lblFloatingTitle.Text = "📈 未實現損益 (Floating PnL)";
            // 
            // cardRealizedPnL
            // 
            cardRealizedPnL.BackColor = Color.FromArgb(32, 32, 35);
            cardRealizedPnL.Controls.Add(lblRealizedVal);
            cardRealizedPnL.Controls.Add(lblRealizedTitle);
            cardRealizedPnL.Dock = DockStyle.Fill;
            cardRealizedPnL.Location = new Point(323, 128);
            cardRealizedPnL.Name = "cardRealizedPnL";
            cardRealizedPnL.Size = new Size(304, 109);
            cardRealizedPnL.TabIndex = 4;
            // 
            // lblRealizedVal
            // 
            lblRealizedVal.AutoSize = true;
            lblRealizedVal.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
            lblRealizedVal.ForeColor = Color.White;
            lblRealizedVal.Location = new Point(15, 48);
            lblRealizedVal.Name = "lblRealizedVal";
            lblRealizedVal.Size = new Size(60, 25);
            lblRealizedVal.TabIndex = 1;
            lblRealizedVal.Text = "NT$ 0";
            // 
            // lblRealizedTitle
            // 
            lblRealizedTitle.AutoSize = true;
            lblRealizedTitle.Font = new Font("Segoe UI", 10F);
            lblRealizedTitle.ForeColor = Color.DarkGray;
            lblRealizedTitle.Location = new Point(15, 15);
            lblRealizedTitle.Name = "lblRealizedTitle";
            lblRealizedTitle.Size = new Size(167, 19);
            lblRealizedTitle.TabIndex = 0;
            lblRealizedTitle.Text = "💰 當日平倉損益 (Realized PnL)";
            // 
            // cardBalance
            // 
            cardBalance.BackColor = Color.FromArgb(32, 32, 35);
            cardBalance.Controls.Add(lblBalanceVal);
            cardBalance.Controls.Add(lblBalanceTitle);
            cardBalance.Dock = DockStyle.Fill;
            cardBalance.Location = new Point(633, 128);
            cardBalance.Name = "cardBalance";
            cardBalance.Size = new Size(304, 109);
            cardBalance.TabIndex = 5;
            // 
            // lblBalanceVal
            // 
            lblBalanceVal.AutoSize = true;
            lblBalanceVal.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
            lblBalanceVal.ForeColor = Color.White;
            lblBalanceVal.Location = new Point(15, 48);
            lblBalanceVal.Name = "lblBalanceVal";
            lblBalanceVal.Size = new Size(60, 25);
            lblBalanceVal.TabIndex = 1;
            lblBalanceVal.Text = "NT$ 0";
            // 
            // lblBalanceTitle
            // 
            lblBalanceTitle.AutoSize = true;
            lblBalanceTitle.Font = new Font("Segoe UI", 10F);
            lblBalanceTitle.ForeColor = Color.DarkGray;
            lblBalanceTitle.Location = new Point(15, 15);
            lblBalanceTitle.Name = "lblBalanceTitle";
            lblBalanceTitle.Size = new Size(137, 19);
            lblBalanceTitle.TabIndex = 0;
            lblBalanceTitle.Text = "💳 帳戶餘額 (Balance)";
            // 
            // cardInitMargin
            // 
            cardInitMargin.BackColor = Color.FromArgb(32, 32, 35);
            cardInitMargin.Controls.Add(lblInitMarginVal);
            cardInitMargin.Controls.Add(lblInitMarginTitle);
            cardInitMargin.Dock = DockStyle.Fill;
            cardInitMargin.Location = new Point(13, 243);
            cardInitMargin.Name = "cardInitMargin";
            cardInitMargin.Size = new Size(304, 109);
            cardInitMargin.TabIndex = 6;
            // 
            // lblInitMarginVal
            // 
            lblInitMarginVal.AutoSize = true;
            lblInitMarginVal.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
            lblInitMarginVal.ForeColor = Color.White;
            lblInitMarginVal.Location = new Point(15, 48);
            lblInitMarginVal.Name = "lblInitMarginVal";
            lblInitMarginVal.Size = new Size(60, 25);
            lblInitMarginVal.TabIndex = 1;
            lblInitMarginVal.Text = "NT$ 0";
            // 
            // lblInitMarginTitle
            // 
            lblInitMarginTitle.AutoSize = true;
            lblInitMarginTitle.Font = new Font("Segoe UI", 10F);
            lblInitMarginTitle.ForeColor = Color.DarkGray;
            lblInitMarginTitle.Location = new Point(15, 15);
            lblInitMarginTitle.Name = "lblInitMarginTitle";
            lblInitMarginTitle.Size = new Size(160, 19);
            lblInitMarginTitle.TabIndex = 0;
            lblInitMarginTitle.Text = "🔒 原始保證金 (Initial Margin)";
            // 
            // cardMaintMargin
            // 
            cardMaintMargin.BackColor = Color.FromArgb(32, 32, 35);
            cardMaintMargin.Controls.Add(lblMaintMarginVal);
            cardMaintMargin.Controls.Add(lblMaintMarginTitle);
            cardMaintMargin.Dock = DockStyle.Fill;
            cardMaintMargin.Location = new Point(323, 243);
            cardMaintMargin.Name = "cardMaintMargin";
            cardMaintMargin.Size = new Size(304, 109);
            cardMaintMargin.TabIndex = 7;
            // 
            // lblMaintMarginVal
            // 
            lblMaintMarginVal.AutoSize = true;
            lblMaintMarginVal.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
            lblMaintMarginVal.ForeColor = Color.White;
            lblMaintMarginVal.Location = new Point(15, 48);
            lblMaintMarginVal.Name = "lblMaintMarginVal";
            lblMaintMarginVal.Size = new Size(60, 25);
            lblMaintMarginVal.TabIndex = 1;
            lblMaintMarginVal.Text = "NT$ 0";
            // 
            // lblMaintMarginTitle
            // 
            lblMaintMarginTitle.AutoSize = true;
            lblMaintMarginTitle.Font = new Font("Segoe UI", 10F);
            lblMaintMarginTitle.ForeColor = Color.DarkGray;
            lblMaintMarginTitle.Location = new Point(15, 15);
            lblMaintMarginTitle.Name = "lblMaintMarginTitle";
            lblMaintMarginTitle.Size = new Size(174, 19);
            lblMaintMarginTitle.TabIndex = 0;
            lblMaintMarginTitle.Text = "🛡️ 維持保證金 (Maint. Margin)";
            // 
            // cardMarginCall
            // 
            cardMarginCall.BackColor = Color.FromArgb(32, 32, 35);
            cardMarginCall.Controls.Add(lblMarginCallVal);
            cardMarginCall.Controls.Add(lblMarginCallTitle);
            cardMarginCall.Dock = DockStyle.Fill;
            cardMarginCall.Location = new Point(633, 243);
            cardMarginCall.Name = "cardMarginCall";
            cardMarginCall.Size = new Size(304, 109);
            cardMarginCall.TabIndex = 8;
            // 
            // lblMarginCallVal
            // 
            lblMarginCallVal.AutoSize = true;
            lblMarginCallVal.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
            lblMarginCallVal.ForeColor = Color.White;
            lblMarginCallVal.Location = new Point(15, 48);
            lblMarginCallVal.Name = "lblMarginCallVal";
            lblMarginCallVal.Size = new Size(60, 25);
            lblMarginCallVal.TabIndex = 1;
            lblMarginCallVal.Text = "NT$ 0";
            // 
            // lblMarginCallTitle
            // 
            lblMarginCallTitle.AutoSize = true;
            lblMarginCallTitle.Font = new Font("Segoe UI", 10F);
            lblMarginCallTitle.ForeColor = Color.DarkGray;
            lblMarginCallTitle.Location = new Point(15, 15);
            lblMarginCallTitle.Name = "lblMarginCallTitle";
            lblMarginCallTitle.Size = new Size(160, 19);
            lblMarginCallTitle.TabIndex = 0;
            lblMarginCallTitle.Text = "⚠️ 追繳保證金 (Margin Call)";
            // 
            // cardWithdraw
            // 
            cardWithdraw.BackColor = Color.FromArgb(32, 32, 35);
            cardWithdraw.Controls.Add(lblWithdrawVal);
            cardWithdraw.Controls.Add(lblWithdrawTitle);
            cardWithdraw.Dock = DockStyle.Fill;
            cardWithdraw.Location = new Point(13, 358);
            cardWithdraw.Name = "cardWithdraw";
            cardWithdraw.Size = new Size(304, 109);
            cardWithdraw.TabIndex = 9;
            // 
            // lblWithdrawVal
            // 
            lblWithdrawVal.AutoSize = true;
            lblWithdrawVal.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
            lblWithdrawVal.ForeColor = Color.White;
            lblWithdrawVal.Location = new Point(15, 48);
            lblWithdrawVal.Name = "lblWithdrawVal";
            lblWithdrawVal.Size = new Size(60, 25);
            lblWithdrawVal.TabIndex = 1;
            lblWithdrawVal.Text = "NT$ 0";
            // 
            // lblWithdrawTitle
            // 
            lblWithdrawTitle.AutoSize = true;
            lblWithdrawTitle.Font = new Font("Segoe UI", 10F);
            lblWithdrawTitle.ForeColor = Color.DarkGray;
            lblWithdrawTitle.Location = new Point(15, 15);
            lblWithdrawTitle.Name = "lblWithdrawTitle";
            lblWithdrawTitle.Size = new Size(157, 19);
            lblWithdrawTitle.TabIndex = 0;
            lblWithdrawTitle.Text = "🏧 可出金金額 (Withdrawal)";
            // 
            // cardFeeTax
            // 
            cardFeeTax.BackColor = Color.FromArgb(32, 32, 35);
            cardFeeTax.Controls.Add(lblFeeTaxVal);
            cardFeeTax.Controls.Add(lblFeeTaxTitle);
            cardFeeTax.Dock = DockStyle.Fill;
            cardFeeTax.Location = new Point(323, 358);
            cardFeeTax.Name = "cardFeeTax";
            cardFeeTax.Size = new Size(304, 109);
            cardFeeTax.TabIndex = 10;
            // 
            // lblFeeTaxVal
            // 
            lblFeeTaxVal.AutoSize = true;
            lblFeeTaxVal.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
            lblFeeTaxVal.ForeColor = Color.White;
            lblFeeTaxVal.Location = new Point(15, 48);
            lblFeeTaxVal.Name = "lblFeeTaxVal";
            lblFeeTaxVal.Size = new Size(60, 25);
            lblFeeTaxVal.TabIndex = 1;
            lblFeeTaxVal.Text = "NT$ 0";
            // 
            // lblFeeTaxTitle
            // 
            lblFeeTaxTitle.AutoSize = true;
            lblFeeTaxTitle.Font = new Font("Segoe UI", 10F);
            lblFeeTaxTitle.ForeColor = Color.DarkGray;
            lblFeeTaxTitle.Location = new Point(15, 15);
            lblFeeTaxTitle.Name = "lblFeeTaxTitle";
            lblFeeTaxTitle.Size = new Size(149, 19);
            lblFeeTaxTitle.TabIndex = 0;
            lblFeeTaxTitle.Text = "🧾 當日手續費/期交稅額";
            // 
            // cardPremium
            // 
            cardPremium.BackColor = Color.FromArgb(32, 32, 35);
            cardPremium.Controls.Add(lblPremiumVal);
            cardPremium.Controls.Add(lblPremiumTitle);
            cardPremium.Dock = DockStyle.Fill;
            cardPremium.Location = new Point(633, 358);
            cardPremium.Name = "cardPremium";
            cardPremium.Size = new Size(304, 109);
            cardPremium.TabIndex = 11;
            // 
            // lblPremiumVal
            // 
            lblPremiumVal.AutoSize = true;
            lblPremiumVal.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
            lblPremiumVal.ForeColor = Color.White;
            lblPremiumVal.Location = new Point(15, 48);
            lblPremiumVal.Name = "lblPremiumVal";
            lblPremiumVal.Size = new Size(60, 25);
            lblPremiumVal.TabIndex = 1;
            lblPremiumVal.Text = "NT$ 0";
            // 
            // lblPremiumTitle
            // 
            lblPremiumTitle.AutoSize = true;
            lblPremiumTitle.Font = new Font("Segoe UI", 10F);
            lblPremiumTitle.ForeColor = Color.DarkGray;
            lblPremiumTitle.Location = new Point(15, 15);
            lblPremiumTitle.Name = "lblPremiumTitle";
            lblPremiumTitle.Size = new Size(157, 19);
            lblPremiumTitle.TabIndex = 0;
            lblPremiumTitle.Text = "📊 選擇權淨權利金收入/出";
            // 
            // pnlBottom
            // 
            pnlBottom.BackColor = Color.FromArgb(28, 28, 28);
            pnlBottom.Controls.Add(lblStatus);
            pnlBottom.Dock = DockStyle.Bottom;
            pnlBottom.Location = new Point(0, 535);
            pnlBottom.Name = "pnlBottom";
            pnlBottom.Size = new Size(950, 35);
            pnlBottom.TabIndex = 2;
            // 
            // lblStatus
            // 
            lblStatus.AutoSize = true;
            lblStatus.ForeColor = Color.DarkGray;
            lblStatus.Location = new Point(15, 8);
            lblStatus.Name = "lblStatus";
            lblStatus.Size = new Size(183, 19);
            lblStatus.TabIndex = 0;
            lblStatus.Text = "權益最後更新時間: 尚未更新";
            // 
            // MarginEquityForm
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(18, 18, 18);
            ClientSize = new Size(950, 570);
            Controls.Add(pnlCards);
            Controls.Add(pnlBottom);
            Controls.Add(pnlTop);
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
            Name = "MarginEquityForm";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "🏦 權益數與保證金 (Margin & Equity Dashboard)";
            pnlTop.ResumeLayout(false);
            pnlTop.PerformLayout();
            pnlCards.ResumeLayout(false);
            cardEquity.ResumeLayout(false);
            cardEquity.PerformLayout();
            cardRisk.ResumeLayout(false);
            cardRisk.PerformLayout();
            cardExcess.ResumeLayout(false);
            cardExcess.PerformLayout();
            cardFloatingPnL.ResumeLayout(false);
            cardFloatingPnL.PerformLayout();
            cardRealizedPnL.ResumeLayout(false);
            cardRealizedPnL.PerformLayout();
            cardBalance.ResumeLayout(false);
            cardBalance.PerformLayout();
            cardInitMargin.ResumeLayout(false);
            cardInitMargin.PerformLayout();
            cardMaintMargin.ResumeLayout(false);
            cardMaintMargin.PerformLayout();
            cardMarginCall.ResumeLayout(false);
            cardMarginCall.PerformLayout();
            cardWithdraw.ResumeLayout(false);
            cardWithdraw.PerformLayout();
            cardFeeTax.ResumeLayout(false);
            cardFeeTax.PerformLayout();
            cardPremium.ResumeLayout(false);
            cardPremium.PerformLayout();
            pnlBottom.ResumeLayout(false);
            pnlBottom.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private Panel pnlTop;
        private Label lblAccount;
        private Button btnRefresh;
        private TableLayoutPanel pnlCards;
        private Panel cardEquity;
        private Label lblEquityTitle;
        private Label lblEquityVal;
        private Panel cardRisk;
        private Label lblRiskTitle;
        private Label lblRiskVal;
        private Panel cardExcess;
        private Label lblExcessTitle;
        private Label lblExcessVal;
        private Panel cardFloatingPnL;
        private Label lblFloatingTitle;
        private Label lblFloatingVal;
        private Panel cardRealizedPnL;
        private Label lblRealizedTitle;
        private Label lblRealizedVal;
        private Panel cardBalance;
        private Label lblBalanceTitle;
        private Label lblBalanceVal;
        private Panel cardInitMargin;
        private Label lblInitMarginTitle;
        private Label lblInitMarginVal;
        private Panel cardMaintMargin;
        private Label lblMaintMarginTitle;
        private Label lblMaintMarginVal;
        private Panel cardMarginCall;
        private Label lblMarginCallTitle;
        private Label lblMarginCallVal;
        private Panel cardWithdraw;
        private Label lblWithdrawTitle;
        private Label lblWithdrawVal;
        private Panel cardFeeTax;
        private Label lblFeeTaxTitle;
        private Label lblFeeTaxVal;
        private Panel cardPremium;
        private Label lblPremiumTitle;
        private Label lblPremiumVal;
        private Panel pnlBottom;
        private Label lblStatus;
    }
}
