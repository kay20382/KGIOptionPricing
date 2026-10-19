#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace KGIOptionPricing;

public sealed partial class InstrumentSelectorForm
{
    private System.ComponentModel.IContainer? components = null;
    private SplitContainer splitContainer = null!;
    private TextBox _search = null!;
    private TreeView _tree = null!;
    private DataGridView _grid = null!;
    private DataGridViewTextBoxColumn monthColumn = null!;
    private DataGridViewTextBoxColumn symbolColumn = null!;
    private DataGridViewTextBoxColumn expiryColumn = null!;
    private DataGridViewTextBoxColumn limitsColumn = null!;
    private Panel footerPanel = null!;
    private Label _selection = null!;
    private Button _apply = null!;
    private Button _applyClose = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing) components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        splitContainer = new SplitContainer();
        _search = new TextBox();
        _tree = new TreeView();
        _grid = new DataGridView();
        monthColumn = new DataGridViewTextBoxColumn();
        symbolColumn = new DataGridViewTextBoxColumn();
        expiryColumn = new DataGridViewTextBoxColumn();
        limitsColumn = new DataGridViewTextBoxColumn();
        footerPanel = new Panel();
        _selection = new Label();
        _apply = new Button();
        _applyClose = new Button();
        ((System.ComponentModel.ISupportInitialize)splitContainer).BeginInit();
        splitContainer.Panel1.SuspendLayout();
        splitContainer.Panel2.SuspendLayout();
        splitContainer.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)_grid).BeginInit();
        footerPanel.SuspendLayout();
        SuspendLayout();
        splitContainer.Dock = DockStyle.Fill;
        splitContainer.Name = "splitContainer";
        splitContainer.SplitterDistance = 300;
        splitContainer.Panel1.Controls.Add(_tree);
        splitContainer.Panel1.Controls.Add(_search);
        splitContainer.Panel2.Controls.Add(_grid);
        _search.Dock = DockStyle.Top;
        _search.Name = "searchTextBox";
        _search.PlaceholderText = "搜尋交易所、商品、月份、代碼或履約價…";
        _tree.Dock = DockStyle.Fill;
        _tree.HideSelection = false;
        _tree.Name = "productTree";
        _grid.Dock = DockStyle.Fill;
        _grid.Name = "contractGrid";
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        monthColumn.Name = "month";
        monthColumn.HeaderText = "月份";
        symbolColumn.Name = "symbol";
        symbolColumn.HeaderText = "完整代碼";
        expiryColumn.Name = "expiry";
        expiryColumn.HeaderText = "到期日";
        limitsColumn.Name = "limits";
        limitsColumn.HeaderText = "跌停 ～ 漲停";
        _grid.Columns.AddRange(new DataGridViewColumn[] { monthColumn, symbolColumn, expiryColumn, limitsColumn });
        footerPanel.Dock = DockStyle.Bottom;
        footerPanel.Height = 42;
        footerPanel.Name = "footerPanel";
        footerPanel.Padding = new Padding(8, 5, 8, 5);
        footerPanel.Controls.Add(_selection);
        footerPanel.Controls.Add(_applyClose);
        footerPanel.Controls.Add(_apply);
        _selection.Dock = DockStyle.Fill;
        _selection.Name = "selectionLabel";
        _selection.TextAlign = ContentAlignment.MiddleLeft;
        _apply.Dock = DockStyle.Right;
        _apply.Width = 90;
        _apply.Enabled = false;
        _apply.Name = "applyButton";
        _apply.Text = "套用";
        _applyClose.Dock = DockStyle.Right;
        _applyClose.Width = 110;
        _applyClose.Enabled = false;
        _applyClose.Name = "applyCloseButton";
        _applyClose.Text = "套用並關閉";
        AutoScaleMode = AutoScaleMode.Font;
        Font = new Font("Microsoft JhengHei UI", 9F);
        MinimumSize = new Size(900, 560);
        Size = new Size(1180, 720);
        StartPosition = FormStartPosition.CenterParent;
        Name = "InstrumentSelectorForm";
        Text = "商品選擇器";
        Controls.Add(splitContainer);
        Controls.Add(footerPanel);
        splitContainer.Panel1.ResumeLayout(false);
        splitContainer.Panel1.PerformLayout();
        splitContainer.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)splitContainer).EndInit();
        splitContainer.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)_grid).EndInit();
        footerPanel.ResumeLayout(false);
        ResumeLayout(false);
    }
}
