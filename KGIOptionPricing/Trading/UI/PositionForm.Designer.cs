using System.Drawing;
using System.Windows.Forms;

namespace KGIOptionPricing
{
    partial class PositionForm
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
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle1 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle2 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle3 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle4 = new System.Windows.Forms.DataGridViewCellStyle();
            pnlTop = new Panel();
            btnRefresh = new Button();
            btnCloseSelected = new Button();
            btnCloseAll = new Button();
            lblDelta = new Label();
            lblTotalQty = new Label();
            lblTotalPnL = new Label();
            tabControl = new TabControl();
            _marketTabs = new TabControl();
            domesticPage = new TabPage();
            foreignPage = new TabPage();
            _foreignTabs = new TabControl();
            foreignSummaryPage = new TabPage();
            foreignDetailPage = new TabPage();
            _foreignSummary = new DataGridView();
            _foreignDetail = new DataGridView();
            fsExchange = new DataGridViewTextBoxColumn();
            fsCommodity = new DataGridViewTextBoxColumn();
            fsMonth = new DataGridViewTextBoxColumn();
            fsStrike = new DataGridViewTextBoxColumn();
            fsCp = new DataGridViewTextBoxColumn();
            fsSide = new DataGridViewTextBoxColumn();
            fsQty = new DataGridViewTextBoxColumn();
            fsAvg = new DataGridViewTextBoxColumn();
            fsMarket = new DataGridViewTextBoxColumn();
            fsPnl = new DataGridViewTextBoxColumn();
            fsCurrency = new DataGridViewTextBoxColumn();
            fdDate = new DataGridViewTextBoxColumn();
            fdExchange = new DataGridViewTextBoxColumn();
            fdOrdno = new DataGridViewTextBoxColumn();
            fdMarketno = new DataGridViewTextBoxColumn();
            fdCommodity = new DataGridViewTextBoxColumn();
            fdSide = new DataGridViewTextBoxColumn();
            fdQty = new DataGridViewTextBoxColumn();
            fdDeal = new DataGridViewTextBoxColumn();
            fdMarket = new DataGridViewTextBoxColumn();
            fdPnl = new DataGridViewTextBoxColumn();
            fdInitial = new DataGridViewTextBoxColumn();
            fdMaintenance = new DataGridViewTextBoxColumn();
            fdCurrency = new DataGridViewTextBoxColumn();
            tabSummary = new TabPage();
            dgvSummary = new DataGridView();
            colSumCom = new DataGridViewTextBoxColumn();
            colSumMonth = new DataGridViewTextBoxColumn();
            colSumStrike = new DataGridViewTextBoxColumn();
            colSumCP = new DataGridViewTextBoxColumn();
            colSumBS = new DataGridViewTextBoxColumn();
            colSumQty = new DataGridViewTextBoxColumn();
            colSumAvgPrice = new DataGridViewTextBoxColumn();
            colSumMarketPrice = new DataGridViewTextBoxColumn();
            colSumPnL = new DataGridViewTextBoxColumn();
            colSumDelta = new DataGridViewTextBoxColumn();
            tabDetail = new TabPage();
            dgvDetail = new DataGridView();
            colDetDate = new DataGridViewTextBoxColumn();
            colDetOrdNo = new DataGridViewTextBoxColumn();
            colDetSeq = new DataGridViewTextBoxColumn();
            colDetCom = new DataGridViewTextBoxColumn();
            colDetBS = new DataGridViewTextBoxColumn();
            colDetQty = new DataGridViewTextBoxColumn();
            colDetDealPrice = new DataGridViewTextBoxColumn();
            colDetMPrice = new DataGridViewTextBoxColumn();
            colDetPnL = new DataGridViewTextBoxColumn();
            colDetInitMargin = new DataGridViewTextBoxColumn();
            colDetSpKey = new DataGridViewTextBoxColumn();
            pnlBottom = new Panel();
            lblStatus = new Label();
            pnlTop.SuspendLayout();
            tabControl.SuspendLayout();
            tabSummary.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)dgvSummary).BeginInit();
            tabDetail.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)dgvDetail).BeginInit();
            pnlBottom.SuspendLayout();
            SuspendLayout();
            // 
            // pnlTop
            // 
            pnlTop.BackColor = Color.FromArgb(28, 28, 28);
            pnlTop.Controls.Add(btnRefresh);
            pnlTop.Controls.Add(btnCloseSelected);
            pnlTop.Controls.Add(btnCloseAll);
            pnlTop.Controls.Add(lblDelta);
            pnlTop.Controls.Add(lblTotalQty);
            pnlTop.Controls.Add(lblTotalPnL);
            pnlTop.Dock = DockStyle.Top;
            pnlTop.Location = new Point(0, 0);
            pnlTop.Name = "pnlTop";
            pnlTop.Size = new Size(1150, 65);
            pnlTop.TabIndex = 0;
            // 
            // btnRefresh
            // 
            btnRefresh.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnRefresh.BackColor = Color.FromArgb(45, 45, 48);
            btnRefresh.FlatStyle = FlatStyle.Flat;
            btnRefresh.ForeColor = Color.White;
            btnRefresh.Location = new Point(1030, 15);
            btnRefresh.Name = "btnRefresh";
            btnRefresh.Size = new Size(105, 35);
            btnRefresh.TabIndex = 5;
            btnRefresh.Text = "🔄 查詢部位";
            btnRefresh.UseVisualStyleBackColor = false;
            btnRefresh.Click += btnRefresh_Click;
            // 
            // btnCloseSelected
            // 
            btnCloseSelected.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnCloseSelected.BackColor = Color.FromArgb(180, 80, 0);
            btnCloseSelected.FlatStyle = FlatStyle.Flat;
            btnCloseSelected.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
            btnCloseSelected.ForeColor = Color.White;
            btnCloseSelected.Location = new Point(905, 15);
            btnCloseSelected.Name = "btnCloseSelected";
            btnCloseSelected.Size = new Size(115, 35);
            btnCloseSelected.TabIndex = 4;
            btnCloseSelected.Text = "🎯 選取平倉";
            btnCloseSelected.UseVisualStyleBackColor = false;
            btnCloseSelected.Click += btnCloseSelected_Click;
            // 
            // btnCloseAll
            // 
            btnCloseAll.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnCloseAll.BackColor = Color.FromArgb(180, 0, 0);
            btnCloseAll.FlatStyle = FlatStyle.Flat;
            btnCloseAll.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
            btnCloseAll.ForeColor = Color.White;
            btnCloseAll.Location = new Point(780, 15);
            btnCloseAll.Name = "btnCloseAll";
            btnCloseAll.Size = new Size(115, 35);
            btnCloseAll.TabIndex = 3;
            btnCloseAll.Text = "💥 市價全平";
            btnCloseAll.UseVisualStyleBackColor = false;
            btnCloseAll.Click += btnCloseAll_Click;
            // 
            // lblDelta
            // 
            lblDelta.AutoSize = true;
            lblDelta.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            lblDelta.ForeColor = Color.DeepSkyBlue;
            lblDelta.Location = new Point(480, 23);
            lblDelta.Name = "lblDelta";
            lblDelta.Size = new Size(130, 19);
            lblDelta.TabIndex = 2;
            lblDelta.Text = "部位 Delta: +0.00 口";
            // 
            // lblTotalQty
            // 
            lblTotalQty.AutoSize = true;
            lblTotalQty.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            lblTotalQty.ForeColor = Color.White;
            lblTotalQty.Location = new Point(270, 23);
            lblTotalQty.Name = "lblTotalQty";
            lblTotalQty.Size = new Size(160, 19);
            lblTotalQty.TabIndex = 1;
            lblTotalQty.Text = "總持倉: 0 口 (多 0 / 空 0)";
            // 
            // lblTotalPnL
            // 
            lblTotalPnL.AutoSize = true;
            lblTotalPnL.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
            lblTotalPnL.ForeColor = Color.LimeGreen;
            lblTotalPnL.Location = new Point(20, 21);
            lblTotalPnL.Name = "lblTotalPnL";
            lblTotalPnL.Size = new Size(198, 21);
            lblTotalPnL.TabIndex = 0;
            lblTotalPnL.Text = "未實現總損益: NT$ 0";
            // 
            // tabControl
            // 
            tabControl.Controls.Add(tabSummary);
            tabControl.Controls.Add(tabDetail);
            tabControl.Dock = DockStyle.Fill;
            tabControl.Location = new Point(0, 65);
            tabControl.Name = "tabControl";
            tabControl.SelectedIndex = 0;
            tabControl.Size = new Size(1150, 500);
            tabControl.TabIndex = 1;
            // 
            // tabSummary
            // 
            tabSummary.BackColor = Color.FromArgb(18, 18, 18);
            tabSummary.Controls.Add(dgvSummary);
            tabSummary.Location = new Point(4, 26);
            tabSummary.Name = "tabSummary";
            tabSummary.Padding = new Padding(3);
            tabSummary.Size = new Size(1142, 470);
            tabSummary.TabIndex = 0;
            tabSummary.Text = " 📊 彙總部位 (P001616) ";
            // 
            // dgvSummary
            // 
            dgvSummary.AllowUserToAddRows = false;
            dgvSummary.AllowUserToDeleteRows = false;
            dgvSummary.AllowUserToResizeRows = false;
            dgvSummary.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dgvSummary.BackgroundColor = Color.FromArgb(18, 18, 18);
            dgvSummary.BorderStyle = BorderStyle.None;
            dgvSummary.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            dgvSummary.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            dataGridViewCellStyle1.Alignment = DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle1.BackColor = Color.FromArgb(32, 32, 32);
            dataGridViewCellStyle1.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
            dataGridViewCellStyle1.ForeColor = Color.White;
            dataGridViewCellStyle1.SelectionBackColor = SystemColors.Highlight;
            dataGridViewCellStyle1.SelectionForeColor = SystemColors.HighlightText;
            dataGridViewCellStyle1.WrapMode = DataGridViewTriState.True;
            dgvSummary.ColumnHeadersDefaultCellStyle = dataGridViewCellStyle1;
            dgvSummary.ColumnHeadersHeight = 35;
            dgvSummary.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            dgvSummary.Columns.AddRange(new DataGridViewColumn[] { colSumCom, colSumMonth, colSumStrike, colSumCP, colSumBS, colSumQty, colSumAvgPrice, colSumMarketPrice, colSumPnL, colSumDelta });
            dataGridViewCellStyle2.Alignment = DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle2.BackColor = Color.FromArgb(18, 18, 18);
            dataGridViewCellStyle2.Font = new Font("Consolas", 9.5F);
            dataGridViewCellStyle2.ForeColor = Color.FromArgb(220, 220, 220);
            dataGridViewCellStyle2.SelectionBackColor = Color.FromArgb(0, 100, 150);
            dataGridViewCellStyle2.SelectionForeColor = Color.White;
            dataGridViewCellStyle2.WrapMode = DataGridViewTriState.False;
            dgvSummary.DefaultCellStyle = dataGridViewCellStyle2;
            dgvSummary.Dock = DockStyle.Fill;
            dgvSummary.EnableHeadersVisualStyles = false;
            dgvSummary.GridColor = Color.FromArgb(45, 45, 48);
            dgvSummary.Location = new Point(3, 3);
            dgvSummary.MultiSelect = false;
            dgvSummary.Name = "dgvSummary";
            dgvSummary.ReadOnly = true;
            dgvSummary.RowHeadersVisible = false;
            dgvSummary.RowTemplate.Height = 28;
            dgvSummary.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgvSummary.Size = new Size(1136, 464);
            dgvSummary.TabIndex = 0;
            // 
            // colSumCom
            // 
            colSumCom.FillWeight = 85F;
            colSumCom.HeaderText = "商品";
            colSumCom.Name = "colSumCom";
            colSumCom.ReadOnly = true;
            // 
            // colSumMonth
            // 
            colSumMonth.FillWeight = 80F;
            colSumMonth.HeaderText = "結算年月";
            colSumMonth.Name = "colSumMonth";
            colSumMonth.ReadOnly = true;
            // 
            // colSumStrike
            // 
            colSumStrike.FillWeight = 80F;
            colSumStrike.HeaderText = "履約價";
            colSumStrike.Name = "colSumStrike";
            colSumStrike.ReadOnly = true;
            // 
            // colSumCP
            // 
            colSumCP.FillWeight = 60F;
            colSumCP.HeaderText = "CP";
            colSumCP.Name = "colSumCP";
            colSumCP.ReadOnly = true;
            // 
            // colSumBS
            // 
            colSumBS.FillWeight = 65F;
            colSumBS.HeaderText = "買賣";
            colSumBS.Name = "colSumBS";
            colSumBS.ReadOnly = true;
            // 
            // colSumQty
            // 
            colSumQty.FillWeight = 65F;
            colSumQty.HeaderText = "未平倉口數";
            colSumQty.Name = "colSumQty";
            colSumQty.ReadOnly = true;
            // 
            // colSumAvgPrice
            // 
            colSumAvgPrice.FillWeight = 85F;
            colSumAvgPrice.HeaderText = "成本均價";
            colSumAvgPrice.Name = "colSumAvgPrice";
            colSumAvgPrice.ReadOnly = true;
            // 
            // colSumMarketPrice
            // 
            colSumMarketPrice.FillWeight = 85F;
            colSumMarketPrice.HeaderText = "即時市價";
            colSumMarketPrice.Name = "colSumMarketPrice";
            colSumMarketPrice.ReadOnly = true;
            // 
            // colSumPnL
            // 
            colSumPnL.FillWeight = 95F;
            colSumPnL.HeaderText = "未實現損益(點)";
            colSumPnL.Name = "colSumPnL";
            colSumPnL.ReadOnly = true;
            // 
            // colSumDelta
            // 
            colSumDelta.FillWeight = 80F;
            colSumDelta.HeaderText = "Delta";
            colSumDelta.Name = "colSumDelta";
            colSumDelta.ReadOnly = true;
            // 
            // tabDetail
            // 
            tabDetail.BackColor = Color.FromArgb(18, 18, 18);
            tabDetail.Controls.Add(dgvDetail);
            tabDetail.Location = new Point(4, 26);
            tabDetail.Name = "tabDetail";
            tabDetail.Padding = new Padding(3);
            tabDetail.Size = new Size(1142, 470);
            tabDetail.TabIndex = 1;
            tabDetail.Text = " 📑 分帳明細部位 (P001618) ";
            // 
            // dgvDetail
            // 
            dgvDetail.AllowUserToAddRows = false;
            dgvDetail.AllowUserToDeleteRows = false;
            dgvDetail.AllowUserToResizeRows = false;
            dgvDetail.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dgvDetail.BackgroundColor = Color.FromArgb(18, 18, 18);
            dgvDetail.BorderStyle = BorderStyle.None;
            dgvDetail.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            dgvDetail.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            dataGridViewCellStyle3.Alignment = DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle3.BackColor = Color.FromArgb(32, 32, 32);
            dataGridViewCellStyle3.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
            dataGridViewCellStyle3.ForeColor = Color.White;
            dataGridViewCellStyle3.SelectionBackColor = SystemColors.Highlight;
            dataGridViewCellStyle3.SelectionForeColor = SystemColors.HighlightText;
            dataGridViewCellStyle3.WrapMode = DataGridViewTriState.True;
            dgvDetail.ColumnHeadersDefaultCellStyle = dataGridViewCellStyle3;
            dgvDetail.ColumnHeadersHeight = 35;
            dgvDetail.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            dgvDetail.Columns.AddRange(new DataGridViewColumn[] { colDetDate, colDetOrdNo, colDetSeq, colDetCom, colDetBS, colDetQty, colDetDealPrice, colDetMPrice, colDetPnL, colDetInitMargin, colDetSpKey });
            dataGridViewCellStyle4.Alignment = DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle4.BackColor = Color.FromArgb(18, 18, 18);
            dataGridViewCellStyle4.Font = new Font("Consolas", 9.5F);
            dataGridViewCellStyle4.ForeColor = Color.FromArgb(220, 220, 220);
            dataGridViewCellStyle4.SelectionBackColor = Color.FromArgb(0, 100, 150);
            dataGridViewCellStyle4.SelectionForeColor = Color.White;
            dataGridViewCellStyle4.WrapMode = DataGridViewTriState.False;
            dgvDetail.DefaultCellStyle = dataGridViewCellStyle4;
            dgvDetail.Dock = DockStyle.Fill;
            dgvDetail.EnableHeadersVisualStyles = false;
            dgvDetail.GridColor = Color.FromArgb(45, 45, 48);
            dgvDetail.Location = new Point(3, 3);
            dgvDetail.MultiSelect = false;
            dgvDetail.Name = "dgvDetail";
            dgvDetail.ReadOnly = true;
            dgvDetail.RowHeadersVisible = false;
            dgvDetail.RowTemplate.Height = 28;
            dgvDetail.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgvDetail.Size = new Size(1136, 464);
            dgvDetail.TabIndex = 0;
            // 
            // colDetDate
            // 
            colDetDate.FillWeight = 85F;
            colDetDate.HeaderText = "成交日期";
            colDetDate.Name = "colDetDate";
            colDetDate.ReadOnly = true;
            // 
            // colDetOrdNo
            // 
            colDetOrdNo.FillWeight = 75F;
            colDetOrdNo.HeaderText = "書號";
            colDetOrdNo.Name = "colDetOrdNo";
            colDetOrdNo.ReadOnly = true;
            // 
            // colDetSeq
            // 
            colDetSeq.FillWeight = 75F;
            colDetSeq.HeaderText = "成交序號";
            colDetSeq.Name = "colDetSeq";
            colDetSeq.ReadOnly = true;
            // 
            // colDetCom
            // 
            colDetCom.FillWeight = 110F;
            colDetCom.HeaderText = "商品代碼";
            colDetCom.Name = "colDetCom";
            colDetCom.ReadOnly = true;
            // 
            // colDetBS
            // 
            colDetBS.FillWeight = 60F;
            colDetBS.HeaderText = "買賣";
            colDetBS.Name = "colDetBS";
            colDetBS.ReadOnly = true;
            // 
            // colDetQty
            // 
            colDetQty.FillWeight = 60F;
            colDetQty.HeaderText = "口數";
            colDetQty.Name = "colDetQty";
            colDetQty.ReadOnly = true;
            // 
            // colDetDealPrice
            // 
            colDetDealPrice.FillWeight = 80F;
            colDetDealPrice.HeaderText = "進場價";
            colDetDealPrice.Name = "colDetDealPrice";
            colDetDealPrice.ReadOnly = true;
            // 
            // colDetMPrice
            // 
            colDetMPrice.FillWeight = 80F;
            colDetMPrice.HeaderText = "即時價";
            colDetMPrice.Name = "colDetMPrice";
            colDetMPrice.ReadOnly = true;
            // 
            // colDetPnL
            // 
            colDetPnL.FillWeight = 85F;
            colDetPnL.HeaderText = "損益(點)";
            colDetPnL.Name = "colDetPnL";
            colDetPnL.ReadOnly = true;
            // 
            // colDetInitMargin
            // 
            colDetInitMargin.FillWeight = 90F;
            colDetInitMargin.HeaderText = "原始保證金";
            colDetInitMargin.Name = "colDetInitMargin";
            colDetInitMargin.ReadOnly = true;
            // 
            // colDetSpKey
            // 
            colDetSpKey.FillWeight = 70F;
            colDetSpKey.HeaderText = "複式單號";
            colDetSpKey.Name = "colDetSpKey";
            colDetSpKey.ReadOnly = true;
            // 
            // pnlBottom
            // 
            pnlBottom.BackColor = Color.FromArgb(28, 28, 28);
            pnlBottom.Controls.Add(lblStatus);
            pnlBottom.Dock = DockStyle.Bottom;
            pnlBottom.Location = new Point(0, 565);
            pnlBottom.Name = "pnlBottom";
            pnlBottom.Size = new Size(1150, 35);
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
            lblStatus.Text = "部位最後更新時間: 尚未更新";
            // 
            // 國內／國外部位頁籤
            // 
            tabControl.Dock = DockStyle.Fill;
            tabSummary.Text = "彙總部位 (1616)";
            tabDetail.Text = "部位明細 (1618)";
            domesticPage.BackColor = Color.FromArgb(18, 18, 18);
            domesticPage.Name = "domesticPage";
            domesticPage.Text = "國內部位";
            domesticPage.Controls.Add(tabControl);
            foreignPage.BackColor = Color.FromArgb(18, 18, 18);
            foreignPage.Name = "foreignPage";
            foreignPage.Text = "國外部位";
            foreignSummaryPage.BackColor = Color.FromArgb(18, 18, 18);
            foreignSummaryPage.Name = "foreignSummaryPage";
            foreignSummaryPage.Padding = new Padding(3);
            foreignSummaryPage.Text = "彙總部位 (1616)";
            foreignDetailPage.BackColor = Color.FromArgb(18, 18, 18);
            foreignDetailPage.Name = "foreignDetailPage";
            foreignDetailPage.Padding = new Padding(3);
            foreignDetailPage.Text = "部位明細 (1618)";
            _foreignSummary.Name = "foreignSummary";
            _foreignSummary.Dock = DockStyle.Fill;
            _foreignSummary.ReadOnly = true;
            _foreignSummary.AllowUserToAddRows = false;
            _foreignSummary.AllowUserToDeleteRows = false;
            _foreignSummary.AllowUserToResizeRows = false;
            _foreignSummary.RowHeadersVisible = false;
            _foreignSummary.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _foreignSummary.BackgroundColor = Color.FromArgb(18, 18, 18);
            _foreignSummary.BorderStyle = BorderStyle.None;
            _foreignSummary.GridColor = Color.FromArgb(45, 45, 48);
            _foreignSummary.EnableHeadersVisualStyles = false;
            _foreignSummary.ColumnHeadersHeight = 35;
            _foreignSummary.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(32, 32, 32);
            _foreignSummary.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            _foreignSummary.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft JhengHei UI", 9.5F, FontStyle.Bold);
            _foreignSummary.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            _foreignSummary.DefaultCellStyle.BackColor = Color.FromArgb(18, 18, 18);
            _foreignSummary.DefaultCellStyle.ForeColor = Color.FromArgb(220, 220, 220);
            _foreignSummary.DefaultCellStyle.SelectionBackColor = Color.FromArgb(0, 100, 150);
            _foreignSummary.DefaultCellStyle.SelectionForeColor = Color.White;
            _foreignSummary.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            _foreignSummary.RowTemplate.Height = 30;
            _foreignDetail.Name = "foreignDetail";
            _foreignDetail.Dock = DockStyle.Fill;
            _foreignDetail.ReadOnly = true;
            _foreignDetail.AllowUserToAddRows = false;
            _foreignDetail.AllowUserToDeleteRows = false;
            _foreignDetail.AllowUserToResizeRows = false;
            _foreignDetail.RowHeadersVisible = false;
            _foreignDetail.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _foreignDetail.BackgroundColor = Color.FromArgb(18, 18, 18);
            _foreignDetail.BorderStyle = BorderStyle.None;
            _foreignDetail.GridColor = Color.FromArgb(45, 45, 48);
            _foreignDetail.EnableHeadersVisualStyles = false;
            _foreignDetail.ColumnHeadersHeight = 35;
            _foreignDetail.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(32, 32, 32);
            _foreignDetail.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            _foreignDetail.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft JhengHei UI", 9.5F, FontStyle.Bold);
            _foreignDetail.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            _foreignDetail.DefaultCellStyle.BackColor = Color.FromArgb(18, 18, 18);
            _foreignDetail.DefaultCellStyle.ForeColor = Color.FromArgb(220, 220, 220);
            _foreignDetail.DefaultCellStyle.SelectionBackColor = Color.FromArgb(0, 100, 150);
            _foreignDetail.DefaultCellStyle.SelectionForeColor = Color.White;
            _foreignDetail.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            _foreignDetail.RowTemplate.Height = 30;
            fsExchange.Name = "exchange"; fsExchange.HeaderText = "交易所"; fsExchange.FillWeight = 65;
            fsCommodity.Name = "commodity"; fsCommodity.HeaderText = "商品"; fsCommodity.FillWeight = 75;
            fsMonth.Name = "month"; fsMonth.HeaderText = "月份"; fsMonth.FillWeight = 75;
            fsStrike.Name = "strike"; fsStrike.HeaderText = "履約價"; fsStrike.FillWeight = 75;
            fsCp.Name = "cp"; fsCp.HeaderText = "C/P"; fsCp.FillWeight = 45;
            fsSide.Name = "side"; fsSide.HeaderText = "方向"; fsSide.FillWeight = 70;
            fsQty.Name = "qty"; fsQty.HeaderText = "口數"; fsQty.FillWeight = 55;
            fsAvg.Name = "avg"; fsAvg.HeaderText = "均價"; fsAvg.FillWeight = 75;
            fsMarket.Name = "market"; fsMarket.HeaderText = "市價"; fsMarket.FillWeight = 75;
            fsPnl.Name = "pnl"; fsPnl.HeaderText = "未實現損益"; fsPnl.FillWeight = 95;
            fsPnl.DefaultCellStyle.Format = "N2";
            fsCurrency.Name = "currency"; fsCurrency.HeaderText = "幣別"; fsCurrency.FillWeight = 55;
            _foreignSummary.Columns.AddRange(new DataGridViewColumn[] { fsExchange, fsCommodity, fsMonth, fsStrike, fsCp, fsSide, fsQty, fsAvg, fsMarket, fsPnl, fsCurrency });
            fdDate.Name = "date"; fdDate.HeaderText = "交易日"; fdDate.FillWeight = 70;
            fdExchange.Name = "exchange"; fdExchange.HeaderText = "交易所"; fdExchange.FillWeight = 60;
            fdOrdno.Name = "ordno"; fdOrdno.HeaderText = "委託書號"; fdOrdno.FillWeight = 95;
            fdMarketno.Name = "marketno"; fdMarketno.HeaderText = "市場單號"; fdMarketno.FillWeight = 85;
            fdCommodity.Name = "commodity"; fdCommodity.HeaderText = "商品契約"; fdCommodity.FillWeight = 125;
            fdSide.Name = "side"; fdSide.HeaderText = "方向"; fdSide.FillWeight = 50;
            fdQty.Name = "qty"; fdQty.HeaderText = "口數"; fdQty.FillWeight = 50;
            fdDeal.Name = "deal"; fdDeal.HeaderText = "成交價"; fdDeal.FillWeight = 70;
            fdMarket.Name = "market"; fdMarket.HeaderText = "市價"; fdMarket.FillWeight = 70;
            fdPnl.Name = "pnl"; fdPnl.HeaderText = "未實現損益"; fdPnl.FillWeight = 85;
            fdPnl.DefaultCellStyle.Format = "N2";
            fdInitial.Name = "initial"; fdInitial.HeaderText = "原始保證金"; fdInitial.FillWeight = 85;
            fdInitial.DefaultCellStyle.Format = "N2";
            fdMaintenance.Name = "maintenance"; fdMaintenance.HeaderText = "維持保證金"; fdMaintenance.FillWeight = 85;
            fdMaintenance.DefaultCellStyle.Format = "N2";
            fdCurrency.Name = "currency"; fdCurrency.HeaderText = "幣別"; fdCurrency.FillWeight = 50;
            _foreignDetail.Columns.AddRange(new DataGridViewColumn[] { fdDate, fdExchange, fdOrdno, fdMarketno, fdCommodity, fdSide, fdQty, fdDeal, fdMarket, fdPnl, fdInitial, fdMaintenance, fdCurrency });
            foreignSummaryPage.Controls.Add(_foreignSummary);
            foreignDetailPage.Controls.Add(_foreignDetail);
            _foreignTabs.Name = "foreignTabs";
            _foreignTabs.Dock = DockStyle.Fill;
            _foreignTabs.TabPages.AddRange(new TabPage[] { foreignSummaryPage, foreignDetailPage });
            foreignPage.Controls.Add(_foreignTabs);
            _marketTabs.Name = "marketTabs";
            _marketTabs.Dock = DockStyle.Fill;
            _marketTabs.Font = new Font("Microsoft JhengHei UI", 10.5F, FontStyle.Bold);
            _marketTabs.TabPages.AddRange(new TabPage[] { domesticPage, foreignPage });
            _marketTabs.SelectedIndexChanged += MarketTabs_SelectedIndexChanged;
            // 
            // PositionForm
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(18, 18, 18);
            ClientSize = new Size(1150, 600);
            Controls.Add(_marketTabs);
            Controls.Add(pnlBottom);
            Controls.Add(pnlTop);
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
            Name = "PositionForm";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "未平倉部位－國內／國外 (Open Positions)";
            pnlTop.ResumeLayout(false);
            pnlTop.PerformLayout();
            tabControl.ResumeLayout(false);
            tabSummary.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)dgvSummary).EndInit();
            tabDetail.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)dgvDetail).EndInit();
            pnlBottom.ResumeLayout(false);
            pnlBottom.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private Panel pnlTop;
        private Label lblTotalPnL;
        private Label lblTotalQty;
        private Label lblDelta;
        private Button btnCloseAll;
        private Button btnCloseSelected;
        private Button btnRefresh;
        private TabControl tabControl;
        private TabControl _marketTabs;
        private TabPage domesticPage;
        private TabPage foreignPage;
        private TabControl _foreignTabs;
        private TabPage foreignSummaryPage;
        private TabPage foreignDetailPage;
        private DataGridView _foreignSummary;
        private DataGridView _foreignDetail;
        private DataGridViewTextBoxColumn fsExchange;
        private DataGridViewTextBoxColumn fsCommodity;
        private DataGridViewTextBoxColumn fsMonth;
        private DataGridViewTextBoxColumn fsStrike;
        private DataGridViewTextBoxColumn fsCp;
        private DataGridViewTextBoxColumn fsSide;
        private DataGridViewTextBoxColumn fsQty;
        private DataGridViewTextBoxColumn fsAvg;
        private DataGridViewTextBoxColumn fsMarket;
        private DataGridViewTextBoxColumn fsPnl;
        private DataGridViewTextBoxColumn fsCurrency;
        private DataGridViewTextBoxColumn fdDate;
        private DataGridViewTextBoxColumn fdExchange;
        private DataGridViewTextBoxColumn fdOrdno;
        private DataGridViewTextBoxColumn fdMarketno;
        private DataGridViewTextBoxColumn fdCommodity;
        private DataGridViewTextBoxColumn fdSide;
        private DataGridViewTextBoxColumn fdQty;
        private DataGridViewTextBoxColumn fdDeal;
        private DataGridViewTextBoxColumn fdMarket;
        private DataGridViewTextBoxColumn fdPnl;
        private DataGridViewTextBoxColumn fdInitial;
        private DataGridViewTextBoxColumn fdMaintenance;
        private DataGridViewTextBoxColumn fdCurrency;
        private TabPage tabSummary;
        private DataGridView dgvSummary;
        private TabPage tabDetail;
        private DataGridView dgvDetail;
        private Panel pnlBottom;
        private Label lblStatus;
        private DataGridViewTextBoxColumn colSumCom;
        private DataGridViewTextBoxColumn colSumMonth;
        private DataGridViewTextBoxColumn colSumStrike;
        private DataGridViewTextBoxColumn colSumCP;
        private DataGridViewTextBoxColumn colSumBS;
        private DataGridViewTextBoxColumn colSumQty;
        private DataGridViewTextBoxColumn colSumAvgPrice;
        private DataGridViewTextBoxColumn colSumMarketPrice;
        private DataGridViewTextBoxColumn colSumPnL;
        private DataGridViewTextBoxColumn colSumDelta;
        private DataGridViewTextBoxColumn colDetDate;
        private DataGridViewTextBoxColumn colDetOrdNo;
        private DataGridViewTextBoxColumn colDetSeq;
        private DataGridViewTextBoxColumn colDetCom;
        private DataGridViewTextBoxColumn colDetBS;
        private DataGridViewTextBoxColumn colDetQty;
        private DataGridViewTextBoxColumn colDetDealPrice;
        private DataGridViewTextBoxColumn colDetMPrice;
        private DataGridViewTextBoxColumn colDetPnL;
        private DataGridViewTextBoxColumn colDetInitMargin;
        private DataGridViewTextBoxColumn colDetSpKey;
    }
}
