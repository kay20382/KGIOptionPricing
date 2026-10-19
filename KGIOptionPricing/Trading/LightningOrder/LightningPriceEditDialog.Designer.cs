#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace KGIOptionPricing;

public partial class LightningPriceEditDialog
{
    private System.ComponentModel.IContainer? components = null;
    private Label priceLabel = null!;
    private NumericUpDown priceInput = null!;
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
        priceInput = new NumericUpDown();
        okButton = new Button();
        cancelButton = new Button();
        ((System.ComponentModel.ISupportInitialize)priceInput).BeginInit();
        SuspendLayout();
        priceLabel.AutoSize = true;
        priceLabel.Location = new Point(16, 18);
        priceLabel.Name = "priceLabel";
        priceLabel.Text = "新委託價";
        priceInput.Location = new Point(92, 14);
        priceInput.Minimum = -100000000;
        priceInput.Maximum = 100000000;
        priceInput.Name = "priceInput";
        priceInput.Width = 170;
        okButton.DialogResult = DialogResult.OK;
        okButton.Location = new Point(106, 58);
        okButton.Name = "okButton";
        okButton.Text = "確定";
        okButton.Width = 75;
        cancelButton.DialogResult = DialogResult.Cancel;
        cancelButton.Location = new Point(187, 58);
        cancelButton.Name = "cancelButton";
        cancelButton.Text = "取消";
        cancelButton.Width = 75;
        AcceptButton = okButton;
        CancelButton = cancelButton;
        ClientSize = new Size(280, 105);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Name = "LightningPriceEditDialog";
        Text = "輸入改價目標";
        Controls.Add(priceLabel);
        Controls.Add(priceInput);
        Controls.Add(okButton);
        Controls.Add(cancelButton);
        ((System.ComponentModel.ISupportInitialize)priceInput).EndInit();
        ResumeLayout(false);
        PerformLayout();
    }
}
