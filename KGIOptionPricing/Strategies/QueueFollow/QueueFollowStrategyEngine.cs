using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using KGIOptionPricing.OrderManagement.Core;

namespace KGIOptionPricing.Strategies;

/// <summary>
/// 價量跟隨完成單核心。此類別不依賴 WinForms；UI、API 或背景服務都只透過命令與快照操作。
/// </summary>
public sealed class QueueFollowStrategyEngine : IDisposable
{
    private sealed class Session
    {
        public QueueFollowRequest Request { get; init; } = new();
        public string OrderId { get; set; } = string.Empty;
        public int OrderGeneration { get; set; }
        public int FilledBeforeCurrentOrder { get; set; }
        public QueueFollowState State { get; set; }
        public int FilledQuantity { get; set; }
        public int RemainingQuantity { get; set; }
        public decimal CurrentPrice { get; set; }
        public decimal PlannedPrice { get; set; }
        public int SamePriceAheadQuantity { get; set; }
        public int BetterPriceQuantity { get; set; }
        public int PreviousDisplayedQuantity { get; set; }
        public int PendingTargetAheadQuantity { get; set; }
        public bool StopRequested { get; set; }
        public bool ReplaceAtFirstAfterCancel { get; set; }
        public bool ReplaceAtTargetAfterCancel { get; set; }
        public bool WaitingToReenterAtFirst { get; set; }
        public bool WaitingToReenterAtTarget { get; set; }
        public bool AutomaticActionsSuspended { get; set; }
        public string Reason { get; set; } = string.Empty;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
        public OrderBookSnapshot? LatestBook { get; set; }
    }

    private readonly object _sync = new();
    private readonly IQueueFollowMarketDataSource _market;
    private readonly IQueueFollowOrderService _orders;
    private readonly IQueueFollowTickProvider _ticks;
    private readonly QueueFollowPricePlanner _planner;
    private readonly IQueueFollowHistorySink _history;
    private readonly Dictionary<string, Session> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Session> _orderSessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _runId = Guid.NewGuid().ToString("N");
    private bool _disposed;
    private long _historySequence;

    public event Action<QueueFollowSnapshot>? SessionUpdated;

    public QueueFollowStrategyEngine(
        IQueueFollowMarketDataSource market,
        IQueueFollowOrderService orders,
        IQueueFollowTickProvider tickProvider,
        IQueueFollowHistorySink? history = null)
    {
        _market = market;
        _orders = orders;
        _ticks = tickProvider;
        _planner = new QueueFollowPricePlanner(tickProvider);
        _history = history ?? NullQueueFollowHistorySink.Instance;
        _market.MarketChanged += OnMarketChanged;
        _orders.OrderChanged += OnOrderChanged;
        _orders.OrderFilled += OnOrderFilled;
        _orders.OrderFailed += OnOrderFailed;
        _orders.ModifyFailed += OnModifyFailed;
        _orders.CancelFailed += OnCancelFailed;
    }

    public string Start(QueueFollowRequest request)
    {
        Validate(request);
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_sessions.ContainsKey(request.StrategyId))
                throw new InvalidOperationException("策略識別碼已存在。");

            string orderId = $"QFS_{request.StrategyId}";
            var session = new Session
            {
                Request = request,
                OrderId = orderId,
                State = QueueFollowState.WaitingForMarket,
                RemainingQuantity = request.Quantity,
                Reason = "等待有效買賣第一檔"
            };
            _sessions[request.StrategyId] = session;
            _orderSessions[orderId] = session;
            Record(session, QueueFollowHistoryEventType.StrategyStarted, action: "Start", reason: "策略建立");
            try
            {
                _market.Subscribe(request.QuoteSymbol);
            }
            catch (Exception ex)
            {
                Fault(session, "行情訂閱失敗：" + ex.Message);
                return request.StrategyId;
            }

            if (_market.TryGetSnapshot(request.QuoteSymbol, out OrderBookSnapshot snapshot))
            {
                session.LatestBook = snapshot;
                Record(session, QueueFollowHistoryEventType.MarketChanged, book: snapshot, reason: "啟動時行情快照");
                TryPlaceInitialOrder(session);
            }
            else
            {
                Publish(session);
            }
            return request.StrategyId;
        }
    }

    public bool Stop(string strategyId)
    {
        lock (_sync)
        {
            if (!_sessions.TryGetValue(strategyId, out Session? session)) return false;
            if (session.State is QueueFollowState.Completed or QueueFollowState.Stopped) return true;
            session.StopRequested = true;
            session.Reason = "已要求停止，等待可刪單狀態";
            Record(session, QueueFollowHistoryEventType.StrategyStopRequested, action: "Stop", reason: session.Reason);

            if (session.State == QueueFollowState.WaitingForMarket)
            {
                session.State = QueueFollowState.Stopped;
                session.Reason = "尚未送單，策略已停止";
                Publish(session);
            }
            else if (session.State is QueueFollowState.Working or QueueFollowState.PendingModify or QueueFollowState.Faulted)
            {
                CancelWorkingOrder(session);
            }
            else
            {
                Publish(session);
            }
            return true;
        }
    }

    public void StopAll()
    {
        string[] ids;
        lock (_sync) ids = _sessions.Keys.ToArray();
        foreach (string id in ids) Stop(id);
    }

    public IReadOnlyList<QueueFollowSnapshot> GetSnapshots()
    {
        lock (_sync) return _sessions.Values.Select(ToSnapshot).ToArray();
    }

    private void OnMarketChanged(OrderBookSnapshot book)
    {
        lock (_sync)
        {
            foreach (Session session in _sessions.Values.Where(s =>
                         SymbolMatches(book.Symbol, s.Request.QuoteSymbol) &&
                         s.State is not QueueFollowState.Completed and not QueueFollowState.Stopped and not QueueFollowState.Faulted).ToArray())
            {
                session.LatestBook = book;
                Record(session, QueueFollowHistoryEventType.MarketChanged, book: book);
                if (session.State == QueueFollowState.WaitingForMarket)
                {
                    if (session.WaitingToReenterAtFirst) TryPlaceAtFirst(session);
                    else if (session.WaitingToReenterAtTarget) TryPlaceAtTarget(session);
                    else TryPlaceInitialOrder(session);
                }
                else if (session.State == QueueFollowState.Working)
                {
                    UpdateAheadEstimate(session, book);
                    EvaluateWorkingOrder(session);
                }
            }
        }
    }

    private void TryPlaceInitialOrder(Session session)
    {
        OrderBookSnapshot? book = session.LatestBook;
        if (book == null || book.BestBid <= 0 || book.BestAsk <= 0)
        {
            session.Reason = "等待有效買賣第一檔";
            Publish(session);
            return;
        }
        if (!_orders.IsReady)
        {
            session.Reason = "交易服務尚未登入";
            Publish(session);
            return;
        }

        decimal target = _planner.SelectPriceForAheadTarget(
            session.Request,
            book,
            session.Request.InitialTargetAheadQuantity);
        PlaceNewOrder(session, target,
            $"首次目標前方量 A={session.Request.InitialTargetAheadQuantity}，送出初始委託 {target}",
            "Place");
    }

    private void TryPlaceAtFirst(Session session)
    {
        OrderBookSnapshot? book = session.LatestBook;
        if (book == null || book.BestBid <= 0 || book.BestAsk <= 0)
        {
            session.State = QueueFollowState.WaitingForMarket;
            session.Reason = "舊單已刪除，等待有效第一檔重下";
            Publish(session);
            return;
        }
        if (!_orders.IsReady)
        {
            session.State = QueueFollowState.WaitingForMarket;
            session.Reason = "舊單已刪除，等待交易服務登入後重下第一檔";
            Publish(session);
            return;
        }

        decimal target = _planner.SelectFirstPrice(session.Request, book);
        PlaceNewOrder(session, target, $"原價位只剩自身委託，重下第一檔 {target}", "PlaceAtFirst");
    }

    private void TryPlaceAtTarget(Session session)
    {
        OrderBookSnapshot? book = session.LatestBook;
        if (book == null || book.BestBid <= 0 || book.BestAsk <= 0)
        {
            session.State = QueueFollowState.WaitingForMarket;
            session.Reason = "舊單已刪除，等待有效行情重新計算 C 價位";
            Publish(session);
            return;
        }
        if (!_orders.IsReady)
        {
            session.State = QueueFollowState.WaitingForMarket;
            session.Reason = "舊單已刪除，等待交易服務登入後重掛";
            Publish(session);
            return;
        }

        decimal target = _planner.SelectPriceForAheadTarget(
            session.Request,
            book,
            session.Request.RepriceTargetAheadQuantity);
        PlaceNewOrder(session, target,
            $"舊單刪除已確認，重掛至目標前方量 C={session.Request.RepriceTargetAheadQuantity} 的價位 {target}",
            "PlaceAfterCancel");
    }

    private void PlaceNewOrder(Session session, decimal target, string reason, string historyAction)
    {
        OrderBookSnapshot? book = session.LatestBook;
        if (target <= 0)
        {
            session.State = QueueFollowState.Faulted;
            session.Reason = "無法計算合法新單價格";
            Publish(session);
            return;
        }

        session.WaitingToReenterAtFirst = false;
        session.WaitingToReenterAtTarget = false;
        session.CurrentPrice = target;
        session.PlannedPrice = target;
        session.SamePriceAheadQuantity = book == null ? 0 : QueueFollowPricePlanner.QuantityAtPrice(book, session.Request.Side, target);
        session.BetterPriceQuantity = book == null ? 0 : QueueFollowPricePlanner.BetterPriceQuantity(session.Request.Side, book, target);
        session.PreviousDisplayedQuantity = session.SamePriceAheadQuantity;
        session.State = QueueFollowState.PendingNew;
        session.Reason = reason;
        Publish(session);

        try
        {
            Record(session, QueueFollowHistoryEventType.OrderCommand, action: historyAction, targetPrice: target, reason: session.Reason);
            _orders.Place(new OrderRequest
            {
                OrderId = session.OrderId,
                Instrument = session.Request.Instrument,
                BuySell = session.Request.Side,
                Price = (double)target,
                Quantity = session.RemainingQuantity,
                BrokerId = session.Request.BrokerId,
                Account = session.Request.Account,
                TimeInForce = session.Request.TimeInForce,
                PositionEffect = session.Request.PositionEffect
            });
        }
        catch (Exception ex)
        {
            Fault(session, "新單送出例外：" + ex.Message);
        }
    }

    private void OnOrderChanged(OrderInfo order)
    {
        lock (_sync)
        {
            if (!_orderSessions.TryGetValue(order.OrderId, out Session? session)) return;
            Record(session, QueueFollowHistoryEventType.OrderReport, action: order.Status.ToString(), order: order, reason: order.Message);
            UpdateAggregateFill(session, order);
            if (order.OrderPrice > 0) session.CurrentPrice = (decimal)order.OrderPrice;

            switch (order.Status)
            {
                case OrderStatus.PendingSubmit:
                case OrderStatus.Submitted:
                    session.State = QueueFollowState.PendingNew;
                    session.Reason = "等待新單確認";
                    Publish(session);
                    break;

                case OrderStatus.ModifyPending:
                    session.State = QueueFollowState.PendingModify;
                    session.Reason = $"等待改價確認：{session.PlannedPrice}";
                    Publish(session);
                    break;

                case OrderStatus.CancelPending:
                    session.State = QueueFollowState.PendingCancel;
                    session.Reason = "等待刪單確認";
                    Publish(session);
                    break;

                case OrderStatus.Accepted:
                case OrderStatus.PartiallyFilled:
                    if (session.RemainingQuantity <= 0)
                    {
                        Complete(session);
                        break;
                    }
                    // OrderManager 在刪改失敗事件後會再通知原單仍有效；不得把失敗狀態
                    // 誤認為新一次確認並自動重送。由呼叫端明確決定後續處置。
                    if (session.State == QueueFollowState.Faulted)
                    {
                        Publish(session);
                        break;
                    }
                    // 改價失敗後已送出替代刪單時，延遲的原單有效通知不可取消 PendingCancel。
                    if (session.State == QueueFollowState.PendingCancel &&
                        (session.ReplaceAtFirstAfterCancel || session.ReplaceAtTargetAfterCancel))
                    {
                        session.Reason = "改價失敗，已送出刪單並等待取消確認";
                        Publish(session);
                        break;
                    }
                    if (session.StopRequested)
                    {
                        CancelWorkingOrder(session);
                        break;
                    }
                    if (session.AutomaticActionsSuspended)
                    {
                        session.State = QueueFollowState.Working;
                        Publish(session);
                        break;
                    }

                    bool modified = session.State == QueueFollowState.PendingModify;
                    session.State = QueueFollowState.Working;
                    if (modified)
                        session.SamePriceAheadQuantity = session.PendingTargetAheadQuantity;
                    if (session.LatestBook != null)
                        session.PreviousDisplayedQuantity = QueueFollowPricePlanner.QuantityAtPrice(session.LatestBook, session.Request.Side, session.CurrentPrice);
                    session.Reason = modified ? "改價已確認，重新評估最新行情" : "委託有效，評估前方價量";
                    EvaluateWorkingOrder(session);
                    break;

                case OrderStatus.Filled:
                    Complete(session);
                    break;

                case OrderStatus.Cancelled:
                    if ((session.ReplaceAtFirstAfterCancel || session.ReplaceAtTargetAfterCancel) && !session.StopRequested)
                    {
                        bool reenterAtFirst = session.ReplaceAtFirstAfterCancel;
                        session.ReplaceAtFirstAfterCancel = false;
                        session.ReplaceAtTargetAfterCancel = false;
                        if (session.RemainingQuantity <= 0)
                        {
                            Complete(session);
                            break;
                        }

                        string cancelledOrderId = session.OrderId;
                        _orderSessions.Remove(cancelledOrderId);
                        session.FilledBeforeCurrentOrder = session.FilledQuantity;
                        session.OrderGeneration++;
                        session.OrderId = $"QFS_{session.Request.StrategyId}_R{session.OrderGeneration}";
                        _orderSessions[session.OrderId] = session;
                        session.WaitingToReenterAtFirst = reenterAtFirst;
                        session.WaitingToReenterAtTarget = !reenterAtFirst;
                        session.State = QueueFollowState.WaitingForMarket;
                        session.Reason = reenterAtFirst
                            ? "舊單刪除已確認，準備以新單重下第一檔"
                            : "舊單刪除已確認，準備依 C 重新計算價格並送新單";
                        Record(session, QueueFollowHistoryEventType.Decision,
                            action: "ReenterScheduled", reason: session.Reason);
                        Publish(session);
                        if (reenterAtFirst) TryPlaceAtFirst(session); else TryPlaceAtTarget(session);
                        break;
                    }

                    session.State = QueueFollowState.Stopped;
                    session.RemainingQuantity = 0;
                    session.Reason = session.StopRequested ? "剩餘委託已刪除，策略停止" : "委託已取消";
                    Publish(session);
                    break;

                case OrderStatus.Rejected:
                    Fault(session, string.IsNullOrWhiteSpace(order.Message) ? "委託遭拒絕" : order.Message);
                    break;
            }
        }
    }

    private void OnOrderFilled(OrderInfo order, int quantity, double price)
    {
        lock (_sync)
        {
            if (!_orderSessions.TryGetValue(order.OrderId, out Session? session)) return;
            Record(session, QueueFollowHistoryEventType.Fill, action: "Fill", order: order,
                targetPrice: (decimal)price, reason: $"成交 {quantity} 口");
            UpdateAggregateFill(session, order);
            session.Reason = session.RemainingQuantity == 0
                ? "目標口數已全部成交"
                : $"部分成交 {quantity} 口，剩餘 {session.RemainingQuantity} 口";
            if (session.RemainingQuantity == 0) Complete(session); else Publish(session);
        }
    }

    private void OnOrderFailed(OrderInfo order, string reason)
    {
        lock (_sync) if (_orderSessions.TryGetValue(order.OrderId, out Session? session)) Fault(session, "新單失敗：" + reason, order);
    }

    private void OnModifyFailed(OrderInfo order, string reason)
    {
        lock (_sync)
        {
            if (!_orderSessions.TryGetValue(order.OrderId, out Session? session)) return;
            decimal failedTarget = session.PlannedPrice;
            UpdateAggregateFill(session, order);
            if (order.OrderPrice > 0) session.CurrentPrice = (decimal)order.OrderPrice;
            session.AutomaticActionsSuspended = false;
            session.State = QueueFollowState.Working;
            session.Reason = "改價失敗，原委託仍有效：" + reason;
            Record(session, QueueFollowHistoryEventType.Error,
                action: "ModifyFailedFallback", order: order, reason: session.Reason);

            if (session.StopRequested)
                CancelWorkingOrder(session);
            else
                CancelForTargetReentry(session, failedTarget);
        }
    }

    private void OnCancelFailed(OrderInfo order, string reason)
    {
        lock (_sync)
        {
            if (!_orderSessions.TryGetValue(order.OrderId, out Session? session)) return;
            UpdateAggregateFill(session, order);
            if (order.OrderPrice > 0) session.CurrentPrice = (decimal)order.OrderPrice;
            session.ReplaceAtFirstAfterCancel = false;
            session.ReplaceAtTargetAfterCancel = false;
            session.AutomaticActionsSuspended = true;
            session.StopRequested = false;
            session.State = QueueFollowState.Working;
            session.Reason = "刪單失敗，原委託仍有效；可再次按停止策略重試刪單：" + reason;
            Record(session, QueueFollowHistoryEventType.Error,
                action: "CancelFailedRecoverable", order: order, reason: session.Reason);
            Publish(session);
        }
    }

    private void UpdateAheadEstimate(Session session, OrderBookSnapshot book)
    {
        int displayed = QueueFollowPricePlanner.QuantityAtPrice(book, session.Request.Side, session.CurrentPrice);
        if (displayed < session.PreviousDisplayedQuantity)
            session.SamePriceAheadQuantity = Math.Max(0, session.SamePriceAheadQuantity - (session.PreviousDisplayedQuantity - displayed));
        session.PreviousDisplayedQuantity = displayed;
    }

    private void EvaluateWorkingOrder(Session session)
    {
        if (session.State != QueueFollowState.Working || session.LatestBook == null) return;
        if (session.AutomaticActionsSuspended) return;
        if (!_orders.IsReady)
        {
            session.Reason = "交易服務未登入，保留委託並暫停操作";
            Publish(session);
            return;
        }

        if (ShouldCancelAndReenterAtFirst(session, session.LatestBook, out decimal adjacentPrice))
        {
            CancelForFirstReentry(session, adjacentPrice);
            return;
        }

        QueueFollowDecision decision = _planner.Decide(
            session.Request,
            session.LatestBook,
            session.CurrentPrice,
            session.RemainingQuantity,
            session.SamePriceAheadQuantity);
        session.BetterPriceQuantity = decision.BetterPriceQuantity;
        session.Reason = decision.Reason;
        Record(session, QueueFollowHistoryEventType.Decision,
            action: decision.Kind.ToString(), targetPrice: decision.TargetPrice, reason: decision.Reason);
        if (decision.Kind == QueueFollowDecisionKind.Hold)
        {
            Publish(session);
            return;
        }

        session.PlannedPrice = decision.TargetPrice;
        session.PendingTargetAheadQuantity = QueueFollowPricePlanner.QuantityAtPrice(
            session.LatestBook, session.Request.Side, decision.TargetPrice);
        session.State = QueueFollowState.PendingModify;
        Publish(session);
        try
        {
            Record(session, QueueFollowHistoryEventType.OrderCommand, action: "Modify",
                targetPrice: decision.TargetPrice, reason: session.Reason);
            _orders.Modify(session.OrderId, (double)decision.TargetPrice, session.Request.Quantity);
        }
        catch (Exception ex)
        {
            Fault(session, "改價送出例外，委託可能仍有效：" + ex.Message);
        }
    }

    private bool ShouldCancelAndReenterAtFirst(Session session, OrderBookSnapshot book, out decimal adjacentPrice)
    {
        adjacentPrice = 0m;
        if (session.RemainingQuantity <= 0 || session.CurrentPrice <= 0) return false;

        decimal firstPrice = session.Request.Side == BuySell.Buy ? book.BestBid : book.BestAsk;
        if (firstPrice <= 0) return false;

        // 只有委託落後第一檔時才套用此規則；重掛第一檔後不可再次觸發。
        bool isBehindFirst = session.Request.Side == BuySell.Buy
            ? session.CurrentPrice < firstPrice
            : session.CurrentPrice > firstPrice;
        if (!isBehindFirst) return false;

        int displayedAtOwnPrice = QueueFollowPricePlanner.QuantityAtPrice(book, session.Request.Side, session.CurrentPrice);
        if (displayedAtOwnPrice != session.RemainingQuantity) return false;

        adjacentPrice = _ticks.Next(
            session.CurrentPrice,
            session.Request.TickType,
            upward: session.Request.Side == BuySell.Buy);

        // 下一跳必須確實位於目前委託與第一檔之間，並且完全沒有市場量。
        bool adjacentIsBetween = session.Request.Side == BuySell.Buy
            ? adjacentPrice < firstPrice
            : adjacentPrice > firstPrice;
        return adjacentIsBetween &&
               QueueFollowPricePlanner.QuantityAtPrice(book, session.Request.Side, adjacentPrice) == 0;
    }

    private void CancelForFirstReentry(Session session, decimal adjacentPrice)
    {
        if (session.State == QueueFollowState.PendingCancel) return;

        session.ReplaceAtFirstAfterCancel = true;
        session.ReplaceAtTargetAfterCancel = false;
        session.State = QueueFollowState.PendingCancel;
        session.Reason = $"目前價位 {session.CurrentPrice} 只剩自身 {session.RemainingQuantity} 口，鄰近價位 {adjacentPrice} 空檔；先刪單再重下第一檔";
        Publish(session);
        try
        {
            Record(session, QueueFollowHistoryEventType.OrderCommand,
                action: "CancelForFirstReentry", reason: session.Reason);
            _orders.Cancel(session.OrderId);
        }
        catch (Exception ex)
        {
            Fault(session, "重掛第一檔前刪單送出例外，原委託可能仍有效：" + ex.Message);
        }
    }

    private void CancelForTargetReentry(Session session, decimal plannedPrice)
    {
        if (session.State == QueueFollowState.PendingCancel) return;

        session.PlannedPrice = plannedPrice;
        session.ReplaceAtFirstAfterCancel = false;
        session.ReplaceAtTargetAfterCancel = true;
        session.State = QueueFollowState.PendingCancel;
        session.Reason += $"；先刪除原單，確認後再以新單重掛（參考價 {plannedPrice}）";
        Publish(session);
        try
        {
            Record(session, QueueFollowHistoryEventType.OrderCommand,
                action: "CancelForReprice", targetPrice: plannedPrice, reason: session.Reason);
            _orders.Cancel(session.OrderId);
        }
        catch (Exception ex)
        {
            Fault(session, "重新掛單前刪單送出例外，原委託可能仍有效：" + ex.Message);
        }
    }

    private void CancelWorkingOrder(Session session)
    {
        if (session.State == QueueFollowState.PendingCancel) return;
        session.State = QueueFollowState.PendingCancel;
        session.Reason = "送出剩餘委託刪單";
        Publish(session);
        try
        {
            Record(session, QueueFollowHistoryEventType.OrderCommand, action: "Cancel", reason: session.Reason);
            _orders.Cancel(session.OrderId);
        }
        catch (Exception ex)
        {
            Fault(session, "刪單送出例外，委託可能仍有效：" + ex.Message);
        }
    }

    private static void UpdateAggregateFill(Session session, OrderInfo order)
    {
        int filledForCurrentOrder = Math.Max(0, order.FilledQty);
        session.FilledQuantity = Math.Min(
            session.Request.Quantity,
            Math.Max(session.FilledQuantity, session.FilledBeforeCurrentOrder + filledForCurrentOrder));
        session.RemainingQuantity = Math.Max(0, session.Request.Quantity - session.FilledQuantity);
    }

    private void Complete(Session session)
    {
        session.State = QueueFollowState.Completed;
        session.FilledQuantity = Math.Max(session.FilledQuantity, session.Request.Quantity);
        session.RemainingQuantity = 0;
        session.Reason = "目標口數已全部成交";
        Publish(session);
    }

    private void Fault(Session session, string reason, OrderInfo? order = null)
    {
        session.State = QueueFollowState.Faulted;
        session.Reason = reason;
        Record(session, QueueFollowHistoryEventType.Error, action: "Fault", order: order, reason: reason);
        Publish(session);
    }

    private void Publish(Session session)
    {
        session.UpdatedAt = DateTime.Now;
        Record(session, QueueFollowHistoryEventType.StateChanged, action: session.State.ToString(), reason: session.Reason);
        try { SessionUpdated?.Invoke(ToSnapshot(session)); } catch { }
    }

    private void Record(
        Session session,
        QueueFollowHistoryEventType eventType,
        string action = "",
        string reason = "",
        decimal targetPrice = 0m,
        OrderBookSnapshot? book = null,
        OrderInfo? order = null)
    {
        try
        {
            _history.Record(new QueueFollowHistoryEntry
            {
                Sequence = Interlocked.Increment(ref _historySequence),
                RunId = _runId,
                Timestamp = DateTimeOffset.Now,
                EventType = eventType,
                StrategyId = session.Request.StrategyId,
                OrderId = session.OrderId,
                Symbol = session.Request.QuoteSymbol,
                Exchange = session.Request.Instrument.Exchange,
                ContractMonth = session.Request.Instrument.ContractMonth,
                Side = session.Request.Side,
                State = session.State,
                Action = action,
                Reason = reason,
                CurrentOrderPrice = session.CurrentPrice,
                TargetPrice = targetPrice,
                PriceLimit = session.Request.PriceLimit,
                TargetQuantity = session.Request.Quantity,
                FilledQuantity = session.FilledQuantity,
                RemainingQuantity = session.RemainingQuantity,
                InitialTargetAheadQuantity = session.Request.InitialTargetAheadQuantity,
                RepriceAheadThreshold = session.Request.RepriceAheadThreshold,
                RepriceTargetAheadQuantity = session.Request.RepriceTargetAheadQuantity,
                SamePriceAheadQuantity = session.SamePriceAheadQuantity,
                BetterPriceQuantity = session.BetterPriceQuantity,
                LastPrice = book?.LastPrice ?? session.LatestBook?.LastPrice ?? 0m,
                MarketReceivedAt = book?.ReceivedAt,
                BidPrices = book?.BidPrices.ToArray() ?? Array.Empty<decimal>(),
                BidQuantities = book?.BidQuantities.ToArray() ?? Array.Empty<int>(),
                AskPrices = book?.AskPrices.ToArray() ?? Array.Empty<decimal>(),
                AskQuantities = book?.AskQuantities.ToArray() ?? Array.Empty<int>(),
                OrderStatus = order?.Status,
                ReportedOrderPrice = order == null ? 0m : (decimal)order.OrderPrice,
                ReportedOrderQuantity = order?.OrderQty ?? 0,
                ReportedFilledQuantity = order?.FilledQty ?? 0,
                ReportedRemainingQuantity = order?.LeavesQty ?? 0,
                Cnt = order?.Identity.Cnt ?? string.Empty,
                OrgCnt = order?.Identity.OrgCnt ?? string.Empty,
                OrdNo = order?.Identity.OrdNo ?? string.Empty,
                WebId = order?.Identity.WebId ?? string.Empty,
                ErrorCode = order?.ErrorCode ?? string.Empty
            });
        }
        catch
        {
            // 歷程記錄失敗不得影響交易決策與委託管理。
        }
    }

    private static QueueFollowSnapshot ToSnapshot(Session session) => new()
    {
        StrategyId = session.Request.StrategyId,
        OrderId = session.OrderId,
        Instrument = session.Request.Instrument,
        QuoteSymbol = session.Request.QuoteSymbol,
        Side = session.Request.Side,
        TargetQuantity = session.Request.Quantity,
        FilledQuantity = session.FilledQuantity,
        RemainingQuantity = session.RemainingQuantity,
        InitialTargetAheadQuantity = session.Request.InitialTargetAheadQuantity,
        RepriceAheadThreshold = session.Request.RepriceAheadThreshold,
        RepriceTargetAheadQuantity = session.Request.RepriceTargetAheadQuantity,
        CurrentOrderPrice = session.CurrentPrice,
        PlannedPrice = session.PlannedPrice,
        PriceLimit = session.Request.PriceLimit,
        SamePriceAheadQuantity = session.SamePriceAheadQuantity,
        BetterPriceQuantity = session.BetterPriceQuantity,
        State = session.State,
        Reason = session.Reason,
        UpdatedAt = session.UpdatedAt
    };

    private static void Validate(QueueFollowRequest request)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.StrategyId)) throw new ArgumentException("缺少策略識別碼。");
        if (request.Instrument == null || string.IsNullOrWhiteSpace(request.Instrument.Symbol)) throw new ArgumentException("缺少交易商品。");
        if (string.IsNullOrWhiteSpace(request.QuoteSymbol)) throw new ArgumentException("缺少行情商品代碼。");
        if (request.Quantity <= 0) throw new ArgumentException("口數必須大於零。");
        if (request.InitialTargetAheadQuantity < 0) throw new ArgumentException("首次目標前方量 (A) 不可小於零。");
        if (request.RepriceAheadThreshold <= 0) throw new ArgumentException("重新掛單門檻 (B) 必須大於零。");
        if (request.RepriceTargetAheadQuantity < 0) throw new ArgumentException("改掛目標前方量 (C) 不可小於零。");
        if (request.RepriceTargetAheadQuantity >= request.RepriceAheadThreshold)
            throw new ArgumentException("改掛目標前方量 (C) 必須小於重新掛單門檻 (B)。");
        if (request.Side == BuySell.Buy && request.PriceLimit <= 0) throw new ArgumentException("買進必須設定最高可接受價格。");
        if (request.PriceLimit < 0) throw new ArgumentException("價格限制不可小於零。");
    }

    private static bool SymbolMatches(string left, string right) =>
        left.Equals(right, StringComparison.OrdinalIgnoreCase) ||
        left.EndsWith("." + right, StringComparison.OrdinalIgnoreCase) ||
        right.EndsWith("." + left, StringComparison.OrdinalIgnoreCase);

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(QueueFollowStrategyEngine));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _market.MarketChanged -= OnMarketChanged;
        _orders.OrderChanged -= OnOrderChanged;
        _orders.OrderFilled -= OnOrderFilled;
        _orders.OrderFailed -= OnOrderFailed;
        _orders.ModifyFailed -= OnModifyFailed;
        _orders.CancelFailed -= OnCancelFailed;
        if (_market is IDisposable marketDisposable) marketDisposable.Dispose();
        if (_orders is IDisposable orderDisposable) orderDisposable.Dispose();
        if (_history is IDisposable historyDisposable) historyDisposable.Dispose();
    }
}
