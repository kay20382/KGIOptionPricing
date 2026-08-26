using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using KGIOptionPricing.OrderManagement.Core;

namespace KGIOptionPricing
{
    public class DealRecordItem
    {
        public DateTime DealTime { get; set; } = DateTime.Now;
        public string SeqNo { get; set; } = string.Empty;
        public string OrdNo { get; set; } = string.Empty;
        public string Instrument { get; set; } = string.Empty;
        public BuySell BuySell { get; set; }
        public double Price { get; set; }
        public int Qty { get; set; }
        public double AvgPrice { get; set; }
        public double EstimatedFee { get; set; } = 40.0;
        public double EstimatedTax { get; set; } = 0.0;
    }

    public partial class DealReportForm : Form
    {
        private readonly OrderManager _orderManager;
        private readonly KGITradeService _tradeService;
        private readonly List<DealRecordItem> _deals = new List<DealRecordItem>();
        private readonly object _lockObj = new object();

        public DealReportForm(OrderManager orderManager, KGITradeService tradeService)
        {
            _orderManager = orderManager ?? throw new ArgumentNullException(nameof(orderManager));
            _tradeService = tradeService ?? throw new ArgumentNullException(nameof(tradeService));

            InitializeComponent();

            _orderManager.OnOrderFilled += OrderManager_OnOrderFilled;
            _tradeService.OnTradeHistory += TradeService_OnTradeHistory;

            this.Load += DealReportForm_Load;
            this.Activated += DealReportForm_Activated;
            this.FormClosing += DealReportForm_FormClosing;
        }

        private void DealReportForm_Load(object? sender, EventArgs e)
        {
            LoadAndRefreshDeals();
        }

        private void DealReportForm_Activated(object? sender, EventArgs e)
        {
            LoadAndRefreshDeals();
        }

        public void LoadAndRefreshDeals()
        {
            lock (_lockObj)
            {
                var filledOrders = _orderManager.GetAllOrders().Where(o => o.FilledQty > 0);
                foreach (var order in filledOrders)
                {
                    string seq = !string.IsNullOrEmpty(order.Identity?.Cnt) ? order.Identity.Cnt : order.OrderId;
                    string ordNo = !string.IsNullOrEmpty(order.Identity?.OrdNo) ? order.Identity.OrdNo : order.OrderId;
                    if (!_deals.Any(d => d.SeqNo == seq && d.OrdNo == ordNo))
                    {
                        _deals.Add(new DealRecordItem
                        {
                            DealTime = order.UpdateTime,
                            SeqNo = seq,
                            OrdNo = ordNo,
                            Instrument = order.Instrument != null && !string.IsNullOrEmpty(order.Instrument.Symbol) ? order.Instrument.Symbol : (order.Instrument != null ? order.Instrument.ToString() : "TXF"),
                            BuySell = order.BuySell,
                            Price = order.AvgFilledPrice > 0 ? order.AvgFilledPrice : order.OrderPrice,
                            Qty = order.FilledQty,
                            AvgPrice = order.AvgFilledPrice > 0 ? order.AvgFilledPrice : order.OrderPrice,
                            EstimatedFee = 40.0 * order.FilledQty,
                            EstimatedTax = Math.Max(0, (order.AvgFilledPrice > 0 ? order.AvgFilledPrice : order.OrderPrice) * 50.0 * 0.001 * order.FilledQty)
                        });
                    }
                }
            }
            RefreshGrid();
        }

        private void DealReportForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            _orderManager.OnOrderFilled -= OrderManager_OnOrderFilled;
            _tradeService.OnTradeHistory -= TradeService_OnTradeHistory;
        }

        private void OrderManager_OnOrderFilled(OrderInfo order, int fillQty, double fillPrice)
        {
            if (IsDisposed || !IsHandleCreated) return;

            var item = new DealRecordItem
            {
                DealTime = order.UpdateTime,
                SeqNo = !string.IsNullOrEmpty(order.Identity.Cnt) ? order.Identity.Cnt : DateTime.Now.ToString("HHmmssfff"),
                OrdNo = !string.IsNullOrEmpty(order.Identity.OrdNo) ? order.Identity.OrdNo : order.OrderId,
                Instrument = order.Instrument != null ? order.Instrument.ToString() : "TXF",
                BuySell = order.BuySell,
                Price = fillPrice,
                Qty = fillQty,
                AvgPrice = order.AvgFilledPrice > 0 ? order.AvgFilledPrice : fillPrice,
                EstimatedFee = 40.0 * fillQty,
                EstimatedTax = Math.Max(0, fillPrice * 50.0 * 0.001 * fillQty)
            };

            lock (_lockObj)
            {
                _deals.Insert(0, item);
            }

            BeginInvoke(new Action(RefreshGrid));
        }

        private void TradeService_OnTradeHistory(List<TradeHistoryItem> history)
        {
            if (IsDisposed || !IsHandleCreated || history == null) return;

            lock (_lockObj)
            {
                foreach (var h in history)
                {
                    if (string.IsNullOrEmpty(h.OrdNo1) && string.IsNullOrEmpty(h.InNo1)) continue;

                    string seq = !string.IsNullOrEmpty(h.InNo1) ? h.InNo1 : h.Cnt1;
                    if (_deals.Any(d => d.SeqNo == seq && d.OrdNo == h.OrdNo1)) continue;

                    string instr = !string.IsNullOrEmpty(h.ComID) ? $"{h.ComID} {h.ComYM} {h.StrikePrice} {h.CP}" : "TXF";
                    BuySell bs = h.BS == "B" ? BuySell.Buy : BuySell.Sell;

                    var item = new DealRecordItem
                    {
                        DealTime = DateTime.TryParseExact(h.OccDT, "yyyyMMddHHmmss", null, System.Globalization.DateTimeStyles.None, out var dt) ? dt : DateTime.Now,
                        SeqNo = seq,
                        OrdNo = h.OrdNo1,
                        Instrument = instr,
                        BuySell = bs,
                        Price = h.TrdPrice1,
                        Qty = h.Qty1,
                        AvgPrice = h.TrdPrice1,
                        EstimatedFee = h.ORIGNFEE > 0 ? h.ORIGNFEE : 40.0 * h.Qty1,
                        EstimatedTax = h.CTAXAMT
                    };

                    _deals.Add(item);
                }
            }

            BeginInvoke(new Action(RefreshGrid));
        }

        private void RefreshGrid()
        {
            List<DealRecordItem> displayList;

            lock (_lockObj)
            {
                displayList = _deals.OrderByDescending(d => d.DealTime).ToList();

                int totalQty = displayList.Sum(d => d.Qty);
                int buyQty = displayList.Where(d => d.BuySell == BuySell.Buy).Sum(d => d.Qty);
                int sellQty = displayList.Where(d => d.BuySell == BuySell.Sell).Sum(d => d.Qty);
                double totalPoints = displayList.Sum(d => d.Price * d.Qty);

                lblStatQty.Text = $"當日總成交口數: {totalQty} 口 (買進: {buyQty} / 賣出: {sellQty})";
                lblStatValue.Text = $"總成交點數: {totalPoints:N1} 點 | 總筆數: {displayList.Count} 筆";
                lblSummary.Text = $"總成交筆數: {displayList.Count} 筆 | 預估手續費: NT$ {displayList.Sum(d => d.EstimatedFee):N0} | 預估期交稅: NT$ {displayList.Sum(d => d.EstimatedTax):N0}";
            }

            dgvDeals.SuspendLayout();

            if (dgvDeals.Rows.Count != displayList.Count)
            {
                dgvDeals.Rows.Clear();
                if (displayList.Count > 0)
                {
                    dgvDeals.Rows.Add(displayList.Count);
                }
            }

            for (int i = 0; i < displayList.Count; i++)
            {
                var d = displayList[i];
                var row = dgvDeals.Rows[i];

                row.Cells[0].Value = d.DealTime.ToString("HH:mm:ss.fff");
                row.Cells[1].Value = d.SeqNo;
                row.Cells[2].Value = d.OrdNo;
                row.Cells[3].Value = d.Instrument;
                row.Cells[4].Value = d.BuySell == BuySell.Buy ? "買進" : "賣出";
                row.Cells[5].Value = d.Price.ToString("F1");
                row.Cells[6].Value = d.Qty;
                row.Cells[7].Value = d.AvgPrice.ToString("F1");
                row.Cells[8].Value = $"${d.EstimatedFee:F0} / ${d.EstimatedTax:F0}";

                // 買紅 賣綠
                row.Cells[4].Style.ForeColor = d.BuySell == BuySell.Buy ? Color.FromArgb(255, 80, 80) : Color.FromArgb(80, 220, 80);
                row.Cells[4].Style.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
            }

            dgvDeals.ResumeLayout();
        }

        private void btnExportCsv_Click(object sender, EventArgs e)
        {
            List<DealRecordItem> list;
            lock (_lockObj) { list = _deals.ToList(); }

            if (list.Count == 0)
            {
                MessageBox.Show(this, "目前尚無成交紀錄可供匯出。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var sfd = new SaveFileDialog
            {
                Title = "匯出當日成交明細",
                Filter = "CSV 逗點分隔檔 (*.csv)|*.csv",
                FileName = $"成交明細_{DateTime.Today:yyyyMMdd}_{DateTime.Now:HHmmss}.csv"
            };

            if (sfd.ShowDialog(this) == DialogResult.OK)
            {
                try
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("成交時間,成交序號,委託書號,商品名稱,買賣,成交價格,口數,成交均價,預估手續費,預估稅金");
                    foreach (var d in list)
                    {
                        sb.AppendLine($"{d.DealTime:yyyy/MM/dd HH:mm:ss.fff},{d.SeqNo},{d.OrdNo},{d.Instrument},{(d.BuySell == BuySell.Buy ? "買進" : "賣出")},{d.Price},{d.Qty},{d.AvgPrice},{d.EstimatedFee},{d.EstimatedTax}");
                    }
                    File.WriteAllText(sfd.FileName, sb.ToString(), Encoding.UTF8);
                    MessageBox.Show(this, $"成交紀錄已成功匯出至:\n{sfd.FileName}", "匯出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"匯出失敗: {ex.Message}", "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }
}
