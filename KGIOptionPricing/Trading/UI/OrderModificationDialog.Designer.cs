#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace KGIOptionPricing;

public partial class OrderModificationDialog
{
    private System.ComponentModel.IContainer? components = null;
    private Label priceLabel = null!;
    private Label quantityLabel = null!;
    private NumericUpDown priceInput = null!;
    private NumericUpDown quantityInput = null!;
    private Button okButton = null!;
    private Button cancelButton = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing) components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        priceLabel = new Label();
        quantityLabel = new Label();
        priceInput = new NumericUpDown();
        quantityInput = new NumericUpDown();
        okButton = new Button();
        cancelButton = new Button();
        ((System.ComponentModel.ISupportInitialize)priceInput).BeginInit();
        ((System.ComponentModel.ISupportInitialize)quantityInput).BeginInit();
        SuspendLayout();
        priceLabel.AutoSize = true;
        priceLabel.Location = new Point(25, 25);
        priceLabel.Name = "priceLabel";
        priceLabel.Text = "新委託價:";
        priceInput.DecimalPlaces = 1;
        priceInput.Location = new Point(120, 22);
        priceInput.Maximum = 99999;
        priceInput.Name = "priceInput";
        priceInput.Width = 140;
        quantityLabel.AutoSize = true;
        quantityLabel.Location = new Point(25, 65);
        quantityLabel.Name = "quantityLabel";
        quantityLabel.Text = "新剩餘口數:";
        quantityInput.DecimalPlaces = 0;
        quantityInput.Location = new Point(120, 62);
        quantityInput.Maximum = 999;
        quantityInput.Name = "quantityInput";
        quantityInput.Width = 140;
        okButton.BackColor = Color.FromArgb(0, 122, 204);
        okButton.DialogResult = DialogResult.OK;
        okButton.FlatStyle = FlatStyle.Flat;
        okButton.Location = new Point(45, 120);
        okButton.Name = "okButton";
        okButton.Size = new Size(100, 35);
        okButton.Text = "確認修改";
        cancelButton.BackColor = Color.FromArgb(45, 45, 48);
        cancelButton.DialogResult = DialogResult.Cancel;
        cancelButton.FlatStyle = FlatStyle.Flat;
        cancelButton.Location = new Point(160, 120);
        cancelButton.Name = "cancelButton";
        cancelButton.Size = new Size(100, 35);
        cancelButton.Text = "取消";
        AcceptButton = okButton;
        CancelButton = cancelButton;
        BackColor = Color.FromArgb(28, 28, 28);
        ForeColor = Color.White;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Size = new Size(320, 230);
        StartPosition = FormStartPosition.CenterParent;
        Name = "OrderModificationDialog";
        Text = "改價/改量";
        Controls.Add(priceLabel);
        Controls.Add(priceInput);
        Controls.Add(quantityLabel);
        Controls.Add(quantityInput);
        Controls.Add(okButton);
        Controls.Add(cancelButton);
        ((System.ComponentModel.ISupportInitialize)priceInput).EndInit();
        ((System.ComponentModel.ISupportInitialize)quantityInput).EndInit();
        ResumeLayout(false);
        PerformLayout();
    }
}
