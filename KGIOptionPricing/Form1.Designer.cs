namespace KGIOptionPricing
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
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
            _lblTradeStatus = new Label();
            _btnSave = new Button();
            _chkAutoSaveMorning = new CheckBox();
            _dtpAutoSaveMorning = new DateTimePicker();
            _chkAutoSaveNight = new CheckBox();
            _dtpAutoSaveNight = new DateTimePicker();
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
            _dgvTQuote.Columns.AddRange(new DataGridViewColumn[] { colCallDelta, colCallIV, colCallBid, colCallAsk, colCallLast, colStrike, colPutLast, colPutBid, colPutAsk, colPutIV, colPutDelta });
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
            _lblStatus.Location = new Point(160, 61);
            _lblStatus.Name = "_lblStatus";
            _lblStatus.Size = new Size(124, 19);
            _lblStatus.TabIndex = 10;
            _lblStatus.Text = "Status: Initializing...";
            // 
            // _lblTradeStatus
            // 
            _lblTradeStatus.AutoSize = true;
            _lblTradeStatus.ForeColor = Color.Orange;
            _lblTradeStatus.Location = new Point(20, 61);
            _lblTradeStatus.Name = "_lblTradeStatus";
            _lblTradeStatus.Size = new Size(114, 19);
            _lblTradeStatus.TabIndex = 11;
            _lblTradeStatus.Text = "[TradeCom: 離線]";
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
            // _chkAutoSaveNight
            // 
            _chkAutoSaveNight.AutoSize = true;
            _chkAutoSaveNight.Checked = true;
            _chkAutoSaveNight.CheckState = CheckState.Checked;
            _chkAutoSaveNight.ForeColor = Color.White;
            _chkAutoSaveNight.Location = new Point(644, 54);
            _chkAutoSaveNight.Name = "_chkAutoSaveNight";
            _chkAutoSaveNight.Size = new Size(149, 23);
            _chkAutoSaveNight.TabIndex = 7;
            _chkAutoSaveNight.Text = "Auto Save(Night) @";
            _chkAutoSaveNight.UseVisualStyleBackColor = true;
            _chkAutoSaveNight.CheckedChanged += _chkAutoSaveNight_CheckedChanged;
            // 
            // _dtpAutoSaveNight
            // 
            _dtpAutoSaveNight.Enabled = false;
            _dtpAutoSaveNight.Format = DateTimePickerFormat.Time;
            _dtpAutoSaveNight.Location = new Point(799, 55);
            _dtpAutoSaveNight.Name = "_dtpAutoSaveNight";
            _dtpAutoSaveNight.ShowUpDown = true;
            _dtpAutoSaveNight.Size = new Size(90, 25);
            _dtpAutoSaveNight.TabIndex = 8;
            _dtpAutoSaveNight.Value = new DateTime(2026, 8, 5, 4, 30, 0, 0);
            _dtpAutoSaveNight.ValueChanged += _dtpAutoSaveNight_ValueChanged;
            // 
            // _chkShift
            // 
            _chkShift.AutoSize = true;
            _chkShift.ForeColor = Color.White;
            _chkShift.Location = new Point(540, 55);
            _chkShift.Name = "_chkShift";
            _chkShift.Size = new Size(87, 23);
            _chkShift.TabIndex = 9;
            _chkShift.Text = "ATM Shift";
            _chkShift.UseVisualStyleBackColor = true;
            // 
            // topPanel
            // 
            topPanel.BackColor = Color.FromArgb(28, 28, 30);
            topPanel.Controls.Add(lblSelect);
            topPanel.Controls.Add(_cboExpiration);
            topPanel.Controls.Add(lblModel);
            topPanel.Controls.Add(_cboModel);
            topPanel.Controls.Add(_btnSave);
            topPanel.Controls.Add(_chkAutoSaveMorning);
            topPanel.Controls.Add(_dtpAutoSaveMorning);
            topPanel.Controls.Add(_chkAutoSaveNight);
            topPanel.Controls.Add(_dtpAutoSaveNight);
            topPanel.Controls.Add(_chkShift);
            topPanel.Controls.Add(_lblTradeStatus);
            topPanel.Controls.Add(_lblStatus);
            topPanel.Dock = DockStyle.Top;
            topPanel.Location = new Point(0, 0);
            topPanel.Name = "topPanel";
            topPanel.Padding = new Padding(10);
            topPanel.Size = new Size(1362, 90);
            topPanel.TabIndex = 0;
            // 
            // lblSelect
            // 
            lblSelect.AutoSize = true;
            lblSelect.ForeColor = Color.FromArgb(200, 200, 200);
            lblSelect.Location = new Point(20, 20);
            lblSelect.Name = "lblSelect";
            lblSelect.Size = new Size(111, 19);
            lblSelect.TabIndex = 0;
            lblSelect.Text = "Select Expiration:";
            // 
            // lblModel
            // 
            lblModel.AutoSize = true;
            lblModel.ForeColor = Color.FromArgb(200, 200, 200);
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
            splitContainer.Panel1.Controls.Add(_formsPlot);
            // 
            // splitContainer.Panel2
            // 
            splitContainer.Panel2.Controls.Add(_dgvTQuote);
            splitContainer.Size = new Size(1362, 710);
            splitContainer.SplitterDistance = 681;
            splitContainer.TabIndex = 1;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(18, 18, 18);
            ClientSize = new Size(1362, 800);
            Controls.Add(splitContainer);
            Controls.Add(topPanel);
            Font = new Font("Segoe UI", 10F);
            ForeColor = Color.White;
            Name = "Form1";
            Text = "TXO Volatility Smile Pro";
            Load += Form1_Load_1;
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
        private System.Windows.Forms.DataGridView _dgvTQuote;
        private System.Windows.Forms.ComboBox _cboExpiration;
        private System.Windows.Forms.ComboBox _cboModel;
        private System.Windows.Forms.Label _lblStatus;
        private System.Windows.Forms.Label _lblTradeStatus;
        private System.Windows.Forms.Button _btnSave;
        private System.Windows.Forms.CheckBox _chkAutoSaveMorning;
        private System.Windows.Forms.DateTimePicker _dtpAutoSaveMorning;
        private System.Windows.Forms.CheckBox _chkAutoSaveNight;
        private System.Windows.Forms.DateTimePicker _dtpAutoSaveNight;
        private System.Windows.Forms.CheckBox _chkShift;
        
        private System.Windows.Forms.Panel topPanel;
        private System.Windows.Forms.Label lblSelect;
        private System.Windows.Forms.Label lblModel;
        private System.Windows.Forms.SplitContainer splitContainer;
        private System.Windows.Forms.DataGridViewTextBoxColumn colCallDelta;
        private System.Windows.Forms.DataGridViewTextBoxColumn colCallIV;
        private System.Windows.Forms.DataGridViewTextBoxColumn colCallBid;
        private System.Windows.Forms.DataGridViewTextBoxColumn colCallAsk;
        private System.Windows.Forms.DataGridViewTextBoxColumn colCallLast;
        private System.Windows.Forms.DataGridViewTextBoxColumn colStrike;
        private System.Windows.Forms.DataGridViewTextBoxColumn colPutLast;
        private System.Windows.Forms.DataGridViewTextBoxColumn colPutBid;
        private System.Windows.Forms.DataGridViewTextBoxColumn colPutAsk;
        private System.Windows.Forms.DataGridViewTextBoxColumn colPutIV;
        private System.Windows.Forms.DataGridViewTextBoxColumn colPutDelta;
    }
}