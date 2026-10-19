#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace KGIOptionPricing;

public sealed partial class LightningOrderSettingsForm
{
    private System.ComponentModel.IContainer? components = null;
    private TableLayoutPanel settingsLayout = null!;
    private Label placeLabel = null!;
    private Label cancelLabel = null!;
    private ComboBox _placeClick = null!;
    private ComboBox _cancelClick = null!;
    private CheckBox _confirmPlace = null!;
    private CheckBox _confirmCancel = null!;
    private FlowLayoutPanel buttonsPanel = null!;
    private Button okButton = null!;
    private Button cancelButton = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing) components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        settingsLayout = new TableLayoutPanel();
        placeLabel = new Label();
        cancelLabel = new Label();
        _placeClick = new ComboBox();
        _cancelClick = new ComboBox();
        _confirmPlace = new CheckBox();
        _confirmCancel = new CheckBox();
        buttonsPanel = new FlowLayoutPanel();
        okButton = new Button();
        cancelButton = new Button();
        settingsLayout.SuspendLayout();
        buttonsPanel.SuspendLayout();
        SuspendLayout();
        settingsLayout.Dock = DockStyle.Fill;
        settingsLayout.Name = "settingsLayout";
        settingsLayout.Padding = new Padding(14);
        settingsLayout.ColumnCount = 2;
        settingsLayout.RowCount = 5;
        settingsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        settingsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        settingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        settingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        settingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        settingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        settingsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        placeLabel.Name = "placeLabel";
        placeLabel.Text = "買量／賣量下單";
        placeLabel.Dock = DockStyle.Fill;
        placeLabel.TextAlign = ContentAlignment.MiddleLeft;
        cancelLabel.Name = "cancelLabel";
        cancelLabel.Text = "委買／委賣刪單";
        cancelLabel.Dock = DockStyle.Fill;
        cancelLabel.TextAlign = ContentAlignment.MiddleLeft;
        _placeClick.Name = "placeClickCombo";
        _placeClick.DropDownStyle = ComboBoxStyle.DropDownList;
        _placeClick.Dock = DockStyle.Fill;
        _placeClick.Items.AddRange(new object[] { "點一下", "點兩下" });
        _cancelClick.Name = "cancelClickCombo";
        _cancelClick.DropDownStyle = ComboBoxStyle.DropDownList;
        _cancelClick.Dock = DockStyle.Fill;
        _cancelClick.Items.AddRange(new object[] { "點一下", "點兩下" });
        _confirmPlace.Name = "confirmPlaceCheck";
        _confirmPlace.Text = "下單前顯示確認視窗";
        _confirmPlace.AutoSize = true;
        _confirmCancel.Name = "confirmCancelCheck";
        _confirmCancel.Text = "刪單前顯示確認視窗";
        _confirmCancel.AutoSize = true;
        buttonsPanel.Name = "buttonsPanel";
        buttonsPanel.Dock = DockStyle.Fill;
        buttonsPanel.FlowDirection = FlowDirection.RightToLeft;
        buttonsPanel.WrapContents = false;
        okButton.Name = "okButton";
        okButton.Text = "確定";
        okButton.DialogResult = DialogResult.OK;
        okButton.Width = 78;
        cancelButton.Name = "cancelButton";
        cancelButton.Text = "取消";
        cancelButton.DialogResult = DialogResult.Cancel;
        cancelButton.Width = 78;
        buttonsPanel.Controls.Add(cancelButton);
        buttonsPanel.Controls.Add(okButton);
        settingsLayout.Controls.Add(placeLabel, 0, 0);
        settingsLayout.Controls.Add(_placeClick, 1, 0);
        settingsLayout.Controls.Add(_confirmPlace, 0, 1);
        settingsLayout.SetColumnSpan(_confirmPlace, 2);
        settingsLayout.Controls.Add(cancelLabel, 0, 2);
        settingsLayout.Controls.Add(_cancelClick, 1, 2);
        settingsLayout.Controls.Add(_confirmCancel, 0, 3);
        settingsLayout.SetColumnSpan(_confirmCancel, 2);
        settingsLayout.Controls.Add(buttonsPanel, 0, 4);
        settingsLayout.SetColumnSpan(buttonsPanel, 2);
        AcceptButton = okButton;
        CancelButton = cancelButton;
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(350, 205);
        Font = new Font("Microsoft JhengHei UI", 10F);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Name = "LightningOrderSettingsForm";
        Text = "閃電下單設定";
        Controls.Add(settingsLayout);
        settingsLayout.ResumeLayout(false);
        settingsLayout.PerformLayout();
        buttonsPanel.ResumeLayout(false);
        ResumeLayout(false);
    }
}
