using System.Windows.Forms;

namespace KGIOptionPricing;

public partial class LightningPriceEditDialog : Form
{
    public LightningPriceEditDialog() => InitializeComponent();

    public LightningPriceEditDialog(decimal current, int decimals) : this()
    {
        priceInput.DecimalPlaces = decimals;
        priceInput.Value = current;
    }

    public decimal Price => priceInput.Value;
}
