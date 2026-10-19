using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Intelligence;
using log4net;
using Package;

namespace KGIOptionPricing
{
    /// <summary>
    /// KGI 報價服務：負責連接 KGI 報價 API，下載國內外商品檔、訂閱選擇權及海期合約並即時組裝行情。
    /// </summary>
    public class KGIQuoteService : StrategyFramework.MarketData.IStrategyQuoteFeed
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(KGIQuoteService));
        private static readonly ConcurrentBag<object> _antiGcCrasher = new ConcurrentBag<object>();

        #region Event
        public event Action<string>? OnLogMessage;
        public event Action<string>? OnQuoteMessage;
        public event Action<PackageBase>? OnQuoteReceived;
        public event Action<string, MarketDataStruct>? OnMarketDataReceived;
        public event Action<string, string, MarketDataStruct>? OnResearchMarketEvent;
        public event Action<long>? OnConnectionGenerationChanged;
        public long ConnectionGeneration => Volatile.Read(ref _connectionGeneration);
        private void AdvanceConnectionGeneration()
        {
            long generation=Interlocked.Increment(ref _connectionGeneration);
            OnConnectionGenerationChanged?.Invoke(generation);
        }
        public event Action<string, RECOVER_STATUS, uint>? OnRecoverStatusChanged;
        public event Action<MarketDataStruct>? OnMarketData;
        public event Action<MarketDataStruct>? OnCloseInfo;
        public event Action<MarketDataStruct>? OnLastInfo;
        public event Action? OnSymbolRepositoryLoaded;
        public event Action<Dictionary<string, OptionChain>>? OnOptionsDataUpdated;
        #endregion

        #region Field
        private UTF8Encoding encoding = new UTF8Encoding();
        private QuoteCom? _quote;
        private int _connectionGeneration;
        private int _twInfoSubscriptionStarted;
        private int _domesticProductDownloadStarted;
        private int _foreignProductDownloadStarted;
        private int _symbolRepositoryBuildStarted;
        private int _optionPricingSubscriptionState;
        public string APIVersion => _quote?.version ?? "Unknown";
        public bool IsOptionPricingSubscriptionStarting => Volatile.Read(ref _optionPricingSubscriptionState) == 1;
        public bool IsOptionPricingSubscriptionStarted => Volatile.Read(ref _optionPricingSubscriptionState) == 2;

        // Settings 
        public string Host { get; set; } = "quoteapi.kgi.com.tw";
        public ushort Port { get; set; } = 443;
        public string SourceID { get; set; } = "API";
        public string Token { get; set; } = "";
        public string UserID { get; set; } = "";
        public string Password { get; set; } = "";
        public double RiskFreeRate { get; set; } = 0.02;
        public IReadOnlyList<string> SubscribeProductIDs => _subscribeProductIds;
        public TimeSpan JpxSubscriptionStartTime { get; private set; } = new(7, 20, 0);

        private Dictionary<string, int> RecoverMap { get; set; } = new Dictionary<string, int>();
        private ConcurrentDictionary<string, MarketDataStruct> h_MData { get; set; } = new ConcurrentDictionary<string, MarketDataStruct>();
        public IEnumerable<MarketDataStruct> GetAllMarketData() => h_MData.Values;
        public MarketDataStruct? GetMarketData(string symbol) => h_MData.TryGetValue(symbol, out var md) ? md : null;

        private Dictionary<string, OptionChain> _optionChains = new Dictionary<string, OptionChain>();
        public Dictionary<string, OptionChain> OptionChains => _optionChains;
        private ConcurrentDictionary<string, PI20080> _lastQuotes = new ConcurrentDictionary<string, PI20080>();
        private ConcurrentDictionary<string, double> _lastMatchPrices = new ConcurrentDictionary<string, double>();
        private readonly List<string> _subscribeProductIds = new();
        private readonly object _jpxSubscriptionLock = new();
        private readonly HashSet<string> _pendingJpxSubscriptions = new(StringComparer.OrdinalIgnoreCase);
        private DateTime? _jpxSubscriptionNotBefore;
        private System.Threading.Timer? _jpxSubscriptionTimer;

        private TradingCalendar _calendar = new TradingCalendar();
        #endregion

        #region Constructor
        public KGIQuoteService()
        {
            LoadConfig();
            InitializeJpxSubscriptionSchedule();
        }

        public KGIQuoteService(QuoteComAccount qutAcc)
        {
            UpdateConfig(qutAcc.ConnIP, qutAcc.ConnPort, qutAcc.UserID, qutAcc.Password, qutAcc.SourceID, qutAcc.Token);
            LoadConfig();
            InitializeJpxSubscriptionSchedule();
        }

        public void SetCalendar(TradingCalendar cal)
        {
            _calendar = cal;
        }

        public void UpdateConfig(string host, ushort port, string id, string pwd, string sourceId = "API", string token = "")
        {
            Host = host;
            Port = port;
            UserID = id;
            Password = pwd;
            SourceID = sourceId;
            Token = token;
        }
        #endregion

        #region Configuration
        private void LoadConfig()
        {
            try
            {
                string configPath = "config.json";
                if (File.Exists(configPath))
                {
                    string json = File.ReadAllText(configPath);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("KGI_USER_ID", out var idElem)) 
                        UserID = idElem.GetString() ?? UserID;
                    
                    if (root.TryGetProperty("KGI_PASSWORD", out var pwdElem)) 
                        Password = pwdElem.GetString() ?? Password;

                    // 1. 優先解析 QuoteServer 子物件
                    if (root.TryGetProperty("QuoteServer", out var qsElem) && qsElem.ValueKind == JsonValueKind.Object)
                    {
                        if (qsElem.TryGetProperty("Host", out var hElem) && !string.IsNullOrWhiteSpace(hElem.GetString()))
                            Host = hElem.GetString()!;

                        if (qsElem.TryGetProperty("Port", out var pElem))
                        {
                            if (pElem.ValueKind == JsonValueKind.Number && pElem.TryGetUInt16(out ushort pValNum))
                                Port = pValNum;
                            else if (pElem.ValueKind == JsonValueKind.String && ushort.TryParse(pElem.GetString(), out ushort pValStr))
                                Port = pValStr;
                        }

                        if (qsElem.TryGetProperty("SourceID", out var srcElem) && !string.IsNullOrWhiteSpace(srcElem.GetString()))
                            SourceID = srcElem.GetString()!;

                        if (qsElem.TryGetProperty("Token", out var tokElem) && !string.IsNullOrWhiteSpace(tokElem.GetString()))
                            Token = tokElem.GetString()!;
                    }
                    else
                    {
                        // 2. 備援解析根目錄單一屬性
                        if (root.TryGetProperty("QuoteHost", out var qhElem) && !string.IsNullOrWhiteSpace(qhElem.GetString()))
                            Host = qhElem.GetString()!;
                        else if (root.TryGetProperty("Host", out var hElem) && !string.IsNullOrWhiteSpace(hElem.GetString()))
                            Host = hElem.GetString()!;

                        if (root.TryGetProperty("QuotePort", out var qpElem))
                        {
                            if (qpElem.ValueKind == JsonValueKind.Number && qpElem.TryGetUInt16(out ushort qpNum))
                                Port = qpNum;
                            else if (qpElem.ValueKind == JsonValueKind.String && ushort.TryParse(qpElem.GetString(), out ushort qpStr))
                                Port = qpStr;
                        }
                        else if (root.TryGetProperty("Port", out var pElem))
                        {
                            if (pElem.ValueKind == JsonValueKind.Number && pElem.TryGetUInt16(out ushort pNum))
                                Port = pNum;
                            else if (pElem.ValueKind == JsonValueKind.String && ushort.TryParse(pElem.GetString(), out ushort pStr))
                                Port = pStr;
                        }

                        if (root.TryGetProperty("SourceID", out var srcElem) && !string.IsNullOrWhiteSpace(srcElem.GetString()))
                            SourceID = srcElem.GetString()!;

                        if (root.TryGetProperty("Token", out var tokElem) && !string.IsNullOrWhiteSpace(tokElem.GetString()))
                            Token = tokElem.GetString()!;
                    }

                    if (root.TryGetProperty("RISK_FREE_RATE", out var rfrElem) && rfrElem.TryGetDouble(out double rate))
                        RiskFreeRate = rate;

                    LoadJpxSubscriptionStartTime(root);

                    LoadSubscribeProductIds(root);
                    LoadHotMonthContractDates(root);
                }
            }
            catch (Exception ex)
            {
                log.Error($"[Config Error] Failed to load config.json: {ex.Message}", ex);
            }
        }

        private void LoadJpxSubscriptionStartTime(JsonElement root)
        {
            if (!root.TryGetProperty("JPXSubscriptionStartTime", out JsonElement value) ||
                value.ValueKind != JsonValueKind.String)
            {
                return;
            }

            string configured = value.GetString() ?? string.Empty;
            if (TimeSpan.TryParseExact(
                    configured,
                    new[] { @"hh\:mm", @"hh\:mm\:ss" },
                    CultureInfo.InvariantCulture,
                    out TimeSpan parsed) &&
                parsed >= TimeSpan.Zero && parsed < TimeSpan.FromDays(1))
            {
                JpxSubscriptionStartTime = parsed;
                return;
            }

            log.Warn($"[Config] JPXSubscriptionStartTime 格式錯誤：{configured}，使用預設值 07:20:00。");
        }

        private void InitializeJpxSubscriptionSchedule()
        {
            _jpxSubscriptionNotBefore = GetJpxSubscriptionNotBefore(DateTime.Now, JpxSubscriptionStartTime);
        }

        private void LoadSubscribeProductIds(JsonElement root)
        {
            _subscribeProductIds.Clear();
            if (!root.TryGetProperty("SubscribeProductIDs", out JsonElement productIds)) return;

            IEnumerable<string?> values = productIds.ValueKind switch
            {
                JsonValueKind.Array => productIds.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString()),
                JsonValueKind.String => (productIds.GetString() ?? string.Empty)
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                _ => Array.Empty<string>()
            };

            foreach (string productId in values
                         .Where(value => !string.IsNullOrWhiteSpace(value))
                         .Select(value => value!.Trim().ToUpperInvariant())
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                _subscribeProductIds.Add(productId);
            }
        }

        private static void LoadHotMonthContractDates(JsonElement root)
        {
            if (!root.TryGetProperty("HotMonthContractDates", out JsonElement dates) ||
                dates.ValueKind != JsonValueKind.Array)
            {
                SymbolRepository.Instance.SetHotMonthContractDates(Array.Empty<HotMonthContractDateInput>());
                return;
            }

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            List<HotMonthContractDateInput> inputs =
                JsonSerializer.Deserialize<List<HotMonthContractDateInput>>(dates.GetRawText(), options) ?? new();
            SymbolRepository.Instance.SetHotMonthContractDates(inputs);
        }
        #endregion

        #region Connect & Lifecycle
        public void Connect()
        {
            try
            {
                AdvanceConnectionGeneration();
                ResetInitializationState();

                if (_quote != null)
                {
                    UnsubscribeEvents();
                    try { _quote.Logout(); } catch { }
                    try { _quote.Dispose(); } catch { }
                    _quote = null;
                }

                _quote = new QuoteCom(Host, Port, SourceID, Token);
                _antiGcCrasher.Add(_quote); // 阻止 GC 回收 QuoteCom 與內部的 Login 物件
                SubscribeEvents();
                _quote.Connect(Host, Port);
                
                string msg = $"[行情] 正在初始化行情伺服器 {Host}:{Port}...";
                log.Info(msg);
                OnLogMessage?.Invoke(msg);
                OnQuoteMessage?.Invoke(msg);
            }
            catch (Exception ex)
            {
                string errMsg = $"[行情錯誤] 初始化失敗: {ex.Message}";
                log.Error(errMsg, ex);
                OnLogMessage?.Invoke(errMsg);
                OnQuoteMessage?.Invoke(errMsg);
            }
        }

        private void SubscribeEvents()
        {
            if (_quote == null) return;
            _quote.OnGetStatus += Quote_OnGetStatus;
            _quote.OnRcvMessage += Quote_OnRcvMessage;
            _quote.OnRecoverStatus += Quote_OnRecoverStatus;
            _quote.OnRcvServerTime += Quote_OnRcvServerTime;
        }

        private void UnsubscribeEvents()
        {
            if (_quote == null) return;
            _quote.OnGetStatus -= Quote_OnGetStatus;
            _quote.OnRcvMessage -= Quote_OnRcvMessage;
            _quote.OnRecoverStatus -= Quote_OnRecoverStatus;
            _quote.OnRcvServerTime -= Quote_OnRcvServerTime;
        }

        public void Login(string id, string pwd)
        {
            if (_quote is null) return;
            _quote.Login(id, pwd, ' ');
            string msg = $"[行情] 正在傳送行情登入請求: {id}...";
            log.Info(msg);
            OnLogMessage?.Invoke(msg);
            OnQuoteMessage?.Invoke(msg);
        }

        private void ResetInitializationState()
        {
            Interlocked.Exchange(ref _twInfoSubscriptionStarted, 0);
            Interlocked.Exchange(ref _domesticProductDownloadStarted, 0);
            Interlocked.Exchange(ref _foreignProductDownloadStarted, 0);
            Interlocked.Exchange(ref _symbolRepositoryBuildStarted, 0);
            Interlocked.Exchange(ref _optionPricingSubscriptionState, 0);
        }

        private void TrySubscribeProductInfo(QuoteCom? quote)
        {
            quote ??= _quote;
            if (quote == null || !ReferenceEquals(_quote, quote)) return;
            if (Interlocked.CompareExchange(ref _twInfoSubscriptionStarted, 1, 0) != 0) return;

            try
            {
                short result = quote.SubQuote("TW.INFO");
                if (result < 0)
                {
                    Interlocked.Exchange(ref _twInfoSubscriptionStarted, 0);
                    string message = $"[行情] TW.INFO 訂閱失敗: {result} ({quote.GetSubQuoteMsg(result)})";
                    log.Warn(message);
                    OnLogMessage?.Invoke(message);
                    OnQuoteMessage?.Invoke(message);
                }
            }
            catch
            {
                Interlocked.Exchange(ref _twInfoSubscriptionStarted, 0);
                throw;
            }
        }

        private void QueueQuoteWork(string operationName, QuoteCom? quote, Action<QuoteCom> work, Action? onFailure = null)
        {
            quote ??= _quote;
            if (quote == null || !ReferenceEquals(_quote, quote)) return;

            int generation = Volatile.Read(ref _connectionGeneration);
            _ = Task.Run(() =>
            {
                if (generation != Volatile.Read(ref _connectionGeneration) || !ReferenceEquals(_quote, quote))
                    return;

                try
                {
                    work(quote);
                }
                catch (Exception ex)
                {
                    onFailure?.Invoke();
                    string message = $"[行情背景作業失敗] {operationName}: {ex.Message}";
                    log.Error(message, ex);
                    OnLogMessage?.Invoke(message);
                    OnQuoteMessage?.Invoke(message);
                }
            });
        }

        public bool StartOptionPricingSubscriptions()
        {
            QuoteCom? quote = _quote;
            if (quote == null)
            {
                PublishQuoteMessage("[選擇權行情] 行情尚未連線，無法啟動訂閱。", true);
                return false;
            }

            if (!SymbolRepository.Instance.IsInitialized)
            {
                PublishQuoteMessage("[選擇權行情] 商品資訊尚未建立完成，請稍後再啟動。", true);
                return false;
            }

            if (Interlocked.CompareExchange(ref _optionPricingSubscriptionState, 1, 0) != 0)
                return false;

            PublishQuoteMessage("[選擇權行情] 正在建立 TXO 報價鏈並送出訂閱...", false);
            QueueQuoteWork("啟動 TXO 行情訂閱", quote, currentQuote =>
            {
                if (BuildTXOChains(currentQuote))
                {
                    Volatile.Write(ref _optionPricingSubscriptionState, 2);
                    PublishQuoteMessage("[選擇權行情] TXO 行情訂閱已啟動。", false);
                }
                else
                {
                    Volatile.Write(ref _optionPricingSubscriptionState, 0);
                    PublishQuoteMessage("[選擇權行情] 找不到可訂閱的 TXO 商品，啟動失敗。", true);
                }
            }, () => Volatile.Write(ref _optionPricingSubscriptionState, 0));
            return true;
        }

        private void PublishQuoteMessage(string message, bool warning)
        {
            if (warning) log.Warn(message);
            else log.Info(message);
            OnLogMessage?.Invoke(message);
            OnQuoteMessage?.Invoke(message);
        }

        public void Disconnect()
        {
            Shutdown();
        }

        public void Shutdown()
        {
            try
            {
                AdvanceConnectionGeneration();
                ResetInitializationState();
                lock (_jpxSubscriptionLock)
                {
                    _jpxSubscriptionTimer?.Dispose();
                    _jpxSubscriptionTimer = null;
                    _pendingJpxSubscriptions.Clear();
                }

                if (_quote is not null)
                {
                    UnsubscribeEvents();
                    try { _quote.Logout(); } catch { }
                    try { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(_quote); } catch { }
                    _quote = null;
                    h_MData.Clear();
                    SymbolRepository.Instance.Clear();
                    string msg = "[行情] 行情服務已關閉";
                    log.Info(msg);
                    OnLogMessage?.Invoke(msg);
                    OnQuoteMessage?.Invoke(msg);
                }
            }
            catch { /* 忽略底層錯誤 */ }
        }

        private System.Timers.Timer? _reconnectTimer;
        private readonly object _reconnectLock = new object();

        public void ScheduleReconnect(int delayMs = 3000)
        {
            lock (_reconnectLock)
            {
                _reconnectTimer?.Stop();
                _reconnectTimer?.Dispose();
                _reconnectTimer = new System.Timers.Timer(delayMs) { AutoReset = false };
                _reconnectTimer.Elapsed += (s, e) =>
                {
                    lock (_reconnectLock)
                    {
                        _reconnectTimer?.Dispose();
                        _reconnectTimer = null;
                    }
                    string msg = "[行情] 正在自動重新建立連線...";
                    log.Info(msg);
                    OnLogMessage?.Invoke(msg);
                    OnQuoteMessage?.Invoke(msg);
                    Connect();
                };
                _reconnectTimer.Start();
            }
        }
        #endregion

        #region Subscription & Query
        public void Subscribe(string symbol)
        {
            if (_quote is null || h_MData.ContainsKey(symbol)) 
                return;

            ContractInfo ci = SymbolRepository.Instance.GetContract(symbol);
            if (string.IsNullOrEmpty(ci.Symbol))
            {
                // 如果 SymbolRepository 查不到，直接走一般國內/字串訂閱
                short r = _quote.SubQuote(symbol);
                if (r >= 0)
                {
                    h_MData.TryAdd(symbol, new MarketDataStruct(symbol));
                    string okMsg = $"[行情] 已送出訂閱請求: {symbol}";
                    OnLogMessage?.Invoke(okMsg);
                    OnQuoteMessage?.Invoke(okMsg);
                }
                else
                {
                    string failMsg = $"[行情] 訂閱失敗代碼: {r} ({_quote.GetSubQuoteMsg(r)})";
                    OnLogMessage?.Invoke(failMsg);
                    OnQuoteMessage?.Invoke(failMsg);
                }
                return;
            }

            if (TryDelayMorningJpxSubscription(ci, symbol))
                return;

            short res = -1;
            if (ci.ExchangeNm == "TIMEX" || string.IsNullOrEmpty(ci.ExchangeNm))
            {
                res = _quote.SubQuote(symbol);
            }
            else
            {
                if (ci.ComType == 'F')
                {
                    res = Subscribe(ci.Exchange, MARKET_FLAG.MF_FUT, symbol);
                }
                else
                {
                    res = Subscribe(ci.Exchange, MARKET_FLAG.MF_OPT, ci.ProductId, ci.ComYM, ci.EndDate, ci.CP, ci.StrikePx.ToString(), ci.ComYM2);
                }
            }

            if (res >= 0)
            {
                h_MData.TryAdd(ci.Symbol, new MarketDataStruct(ci.Symbol));
                h_MData[ci.Symbol].ContInfo = ci;
                string okMsg = $"[行情] 已送出訂閱請求: {ci.Symbol}";
                OnLogMessage?.Invoke(okMsg);
                OnQuoteMessage?.Invoke(okMsg);
            }
            else
            {
                string failMsg = $"[行情] 訂閱失敗代碼: {res} ({_quote.GetSubQuoteMsg(res)})";
                OnLogMessage?.Invoke(failMsg);
                OnQuoteMessage?.Invoke(failMsg);
            }
        }

        /// <summary>
        /// 清晨排程啟動時，日本市場商品先完成商品檔與熱門月解析，到設定時間才送行情訂閱。
        /// 夜盤期間或設定時間之後啟動不延遲。
        /// </summary>
        public static DateTime? GetJpxSubscriptionNotBefore(DateTime startupTime)
            => GetJpxSubscriptionNotBefore(startupTime, new TimeSpan(7, 20, 0));

        public static DateTime? GetJpxSubscriptionNotBefore(DateTime startupTime, TimeSpan subscriptionStartTime)
        {
            TimeSpan time = startupTime.TimeOfDay;
            TimeSpan morningRestartWindow = new(5, 45, 0);
            return time >= morningRestartWindow && time < subscriptionStartTime
                ? startupTime.Date.Add(subscriptionStartTime)
                : null;
        }

        private bool TryDelayMorningJpxSubscription(ContractInfo contract, string symbol)
        {
            if (!_jpxSubscriptionNotBefore.HasValue ||
                !string.Equals(contract.ExchangeNm, "JPX", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            DateTime now = DateTime.Now;
            if (now >= _jpxSubscriptionNotBefore.Value)
                return false;

            bool added;
            lock (_jpxSubscriptionLock)
            {
                now = DateTime.Now;
                if (now >= _jpxSubscriptionNotBefore.Value)
                    return false;

                added = _pendingJpxSubscriptions.Add(symbol);
                ScheduleJpxSubscriptionTimerLocked(_jpxSubscriptionNotBefore.Value - now);
            }

            if (added)
            {
                string message = $"[行情] JPX 熱門月已定月，行情訂閱延後至 {_jpxSubscriptionNotBefore.Value:HH:mm}：{symbol}";
                log.Info(message);
                OnLogMessage?.Invoke(message);
                OnQuoteMessage?.Invoke(message);
            }
            return true;
        }

        private void ScheduleJpxSubscriptionTimerLocked(TimeSpan dueTime)
        {
            if (dueTime < TimeSpan.Zero) dueTime = TimeSpan.Zero;
            if (_jpxSubscriptionTimer == null)
            {
                _jpxSubscriptionTimer = new System.Threading.Timer(
                    _ => ReleasePendingJpxSubscriptions(),
                    null,
                    dueTime,
                    Timeout.InfiniteTimeSpan);
            }
            else
            {
                _jpxSubscriptionTimer.Change(dueTime, Timeout.InfiniteTimeSpan);
            }
        }

        private void ReleasePendingJpxSubscriptions()
        {
            string[] symbols;
            lock (_jpxSubscriptionLock)
            {
                if (_jpxSubscriptionNotBefore.HasValue && DateTime.Now < _jpxSubscriptionNotBefore.Value)
                {
                    ScheduleJpxSubscriptionTimerLocked(_jpxSubscriptionNotBefore.Value - DateTime.Now);
                    return;
                }
                symbols = _pendingJpxSubscriptions.ToArray();
            }

            if (symbols.Length == 0) return;

            string startMessage = $"[行情] {JpxSubscriptionStartTime:hh\\:mm} 已到，開始訂閱 {symbols.Length} 個 JPX 熱門月商品。";
            log.Info(startMessage);
            OnLogMessage?.Invoke(startMessage);
            OnQuoteMessage?.Invoke(startMessage);

            foreach (string symbol in symbols)
                Subscribe(symbol);

            lock (_jpxSubscriptionLock)
            {
                foreach (string symbol in symbols)
                {
                    if (h_MData.ContainsKey(symbol))
                        _pendingJpxSubscriptions.Remove(symbol);
                }

                if (_pendingJpxSubscriptions.Count > 0)
                {
                    // 若設定時間到達時行情連線尚未恢復，保留清單並稍後再試。
                    ScheduleJpxSubscriptionTimerLocked(TimeSpan.FromSeconds(30));
                }
                else
                {
                    _jpxSubscriptionTimer?.Dispose();
                    _jpxSubscriptionTimer = null;
                }
            }
        }

        public short Subscribe(MARKET_EXCHANGE exchange, MARKET_FLAG flag, string symbol, string comYM = "", string dd = "", string cp = "", string strike = "", string comYM2 = "")
        {
            short res = -1;
            if (_quote is null)
                return res;

            if (string.IsNullOrEmpty(comYM))
            {
                res = _quote.SubQuote(exchange, flag, symbol);
            }
            else
            {
                // where symbol is productID (e.g. "E4C")
                res = _quote.SubQuote(exchange, flag, symbol, comYM, dd, cp, strike, comYM2);
            }

            if (res >= 0)
            {
                h_MData.TryAdd(symbol, new MarketDataStruct(symbol));
                h_MData[symbol].ContInfo = SymbolRepository.Instance.GetContract(symbol);
                string okMsg = $"[行情] 已送出國外訂閱請求: {symbol}";
                OnLogMessage?.Invoke(okMsg);
                OnQuoteMessage?.Invoke(okMsg);
                _quote.RetriveLastPrice(exchange, flag, FOREIGN_SESSION.FS_EC, symbol);
                string repMsg = $"[行情] 已送出國外價格回補請求: {symbol}";
                OnLogMessage?.Invoke(repMsg);
                OnQuoteMessage?.Invoke(repMsg);
            }
            else
            {
                string failMsg = $"[行情] 國外訂閱失敗代碼: {res} ({_quote.GetSubQuoteMsg(res)})";
                OnLogMessage?.Invoke(failMsg);
                OnQuoteMessage?.Invoke(failMsg);
            }

            return res;
        }

        public void Unsubscribe(string symbol)
        {
            if (_quote is null) return;
            _quote.UnsubQuote(symbol);
            string msg = $"[行情] 已取消訂閱: {symbol}";
            OnLogMessage?.Invoke(msg);
            OnQuoteMessage?.Invoke(msg);
        }

        public void QueryClosePrice()
        {
            if (_quote is null) return;
            short res = _quote.RetriveClosePrice();
            if (res < 0)
            {
                string msg = $"[行情] 查詢收盤失敗: {_quote.GetSubQuoteMsg(res)}";
                OnLogMessage?.Invoke(msg);
                OnQuoteMessage?.Invoke(msg);
            }
        }

        public void QueryLastPrice(string symbol)
        {
            if (_quote is null) return;
            short res = _quote.RetriveLastPrice(symbol);
            if (res < 0)
            {
                string msg = $"[行情] 查詢最後價格失敗: {_quote.GetSubQuoteMsg(res)}";
                OnLogMessage?.Invoke(msg);
                OnQuoteMessage?.Invoke(msg);
            }
        }

        public void QueryRecover(string symbol, string startTime, string endTime)
        {
            if (_quote is null) return;
            short res = _quote.RetriveRecover(symbol, startTime, endTime);
            if (res < 0)
            {
                string msg = $"[行情] 回補請求失敗: {_quote.GetSubQuoteMsg(res)}";
                OnLogMessage?.Invoke(msg);
                OnQuoteMessage?.Invoke(msg);
            }
        }

        public void DownLoadProductXML()
        {
            if (_quote == null) return;
            string msg = "國內商品查詢中.....";
            log.Info(msg);
            OnLogMessage?.Invoke(msg);
            OnQuoteMessage?.Invoke(msg);
            _quote.LoadTaifexProductXML();
        }

        public void GenSymbolRepository(QuoteCom? quote = null)
        {
            quote ??= _quote;
            if (quote == null) return;

            bool loaded = SymbolRepository.Instance.LoadAllProducts(quote);

            if (loaded)
            {
                string configuredCounts = _subscribeProductIds.Count == 0
                    ? string.Empty
                    : " 訂閱商品合約數：" + string.Join(", ", _subscribeProductIds.Select(productId =>
                        $"{productId}={SymbolRepository.Instance.GetContractsByProductId(productId).Count}"));
                string okMsg = $"商品庫存建立完成！可以開始看盤下單了。{configuredCounts}";
                log.Info(okMsg);
                OnLogMessage?.Invoke(okMsg);
                OnQuoteMessage?.Invoke(okMsg);
                SubscribeConfiguredHotContracts();
                OnSymbolRepositoryLoaded?.Invoke();
            }
            else
            {
                string failMsg = "商品庫存建立失敗，請聯絡管理員。";
                log.Warn(failMsg);
                OnLogMessage?.Invoke(failMsg);
                OnQuoteMessage?.Invoke(failMsg);
            }
        }

        private void SubscribeConfiguredHotContracts()
        {
            if (_subscribeProductIds.Count == 0) return;

            foreach (string productId in _subscribeProductIds)
            {
                ContractInfo? contract = SymbolRepository.Instance.GetHotMonthContract(productId, DateTime.Today);
                if (contract == null)
                {
                    string missing = $"[行情] 自動訂閱略過：商品庫找不到 ProductID={productId} 的未到期期貨合約。";
                    log.Warn(missing);
                    OnLogMessage?.Invoke(missing);
                    OnQuoteMessage?.Invoke(missing);
                    LogHotMonthCandidates(productId);
                    continue;
                }

                // 商品庫會在每次重新連線後重建；移除舊快照才能重新送出訂閱。
                h_MData.TryRemove(contract.Symbol, out _);
                string selected = $"[行情] 自動訂閱熱門月：ProductID={productId}, Symbol={contract.Symbol}, Month={contract.Month}, Hot={contract.Hot}";
                log.Info(selected);
                OnLogMessage?.Invoke(selected);
                OnQuoteMessage?.Invoke(selected);
                Subscribe(contract.Symbol);
            }
        }

        private void LogHotMonthCandidates(string productId)
        {
            IReadOnlyList<ContractInfo> candidates = SymbolRepository.Instance.GetContractsByProductId(productId);
            string details = candidates.Count == 0
                ? "無索引資料"
                : string.Join(" | ", candidates.Take(12).Select(contract =>
                    $"Symbol={contract.Symbol}, CPF={contract.CPF}, ComType={(int)contract.ComType}('{contract.ComType}'), " +
                    $"Month={contract.Month}, ComYM={contract.ComYM}, ComYM2={contract.ComYM2}, " +
                    $"FND={contract.FirstNoticeDate}, LTD={contract.LastTradeDate}, Expiry={contract.ExpiryDate}"));
            string message = $"[行情診斷] ProductID={productId}, Today={DateTime.Today:yyyyMMdd}, 候選={details}";
            log.Warn(message);
            OnLogMessage?.Invoke(message);
            OnQuoteMessage?.Invoke(message);
        }
        #endregion

        #region Event Handler
        private void Quote_OnGetStatus(object sender, COM_STATUS status, byte[] msg)
        {
            QuoteCom? com = sender as QuoteCom;

            try
            {
                string message = Encoding.Default.GetString(msg).Replace("\0", "");
                if (status != COM_STATUS.RECOVER_DATA)
                {
                    string statusMsg = $"[行情狀態] {status}: {message}";
                    log.Info(statusMsg);
                    OnLogMessage?.Invoke(statusMsg);
                    OnQuoteMessage?.Invoke(statusMsg);
                }

                switch (status)
                {
                    case COM_STATUS.LOGIN_READY:
                        TrySubscribeProductInfo(com);
                        break;
                    case COM_STATUS.LOGIN_FAIL:
                    case COM_STATUS.LOGIN_UNKNOW:
                        break;
                    case COM_STATUS.CONNECT_READY:
                        log.Info("[KGI API] Connect ready, attempting login...");
                        Login(UserID, Password);
                        break;
                    case COM_STATUS.CONNECT_FAIL:
                        break;
                    case COM_STATUS.DISCONNECTED:
                        AdvanceConnectionGeneration();
                        ResetInitializationState();
                        var now = DateTime.Now;
                        if (now.Hour == 5 && now.Minute >= 30 && now.Minute <= 55)
                        {
                            string rebootMsg = "[系統] 觸發排程重啟，準備開啟新程式並關閉目前實體...";
                            OnLogMessage?.Invoke(rebootMsg);
                            OnQuoteMessage?.Invoke(rebootMsg);
                            try
                            {
                                string exePath = System.Windows.Forms.Application.ExecutablePath;
                                System.Diagnostics.Process.Start(exePath);
                            }
                            catch (Exception ex)
                            {
                                string err = $"[系統] 開啟新程式失敗: {ex.Message}";
                                OnLogMessage?.Invoke(err);
                                OnQuoteMessage?.Invoke(err);
                            }
                            Environment.Exit(0);
                        }
                        break;
                    case COM_STATUS.SUBSCRIBE:
                        if (message == "TW.INFO" &&
                            Interlocked.CompareExchange(ref _domesticProductDownloadStarted, 1, 0) == 0)
                        {
                            QueueQuoteWork("下載國內商品檔", com, quote =>
                            {
                                string startMsg = "國內商品查詢中.....";
                                log.Info(startMsg);
                                OnLogMessage?.Invoke(startMsg);
                                OnQuoteMessage?.Invoke(startMsg);
                                quote.LoadTaifexProductXML();
                            });
                        }
                        break;
                    case COM_STATUS.UNSUBSCRIBE:
                    case COM_STATUS.ACK_REQUESTID:
                        break;
                    case COM_STATUS.RECOVER_DATA:
                        message = encoding.GetString(msg, 1, msg.Length - 1);
                        if (!RecoverMap.ContainsKey(message))
                            RecoverMap.Add(message, 0);

                        if (msg[0] == 0)
                        {
                            RecoverMap[message] = 0;
                            string recStart = $"開始回補 Topic:[{message}]";
                            OnLogMessage?.Invoke(recStart);
                            OnQuoteMessage?.Invoke(recStart);
                        }

                        if (msg[0] == 1)
                        {
                            string recEnd = $"結束回補 Topic:[{message} 筆數:{RecoverMap[message]}]";
                            OnLogMessage?.Invoke(recEnd);
                            OnQuoteMessage?.Invoke(recEnd);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                log.Error($"[行情狀態處理錯誤] {status}: {ex.Message}", ex);
            }
            finally
            {
                try { com?.Processed(); }
                catch (Exception ex) { log.Error($"[行情狀態確認錯誤] {ex.Message}", ex); }
            }
        }

        private void Quote_OnRecoverStatus(object sender, string topic, RECOVER_STATUS status, uint recoverCount)
        {
            OnRecoverStatusChanged?.Invoke(topic, status, recoverCount);
            string statusStr = (status == RECOVER_STATUS.RS_BEGIN) ? "開始回補" : "回補完成";
            string logMsg = $"[行情回補] {topic} {statusStr} (筆數: {recoverCount})";
            log.Info(logMsg);
            OnLogMessage?.Invoke(logMsg);
            OnQuoteMessage?.Invoke(logMsg);

            if (status == RECOVER_STATUS.RS_DONE && topic == "ProductBaseSv1802.xml")
            {
                QuoteCom? quote = sender as QuoteCom;
                if (Interlocked.CompareExchange(ref _foreignProductDownloadStarted, 1, 0) == 0)
                {
                    QueueQuoteWork("下載國外商品檔", quote, currentQuote =>
                    {
                        log.Info("[KGI API] 國內商品檔下載完成，開始查詢國外商品...");
                        OnLogMessage?.Invoke("[KGI API] 國內商品檔下載完成，開始查詢國外商品...");

                        OnQuoteMessage?.Invoke("國外商品查詢中.....");
                        currentQuote.LoadProductForeighSvXML();
                    });
                }
            }

            if (status == RECOVER_STATUS.RS_DONE && topic == "ProductBaseSv1806.xml")
            {
                QuoteCom? quote = sender as QuoteCom;
                if (Interlocked.CompareExchange(ref _symbolRepositoryBuildStarted, 1, 0) == 0)
                {
                    QueueQuoteWork("建立商品資訊庫", quote, currentQuote =>
                    {
                        log.Info("[KGI API] 國外商品查詢完成，開始建立商品資訊庫...");
                        OnLogMessage?.Invoke("[KGI API] 國外商品查詢完成，開始建立商品資訊庫...");
                        OnQuoteMessage?.Invoke("國外商品查詢完成.....");
                        OnQuoteMessage?.Invoke("建立商品資訊.....");
                        GenSymbolRepository(currentQuote);
                    });
                }
            }
        }

        private void Quote_OnRcvServerTime(object sender, DateTime serverTime)
        {
        }

        private void Quote_OnRcvMessage(object sender, PackageBase package)
        {
            if (package.TOPIC != null && RecoverMap.ContainsKey(package.TOPIC))
            {
                RecoverMap[package.TOPIC]++;
            }

            OnQuoteReceived?.Invoke(package);

            switch (package.DT)
            {
                case (ushort)DT.LOGIN:
                    P001503 _p001503 = (P001503)package;
                    if (_p001503.Code == 0)
                    {
                        string qMsg = "登入成功，可註冊檔數：" + _p001503.Qnum;
                        OnLogMessage?.Invoke(qMsg);
                        OnQuoteMessage?.Invoke(qMsg);
                        TrySubscribeProductInfo(sender as QuoteCom);
                    }
                    else
                    {
                        string qErr = $"登入失敗，代碼：{_p001503.Code}";
                        OnLogMessage?.Invoke(qErr);
                        OnQuoteMessage?.Invoke(qErr);
                    }
                    break;

                case (ushort)DT.NOTICE_RPT:
                    P001701 p1701 = (P001701)package;
                    OnQuoteMessage?.Invoke(p1701.ToLog());
                    break;

                case (ushort)DT.NOTICE:
                    P001702 p1702 = (P001702)package;
                    OnQuoteMessage?.Invoke(p1702.ToLog());
                    break;

                case (ushort)DT.QUOTE_I020:
                case (ushort)DT.QUOTE_I022:
                    PI20020 i20020 = (PI20020)package;
                    if (!string.IsNullOrEmpty(i20020.Symbol) && (double)i20020.Price > 0)
                    {
                        _lastMatchPrices[i20020.Symbol] = (double)i20020.Price;
                    }
                    if (h_MData.ContainsKey(i20020.Symbol))
                    {
                        h_MData[i20020.Symbol].UpdateMatch(i20020);
                        OnMarketData?.Invoke(h_MData[i20020.Symbol]);
                        OnMarketDataReceived?.Invoke(i20020.Symbol, h_MData[i20020.Symbol]);
                    }
                    break;

                case (ushort)DT.QUOTE_I080:
                case (ushort)DT.QUOTE_I082:
                case (ushort)DT.QUOTE_I380:
                    PI20080 i20080 = (PI20080)package;
                    if (!string.IsNullOrEmpty(i20080.Symbol))
                    {
                        _lastQuotes[i20080.Symbol] = i20080;
                    }
                    if (h_MData.ContainsKey(i20080.Symbol))
                    {
                        h_MData[i20080.Symbol].UpdateQuote(i20080);
                        OnMarketData?.Invoke(h_MData[i20080.Symbol]);
                        OnMarketDataReceived?.Invoke(i20080.Symbol, h_MData[i20080.Symbol]);
                    }
                    break;

                case (ushort)DT.QUOTE_I080_RECOVER:
                    PI20080 i21080 = (PI20080)package;
                    if (h_MData.ContainsKey(i21080.Symbol))
                    {
                        h_MData[i21080.Symbol].UpdateQuote(i21080);
                        OnMarketData?.Invoke(h_MData[i21080.Symbol]);
                        OnMarketDataReceived?.Invoke(i21080.Symbol, h_MData[i21080.Symbol]);
                    }
                    break;

                case (ushort)DT.QUOTE_I081:
                    PI20081 i20081 = (PI20081)package;
                    OnQuoteMessage?.Invoke(i20081.ToLog() + Environment.NewLine);
                    break;

                case (ushort)DT.QUOTE_I024:
                    PI20024 i20024 = (PI20024)package;
                    OnQuoteMessage?.Invoke(i20024.ToLog() + Environment.NewLine);
                    break;

                #region 外期行情處理 (Foreign Futures Quotes Assembling)
                case (ushort)DT.QUOTE_FOREIGN_MATCH:
                    PI40020 pi40020 = (PI40020)package;
                    if (h_MData.ContainsKey(pi40020.Symbol))
                    {
                        h_MData[pi40020.Symbol].UpdateMatch(pi40020);
                        OnResearchMarketEvent?.Invoke(pi40020.Symbol, "trade", h_MData[pi40020.Symbol]);
                        OnMarketData?.Invoke(h_MData[pi40020.Symbol]);
                        OnMarketDataReceived?.Invoke(pi40020.Symbol, h_MData[pi40020.Symbol]);
                    }
                    break;

                case (ushort)DT.QUOTE_FOREIGN_TOTALQTY:
                    PI40023 pi40023 = (PI40023)package;
                    if (h_MData.ContainsKey(pi40023.Symbol))
                    {
                        h_MData[pi40023.Symbol].UpdateTotalQty(pi40023);
                        OnResearchMarketEvent?.Invoke(pi40023.Symbol, "total", h_MData[pi40023.Symbol]);
                    }
                    break;

                case (ushort)DT.QUOTE_FOREIGN_CLOSEINFO:
                    PI40021 pi40021 = (PI40021)package;
                    if (h_MData.ContainsKey(pi40021.Symbol))
                    {
                        h_MData[pi40021.Symbol].UpdateCloseInfo(pi40021);
                        OnCloseInfo?.Invoke(h_MData[pi40021.Symbol]);
                    }
                    break;

                case (ushort)DT.QUOTE_FOREIGN_SETTLEMENTPRICE:
                    PI40022 pi40022 = (PI40022)package;
                    if (h_MData.ContainsKey(pi40022.Symbol))
                    {
                        h_MData[pi40022.Symbol].UpdateSettlePx(pi40022);
                    }
                    break;

                case (ushort)DT.QUOTE_FOREIGN_DEPTH:
                    PI40080 pi40080 = (PI40080)package;
                    if (h_MData.ContainsKey(pi40080.Symbol))
                    {
                        h_MData[pi40080.Symbol].UpdateQuote(pi40080);
                        OnResearchMarketEvent?.Invoke(pi40080.Symbol, "depth_delta", h_MData[pi40080.Symbol]);
                        OnMarketData?.Invoke(h_MData[pi40080.Symbol]);
                        OnMarketDataReceived?.Invoke(pi40080.Symbol, h_MData[pi40080.Symbol]);
                    }
                    break;

                case (ushort)DT.QUOTE_FOREIGN_DEPTH_SS:
                    PI40090 pi40090 = (PI40090)package;
                    if (h_MData.ContainsKey(pi40090.Symbol))
                    {
                        h_MData[pi40090.Symbol].UpdateQuote(pi40090);
                        OnResearchMarketEvent?.Invoke(pi40090.Symbol, "depth_snapshot", h_MData[pi40090.Symbol]);
                        OnMarketData?.Invoke(h_MData[pi40090.Symbol]);
                        OnMarketDataReceived?.Invoke(pi40090.Symbol, h_MData[pi40090.Symbol]);
                    }
                    break;

                case (ushort)DT.QUOTE_FOREIGN_LASTPRICE:
                    PI40026 pi40026 = (PI40026)package;
                    if (h_MData.ContainsKey(pi40026.Symbol))
                    {
                        h_MData[pi40026.Symbol].UpdateLastPx(pi40026);
                        OnLastInfo?.Invoke(h_MData[pi40026.Symbol]);
                    }
                    break;
                #endregion
            }
        }
        #endregion

        #region TXO Option Chains Processing
        private bool BuildTXOChains(QuoteCom? quote = null)
        {
            quote ??= _quote;
            if (quote == null) return false;

            string[] productIds = new string[] { "TXO", "TX1", "TX2", "TX4", "TX5", "TXU", "TXV", "TXX", "TXY", "TXZ" };
            var groups = new Dictionary<string, (DateTime Date, string Pid, List<PT01802> Options)>();

            foreach (var pid in productIds)
            {
                var detailList = quote.GetTaifexProductDetailList(pid);
                if (detailList == null) continue;

                foreach (PT01802 p in detailList)
                {
                    if (DateTime.TryParseExact(p.EndDate, "yyyyMMdd", null, DateTimeStyles.None, out DateTime expDate))
                    {
                        string key = $"{pid} {expDate:yyyy-MM-dd}";
                        if (!groups.ContainsKey(key)) groups[key] = (expDate, pid, new List<PT01802>());
                        groups[key].Options.Add(p);
                    }
                }
            }

            if (groups.Count == 0)
            {
                OnLogMessage?.Invoke("[KGI API] 無法取得任何選擇權商品明細！");
                return false;
            }

            var sortedGroups = groups.Values.OrderBy(g => g.Date).ToList();
            var targetGroups = new List<(DateTime Date, string Pid, List<PT01802> Options)>();

            int addedCount = 0;
            bool txoAdded = false;

            foreach (var g in sortedGroups)
            {
                if (addedCount < 4)
                {
                    targetGroups.Add(g);
                    addedCount++;
                    if (g.Pid == "TXO") txoAdded = true;
                }
                else if (!txoAdded && g.Pid == "TXO")
                {
                    targetGroups.Add(g);
                    txoAdded = true;
                    addedCount++;
                }
                
                if (addedCount >= 5) break;
            }

            _optionChains.Clear();
            _lastQuotes.Clear();

            int subCount = 0;
            foreach (var g in targetGroups)
            {
                bool isWeekly = (g.Date - DateTime.Today).TotalDays < 10;
                var chain = new OptionChain(g.Date, isWeekly, RiskFreeRate, _calendar);
                string key = $"{g.Pid} {g.Date:yyyy-MM-dd}";
                
                foreach (var p in g.Options)
                {
                    string comId = p.ComId;
                    if (string.IsNullOrEmpty(comId) || comId.Length < 6) continue;
                    
                    char cpChar = char.ToUpper(comId[comId.Length - 2]);
                    bool isCall = cpChar <= 'L' && cpChar >= 'A';
                    
                    string strikeStr = comId.Substring(3, comId.Length - 5);
                    if (decimal.TryParse(strikeStr, out decimal strike))
                    {
                        var opt = new Option(isCall, (double)strike, g.Date, RiskFreeRate, _calendar)
                        {
                            Symbol = comId
                        };

                        if (isCall) chain.AddOption(opt);
                        else chain.AddOption(opt);
                        
                        quote.SubQuote(comId);
                        subCount++;
                    }
                }
                _optionChains[key] = chain;
            }

            OnLogMessage?.Invoke($"[KGI API] 已完成 {targetGroups.Count} 個到期日，共 {subCount} 檔選擇權訂閱，等待即時報價...");
            OnOptionsDataUpdated?.Invoke(_optionChains);
            return true;
        }

        public Dictionary<string, OptionChain> MockCreateTXOContracts(double currentFuturesPrice, TradingCalendar calendar)
        {
            _calendar = calendar;
            return _optionChains;
        }

        public void FetchLatestQuotesIntoChains(double currentFuturesPrice, DateTime currentDate)
        {
            foreach (var chain in _optionChains.Values)
            {
                foreach (var call in chain.Calls)
                {
                    if (string.IsNullOrEmpty(call.Symbol)) continue;
                    string symbolTW = call.Symbol.StartsWith("TW.") ? call.Symbol : "TW." + call.Symbol;
                    if (_lastQuotes.TryGetValue(call.Symbol, out var qut) || _lastQuotes.TryGetValue(symbolTW, out qut))
                    {
                        if (qut.BUY_DEPTH != null && qut.BUY_DEPTH.Length > 0 && (double)qut.BUY_DEPTH[0].PRICE > 0)
                            call.Bid = (double)qut.BUY_DEPTH[0].PRICE;
                            
                        if (qut.SELL_DEPTH != null && qut.SELL_DEPTH.Length > 0 && (double)qut.SELL_DEPTH[0].PRICE > 0)
                            call.Ask = (double)qut.SELL_DEPTH[0].PRICE;
                    }
                    if (_lastMatchPrices.TryGetValue(call.Symbol, out double matchPrice) || _lastMatchPrices.TryGetValue(symbolTW, out matchPrice))
                    {
                        call.LastPrice = matchPrice;
                    }
                }
                foreach (var put in chain.Puts)
                {
                    if (string.IsNullOrEmpty(put.Symbol)) continue;
                    string symbolTW = put.Symbol.StartsWith("TW.") ? put.Symbol : "TW." + put.Symbol;
                    if (_lastQuotes.TryGetValue(put.Symbol, out var qut) || _lastQuotes.TryGetValue(symbolTW, out qut))
                    {
                        if (qut.BUY_DEPTH != null && qut.BUY_DEPTH.Length > 0 && (double)qut.BUY_DEPTH[0].PRICE > 0)
                            put.Bid = (double)qut.BUY_DEPTH[0].PRICE;
                            
                        if (qut.SELL_DEPTH != null && qut.SELL_DEPTH.Length > 0 && (double)qut.SELL_DEPTH[0].PRICE > 0)
                            put.Ask = (double)qut.SELL_DEPTH[0].PRICE;
                    }
                    if (_lastMatchPrices.TryGetValue(put.Symbol, out double matchPrice) || _lastMatchPrices.TryGetValue(symbolTW, out matchPrice))
                    {
                        put.LastPrice = matchPrice;
                    }
                }

                // Push through pricing engine
                chain.UpdateMarketData(0, currentDate);
            }

            if (_optionChains.Count > 0)
            {
                OnOptionsDataUpdated?.Invoke(_optionChains);
            }
        }
        #endregion

        #region Data Models (MarketDataStruct, ContractInfoStruct, QuoteComAccount)
        /// <summary>
        /// 即時行情聚合結構（支援台指、個股選、海期 10 檔委託簿即時組裝與撮合成交）
        /// </summary>
        public class MarketDataStruct
        {
            public string Symbol { get; set; } = string.Empty;
            public char ComType { get; set; }
            public string YM6 { get; set; } = string.Empty;
            public double StrikePx { get; set; }
            public string ProductID { get; set; } = string.Empty;
            public MARKET_FLAG MarketFlag { get; set; }
            public ContractInfo ContInfo { get; set; }
            public decimal[] BuyPx { get; set; }
            public decimal[] SellPx { get; set; }
            public int[] BuyQty { get; set; }
            public int[] SellQty { get; set; }
            public int MatchQty { get; set; }
            public string? SourceTradeTime { get; private set; }
            public int MatchTotalQty { get; set; }
            public decimal MatchcPx { get; set; }
            public bool IsMatchUpdate { get; set; }
            public TimeOnly UpdateTime { get; set; }
            public uint iUpdateTime { get; set; }
            public decimal SettlementPx { get; set; }
            public decimal OpenPx { get; set; }
            public decimal LastPx { get; set; }
            public int LastQty { get; set; }

            public MarketDataStruct(string symbol = "")
            {
                Symbol = symbol;
                ContInfo = new ContractInfo();
                if (!string.IsNullOrEmpty(symbol))
                    ContInfo = SymbolRepository.Instance.GetContract(symbol);
                MarketFlag = MARKET_FLAG.MF_FUT;
                ProductID = ContInfo.ProductId;
               
                BuyPx = new decimal[10];
                SellPx = new decimal[10];
                BuyQty = new int[10];
                SellQty = new int[10];
            }

            public void UpdateQuote(PI20080 qut)
            {
                IsMatchUpdate = false;
                iUpdateTime = qut.DATA_TIME;

                ReadOnlySpan<PI20080.FIVE_LEVEL_PRICE> buys = qut.BUY_DEPTH;
                ReadOnlySpan<PI20080.FIVE_LEVEL_PRICE> sells = qut.SELL_DEPTH;

                int len = Math.Min(5, Math.Min(buys.Length, sells.Length));

                for (int i = 0; i < len; i++)
                {
                    BuyPx[i] = buys[i].PRICE;
                    BuyQty[i] = buys[i].QUANTITY;

                    SellPx[i] = sells[i].PRICE;
                    SellQty[i] = sells[i].QUANTITY;
                }
            }

            public void UpdateMatch(PI20020 match)
            {
                IsMatchUpdate = true;
                iUpdateTime = match.MatchTime;
                UpdateTime = ConvertTime(iUpdateTime);
                SourceTradeTime = UpdateTime.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
                MatchcPx = match.Price;
                MatchQty = match.MatchQuantity;
                MatchTotalQty = match.MatchTotalQty;
            }

            /// <summary>
            /// 外期委託簿快照更新 (PI40090)
            /// </summary>
            public void UpdateQuote(PI40090 fQut)
            {
                if (fQut == null || fQut.BUY_DEPTH == null || fQut.SELL_DEPTH == null)
                    return;

                IsMatchUpdate = false;
                ReadOnlySpan<PI40090.FIVE_LEVEL_PRICE> buys = fQut.BUY_DEPTH;
                ReadOnlySpan<PI40090.FIVE_LEVEL_PRICE> sells = fQut.SELL_DEPTH;

                int len = Math.Min(10, Math.Min(buys.Length, sells.Length));

                Array.Clear(BuyPx); Array.Clear(BuyQty);
                Array.Clear(SellPx); Array.Clear(SellQty);

                for (int i = 0; i < len; i++)
                {
                    BuyPx[i] = buys[i].PRICE;
                    BuyQty[i] = buys[i].QUANTITY;

                    SellPx[i] = sells[i].PRICE;
                    SellQty[i] = sells[i].QUANTITY;
                }
            }

            /// <summary>
            /// 外期逐筆/檔位增量更新與 10 檔委託簿維護演算法 (PI40080)
            /// </summary>
            public void UpdateQuote(PI40080 fTick)
            {
                IsMatchUpdate = false;
                iUpdateTime = Convert.ToUInt32(DateTime.Now.ToString("HHmmssff"));
                ForeignDepthBook.Apply(BuyPx, BuyQty, SellPx, SellQty,
                    fTick.Side, fTick.Position, fTick.TopMark,
                    (decimal)fTick.Price, fTick.Quantity);
            }

            private static void RemoveDepthPrice(decimal[] prices, int[] quantities, decimal price)
            {
                int index = Array.IndexOf(prices, price);
                if (index < 0) return;
                int count = prices.Length - index - 1;
                Array.Copy(prices, index + 1, prices, index, count);
                Array.Copy(quantities, index + 1, quantities, index, count);
                prices[^1] = 0;
                quantities[^1] = 0;
            }

            public void UpdateMatch(PI40020 fMatch)
            {
                SourceTradeTime = string.IsNullOrWhiteSpace(fMatch.TradeTime) ? null : fMatch.TradeTime;
                IsMatchUpdate = true;
                MatchcPx = fMatch.Price;
                MatchQty = fMatch.Quantity;
                MatchTotalQty = fMatch.TotalQty;
                if (!string.IsNullOrEmpty(fMatch.TradeTime))
                {
                    if (TimeOnly.TryParseExact(fMatch.TradeTime, "HH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly t))
                        UpdateTime = t;
                    else if (TimeOnly.TryParse(fMatch.TradeTime, out TimeOnly t2))
                        UpdateTime = t2;
                }
            }

            private TimeOnly ConvertTime(uint iUpdateTime)
            {
                uint _time = iUpdateTime;
                int hundredths = (int)(_time % 100);
                _time /= 100;

                int seconds = (int)(_time % 100);
                _time /= 100;

                int minutes = (int)(_time % 100);
                int hours = (int)(_time / 100);

                return new TimeOnly(hours, minutes, seconds, hundredths * 10);
            }

            internal void UpdateTotalQty(PI40023 pi40023)
            {
                MatchTotalQty = pi40023.TotalQty;
            }

            internal void UpdateCloseInfo(PI40021 pi40021)
            {
                MatchTotalQty = pi40021.CloseQty;
                MatchcPx = pi40021.ClosePrice;
            }

            internal void UpdateSettlePx(PI40022 pi40022)
            {
                SettlementPx = pi40022.SettlementPrice;
            }

            internal void UpdateLastPx(PI40026 pi40026)
            {
                OpenPx = pi40026.OpenPrice;
                LastPx = pi40026.LastPrice;
                LastQty = pi40026.DayTradeQty;
                for (int i = 0; i < Math.Min(10, pi40026.BuyDepthCount); i++)
                {
                    BuyPx[i] = pi40026.BUY_DEPTH[i].PRICE;
                    BuyQty[i] = pi40026.BUY_DEPTH[i].QUANTITY;
                }
                for (int i = 0; i < Math.Min(10, pi40026.SellDepthCount); i++)
                {
                    SellPx[i] = pi40026.SELL_DEPTH[i].PRICE;
                    SellQty[i] = pi40026.SELL_DEPTH[i].QUANTITY;
                }
            }
        }

        public class ContractInfoStruct
        {
            public string Symbol { get; set; } = string.Empty;
            public PT01805? ProductBase { get; set; }
            public PT01802? ProductDetail { get; set; }
        }

        public class QuoteComAccount
        {
            public string AccName { get; set; } = "DefaultAcc";
            public string ConnIP { get; set; } = "quoteapi.kgi.com.tw";
            public ushort ConnPort { get; set; } = 443;
            public string UserID { get; set; } = "";
            public string Password { get; set; } = "";
            public string SourceID { get; set; } = "API";
            public string Token { get; set; } = "";
            public char Area { get; set; } = ' ';

            public static QuoteComAccount GetDefaultAcc(string? configPath = null)
            {
                var dacc = new QuoteComAccount();
                string path = configPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");

                if (File.Exists(path))
                {
                    try
                    {
                        string json = File.ReadAllText(path);
                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;

                        if (root.TryGetProperty("QuoteHost", out var qh) && !string.IsNullOrWhiteSpace(qh.GetString()))
                            dacc.ConnIP = qh.GetString()!;
                        else if (root.TryGetProperty("Host", out var h) && !string.IsNullOrWhiteSpace(h.GetString()))
                            dacc.ConnIP = h.GetString()!;

                        if (root.TryGetProperty("QuotePort", out var qp) && ushort.TryParse(qp.GetString(), out ushort p1))
                            dacc.ConnPort = p1;
                        else if (root.TryGetProperty("Port", out var p) && ushort.TryParse(p.GetString(), out ushort p2))
                            dacc.ConnPort = p2;

                        if (root.TryGetProperty("SourceID", out var sid) && !string.IsNullOrWhiteSpace(sid.GetString()))
                            dacc.SourceID = sid.GetString()!;

                        if (root.TryGetProperty("Token", out var tok) && !string.IsNullOrWhiteSpace(tok.GetString()))
                            dacc.Token = tok.GetString()!;

                        if (root.TryGetProperty("ID", out var id) && !string.IsNullOrWhiteSpace(id.GetString()))
                            dacc.UserID = id.GetString()!;
                        else if (root.TryGetProperty("KGI_USER_ID", out var kid) && !string.IsNullOrWhiteSpace(kid.GetString()))
                            dacc.UserID = kid.GetString()!;

                        if (root.TryGetProperty("Pwd", out var pwd) && !string.IsNullOrWhiteSpace(pwd.GetString()))
                            dacc.Password = pwd.GetString()!;
                        else if (root.TryGetProperty("KGI_PASSWORD", out var kpwd) && !string.IsNullOrWhiteSpace(kpwd.GetString()))
                            dacc.Password = kpwd.GetString()!;

                        return dacc;
                    }
                    catch { /* 讀取設定檔失敗時使用預設值 */ }
                }

                dacc.ConnIP = "quoteapi.kgi.com.tw";
                dacc.ConnPort = 443;
                dacc.UserID = "";
                dacc.Password = "";
                dacc.SourceID = "API";
                dacc.Token = "";
                return dacc;
            }
        }
        #endregion
    }
}
