using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace KGIOptionPricing;

public sealed partial class InstrumentSelectorForm : Form
{
    private ContractInfo? _selected;
    private IReadOnlyList<ContractInfo> _contracts = Array.Empty<ContractInfo>();

    public event Action<ContractInfo>? InstrumentSelected;

    public InstrumentSelectorForm()
    {
        InitializeComponent();
        if (System.ComponentModel.LicenseManager.UsageMode == System.ComponentModel.LicenseUsageMode.Designtime)
            return;

        _tree.AfterSelect += (_, e) => { if (e.Node != null) ShowNode(e.Node); };
        _search.TextChanged += (_, _) => BuildTree(_search.Text.Trim());
        _grid.CellClick += GridCellClick;
        _grid.CellDoubleClick += (_, _) => Apply(true);
        _apply.Click += (_, _) => Apply(false);
        _applyClose.Click += (_, _) => Apply(true);
        Shown += (_, _) => ReloadCatalog();
    }

    public void ReloadCatalog()
    {
        _contracts = SymbolRepository.Instance.GetAllContracts();
        BuildTree(_search.Text.Trim());
    }

    private void BuildTree(string filter)
    {
        _tree.BeginUpdate();
        _tree.Nodes.Clear();
        IEnumerable<ContractInfo> source = _contracts;
        if (!string.IsNullOrWhiteSpace(filter))
        {
            source = source.Where(c => SearchText(c).Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var exchangeGroup in source.GroupBy(c => string.IsNullOrWhiteSpace(c.ExchangeNm) ? c.Exchange.ToString() : c.ExchangeNm).OrderBy(g => g.Key))
        {
            var exchangeNode = new TreeNode(exchangeGroup.Key);
            foreach (var productGroup in exchangeGroup.GroupBy(c => c.ProductId).OrderBy(g => g.Key))
            {
                string name = productGroup.Select(c => c.SymbolCName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))
                              ?? productGroup.Select(c => c.ProductName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? "";
                var productNode = new TreeNode(ProductDisplayName(productGroup.Key, name)) { Tag = productGroup.ToArray() };
                foreach (var monthGroup in productGroup.GroupBy(MonthLabel).OrderBy(g => g.Key))
                    productNode.Nodes.Add(new TreeNode(monthGroup.Key) { Tag = monthGroup.ToArray() });
                exchangeNode.Nodes.Add(productNode);
            }
            _tree.Nodes.Add(exchangeNode);
        }
        if (_tree.Nodes.Count > 0) _tree.Nodes[0].Expand();
        _tree.EndUpdate();
    }

    private void ShowNode(TreeNode node)
    {
        if (node.Tag is not ContractInfo[] contracts || contracts.Length == 0) return;
        bool hasOptions = contracts.Any(c => c.CPF is CPFEnum.Call or CPFEnum.Put);
        if (hasOptions) ShowOptionMatrix(contracts);
        else ShowFutures(contracts);
    }

    private void ShowFutures(IEnumerable<ContractInfo> contracts)
    {
        _grid.Columns.Clear();
        _grid.Rows.Clear();
        _grid.Columns.AddRange(new DataGridViewColumn[]
        {
            (DataGridViewColumn)monthColumn.Clone(),
            (DataGridViewColumn)symbolColumn.Clone(),
            (DataGridViewColumn)expiryColumn.Clone(),
            (DataGridViewColumn)limitsColumn.Clone()
        });
        foreach (var c in contracts.OrderBy(c => c.Month).ThenBy(c => c.Symbol))
        {
            int row = _grid.Rows.Add(MonthLabel(c), c.Symbol, c.EndDate, c.FallPx > 0 && c.RisePx > 0 ? $"{c.FallPx} ～ {c.RisePx}" : "無漲跌停");
            _grid.Rows[row].Tag = c;
        }
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
    }

    private void ShowOptionMatrix(IEnumerable<ContractInfo> source)
    {
        var contracts = source.Where(c => c.CPF is CPFEnum.Call or CPFEnum.Put).ToArray();
        var months = contracts.Select(MonthLabel).Distinct().OrderBy(x => x).ToArray();
        _grid.Columns.Clear();
        _grid.Rows.Clear();
        foreach (string month in months.Reverse()) _grid.Columns.Add("C_" + month, month + " 買權");
        int strikeColumn = _grid.Columns.Add("strike", "履約價");
        foreach (string month in months) _grid.Columns.Add("P_" + month, month + " 賣權");

        foreach (decimal strike in contracts.Select(c => c.StrikePx).Distinct().OrderBy(x => x))
        {
            int rowIndex = _grid.Rows.Add();
            var row = _grid.Rows[rowIndex];
            row.Cells[strikeColumn].Value = strike;
            row.Cells[strikeColumn].Style.BackColor = Color.FromArgb(225, 225, 235);
            row.Cells[strikeColumn].Style.Font = new Font(_grid.Font, FontStyle.Bold);
            foreach (string month in months)
            {
                var call = contracts.FirstOrDefault(c => c.StrikePx == strike && c.CPF == CPFEnum.Call && MonthLabel(c) == month);
                var put = contracts.FirstOrDefault(c => c.StrikePx == strike && c.CPF == CPFEnum.Put && MonthLabel(c) == month);
                SetContractCell(row.Cells["C_" + month], call, Color.MistyRose);
                SetContractCell(row.Cells["P_" + month], put, Color.AliceBlue);
            }
        }
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns[strikeColumn].Frozen = true;
    }

    private static void SetContractCell(DataGridViewCell cell, ContractInfo? contract, Color color)
    {
        if (contract == null) return;
        cell.Value = "✓";
        cell.Tag = contract;
        cell.ToolTipText = $"{contract.Symbol}  {contract.CPF}  {contract.StrikePx}  到期 {contract.EndDate}";
        cell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
        cell.Style.ForeColor = Color.Crimson;
        cell.Style.BackColor = color;
    }

    private void GridCellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        _selected = _grid[e.ColumnIndex, e.RowIndex].Tag as ContractInfo ?? _grid.Rows[e.RowIndex].Tag as ContractInfo;
        _apply.Enabled = _applyClose.Enabled = _selected != null;
        _selection.Text = _selected == null ? "請選擇有效契約" : $"選定：{_selected.ProductId}  {MonthLabel(_selected)}  {_selected.CPF}  {(_selected.StrikePx > 0 ? _selected.StrikePx : "")}  [{_selected.Symbol}]";
    }

    private void Apply(bool close)
    {
        if (_selected == null) return;
        InstrumentSelected?.Invoke(_selected);
        if (close) Hide();
    }

    private static string MonthLabel(ContractInfo c) => !string.IsNullOrWhiteSpace(c.ComYM2) ? c.ComYM2 : !string.IsNullOrWhiteSpace(c.ComYM) ? c.ComYM : !string.IsNullOrWhiteSpace(c.Month) ? c.Month : c.CharMonth;
    private static string ProductDisplayName(string productId, string name)
    {
        string cleanName = (name ?? string.Empty).Trim();
        if (cleanName.StartsWith(productId, StringComparison.OrdinalIgnoreCase))
        {
            cleanName = cleanName[productId.Length..].TrimStart(' ', '-', '_', ':', '：');
        }
        return string.IsNullOrWhiteSpace(cleanName) ? productId : $"{productId} {cleanName}";
    }
    private static string SearchText(ContractInfo c) => $"{c.ExchangeNm} {c.Exchange} {c.ProductId} {c.ProductName} {c.SymbolCName} {c.SymbolEName} {c.Symbol} {c.Month} {c.ComYM} {c.ComYM2} {c.CPF} {c.StrikePx}";
}
