using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using KGIOptionPricing.OrderManagement.Core;

namespace KGIOptionPricing
{
    public partial class OrderReportForm : Form
    {
        private readonly OrderManager _orderManager = null!;
        private readonly KGITradeService _tradeService = null!;
        private readonly List<OrderInfo> _allOrders = new List<OrderInfo>();
        private readonly object _lockObj = new object();

        public OrderReportForm() => InitializeComponent();

        public OrderReportForm(OrderManager orderManager, KGITradeService tradeService) : this()
        {
            _orderManager = orderManager ?? throw new ArgumentNullException(nameof(orderManager));
            _tradeService = tradeService ?? throw new ArgumentNullException(nameof(tradeService));

            cboFilterStatus.SelectedIndex = 0; // Default: All

            // 綁定 OrderManager 事件
            _orderManager.OnOrderStateChanged += OrderManager_OnOrderStateChanged;
            _orderManager.OnOrderFilled += OrderManager_OnOrderFilled;
            _orderManager.OnOrderFailed += OrderManager_OnOrderFailed;
            _orderManager.OnCancelFailed += OrderManager_OnCancelFailed;
            _orderManager.OnModifyFailed += OrderManager_OnModifyFailed;

            this.Load += OrderReportForm_Load;
            this.Activated += OrderReportForm_Activated;
            this.FormClosing += OrderReportForm_FormClosing;
        }

        private void OrderReportForm_Load(object? sender, EventArgs e)
        {
            LoadAndRefreshOrders();
        }

        private void OrderReportForm_Activated(object? sender, EventArgs e)
        {
            LoadAndRefreshOrders();
        }

        public void LoadAndRefreshOrders()
        {
            lock (_lockObj)
            {
                _allOrders.Clear();
                var currentOrders = _orderManager.GetAllOrders()
                    .OrderByDescending(o => o.SubmitTime)
                    .ThenByDescending(o => o.UpdateTime);
                _allOrders.AddRange(currentOrders);
            }
            RefreshGrid();
        }

        private void OrderReportForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            _orderManager.OnOrderStateChanged -= OrderManager_OnOrderStateChanged;
            _orderManager.OnOrderFilled -= OrderManager_OnOrderFilled;
            _orderManager.OnOrderFailed -= OrderManager_OnOrderFailed;
            _orderManager.OnCancelFailed -= OrderManager_OnCancelFailed;
            _orderManager.OnModifyFailed -= OrderManager_OnModifyFailed;
        }

        private void OrderManager_OnOrderStateChanged(OrderInfo order)
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke(new Action(() =>
            {
                lock (_lockObj)
                {
                    int idx = _allOrders.FindIndex(o => o.OrderId == order.OrderId);
                    if (idx >= 0)
                    {
                        _allOrders[idx] = order;
                    }
                    else
                    {
                        _allOrders.Insert(0, order);
                    }
                }
                RefreshGrid();
            }));
        }

        private void OrderManager_OnOrderFilled(OrderInfo order, int fillQty, double fillPrice)
        {
            OrderManager_OnOrderStateChanged(order);
        }

        private void OrderManager_OnOrderFailed(OrderInfo order, string reason)
        {
            OrderManager_OnOrderStateChanged(order);
        }

        private void OrderManager_OnCancelFailed(OrderInfo order, string reason)
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke(new Action(() =>
            {
                MessageBox.Show(this, $"刪單失敗: {reason} (單號: {order.OrderId})", "刪單警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }));
        }

        private void OrderManager_OnModifyFailed(OrderInfo order, string reason)
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke(new Action(() =>
            {
                MessageBox.Show(this, $"改單失敗: {reason} (單號: {order.OrderId})", "改單警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }));
        }

        private void cboFilterStatus_SelectedIndexChanged(object sender, EventArgs e)
        {
            RefreshGrid();
        }

        private void RefreshGrid()
        {
            int filterIdx = cboFilterStatus.SelectedIndex;
            List<OrderInfo> displayList;

            lock (_lockObj)
            {
                displayList = _allOrders.Where(o =>
                {
                    int leaves = Math.Max(0, o.OrderQty - o.FilledQty);
                    switch (filterIdx)
                    {
                        case 1: // 有效掛單
                            return leaves > 0 &&
                                   o.Status != OrderStatus.Filled &&
                                   o.Status != OrderStatus.Cancelled &&
                                   o.Status != OrderStatus.Rejected;
                        case 2: // 已完全成交
                            return o.Status == OrderStatus.Filled;
                        case 3: // 已取消或失敗
                            return o.Status == OrderStatus.Cancelled || o.Status == OrderStatus.Rejected;
                        default: // 全部
                            return true;
                    }
                }).ToList();

                int total = _allOrders.Count;
                int active = _allOrders.Count(o => (o.OrderQty - o.FilledQty) > 0 && o.Status != OrderStatus.Filled && o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.Rejected);
                int filled = _allOrders.Count(o => o.Status == OrderStatus.Filled);
                int cancelled = _allOrders.Count(o => o.Status == OrderStatus.Cancelled || o.Status == OrderStatus.Rejected);

                lblSummary.Text = $"總委託筆數: {total} | 有效掛單: {active} | 已成交: {filled} | 已取消/失敗: {cancelled}";
            }

            dgvOrders.SuspendLayout();

            if (dgvOrders.Rows.Count != displayList.Count)
            {
                dgvOrders.Rows.Clear();
                if (displayList.Count > 0)
                {
                    dgvOrders.Rows.Add(displayList.Count);
                }
            }

            for (int i = 0; i < displayList.Count; i++)
            {
                var o = displayList[i];
                var row = dgvOrders.Rows[i];
                row.Tag = o;

                int leavesQty = Math.Max(0, o.OrderQty - o.FilledQty);
               
                row.Cells[0].Value = o.SubmitTime.ToString("HH:mm:ss.fff");
                row.Cells[1].Value = o.OrderId;
                row.Cells[2].Value = !string.IsNullOrEmpty(o.Identity.OrdNo) ? o.Identity.OrdNo : o.Identity.Cnt;
                row.Cells[3].Value = !string.IsNullOrEmpty(o.Account) ? o.Account : _tradeService.Account;
                row.Cells[4].Value = o.Instrument != null && !string.IsNullOrEmpty(o.Instrument.Symbol) ? o.Instrument.Symbol : (o.Instrument != null ? o.Instrument.ToString() : "TXF");
                row.Cells[5].Value = o.BuySell == BuySell.Buy ? "買 (Buy)" : "賣 (Sell)";
                row.Cells[6].Value = o.Source switch
                {
                    OrderSource.Internal => "本系統",
                    OrderSource.External => "外部單",
                    OrderSource.Recovered => "回補單",
                    _ => "ROD / 限價"
                };
                row.Cells[7].Value = o.OrderPrice > 0 ? o.OrderPrice.ToString("F1") : "市價";
                row.Cells[8].Value = o.OrderQty;
                row.Cells[9].Value = o.FilledQty;
                row.Cells[10].Value = leavesQty;
                row.Cells[11].Value = GetStatusDisplayText(o.Status);
                row.Cells[12].Value = o.Message;

                // 買賣配色 (買紅 賣綠)
                row.Cells[5].Style.ForeColor = o.BuySell == BuySell.Buy ? Color.FromArgb(255, 80, 80) : Color.FromArgb(80, 220, 80);
                row.Cells[5].Style.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);

                // 狀態配色
                row.Cells[11].Style.ForeColor = GetStatusColor(o.Status);
                row.Cells[11].Style.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
            }

            dgvOrders.ResumeLayout();
        }

        private static string GetStatusDisplayText(OrderStatus status)
        {
            return status switch
            {
                OrderStatus.PendingSubmit => "傳送中...",
                OrderStatus.Submitted => "已送出",
                OrderStatus.Accepted => "掛單中",
                OrderStatus.PartiallyFilled => "部分成交",
                OrderStatus.Filled => "完全成交",
                OrderStatus.CancelPending => "取消中...",
                OrderStatus.Cancelled => "已取消",
                OrderStatus.ModifyPending => "改單中...",
                OrderStatus.Rejected => "被拒絕",
                _ => status.ToString()
            };
        }

        private static Color GetStatusColor(OrderStatus status)
        {
            return status switch
            {
                OrderStatus.Accepted => Color.DeepSkyBlue,
                OrderStatus.PartiallyFilled => Color.Orange,
                OrderStatus.Filled => Color.LimeGreen,
                OrderStatus.Cancelled => Color.DarkGray,
                OrderStatus.Rejected => Color.Crimson,
                _ => Color.White
            };
        }

        private void btnCancelAll_Click(object sender, EventArgs e)
        {
            List<OrderInfo> activeOrders;
            lock (_lockObj)
            {
                activeOrders = _allOrders.Where(o => (o.OrderQty - o.FilledQty) > 0 &&
                    o.Status != OrderStatus.Filled &&
                    o.Status != OrderStatus.Cancelled &&
                    o.Status != OrderStatus.Rejected &&
                    o.Status != OrderStatus.CancelPending).ToList();
            }

            if (activeOrders.Count == 0)
            {
                MessageBox.Show(this, "目前沒有任何有效掛單可供取消。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (MessageBox.Show(this, $"確定要【一鍵全刪】目前所有 {activeOrders.Count} 筆掛單中的委託嗎？", "一鍵全刪確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                foreach (var order in activeOrders)
                {
                    _orderManager.CancelOrder(order.OrderId);
                }
            }
        }

        private void btnCancelSelected_Click(object sender, EventArgs e)
        {
            if (dgvOrders.CurrentRow?.Tag is OrderInfo order)
            {
                int leaves = order.OrderQty - order.FilledQty;
                if (leaves <= 0 || order.Status == OrderStatus.Filled || order.Status == OrderStatus.Cancelled)
                {
                    MessageBox.Show(this, "該委託非掛單中狀態，無法執行刪單。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                if (MessageBox.Show(this, $"確定刪除委託: {order.Instrument} {order.BuySell} {leaves}口 @ {order.OrderPrice} 嗎？", "刪單確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    _orderManager.CancelOrder(order.OrderId);
                }
            }
            else
            {
                MessageBox.Show(this, "請先在清單中選取一筆要刪除的委託單。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void btnModify_Click(object sender, EventArgs e)
        {
            if (dgvOrders.CurrentRow?.Tag is OrderInfo order)
            {
                int leaves = order.OrderQty - order.FilledQty;
                if (leaves <= 0 || order.Status == OrderStatus.Filled || order.Status == OrderStatus.Cancelled)
                {
                    MessageBox.Show(this, "該委託非掛單中狀態，無法改價改量。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                using var dlg = new OrderModificationDialog((decimal)order.OrderPrice, leaves)
                {
                    Text = $"改價/改量 - {order.Instrument}"
                };

                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _orderManager.ModifyOrder(order.OrderId, (double)dlg.Price, dlg.Quantity);
                }
            }
            else
            {
                MessageBox.Show(this, "請先選取一筆要修改的委託單。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void btnRecover_Click(object sender, EventArgs e)
        {
            if (_tradeService != null && _tradeService.IsLoggedIn)
            {
                _tradeService.RequestCoverHistory("I", DateTime.Today.ToString("yyyyMMdd"), DateTime.Today.ToString("yyyyMMdd"));
                MessageBox.Show(this, "已發送委託/回報回補請求！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show(this, "交易主機尚未連線或登入，無法執行回補。", "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OrderReportForm_Load_1(object sender, EventArgs e)
        {

        }
    }
}
