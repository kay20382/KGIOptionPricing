using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Intelligence;
using Package;
using System.Diagnostics;

namespace KGIOptionPricing
{
    public class KGIQuoteService
    {
        public event Action<string>? OnLogMessage;
        public event Action<Dictionary<string, OptionChain>>? OnOptionsDataUpdated;

        private QuoteCom? _quote;
        
        // Settings 
        public string Host { get; set; } = "quoteapi.kgi.com.tw";
        public ushort Port { get; set; } = 443;
        public string SourceID { get; set; } = "API";
        public string Token { get; set; } = "b6eb";
        public string UserID { get; set; } = "";
        public string Password { get; set; } = "";
        public double RiskFreeRate { get; set; } = 0.02;
        
        private Dictionary<string, OptionChain> _optionChains = new Dictionary<string, OptionChain>();
        private ConcurrentDictionary<string, PI20080> _lastQuotes = new ConcurrentDictionary<string, PI20080>();
        private ConcurrentDictionary<string, double> _lastMatchPrices = new ConcurrentDictionary<string, double>();
        
        // Expose a public setter or just set it later. For now, MarketDataService uses _optionChains directly
        private TradingCalendar _calendar = new TradingCalendar();
        
        public void SetCalendar(TradingCalendar cal)
        {
            _calendar = cal;
        }

        public KGIQuoteService()
        {
            LoadConfig();
        }

        private void LoadConfig()
        {
            try
            {
                string configPath = "config.json";
                if (System.IO.File.Exists(configPath))
                {
                    string json = System.IO.File.ReadAllText(configPath);
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("KGI_USER_ID", out var idElem)) 
                        UserID = idElem.GetString() ?? UserID;
                    
                    if (root.TryGetProperty("KGI_PASSWORD", out var pwdElem)) 
                        Password = pwdElem.GetString() ?? Password;

                    // 1. 優先解析 QuoteServer 子物件
                    if (root.TryGetProperty("QuoteServer", out var qsElem) && qsElem.ValueKind == System.Text.Json.JsonValueKind.Object)
                    {
                        if (qsElem.TryGetProperty("Host", out var hElem) && !string.IsNullOrWhiteSpace(hElem.GetString()))
                            Host = hElem.GetString()!;

                        if (qsElem.TryGetProperty("Port", out var pElem))
                        {
                            if (pElem.ValueKind == System.Text.Json.JsonValueKind.Number && pElem.TryGetUInt16(out ushort pValNum))
                                Port = pValNum;
                            else if (pElem.ValueKind == System.Text.Json.JsonValueKind.String && ushort.TryParse(pElem.GetString(), out ushort pValStr))
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
                            if (qpElem.ValueKind == System.Text.Json.JsonValueKind.Number && qpElem.TryGetUInt16(out ushort qpNum))
                                Port = qpNum;
                            else if (qpElem.ValueKind == System.Text.Json.JsonValueKind.String && ushort.TryParse(qpElem.GetString(), out ushort qpStr))
                                Port = qpStr;
                        }
                        else if (root.TryGetProperty("Port", out var pElem))
                        {
                            if (pElem.ValueKind == System.Text.Json.JsonValueKind.Number && pElem.TryGetUInt16(out ushort pNum))
                                Port = pNum;
                            else if (pElem.ValueKind == System.Text.Json.JsonValueKind.String && ushort.TryParse(pElem.GetString(), out ushort pStr))
                                Port = pStr;
                        }

                        if (root.TryGetProperty("SourceID", out var srcElem) && !string.IsNullOrWhiteSpace(srcElem.GetString()))
                            SourceID = srcElem.GetString()!;

                        if (root.TryGetProperty("Token", out var tokElem) && !string.IsNullOrWhiteSpace(tokElem.GetString()))
                            Token = tokElem.GetString()!;
                    }

                    if (root.TryGetProperty("RISK_FREE_RATE", out var rfrElem) && rfrElem.TryGetDouble(out double rate))
                        RiskFreeRate = rate;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Config Error] Failed to load config.json: {ex.Message}");
            }
        }

        public void Connect()
        {
            try
            {
                if (_quote != null)
                {
                    _quote.Logout();
                    _quote.Dispose();
                }

                _quote = new QuoteCom(Host, Port, SourceID, Token);
                _quote.OnGetStatus += Quote_OnGetStatus;
                _quote.OnRcvMessage += Quote_OnRcvMessage;
                _quote.OnRecoverStatus += Quote_OnRecoverStatus;
                
                _quote.Connect(Host, Port);
                
                OnLogMessage?.Invoke($"[KGI API] 正在連線到 {Host}:{Port}...");
            }
            catch (Exception ex)
            {
                OnLogMessage?.Invoke($"[KGI API Error] Connection failed: {ex.Message}");
            }
        }

        private void Quote_OnGetStatus(object sender, COM_STATUS status, byte[] msg)
        {
            string message = System.Text.Encoding.Default.GetString(msg).Replace("\0", "");
            
            if (status != COM_STATUS.RECOVER_DATA)
            {
                OnLogMessage?.Invoke($"[Status] {status}: {message}");
            }

            if (status == COM_STATUS.CONNECT_READY)
            {
                _quote?.Login(UserID, Password, ' ');
            }
            else if (status == COM_STATUS.SUBSCRIBE && message == "TW.INFO")
            {
                OnLogMessage?.Invoke("[KGI API] 收到 TW.INFO，開始下載國內商品檔...");
                _quote?.LoadTaifexProductXML();
            }
        }

        private void Quote_OnRecoverStatus(object sender, string topic, RECOVER_STATUS status, uint recoverCount)
        {
            if (status == RECOVER_STATUS.RS_DONE && topic == "ProductBaseSv1802.xml")
            {
                OnLogMessage?.Invoke("[KGI API] 國內商品檔下載完成，開始解析 TXO 合約...");
                BuildTXOChains();
            }
        }

        private void BuildTXOChains()
        {
            if (_quote == null) return;

            string[] productIds = new string[] { "TXO", "TX1", "TX2", "TX4", "TX5", "TXU", "TXV", "TXX", "TXY", "TXZ" };
            var groups = new Dictionary<string, (DateTime Date, string Pid, List<PT01802> Options)>();

            foreach (var pid in productIds)
            {
                var detailList = _quote.GetTaifexProductDetailList(pid);
                if (detailList == null) continue;

                foreach (PT01802 p in detailList)
                {
                    if (DateTime.TryParseExact(p.EndDate, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out DateTime expDate))
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
                return;
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
                        
                        _quote.SubQuote(comId);
                        subCount++;
                    }
                }
                _optionChains[key] = chain;
            }

            OnLogMessage?.Invoke($"[KGI API] 已完成 {targetGroups.Count} 個到期日，共 {subCount} 檔選擇權訂閱，等待即時報價...");
            OnOptionsDataUpdated?.Invoke(_optionChains);
        }

        private void Quote_OnRcvMessage(object sender, PackageBase package)
        {
            if (package.DT == (ushort)DT.QUOTE_I080 || package.DT == (ushort)DT.QUOTE_I082)
            {
                var qut = (PI20080)package;
                if (!string.IsNullOrEmpty(qut.Symbol))
                {
                    _lastQuotes[qut.Symbol] = qut; 
                }
            }
            else if (package.DT == (ushort)DT.QUOTE_I020)
            {
                var match = (PI20020)package;
                if (!string.IsNullOrEmpty(match.Symbol) && (double)match.Price > 0)
                {
                    _lastMatchPrices[match.Symbol] = (double)match.Price;
                }
            }
            // For synthetic futures, we might also want TXF prices, but MarketDataService calculates synthetic futures from Options Parity.
        }

        public void Disconnect()
        {
            if (_quote != null)
            {
                try { _quote.Logout(); } catch { }
                try { _quote.Dispose(); } catch { }
                try { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(_quote); } catch { }
                _quote = null;
            }
        }

        public Dictionary<string, OptionChain> MockCreateTXOContracts(double currentFuturesPrice, TradingCalendar calendar)
        {
            // Do nothing if we use real data
            _calendar = calendar;
            return _optionChains;
        }

        public void FetchLatestQuotesIntoChains(double currentFuturesPrice, DateTime currentDate)
        {
            // Note: `currentFuturesPrice` passed here is usually derived or just a fallback.
            // We rely on OptionParity synthetic futures.
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
                // Pass 0 as currentFuturesPrice to force the system to calculate Synthetic Futures
                chain.UpdateMarketData(0, currentDate);
            }

            if (_optionChains.Count > 0)
            {
                OnOptionsDataUpdated?.Invoke(_optionChains);
            }
        }
    }
}
