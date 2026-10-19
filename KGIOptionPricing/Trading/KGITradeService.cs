using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Intelligence;
using log4net;
using Package;
using Smart;
using KGIOptionPricing.OrderManagement.Core;
using KGIOptionPricing.OrderManagement.Gateway;

namespace KGIOptionPricing
{
    public class TradePositionItem
    {
        public string BrokerId { get; set; } = string.Empty;
        public string Account { get; set; } = string.Empty;
        public string Group { get; set; } = string.Empty;
        public string Trader { get; set; } = string.Empty;
        public string Exchange { get; set; } = string.Empty;
        public string ComType { get; set; } = string.Empty;
        public string Commodity { get; set; } = string.Empty;
        public string SettleMonth { get; set; } = string.Empty;
        public double StrikePrice { get; set; }
        public string CallPut { get; set; } = string.Empty;
        public string BuySell { get; set; } = string.Empty;
        public int OpenInterest { get; set; }
        public double AveragePrice { get; set; }
        public double MarketPrice { get; set; }
        public double UnrealizedPnL { get; set; }
        public string Currency { get; set; } = string.Empty;
    }

    public class TradePositionDetailItem
    {
        public string TradeDate { get; set; } = string.Empty;
        public string OrdNo { get; set; } = string.Empty;
        public string MarketNo { get; set; } = string.Empty;
        public string Exchange { get; set; } = string.Empty;
        public string ComID { get; set; } = string.Empty;
        public string ComYM { get; set; } = string.Empty;
        public double StrikePrice { get; set; }
        public string CP { get; set; } = string.Empty;
        public string BS { get; set; } = string.Empty;
        public int Qty { get; set; }
        public double DealPrice { get; set; }
        public double MPrice { get; set; }
        public double PRTLOS { get; set; }
        public double InitialMargin { get; set; }
        public double MTMargin { get; set; }
        public string Currency { get; set; } = string.Empty;
        public int SpKey { get; set; }
        public string BrokerId { get; set; } = string.Empty;
        public string Account { get; set; } = string.Empty;
        public string Trader { get; set; } = string.Empty;
    }

    public class Financial_TradernItem
    {
        public string BrokerId { get; set; } = string.Empty;
        public string Account { get; set; } = string.Empty;
        public string Group { get; set; } = string.Empty;
        public string Trader { get; set; } = string.Empty;
        public string Currency { get; set; } = string.Empty;
        public double LCTDAB { get; set; }
        public double ORIGNFEE { get; set; }
        public double TAXAMT { get; set; }
        public double CTAXAMT { get; set; }
        public double DWAMT { get; set; }
        public double OSPRTLOS { get; set; }
        public double PRTLOS { get; set; }
        public double BMKTVAL { get; set; }
        public double SMKTVAL { get; set; }
        public double OPREMIUM { get; set; }
        public double TPREMIUM { get; set; }
        public double EQUITY { get; set; }
        public double IAMT { get; set; }
        public double MAMT { get; set; }
        public double EXCESS { get; set; }
        public double ORDEXCESS { get; set; }
        public double ORDAMT { get; set; }
        public double ExProfit { get; set; }
        public double Premium { get; set; }
        public string PTime { get; set; } = string.Empty;
        public double FloatProfit { get; set; }
        public double LASSPRTLOS { get; set; }
        public double CLOSEAMT { get; set; }
        public double ORDIAMT { get; set; }
        public double ORDMAMT { get; set; }
        public double DayTradeAMT { get; set; }
        public double ReductionAMT { get; set; }
        public double CreditAMT { get; set; }
        public double Balance { get; set; }
        public double IPremium { get; set; }
        public double OPremium { get; set; }
        public double Securities { get; set; }
        public double SecuritiesOffset { get; set; }
        public double OffsetAMT { get; set; }
        public double Offset { get; set; }
        public double FULLMTRISK { get; set; }
        public double FULLRISK { get; set; }
        public double MarginCall { get; set; }
        public double SellVerticalSpread { get; set; }
        public double StrikePrice { get; set; }
        public double ActMarketValue { get; set; }
        public double TPRTLOS { get; set; }
        public double MarginCall1 { get; set; }
        public double AddMargin { get; set; }
        public double ORDAMTNOCN { get; set; }
        public double WithdrawMnt { get; set; }
        public double OPREMIUM1 { get; set; }
        public double OPREMIUM2 { get; set; }
    }

    public class TradeHistoryItem
    {
        public string Market { get; set; } = string.Empty;
        public string BrokerId { get; set; } = string.Empty;
        public string Account { get; set; } = string.Empty;
        public string Group { get; set; } = string.Empty;
        public string Trader { get; set; } = string.Empty;
        public string RtnCode { get; set; } = string.Empty;
        public string Exchange { get; set; } = string.Empty;
        public string OccDT { get; set; } = string.Empty;
        public string TrdDT1 { get; set; } = string.Empty;
        public string OrdNo1 { get; set; } = string.Empty;
        public string FirmOrd1 { get; set; } = string.Empty;
        public string OffsetSpliteSeqNo { get; set; } = string.Empty;
        public string TrdDT2 { get; set; } = string.Empty;
        public string OrdNo2 { get; set; } = string.Empty;
        public string FirmOrd2 { get; set; } = string.Empty;
        public string OffsetSpliteSeqNo2 { get; set; } = string.Empty;
        public string OffsetCode { get; set; } = string.Empty;
        public string Offset { get; set; } = string.Empty;
        public string BS { get; set; } = string.Empty;
        public string ComID { get; set; } = string.Empty;
        public string ComYM { get; set; } = string.Empty;
        public double StrikePrice { get; set; }
        public string CP { get; set; } = string.Empty;
        public string ComID2 { get; set; } = string.Empty;
        public int Qty1 { get; set; }
        public int Qty2 { get; set; }
        public double TrdPrice1 { get; set; }
        public double TrdPrice2 { get; set; }
        public double PRTLOS { get; set; }
        public string AENO { get; set; } = string.Empty;
        public string Currency { get; set; } = string.Empty;
        public double CTAXAMT { get; set; }
        public double ORIGNFEE { get; set; }
        public double Premium1 { get; set; }
        public double Premium2 { get; set; }
        public string InNo1 { get; set; } = string.Empty;
        public string InNo2 { get; set; } = string.Empty;
        public string Cnt1 { get; set; } = string.Empty;
        public string Cnt2 { get; set; } = string.Empty;
        public double CurrencyRate { get; set; }
    }

    public class KGITradeService : IOrderGateway
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(KGITradeService));

        private TaiFexCom? _tfcom;
        private readonly string _host;
        private readonly ushort _port;
        private readonly string _userId;
        private readonly string _password;
        private readonly string _account;
        private readonly string _borkerID;

        public string Account => _account;
        public string BrokerId => _borkerID;
        public string BrokerID => _borkerID;

        public event Action<string>? OnLogMessage;
        public event Action<string, bool>? OnStatusChanged;
        public event Action<List<TradePositionItem>>? OnPositionsUpdated;
        public event Action<List<TradePositionDetailItem>>? OnPositionsDetailUpdated;
        public event Action<string, List<TradePositionItem>>? OnPositionsByMarket;
        public event Action<string, List<TradePositionDetailItem>>? OnPositionDetailsByMarket;
        public event Action<List<Financial_TradernItem>>? OnFinancial_Tradern;
        public event Action<string, List<Financial_TradernItem>>? OnFinancialByMarket;
        public event Action<List<TradeHistoryItem>>? OnTradeHistory;

        public event Action<OrderEvent>? OrderEventReceived;
        public event Action<FillEvent>? FillEventReceived;
        public event Action<RecoveryEvent>? RecoveryEventReceived;

        public List<TradePositionItem> CurrentPositions { get; private set; } = new List<TradePositionItem>();
        public List<TradePositionDetailItem> CurrentPositionsDetail { get; private set; } = new List<TradePositionDetailItem>();
        public List<TradePositionItem> CurrentForeignPositions { get; private set; } = new List<TradePositionItem>();
        public List<TradePositionDetailItem> CurrentForeignPositionsDetail { get; private set; } = new List<TradePositionDetailItem>();
        public List<Financial_TradernItem> CurrentFinancialTradern { get; private set; } = new List<Financial_TradernItem>();
        private readonly ConcurrentQueue<string> _pendingPositionMarkets = new();
        private readonly ConcurrentQueue<string> _pendingPositionDetailMarkets = new();
        private readonly ConcurrentQueue<string> _pendingMarginMarkets = new();
        public List<TradeHistoryItem> CurrentTradeHistory { get; private set; } = new List<TradeHistoryItem>();

        public bool IsLoggedIn { get; private set; } = false;

        public KGITradeService(string host, ushort port, string userId, string password, string brokerID, string account)
        {
            _host = host;
            _port = port;
            _userId = userId;
            _password = password;
            _borkerID = brokerID;
            _account = account;
        }

        public void Start()
        {
            try
            {
                OnLogMessage?.Invoke($"[TradeCom] 正在初始化連線至 {_host}:{_port}...");
                _tfcom = new TaiFexCom(_host, _port, "API", Language.Chinese);
                _tfcom.OnRcvMessage += OnRcvMessage;
                _tfcom.OnGetStatus += OnGetStatus;
                _tfcom.OnRecoverStatus += OnRecoverStatus;

                _tfcom.AutoSubReport = true;
                _tfcom.AutoRecoverReport = true;

                OnLogMessage?.Invoke($"[TradeCom] 發送 LoginDirect 帳號: {_userId}");
                _tfcom.LoginDirect(_host, _port, $"{_userId},,{_password}");
            }
            catch (Exception ex)
            {
                OnLogMessage?.Invoke($"[TradeCom] 初始化失敗: {ex.Message}");
                OnStatusChanged?.Invoke("連線失敗", false);
            }
        }

     
        public void Stop()
        {
            try
            {
                if (_tfcom != null)
                {
                    try { _tfcom.Logout(); } catch { }
                    try { _tfcom.Dispose(); } catch { }
                    try { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(_tfcom); } catch { }
                    _tfcom = null;
                }
                IsLoggedIn = false;
                OnStatusChanged?.Invoke("已離線", false);
            }
            catch { }
        }

        #region IOrderGateway 實作

        public OrderResult NewOrder(OrderRequest request)
        {
            if (_tfcom == null || !IsLoggedIn)
            {
                string msg = "[TradeCom] 新單委託失敗: 尚未登入交易伺服器";
                log.Error(msg);
                OnLogMessage?.Invoke(msg);
                return new OrderResult { IsSuccess = false, ErrorMessage = "Not logged in" };
            }

            try
            {
                long requestId = _tfcom.GetRequestId();
                string brokerId = !string.IsNullOrEmpty(request.BrokerId) ? request.BrokerId : _borkerID;
                string account = !string.IsNullOrEmpty(request.Account) ? request.Account : _account;
                string symbol = request.Instrument != null ? (string.IsNullOrEmpty(request.Instrument.Symbol) ? request.Instrument.ToString() : request.Instrument.Symbol) : "";

                MARKET_FLAG mf = (request.Instrument?.Market == MarketType.DomesticOption || symbol.Length > 10) 
                    ? MARKET_FLAG.MF_OPT 
                    : MARKET_FLAG.MF_FUT;

                SIDE_FLAG sideFlag = request.BuySell == BuySell.Buy ? SIDE_FLAG.SF_BUY : SIDE_FLAG.SF_SELL;
                PRICE_FLAG priceFlag = request.Price > 0 ? PRICE_FLAG.PF_SPECIFIED : PRICE_FLAG.PF_MARKET;

                TIME_IN_FORCE tif = request.TimeInForce switch
                {
                    TimeInForce.IOC => TIME_IN_FORCE.TIF_IOC,
                    TimeInForce.FOK => TIME_IN_FORCE.TIF_FOK,
                    _ => TIME_IN_FORCE.TIF_ROD
                };

                POSITION_EFFECT pe = request.PositionEffect switch
                {
                    PositionEffect.Open => POSITION_EFFECT.PE_OPEN,
                    PositionEffect.Close => POSITION_EFFECT.PE_CLOSE,
                    PositionEffect.DayTrade => POSITION_EFFECT.PE_DAY_TRADE,
                    _ => POSITION_EFFECT.PE_AUTO
                };

                decimal decPrice = (decimal)request.Price;
                ushort uQty = (ushort)Math.Max(1, request.Quantity);

                log.Info($"[TradeCom] 送出新委託: ReqId={requestId}, Symbol={symbol}, Side={sideFlag}, Price={decPrice}, Qty={uQty}, TIF={tif}");
                OnLogMessage?.Invoke($"[TradeCom] 送出新委託: ReqId={requestId}, Symbol={symbol}, {(sideFlag == SIDE_FLAG.SF_BUY ? "買進" : "賣出")} {uQty}口 @ {decPrice}");

                long rtn = 0;
                bool isForeign = (request.Instrument?.Market == MarketType.ForeignFuture || request.Instrument?.Market == MarketType.ForeignOption);

                if (isForeign)
                {
                    string exchange = !string.IsNullOrEmpty(request.Instrument?.Exchange) ? request.Instrument.Exchange : "CME";
                    string sym = !string.IsNullOrEmpty(request.Instrument?.Symbol) ? request.Instrument.Symbol : symbol;
                    string comYm = request.Instrument?.ContractMonth ?? "";
                    MARKET_FLAG foreignMarketFlag = request.Instrument?.Market == MarketType.ForeignOption
                        ? MARKET_FLAG.MF_OPT
                        : MARKET_FLAG.MF_FUT;
                    char cp = ResolveNewForeignCp(request.Instrument, sym);
                    char dayTrade = request.PositionEffect == PositionEffect.DayTrade ? 'Y' : 'N';

                    log.Info($"[TradeCom] 送出國外期貨新委託: ReqId={requestId}, Exch={exchange}, Symbol={sym}, ComYM={comYm}, CP={cp}, DayTrade={dayTrade}, Side={sideFlag}, Price={decPrice}, Qty={uQty}");
                    OnLogMessage?.Invoke($"[TradeCom] 送出國外新單: ReqId={requestId}, 交易所={exchange}, 商品={sym} {comYm}, {(sideFlag == SIDE_FLAG.SF_BUY ? "買進" : "賣出")} {uQty}口 @ {decPrice}");

                    rtn = _tfcom.FOrder(
                        ORDER_KIND.OK_CONTROL,
                        ORDER_TYPE.OT_NEW,
                        requestId,
                        foreignMarketFlag,
                        brokerId,
                        account,
                        "",
                        exchange,
                        sym,
                        comYm,
                        0,
                        cp,
                        sideFlag,
                        priceFlag,
                        dayTrade,
                        pe,
                        tif,
                        decPrice,
                        0,
                        (int)uQty,
                        "",
                        "",
                        "",
                        "",
                        "",
                        "",
                        "",
                        "",
                        ""
                    );
                }
                else
                {
                    rtn = _tfcom.Order(
                        ORDER_TYPE.OT_NEW,
                        mf,
                        requestId,
                        brokerId,
                        account,
                        "",
                        symbol,
                        sideFlag,
                        priceFlag,
                        decPrice,
                        tif,
                        uQty,
                        pe,
                        OFFICE_FLAG.OF_SPEEDY
                    );
                }

                if (rtn == 0)
                {
                    return new OrderResult { IsSuccess = true, RequestId = requestId.ToString() };
                }
                else
                {
                    string errMsg = _tfcom.GetOrderErrMsg(rtn);
                    string msg = $"[TradeCom] 新單委託失敗，Error Code: {rtn}, 原因: {errMsg}";
                    log.Error(msg);
                    OnLogMessage?.Invoke(msg);
                    return new OrderResult { IsSuccess = false, ErrorMessage = errMsg, RequestId = requestId.ToString() };
                }
            }
            catch (Exception ex)
            {
                log.Error($"[TradeCom] NewOrder Exception: {ex.Message}", ex);
                OnLogMessage?.Invoke($"[TradeCom] 新單發生例外: {ex.Message}");
                return new OrderResult { IsSuccess = false, ErrorMessage = ex.Message };
            }
        }

        public OrderResult ModifyOrder(ModifyOrderRequest request)
        {
            if (_tfcom == null || !IsLoggedIn)
            {
                string msg = "[TradeCom] 改單失敗: 尚未登入交易伺服器";
                log.Error(msg);
                OnLogMessage?.Invoke(msg);
                return new OrderResult { IsSuccess = false, ErrorMessage = "Not logged in" };
            }

            try
            {
                long requestId = _tfcom.GetRequestId();
                string brokerId = !string.IsNullOrEmpty(request.BrokerId) ? request.BrokerId : _borkerID;
                string account = !string.IsNullOrEmpty(request.Account) ? request.Account : _account;
                string orderNo = request.Identity?.OrdNo ?? "";
                string cnt = !string.IsNullOrWhiteSpace(request.Identity?.OrgCnt)
                    ? request.Identity.OrgCnt
                    : request.Identity?.Cnt ?? "";
                string webId = !string.IsNullOrEmpty(request.WebId) ? request.WebId : (request.Identity?.WebId ?? "");

                string symbol = !string.IsNullOrEmpty(request.Symbol)
                    ? request.Symbol
                    : (request.Instrument != null ? (string.IsNullOrEmpty(request.Instrument.Symbol) ? request.Instrument.ToString() : request.Instrument.Symbol) : "");

                SIDE_FLAG sideFlag = request.BuySell == BuySell.Buy ? SIDE_FLAG.SF_BUY : SIDE_FLAG.SF_SELL;
                MARKET_FLAG mf = (request.Instrument?.Market == MarketType.DomesticOption || symbol.Length > 10) 
                    ? MARKET_FLAG.MF_OPT 
                    : MARKET_FLAG.MF_FUT;

                decimal decPrice = (decimal)request.NewPrice;
                ushort uQty = (ushort)Math.Max(0, request.NewQuantity);

                bool isForeign = (request.Instrument?.Market == MarketType.ForeignFuture || request.Instrument?.Market == MarketType.ForeignOption);
                long rtn = 0;

                if (isForeign)
                {
                    if (!TryValidateForeignAmend(request.ForeignContext, orderNo, cnt, webId, out string validationError))
                        return RejectLocalForeignRequest("改單", requestId, validationError);

                    string exchange = !string.IsNullOrEmpty(request.Instrument?.Exchange) ? request.Instrument.Exchange : "CME";
                    string sym = !string.IsNullOrEmpty(request.Instrument?.Symbol) ? request.Instrument.Symbol : symbol;
                    string comYm = request.Instrument?.ContractMonth ?? "";
                    ForeignOrderContext context = request.ForeignContext!;
                    MARKET_FLAG foreignMarketFlag = request.Instrument?.Market == MarketType.ForeignOption
                        ? MARKET_FLAG.MF_OPT
                        : MARKET_FLAG.MF_FUT;
                    char cp = ForeignOrderRules.ResolveCp(request.Instrument, context);
                    char dayTrade = ForeignOrderRules.ResolveDayTrade(context);
                    POSITION_EFFECT positionEffect = ResolveForeignPositionEffect(context.PositionEffect);
                    TIME_IN_FORCE timeInForce = ResolveForeignTimeInForce(context.TimeInForce);

                    log.Info($"[TradeCom] 送出國外期貨改單: ReqId={requestId}, Exch={exchange}, Symbol={sym}, CP={cp}, DayTrade={dayTrade}, OrdNo={orderNo}, CNT={cnt}, Price={decPrice}, Qty={uQty}");
                    OnLogMessage?.Invoke($"[TradeCom] 送出國外改單: ReqId={requestId}, 交易所={exchange}, 商品={sym}, 書號={orderNo}, 序號={cnt}, 新價={decPrice}, 新量={uQty}");

                    rtn = _tfcom.FOrder(
                        ORDER_KIND.OK_CONTROL,
                        ORDER_TYPE.OT_MODIFY,
                        requestId,
                        foreignMarketFlag,
                        brokerId,
                        account,
                        "",
                        exchange,
                        sym,
                        comYm,
                        0,
                        cp,
                        sideFlag,
                        PRICE_FLAG.PF_SPECIFIED,
                        dayTrade,
                        positionEffect,
                        timeInForce,
                        decPrice,
                        0,
                        (int)uQty,
                        "",
                        context.Fcm,
                        context.ClearingAccount,
                        context.Source,
                        cnt,
                        orderNo,
                        context.TradeDate,
                        webId,
                        context.KeyIn
                    );
                }
                else
                {
                    ORDER_TYPE ot = ORDER_TYPE.OT_MODIFY_PRICE;

                    log.Info($"[TradeCom] 送出改單: ReqId={requestId}, Symbol={symbol}, Side={sideFlag}, OrdNo={orderNo}, WebId={webId}, CNT={cnt}, Price={decPrice}, Qty={uQty}");
                    OnLogMessage?.Invoke($"[TradeCom] 送出改單請求: ReqId={requestId}, 商品={symbol}, 書號={orderNo}, 序號={cnt}, WebID={webId}, 新價={decPrice}, 新量={uQty}");

                    rtn = _tfcom.Order(
                        ot,
                        mf,
                        requestId,
                        brokerId,
                        account,
                        "",
                        symbol,
                        sideFlag,
                        PRICE_FLAG.PF_SPECIFIED,
                        decPrice,
                        TIME_IN_FORCE.TIF_ROD,
                        uQty,
                        POSITION_EFFECT.PE_AUTO,
                        OFFICE_FLAG.OF_SPEEDY,
                        webId,
                        cnt,
                        orderNo
                    );
                }

                if (rtn == 0)
                {
                    return new OrderResult { IsSuccess = true, RequestId = requestId.ToString() };
                }
                else
                {
                    string errMsg = _tfcom.GetOrderErrMsg(rtn);
                    string msg = $"[TradeCom] 改單失敗，Error Code: {rtn}, 原因: {errMsg}";
                    log.Error(msg);
                    OnLogMessage?.Invoke(msg);
                    return new OrderResult { IsSuccess = false, ErrorMessage = errMsg, RequestId = requestId.ToString() };
                }
            }
            catch (Exception ex)
            {
                log.Error($"[TradeCom] ModifyOrder Exception: {ex.Message}", ex);
                OnLogMessage?.Invoke($"[TradeCom] 改單發生例外: {ex.Message}");
                return new OrderResult { IsSuccess = false, ErrorMessage = ex.Message };
            }
        }

        public OrderResult CancelOrder(CancelOrderRequest request)
        {
            if (_tfcom == null || !IsLoggedIn)
            {
                string msg = "[TradeCom] 刪單失敗: 尚未登入交易伺服器";
                log.Error(msg);
                OnLogMessage?.Invoke(msg);
                return new OrderResult { IsSuccess = false, ErrorMessage = "Not logged in" };
            }

            try
            {
                long requestId = _tfcom.GetRequestId();
                string brokerId = !string.IsNullOrEmpty(request.BrokerId) ? request.BrokerId : _borkerID;
                string account = !string.IsNullOrEmpty(request.Account) ? request.Account : _account;
                string orderNo = request.Identity?.OrdNo ?? "";
                string cnt = !string.IsNullOrWhiteSpace(request.Identity?.OrgCnt)
                    ? request.Identity.OrgCnt
                    : request.Identity?.Cnt ?? "";
                string webId = !string.IsNullOrEmpty(request.WebId) ? request.WebId : (request.Identity?.WebId ?? "");
                ushort qty = request.Qty;
                string symbol = !string.IsNullOrEmpty(request.Symbol)
                    ? request.Symbol
                    : (request.Instrument != null ? (string.IsNullOrEmpty(request.Instrument.Symbol) ? request.Instrument.ToString() : request.Instrument.Symbol) : "");

                SIDE_FLAG sideFlag = request.BuySell == BuySell.Buy ? SIDE_FLAG.SF_BUY : SIDE_FLAG.SF_SELL;
                MARKET_FLAG mf = (request.Instrument?.Market == MarketType.DomesticOption || symbol.Length > 10) 
                    ? MARKET_FLAG.MF_OPT 
                    : MARKET_FLAG.MF_FUT;

                bool isForeign = (request.Instrument?.Market == MarketType.ForeignFuture || request.Instrument?.Market == MarketType.ForeignOption);
                long rtn = 0;

                if (isForeign)
                {
                    if (!TryValidateForeignAmend(request.ForeignContext, orderNo, cnt, webId, out string validationError))
                        return RejectLocalForeignRequest("刪單", requestId, validationError);

                    string exchange = !string.IsNullOrEmpty(request.Instrument?.Exchange) ? request.Instrument.Exchange : "CME";
                    string sym = !string.IsNullOrEmpty(request.Instrument?.Symbol) ? request.Instrument.Symbol : symbol;
                    string comYm = request.Instrument?.ContractMonth ?? "";
                    ForeignOrderContext context = request.ForeignContext!;
                    MARKET_FLAG foreignMarketFlag = request.Instrument?.Market == MarketType.ForeignOption
                        ? MARKET_FLAG.MF_OPT
                        : MARKET_FLAG.MF_FUT;
                    char cp = ForeignOrderRules.ResolveCp(request.Instrument, context);
                    char dayTrade = ForeignOrderRules.ResolveDayTrade(context);
                    POSITION_EFFECT positionEffect = ResolveForeignPositionEffect(context.PositionEffect);
                    TIME_IN_FORCE timeInForce = ResolveForeignTimeInForce(context.TimeInForce);

                    log.Info($"[TradeCom] 送出國外期貨刪單: ReqId={requestId}, Exch={exchange}, Symbol={sym}, CP={cp}, DayTrade={dayTrade}, OrdNo={orderNo}, CNT={cnt}");
                    OnLogMessage?.Invoke($"[TradeCom] 送出國外刪單: ReqId={requestId}, 交易所={exchange}, 商品={sym}, 書號={orderNo}, 序號={cnt}");

                    rtn = _tfcom.FOrder(
                        ORDER_KIND.OK_CONTROL,
                        ORDER_TYPE.OT_CANCEL,
                        requestId,
                        foreignMarketFlag,
                        brokerId,
                        account,
                        "",
                        exchange,
                        sym,
                        comYm,
                        0,
                        cp,
                        sideFlag,
                        PRICE_FLAG.PF_SPECIFIED,
                        dayTrade,
                        positionEffect,
                        timeInForce,
                        0,
                        0,
                        (int)qty,
                        "",
                        context.Fcm,
                        context.ClearingAccount,
                        context.Source,
                        cnt,
                        orderNo,
                        context.TradeDate,
                        webId,
                        context.KeyIn
                    );
                }
                else
                {
                    log.Info($"[TradeCom] 送出刪單: ReqId={requestId}, mf:{mf},Symbol={symbol}, Side={sideFlag}, OrdNo={orderNo}, WebId={webId}, CNT={cnt}");
                    OnLogMessage?.Invoke($"[TradeCom] 送出刪單請求: ReqId={requestId}, 市場:{mf},商品={symbol}, 書號={orderNo}, 序號={cnt}, WebID={webId}");
                    Debug.WriteLine($"[TradeCom] 送出刪單: ReqId={requestId}, mf:{mf},Symbol={symbol}, Side={sideFlag}, OrdNo={orderNo}, WebId={webId}, CNT={cnt}");
                    rtn = _tfcom.Order(
                        ORDER_TYPE.OT_CANCEL,
                        mf,
                        requestId,
                        brokerId,
                        account,
                        "",
                        symbol,
                        sideFlag,
                        PRICE_FLAG.PF_SPECIFIED,
                        0,
                        TIME_IN_FORCE.TIF_ROD,
                        qty,
                        POSITION_EFFECT.PE_AUTO,
                        OFFICE_FLAG.OF_SPEEDY,
                        webId,
                        cnt,
                        orderNo
                    );
                }

                if (rtn == 0)
                {
                    return new OrderResult { IsSuccess = true, RequestId = requestId.ToString() };
                }
                else
                {
                    string errMsg = _tfcom.GetOrderErrMsg(rtn);
                    string msg = $"[TradeCom] 刪單失敗，Error Code: {rtn}, 原因: {errMsg}";
                    log.Error(msg);
                    OnLogMessage?.Invoke(msg);
                    return new OrderResult { IsSuccess = false, ErrorMessage = errMsg, RequestId = requestId.ToString() };
                }
            }
            catch (Exception ex)
            {
                log.Error($"[TradeCom] CancelOrder Exception: {ex.Message}", ex);
                OnLogMessage?.Invoke($"[TradeCom] 刪單發生例外: {ex.Message}");
                return new OrderResult { IsSuccess = false, ErrorMessage = ex.Message };
            }
        }

        private static bool TryValidateForeignAmend(
            ForeignOrderContext? context,
            string orderNo,
            string orgCnt,
            string webId,
            out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(context?.Fcm))
                error = "上手券商不可為空";
            else if (string.IsNullOrWhiteSpace(context.ClearingAccount))
                error = "上手帳號不可為空";
            else if (string.IsNullOrWhiteSpace(orderNo) || orderNo.Trim() == "0")
                error = "上手委託書號不可為空或 0";
            else if (context.TradeDate.Length != 8 || !context.TradeDate.All(char.IsDigit))
                error = "交易日期必須為 8 碼數字";
            else if (string.IsNullOrWhiteSpace(orgCnt))
                error = "原始委託 OrgCnt 不可為空";
            else if (string.IsNullOrWhiteSpace(webId))
                error = "原始委託 WEBID 不可為空";

            return error.Length == 0;
        }

        private OrderResult RejectLocalForeignRequest(string operation, long requestId, string error)
        {
            string message = $"[TradeCom] 國外{operation}送出前驗證失敗: {error}";
            log.Warn(message);
            OnLogMessage?.Invoke(message);
            return new OrderResult
            {
                IsSuccess = false,
                RequestId = requestId.ToString(),
                ErrorMessage = error
            };
        }

        private static char ResolveNewForeignCp(InstrumentKey? instrument, string symbol)
        {
            if (instrument?.Market == MarketType.ForeignFuture)
                return 'F';

            ContractInfo contract = SymbolRepository.Instance.GetContract(symbol);
            char cp = string.IsNullOrWhiteSpace(contract.CP)
                ? '\0'
                : char.ToUpperInvariant(contract.CP[0]);
            if (cp is 'C' or 'P')
                return cp;
            if (contract.CPF == CPFEnum.Call)
                return 'C';
            if (contract.CPF == CPFEnum.Put)
                return 'P';
            return ' ';
        }

        private static POSITION_EFFECT ResolveForeignPositionEffect(char value) =>
            char.ToUpperInvariant(value) switch
            {
                'O' => POSITION_EFFECT.PE_OPEN,
                'C' => POSITION_EFFECT.PE_CLOSE,
                'D' => POSITION_EFFECT.PE_DAY_TRADE,
                _ => POSITION_EFFECT.PE_AUTO
            };

        private static TIME_IN_FORCE ResolveForeignTimeInForce(char value) =>
            char.ToUpperInvariant(value) switch
            {
                'I' => TIME_IN_FORCE.TIF_IOC,
                'F' => TIME_IN_FORCE.TIF_FOK,
                _ => TIME_IN_FORCE.TIF_ROD
            };

        #endregion

        #region 帳務與歷史查詢

        public bool RequestPositions(string market = "I", string trader = "", string group = "")
        {
            if (_tfcom == null || !IsLoggedIn)
                return false;

            string normalizedMarket = NormalizeAccountMarket(market);
            string marketName = normalizedMarket == "O" ? "國外" : "國內";
            log.Info($"[TradeCom] 請求{marketName}彙總部位查詢 RetrivePositionSum...");
            OnLogMessage?.Invoke($"[TradeCom] 請求{marketName}彙總部位查詢 RetrivePositionSum...");
            long rtn = _tfcom.RetrivePositionSum(normalizedMarket, _borkerID, _account, trader, group);
            if (rtn < 0)
            {
                string msg = $"[TradeCom] 請求{marketName}彙總部位失敗，Error Code: {rtn}, 原因: {_tfcom.GetOrderErrMsg(rtn)}";
                log.Error(msg);
                OnLogMessage?.Invoke(msg);
                return false;
            }

            _pendingPositionMarkets.Enqueue(normalizedMarket);
            return true;
        }

        public bool RequestPositionDetail(string market = "I", string trader = "", string group = "")
        {
            if (_tfcom == null || !IsLoggedIn) return false;

            string normalizedMarket = NormalizeAccountMarket(market);
            string marketName = normalizedMarket == "O" ? "國外" : "國內";
            log.Info($"[TradeCom] 請求{marketName}部位明細 RetrivePositionDetail...");
            OnLogMessage?.Invoke($"[TradeCom] 請求{marketName}部位明細 RetrivePositionDetail...");
            long rtn = _tfcom.RetrivePositionDetail(normalizedMarket, _borkerID, _account, trader, group);
            if (rtn < 0)
            {
                string msg = $"[TradeCom] 請求{marketName}部位明細失敗，Error Code: {rtn}, 原因: {_tfcom.GetOrderErrMsg(rtn)}";
                log.Error(msg);
                OnLogMessage?.Invoke(msg);
                return false;
            }

            _pendingPositionDetailMarkets.Enqueue(normalizedMarket);
            return true;
        }

        private static string NormalizeAccountMarket(string market) =>
            string.Equals(market, "O", StringComparison.OrdinalIgnoreCase) ? "O" : "I";

        public bool RequestMargin(string market = "I", string trader = "", string group = "")
        {
            if (_tfcom == null || !IsLoggedIn) return false;

            string normalizedMarket = string.Equals(market, "O", StringComparison.OrdinalIgnoreCase) ? "O" : "I";
            string marketName = normalizedMarket == "O" ? "國外" : "國內";
            log.Info($"[TradeCom] 請求{marketName}權益數查詢 RetriveFMargin...");
            OnLogMessage?.Invoke($"[TradeCom] 請求{marketName}權益數查詢 RetriveFMargin...");
            long rtn = _tfcom.RetriveFMargin(normalizedMarket, _borkerID, _account, trader, group);
            if (rtn < 0)
            {
                string msg = $"[TradeCom] 請求{marketName}權益數查詢失敗，Error Code: {rtn}, 原因: {_tfcom.GetOrderErrMsg(rtn)}";
                log.Error(msg);
                OnLogMessage?.Invoke(msg);
                return false;
            }

            // 1626 回報沒有市場欄位。MarginEquityForm 會依序送出 I、O，
            // 此佇列用來把每一批回報還原成正確市場。
            _pendingMarginMarkets.Enqueue(normalizedMarket);
            return true;
        }

        public void ResetMarginRequestTracking()
        {
            while (_pendingMarginMarkets.TryDequeue(out _)) { }
        }

        public void RequestCoverHistory(string market, string bDate, string eDate, string trader = "", string group = "")
        {
            if (_tfcom == null || !IsLoggedIn) return;

            log.Info($"[TradeCom] 請求歷史平倉明細查詢 RequestCoverHistory ({bDate} ~ {eDate})...");
            OnLogMessage?.Invoke($"[TradeCom] 請求歷史平倉明細查詢 RequestCoverHistory ({bDate} ~ {eDate})...");
            long rtn = _tfcom.RetriveCoverDHistory(market, _borkerID, _account, trader, group, bDate, eDate);
            if (rtn < 0)
            {
                string msg = $"[TradeCom] 請求歷史平倉明細查詢失敗，Error Code: {rtn}, 原因: {_tfcom.GetOrderErrMsg(rtn)}";
                log.Error(msg);
                OnLogMessage?.Invoke(msg);
            }
        }

        public void RequestTradeHistory(string sDate, string eDate, string market = "I", string trader = "", string group = "")
        {
            if (_tfcom != null && IsLoggedIn)
            {
                log.Info($"[TradeCom] 請求平倉歷史查詢 RetriveCoverDHistory ({sDate} ~ {eDate})...");
                OnLogMessage?.Invoke($"[TradeCom] 請求平倉歷史查詢 RetriveCoverDHistory ({sDate} ~ {eDate})...");
                _tfcom.RetriveCoverDHistory(market, _borkerID, _account, trader, group, sDate, eDate);
            }
        }

        #endregion

        #region 連線與封包回報處理

        private void OnGetStatus(object sender, COM_STATUS status, byte[] msg)
        {
            string msgText = Encoding.UTF8.GetString(msg);
            switch (status)
            {
                case COM_STATUS.LOGIN_READY:
                    IsLoggedIn = true;
                    log.Info("[TradeCom] 登入成功！");
                    OnLogMessage?.Invoke("[TradeCom] 登入成功！");
                    OnStatusChanged?.Invoke("已登入", true);
                    RequestPositions("I");
                    break;

                case COM_STATUS.LOGIN_FAIL:
                    IsLoggedIn = false;
                    OnLogMessage?.Invoke($"[TradeCom] 登入失敗: {msgText}");
                    OnStatusChanged?.Invoke("登入失敗", false);
                    break;

                case COM_STATUS.CONNECT_READY:
                    OnLogMessage?.Invoke($"[TradeCom] 伺服器連線成功: {msgText}");
                    OnStatusChanged?.Invoke("連線中", false);
                    break;

                case COM_STATUS.CONNECT_FAIL:
                case COM_STATUS.DISCONNECTED:
                    IsLoggedIn = false;
                    OnLogMessage?.Invoke($"[TradeCom] 連線中斷: {msgText}");
                    OnStatusChanged?.Invoke("離線", false);
                    break;

                case COM_STATUS.ACK_REQUESTID:
                    bool isSuccess = (msg.Length > 8 && msg[8] == 1);
                    string reqId = msgText.Split(',')[0];
                    var orderEvent = new OrderEvent
                    {
                        Identity = new ExternalOrderIdentity { RequestId = reqId },
                        EventType = isSuccess ? OrderEventType.Ack : OrderEventType.Rejected,
                        Message = msgText
                    };
                    //OrderEventReceived?.Invoke(orderEvent);

                    break;
            }
        }

        private void OnRcvMessage(object sender, PackageBase package)
        {
            if (package == null || _tfcom == null) return;

            Debug.WriteLine((DT)package.DT);

            switch ((DT)package.DT)
            {
                case DT.AUTH_EXCHANGE:
                    P001506 p1506 = (P001506)package;
                    Debug.Write(p1506.ToLog());
                    break;

                case DT.LOGIN:
                    P001503 p1503 = (P001503)package;
                    if (p1503.Code != 0)
                    {
                        log.Error("登入失敗 CODE = " + p1503.Code + " " + _tfcom.GetMessageMap(p1503.Code));
                        OnLogMessage?.Invoke("登入失敗 CODE = " + p1503.Code + " " + _tfcom.GetMessageMap(p1503.Code));
                    }
                    else
                    {
                        OnLogMessage?.Invoke("登入成功 ");
                        OnLogMessage?.Invoke(p1503.ToLog());
                    }
                    break;

                case DT.INVENTORY_DETAIL_TRADER:
                    Decoder_Inventory_Detail_Trader(package);
                    break;

                case DT.INVENTORY_TRADER:
                    Decoder_Inventory_Trader(package);
                    break;

                case DT.FINANCIAL_TRADERN:
                    Decoder_Financial_Tradern(package);
                    break;

                case DT.FINANCIAL_COVERDH:
                    Decoder_Financial_CoverDH(package);
                    break;

                // 國內回報
                case DT.FUT_ORDER_ACK:
                    Decoder_2002(package);
                    break;

                case (DT)2006:
                    Decoder_2006(package);
                    break;

                case DT.FUT_ORDER_RPT:
                    Decoder_2010(package);
                    break;

                case DT.FUT_DEAL_RPT:
                    Decoder_2011(package);
                    break;

                // 國外回報
                case (DT)3302:
                    Decoder_3302(package);
                    break;

                case (DT)3310:
                    Decoder_3310(package);
                    break;

                case (DT)3311:
                    Decoder_3311(package);
                    break;
            }
        }

        private void OnRecoverStatus(object sender, string Topic, RECOVER_STATUS status, uint RecoverCount)
        {
            TaiFexCom com = (TaiFexCom)sender;
            switch (status)
            {
                case RECOVER_STATUS.RS_DONE:        //回補資料結束
                    if (RecoverCount == 0)
                        OnLogMessage?.Invoke(String.Format("結束回補 Topic:[{0}]", Topic));
                    else 
                        OnLogMessage?.Invoke(String.Format("結束回補 Topic:[{0} 筆數:{1}]", Topic, RecoverCount));
                    break;
                case RECOVER_STATUS.RS_BEGIN:       //開始回補資料
                    OnLogMessage?.Invoke(String.Format("開始回補 Topic:[{0}]", Topic));
                    break;
            }
        }


        private void Decoder_Inventory_Detail_Trader(PackageBase package)
        {
            P001618 p1618 = (P001618)package;
            _pendingPositionDetailMarkets.TryDequeue(out string? requestedMarket);
            string market = requestedMarket ?? "I";
            string marketName = market == "O" ? "國外" : "國內";
            OnLogMessage?.Invoke($"[TradeCom] 收到 1618 {marketName}部位明細，筆數: {p1618.Rows}");
            log.Info($"[TradeCom] 收到 1618 {marketName}部位明細，筆數: {p1618.Rows}");
            var newDetails = new List<TradePositionDetailItem>();
            if (p1618.Rows > 0 && p1618.p001618_2 != null)
            {
                foreach (P001618_2 p in p1618.p001618_2)
                {
                    double.TryParse(p.StrikePrice, out double strikePrice);
                    int.TryParse(p.Qty, out int qty);
                    double.TryParse(p.MPrice, out double mPrice);
                    double.TryParse(p.PRTLOS, out double prtlos);
                    double.TryParse(p.InitialMargin, out double initialMargin);
                    double.TryParse(p.MTMargin, out double mtMargin);
                    double.TryParse(p.DealPrice, out double dealPrice);

                    newDetails.Add(new TradePositionDetailItem
                    {
                        TradeDate = p.TradeDate ?? "",
                        OrdNo = p.OrdNo ?? "",
                        MarketNo = p.MarketNo ?? "",
                        Exchange = p.Exchange ?? "",
                        ComID = p.ComID ?? "",
                        ComYM = p.ComYM ?? "",
                        StrikePrice = strikePrice,
                        CP = p.CP.ToString(),
                        BS = p.BS.ToString(),
                        Qty = qty,
                        DealPrice = dealPrice,
                        MPrice = mPrice,
                        PRTLOS = prtlos,
                        InitialMargin = initialMargin,
                        MTMargin = mtMargin,
                        Currency = p.Currency ?? "",
                        SpKey = p.spKey,
                        BrokerId = p.BrokerId ?? "",
                        Account = p.Account ?? "",
                        Trader = p.Trader ?? ""
                    });
                }
            }

            if (market == "O")
                CurrentForeignPositionsDetail = newDetails;
            else
                CurrentPositionsDetail = newDetails;
            OnPositionDetailsByMarket?.Invoke(market, newDetails);
            if (market != "O") OnPositionsDetailUpdated?.Invoke(CurrentPositionsDetail);
        }

        private void Decoder_Inventory_Trader(PackageBase package)
        {
            P001616 p1616 = (P001616)package;
            _pendingPositionMarkets.TryDequeue(out string? requestedMarket);
            string market = requestedMarket ?? "I";
            string marketName = market == "O" ? "國外" : "國內";
            OnLogMessage?.Invoke($"[TradeCom] 收到 1616 {marketName}彙總部位，筆數: {p1616.Rows}");
            log.Info($"[TradeCom] 收到 1616 {marketName}彙總部位，筆數: {p1616.Rows}");
            var newPositions = new List<TradePositionItem>();
            if (p1616.Rows > 0 && p1616.p001616_2 != null)
            {
                foreach (P001616_2 p in p1616.p001616_2)
                {
                    int.TryParse(p.OTQty, out int qty);
                    double.TryParse(p.StrikePrice, out double strike);
                    double.TryParse(p.TrdPrice, out double trdPrice);
                    double.TryParse(p.MPrice, out double mPrice);
                    double.TryParse(p.PRTLOS, out double unrealizedPnL);

                    newPositions.Add(new TradePositionItem
                    {
                        BrokerId = p.BrokerId ?? "",
                        Account = p.Account ?? "",
                        Group = p.Group ?? "",
                        Trader = p.Trader ?? "",
                        Exchange = p.Exchange ?? "",
                        ComType = p.ComType ?? "",
                        Commodity = p.ComID ?? "",
                        SettleMonth = p.ComYM ?? "",
                        StrikePrice = strike,
                        CallPut = p.CP ?? "",
                        BuySell = p.BS ?? "",
                        OpenInterest = qty,
                        AveragePrice = trdPrice,
                        MarketPrice = mPrice,
                        UnrealizedPnL = unrealizedPnL,
                        Currency = p.Currency ?? ""
                    });
                }
            }

            if (market == "O")
                CurrentForeignPositions = newPositions;
            else
                CurrentPositions = newPositions;
            OnPositionsByMarket?.Invoke(market, newPositions);
            if (market != "O") OnPositionsUpdated?.Invoke(CurrentPositions);
        }

        private void Decoder_Financial_Tradern(PackageBase package)
        {
            P001626 p1626 = (P001626)package;
            _pendingMarginMarkets.TryDequeue(out string? requestedMarket);
            string market = requestedMarket ?? string.Empty;
            string marketName = market == "O" ? "國外" : market == "I" ? "國內" : "未識別市場";
            OnLogMessage?.Invoke($"[TradeCom] 收到 1626 {marketName}權益回報，筆數: {p1626.Count}");
            log.Info($"[TradeCom] 收到 1626 {marketName}權益回報，筆數: {p1626.Count}");

            var items = new List<Financial_TradernItem>();
            if (p1626.Count > 0 && p1626.p001626_2 != null)
            {
                foreach (P001626_2 p in p1626.p001626_2)
                {
                    double.TryParse(p.LCTDAB, out double lctdab);
                    double.TryParse(p.ORIGNFEE, out double orignfee);
                    double.TryParse(p.TAXAMT, out double taxamt);
                    double.TryParse(p.CTAXAMT, out double ctaxamt);
                    double.TryParse(p.DWAMT, out double dwamt);
                    double.TryParse(p.OSPRTLOS, out double osprtlos);
                    double.TryParse(p.PRTLOS, out double prtlos);
                    double.TryParse(p.BMKTVAL, out double bmktval);
                    double.TryParse(p.SMKTVAL, out double smktval);
                    double.TryParse(p.OPREMIUM, out double opremium);
                    double.TryParse(p.TPREMIUM, out double tpremium);
                    double.TryParse(p.EQUITY, out double equity);
                    double.TryParse(p.IAMT, out double iamt);
                    double.TryParse(p.MAMT, out double mamt);
                    double.TryParse(p.EXCESS, out double excess);
                    double.TryParse(p.ORDEXCESS, out double ordexcess);
                    double.TryParse(p.ORDAMT, out double ordamt);
                    double.TryParse(p.ExProfit, out double exProfit);
                    double.TryParse(p.Premium, out double premium);
                    double.TryParse(p.FloatProfit, out double floatProfit);
                    double.TryParse(p.LASSPRTLOS, out double lassprtlos);
                    double.TryParse(p.CLOSEAMT, out double closeamt);
                    double.TryParse(p.ORDIAMT, out double ordiamt);
                    double.TryParse(p.ORDMAMT, out double ordmamt);
                    double.TryParse(p.DayTradeAMT, out double dayTradeAMT);
                    double.TryParse(p.ReductionAMT, out double reductionAMT);
                    double.TryParse(p.CreditAMT, out double creditAMT);
                    double.TryParse(p.balance, out double balance);
                    double.TryParse(p.IPremium, out double iPremium);
                    double.TryParse(p.OPremium, out double oPremium);
                    double.TryParse(p.Securities, out double securities);
                    double.TryParse(p.SecuritiesOffset, out double securitiesOffset);
                    double.TryParse(p.OffsetAMT, out double offsetAMT);
                    double.TryParse(p.Offset, out double offset);
                    double.TryParse(p.FULLMTRISK, out double fullmtrisk);
                    double.TryParse(p.FULLRISK, out double fullrisk);
                    double.TryParse(p.MarginCall, out double marginCall);
                    double.TryParse(p.SellVerticalSpread, out double sellVerticalSpread);
                    double.TryParse(p.StrikePrice, out double strikePrice);
                    double.TryParse(p.ActMarketValue, out double actMarketValue);
                    double.TryParse(p.TPRTLOS, out double tprtlos);
                    double.TryParse(p.MarginCall1, out double marginCall1);
                    double.TryParse(p.AddMargin, out double addMargin);
                    double.TryParse(p.ORDAMTNOCN, out double ordamtnocn);
                    double.TryParse(p.WithdrawMnt, out double withdrawMnt);
                    double.TryParse(p.OPREMIUM1, out double opremium1);
                    double.TryParse(p.OPREMIUM2, out double opremium2);

                    items.Add(new Financial_TradernItem
                    {
                        BrokerId = p.BrokerId ?? "",
                        Account = p.Account ?? "",
                        Group = p.Group ?? "",
                        Trader = p.Trader ?? "",
                        Currency = p.Currency ?? "",
                        LCTDAB = lctdab,
                        ORIGNFEE = orignfee,
                        TAXAMT = taxamt,
                        CTAXAMT = ctaxamt,
                        DWAMT = dwamt,
                        OSPRTLOS = osprtlos,
                        PRTLOS = prtlos,
                        BMKTVAL = bmktval,
                        SMKTVAL = smktval,
                        OPREMIUM = opremium,
                        TPREMIUM = tpremium,
                        EQUITY = equity,
                        IAMT = iamt,
                        MAMT = mamt,
                        EXCESS = excess,
                        ORDEXCESS = ordexcess,
                        ORDAMT = ordamt,
                        ExProfit = exProfit,
                        Premium = premium,
                        PTime = p.PTime ?? "",
                        FloatProfit = floatProfit,
                        LASSPRTLOS = lassprtlos,
                        CLOSEAMT = closeamt,
                        ORDIAMT = ordiamt,
                        ORDMAMT = ordmamt,
                        DayTradeAMT = dayTradeAMT,
                        ReductionAMT = reductionAMT,
                        CreditAMT = creditAMT,
                        Balance = balance,
                        IPremium = iPremium,
                        OPremium = oPremium,
                        Securities = securities,
                        SecuritiesOffset = securitiesOffset,
                        OffsetAMT = offsetAMT,
                        Offset = offset,
                        FULLMTRISK = fullmtrisk,
                        FULLRISK = fullrisk,
                        MarginCall = marginCall,
                        SellVerticalSpread = sellVerticalSpread,
                        StrikePrice = strikePrice,
                        ActMarketValue = actMarketValue,
                        TPRTLOS = tprtlos,
                        MarginCall1 = marginCall1,
                        AddMargin = addMargin,
                        ORDAMTNOCN = ordamtnocn,
                        WithdrawMnt = withdrawMnt,
                        OPREMIUM1 = opremium1,
                        OPREMIUM2 = opremium2
                    });
                }
            }

            CurrentFinancialTradern = items;
            OnFinancialByMarket?.Invoke(market, items);
            OnFinancial_Tradern?.Invoke(CurrentFinancialTradern);
        }

        private void Decoder_Financial_CoverDH(PackageBase package)
        {
            P001645 p1645 = (P001645)package;
            OnLogMessage?.Invoke($"[TradeCom] 收到 1645 成交平倉歷史回報，筆數: {p1645.Rows}");
            log.Info($"[TradeCom] 收到 1645 成交平倉歷史回報，筆數: {p1645.Rows}");

            var items = new List<TradeHistoryItem>();
            if (p1645.Rows > 0 && p1645.Detail != null)
            {
                foreach (P001645_2 p in p1645.Detail)
                {
                    double.TryParse(p.StrikePrice, out double strikePrice);
                    int.TryParse(p.Qty1, out int qty1);
                    int.TryParse(p.Qty2, out int qty2);
                    double.TryParse(p.TrdPrice1, out double trdPrice1);
                    double.TryParse(p.TrdPrice2, out double trdPrice2);
                    double.TryParse(p.PRTLOS, out double prtlos);
                    double.TryParse(p.CTAXAMT, out double ctaxamt);
                    double.TryParse(p.ORIGNFEE, out double orignfee);
                    double.TryParse(p.Premium1, out double premium1);
                    double.TryParse(p.Premium2, out double premium2);
                    double.TryParse(p.CurrencyRate, out double currencyRate);

                    items.Add(new TradeHistoryItem
                    {
                        Market = p.Market ?? "",
                        BrokerId = p.BrokerId ?? "",
                        Account = p.Account ?? "",
                        Group = p.Group ?? "",
                        Trader = p.Trader ?? "",
                        RtnCode = p.RtnCode ?? "",
                        Exchange = p.Exchange ?? "",
                        OccDT = p.OccDT ?? "",
                        TrdDT1 = p.TrdDT1 ?? "",
                        OrdNo1 = p.OrdNo1 ?? "",
                        FirmOrd1 = p.FirmOrd1 ?? "",
                        OffsetSpliteSeqNo = p.OffsetSpliteSeqNo ?? "",
                        TrdDT2 = p.TrdDT2 ?? "",
                        OrdNo2 = p.OrdNo2 ?? "",
                        FirmOrd2 = p.FirmOrd2 ?? "",
                        OffsetSpliteSeqNo2 = p.OffsetSpliteSeqNo2 ?? "",
                        OffsetCode = p.OffsetCode.ToString(),
                        Offset = p.offset.ToString(),
                        BS = p.BS.ToString(),
                        ComID = p.ComID ?? "",
                        ComYM = p.ComYM ?? "",
                        StrikePrice = strikePrice,
                        CP = p.CP.ToString(),
                        ComID2 = p.ComID2 ?? "",
                        Qty1 = qty1,
                        Qty2 = qty2,
                        TrdPrice1 = trdPrice1,
                        TrdPrice2 = trdPrice2,
                        PRTLOS = prtlos,
                        AENO = p.AENO ?? "",
                        Currency = p.Currency ?? "",
                        CTAXAMT = ctaxamt,
                        ORIGNFEE = orignfee,
                        Premium1 = premium1,
                        Premium2 = premium2,
                        InNo1 = p.InNo1 ?? "",
                        InNo2 = p.InNo2 ?? "",
                        Cnt1 = p.Cnt1 ?? "",
                        Cnt2 = p.Cnt2 ?? "",
                        CurrencyRate = currencyRate
                    });
                }
            }

            CurrentTradeHistory = items;
            OnTradeHistory?.Invoke(CurrentTradeHistory);
        }

        #endregion

        #region 委託與成交回報解析 (20xx & 33xx)

        private void Decoder_2002(PackageBase package)
        {
            try
            {
                
                PT02002 p = (PT02002)package;
                Debug.WriteLine($"2002:{p.ToLog()}");
                string reqId = p.RequestId.ToString();
                string cnt = p.CNT?.ToString() ?? "";
                string errCode = p.ErrorCode.ToString();

                var orderEvent = new OrderEvent
                {
                    Identity = new ExternalOrderIdentity { RequestId = reqId, Cnt = cnt },
                    EventType = OrderEventType.Ack,
                    ErrorCode = errCode
                };
                OrderEventReceived?.Invoke(orderEvent);
            }
            catch (Exception ex)
            {
                log.Error($"[TradeCom] 解析 2002 失敗: {ex.Message}");
            }
        }

        private void Decoder_2006(PackageBase package)
        {
            try
            {
                dynamic p = package;
              
                
                string reqId = p.RequestId?.ToString() ?? "";
                string cnt = p.CNT?.ToString() ?? "";
                string errCode = p.ErrorCode?.ToString() ?? "";

                var orderEvent = new OrderEvent
                {
                    Identity = new ExternalOrderIdentity { RequestId = reqId, Cnt = cnt },
                    EventType = OrderEventType.Ack,
                    ErrorCode = errCode
                };
                // OrderEventReceived?.Invoke(orderEvent);
            }
            catch (Exception ex)
            {
                log.Error($"[TradeCom] 解析 2006 失敗: {ex.Message}");
            }
        }

        public static DateTime ParseKgiDateTime(string? dateStr, string? timeStr)
        {
            try
            {
                string date = string.IsNullOrWhiteSpace(dateStr) ? DateTime.Today.ToString("yyyyMMdd") : dateStr.Trim();
                string time = string.IsNullOrWhiteSpace(timeStr) ? DateTime.Now.ToString("HHmmssfff") : timeStr.Trim();

                date = new string(date.Where(char.IsDigit).ToArray());
                time = new string(time.Where(char.IsDigit).ToArray());

                if (date.Length == 7)
                {
                    if (int.TryParse(date.Substring(0, 3), out int rocYear))
                    {
                        date = (rocYear + 1911) + date.Substring(3);
                    }
                }
                else if (date.Length != 8)
                {
                    date = DateTime.Today.ToString("yyyyMMdd");
                }

                if (time.Length == 6)
                {
                    time += "000";
                }
                else if (time.Length == 7)
                {
                    time += "00";
                }
                else if (time.Length == 8)
                {
                    time += "0";
                }
                else if (time.Length > 9)
                {
                    time = time.Substring(0, 9);
                }
                else if (time.Length < 6)
                {
                    time = time.PadLeft(6, '0') + "000";
                }

                string full = date + time;
                if (DateTime.TryParseExact(full, "yyyyMMddHHmmssfff", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var dt))
                {
                    return dt;
                }

                if (DateTime.TryParseExact(date, "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d))
                {
                    return d;
                }
            }
            catch { }

            return DateTime.Now;
        }

        private void Decoder_2010(PackageBase package)
        {
            try
            {
                PT02010 p = (PT02010)package;
                Debug.WriteLine($"2010:{p.ToLog()}");
                string orderFunc = p.OrderFunc.ToString() ?? "";
                string cnt = p.CNT?.ToString() ?? "";
                string ordNo = p.OrderNo?.ToString() ?? "";
                string webId = p.WebID?.ToString() ?? "";
                string errCode = p.Code?.ToString() ?? "";
                string errMsg = p.ErrMsg?.ToString() ?? "";
                string reqID = p.RequestId?.ToString() ?? "";
                string symbol = p.Symbol?.ToString() ?? "";
                string brokerId = p.BrokerId?.ToString() ?? "";
                string account = p.Account?.ToString() ?? "";

                BuySell buySell = (p.Side == 'B' || p.Side == (char)SIDE_FLAG.SF_BUY || p.Side == '1') ? BuySell.Buy : BuySell.Sell;

                double price = 0;
                int afterQty = 0;
                int beforeQty = 0;

                double.TryParse(p.Price?.ToString(), out price);
                int.TryParse(p.AfterQty?.ToString(), out afterQty);
                int.TryParse(p.BeforeQty?.ToString(), out beforeQty);

                int orderQty = beforeQty > 0 ? beforeQty : afterQty;
                int leavesQty = afterQty;

                DateTime eventTime = ParseKgiDateTime(p.TradeDate, p.ReportTime);

                var orderEvent = new OrderEvent
                {
                    EventTime = eventTime,
                    Identity = new ExternalOrderIdentity { Cnt = cnt, OrdNo = ordNo, WebId = webId, RequestId = reqID },
                    Instrument = new InstrumentKey
                    {
                        Market = (symbol.Length >= 10 ? MarketType.DomesticOption : MarketType.DomesticFuture),
                        Symbol = symbol,
                        Exchange = "TAIFEX"
                    },
                    BuySell = buySell,
                    OrderPrice = price,
                    OrderQty = orderQty,
                    LeavesQty = leavesQty,
                    BrokerId = brokerId,
                    Account = account,
                    ErrorCode = errCode,
                    Message = errMsg,
                    UpdatedPrice = price,
                    UpdatedQty = afterQty
                };

                bool isError = (errCode != "0" && errCode != "000000" && errCode != "");

                switch (orderFunc)
                {
                    case "I":
                        orderEvent.OperationType = OrderOperationType.New;
                        orderEvent.EventType = isError ? OrderEventType.Rejected : OrderEventType.Accepted;
                        break;
                    case "C":
                    case "R":
                        orderEvent.OperationType = OrderOperationType.Modify;
                        orderEvent.EventType = isError ? OrderEventType.ModifyFailed : OrderEventType.Modified;
                        break;
                    case "D":
                        orderEvent.OperationType = OrderOperationType.Cancel;
                        orderEvent.EventType = isError ? OrderEventType.CancelFailed : OrderEventType.Cancelled;
                        break;
                    default:
                        orderEvent.OperationType = OrderOperationType.New;
                        orderEvent.EventType = isError ? OrderEventType.Rejected : OrderEventType.Accepted;
                        break;
                }

                OrderEventReceived?.Invoke(orderEvent);
            }
            catch (Exception ex)
            {
                log.Error($"[TradeCom] 解析 2010 失敗: {ex.Message}");
            }
        }

        private void Decoder_2011(PackageBase package)
        {
            try
            {
                PT02011 p = (PT02011)package;
                Debug.WriteLine("2011" + p.ToLog());
                string cnt = p.CNT?.ToString() ?? "";
                string ordNo = p.OrderNo?.ToString() ?? "";
                string marketNo = p.MarketNo?.ToString() ?? "";
                string exchange = "TAIFEX";
                string symbol = p.Symbol?.ToString() ?? "";
                BuySell buySell = (p.Side == 'B' || p.Side == (char)SIDE_FLAG.SF_BUY || p.Side == '1') ? BuySell.Buy : BuySell.Sell;

                double dealPrice = 0;
                int dealQty = 0;
                int cumQty = 0;
                int leavesQty = 0;

                double.TryParse(p.DealPrice?.ToString(), out dealPrice);
                int.TryParse(p.DealQty?.ToString(), out dealQty);
                int.TryParse(p.CumQty?.ToString(), out cumQty);
                int.TryParse(p.LeaveQty?.ToString() ?? p.LeaveQty?.ToString(), out leavesQty);

                DateTime tradeTime = ParseKgiDateTime(p.TradeDate, p.ReportTime);
                    
                var fillEvent = new FillEvent
                {
                    EventTime = tradeTime,
                    Fill = new FillInfo
                    {
                        ExecID = marketNo,
                        Cnt = cnt,
                        OrdNo = ordNo,
                        Exchange = exchange,
                        Symbol = symbol,
                        DealPrice = dealPrice,
                        DealQty = dealQty,
                        CumQty = cumQty,
                        LeavesQty = leavesQty,
                        TradeTime = tradeTime
                    },
                    BuySell = buySell
                };

                FillEventReceived?.Invoke(fillEvent);
            }
            catch (Exception ex)
            {
                log.Error($"[TradeCom] 解析 2011 失敗: {ex.Message}");
            }
        }

        private void Decoder_3302(PackageBase package)
        {
            try
            {
                PT03302 p = (PT03302)package;
                Debug.WriteLine("3302" + p.ToLog());
                string errCode = p.ErrCode.ToString();
                var orderEvent = new OrderEvent
                {
                    Identity = new ExternalOrderIdentity
                    {
                        RequestId = p.RequestId.ToString(),
                        LastRequestId = p.RequestId.ToString(),
                        Cnt = p.CNT ?? string.Empty,
                        LastOperationCnt = p.CNT ?? string.Empty,
                        WebId = p.WEBID ?? string.Empty
                    },
                    EventType = ForeignOrderRules.IsSuccessCode(errCode)
                        ? OrderEventType.Ack
                        : OrderEventType.Rejected,
                    ErrorCode = errCode,
                    Message = ForeignOrderRules.IsSuccessCode(errCode) ? string.Empty : $"3302 ErrorCode={errCode}"
                };
                OrderEventReceived?.Invoke(orderEvent);
            }
            catch (Exception ex)
            {
                log.Error($"[TradeCom] 解析 3302 失敗: {ex.Message}", ex);
            }
        }

        private void Decoder_3310(PackageBase package)
        {
            try
            {
                PT03310 p = (PT03310)package;
                Debug.WriteLine("3310" + p.ToLog());
                int.TryParse(p.BeforeQty, out int beforeQty);
                int.TryParse(p.AfterQty, out int afterQty);
                string errCode = p.ErrCode ?? string.Empty;
                bool success = ForeignOrderRules.IsSuccessCode(errCode);
                OrderOperationType operation = p.OrderFunc switch
                {
                    'M' => OrderOperationType.Modify,
                    'C' => OrderOperationType.Cancel,
                    _ => OrderOperationType.New
                };

                OrderEventType eventType = operation switch
                {
                    OrderOperationType.Modify => success ? OrderEventType.Modified : OrderEventType.ModifyFailed,
                    OrderOperationType.Cancel => success ? OrderEventType.Cancelled : OrderEventType.CancelFailed,
                    _ => success ? OrderEventType.Accepted : OrderEventType.Rejected
                };

                double price = ParseForeignReportPrice(p.Price);
                var orderEvent = new OrderEvent
                {
                    EventTime = ParseKgiDateTime(p.TradeDate, p.ReportTime),
                    Identity = new ExternalOrderIdentity
                    {
                        Cnt = p.CNT ?? string.Empty,
                        LastOperationCnt = p.CNT ?? string.Empty,
                        OrgCnt = p.OrgCnt ?? string.Empty,
                        OrdNo = p.ORDNO ?? string.Empty,
                        WebId = p.WEBID ?? string.Empty
                    },
                    ForeignContext = new ForeignOrderContext
                    {
                        Fcm = p.FCM ?? string.Empty,
                        ClearingAccount = p.FFUT_ACCOUNT ?? string.Empty,
                        TradeDate = p.TradeDate ?? string.Empty,
                        Source = p.SOURCE ?? string.Empty,
                        KeyIn = p.KeyIn ?? string.Empty,
                        Cp = p.CP,
                        DayTrade = p.DayTrade,
                        PositionEffect = p.PositionEffect,
                        TimeInForce = p.TimeInForce,
                        PriceFlag = p.PriceFlag
                    },
                    Instrument = new InstrumentKey
                    {
                        Market = p.CP == 'F' ? MarketType.ForeignFuture : MarketType.ForeignOption,
                        Symbol = p.Symbol ?? string.Empty,
                        ContractMonth = p.ComYM ?? string.Empty,
                        Exchange = p.Exchange ?? string.Empty
                    },
                    BuySell = p.BS is 'B' or '1' ? BuySell.Buy : BuySell.Sell,
                    OrderPrice = price,
                    OrderQty = Math.Max(beforeQty, afterQty),
                    LeavesQty = afterQty,
                    BrokerId = p.BrokerId ?? string.Empty,
                    Account = p.Account ?? string.Empty,
                    ErrorCode = errCode,
                    Message = p.ErrMsg ?? string.Empty,
                    UpdatedPrice = price,
                    UpdatedQty = afterQty,
                    OperationType = operation,
                    EventType = eventType
                };

                OrderEventReceived?.Invoke(orderEvent);
            }
            catch (Exception ex)
            {
                log.Error($"[TradeCom] 解析 3310 失敗: {ex.Message}", ex);
            }
        }

        private void Decoder_3311(PackageBase package)
        {
            try
            {
                PT03311 p = (PT03311)package;
                Debug.WriteLine("3311" + p.ToLog());
                int.TryParse(p.DealQty, out int dealQty);
                int.TryParse(p.CumQty, out int cumQty);
                int.TryParse(p.LeavesQty, out int leavesQty);
                DateTime tradeTime = ParseKgiDateTime(p.DealDate, p.ReportTime);

                FillEventReceived?.Invoke(new FillEvent
                {
                    EventTime = tradeTime,
                    Fill = new FillInfo
                    {
                        ExecID = p.PATSNo ?? string.Empty,
                        Cnt = p.CNT ?? string.Empty,
                        OrdNo = p.ORDNO ?? string.Empty,
                        Exchange = p.Exchange ?? string.Empty,
                        Symbol = p.Symbol ?? string.Empty,
                        DealPrice = ParseForeignReportPrice(p.DealPrice),
                        DealQty = dealQty,
                        CumQty = cumQty,
                        LeavesQty = leavesQty,
                        TradeTime = tradeTime
                    },
                    BuySell = p.BS is 'B' or '1' ? BuySell.Buy : BuySell.Sell
                });
            }
            catch (Exception ex)
            {
                log.Error($"[TradeCom] 解析 3311 失敗: {ex.Message}", ex);
            }
        }

        private static double ParseForeignReportPrice(string? value)
        {
            if (!decimal.TryParse(value, out decimal raw))
                return 0;

            // 3310/3311 價格為 13 碼、末 6 碼為固定小數，例如
            // 0065005000000 => 65005，TPXM 亦能保留 0.25。
            return (double)(raw / 1_000_000m);
        }

        #endregion
    }
}
