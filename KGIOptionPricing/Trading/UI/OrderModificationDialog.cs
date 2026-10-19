using System.Windows.Forms;

namespace KGIOptionPricing;

public partial class OrderModificationDialog : Form
{
    public OrderModificationDialog() => InitializeComponent();

    public OrderModificationDialog(decimal price, int leaves) : this()
    {
        priceInput.Value = price;
        quantityInput.Value = leaves;
    }

    public decimal Price => priceInput.Value;
    public int Quantity => (int)quantityInput.Value;
}
