using System.Drawing;
using System.Windows.Forms;

namespace KGIOptionPricing
{
    partial class OrderReportForm
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
            DataGridViewCellStyle dataGridViewCellStyle1 = new DataGridViewCellStyle();
            DataGridViewCellStyle dataGridViewCellStyle2 = new DataGridViewCellStyle();
            pnlTop = new Panel();
            btnRecover = new Button();
            btnModify = new Button();
            btnCancelSelected = new Button();
            btnCancelAll = new Button();
            cboFilterStatus = new ComboBox();
            lblFilter = new Label();
            dgvOrders = new DataGridView();
            colTime = new DataGridViewTextBoxColumn();
            colOrderId = new DataGridViewTextBoxColumn();
            colOrdNo = new DataGridViewTextBoxColumn();
            colAccount = new DataGridViewTextBoxColumn();
            colInstrument = new DataGridViewTextBoxColumn();
            colBuySell = new DataGridViewTextBoxColumn();
            colCondition = new DataGridViewTextBoxColumn();
            colPrice = new DataGridViewTextBoxColumn();
            colQty = new DataGridViewTextBoxColumn();
            colFilledQty = new DataGridViewTextBoxColumn();
            colLeavesQty = new DataGridViewTextBoxColumn();
            colStatus = new DataGridViewTextBoxColumn();
            colMessage = new DataGridViewTextBoxColumn();
            pnlBottom = new Panel();
            lblSummary = new Label();
            pnlTop.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)dgvOrders).BeginInit();
            pnlBottom.SuspendLayout();
            SuspendLayout();
            // 
            // pnlTop
            // 
            pnlTop.BackColor = Color.FromArgb(28, 28, 28);
            pnlTop.Controls.Add(btnRecover);
            pnlTop.Controls.Add(btnModify);
            pnlTop.Controls.Add(btnCancelSelected);
            pnlTop.Controls.Add(btnCancelAll);
            pnlTop.Controls.Add(cboFilterStatus);
            pnlTop.Controls.Add(lblFilter);
            pnlTop.Dock = DockStyle.Top;
            pnlTop.Location = new Point(0, 0);
            pnlTop.Name = "pnlTop";
            pnlTop.Size = new Size(1184, 55);
            pnlTop.TabIndex = 0;
            // 
            // btnRecover
            // 
            btnRecover.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnRecover.BackColor = Color.FromArgb(45, 45, 48);
            btnRecover.FlatStyle = FlatStyle.Flat;
            btnRecover.ForeColor = Color.White;
            btnRecover.Location = new Point(1060, 10);
            btnRecover.Name = "btnRecover";
            btnRecover.Size = new Size(110, 35);
            btnRecover.TabIndex = 5;
            btnRecover.Text = "🔄 委託回補";
            btnRecover.UseVisualStyleBackColor = false;
            btnRecover.Click += btnRecover_Click;
            // 
            // btnModify
            // 
            btnModify.BackColor = Color.FromArgb(0, 122, 204);
            btnModify.FlatStyle = FlatStyle.Flat;
            btnModify.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point);
            btnModify.ForeColor = Color.White;
            btnModify.Location = new Point(480, 10);
            btnModify.Name = "btnModify";
            btnModify.Size = new Size(100, 35);
            btnModify.TabIndex = 4;
            btnModify.Text = "✏️ 改價/改量";
            btnModify.UseVisualStyleBackColor = false;
            btnModify.Click += btnModify_Click;
            // 
            // btnCancelSelected
            // 
            btnCancelSelected.BackColor = Color.FromArgb(180, 80, 0);
            btnCancelSelected.FlatStyle = FlatStyle.Flat;
            btnCancelSelected.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point);
            btnCancelSelected.ForeColor = Color.White;
            btnCancelSelected.Location = new Point(370, 10);
            btnCancelSelected.Name = "btnCancelSelected";
            btnCancelSelected.Size = new Size(100, 35);
            btnCancelSelected.TabIndex = 3;
            btnCancelSelected.Text = "❌ 單筆刪單";
            btnCancelSelected.UseVisualStyleBackColor = false;
            btnCancelSelected.Click += btnCancelSelected_Click;
            // 
            // btnCancelAll
            // 
            btnCancelAll.BackColor = Color.FromArgb(180, 0, 0);
            btnCancelAll.FlatStyle = FlatStyle.Flat;
            btnCancelAll.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point);
            btnCancelAll.ForeColor = Color.White;
            btnCancelAll.Location = new Point(250, 10);
            btnCancelAll.Name = "btnCancelAll";
            btnCancelAll.Size = new Size(110, 35);
            btnCancelAll.TabIndex = 2;
            btnCancelAll.Text = "🔴 一鍵全刪";
            btnCancelAll.UseVisualStyleBackColor = false;
            btnCancelAll.Click += btnCancelAll_Click;
            // 
            // cboFilterStatus
            // 
            cboFilterStatus.BackColor = Color.FromArgb(45, 45, 48);
            cboFilterStatus.DropDownStyle = ComboBoxStyle.DropDownList;
            cboFilterStatus.FlatStyle = FlatStyle.Flat;
            cboFilterStatus.ForeColor = Color.White;
            cboFilterStatus.FormattingEnabled = true;
            cboFilterStatus.Items.AddRange(new object[] { "全部委託 (All)", "有效掛單 (Active Only)", "已完全成交 (Filled)", "已取消/失敗 (Cancelled/Failed)" });
            cboFilterStatus.Location = new Point(70, 15);
            cboFilterStatus.Name = "cboFilterStatus";
            cboFilterStatus.Size = new Size(165, 25);
            cboFilterStatus.TabIndex = 1;
            cboFilterStatus.SelectedIndexChanged += cboFilterStatus_SelectedIndexChanged;
            // 
            // lblFilter
            // 
            lblFilter.AutoSize = true;
            lblFilter.ForeColor = Color.White;
            lblFilter.Location = new Point(15, 18);
            lblFilter.Name = "lblFilter";
            lblFilter.Size = new Size(51, 19);
            lblFilter.TabIndex = 0;
            lblFilter.Text = "篩選：";
            // 
            // dgvOrders
            // 
            dgvOrders.AllowUserToAddRows = false;
            dgvOrders.AllowUserToDeleteRows = false;
            dgvOrders.AllowUserToResizeRows = false;
            dgvOrders.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dgvOrders.BackgroundColor = Color.FromArgb(18, 18, 18);
            dgvOrders.BorderStyle = BorderStyle.None;
            dgvOrders.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            dgvOrders.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            dataGridViewCellStyle1.Alignment = DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle1.BackColor = Color.FromArgb(32, 32, 32);
            dataGridViewCellStyle1.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point);
            dataGridViewCellStyle1.ForeColor = Color.White;
            dataGridViewCellStyle1.SelectionBackColor = SystemColors.Highlight;
            dataGridViewCellStyle1.SelectionForeColor = SystemColors.HighlightText;
            dataGridViewCellStyle1.WrapMode = DataGridViewTriState.True;
            dgvOrders.ColumnHeadersDefaultCellStyle = dataGridViewCellStyle1;
            dgvOrders.ColumnHeadersHeight = 35;
            dgvOrders.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            dgvOrders.Columns.AddRange(new DataGridViewColumn[] { colTime, colOrderId, colOrdNo, colAccount, colInstrument, colBuySell, colCondition, colPrice, colQty, colFilledQty, colLeavesQty, colStatus, colMessage });
            dataGridViewCellStyle2.Alignment = DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle2.BackColor = Color.FromArgb(18, 18, 18);
            dataGridViewCellStyle2.Font = new Font("Consolas", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
            dataGridViewCellStyle2.ForeColor = Color.FromArgb(220, 220, 220);
            dataGridViewCellStyle2.SelectionBackColor = Color.FromArgb(0, 100, 150);
            dataGridViewCellStyle2.SelectionForeColor = Color.White;
            dataGridViewCellStyle2.WrapMode = DataGridViewTriState.False;
            dgvOrders.DefaultCellStyle = dataGridViewCellStyle2;
            dgvOrders.Dock = DockStyle.Fill;
            dgvOrders.EnableHeadersVisualStyles = false;
            dgvOrders.GridColor = Color.FromArgb(45, 45, 48);
            dgvOrders.Location = new Point(0, 55);
            dgvOrders.MultiSelect = false;
            dgvOrders.Name = "dgvOrders";
            dgvOrders.ReadOnly = true;
            dgvOrders.RowHeadersVisible = false;
            dgvOrders.RowTemplate.Height = 28;
            dgvOrders.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgvOrders.Size = new Size(1184, 525);
            dgvOrders.TabIndex = 1;
            // 
            // colTime
            // 
            colTime.FillWeight = 85F;
            colTime.HeaderText = "委託時間";
            colTime.Name = "colTime";
            colTime.ReadOnly = true;
            // 
            // colOrderId
            // 
            colOrderId.FillWeight = 80F;
            colOrderId.HeaderText = "自訂單號";
            colOrderId.Name = "colOrderId";
            colOrderId.ReadOnly = true;
            // 
            // colOrdNo
            // 
            colOrdNo.FillWeight = 75F;
            colOrdNo.HeaderText = "委託書號";
            colOrdNo.Name = "colOrdNo";
            colOrdNo.ReadOnly = true;
            // 
            // colAccount
            // 
            colAccount.FillWeight = 80F;
            colAccount.HeaderText = "帳號";
            colAccount.Name = "colAccount";
            colAccount.ReadOnly = true;
            // 
            // colInstrument
            // 
            colInstrument.FillWeight = 110F;
            colInstrument.HeaderText = "商品名稱";
            colInstrument.Name = "colInstrument";
            colInstrument.ReadOnly = true;
            // 
            // colBuySell
            // 
            colBuySell.FillWeight = 60F;
            colBuySell.HeaderText = "買賣";
            colBuySell.Name = "colBuySell";
            colBuySell.ReadOnly = true;
            // 
            // colCondition
            // 
            colCondition.FillWeight = 70F;
            colCondition.HeaderText = "條件";
            colCondition.Name = "colCondition";
            colCondition.ReadOnly = true;
            // 
            // colPrice
            // 
            colPrice.FillWeight = 75F;
            colPrice.HeaderText = "委託價";
            colPrice.Name = "colPrice";
            colPrice.ReadOnly = true;
            // 
            // colQty
            // 
            colQty.FillWeight = 55F;
            colQty.HeaderText = "口數";
            colQty.Name = "colQty";
            colQty.ReadOnly = true;
            // 
            // colFilledQty
            // 
            colFilledQty.FillWeight = 65F;
            colFilledQty.HeaderText = "已成交";
            colFilledQty.Name = "colFilledQty";
            colFilledQty.ReadOnly = true;
            // 
            // colLeavesQty
            // 
            colLeavesQty.FillWeight = 65F;
            colLeavesQty.HeaderText = "未成交";
            colLeavesQty.Name = "colLeavesQty";
            colLeavesQty.ReadOnly = true;
            // 
            // colStatus
            // 
            colStatus.FillWeight = 85F;
            colStatus.HeaderText = "委託狀態";
            colStatus.Name = "colStatus";
            colStatus.ReadOnly = true;
            // 
            // colMessage
            // 
            colMessage.FillWeight = 130F;
            colMessage.HeaderText = "回報備註";
            colMessage.Name = "colMessage";
            colMessage.ReadOnly = true;
            // 
            // pnlBottom
            // 
            pnlBottom.BackColor = Color.FromArgb(28, 28, 28);
            pnlBottom.Controls.Add(lblSummary);
            pnlBottom.Dock = DockStyle.Bottom;
            pnlBottom.Location = new Point(0, 580);
            pnlBottom.Name = "pnlBottom";
            pnlBottom.Size = new Size(1184, 35);
            pnlBottom.TabIndex = 2;
            // 
            // lblSummary
            // 
            lblSummary.AutoSize = true;
            lblSummary.ForeColor = Color.DarkGray;
            lblSummary.Location = new Point(15, 8);
            lblSummary.Name = "lblSummary";
            lblSummary.Size = new Size(345, 19);
            lblSummary.TabIndex = 0;
            lblSummary.Text = "總委託筆數: 0 | 有效掛單: 0 | 已成交: 0 | 已取消/失敗: 0";
            // 
            // OrderReportForm
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(18, 18, 18);
            ClientSize = new Size(1184, 615);
            Controls.Add(dgvOrders);
            Controls.Add(pnlBottom);
            Controls.Add(pnlTop);
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
            Name = "OrderReportForm";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "📋 委託回報 (Order Report)";
            Load += OrderReportForm_Load_1;
            pnlTop.ResumeLayout(false);
            pnlTop.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)dgvOrders).EndInit();
            pnlBottom.ResumeLayout(false);
            pnlBottom.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private Panel pnlTop;
        private Label lblFilter;
        private ComboBox cboFilterStatus;
        private Button btnCancelAll;
        private Button btnCancelSelected;
        private Button btnModify;
        private Button btnRecover;
        private DataGridView dgvOrders;
        private Panel pnlBottom;
        private Label lblSummary;
        private DataGridViewTextBoxColumn colTime;
        private DataGridViewTextBoxColumn colOrderId;
        private DataGridViewTextBoxColumn colOrdNo;
        private DataGridViewTextBoxColumn colAccount;
        private DataGridViewTextBoxColumn colInstrument;
        private DataGridViewTextBoxColumn colBuySell;
        private DataGridViewTextBoxColumn colCondition;
        private DataGridViewTextBoxColumn colPrice;
        private DataGridViewTextBoxColumn colQty;
        private DataGridViewTextBoxColumn colFilledQty;
        private DataGridViewTextBoxColumn colLeavesQty;
        private DataGridViewTextBoxColumn colStatus;
        private DataGridViewTextBoxColumn colMessage;
    }
}
