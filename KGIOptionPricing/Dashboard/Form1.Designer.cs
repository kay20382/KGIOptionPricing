using System.Drawing;
using System.Windows.Forms;

namespace KGIOptionPricing
{
    partial class Form1
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
            menuStrip = new MenuStrip();
            tsmFile = new ToolStripMenuItem();
            tsmExit = new ToolStripMenuItem();
            tsmPricing = new ToolStripMenuItem();
            tsmOptionPricing = new ToolStripMenuItem();
            tsmOrderPlacement = new ToolStripMenuItem();
            tsmLightningOrder = new ToolStripMenuItem();
            tsmQueueFollow = new ToolStripMenuItem();
            tsmSimulation = new ToolStripMenuItem();
            tsmLiveStrategy = new ToolStripMenuItem();
            tsmReports = new ToolStripMenuItem();
            tsmOrderReport = new ToolStripMenuItem();
            tsmDealReport = new ToolStripMenuItem();
            tsmPosition = new ToolStripMenuItem();
            tsmMarginEquity = new ToolStripMenuItem();
            toolStrip = new ToolStrip();
            btnTsPricing = new ToolStripButton();
            btnTsOrderPlacement = new ToolStripButton();
            btnTsLightningOrder = new ToolStripButton();
            toolStripSeparator1 = new ToolStripSeparator();
            btnTsOrderReport = new ToolStripButton();
            btnTsDealReport = new ToolStripButton();
            btnTsPosition = new ToolStripButton();
            btnTsMargin = new ToolStripButton();
            pnlStatus = new Panel();
            cardRiskData = new Panel();
            lblRiskDataStatus = new Label();
            lblRiskDataTitle = new Label();
            cardFutures = new Panel();
            lblFuturesPrice = new Label();
            lblFuturesTitle = new Label();
            cardTrade = new Panel();
            lblTradeStatus = new Label();
            lblTradeTitle = new Label();
            cardQuote = new Panel();
            lblQuoteStatus = new Label();
            lblQuoteTitle = new Label();
            pnlLogHeader = new Panel();
            chkAutoScroll = new CheckBox();
            btnClearLog = new Button();
            lblLogTitle = new Label();
            rtbLog = new RichTextBox();
            pnlLog = new Panel();
            menuStrip.SuspendLayout();
            toolStrip.SuspendLayout();
            pnlStatus.SuspendLayout();
            cardRiskData.SuspendLayout();
            cardFutures.SuspendLayout();
            cardTrade.SuspendLayout();
            cardQuote.SuspendLayout();
            pnlLogHeader.SuspendLayout();
            pnlLog.SuspendLayout();
            SuspendLayout();
            // 
            // menuStrip
            // 
            menuStrip.BackColor = Color.FromArgb(32, 32, 35);
            menuStrip.Items.AddRange(new ToolStripItem[] { tsmFile, tsmPricing, tsmReports });
            menuStrip.Location = new Point(0, 0);
            menuStrip.Name = "menuStrip";
            menuStrip.Size = new Size(1100, 24);
            menuStrip.TabIndex = 0;
            // 
            // tsmFile
            // 
            tsmFile.DropDownItems.AddRange(new ToolStripItem[] { tsmExit });
            tsmFile.ForeColor = Color.White;
            tsmFile.Name = "tsmFile";
            tsmFile.Size = new Size(58, 20);
            tsmFile.Text = "檔案 (&F)";
            // 
            // tsmExit
            // 
            tsmExit.Name = "tsmExit";
            tsmExit.Size = new Size(118, 22);
            tsmExit.Text = "結束離開";
            tsmExit.Click += tsmExit_Click;
            // 
            // tsmPricing
            // 
            tsmPricing.DropDownItems.AddRange(new ToolStripItem[] { tsmOptionPricing, tsmOrderPlacement, tsmLightningOrder, tsmQueueFollow, tsmSimulation, tsmLiveStrategy });
            tsmPricing.ForeColor = Color.White;
            tsmPricing.Name = "tsmPricing";
            tsmPricing.Size = new Size(82, 20);
            tsmPricing.Text = "交易與報價 (&M)";
            // 
            // tsmOptionPricing
            // 
            tsmOptionPricing.Name = "tsmOptionPricing";
            tsmOptionPricing.Size = new Size(211, 22);
            tsmOptionPricing.Text = "📊 選擇權定價與 T 報價鏈";
            tsmOptionPricing.Click += btnTsPricing_Click;
            // 
            // tsmOrderPlacement
            // 
            tsmOrderPlacement.Name = "tsmOrderPlacement";
            tsmOrderPlacement.Size = new Size(211, 22);
            tsmOrderPlacement.Text = "🎯 智能下單匣";
            tsmOrderPlacement.Click += btnTsOrderPlacement_Click;
            // 
            // tsmLightningOrder
            // 
            tsmLightningOrder.Name = "tsmLightningOrder";
            tsmLightningOrder.Size = new Size(211, 22);
            tsmLightningOrder.Text = "⚡ 閃電下單";
            tsmLightningOrder.Click += btnTsLightningOrder_Click;
            // 
            // tsmQueueFollow
            // 
            tsmQueueFollow.Name = "tsmQueueFollow";
            tsmQueueFollow.Size = new Size(246, 22);
            tsmQueueFollow.Text = "🧭 價量跟隨完成單（測試）";
            tsmQueueFollow.Click += tsmQueueFollow_Click;
            // 
            // tsmSimulation
            // 
            tsmSimulation.Name = "tsmSimulation";
            tsmSimulation.Size = new Size(246, 22);
            tsmSimulation.Text = "🧪 多策略中心（模擬／回測）";
            tsmSimulation.Click += tsmSimulation_Click;
            // 
            // tsmLiveStrategy
            // 
            tsmLiveStrategy.ForeColor = Color.DarkRed;
            tsmLiveStrategy.Name = "tsmLiveStrategy";
            tsmLiveStrategy.Size = new Size(246, 22);
            tsmLiveStrategy.Text = "⚠ 多策略中心（實盤交易）";
            tsmLiveStrategy.Click += tsmLiveStrategy_Click;
            // 
            // tsmReports
            // 
            tsmReports.DropDownItems.AddRange(new ToolStripItem[] { tsmOrderReport, tsmDealReport, tsmPosition, tsmMarginEquity });
            tsmReports.ForeColor = Color.White;
            tsmReports.Name = "tsmReports";
            tsmReports.Size = new Size(80, 20);
            tsmReports.Text = "帳務回報 (&A)";
            // 
            // tsmOrderReport
            // 
            tsmOrderReport.Name = "tsmOrderReport";
            tsmOrderReport.Size = new Size(163, 22);
            tsmOrderReport.Text = "📋 委託回報";
            tsmOrderReport.Click += btnTsOrderReport_Click;
            // 
            // tsmDealReport
            // 
            tsmDealReport.Name = "tsmDealReport";
            tsmDealReport.Size = new Size(163, 22);
            tsmDealReport.Text = "💰 成交回報";
            tsmDealReport.Click += btnTsDealReport_Click;
            // 
            // tsmPosition
            // 
            tsmPosition.Name = "tsmPosition";
            tsmPosition.Size = new Size(163, 22);
            tsmPosition.Text = "🎯 未平倉部位";
            tsmPosition.Click += btnTsPosition_Click;
            // 
            // tsmMarginEquity
            // 
            tsmMarginEquity.Name = "tsmMarginEquity";
            tsmMarginEquity.Size = new Size(163, 22);
            tsmMarginEquity.Text = "🏦 權益數與保證金";
            tsmMarginEquity.Click += btnTsMargin_Click;
            // 
            // toolStrip
            // 
            toolStrip.BackColor = Color.FromArgb(24, 24, 26);
            toolStrip.GripStyle = ToolStripGripStyle.Hidden;
            toolStrip.ImageScalingSize = new Size(24, 24);
            toolStrip.Items.AddRange(new ToolStripItem[] { btnTsPricing, btnTsOrderPlacement, btnTsLightningOrder, toolStripSeparator1, btnTsOrderReport, btnTsDealReport, btnTsPosition, btnTsMargin });
            toolStrip.Location = new Point(0, 24);
            toolStrip.Name = "toolStrip";
            toolStrip.Padding = new Padding(10, 4, 10, 4);
            toolStrip.Size = new Size(1100, 43);
            toolStrip.TabIndex = 1;
            // 
            // btnTsPricing
            // 
            btnTsPricing.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            btnTsPricing.ForeColor = Color.White;
            btnTsPricing.ImageTransparentColor = Color.Magenta;
            btnTsPricing.Margin = new Padding(0, 1, 5, 2);
            btnTsPricing.Name = "btnTsPricing";
            btnTsPricing.Padding = new Padding(6, 4, 6, 4);
            btnTsPricing.Size = new Size(149, 32);
            btnTsPricing.Text = "📊 選擇權定價 (T鏈)";
            btnTsPricing.Click += btnTsPricing_Click;
            // 
            // btnTsOrderPlacement
            // 
            btnTsOrderPlacement.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            btnTsOrderPlacement.ForeColor = Color.FromArgb(0, 210, 255);
            btnTsOrderPlacement.ImageTransparentColor = Color.Magenta;
            btnTsOrderPlacement.Margin = new Padding(5, 1, 5, 2);
            btnTsOrderPlacement.Name = "btnTsOrderPlacement";
            btnTsOrderPlacement.Padding = new Padding(6, 4, 6, 4);
            btnTsOrderPlacement.Size = new Size(118, 32);
            btnTsOrderPlacement.Text = "🎯 智能下單匣";
            btnTsOrderPlacement.Click += btnTsOrderPlacement_Click;
            // 
            // btnTsLightningOrder
            // 
            btnTsLightningOrder.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            btnTsLightningOrder.ForeColor = Color.Gold;
            btnTsLightningOrder.Margin = new Padding(5, 1, 5, 2);
            btnTsLightningOrder.Name = "btnTsLightningOrder";
            btnTsLightningOrder.Padding = new Padding(6, 4, 6, 4);
            btnTsLightningOrder.Size = new Size(102, 32);
            btnTsLightningOrder.Text = "⚡ 閃電下單";
            btnTsLightningOrder.Click += btnTsLightningOrder_Click;
            // 
            // toolStripSeparator1
            // 
            toolStripSeparator1.Name = "toolStripSeparator1";
            toolStripSeparator1.Size = new Size(6, 35);
            // 
            // btnTsOrderReport
            // 
            btnTsOrderReport.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            btnTsOrderReport.ForeColor = Color.White;
            btnTsOrderReport.ImageTransparentColor = Color.Magenta;
            btnTsOrderReport.Margin = new Padding(5, 1, 5, 2);
            btnTsOrderReport.Name = "btnTsOrderReport";
            btnTsOrderReport.Padding = new Padding(6, 4, 6, 4);
            btnTsOrderReport.Size = new Size(106, 32);
            btnTsOrderReport.Text = "📋 委託回報";
            btnTsOrderReport.Click += btnTsOrderReport_Click;
            // 
            // btnTsDealReport
            // 
            btnTsDealReport.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            btnTsDealReport.ForeColor = Color.White;
            btnTsDealReport.ImageTransparentColor = Color.Magenta;
            btnTsDealReport.Margin = new Padding(5, 1, 5, 2);
            btnTsDealReport.Name = "btnTsDealReport";
            btnTsDealReport.Padding = new Padding(6, 4, 6, 4);
            btnTsDealReport.Size = new Size(106, 32);
            btnTsDealReport.Text = "💰 成交回報";
            btnTsDealReport.Click += btnTsDealReport_Click;
            // 
            // btnTsPosition
            // 
            btnTsPosition.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            btnTsPosition.ForeColor = Color.White;
            btnTsPosition.ImageTransparentColor = Color.Magenta;
            btnTsPosition.Margin = new Padding(5, 1, 5, 2);
            btnTsPosition.Name = "btnTsPosition";
            btnTsPosition.Padding = new Padding(6, 4, 6, 4);
            btnTsPosition.Size = new Size(120, 32);
            btnTsPosition.Text = "🎯 未平倉部位";
            btnTsPosition.Click += btnTsPosition_Click;
            // 
            // btnTsMargin
            // 
            btnTsMargin.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            btnTsMargin.ForeColor = Color.White;
            btnTsMargin.ImageTransparentColor = Color.Magenta;
            btnTsMargin.Margin = new Padding(5, 1, 5, 2);
            btnTsMargin.Name = "btnTsMargin";
            btnTsMargin.Padding = new Padding(6, 4, 6, 4);
            btnTsMargin.Size = new Size(106, 32);
            btnTsMargin.Text = "🏦 權益查詢";
            btnTsMargin.Click += btnTsMargin_Click;
            // 
            // pnlStatus
            // 
            pnlStatus.BackColor = Color.FromArgb(20, 20, 22);
            pnlStatus.Controls.Add(cardRiskData);
            pnlStatus.Controls.Add(cardFutures);
            pnlStatus.Controls.Add(cardTrade);
            pnlStatus.Controls.Add(cardQuote);
            pnlStatus.Dock = DockStyle.Top;
            pnlStatus.Location = new Point(0, 67);
            pnlStatus.Name = "pnlStatus";
            pnlStatus.Padding = new Padding(12);
            pnlStatus.Size = new Size(1100, 105);
            pnlStatus.TabIndex = 2;
            // 
            // cardRiskData
            // 
            cardRiskData.BackColor = Color.FromArgb(32, 32, 36);
            cardRiskData.Controls.Add(lblRiskDataStatus);
            cardRiskData.Controls.Add(lblRiskDataTitle);
            cardRiskData.Location = new Point(810, 12);
            cardRiskData.Name = "cardRiskData";
            cardRiskData.Size = new Size(260, 80);
            cardRiskData.TabIndex = 3;
            // 
            // lblRiskDataStatus
            // 
            lblRiskDataStatus.AutoSize = true;
            lblRiskDataStatus.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            lblRiskDataStatus.ForeColor = Color.DarkGray;
            lblRiskDataStatus.Location = new Point(15, 42);
            lblRiskDataStatus.Name = "lblRiskDataStatus";
            lblRiskDataStatus.Size = new Size(95, 20);
            lblRiskDataStatus.TabIndex = 1;
            lblRiskDataStatus.Text = "定時 15 秒更新";
            // 
            // lblRiskDataTitle
            // 
            lblRiskDataTitle.AutoSize = true;
            lblRiskDataTitle.Font = new Font("Segoe UI", 9.5F);
            lblRiskDataTitle.ForeColor = Color.DarkGray;
            lblRiskDataTitle.Location = new Point(15, 14);
            lblRiskDataTitle.Name = "lblRiskDataTitle";
            lblRiskDataTitle.Size = new Size(130, 17);
            lblRiskDataTitle.TabIndex = 0;
            lblRiskDataTitle.Text = "⚡ 風控資料匯出狀態";
            // 
            // cardFutures
            // 
            cardFutures.BackColor = Color.FromArgb(32, 32, 36);
            cardFutures.Controls.Add(lblFuturesPrice);
            cardFutures.Controls.Add(lblFuturesTitle);
            cardFutures.Location = new Point(544, 12);
            cardFutures.Name = "cardFutures";
            cardFutures.Size = new Size(250, 80);
            cardFutures.TabIndex = 2;
            // 
            // lblFuturesPrice
            // 
            lblFuturesPrice.AutoSize = true;
            lblFuturesPrice.Font = new Font("Segoe UI", 15F, FontStyle.Bold);
            lblFuturesPrice.ForeColor = Color.Gold;
            lblFuturesPrice.Location = new Point(15, 38);
            lblFuturesPrice.Name = "lblFuturesPrice";
            lblFuturesPrice.Size = new Size(24, 28);
            lblFuturesPrice.TabIndex = 1;
            lblFuturesPrice.Text = "--";
            // 
            // lblFuturesTitle
            // 
            lblFuturesTitle.AutoSize = true;
            lblFuturesTitle.Font = new Font("Segoe UI", 9.5F);
            lblFuturesTitle.ForeColor = Color.DarkGray;
            lblFuturesTitle.Location = new Point(15, 14);
            lblFuturesTitle.Name = "lblFuturesTitle";
            lblFuturesTitle.Size = new Size(104, 17);
            lblFuturesTitle.TabIndex = 0;
            lblFuturesTitle.Text = "📈 台指期貨現價";
            // 
            // cardTrade
            // 
            cardTrade.BackColor = Color.FromArgb(32, 32, 36);
            cardTrade.Controls.Add(lblTradeStatus);
            cardTrade.Controls.Add(lblTradeTitle);
            cardTrade.Location = new Point(278, 12);
            cardTrade.Name = "cardTrade";
            cardTrade.Size = new Size(250, 80);
            cardTrade.TabIndex = 1;
            // 
            // lblTradeStatus
            // 
            lblTradeStatus.AutoSize = true;
            lblTradeStatus.Font = new Font("Segoe UI", 13F, FontStyle.Bold);
            lblTradeStatus.ForeColor = Color.Orange;
            lblTradeStatus.Location = new Point(15, 40);
            lblTradeStatus.Name = "lblTradeStatus";
            lblTradeStatus.Size = new Size(107, 25);
            lblTradeStatus.TabIndex = 1;
            lblTradeStatus.Text = "🟠 連線中...";
            // 
            // lblTradeTitle
            // 
            lblTradeTitle.AutoSize = true;
            lblTradeTitle.Font = new Font("Segoe UI", 9.5F);
            lblTradeTitle.ForeColor = Color.DarkGray;
            lblTradeTitle.Location = new Point(15, 14);
            lblTradeTitle.Name = "lblTradeTitle";
            lblTradeTitle.Size = new Size(174, 17);
            lblTradeTitle.TabIndex = 0;
            lblTradeTitle.Text = "🔑 凱基交易 (TradeCom API)";
            // 
            // cardQuote
            // 
            cardQuote.BackColor = Color.FromArgb(32, 32, 36);
            cardQuote.Controls.Add(lblQuoteStatus);
            cardQuote.Controls.Add(lblQuoteTitle);
            cardQuote.Location = new Point(12, 12);
            cardQuote.Name = "cardQuote";
            cardQuote.Size = new Size(250, 80);
            cardQuote.TabIndex = 0;
            // 
            // lblQuoteStatus
            // 
            lblQuoteStatus.AutoSize = true;
            lblQuoteStatus.Font = new Font("Segoe UI", 13F, FontStyle.Bold);
            lblQuoteStatus.ForeColor = Color.Orange;
            lblQuoteStatus.Location = new Point(15, 40);
            lblQuoteStatus.Name = "lblQuoteStatus";
            lblQuoteStatus.Size = new Size(107, 25);
            lblQuoteStatus.TabIndex = 1;
            lblQuoteStatus.Text = "🟠 連線中...";
            // 
            // lblQuoteTitle
            // 
            lblQuoteTitle.AutoSize = true;
            lblQuoteTitle.Font = new Font("Segoe UI", 9.5F);
            lblQuoteTitle.ForeColor = Color.DarkGray;
            lblQuoteTitle.Location = new Point(15, 14);
            lblQuoteTitle.Name = "lblQuoteTitle";
            lblQuoteTitle.Size = new Size(179, 17);
            lblQuoteTitle.TabIndex = 0;
            lblQuoteTitle.Text = "📡 凱基行情 (QuoteCom API)";
            // 
            // pnlLogHeader
            // 
            pnlLogHeader.BackColor = Color.FromArgb(26, 26, 28);
            pnlLogHeader.Controls.Add(chkAutoScroll);
            pnlLogHeader.Controls.Add(btnClearLog);
            pnlLogHeader.Controls.Add(lblLogTitle);
            pnlLogHeader.Dock = DockStyle.Top;
            pnlLogHeader.Location = new Point(0, 172);
            pnlLogHeader.Name = "pnlLogHeader";
            pnlLogHeader.Padding = new Padding(10, 0, 10, 0);
            pnlLogHeader.Size = new Size(1100, 38);
            pnlLogHeader.TabIndex = 3;
            // 
            // chkAutoScroll
            // 
            chkAutoScroll.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            chkAutoScroll.AutoSize = true;
            chkAutoScroll.Checked = true;
            chkAutoScroll.CheckState = CheckState.Checked;
            chkAutoScroll.ForeColor = Color.White;
            chkAutoScroll.Location = new Point(885, 9);
            chkAutoScroll.Name = "chkAutoScroll";
            chkAutoScroll.Size = new Size(98, 23);
            chkAutoScroll.TabIndex = 2;
            chkAutoScroll.Text = "自動滾動日誌";
            chkAutoScroll.UseVisualStyleBackColor = true;
            // 
            // btnClearLog
            // 
            btnClearLog.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnClearLog.BackColor = Color.FromArgb(45, 45, 48);
            btnClearLog.FlatStyle = FlatStyle.Flat;
            btnClearLog.ForeColor = Color.White;
            btnClearLog.Location = new Point(995, 4);
            btnClearLog.Name = "btnClearLog";
            btnClearLog.Size = new Size(95, 30);
            btnClearLog.TabIndex = 1;
            btnClearLog.Text = "🧹 清空 Log";
            btnClearLog.UseVisualStyleBackColor = false;
            btnClearLog.Click += btnClearLog_Click;
            // 
            // lblLogTitle
            // 
            lblLogTitle.AutoSize = true;
            lblLogTitle.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            lblLogTitle.ForeColor = Color.White;
            lblLogTitle.Location = new Point(12, 9);
            lblLogTitle.Name = "lblLogTitle";
            lblLogTitle.Size = new Size(147, 19);
            lblLogTitle.TabIndex = 0;
            lblLogTitle.Text = "📜 即時系統 Log 監控";
            // 
            // rtbLog
            // 
            rtbLog.BackColor = Color.FromArgb(14, 14, 16);
            rtbLog.BorderStyle = BorderStyle.None;
            rtbLog.Dock = DockStyle.Fill;
            rtbLog.Font = new Font("Consolas", 10F);
            rtbLog.ForeColor = Color.FromArgb(220, 220, 220);
            rtbLog.Location = new Point(0, 0);
            rtbLog.Name = "rtbLog";
            rtbLog.ReadOnly = true;
            rtbLog.ScrollBars = RichTextBoxScrollBars.Vertical;
            rtbLog.Size = new Size(1100, 440);
            rtbLog.TabIndex = 0;
            rtbLog.Text = "";
            // 
            // pnlLog
            // 
            pnlLog.Controls.Add(rtbLog);
            pnlLog.Dock = DockStyle.Fill;
            pnlLog.Location = new Point(0, 210);
            pnlLog.Name = "pnlLog";
            pnlLog.Size = new Size(1100, 440);
            pnlLog.TabIndex = 4;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(18, 18, 18);
            ClientSize = new Size(1100, 650);
            Controls.Add(pnlLog);
            Controls.Add(pnlLogHeader);
            Controls.Add(pnlStatus);
            Controls.Add(toolStrip);
            Controls.Add(menuStrip);
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
            MainMenuStrip = menuStrip;
            Name = "Form1";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "KGIOptionPricing - 凱基期權定價與造市交易系統";
            menuStrip.ResumeLayout(false);
            menuStrip.PerformLayout();
            toolStrip.ResumeLayout(false);
            toolStrip.PerformLayout();
            pnlStatus.ResumeLayout(false);
            cardRiskData.ResumeLayout(false);
            cardRiskData.PerformLayout();
            cardFutures.ResumeLayout(false);
            cardFutures.PerformLayout();
            cardTrade.ResumeLayout(false);
            cardTrade.PerformLayout();
            cardQuote.ResumeLayout(false);
            cardQuote.PerformLayout();
            pnlLogHeader.ResumeLayout(false);
            pnlLogHeader.PerformLayout();
            pnlLog.ResumeLayout(false);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private MenuStrip menuStrip;
        private ToolStripMenuItem tsmFile;
        private ToolStripMenuItem tsmExit;
        private ToolStripMenuItem tsmPricing;
        private ToolStripMenuItem tsmOptionPricing;
        private ToolStripMenuItem tsmOrderPlacement;
        private ToolStripMenuItem tsmLightningOrder;
        private ToolStripMenuItem tsmQueueFollow;
        private ToolStripMenuItem tsmSimulation;
        private ToolStripMenuItem tsmLiveStrategy;
        private ToolStripMenuItem tsmReports;
        private ToolStripMenuItem tsmOrderReport;
        private ToolStripMenuItem tsmDealReport;
        private ToolStripMenuItem tsmPosition;
        private ToolStripMenuItem tsmMarginEquity;
        private ToolStrip toolStrip;
        private ToolStripButton btnTsPricing;
        private ToolStripButton btnTsOrderPlacement;
        private ToolStripButton btnTsLightningOrder;
        private ToolStripSeparator toolStripSeparator1;
        private ToolStripButton btnTsOrderReport;
        private ToolStripButton btnTsDealReport;
        private ToolStripButton btnTsPosition;
        private ToolStripButton btnTsMargin;
        private Panel pnlStatus;
        private Panel cardQuote;
        private Label lblQuoteTitle;
        private Label lblQuoteStatus;
        private Panel cardTrade;
        private Label lblTradeTitle;
        private Label lblTradeStatus;
        private Panel cardFutures;
        private Label lblFuturesTitle;
        private Label lblFuturesPrice;
        private Panel cardRiskData;
        private Label lblRiskDataTitle;
        private Label lblRiskDataStatus;
        private Panel pnlLogHeader;
        private Label lblLogTitle;
        private Button btnClearLog;
        private CheckBox chkAutoScroll;
        private RichTextBox rtbLog;
        private Panel pnlLog;
    }
}
