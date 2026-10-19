using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using KGIOptionPricing.OrderManagement.Core;
using KGIOptionPricing.Strategies;

namespace KGIOptionPricing;

/// <summary>
/// 價量跟隨策略的暫時操作／觀察介面。策略邏輯與生命週期均由 QueueFollowStrategyEngine 管理。
/// </summary>
public sealed partial class QueueFollowStrategyForm : Form
{
    private readonly QueueFollowStrategyEngine _engine = null!;
    private readonly KGITradeService _trade = null!;
    private readonly KGIQuoteService _quote = null!;
    private readonly InstrumentSelectorForm _selector = new();
    private ContractInfo? _contract;

    public QueueFollowStrategyForm()
    {
        InitializeComponent();
    }

    public QueueFollowStrategyForm(QueueFollowStrategyEngine engine, KGITradeService trade, KGIQuoteService quote) : this()
    {
        _engine = engine;
        _trade = trade;
        _quote = quote;
        _choose.Click += (_, _) => ShowSelector();
        _buy.CheckedChanged += (_, _) => SetSuggestedLimit();
        _start.Click += (_, _) => StartStrategy();
        _stop.Click += (_, _) => StopSelected();
        _selector.InstrumentSelected += SelectInstrument;
        _engine.SessionUpdated += EngineSessionUpdated;
        Shown += (_, _) => RefreshAll();
        FormClosed += (_, _) =>
        {
            _engine.SessionUpdated -= EngineSessionUpdated;
            if (!_selector.IsDisposed) _selector.Close();
        };
    }

    private void ShowSelector()
    {
        if (_selector.IsDisposed) return;
        _selector.ReloadCatalog();
        _selector.Show(this);
        _selector.Activate();
    }

    private void SelectInstrument(ContractInfo contract)
    {
        _contract = contract;
        string month = ContractMonth(contract);
        _instrument.Text = $"{contract.ExchangeNm}  {contract.ProductId}  {month}  {contract.CPF}  [{contract.Symbol}]";
        _priceLimit.DecimalPlaces = Math.Min(7, Math.Max(contract.PxDecimal, PriceTick.getPrcDigital(contract.ProductId)));
        _priceLimit.Increment = contract.TickSize > 0 ? contract.TickSize : 1m;
        SetSuggestedLimit();
    }

    private void SetSuggestedLimit()
    {
        if (_contract == null) return;
        bool buy = _buy.Checked;
        KGIQuoteService.MarketDataStruct? market = _quote.GetMarketData(_contract.Symbol) ?? _quote.GetMarketData("TW." + _contract.Symbol);
        decimal reference = market?.MatchcPx > 0 ? market.MatchcPx : market?.LastPx > 0 ? market.LastPx : _contract.PreClosePx;
        decimal limit = buy
            ? (_contract.RisePx > 0 ? _contract.RisePx : reference * 1.10m)
            : (_contract.FallPx > 0 ? _contract.FallPx : reference * 0.90m);
        if (limit > 0)
            _priceLimit.Value = Math.Min(_priceLimit.Maximum, Math.Max(_priceLimit.Minimum, limit));
    }

    private void StartStrategy()
    {
        try
        {
            if (_contract == null) throw new InvalidOperationException("請先選擇商品。");
            if (_priceLimit.Value <= 0) throw new InvalidOperationException("請輸入有效的價格界線。");

            if (_repriceTarget.Value >= _repriceThreshold.Value)
                throw new InvalidOperationException("改掛目標前方量 (C) 必須小於重新掛單門檻 (B)。");

            BuySell side = _buy.Checked ? BuySell.Buy : BuySell.Sell;
            string id = $"{_contract.ProductId}-{DateTime.Now:HHmmssfff}";
            _engine.Start(new QueueFollowRequest
            {
                StrategyId = id,
                Instrument = ToInstrument(_contract),
                QuoteSymbol = _contract.Symbol,
                ProductId = _contract.ProductId,
                TickType = _contract.TickType,
                Side = side,
                Quantity = (int)_quantity.Value,
                InitialTargetAheadQuantity = (int)_initialAhead.Value,
                RepriceAheadThreshold = (int)_repriceThreshold.Value,
                RepriceTargetAheadQuantity = (int)_repriceTarget.Value,
                PriceLimit = _priceLimit.Value,
                BrokerId = _trade.BrokerId,
                Account = _trade.Account,
                TimeInForce = TimeInForce.ROD,
                PositionEffect = PositionEffect.Auto
            });
            _status.Text = $"已送出啟動指令：{id}";
            RefreshAll();
            SelectStrategy(id);
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "無法啟動策略", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void StopSelected()
    {
        if (_grid.SelectedRows.Count == 0) return;
        string id = Convert.ToString(_grid.SelectedRows[0].Cells["StrategyId"].Value) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(id) && _engine.Stop(id))
            _status.Text = $"已送出停止指令：{id}";
    }

    private void EngineSessionUpdated(QueueFollowSnapshot snapshot)
    {
        if (IsDisposed || !IsHandleCreated) return;
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => Upsert(snapshot)));
            return;
        }
        Upsert(snapshot);
    }

    private void RefreshAll()
    {
        foreach (QueueFollowSnapshot snapshot in _engine.GetSnapshots()) Upsert(snapshot);
    }

    private void Upsert(QueueFollowSnapshot snapshot)
    {
        DataGridViewRow? row = _grid.Rows.Cast<DataGridViewRow>()
            .FirstOrDefault(r => string.Equals(Convert.ToString(r.Cells["StrategyId"].Value), snapshot.StrategyId, StringComparison.OrdinalIgnoreCase));
        if (row == null)
        {
            int index = _grid.Rows.Add();
            row = _grid.Rows[index];
        }

        row.Cells["StrategyId"].Value = snapshot.StrategyId;
        row.Cells["Product"].Value = snapshot.Instrument.ToString();
        row.Cells["Side"].Value = snapshot.Side == BuySell.Buy ? "買" : "賣";
        row.Cells["Target"].Value = snapshot.TargetQuantity;
        row.Cells["Filled"].Value = snapshot.FilledQuantity;
        row.Cells["Leaves"].Value = snapshot.RemainingQuantity;
        row.Cells["CurrentPrice"].Value = snapshot.CurrentOrderPrice;
        row.Cells["PriceLimit"].Value = snapshot.PriceLimit;
        row.Cells["A"].Value = snapshot.InitialTargetAheadQuantity;
        row.Cells["B"].Value = snapshot.RepriceAheadThreshold;
        row.Cells["C"].Value = snapshot.RepriceTargetAheadQuantity;
        row.Cells["SameAhead"].Value = snapshot.SamePriceAheadQuantity;
        row.Cells["BetterAhead"].Value = snapshot.BetterPriceQuantity;
        row.Cells["TotalAhead"].Value = snapshot.TotalAheadQuantity;
        row.Cells["State"].Value = snapshot.State;
        row.Cells["Reason"].Value = snapshot.Reason;
    }

    private void SelectStrategy(string strategyId)
    {
        DataGridViewRow? row = _grid.Rows.Cast<DataGridViewRow>()
            .FirstOrDefault(r => string.Equals(Convert.ToString(r.Cells["StrategyId"].Value), strategyId, StringComparison.OrdinalIgnoreCase));
        if (row == null) return;
        _grid.ClearSelection();
        row.Selected = true;
        _grid.CurrentCell = row.Cells["StrategyId"];
        _grid.Focus();
    }

    private static string ContractMonth(ContractInfo contract) =>
        !string.IsNullOrWhiteSpace(contract.ComYM2) ? contract.ComYM2 :
        !string.IsNullOrWhiteSpace(contract.ComYM) ? contract.ComYM : contract.Month;

    private static InstrumentKey ToInstrument(ContractInfo contract)
    {
        bool foreign = !string.IsNullOrWhiteSpace(contract.ExchangeNm) &&
                       !contract.ExchangeNm.Equals("TIMEX", StringComparison.OrdinalIgnoreCase) &&
                       !contract.ExchangeNm.Equals("TAIFEX", StringComparison.OrdinalIgnoreCase);
        bool option = contract.CPF is CPFEnum.Call or CPFEnum.Put || (contract.ComType != '\0' && contract.ComType != 'F');
        return new InstrumentKey
        {
            Market = foreign
                ? (option ? MarketType.ForeignOption : MarketType.ForeignFuture)
                : (option ? MarketType.DomesticOption : MarketType.DomesticFuture),
            Exchange = contract.ExchangeNm,
            Symbol = foreign && !string.IsNullOrWhiteSpace(contract.ProductId) ? contract.ProductId : contract.Symbol,
            ContractMonth = ContractMonth(contract)
        };
    }
}
