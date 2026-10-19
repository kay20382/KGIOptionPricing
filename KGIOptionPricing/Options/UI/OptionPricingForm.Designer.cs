using System.Drawing;
using System.Windows.Forms;

namespace KGIOptionPricing
{
    partial class OptionPricingForm
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
            _formsPlot = new ScottPlot.WinForms.FormsPlot();
            _dgvTQuote = new DataGridView();
            colCallDelta = new DataGridViewTextBoxColumn();
            colCallIV = new DataGridViewTextBoxColumn();
            colCallBid = new DataGridViewTextBoxColumn();
            colCallAsk = new DataGridViewTextBoxColumn();
            colCallLast = new DataGridViewTextBoxColumn();
            colStrike = new DataGridViewTextBoxColumn();
            colPutLast = new DataGridViewTextBoxColumn();
            colPutBid = new DataGridViewTextBoxColumn();
            colPutAsk = new DataGridViewTextBoxColumn();
            colPutIV = new DataGridViewTextBoxColumn();
            colPutDelta = new DataGridViewTextBoxColumn();
            _cboExpiration = new ComboBox();
            _cboModel = new ComboBox();
            _lblStatus = new Label();
            _btnStartQuotes = new Button();
            _btnSave = new Button();
            _chkAutoSaveMorning = new CheckBox();
            _dtpAutoSaveMorning = new DateTimePicker();
            _chkShift = new CheckBox();
            topPanel = new Panel();
            lblSelect = new Label();
            lblModel = new Label();
            splitContainer = new SplitContainer();
            ((System.ComponentModel.ISupportInitialize)_dgvTQuote).BeginInit();
            topPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)splitContainer).BeginInit();
            splitContainer.Panel1.SuspendLayout();
            splitContainer.Panel2.SuspendLayout();
            splitContainer.SuspendLayout();
            SuspendLayout();
            // 
            // _formsPlot
            // 
            _formsPlot.BackColor = Color.FromArgb(18, 18, 18);
            _formsPlot.Dock = DockStyle.Fill;
            _formsPlot.Location = new Point(0, 0);
            _formsPlot.Name = "_formsPlot";
            _formsPlot.Size = new Size(681, 710);
            _formsPlot.TabIndex = 0;
            // 
            // _dgvTQuote
            // 
            _dgvTQuote.AllowUserToAddRows = false;
            _dgvTQuote.AllowUserToDeleteRows = false;
            _dgvTQuote.AllowUserToResizeRows = false;
            _dgvTQuote.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _dgvTQuote.BackgroundColor = Color.FromArgb(18, 18, 18);
            _dgvTQuote.BorderStyle = BorderStyle.None;
            _dgvTQuote.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            _dgvTQuote.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            _dgvTQuote.ColumnHeadersHeight = 35;
            _dgvTQuote.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            _dgvTQuote.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            _dgvTQuote.Columns.AddRange(new DataGridViewColumn[] { colCallDelta, colCallIV, colCallBid, colCallAsk, colCallLast, colStrike, colPutLast, colPutBid, colPutAsk, colPutIV, colPutDelta });
            _dgvTQuote.DefaultCellStyle.Font = new Font("Consolas", 10F);
            _dgvTQuote.Dock = DockStyle.Fill;
            _dgvTQuote.EnableHeadersVisualStyles = false;
            _dgvTQuote.GridColor = Color.FromArgb(45, 45, 48);
            _dgvTQuote.Location = new Point(0, 0);
            _dgvTQuote.MultiSelect = false;
            _dgvTQuote.Name = "_dgvTQuote";
            _dgvTQuote.ReadOnly = true;
            _dgvTQuote.RowHeadersVisible = false;
            _dgvTQuote.RowTemplate.Height = 28;
            _dgvTQuote.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _dgvTQuote.Size = new Size(677, 710);
            _dgvTQuote.TabIndex = 0;
            // 
            // colCallDelta
            // 
            colCallDelta.HeaderText = "Call Delta";
            colCallDelta.Name = "colCallDelta";
            colCallDelta.ReadOnly = true;
            // 
            // colCallIV
            // 
            colCallIV.HeaderText = "Call IV";
            colCallIV.Name = "colCallIV";
            colCallIV.ReadOnly = true;
            // 
            // colCallBid
            // 
            colCallBid.HeaderText = "Call Bid";
            colCallBid.Name = "colCallBid";
            colCallBid.ReadOnly = true;
            // 
            // colCallAsk
            // 
            colCallAsk.HeaderText = "Call Ask";
            colCallAsk.Name = "colCallAsk";
            colCallAsk.ReadOnly = true;
            // 
            // colCallLast
            // 
            colCallLast.HeaderText = "Call Last";
            colCallLast.Name = "colCallLast";
            colCallLast.ReadOnly = true;
            // 
            // colStrike
            // 
            colStrike.DefaultCellStyle.BackColor = Color.FromArgb(0, 0, 100);
            colStrike.DefaultCellStyle.ForeColor = Color.Gold;
            colStrike.DefaultCellStyle.Font = new Font("Consolas", 10F, FontStyle.Bold);
            colStrike.HeaderText = "Strike";
            colStrike.Name = "colStrike";
            colStrike.ReadOnly = true;
            // 
            // colPutLast
            // 
            colPutLast.HeaderText = "Put Last";
            colPutLast.Name = "colPutLast";
            colPutLast.ReadOnly = true;
            // 
            // colPutBid
            // 
            colPutBid.HeaderText = "Put Bid";
            colPutBid.Name = "colPutBid";
            colPutBid.ReadOnly = true;
            // 
            // colPutAsk
            // 
            colPutAsk.HeaderText = "Put Ask";
            colPutAsk.Name = "colPutAsk";
            colPutAsk.ReadOnly = true;
            // 
            // colPutIV
            // 
            colPutIV.HeaderText = "Put IV";
            colPutIV.Name = "colPutIV";
            colPutIV.ReadOnly = true;
            // 
            // colPutDelta
            // 
            colPutDelta.HeaderText = "Put Delta";
            colPutDelta.Name = "colPutDelta";
            colPutDelta.ReadOnly = true;
            // 
            // _cboExpiration
            // 
            _cboExpiration.BackColor = Color.FromArgb(45, 45, 48);
            _cboExpiration.DropDownStyle = ComboBoxStyle.DropDownList;
            _cboExpiration.FlatStyle = FlatStyle.Flat;
            _cboExpiration.ForeColor = Color.White;
            _cboExpiration.FormattingEnabled = true;
            _cboExpiration.Location = new Point(140, 16);
            _cboExpiration.Name = "_cboExpiration";
            _cboExpiration.Size = new Size(200, 25);
            _cboExpiration.TabIndex = 1;
            _cboExpiration.SelectedIndexChanged += CboExpiration_SelectedIndexChanged;
            // 
            // _cboModel
            // 
            _cboModel.BackColor = Color.FromArgb(45, 45, 48);
            _cboModel.DropDownStyle = ComboBoxStyle.DropDownList;
            _cboModel.FlatStyle = FlatStyle.Flat;
            _cboModel.ForeColor = Color.White;
            _cboModel.FormattingEnabled = true;
            _cboModel.Location = new Point(430, 16);
            _cboModel.Name = "_cboModel";
            _cboModel.Size = new Size(100, 25);
            _cboModel.TabIndex = 3;
            _cboModel.SelectedIndexChanged += CboModel_SelectedIndexChanged;
            // 
            // _lblStatus
            // 
            _lblStatus.AutoSize = true;
            _lblStatus.ForeColor = Color.LimeGreen;
            _lblStatus.Location = new Point(20, 61);
            _lblStatus.Name = "_lblStatus";
            _lblStatus.Size = new Size(124, 19);
            _lblStatus.TabIndex = 10;
            _lblStatus.Text = "Status: Initializing...";
            // 
            // _btnSave
            // 
            _btnSave.BackColor = Color.FromArgb(45, 45, 48);
            _btnSave.FlatStyle = FlatStyle.Flat;
            _btnSave.ForeColor = Color.White;
            _btnSave.Location = new Point(540, 15);
            _btnSave.Name = "_btnSave";
            _btnSave.Size = new Size(70, 30);
            _btnSave.TabIndex = 4;
            _btnSave.Text = "Save IV";
            _btnSave.UseVisualStyleBackColor = false;
            _btnSave.Click += BtnSave_Click;
            //
            // _btnStartQuotes
            // 
            _btnStartQuotes.BackColor = Color.FromArgb(0, 90, 150);
            _btnStartQuotes.FlatStyle = FlatStyle.Flat;
            _btnStartQuotes.ForeColor = Color.White;
            _btnStartQuotes.Location = new Point(1040, 15);
            _btnStartQuotes.Name = "_btnStartQuotes";
            _btnStartQuotes.Size = new Size(110, 30);
            _btnStartQuotes.TabIndex = 8;
            _btnStartQuotes.Text = "啟動行情";
            _btnStartQuotes.UseVisualStyleBackColor = false;
            _btnStartQuotes.Click += BtnStartQuotes_Click;
            //
            // _chkAutoSaveMorning
            // 
            _chkAutoSaveMorning.AutoSize = true;
            _chkAutoSaveMorning.Checked = true;
            _chkAutoSaveMorning.CheckState = CheckState.Checked;
            _chkAutoSaveMorning.ForeColor = Color.White;
            _chkAutoSaveMorning.Location = new Point(644, 20);
            _chkAutoSaveMorning.Name = "_chkAutoSaveMorning";
            _chkAutoSaveMorning.Size = new Size(139, 23);
            _chkAutoSaveMorning.TabIndex = 5;
            _chkAutoSaveMorning.Text = "Auto Save(Day) @";
            _chkAutoSaveMorning.UseVisualStyleBackColor = true;
            _chkAutoSaveMorning.CheckedChanged += _chkAutoSaveMorning_CheckedChanged;
            // 
            // _dtpAutoSaveMorning
            // 
            _dtpAutoSaveMorning.Enabled = false;
            _dtpAutoSaveMorning.Format = DateTimePickerFormat.Time;
            _dtpAutoSaveMorning.Location = new Point(799, 20);
            _dtpAutoSaveMorning.Name = "_dtpAutoSaveMorning";
            _dtpAutoSaveMorning.ShowUpDown = true;
            _dtpAutoSaveMorning.Size = new Size(90, 25);
            _dtpAutoSaveMorning.TabIndex = 6;
            _dtpAutoSaveMorning.Value = new DateTime(2026, 8, 5, 13, 35, 0, 0);
            _dtpAutoSaveMorning.ValueChanged += _dtpAutoSaveMorning_ValueChanged;
            // 
            // _chkShift
            // 
            _chkShift.AutoSize = true;
            _chkShift.Checked = true;
            _chkShift.CheckState = CheckState.Checked;
            _chkShift.ForeColor = Color.White;
            _chkShift.Location = new Point(910, 20);
            _chkShift.Name = "_chkShift";
            _chkShift.Size = new Size(111, 23);
            _chkShift.TabIndex = 7;
            _chkShift.Text = "Shift (ATM IV)";
            _chkShift.UseVisualStyleBackColor = true;
            // 
            // topPanel
            // 
            topPanel.BackColor = Color.FromArgb(28, 28, 28);
            topPanel.Controls.Add(lblSelect);
            topPanel.Controls.Add(_cboExpiration);
            topPanel.Controls.Add(lblModel);
            topPanel.Controls.Add(_cboModel);
            topPanel.Controls.Add(_btnSave);
            topPanel.Controls.Add(_btnStartQuotes);
            topPanel.Controls.Add(_chkAutoSaveMorning);
            topPanel.Controls.Add(_dtpAutoSaveMorning);
            topPanel.Controls.Add(_chkShift);
            topPanel.Controls.Add(_lblStatus);
            topPanel.Dock = DockStyle.Top;
            topPanel.Location = new Point(0, 0);
            topPanel.Name = "topPanel";
            topPanel.Size = new Size(1362, 90);
            topPanel.TabIndex = 0;
            // 
            // lblSelect
            // 
            lblSelect.AutoSize = true;
            lblSelect.ForeColor = Color.White;
            lblSelect.Location = new Point(20, 20);
            lblSelect.Name = "lblSelect";
            lblSelect.Size = new Size(114, 19);
            lblSelect.TabIndex = 0;
            lblSelect.Text = "Select Expiration:";
            // 
            // lblModel
            // 
            lblModel.AutoSize = true;
            lblModel.ForeColor = Color.White;
            lblModel.Location = new Point(360, 20);
            lblModel.Name = "lblModel";
            lblModel.Size = new Size(68, 19);
            lblModel.TabIndex = 2;
            lblModel.Text = "IV Model:";
            // 
            // splitContainer
            // 
            splitContainer.BackColor = Color.FromArgb(45, 45, 48);
            splitContainer.Dock = DockStyle.Fill;
            splitContainer.Location = new Point(0, 90);
            splitContainer.Name = "splitContainer";
            // 
            // splitContainer.Panel1
            // 
            splitContainer.Panel1.Controls.Add(_dgvTQuote);
            // 
            // splitContainer.Panel2
            // 
            splitContainer.Panel2.Controls.Add(_formsPlot);
            splitContainer.Size = new Size(1362, 710);
            splitContainer.SplitterDistance = 677;
            splitContainer.TabIndex = 1;
            // 
            // OptionPricingForm
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(18, 18, 18);
            ClientSize = new Size(1362, 800);
            Controls.Add(splitContainer);
            Controls.Add(topPanel);
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
            Name = "OptionPricingForm";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "📊 選擇權定價與 T 報價鏈 (Option Pricing & Chain)";
            ((System.ComponentModel.ISupportInitialize)_dgvTQuote).EndInit();
            topPanel.ResumeLayout(false);
            topPanel.PerformLayout();
            splitContainer.Panel1.ResumeLayout(false);
            splitContainer.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)splitContainer).EndInit();
            splitContainer.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private ScottPlot.WinForms.FormsPlot _formsPlot;
        private DataGridView _dgvTQuote;
        private DataGridViewTextBoxColumn colCallDelta;
        private DataGridViewTextBoxColumn colCallIV;
        private DataGridViewTextBoxColumn colCallBid;
        private DataGridViewTextBoxColumn colCallAsk;
        private DataGridViewTextBoxColumn colCallLast;
        private DataGridViewTextBoxColumn colStrike;
        private DataGridViewTextBoxColumn colPutLast;
        private DataGridViewTextBoxColumn colPutBid;
        private DataGridViewTextBoxColumn colPutAsk;
        private DataGridViewTextBoxColumn colPutIV;
        private DataGridViewTextBoxColumn colPutDelta;
        private ComboBox _cboExpiration;
        private ComboBox _cboModel;
        private Label _lblStatus;
        private Button _btnStartQuotes;
        private Button _btnSave;
        private CheckBox _chkAutoSaveMorning;
        private DateTimePicker _dtpAutoSaveMorning;
        private CheckBox _chkShift;
        private Panel topPanel;
        private Label lblSelect;
        private Label lblModel;
        private SplitContainer splitContainer;
    }
}
