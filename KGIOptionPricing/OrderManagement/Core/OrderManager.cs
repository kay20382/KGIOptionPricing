using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using log4net;
using KGIOptionPricing.OrderManagement.Gateway;
using KGIOptionPricing.OrderManagement.Storage;

namespace KGIOptionPricing.OrderManagement.Core
{
    public class OrderManager
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(OrderManager));
        private readonly IOrderGateway _gateway;
        private readonly OrderStorageManager _storageManager;

        // 多重索引緩存
        private readonly ConcurrentDictionary<string, OrderInfo> _requestMap = new ConcurrentDictionary<string, OrderInfo>();
        private readonly ConcurrentDictionary<string, OrderInfo> _cntMap = new ConcurrentDictionary<string, OrderInfo>();
        private readonly ConcurrentDictionary<string, OrderInfo> _ordNoMap = new ConcurrentDictionary<string, OrderInfo>();
        
        // 活躍中委託單 (未完全成交、未取消、未被拒絕)
        private readonly ConcurrentDictionary<string, OrderInfo> _activeOrders = new ConcurrentDictionary<string, OrderInfo>();
        
        // 歷史終結委託單 (已完全成交、已取消、已拒絕)
        private readonly ConcurrentDictionary<string, OrderInfo> _historicalOrders = new ConcurrentDictionary<string, OrderInfo>();

        public event Action<OrderInfo>? OnOrderStateChanged;
        public event Action<OrderInfo, int, double>? OnOrderFilled;
        public event Action<OrderInfo, string>? OnOrderFailed;
        public event Action<OrderInfo, string>? OnCancelFailed;
        public event Action<OrderInfo, string>? OnModifyFailed;

        /// <summary>
        /// 初始化訂單管理員，並訂閱底層 Gateway 的事件與載入持久化訂單。
        /// </summary>
        /// <param name="gateway">負責與券商 API 溝通的底層介面</param>
        /// <param name="storageManager">訂單持久化儲存管理員 (選填)</param>
        public OrderManager(IOrderGateway gateway, OrderStorageManager? storageManager = null)
        {
            _gateway = gateway;
            _storageManager = storageManager ?? new OrderStorageManager();

            _gateway.OrderEventReceived += HandleOrderEvent;
            _gateway.FillEventReceived += HandleFillEvent;
            _gateway.RecoveryEventReceived += HandleRecoveryEvent;

            //LoadPersistedOrders();
        }

        /// <summary>
        /// 自持久化儲存載入今日所有委託與回補紀錄，並建立索引。
        /// </summary>
        private void LoadPersistedOrders()
        {
            try
            {
                var loaded = _storageManager.LoadOrders();
                foreach (var order in loaded)
                {
                    if (order.Status == OrderStatus.Filled || order.Status == OrderStatus.Cancelled || order.Status == OrderStatus.Rejected)
                    {
                        _historicalOrders[order.OrderId] = order;
                    }
                    else
                    {
                        _activeOrders[order.OrderId] = order;
                    }

                    if (!string.IsNullOrEmpty(order.Identity?.Cnt))
                        _cntMap[order.Identity.Cnt] = order;

                    if (!string.IsNullOrEmpty(order.Identity?.OrdNo))
                        _ordNoMap[order.Identity.OrdNo] = order;

                    if (!string.IsNullOrEmpty(order.Identity?.RequestId))
                        _requestMap[order.Identity.RequestId] = order;
                }

                log.Info($"[OrderManager] 初始載入完成: 活躍單 {_activeOrders.Count} 筆, 歷史單 {_historicalOrders.Count} 筆");
            }
            catch (Exception ex)
            {
                log.Error($"[OrderManager] 載入持久化訂單失敗: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 取得所有當前訂單 (活躍中 + 歷史紀錄)
        /// </summary>
        public OrderInfo[] GetAllOrders()
        {
            return _activeOrders.Values.Concat(_historicalOrders.Values).ToArray();
        }

        /// <summary>
        /// 取得所有活躍中委託單
        /// </summary>
        public OrderInfo[] GetActiveOrders()
        {
            return _activeOrders.Values.ToArray();
        }

        /// <summary>
        /// 委託新單。建立初始訂單紀錄後交由 Gateway 發送。
        /// </summary>
        /// <param name="request">新單參數 (商品、買賣、價量等)</param>
        public void PlaceOrder(OrderRequest request)
        {
            var orderInfo = new OrderInfo
            {
                OrderId = !string.IsNullOrEmpty(request.OrderId) ? request.OrderId : Guid.NewGuid().ToString("N"),
                Source = OrderSource.Internal,
                Instrument = request.Instrument ?? new InstrumentKey(),
                BuySell = request.BuySell,
                OrderPrice = request.Price,
                OrderQty = request.Quantity,
                LeavesQty = request.Quantity,
                Status = OrderStatus.PendingSubmit,
                SubmitTime = DateTime.Now,
                UpdateTime = DateTime.Now,
                BrokerId = request.BrokerId,
                Account = request.Account
            };

            _activeOrders[orderInfo.OrderId] = orderInfo;

            var result = _gateway.NewOrder(request);
            if (result.IsSuccess)
            {
                orderInfo.Identity.RequestId = result.RequestId;
                orderInfo.Status = OrderStatus.Submitted;
                if (!string.IsNullOrEmpty(result.RequestId))
                {
                    _requestMap[result.RequestId] = orderInfo;
                }
            }
            else
            {
                orderInfo.Status = OrderStatus.Rejected;
                orderInfo.Message = result.ErrorMessage;
                MoveToHistory(orderInfo);
                OnOrderFailed?.Invoke(orderInfo, result.ErrorMessage);
            }
            
            NotifyStateChanged(orderInfo);
        }

        /// <summary>
        /// 修改訂單 (改價或改量)。
        /// </summary>
        /// <param name="orderId">訂單 ID</param>
        /// <param name="newPrice">新委託價格</param>
        /// <param name="newQty">新委託數量</param>
        public void ModifyOrder(string orderId, double newPrice, int newQty)
        {
            if (!_activeOrders.TryGetValue(orderId, out var orderInfo))
            {
                return;
            }

            if (orderInfo.Status == OrderStatus.Filled || orderInfo.Status == OrderStatus.Cancelled || orderInfo.Status == OrderStatus.Rejected)
                return;

            orderInfo.PreviousStatus = orderInfo.Status;
            orderInfo.Status = OrderStatus.ModifyPending;
            orderInfo.UpdateTime = DateTime.Now;

            var request = new ModifyOrderRequest
            {
                OrderId = orderInfo.OrderId,
                Identity = orderInfo.Identity,
                Instrument = orderInfo.Instrument,
                Symbol = orderInfo.Instrument?.Symbol ?? "",
                BuySell = orderInfo.BuySell,
                WebId = orderInfo.Identity?.WebId ?? "",
                BrokerId = orderInfo.BrokerId,
                Account = orderInfo.Account,
                NewPrice = newPrice,
                NewQuantity = newQty
            };

            var result = _gateway.ModifyOrder(request);
            if (!result.IsSuccess)
            {
                orderInfo.Status = (orderInfo.FilledQty > 0) ? OrderStatus.PartiallyFilled : OrderStatus.Accepted;
                orderInfo.Message = result.ErrorMessage;
                OnModifyFailed?.Invoke(orderInfo, result.ErrorMessage);
            }
            else
            {
                if (!string.IsNullOrEmpty(result.RequestId))
                {
                    _requestMap[result.RequestId] = orderInfo;
                }
            }

            NotifyStateChanged(orderInfo);
        }

        /// <summary>
        /// 取消訂單。
        /// </summary>
        /// <param name="orderId">訂單 ID</param>
        public void CancelOrder(string orderId)
        {
            if (!_activeOrders.TryGetValue(orderId, out var orderInfo))
                return;

            if (orderInfo.Status == OrderStatus.Filled || orderInfo.Status == OrderStatus.Cancelled || orderInfo.Status == OrderStatus.Rejected)
                return;

            orderInfo.PreviousStatus = orderInfo.Status;
            orderInfo.Status = OrderStatus.CancelPending;
            orderInfo.UpdateTime = DateTime.Now;

            var request = new CancelOrderRequest
            {
                OrderId = orderInfo.OrderId,
                Identity = orderInfo.Identity,
                Instrument = orderInfo.Instrument,
                Symbol = orderInfo.Instrument?.Symbol ?? "",
                Qty = (ushort)orderInfo.LeavesQty,
                BuySell = orderInfo.BuySell,
                WebId = orderInfo.Identity?.WebId ?? "",
                BrokerId = orderInfo.BrokerId,
                Account = orderInfo.Account
            };

            var result = _gateway.CancelOrder(request);
            if (!result.IsSuccess)
            {
                orderInfo.Status = (orderInfo.FilledQty > 0) ? OrderStatus.PartiallyFilled : OrderStatus.Accepted;
                orderInfo.Message = result.ErrorMessage;
                OnCancelFailed?.Invoke(orderInfo, result.ErrorMessage);
            }
            else
            {
                if (!string.IsNullOrEmpty(result.RequestId))
                {
                    _requestMap[result.RequestId] = orderInfo;
                }
            }

            NotifyStateChanged(orderInfo);
        }

        /// <summary>
        /// 處理來自 Gateway 的訂單狀態事件 (包含本系統下單、外部單回報、改刪單結果等)。
        /// </summary>
        /// <param name="ev">標準化的訂單事件物件</param>
        private void HandleOrderEvent(OrderEvent ev)
        {
            OrderInfo order = FindOrCreateOrder(ev);

            order.UpdateTime = ev.EventTime;
            
            // 補足單號與身份識別
            if (!string.IsNullOrEmpty(ev.Identity.Cnt) && string.IsNullOrEmpty(order.Identity.Cnt))
            {
                order.Identity.Cnt = ev.Identity.Cnt;
                _cntMap[order.Identity.Cnt] = order;
            }
            if (!string.IsNullOrEmpty(ev.Identity.OrdNo) && string.IsNullOrEmpty(order.Identity.OrdNo))
            {
                order.Identity.OrdNo = ev.Identity.OrdNo;
                _ordNoMap[order.Identity.OrdNo] = order;
            }
            if (!string.IsNullOrEmpty(ev.Identity.WebId) && string.IsNullOrEmpty(order.Identity.WebId))
            {
                order.Identity.WebId = ev.Identity.WebId;
            }

            // 更新剩餘口數 (若事件有提供)
            if (ev.LeavesQty > 0)
            {
                order.LeavesQty = ev.LeavesQty;
            }

            if (!string.IsNullOrEmpty(ev.ErrorCode) && ev.ErrorCode != "0" && ev.ErrorCode != "000000")
            {
                order.ErrorCode = ev.ErrorCode;
                order.Message = ev.Message;
            }

            switch (ev.EventType)
            {
                case OrderEventType.Ack:
                    break;

                case OrderEventType.Accepted:
                    if (order.Status == OrderStatus.PendingSubmit || order.Status == OrderStatus.Submitted)
                    {
                        order.Status = OrderStatus.Accepted;
                    }
                    else if (order.Status == OrderStatus.ModifyPending)
                    {
                        order.Status = (order.FilledQty > 0) ? OrderStatus.PartiallyFilled : OrderStatus.Accepted;
                    }
                    break;

                case OrderEventType.Rejected:
                    if (ev.OperationType == OrderOperationType.Cancel || order.Status == OrderStatus.CancelPending)
                    {
                        order.Status = (order.FilledQty > 0) ? OrderStatus.PartiallyFilled : OrderStatus.Accepted;
                        OnCancelFailed?.Invoke(order, order.Message);
                    }
                    else if (ev.OperationType == OrderOperationType.Modify || order.Status == OrderStatus.ModifyPending)
                    {
                        order.Status = (order.FilledQty > 0) ? OrderStatus.PartiallyFilled : OrderStatus.Accepted;
                        OnModifyFailed?.Invoke(order, order.Message);
                    }
                    else
                    {
                        order.Status = OrderStatus.Rejected;
                        MoveToHistory(order);
                        OnOrderFailed?.Invoke(order, order.Message);
                    }
                    break;

                case OrderEventType.Cancelled:
                    order.Status = OrderStatus.Cancelled;
                    order.LeavesQty = 0;
                    MoveToHistory(order);
                    break;

                case OrderEventType.Modified:
                    double oldPx = order.OrderPrice;
                    int oldQty = order.OrderQty;
                    double newPx = ev.UpdatedPrice > 0 ? ev.UpdatedPrice : order.OrderPrice;
                    int newQty = ev.UpdatedQty > 0 ? ev.UpdatedQty : order.OrderQty;

                    order.ModifyHistory.Add(new OrderModifyRecord
                    {
                        OldPrice = oldPx,
                        OldQty = oldQty,
                        NewPrice = newPx,
                        NewQty = newQty,
                        ModifyTime = ev.EventTime
                    });
                    
                    if (ev.UpdatedPrice > 0) order.OrderPrice = ev.UpdatedPrice;
                    if (ev.UpdatedQty > 0) 
                    {
                        order.OrderQty = ev.UpdatedQty;
                        order.LeavesQty = Math.Max(0, ev.UpdatedQty - order.FilledQty);
                    }
                    
                    order.Status = (order.FilledQty > 0) ? OrderStatus.PartiallyFilled : OrderStatus.Accepted;
                    break;

                case OrderEventType.CancelFailed:
                    order.Status = (order.FilledQty > 0) ? OrderStatus.PartiallyFilled : OrderStatus.Accepted;
                    OnCancelFailed?.Invoke(order, ev.Message);
                    break;

                case OrderEventType.ModifyFailed:
                    order.Status = (order.FilledQty > 0) ? OrderStatus.PartiallyFilled : OrderStatus.Accepted;
                    OnModifyFailed?.Invoke(order, ev.Message);
                    break;
            }

            NotifyStateChanged(order);
        }

        /// <summary>
        /// 處理來自 Gateway 的成交事件。
        /// </summary>
        /// <param name="ev">標準化的成交事件物件</param>
        private void HandleFillEvent(FillEvent ev)
        {
            OrderInfo order = FindOrCreateOrderByFill(ev);

            order.UpdateTime = ev.EventTime;
            
            // 計算成交均價與累計成交量
            int dealQty = ev.Fill.DealQty;
            double dealPrice = ev.Fill.DealPrice;

            if (dealQty > 0)
            {
                double totalValue = (order.AvgFilledPrice * order.FilledQty) + (dealPrice * dealQty);
                order.FilledQty += dealQty;
                order.AvgFilledPrice = totalValue / order.FilledQty;
            }

            if (ev.Fill.LeavesQty >= 0 && (ev.Fill.LeavesQty > 0 || order.FilledQty > 0))
            {
                order.LeavesQty = ev.Fill.LeavesQty;
            }
            else
            {
                order.LeavesQty = Math.Max(0, order.OrderQty - order.FilledQty);
            }

            // 判斷是否完全成交
            if ((order.OrderQty > 0 && order.FilledQty >= order.OrderQty) || (ev.Fill.LeavesQty == 0 && order.FilledQty > 0))
            {
                order.Status = OrderStatus.Filled;
                MoveToHistory(order);
            }
            else
            {
                order.Status = OrderStatus.PartiallyFilled;
            }

            OnOrderFilled?.Invoke(order, dealQty, dealPrice);
            NotifyStateChanged(order);
        }

        /// <summary>
        /// 處理斷線重連或啟動後的訂單狀態回補 (Order Recovery)。
        /// </summary>
        /// <param name="ev">標準化的還原事件物件</param>
        private void HandleRecoveryEvent(RecoveryEvent ev)
        {
            OrderInfo order = FindOrCreateOrderByRecovery(ev);

            order.UpdateTime = ev.OrderTime;
            if (ev.FilledQty > 0) order.FilledQty = ev.FilledQty;
            if (ev.LeavesQty >= 0) order.LeavesQty = ev.LeavesQty;
            if (ev.AvgFilledPrice > 0) order.AvgFilledPrice = ev.AvgFilledPrice;
            if (ev.OrderPrice > 0) order.OrderPrice = ev.OrderPrice;
            if (ev.OrderQty > 0) order.OrderQty = ev.OrderQty;
            if (ev.Status != default) order.Status = ev.Status;

            if (order.Status == OrderStatus.Filled || order.Status == OrderStatus.Cancelled || order.Status == OrderStatus.Rejected)
            {
                MoveToHistory(order);
            }

            NotifyStateChanged(order);
        }

        private void NotifyStateChanged(OrderInfo order)
        {
            _storageManager.SaveOrder(order);
            OnOrderStateChanged?.Invoke(order);
        }

        #region 多重索引與動態建單輔助函式

        private OrderInfo FindOrCreateOrder(OrderEvent ev)
        {
            // 1. 依 RequestId 查詢
            if (!string.IsNullOrEmpty(ev.Identity.RequestId) && _requestMap.TryGetValue(ev.Identity.RequestId, out var orderByReq))
            {
                return orderByReq;
            }

            // 2. 依 CNT (網路單號) 查詢
            if (!string.IsNullOrEmpty(ev.Identity.Cnt) && _cntMap.TryGetValue(ev.Identity.Cnt, out var orderByCnt))
            {
                return orderByCnt;
            }

            // 3. 依 OrdNo (委託書號) 查詢
            if (!string.IsNullOrEmpty(ev.Identity.OrdNo) && _ordNoMap.TryGetValue(ev.Identity.OrdNo, out var orderByOrdNo))
            {
                return orderByOrdNo;
            }

            // 4. 外部下單 (無事前 RequestId) -> 動態建立外部訂單
            string extOrderId = !string.IsNullOrEmpty(ev.Identity.Cnt) 
                ? $"EXT_CNT_{ev.Identity.Cnt}" 
                : (!string.IsNullOrEmpty(ev.Identity.OrdNo) ? $"EXT_ORD_{ev.Identity.OrdNo}" : $"EXT_{Guid.NewGuid():N}");

            var newOrder = new OrderInfo
            {
                OrderId = extOrderId,
                Source = OrderSource.External,
                Instrument = ev.Instrument ?? new InstrumentKey(),
                BuySell = ev.BuySell,
                OrderPrice = ev.OrderPrice > 0 ? ev.OrderPrice : ev.UpdatedPrice,
                OrderQty = ev.OrderQty > 0 ? ev.OrderQty : ev.UpdatedQty,
                LeavesQty = ev.LeavesQty > 0 ? ev.LeavesQty : (ev.OrderQty > 0 ? ev.OrderQty : ev.UpdatedQty),
                Status = OrderStatus.Accepted,
                SubmitTime = ev.EventTime,
                UpdateTime = ev.EventTime,
                BrokerId = ev.BrokerId,
                Account = ev.Account,
                Identity = new ExternalOrderIdentity
                {
                    Cnt = ev.Identity.Cnt,
                    OrdNo = ev.Identity.OrdNo,
                    WebId = ev.Identity.WebId,
                    RequestId = ev.Identity.RequestId
                }
            };

            _activeOrders[newOrder.OrderId] = newOrder;

            if (!string.IsNullOrEmpty(newOrder.Identity.Cnt))
                _cntMap[newOrder.Identity.Cnt] = newOrder;

            if (!string.IsNullOrEmpty(newOrder.Identity.OrdNo))
                _ordNoMap[newOrder.Identity.OrdNo] = newOrder;

            if (!string.IsNullOrEmpty(newOrder.Identity.RequestId))
                _requestMap[newOrder.Identity.RequestId] = newOrder;

            return newOrder;
        }

        private OrderInfo FindOrCreateOrderByFill(FillEvent ev)
        {
            if (!string.IsNullOrEmpty(ev.Fill.Cnt) && _cntMap.TryGetValue(ev.Fill.Cnt, out var orderByCnt))
            {
                return orderByCnt;
            }

            if (!string.IsNullOrEmpty(ev.Fill.OrdNo) && _ordNoMap.TryGetValue(ev.Fill.OrdNo, out var orderByOrdNo))
            {
                return orderByOrdNo;
            }

            // 若成交先於回報到達，動態建立外部訂單
            string extOrderId = !string.IsNullOrEmpty(ev.Fill.Cnt) 
                ? $"EXT_CNT_{ev.Fill.Cnt}" 
                : (!string.IsNullOrEmpty(ev.Fill.OrdNo) ? $"EXT_ORD_{ev.Fill.OrdNo}" : $"EXT_{Guid.NewGuid():N}");

            int orderQty = (ev.Fill.CumQty > 0) ? ev.Fill.CumQty : ev.Fill.DealQty;
            if (ev.Fill.LeavesQty > 0) orderQty += ev.Fill.LeavesQty;

            var newOrder = new OrderInfo
            {
                OrderId = extOrderId,
                Source = OrderSource.External,
                Instrument = new InstrumentKey { Symbol = ev.Fill.Symbol, Exchange = ev.Fill.Exchange },
                BuySell = ev.BuySell,
                OrderPrice = ev.Fill.DealPrice,
                OrderQty = orderQty,
                LeavesQty = ev.Fill.LeavesQty,
                Status = OrderStatus.PartiallyFilled,
                SubmitTime = ev.EventTime,
                UpdateTime = ev.EventTime,
                Identity = new ExternalOrderIdentity
                {
                    Cnt = ev.Fill.Cnt,
                    OrdNo = ev.Fill.OrdNo
                }
            };

            _activeOrders[newOrder.OrderId] = newOrder;

            if (!string.IsNullOrEmpty(newOrder.Identity.Cnt))
                _cntMap[newOrder.Identity.Cnt] = newOrder;

            if (!string.IsNullOrEmpty(newOrder.Identity.OrdNo))
                _ordNoMap[newOrder.Identity.OrdNo] = newOrder;

            return newOrder;
        }

        private OrderInfo FindOrCreateOrderByRecovery(RecoveryEvent ev)
        {
            if (!string.IsNullOrEmpty(ev.Identity.Cnt) && _cntMap.TryGetValue(ev.Identity.Cnt, out var orderByCnt))
            {
                return orderByCnt;
            }

            if (!string.IsNullOrEmpty(ev.Identity.OrdNo) && _ordNoMap.TryGetValue(ev.Identity.OrdNo, out var orderByOrdNo))
            {
                return orderByOrdNo;
            }

            string recOrderId = !string.IsNullOrEmpty(ev.OrderId) ? ev.OrderId : (!string.IsNullOrEmpty(ev.Identity.Cnt) ? $"REC_{ev.Identity.Cnt}" : $"REC_{Guid.NewGuid():N}");

            var newOrder = new OrderInfo
            {
                OrderId = recOrderId,
                Source = OrderSource.Recovered,
                Instrument = ev.Instrument ?? new InstrumentKey(),
                BuySell = ev.BuySell,
                OrderPrice = ev.OrderPrice,
                OrderQty = ev.OrderQty,
                FilledQty = ev.FilledQty,
                LeavesQty = ev.LeavesQty,
                AvgFilledPrice = ev.AvgFilledPrice,
                Status = ev.Status,
                SubmitTime = ev.OrderTime,
                UpdateTime = ev.OrderTime,
                BrokerId = ev.BrokerId,
                Account = ev.Account,
                Identity = ev.Identity
            };

            _activeOrders[newOrder.OrderId] = newOrder;

            if (!string.IsNullOrEmpty(newOrder.Identity.Cnt))
                _cntMap[newOrder.Identity.Cnt] = newOrder;

            if (!string.IsNullOrEmpty(newOrder.Identity.OrdNo))
                _ordNoMap[newOrder.Identity.OrdNo] = newOrder;

            return newOrder;
        }

        /// <summary>
        /// 將已終結 (Filled/Cancelled/Rejected) 的訂單從活躍字典移至歷史紀錄字典。
        /// </summary>
        /// <param name="order">欲移轉的訂單紀錄</param>
        private void MoveToHistory(OrderInfo order)
        {
            _activeOrders.TryRemove(order.OrderId, out _);
            _historicalOrders[order.OrderId] = order;
        }

        #endregion
    }
}
