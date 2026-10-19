using System.Drawing;
using System.Windows.Forms;

namespace KGIOptionPricing
{
    partial class DealReportForm
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
            pnlTop = new Panel();
            btnExportCsv = new Button();
            lblStatValue = new Label();
            lblStatQty = new Label();
            dgvDeals = new DataGridView();
            colTime = new DataGridViewTextBoxColumn();
            colSeqNo = new DataGridViewTextBoxColumn();
            colOrdNo = new DataGridViewTextBoxColumn();
            colInstrument = new DataGridViewTextBoxColumn();
            colBuySell = new DataGridViewTextBoxColumn();
            colPrice = new DataGridViewTextBoxColumn();
            colQty = new DataGridViewTextBoxColumn();
            colAvgPrice = new DataGridViewTextBoxColumn();
            pnlBottom = new Panel();
            lblSummary = new Label();
            pnlTop.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)dgvDeals).BeginInit();
            pnlBottom.SuspendLayout();
            SuspendLayout();
            // 
            // pnlTop
            // 
            pnlTop.BackColor = Color.FromArgb(28, 28, 28);
            pnlTop.Controls.Add(btnExportCsv);
            pnlTop.Controls.Add(lblStatValue);
            pnlTop.Controls.Add(lblStatQty);
            pnlTop.Dock = DockStyle.Top;
            pnlTop.Location = new Point(0, 0);
            pnlTop.Name = "pnlTop";
            pnlTop.Size = new Size(1100, 55);
            pnlTop.TabIndex = 0;
            // 
            // btnExportCsv
            // 
            btnExportCsv.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnExportCsv.BackColor = Color.FromArgb(45, 45, 48);
            btnExportCsv.FlatStyle = FlatStyle.Flat;
            btnExportCsv.ForeColor = Color.White;
            btnExportCsv.Location = new Point(965, 10);
            btnExportCsv.Name = "btnExportCsv";
            btnExportCsv.Size = new Size(120, 35);
            btnExportCsv.TabIndex = 2;
            btnExportCsv.Text = "📁 匯出 CSV";
            btnExportCsv.UseVisualStyleBackColor = false;
            btnExportCsv.Click += btnExportCsv_Click;
            // 
            // lblStatValue
            // 
            lblStatValue.AutoSize = true;
            lblStatValue.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            lblStatValue.ForeColor = Color.Gold;
            lblStatValue.Location = new Point(250, 18);
            lblStatValue.Name = "lblStatValue";
            lblStatValue.Size = new Size(174, 19);
            lblStatValue.TabIndex = 1;
            lblStatValue.Text = "總成交點數/金額: 0 點";
            // 
            // lblStatQty
            // 
            lblStatQty.AutoSize = true;
            lblStatQty.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            lblStatQty.ForeColor = Color.LimeGreen;
            lblStatQty.Location = new Point(20, 18);
            lblStatQty.Name = "lblStatQty";
            lblStatQty.Size = new Size(196, 19);
            lblStatQty.TabIndex = 0;
            lblStatQty.Text = "當日總成交口數: 0 口 (買 0 / 賣 0)";
            // 
            // dgvDeals
            // 
            dgvDeals.AllowUserToAddRows = false;
            dgvDeals.AllowUserToDeleteRows = false;
            dgvDeals.AllowUserToResizeRows = false;
            dgvDeals.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dgvDeals.BackgroundColor = Color.FromArgb(18, 18, 18);
            dgvDeals.BorderStyle = BorderStyle.None;
            dgvDeals.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            dgvDeals.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            dataGridViewCellStyle1.Alignment = DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle1.BackColor = Color.FromArgb(32, 32, 32);
            dataGridViewCellStyle1.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
            dataGridViewCellStyle1.ForeColor = Color.White;
            dataGridViewCellStyle1.SelectionBackColor = SystemColors.Highlight;
            dataGridViewCellStyle1.SelectionForeColor = SystemColors.HighlightText;
            dataGridViewCellStyle1.WrapMode = DataGridViewTriState.True;
            dgvDeals.ColumnHeadersDefaultCellStyle = dataGridViewCellStyle1;
            dgvDeals.ColumnHeadersHeight = 35;
            dgvDeals.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            dgvDeals.Columns.AddRange(new DataGridViewColumn[] { colTime, colSeqNo, colOrdNo, colInstrument, colBuySell, colPrice, colQty, colAvgPrice });
            dataGridViewCellStyle2.Alignment = DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle2.BackColor = Color.FromArgb(18, 18, 18);
            dataGridViewCellStyle2.Font = new Font("Consolas", 9.5F);
            dataGridViewCellStyle2.ForeColor = Color.FromArgb(220, 220, 220);
            dataGridViewCellStyle2.SelectionBackColor = Color.FromArgb(0, 100, 150);
            dataGridViewCellStyle2.SelectionForeColor = Color.White;
            dataGridViewCellStyle2.WrapMode = DataGridViewTriState.False;
            dgvDeals.DefaultCellStyle = dataGridViewCellStyle2;
            dgvDeals.Dock = DockStyle.Fill;
            dgvDeals.EnableHeadersVisualStyles = false;
            dgvDeals.GridColor = Color.FromArgb(45, 45, 48);
            dgvDeals.Location = new Point(0, 55);
            dgvDeals.MultiSelect = false;
            dgvDeals.Name = "dgvDeals";
            dgvDeals.ReadOnly = true;
            dgvDeals.RowHeadersVisible = false;
            dgvDeals.RowTemplate.Height = 28;
            dgvDeals.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgvDeals.Size = new Size(1100, 520);
            dgvDeals.TabIndex = 1;
            // 
            // colTime
            // 
            colTime.FillWeight = 110F;
            colTime.HeaderText = "成交時間";
            colTime.Name = "colTime";
            colTime.ReadOnly = true;
            // 
            // colSeqNo
            // 
            colSeqNo.FillWeight = 95F;
            colSeqNo.HeaderText = "成交書號";
            colSeqNo.Name = "colSeqNo";
            colSeqNo.ReadOnly = true;
            // 
            // colOrdNo
            // 
            colOrdNo.FillWeight = 90F;
            colOrdNo.HeaderText = "委託書號";
            colOrdNo.Name = "colOrdNo";
            colOrdNo.ReadOnly = true;
            // 
            // colInstrument
            // 
            colInstrument.FillWeight = 150F;
            colInstrument.HeaderText = "商品名稱";
            colInstrument.Name = "colInstrument";
            colInstrument.ReadOnly = true;
            // 
            // colBuySell
            // 
            colBuySell.FillWeight = 75F;
            colBuySell.HeaderText = "買賣";
            colBuySell.Name = "colBuySell";
            colBuySell.ReadOnly = true;
            // 
            // colPrice
            // 
            colPrice.FillWeight = 90F;
            colPrice.HeaderText = "成交價";
            colPrice.Name = "colPrice";
            colPrice.ReadOnly = true;
            // 
            // colQty
            // 
            colQty.FillWeight = 75F;
            colQty.HeaderText = "口數";
            colQty.Name = "colQty";
            colQty.ReadOnly = true;
            // 
            // colAvgPrice
            // 
            colAvgPrice.FillWeight = 95F;
            colAvgPrice.HeaderText = "成交均價";
            colAvgPrice.Name = "colAvgPrice";
            colAvgPrice.ReadOnly = true;
            // 
            // pnlBottom
            // 
            pnlBottom.BackColor = Color.FromArgb(28, 28, 28);
            pnlBottom.Controls.Add(lblSummary);
            pnlBottom.Dock = DockStyle.Bottom;
            pnlBottom.Location = new Point(0, 575);
            pnlBottom.Name = "pnlBottom";
            pnlBottom.Size = new Size(1100, 35);
            pnlBottom.TabIndex = 2;
            // 
            // lblSummary
            // 
            lblSummary.AutoSize = true;
            lblSummary.ForeColor = Color.DarkGray;
            lblSummary.Location = new Point(15, 8);
            lblSummary.Name = "lblSummary";
            lblSummary.Size = new Size(140, 19);
            lblSummary.TabIndex = 0;
            lblSummary.Text = "總成交筆數: 0 筆";
            // 
            // DealReportForm
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(18, 18, 18);
            ClientSize = new Size(1100, 610);
            Controls.Add(dgvDeals);
            Controls.Add(pnlBottom);
            Controls.Add(pnlTop);
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
            Name = "DealReportForm";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "💰 成交回報 (Deal Report)";
            pnlTop.ResumeLayout(false);
            pnlTop.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)dgvDeals).EndInit();
            pnlBottom.ResumeLayout(false);
            pnlBottom.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private Panel pnlTop;
        private Label lblStatQty;
        private Label lblStatValue;
        private Button btnExportCsv;
        private DataGridView dgvDeals;
        private Panel pnlBottom;
        private Label lblSummary;
        private DataGridViewTextBoxColumn colTime;
        private DataGridViewTextBoxColumn colSeqNo;
        private DataGridViewTextBoxColumn colOrdNo;
        private DataGridViewTextBoxColumn colInstrument;
        private DataGridViewTextBoxColumn colBuySell;
        private DataGridViewTextBoxColumn colPrice;
        private DataGridViewTextBoxColumn colQty;
        private DataGridViewTextBoxColumn colAvgPrice;
    }
}
