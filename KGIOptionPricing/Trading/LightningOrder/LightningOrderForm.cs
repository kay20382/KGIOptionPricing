using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using KGIOptionPricing.OrderManagement.Core;

namespace KGIOptionPricing;

public sealed partial class LightningOrderForm : Form
{
    private readonly OrderManager _orders = null!;
    private readonly KGITradeService _trade = null!;
    private readonly KGIQuoteService _quote = null!;
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 50 };
    private readonly System.Windows.Forms.Timer _clickTimer = new() { Interval = SystemInformation.DoubleClickTime };
    private readonly InstrumentSelectorForm _selector = new();
    private static readonly LightningOrderInteractionSettings InteractionSettings = new();
    private ContractInfo? _contract;
    private PriceLadderRange _range = new();
    private List<decimal> _prices = new();
    private IReadOnlyDictionary<decimal, WorkingOrderLevel> _working = new Dictionary<decimal, WorkingOrderLevel>();
    private KGIQuoteService.MarketDataStruct? _market;
    private bool _dirty;
    private (int Row, int Column)? _pendingClick;

    public LightningOrderForm() => InitializeComponent();

    public LightningOrderForm(OrderManager orders, KGITradeService trade, MarketDataService marketData) : this()
    {
        _orders = orders;
        _trade = trade;
        _quote = marketData.QuoteService;
        _choose.Click += (_, _) => ShowSelector();
        settingsButton.Click += (_, _) => ShowInteractionSettings();
        _selector.InstrumentSelected += SelectInstrument;
        cancelBuy.Click += (_, _) => CancelOrders(BuySell.Buy, null);
        cancelSell.Click += (_, _) => CancelOrders(BuySell.Sell, null);
        cancelAll.Click += (_, _) => CancelOrders(null, null);
        _ladder.CellValueNeeded += LadderCellValueNeeded;
        _ladder.CellFormatting += LadderCellFormatting;
        _ladder.CellMouseClick += LadderCellMouseClick;
        _ladder.CellMouseDoubleClick += LadderCellMouseDoubleClick;
        _ladder.MouseDown += LadderMouseDown;
        _orders.OnOrderStateChanged += OrderChanged;
        _orders.OnOrderFilled += OrderFilled;
        _orders.OnOrderFailed += OrderFailed;
        _orders.OnModifyFailed += ModifyFailed;
        _orders.OnCancelFailed += CancelFailed;
        _quote.OnMarketDataReceived += MarketDataReceived;
        _quote.OnSymbolRepositoryLoaded += CatalogLoaded;
        _refreshTimer.Tick += (_, _) => RefreshDirtyState();
        _clickTimer.Tick += (_, _) => ExecutePendingSingleClick();
        _refreshTimer.Start();
        Shown += (_, _) => ApplyScreenHeightLimits();
        LocationChanged += (_, _) => { if (IsHandleCreated) ApplyScreenHeightLimits(); };
        FormClosed += (_, _) => Cleanup();
    }

    private void ShowSelector()
    {
        _selector.ReloadCatalog();
        if (!_selector.Visible) _selector.Show(this);
        else { _selector.Activate(); _selector.BringToFront(); }
    }

    private void SelectInstrument(ContractInfo contract)
    {
        string old = _contract?.Symbol ?? "";
        _contract = contract;
        _market = null;
        _symbol.Text = $"{contract.ProductId}  {contract.ComYM2}{contract.ComYM}{contract.Month}  {contract.CPF}  {(contract.StrikePx > 0 ? contract.StrikePx : "")}  [{contract.Symbol}]";
        // 報價服務為多視窗共用；此處不取消舊訂閱，以免中斷定價或其他下單視窗。
        _quote.Subscribe(contract.Symbol);
        _market = _quote.GetMarketData(contract.Symbol) ?? _quote.GetMarketData("TW." + contract.Symbol);
        decimal reference = SettlementOrLast(_market, contract);
        _range = PriceLadderRangeBuilder.Create(contract, reference);
        RebuildPrices(true);
        UpdateOperationHint();
    }

    private void MarketDataReceived(string symbol, KGIQuoteService.MarketDataStruct market)
    {
        if (_contract == null || !SymbolMatches(symbol, _contract.Symbol)) return;
        _market = market;
        decimal last = MarketPrice(market);
        PriceLadderRange expanded = PriceLadderRangeBuilder.ExpandIfNeeded(_contract, _range, last);
        bool changed = expanded.LowerBound != _range.LowerBound || expanded.UpperBound != _range.UpperBound;
        _range = expanded;
        if (changed && IsHandleCreated) BeginInvoke(new Action(() => RebuildPrices(false)));
        MarkDirty();
    }

    private void OrderChanged(OrderInfo order)
    {
        if (_contract == null || !WorkingOrderAggregator.MatchesContract(order.Instrument, _contract)) return;
        MarkDirty();
    }

    private void OrderFilled(OrderInfo order, int quantity, double price) => MarkDirty();
    private void OrderFailed(OrderInfo order, string reason) => SetStatus("拒單：" + reason, Color.Crimson);
    private void ModifyFailed(OrderInfo order, string reason) => SetStatus("改價失敗：" + reason, Color.Crimson);
    private void CancelFailed(OrderInfo order, string reason) => SetStatus("刪單失敗：" + reason, Color.Crimson);

    private void MarkDirty() => _dirty = true;

    private void RefreshDirtyState()
    {
        if (!_dirty || _contract == null) return;
        _dirty = false;
        _working = WorkingOrderAggregator.Aggregate(_orders.GetActiveOrders(), _contract);
        var included = PriceLadderRangeBuilder.IncludeWorkingPrices(_contract, _range, _working.Keys);
        if (included.LowerBound != _range.LowerBound || included.UpperBound != _range.UpperBound)
        {
            _range = included;
            RebuildPrices(false);
        }
        _ladder.Invalidate();
    }

    private void RebuildPrices(bool center)
    {
        if (_contract == null) return;
        _prices = PriceLadderRangeBuilder.BuildPrices(_contract, _range);
        _ladder.RowCount = _prices.Count;
        _working = WorkingOrderAggregator.Aggregate(_orders.GetActiveOrders(), _contract);
        if (center) CenterOnMarket();
    }

    private void LadderCellValueNeeded(object? sender, DataGridViewCellValueEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _prices.Count) return;
        decimal price = _prices[e.RowIndex];
        _working.TryGetValue(price, out var own);
        if (e.ColumnIndex == 0) e.Value = FormatOwn(own?.BuyLeaves ?? 0, own?.BuyCount ?? 0);
        else if (e.ColumnIndex == 1) { int qty = MarketQty(price, true); e.Value = qty > 0 ? qty : null; }
        else if (e.ColumnIndex == 2)
        {
            e.Value = price.ToString($"F{GetDisplayDecimals()}");
        }
        else if (e.ColumnIndex == 3) { int qty = MarketQty(price, false); e.Value = qty > 0 ? qty : null; }
        else if (e.ColumnIndex == 4) e.Value = FormatOwn(own?.SellLeaves ?? 0, own?.SellCount ?? 0);
    }

    private void LadderCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _prices.Count) return;
        decimal price = _prices[e.RowIndex];
        e.CellStyle.BackColor = Color.Black;
        e.CellStyle.SelectionBackColor = Color.FromArgb(45, 45, 45);
        if (e.ColumnIndex is 0 or 4)
        {
            e.CellStyle.ForeColor = Color.Gold;
            e.CellStyle.SelectionForeColor = Color.Gold;
            e.CellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        }
        if (e.ColumnIndex is 1 or 3)
        {
            e.CellStyle.ForeColor = Color.White;
            e.CellStyle.SelectionForeColor = Color.White;
            e.CellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        }
        if (e.ColumnIndex == 2)
        {
            e.CellStyle.ForeColor = Color.Red;
            e.CellStyle.SelectionForeColor = Color.Red;
            e.CellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            e.CellStyle.Font = new Font("Microsoft JhengHei UI", 13F, FontStyle.Bold);
            decimal last = MarketPrice(_market);
            if (last == price)
            {
                e.CellStyle.ForeColor = Color.Gold;
                e.CellStyle.SelectionForeColor = Color.Gold;
                e.CellStyle.BackColor = Color.FromArgb(65, 45, 0);
            }
        }
        if (_working.TryGetValue(price, out var level))
        {
            if (e.ColumnIndex == 0 && level.HasPendingBuy) e.CellStyle.ForeColor = Color.DarkGoldenrod;
            if (e.ColumnIndex == 4 && level.HasPendingSell) e.CellStyle.ForeColor = Color.DarkGoldenrod;
        }
    }

    private void LadderCellMouseClick(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || e.RowIndex < 0 || ClickModeForColumn(e.ColumnIndex) != LightningClickMode.SingleClick)
            return;
        _pendingClick = (e.RowIndex, e.ColumnIndex);
        _clickTimer.Stop();
        _clickTimer.Start();
    }

    private void LadderCellMouseDoubleClick(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || e.RowIndex < 0)
            return;
        _clickTimer.Stop();
        _pendingClick = null;
        if (ClickModeForColumn(e.ColumnIndex) == LightningClickMode.DoubleClick)
            ExecuteLadderAction(e.RowIndex, e.ColumnIndex);
    }

    private void ExecutePendingSingleClick()
    {
        _clickTimer.Stop();
        if (_pendingClick is not { } click)
            return;
        _pendingClick = null;
        ExecuteLadderAction(click.Row, click.Column);
    }

    private static LightningClickMode? ClickModeForColumn(int column) => column switch
    {
        0 or 4 => InteractionSettings.CancelClickMode,
        1 or 3 => InteractionSettings.PlaceClickMode,
        _ => null
    };

    private void ExecuteLadderAction(int row, int column)
    {
        if (row < 0 || row >= _prices.Count)
            return;
        decimal price = _prices[row];
        if (column == 1) PlaceOrder(BuySell.Buy, price);
        else if (column == 3) PlaceOrder(BuySell.Sell, price);
        else if (column == 0) CancelOrders(BuySell.Buy, price);
        else if (column == 4) CancelOrders(BuySell.Sell, price);
    }

    private void LadderMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right) return;
        // 右鍵固定回到最新成交價，不受點擊欄位或列影響。
        BeginInvoke(new Action(CenterOnMarket));
    }

    private void PlaceAtSelected(BuySell side)
    {
        decimal? p = SelectedPrice();
        if (p.HasValue) PlaceOrder(side, p.Value);
    }

    private void PlaceOrder(BuySell side, decimal price)
    {
        if (_contract == null || !_trade.IsLoggedIn) { SetStatus("交易 API 尚未登入，禁止送單", Color.Crimson); return; }
        if (InteractionSettings.ConfirmPlaceOrder)
        {
            string sideText = side == BuySell.Buy ? "買進" : "賣出";
            if (MessageBox.Show(this, $"確定送出 {sideText} {_qty.Value} 口？\n價格：{price:F6}".TrimEnd('0').TrimEnd('.'), "下單確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
        }
        var request = new OrderRequest
        {
            Instrument = ToInstrument(_contract), BuySell = side, Price = (double)price, Quantity = (int)_qty.Value,
            Account = _trade.Account, BrokerId = _trade.BrokerId, TimeInForce = TimeInForce.ROD, PositionEffect = PositionEffect.Auto
        };
        _orders.PlaceOrder(request);
        SetStatus($"送出中：{side} {_qty.Value} @ {price}", Color.DarkOrange);
    }

    private void CancelOrders(BuySell? side, decimal? price)
    {
        if (_contract == null) return;
        var currentLevels = WorkingOrderAggregator.Aggregate(_orders.GetActiveOrders(), _contract);
        IEnumerable<OrderInfo> targets = currentLevels.Values.SelectMany(level => level.Orders).DistinctBy(order => order.OrderId);
        if (side.HasValue) targets = targets.Where(o => o.BuySell == side.Value);
        if (price.HasValue)
            targets = currentLevels.TryGetValue(price.Value, out WorkingOrderLevel? level)
                ? level.Orders.Where(o => !side.HasValue || o.BuySell == side.Value)
                : Enumerable.Empty<OrderInfo>();
        // 改價等待中仍需保留人工刪單控制權；只有已送出刪單的委託不可重複送。
        var list = targets.Where(o => o.Status is not OrderStatus.CancelPending).ToArray();
        if (list.Length == 0) { SetStatus("此範圍沒有可刪除的委託", Color.Crimson); return; }
        if (InteractionSettings.ConfirmCancelOrder)
        {
            string scope = price.HasValue ? $"價格 {price.Value}" : side.HasValue ? (side == BuySell.Buy ? "全部買單" : "全部賣單") : "全部委託";
            if (MessageBox.Show(this, $"確定刪除 {scope}，共 {list.Length} 筆？", "刪單確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
        }
        foreach (var order in list) _orders.CancelOrder(order.OrderId);
        SetStatus($"已送出 {list.Length} 筆刪單要求；確認前保留原委託量", Color.DarkOrange);
    }

    private void ModifyOrders(BuySell side, decimal? sourcePrice)
    {
        if (_contract == null || !sourcePrice.HasValue) return;
        if (!_working.TryGetValue(sourcePrice.Value, out var level) || !level.Orders.Any(o => o.BuySell == side))
        { SetStatus("此價位沒有可改價的委託", Color.Crimson); return; }
        decimal? targetPrice = PromptForPrice(sourcePrice.Value);
        if (!targetPrice.HasValue || targetPrice.Value == sourcePrice.Value) return;
        var targets = _working[sourcePrice.Value].Orders.Where(o => o.BuySell == side && o.Status is not OrderStatus.ModifyPending and not OrderStatus.CancelPending).ToArray();
        foreach (var order in targets) _orders.ModifyOrder(order.OrderId, (double)targetPrice.Value, order.OrderQty);
        SetStatus($"已送出 {targets.Length} 筆改價：{sourcePrice} → {targetPrice}；確認前保留原價量", Color.DarkOrange);
    }

    private decimal? PromptForPrice(decimal current)
    {
        using var dialog = new LightningPriceEditDialog(current, GetDisplayDecimals());
        if (dialog.ShowDialog(this) != DialogResult.OK) return null;
        decimal regular = PriceTick.getRegularPx(_contract!.TickType, dialog.Price, dialog.Price >= current);
        if (_range.HasDailyLimits && (regular < _range.LowerBound || regular > _range.UpperBound))
        { SetStatus("改價超出漲跌停範圍", Color.Crimson); return null; }
        return regular;
    }

    private void CenterOnSelected() { decimal? p = SelectedPrice(); if (p.HasValue) CenterOn(p.Value); }
    private void CenterOnMarket() { decimal p = MarketPrice(_market); if (p <= 0) p = _range.ReferencePrice; CenterOn(p); }
    private void CenterOn(decimal price)
    {
        if (_prices.Count == 0 || !_ladder.IsHandleCreated) return;
        int index = _prices.Select((p, i) => (p, i)).OrderBy(x => Math.Abs(x.p - price)).First().i;
        int visible = Math.Max(1, _ladder.DisplayedRowCount(false));
        _ladder.FirstDisplayedScrollingRowIndex = Math.Max(0, Math.Min(_prices.Count - 1, index - visible / 2));
        _ladder.CurrentCell = _ladder[2, index];
    }

    private int MarketQty(decimal price, bool bid)
    {
        if (_market == null) return 0;
        decimal[] px = bid ? _market.BuyPx : _market.SellPx;
        int[] qty = bid ? _market.BuyQty : _market.SellQty;
        for (int i = 0; i < Math.Min(px.Length, qty.Length); i++) if (px[i] == price) return qty[i];
        return 0;
    }

    private decimal? SelectedPrice() => _ladder.CurrentCell != null && _ladder.CurrentCell.RowIndex < _prices.Count ? _prices[_ladder.CurrentCell.RowIndex] : null;
    private static string FormatOwn(int qty, int count) => qty <= 0 ? "" : count > 1 ? $"{qty} ({count})" : qty.ToString();

    private int GetDisplayDecimals()
    {
        if (_contract is null)
            return 0;

        // ContractInfo normally carries QuoteCom PT01805.PriceDecimal. Keep the
        // central tick table as a fallback for feeds where product base is absent.
        return Math.Max(_contract.PxDecimal, PriceTick.getPrcDigital(_contract.ProductId));
    }
    private static decimal MarketPrice(KGIQuoteService.MarketDataStruct? m) => m == null ? 0 : m.MatchcPx > 0 ? m.MatchcPx : m.LastPx > 0 ? m.LastPx : m.BuyPx[0] > 0 && m.SellPx[0] > 0 ? (m.BuyPx[0] + m.SellPx[0]) / 2 : m.BuyPx[0] > 0 ? m.BuyPx[0] : m.SellPx[0];
    private static decimal SettlementOrLast(KGIQuoteService.MarketDataStruct? m, ContractInfo c) => m != null && m.SettlementPx > 0 ? m.SettlementPx : c.PreClosePx > 0 ? c.PreClosePx : MarketPrice(m);
    private static bool SymbolMatches(string a, string b) => a.Equals(b, StringComparison.OrdinalIgnoreCase) || a.EndsWith("." + b, StringComparison.OrdinalIgnoreCase) || b.EndsWith("." + a, StringComparison.OrdinalIgnoreCase);
    private static InstrumentKey ToInstrument(ContractInfo c)
    {
        bool foreign = !string.IsNullOrWhiteSpace(c.ExchangeNm) && !c.ExchangeNm.Equals("TIMEX", StringComparison.OrdinalIgnoreCase) && !c.ExchangeNm.Equals("TAIFEX", StringComparison.OrdinalIgnoreCase);
        bool option = c.CPF is CPFEnum.Call or CPFEnum.Put || (c.ComType != '\0' && c.ComType != 'F');
        return new InstrumentKey
        {
            Market = foreign ? (option ? MarketType.ForeignOption : MarketType.ForeignFuture) : (option ? MarketType.DomesticOption : MarketType.DomesticFuture),
            Exchange = c.ExchangeNm,
            Symbol = foreign && !string.IsNullOrWhiteSpace(c.ProductId) ? c.ProductId : c.Symbol,
            ContractMonth = !string.IsNullOrWhiteSpace(c.ComYM2) ? c.ComYM2 : !string.IsNullOrWhiteSpace(c.ComYM) ? c.ComYM : c.Month
        };
    }

    private void ShowInteractionSettings()
    {
        using var dialog = new LightningOrderSettingsForm(InteractionSettings);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        dialog.ApplyTo(InteractionSettings);
        UpdateOperationHint();
    }

    private void UpdateOperationHint()
    {
        string place = InteractionSettings.PlaceClickMode == LightningClickMode.SingleClick ? "單擊" : "雙擊";
        string cancel = InteractionSettings.CancelClickMode == LightningClickMode.SingleClick ? "單擊" : "雙擊";
        SetStatus($"{place}買量／賣量下單；{cancel}委買／委賣刪單；右鍵置中", Color.DarkGreen);
    }

    private void SetStatus(string message, Color color)
    {
        if (InvokeRequired) { BeginInvoke(new Action(() => SetStatus(message, color))); return; }
        _status.Text = message; _status.ForeColor = color;
    }

    private void ApplyScreenHeightLimits()
    {
        Rectangle workingArea = Screen.FromControl(this).WorkingArea;
        int chromeHeight = Math.Max(0, Height - ClientSize.Height);
        int minimumClientHeight = 44 + 44 + _status.Height + _ladder.ColumnHeadersHeight + (_ladder.RowTemplate.Height * 11);
        int minimumOuterHeight = minimumClientHeight + chromeHeight;
        MinimumSize = new Size(504, Math.Min(minimumOuterHeight, workingArea.Height));
        MaximumSize = new Size(10000, workingArea.Height);
        if (Height > workingArea.Height) Height = workingArea.Height;
    }

    private void CatalogLoaded() { if (_selector.IsHandleCreated) _selector.BeginInvoke(new Action(_selector.ReloadCatalog)); }
    private void Cleanup()
    {
        _refreshTimer.Stop();
        _clickTimer.Stop();
        _quote.OnMarketDataReceived -= MarketDataReceived;
        _quote.OnSymbolRepositoryLoaded -= CatalogLoaded;
        _orders.OnOrderStateChanged -= OrderChanged;
        _orders.OnOrderFilled -= OrderFilled;
        _orders.OnOrderFailed -= OrderFailed;
        _orders.OnModifyFailed -= ModifyFailed;
        _orders.OnCancelFailed -= CancelFailed;
        if (!_selector.IsDisposed) _selector.Close();
    }
}
