using System;
using System.Drawing;
using System.Windows.Forms;

namespace KGIOptionPricing;

public enum LightningClickMode
{
    SingleClick,
    DoubleClick
}

public sealed class LightningOrderInteractionSettings
{
    public LightningClickMode PlaceClickMode { get; set; } = LightningClickMode.DoubleClick;
    public LightningClickMode CancelClickMode { get; set; } = LightningClickMode.DoubleClick;
    public bool ConfirmPlaceOrder { get; set; }
    public bool ConfirmCancelOrder { get; set; }
}

public sealed partial class LightningOrderSettingsForm : Form
{
    public LightningOrderSettingsForm() => InitializeComponent();

    public LightningOrderSettingsForm(LightningOrderInteractionSettings settings) : this()
    {
        _placeClick.SelectedIndex = settings.PlaceClickMode == LightningClickMode.SingleClick ? 0 : 1;
        _cancelClick.SelectedIndex = settings.CancelClickMode == LightningClickMode.SingleClick ? 0 : 1;
        _confirmPlace.Checked = settings.ConfirmPlaceOrder;
        _confirmCancel.Checked = settings.ConfirmCancelOrder;
    }

    public void ApplyTo(LightningOrderInteractionSettings settings)
    {
        settings.PlaceClickMode = _placeClick.SelectedIndex == 0 ? LightningClickMode.SingleClick : LightningClickMode.DoubleClick;
        settings.CancelClickMode = _cancelClick.SelectedIndex == 0 ? LightningClickMode.SingleClick : LightningClickMode.DoubleClick;
        settings.ConfirmPlaceOrder = _confirmPlace.Checked;
        settings.ConfirmCancelOrder = _confirmCancel.Checked;
    }

}
