using System.Drawing;
using System.Windows.Forms;

namespace KGIOptionPricing
{
    partial class OrderPlacementForm
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
            pnlHeader = new Panel();
            lblTitle = new Label();
            lblAccount = new Label();
            cboAccount = new ComboBox();
            lblStatusConn = new Label();
            pnlMarketSwitch = new Panel();
            rbDomestic = new RadioButton();
            rbForeign = new RadioButton();
            pnlDomestic = new Panel();
            lblDomProd = new Label();
            rbTXO = new RadioButton();
            rbTXF = new RadioButton();
            rbMXF = new RadioButton();
            rbTMF = new RadioButton();
            lblDomYM = new Label();
            cboDomYM = new ComboBox();
            lblDomCP = new Label();
            cboDomCP = new ComboBox();
            lblDomStrike = new Label();
            cboDomStrike = new ComboBox();
            lblDomSymbol = new Label();
            txtDomSymbol = new TextBox();
            pnlForeign = new Panel();
            lblForExch = new Label();
            cboForExch = new ComboBox();
            lblForSymbol = new Label();
            cboForSymbol = new ComboBox();
            lblForYM = new Label();
            cboForYM = new ComboBox();
            lblForFullCode = new Label();
            txtForFullCode = new TextBox();
            pnlConditions = new Panel();
            lblPriceType = new Label();
            cboPriceType = new ComboBox();
            lblTIF = new Label();
            cboTIF = new ComboBox();
            lblPosEffect = new Label();
            cboPosEffect = new ComboBox();
            pnlPriceQty = new Panel();
            lblPrice = new Label();
            nudPrice = new NumericUpDown();
            btnPriceMarket = new Button();
            btnPriceOpponent = new Button();
            btnPriceSame = new Button();
            btnPriceMatch = new Button();
            lblQty = new Label();
            nudQty = new NumericUpDown();
            btnQtyMinus1 = new Button();
            btnQtyPlus1 = new Button();
            btnQtyPlus5 = new Button();
            btnQtyPlus10 = new Button();
            pnlActions = new Panel();
            btnBuy = new Button();
            btnSell = new Button();
            chkConfirm = new CheckBox();
            btnOpenReport = new Button();
            pnlBottom = new Panel();
            lblMsg = new Label();
            pnlHeader.SuspendLayout();
            pnlMarketSwitch.SuspendLayout();
            pnlDomestic.SuspendLayout();
            pnlForeign.SuspendLayout();
            pnlConditions.SuspendLayout();
            pnlPriceQty.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)nudPrice).BeginInit();
            ((System.ComponentModel.ISupportInitialize)nudQty).BeginInit();
            pnlActions.SuspendLayout();
            pnlBottom.SuspendLayout();
            SuspendLayout();
            // 
            // pnlHeader
            // 
            pnlHeader.BackColor = Color.FromArgb(32, 34, 40);
            pnlHeader.Controls.Add(lblTitle);
            pnlHeader.Controls.Add(lblAccount);
            pnlHeader.Controls.Add(cboAccount);
            pnlHeader.Controls.Add(lblStatusConn);
            pnlHeader.Dock = DockStyle.Top;
            pnlHeader.Location = new Point(0, 0);
            pnlHeader.Name = "pnlHeader";
            pnlHeader.Size = new Size(540, 48);
            pnlHeader.TabIndex = 0;
            // 
            // lblTitle
            // 
            lblTitle.AutoSize = true;
            lblTitle.Font = new Font("Microsoft JhengHei UI", 12F, FontStyle.Bold, GraphicsUnit.Point);
            lblTitle.ForeColor = Color.FromArgb(0, 210, 255);
            lblTitle.Location = new Point(12, 13);
            lblTitle.Name = "lblTitle";
            lblTitle.Size = new Size(110, 20);
            lblTitle.TabIndex = 0;
            lblTitle.Text = "🎯 智能下單匣";
            // 
            // lblAccount
            // 
            lblAccount.AutoSize = true;
            lblAccount.ForeColor = Color.Silver;
            lblAccount.Location = new Point(145, 16);
            lblAccount.Name = "lblAccount";
            lblAccount.Size = new Size(34, 15);
            lblAccount.TabIndex = 1;
            lblAccount.Text = "帳號:";
            // 
            // cboAccount
            // 
            cboAccount.BackColor = Color.FromArgb(40, 44, 52);
            cboAccount.DropDownStyle = ComboBoxStyle.DropDownList;
            cboAccount.FlatStyle = FlatStyle.Flat;
            cboAccount.ForeColor = Color.White;
            cboAccount.Location = new Point(185, 12);
            cboAccount.Name = "cboAccount";
            cboAccount.Size = new Size(160, 23);
            cboAccount.TabIndex = 1;
            // 
            // lblStatusConn
            // 
            lblStatusConn.AutoSize = true;
            lblStatusConn.ForeColor = Color.FromArgb(80, 220, 100);
            lblStatusConn.Location = new Point(360, 16);
            lblStatusConn.Name = "lblStatusConn";
            lblStatusConn.Size = new Size(118, 15);
            lblStatusConn.TabIndex = 2;
            lblStatusConn.Text = "● 交易伺服器已連線";
            // 
            // pnlMarketSwitch
            // 
            pnlMarketSwitch.BackColor = Color.FromArgb(24, 26, 32);
            pnlMarketSwitch.Controls.Add(rbDomestic);
            pnlMarketSwitch.Controls.Add(rbForeign);
            pnlMarketSwitch.Dock = DockStyle.Top;
            pnlMarketSwitch.Location = new Point(0, 48);
            pnlMarketSwitch.Name = "pnlMarketSwitch";
            pnlMarketSwitch.Size = new Size(540, 38);
            pnlMarketSwitch.TabIndex = 1;
            // 
            // rbDomestic
            // 
            rbDomestic.AutoSize = true;
            rbDomestic.Checked = true;
            rbDomestic.Font = new Font("Microsoft JhengHei UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point);
            rbDomestic.ForeColor = Color.FromArgb(255, 200, 80);
            rbDomestic.Location = new Point(20, 9);
            rbDomestic.Name = "rbDomestic";
            rbDomestic.Size = new Size(169, 21);
            rbDomestic.TabIndex = 0;
            rbDomestic.TabStop = true;
            rbDomestic.Text = "🇹🇼 國內期權 (Domestic)";
            rbDomestic.CheckedChanged += MarketSwitch_CheckedChanged;
            // 
            // rbForeign
            // 
            rbForeign.AutoSize = true;
            rbForeign.Font = new Font("Microsoft JhengHei UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point);
            rbForeign.ForeColor = Color.FromArgb(120, 200, 255);
            rbForeign.Location = new Point(220, 9);
            rbForeign.Name = "rbForeign";
            rbForeign.Size = new Size(159, 21);
            rbForeign.TabIndex = 1;
            rbForeign.Text = "🌐 國外期貨 (Foreign)";
            rbForeign.CheckedChanged += MarketSwitch_CheckedChanged;
            // 
            // pnlDomestic
            // 
            pnlDomestic.BackColor = Color.FromArgb(28, 30, 36);
            pnlDomestic.BorderStyle = BorderStyle.FixedSingle;
            pnlDomestic.Controls.Add(lblDomProd);
            pnlDomestic.Controls.Add(rbTXO);
            pnlDomestic.Controls.Add(rbTXF);
            pnlDomestic.Controls.Add(rbMXF);
            pnlDomestic.Controls.Add(rbTMF);
            pnlDomestic.Controls.Add(lblDomYM);
            pnlDomestic.Controls.Add(cboDomYM);
            pnlDomestic.Controls.Add(lblDomCP);
            pnlDomestic.Controls.Add(cboDomCP);
            pnlDomestic.Controls.Add(lblDomStrike);
            pnlDomestic.Controls.Add(cboDomStrike);
            pnlDomestic.Controls.Add(lblDomSymbol);
            pnlDomestic.Controls.Add(txtDomSymbol);
            pnlDomestic.Location = new Point(12, 92);
            pnlDomestic.Name = "pnlDomestic";
            pnlDomestic.Size = new Size(516, 95);
            pnlDomestic.TabIndex = 2;
            // 
            // lblDomProd
            // 
            lblDomProd.AutoSize = true;
            lblDomProd.ForeColor = Color.Silver;
            lblDomProd.Location = new Point(10, 12);
            lblDomProd.Name = "lblDomProd";
            lblDomProd.Size = new Size(34, 15);
            lblDomProd.TabIndex = 0;
            lblDomProd.Text = "商品:";
            // 
            // rbTXO
            // 
            rbTXO.AutoSize = true;
            rbTXO.Checked = true;
            rbTXO.ForeColor = Color.White;
            rbTXO.Location = new Point(45, 10);
            rbTXO.Name = "rbTXO";
            rbTXO.Size = new Size(97, 19);
            rbTXO.TabIndex = 1;
            rbTXO.TabStop = true;
            rbTXO.Text = "選擇權 (TXO)";
            rbTXO.CheckedChanged += DomProduct_CheckedChanged;
            // 
            // rbTXF
            // 
            rbTXF.AutoSize = true;
            rbTXF.ForeColor = Color.White;
            rbTXF.Location = new Point(148, 10);
            rbTXF.Name = "rbTXF";
            rbTXF.Size = new Size(93, 19);
            rbTXF.TabIndex = 2;
            rbTXF.Text = "臺指期 (TXF)";
            rbTXF.CheckedChanged += DomProduct_CheckedChanged;
            // 
            // rbMXF
            // 
            rbMXF.AutoSize = true;
            rbMXF.ForeColor = Color.White;
            rbMXF.Location = new Point(248, 10);
            rbMXF.Name = "rbMXF";
            rbMXF.Size = new Size(98, 19);
            rbMXF.TabIndex = 3;
            rbMXF.Text = "小臺指 (MXF)";
            rbMXF.CheckedChanged += DomProduct_CheckedChanged;
            // 
            // rbTMF
            // 
            rbTMF.AutoSize = true;
            rbTMF.ForeColor = Color.White;
            rbTMF.Location = new Point(352, 10);
            rbTMF.Name = "rbTMF";
            rbTMF.Size = new Size(98, 19);
            rbTMF.TabIndex = 4;
            rbTMF.Text = "微台指 (TMF)";
            rbTMF.CheckedChanged += DomProduct_CheckedChanged;
            // 
            // lblDomYM
            // 
            lblDomYM.AutoSize = true;
            lblDomYM.ForeColor = Color.Silver;
            lblDomYM.Location = new Point(10, 40);
            lblDomYM.Name = "lblDomYM";
            lblDomYM.Size = new Size(34, 15);
            lblDomYM.TabIndex = 5;
            lblDomYM.Text = "年月:";
            // 
            // cboDomYM
            // 
            cboDomYM.BackColor = Color.FromArgb(40, 44, 52);
            cboDomYM.DropDownStyle = ComboBoxStyle.DropDownList;
            cboDomYM.FlatStyle = FlatStyle.Flat;
            cboDomYM.ForeColor = Color.White;
            cboDomYM.Location = new Point(50, 36);
            cboDomYM.Name = "cboDomYM";
            cboDomYM.Size = new Size(95, 23);
            cboDomYM.TabIndex = 6;
            cboDomYM.SelectedIndexChanged += DomInputs_Changed;
            // 
            // lblDomCP
            // 
            lblDomCP.AutoSize = true;
            lblDomCP.ForeColor = Color.Silver;
            lblDomCP.Location = new Point(155, 40);
            lblDomCP.Name = "lblDomCP";
            lblDomCP.Size = new Size(30, 15);
            lblDomCP.TabIndex = 7;
            lblDomCP.Text = "C/P:";
            // 
            // cboDomCP
            // 
            cboDomCP.BackColor = Color.FromArgb(40, 44, 52);
            cboDomCP.DropDownStyle = ComboBoxStyle.DropDownList;
            cboDomCP.FlatStyle = FlatStyle.Flat;
            cboDomCP.ForeColor = Color.White;
            cboDomCP.Location = new Point(190, 36);
            cboDomCP.Name = "cboDomCP";
            cboDomCP.Size = new Size(75, 23);
            cboDomCP.TabIndex = 8;
            cboDomCP.SelectedIndexChanged += DomInputs_Changed;
            // 
            // lblDomStrike
            // 
            lblDomStrike.AutoSize = true;
            lblDomStrike.ForeColor = Color.Silver;
            lblDomStrike.Location = new Point(275, 40);
            lblDomStrike.Name = "lblDomStrike";
            lblDomStrike.Size = new Size(46, 15);
            lblDomStrike.TabIndex = 9;
            lblDomStrike.Text = "履約價:";
            // 
            // cboDomStrike
            // 
            cboDomStrike.BackColor = Color.FromArgb(40, 44, 52);
            cboDomStrike.FlatStyle = FlatStyle.Flat;
            cboDomStrike.ForeColor = Color.White;
            cboDomStrike.Location = new Point(325, 36);
            cboDomStrike.Name = "cboDomStrike";
            cboDomStrike.Size = new Size(85, 23);
            cboDomStrike.TabIndex = 10;
            cboDomStrike.SelectedIndexChanged += DomInputs_Changed;
            cboDomStrike.TextChanged += DomInputs_Changed;
            // 
            // lblDomSymbol
            // 
            lblDomSymbol.AutoSize = true;
            lblDomSymbol.ForeColor = Color.Silver;
            lblDomSymbol.Location = new Point(10, 68);
            lblDomSymbol.Name = "lblDomSymbol";
            lblDomSymbol.Size = new Size(58, 15);
            lblDomSymbol.TabIndex = 11;
            lblDomSymbol.Text = "商品全碼:";
            // 
            // txtDomSymbol
            // 
            txtDomSymbol.BackColor = Color.FromArgb(32, 34, 40);
            txtDomSymbol.BorderStyle = BorderStyle.FixedSingle;
            txtDomSymbol.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
            txtDomSymbol.ForeColor = Color.FromArgb(0, 210, 255);
            txtDomSymbol.Location = new Point(74, 65);
            txtDomSymbol.Name = "txtDomSymbol";
            txtDomSymbol.ReadOnly = false;
            txtDomSymbol.Size = new Size(336, 24);
            txtDomSymbol.TabIndex = 12;
            // 
            // pnlForeign
            // 
            pnlForeign.BackColor = Color.FromArgb(28, 30, 36);
            pnlForeign.BorderStyle = BorderStyle.FixedSingle;
            pnlForeign.Controls.Add(lblForExch);
            pnlForeign.Controls.Add(cboForExch);
            pnlForeign.Controls.Add(lblForSymbol);
            pnlForeign.Controls.Add(cboForSymbol);
            pnlForeign.Controls.Add(lblForYM);
            pnlForeign.Controls.Add(cboForYM);
            pnlForeign.Controls.Add(lblForFullCode);
            pnlForeign.Controls.Add(txtForFullCode);
            pnlForeign.Location = new Point(12, 92);
            pnlForeign.Name = "pnlForeign";
            pnlForeign.Size = new Size(516, 95);
            pnlForeign.TabIndex = 3;
            pnlForeign.Visible = false;
            // 
            // lblForExch
            // 
            lblForExch.AutoSize = true;
            lblForExch.ForeColor = Color.Silver;
            lblForExch.Location = new Point(10, 15);
            lblForExch.Name = "lblForExch";
            lblForExch.Size = new Size(46, 15);
            lblForExch.TabIndex = 0;
            lblForExch.Text = "交易所:";
            // 
            // cboForExch
            // 
            cboForExch.BackColor = Color.FromArgb(40, 44, 52);
            cboForExch.DropDownStyle = ComboBoxStyle.DropDownList;
            cboForExch.FlatStyle = FlatStyle.Flat;
            cboForExch.ForeColor = Color.White;
            cboForExch.Location = new Point(60, 12);
            cboForExch.Name = "cboForExch";
            cboForExch.Size = new Size(85, 23);
            cboForExch.TabIndex = 1;
            cboForExch.SelectedIndexChanged += ForInputs_Changed;
            // 
            // lblForSymbol
            // 
            lblForSymbol.AutoSize = true;
            lblForSymbol.ForeColor = Color.Silver;
            lblForSymbol.Location = new Point(155, 15);
            lblForSymbol.Name = "lblForSymbol";
            lblForSymbol.Size = new Size(34, 15);
            lblForSymbol.TabIndex = 2;
            lblForSymbol.Text = "商品:";
            // 
            // cboForSymbol
            // 
            cboForSymbol.BackColor = Color.FromArgb(40, 44, 52);
            cboForSymbol.FlatStyle = FlatStyle.Flat;
            cboForSymbol.ForeColor = Color.White;
            cboForSymbol.Location = new Point(195, 12);
            cboForSymbol.Name = "cboForSymbol";
            cboForSymbol.Size = new Size(185, 23);
            cboForSymbol.TabIndex = 3;
            cboForSymbol.SelectedIndexChanged += ForInputs_Changed;
            cboForSymbol.TextChanged += ForInputs_Changed;
            // 
            // lblForYM
            // 
            lblForYM.AutoSize = true;
            lblForYM.ForeColor = Color.Silver;
            lblForYM.Location = new Point(390, 15);
            lblForYM.Name = "lblForYM";
            lblForYM.Size = new Size(34, 15);
            lblForYM.TabIndex = 4;
            lblForYM.Text = "年月:";
            // 
            // cboForYM
            // 
            cboForYM.BackColor = Color.FromArgb(40, 44, 52);
            cboForYM.DropDownStyle = ComboBoxStyle.DropDownList;
            cboForYM.FlatStyle = FlatStyle.Flat;
            cboForYM.ForeColor = Color.White;
            cboForYM.Location = new Point(430, 12);
            cboForYM.Name = "cboForYM";
            cboForYM.Size = new Size(75, 23);
            cboForYM.TabIndex = 5;
            cboForYM.SelectedIndexChanged += ForInputs_Changed;
            // 
            // lblForFullCode
            // 
            lblForFullCode.AutoSize = true;
            lblForFullCode.ForeColor = Color.Silver;
            lblForFullCode.Location = new Point(10, 55);
            lblForFullCode.Name = "lblForFullCode";
            lblForFullCode.Size = new Size(58, 15);
            lblForFullCode.TabIndex = 6;
            lblForFullCode.Text = "商品全碼:";
            // 
            // txtForFullCode
            // 
            txtForFullCode.BackColor = Color.FromArgb(32, 34, 40);
            txtForFullCode.BorderStyle = BorderStyle.FixedSingle;
            txtForFullCode.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
            txtForFullCode.ForeColor = Color.FromArgb(120, 200, 255);
            txtForFullCode.Location = new Point(74, 52);
            txtForFullCode.Name = "txtForFullCode";
            txtForFullCode.ReadOnly = false;
            txtForFullCode.Size = new Size(431, 24);
            txtForFullCode.TabIndex = 7;
            // 
            // pnlConditions
            // 
            pnlConditions.BackColor = Color.FromArgb(28, 30, 36);
            pnlConditions.BorderStyle = BorderStyle.FixedSingle;
            pnlConditions.Controls.Add(lblPriceType);
            pnlConditions.Controls.Add(cboPriceType);
            pnlConditions.Controls.Add(lblTIF);
            pnlConditions.Controls.Add(cboTIF);
            pnlConditions.Controls.Add(lblPosEffect);
            pnlConditions.Controls.Add(cboPosEffect);
            pnlConditions.Location = new Point(12, 195);
            pnlConditions.Name = "pnlConditions";
            pnlConditions.Size = new Size(516, 45);
            pnlConditions.TabIndex = 4;
            // 
            // lblPriceType
            // 
            lblPriceType.AutoSize = true;
            lblPriceType.ForeColor = Color.Silver;
            lblPriceType.Location = new Point(10, 14);
            lblPriceType.Name = "lblPriceType";
            lblPriceType.Size = new Size(34, 15);
            lblPriceType.TabIndex = 0;
            lblPriceType.Text = "類別:";
            // 
            // cboPriceType
            // 
            cboPriceType.BackColor = Color.FromArgb(40, 44, 52);
            cboPriceType.DropDownStyle = ComboBoxStyle.DropDownList;
            cboPriceType.FlatStyle = FlatStyle.Flat;
            cboPriceType.ForeColor = Color.White;
            cboPriceType.Location = new Point(48, 10);
            cboPriceType.Name = "cboPriceType";
            cboPriceType.Size = new Size(110, 23);
            cboPriceType.TabIndex = 1;
            cboPriceType.SelectedIndexChanged += PriceType_SelectedIndexChanged;
            // 
            // lblTIF
            // 
            lblTIF.AutoSize = true;
            lblTIF.ForeColor = Color.Silver;
            lblTIF.Location = new Point(175, 14);
            lblTIF.Name = "lblTIF";
            lblTIF.Size = new Size(34, 15);
            lblTIF.TabIndex = 2;
            lblTIF.Text = "條件:";
            // 
            // cboTIF
            // 
            cboTIF.BackColor = Color.FromArgb(40, 44, 52);
            cboTIF.DropDownStyle = ComboBoxStyle.DropDownList;
            cboTIF.FlatStyle = FlatStyle.Flat;
            cboTIF.ForeColor = Color.White;
            cboTIF.Location = new Point(215, 10);
            cboTIF.Name = "cboTIF";
            cboTIF.Size = new Size(80, 23);
            cboTIF.TabIndex = 3;
            // 
            // lblPosEffect
            // 
            lblPosEffect.AutoSize = true;
            lblPosEffect.ForeColor = Color.Silver;
            lblPosEffect.Location = new Point(310, 14);
            lblPosEffect.Name = "lblPosEffect";
            lblPosEffect.Size = new Size(34, 15);
            lblPosEffect.TabIndex = 4;
            lblPosEffect.Text = "倉別:";
            // 
            // cboPosEffect
            // 
            cboPosEffect.BackColor = Color.FromArgb(40, 44, 52);
            cboPosEffect.DropDownStyle = ComboBoxStyle.DropDownList;
            cboPosEffect.FlatStyle = FlatStyle.Flat;
            cboPosEffect.ForeColor = Color.White;
            cboPosEffect.Location = new Point(350, 10);
            cboPosEffect.Name = "cboPosEffect";
            cboPosEffect.Size = new Size(100, 23);
            cboPosEffect.TabIndex = 5;
            // 
            // pnlPriceQty
            // 
            pnlPriceQty.BackColor = Color.FromArgb(28, 30, 36);
            pnlPriceQty.BorderStyle = BorderStyle.FixedSingle;
            pnlPriceQty.Controls.Add(lblPrice);
            pnlPriceQty.Controls.Add(nudPrice);
            pnlPriceQty.Controls.Add(btnPriceMarket);
            pnlPriceQty.Controls.Add(btnPriceOpponent);
            pnlPriceQty.Controls.Add(btnPriceSame);
            pnlPriceQty.Controls.Add(btnPriceMatch);
            pnlPriceQty.Controls.Add(lblQty);
            pnlPriceQty.Controls.Add(nudQty);
            pnlPriceQty.Controls.Add(btnQtyMinus1);
            pnlPriceQty.Controls.Add(btnQtyPlus1);
            pnlPriceQty.Controls.Add(btnQtyPlus5);
            pnlPriceQty.Controls.Add(btnQtyPlus10);
            pnlPriceQty.Location = new Point(12, 248);
            pnlPriceQty.Name = "pnlPriceQty";
            pnlPriceQty.Size = new Size(516, 95);
            pnlPriceQty.TabIndex = 5;
            // 
            // lblPrice
            // 
            lblPrice.AutoSize = true;
            lblPrice.ForeColor = Color.Silver;
            lblPrice.Location = new Point(10, 18);
            lblPrice.Name = "lblPrice";
            lblPrice.Size = new Size(58, 15);
            lblPrice.TabIndex = 0;
            lblPrice.Text = "委託價格:";
            // 
            // nudPrice
            // 
            nudPrice.BackColor = Color.FromArgb(18, 20, 24);
            nudPrice.BorderStyle = BorderStyle.FixedSingle;
            nudPrice.DecimalPlaces = 1;
            nudPrice.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            nudPrice.ForeColor = Color.FromArgb(255, 230, 100);
            nudPrice.Location = new Point(74, 12);
            nudPrice.Maximum = new decimal(new int[] { 999999, 0, 0, 0 });
            nudPrice.Name = "nudPrice";
            nudPrice.Size = new Size(115, 27);
            nudPrice.TabIndex = 1;
            nudPrice.TextAlign = HorizontalAlignment.Right;
            // 
            // btnPriceOpponent
            // 
            btnPriceOpponent.BackColor = Color.FromArgb(45, 48, 56);
            btnPriceOpponent.FlatStyle = FlatStyle.Flat;
            btnPriceOpponent.ForeColor = Color.White;
            btnPriceOpponent.Location = new Point(198, 12);
            btnPriceOpponent.Name = "btnPriceOpponent";
            btnPriceOpponent.Size = new Size(68, 27);
            btnPriceOpponent.TabIndex = 2;
            btnPriceOpponent.Text = "對手價";
            btnPriceOpponent.UseVisualStyleBackColor = false;
            btnPriceOpponent.Click += QuickPrice_Click;
            // 
            // btnPriceSame
            // 
            btnPriceSame.BackColor = Color.FromArgb(45, 48, 56);
            btnPriceSame.FlatStyle = FlatStyle.Flat;
            btnPriceSame.ForeColor = Color.White;
            btnPriceSame.Location = new Point(272, 12);
            btnPriceSame.Name = "btnPriceSame";
            btnPriceSame.Size = new Size(68, 27);
            btnPriceSame.TabIndex = 3;
            btnPriceSame.Text = "本方價";
            btnPriceSame.UseVisualStyleBackColor = false;
            btnPriceSame.Click += QuickPrice_Click;
            // 
            // btnPriceMatch
            // 
            btnPriceMatch.BackColor = Color.FromArgb(45, 48, 56);
            btnPriceMatch.FlatStyle = FlatStyle.Flat;
            btnPriceMatch.ForeColor = Color.White;
            btnPriceMatch.Location = new Point(346, 12);
            btnPriceMatch.Name = "btnPriceMatch";
            btnPriceMatch.Size = new Size(68, 27);
            btnPriceMatch.TabIndex = 4;
            btnPriceMatch.Text = "成交價";
            btnPriceMatch.UseVisualStyleBackColor = false;
            btnPriceMatch.Click += QuickPrice_Click;
            // 
            // btnPriceMarket
            // 
            btnPriceMarket.BackColor = Color.FromArgb(50, 45, 30);
            btnPriceMarket.FlatStyle = FlatStyle.Flat;
            btnPriceMarket.ForeColor = Color.FromArgb(255, 180, 50);
            btnPriceMarket.Location = new Point(420, 12);
            btnPriceMarket.Name = "btnPriceMarket";
            btnPriceMarket.Size = new Size(68, 27);
            btnPriceMarket.TabIndex = 5;
            btnPriceMarket.Text = "市價";
            btnPriceMarket.UseVisualStyleBackColor = false;
            btnPriceMarket.Click += btnPriceMarket_Click;
            // 
            // lblQty
            // 
            lblQty.AutoSize = true;
            lblQty.ForeColor = Color.Silver;
            lblQty.Location = new Point(10, 58);
            lblQty.Name = "lblQty";
            lblQty.Size = new Size(58, 15);
            lblQty.TabIndex = 6;
            lblQty.Text = "委託口數:";
            // 
            // nudQty
            // 
            nudQty.BackColor = Color.FromArgb(18, 20, 24);
            nudQty.BorderStyle = BorderStyle.FixedSingle;
            nudQty.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            nudQty.ForeColor = Color.FromArgb(100, 240, 255);
            nudQty.Location = new Point(74, 52);
            nudQty.Maximum = new decimal(new int[] { 999, 0, 0, 0 });
            nudQty.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            nudQty.Name = "nudQty";
            nudQty.Size = new Size(115, 27);
            nudQty.TabIndex = 7;
            nudQty.TextAlign = HorizontalAlignment.Right;
            nudQty.Value = new decimal(new int[] { 1, 0, 0, 0 });
            // 
            // btnQtyMinus1
            // 
            btnQtyMinus1.BackColor = Color.FromArgb(45, 48, 56);
            btnQtyMinus1.FlatStyle = FlatStyle.Flat;
            btnQtyMinus1.ForeColor = Color.White;
            btnQtyMinus1.Location = new Point(198, 52);
            btnQtyMinus1.Name = "btnQtyMinus1";
            btnQtyMinus1.Size = new Size(55, 27);
            btnQtyMinus1.TabIndex = 8;
            btnQtyMinus1.Text = "-1";
            btnQtyMinus1.UseVisualStyleBackColor = false;
            btnQtyMinus1.Click += btnQtyMinus1_Click;
            // 
            // btnQtyPlus1
            // 
            btnQtyPlus1.BackColor = Color.FromArgb(45, 48, 56);
            btnQtyPlus1.FlatStyle = FlatStyle.Flat;
            btnQtyPlus1.ForeColor = Color.White;
            btnQtyPlus1.Location = new Point(259, 52);
            btnQtyPlus1.Name = "btnQtyPlus1";
            btnQtyPlus1.Size = new Size(55, 27);
            btnQtyPlus1.TabIndex = 9;
            btnQtyPlus1.Text = "+1";
            btnQtyPlus1.UseVisualStyleBackColor = false;
            btnQtyPlus1.Click += btnQtyPlus1_Click;
            // 
            // btnQtyPlus5
            // 
            btnQtyPlus5.BackColor = Color.FromArgb(45, 48, 56);
            btnQtyPlus5.FlatStyle = FlatStyle.Flat;
            btnQtyPlus5.ForeColor = Color.White;
            btnQtyPlus5.Location = new Point(320, 52);
            btnQtyPlus5.Name = "btnQtyPlus5";
            btnQtyPlus5.Size = new Size(55, 27);
            btnQtyPlus5.TabIndex = 10;
            btnQtyPlus5.Text = "+5";
            btnQtyPlus5.UseVisualStyleBackColor = false;
            btnQtyPlus5.Click += btnQtyPlus5_Click;
            // 
            // btnQtyPlus10
            // 
            btnQtyPlus10.BackColor = Color.FromArgb(45, 48, 56);
            btnQtyPlus10.FlatStyle = FlatStyle.Flat;
            btnQtyPlus10.ForeColor = Color.White;
            btnQtyPlus10.Location = new Point(381, 52);
            btnQtyPlus10.Name = "btnQtyPlus10";
            btnQtyPlus10.Size = new Size(55, 27);
            btnQtyPlus10.TabIndex = 11;
            btnQtyPlus10.Text = "+10";
            btnQtyPlus10.UseVisualStyleBackColor = false;
            btnQtyPlus10.Click += btnQtyPlus10_Click;
            // 
            // pnlActions
            // 
            pnlActions.BackColor = Color.FromArgb(24, 26, 32);
            pnlActions.Controls.Add(btnBuy);
            pnlActions.Controls.Add(btnSell);
            pnlActions.Controls.Add(chkConfirm);
            pnlActions.Controls.Add(btnOpenReport);
            pnlActions.Location = new Point(12, 350);
            pnlActions.Name = "pnlActions";
            pnlActions.Size = new Size(516, 85);
            pnlActions.TabIndex = 6;
            // 
            // btnBuy
            // 
            btnBuy.BackColor = Color.FromArgb(210, 45, 60);
            btnBuy.FlatAppearance.BorderSize = 0;
            btnBuy.FlatStyle = FlatStyle.Flat;
            btnBuy.Font = new Font("Microsoft JhengHei UI", 12F, FontStyle.Bold);
            btnBuy.ForeColor = Color.White;
            btnBuy.Location = new Point(10, 10);
            btnBuy.Name = "btnBuy";
            btnBuy.Size = new Size(185, 42);
            btnBuy.TabIndex = 0;
            btnBuy.Text = "買進 (BUY)";
            btnBuy.UseVisualStyleBackColor = false;
            btnBuy.Click += BtnBuy_Click;
            // 
            // btnSell
            // 
            btnSell.BackColor = Color.FromArgb(35, 155, 65);
            btnSell.FlatAppearance.BorderSize = 0;
            btnSell.FlatStyle = FlatStyle.Flat;
            btnSell.Font = new Font("Microsoft JhengHei UI", 12F, FontStyle.Bold);
            btnSell.ForeColor = Color.White;
            btnSell.Location = new Point(205, 10);
            btnSell.Name = "btnSell";
            btnSell.Size = new Size(185, 42);
            btnSell.TabIndex = 1;
            btnSell.Text = "賣出 (SELL)";
            btnSell.UseVisualStyleBackColor = false;
            btnSell.Click += BtnSell_Click;
            // 
            // btnOpenReport
            // 
            btnOpenReport.BackColor = Color.FromArgb(45, 48, 56);
            btnOpenReport.FlatStyle = FlatStyle.Flat;
            btnOpenReport.ForeColor = Color.Silver;
            btnOpenReport.Location = new Point(400, 10);
            btnOpenReport.Name = "btnOpenReport";
            btnOpenReport.Size = new Size(105, 42);
            btnOpenReport.TabIndex = 2;
            btnOpenReport.Text = "委託回報 ➔";
            btnOpenReport.UseVisualStyleBackColor = false;
            btnOpenReport.Click += BtnOpenReport_Click;
            // 
            // chkConfirm
            // 
            chkConfirm.AutoSize = true;
            chkConfirm.Checked = true;
            chkConfirm.CheckState = CheckState.Checked;
            chkConfirm.ForeColor = Color.Silver;
            chkConfirm.Location = new Point(14, 58);
            chkConfirm.Name = "chkConfirm";
            chkConfirm.Size = new Size(86, 19);
            chkConfirm.TabIndex = 3;
            chkConfirm.Text = "下單前請確認";
            chkConfirm.UseVisualStyleBackColor = true;
            // 
            // pnlBottom
            // 
            pnlBottom.BackColor = Color.FromArgb(18, 20, 24);
            pnlBottom.Controls.Add(lblMsg);
            pnlBottom.Dock = DockStyle.Bottom;
            pnlBottom.Location = new Point(0, 442);
            pnlBottom.Name = "pnlBottom";
            pnlBottom.Size = new Size(540, 32);
            pnlBottom.TabIndex = 7;
            // 
            // lblMsg
            // 
            lblMsg.AutoSize = true;
            lblMsg.ForeColor = Color.Silver;
            lblMsg.Location = new Point(12, 8);
            lblMsg.Name = "lblMsg";
            lblMsg.Size = new Size(71, 15);
            lblMsg.TabIndex = 0;
            lblMsg.Text = "狀態: 待命中";
            // 
            // OrderPlacementForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(20, 22, 26);
            ClientSize = new Size(540, 474);
            Controls.Add(pnlBottom);
            Controls.Add(pnlActions);
            Controls.Add(pnlPriceQty);
            Controls.Add(pnlConditions);
            Controls.Add(pnlDomestic);
            Controls.Add(pnlForeign);
            Controls.Add(pnlMarketSwitch);
            Controls.Add(pnlHeader);
            Font = new Font("Microsoft JhengHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            Name = "OrderPlacementForm";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "🎯 下單匣 (Order Placement)";
            FormClosing += OrderPlacementForm_FormClosing;
            Load += OrderPlacementForm_Load;
            pnlHeader.ResumeLayout(false);
            pnlHeader.PerformLayout();
            pnlMarketSwitch.ResumeLayout(false);
            pnlMarketSwitch.PerformLayout();
            pnlDomestic.ResumeLayout(false);
            pnlDomestic.PerformLayout();
            pnlForeign.ResumeLayout(false);
            pnlForeign.PerformLayout();
            pnlConditions.ResumeLayout(false);
            pnlConditions.PerformLayout();
            pnlPriceQty.ResumeLayout(false);
            pnlPriceQty.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)nudPrice).EndInit();
            ((System.ComponentModel.ISupportInitialize)nudQty).EndInit();
            pnlActions.ResumeLayout(false);
            pnlActions.PerformLayout();
            pnlBottom.ResumeLayout(false);
            pnlBottom.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private Panel pnlHeader;
        private Label lblTitle;
        private Label lblAccount;
        private ComboBox cboAccount;
        private Label lblStatusConn;
        private Panel pnlMarketSwitch;
        private RadioButton rbDomestic;
        private RadioButton rbForeign;
        private Panel pnlDomestic;
        private Label lblDomProd;
        private RadioButton rbTXO;
        private RadioButton rbTXF;
        private RadioButton rbMXF;
        private RadioButton rbTMF;
        private Label lblDomYM;
        private ComboBox cboDomYM;
        private Label lblDomCP;
        private ComboBox cboDomCP;
        private Label lblDomStrike;
        private ComboBox cboDomStrike;
        private Label lblDomSymbol;
        private TextBox txtDomSymbol;
        private Panel pnlForeign;
        private Label lblForExch;
        private ComboBox cboForExch;
        private Label lblForSymbol;
        private ComboBox cboForSymbol;
        private Label lblForYM;
        private ComboBox cboForYM;
        private Label lblForFullCode;
        private TextBox txtForFullCode;
        private Panel pnlConditions;
        private Label lblPriceType;
        private ComboBox cboPriceType;
        private Label lblTIF;
        private ComboBox cboTIF;
        private Label lblPosEffect;
        private ComboBox cboPosEffect;
        private Panel pnlPriceQty;
        private Label lblPrice;
        private NumericUpDown nudPrice;
        private Button btnPriceMarket;
        private Button btnPriceOpponent;
        private Button btnPriceSame;
        private Button btnPriceMatch;
        private Label lblQty;
        private NumericUpDown nudQty;
        private Button btnQtyMinus1;
        private Button btnQtyPlus1;
        private Button btnQtyPlus5;
        private Button btnQtyPlus10;
        private Panel pnlActions;
        private Button btnBuy;
        private Button btnSell;
        private Button btnOpenReport;
        private CheckBox chkConfirm;
        private Panel pnlBottom;
        private Label lblMsg;
    }
}
