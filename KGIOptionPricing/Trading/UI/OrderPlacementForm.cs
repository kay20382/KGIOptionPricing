using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using KGIOptionPricing.OrderManagement.Core;

namespace KGIOptionPricing
{
    public partial class OrderPlacementForm : Form
    {
        private readonly OrderManager _orderManager = null!;
        private readonly KGITradeService _tradeService = null!;
        private readonly MarketDataService? _marketDataService;
        private readonly Form1? _mainForm;

        // Foreign common symbol library
        private static readonly Dictionary<string, List<string>> _foreignExchSymbols = new Dictionary<string, List<string>>
        {
            { "CME", new List<string> { "MNQ - 微型那指", "NQ - 那斯達克100", "MES - 微型標普", "ES - 標普500", "MYM - 微型道瓊", "YM - 道瓊期貨", "MCL - 微型原油", "CL - 輕原油", "MGC - 微型黃金", "GC - 黃金", "SI - 白銀", "TU - 2年期美債", "FV - 5年期美債", "TY - 10年期美債", "US - 30年期美債", "UB - 超級30年美債" } },
            { "CBOT", new List<string> { "C - 玉米", "W - 小麥", "S - 黃豆", "SM - 黃豆粉", "BO - 黃豆油" } },
            { "SGX", new List<string> { "TWN - 富時台灣", "CN - 富時A50", "FEF - 鐵礦石", "NK - 日經225", "IN - 印度指數" } },
            { "HKEX", new List<string> { "HSI - 恆生指數", "MHI - 微型恆指", "HHI - 國企指數", "MCH - 微型國企" } },
            { "ICE", new List<string> { "DX - 美元指數", "B - 布蘭特原油", "SB - 11號糖", "KC - 咖啡", "CC - 可可", "CT - 棉花", "OJ - 凍橘汁" } },
            { "OSE", new List<string> { "225M - 微型日經", "NK225 - 日經225", "TPX - 東證期貨" } },
            { "KRX", new List<string> { "KOSPI2 - 韓國指數", "FUS50 - 海力士個股期" } }
        };

        public OrderPlacementForm() => InitializeComponent();

        public OrderPlacementForm(OrderManager orderManager, KGITradeService tradeService, MarketDataService? marketDataService, Form1? mainForm = null) : this()
        {
            _orderManager = orderManager;
            _tradeService = tradeService;
            _marketDataService = marketDataService;
            _mainForm = mainForm;

            // Hook events for order execution feedback
            _orderManager.OnOrderStateChanged += OrderManager_OnOrderStateChanged;
            _orderManager.OnOrderFailed += OrderManager_OnOrderFailed;

            if (_marketDataService?.QuoteService != null)
            {
                _marketDataService.QuoteService.OnOptionsDataUpdated += QuoteService_OnOptionsDataUpdated;
            }
        }

        private void OrderPlacementForm_Load(object sender, EventArgs e)
        {
            InitAccountAndBroker();
            RefreshDomesticExpirationsAndStrikes();
            InitForeignCombos();
            InitConditionCombos();
            UpdateSymbolDisplay();
        }

        private void OrderPlacementForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        }

        private void QuoteService_OnOptionsDataUpdated(Dictionary<string, OptionChain> chains)
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke(new Action(() =>
            {
                if (rbTXO.Checked)
                {
                    RefreshDomesticExpirationsAndStrikes();
                    UpdateSymbolDisplay();
                }
            }));
        }

        private void InitAccountAndBroker()
        {
            cboAccount.Items.Clear();
            string broker = _tradeService.BrokerID;
            string acc = _tradeService.Account;
            if (!string.IsNullOrEmpty(acc))
            {
                string display = string.IsNullOrEmpty(broker) ? acc : $"{broker}-{acc}";
                cboAccount.Items.Add(display);
                cboAccount.SelectedIndex = 0;
            }
            else
            {
                cboAccount.Items.Add("未登入帳號");
                cboAccount.SelectedIndex = 0;
            }

            lblStatusConn.Text = _tradeService.IsLoggedIn ? "● 交易伺服器已連線" : "○ 交易伺服器未連線";
            lblStatusConn.ForeColor = _tradeService.IsLoggedIn ? Color.FromArgb(80, 220, 100) : Color.FromArgb(220, 80, 80);
        }

        #region Domestic Expiration & Strike Synchronization

        public void RefreshDomesticExpirationsAndStrikes()
        {
            if (rbTXO.Checked)
            {
                RefreshDomesticOptionExpirations();
            }
            else
            {
                string prod = rbTMF.Checked ? "TMF" : (rbMXF.Checked ? "MXF" : "TXF");
                RefreshDomesticFutureExpirations(prod);
            }
        }

        private void RefreshDomesticOptionExpirations()
        {
            var chains = _marketDataService?.OptionChains ?? _marketDataService?.QuoteService?.OptionChains;
            string? currentSelectedYM = cboDomYM.SelectedItem?.ToString();

            cboDomYM.Items.Clear();

            if (chains != null && chains.Count > 0)
            {
                foreach (var key in chains.Keys)
                {
                    cboDomYM.Items.Add(key);
                }

                if (!string.IsNullOrEmpty(currentSelectedYM) && cboDomYM.Items.Contains(currentSelectedYM))
                {
                    cboDomYM.SelectedItem = currentSelectedYM;
                }
                else if (cboDomYM.Items.Count > 0)
                {
                    cboDomYM.SelectedIndex = 0;
                }
            }
            else
            {
                DateTime now = DateTime.Now;
                cboDomYM.Items.Add($"TX4 {now:yyyy-MM-dd}");
                cboDomYM.Items.Add($"TXO {now:yyyy-MM-16}");
                cboDomYM.Items.Add($"TXO {now.AddMonths(1):yyyy-MM-21}");
                cboDomYM.SelectedIndex = 0;
            }

            // Call / Put
            if (cboDomCP.Items.Count == 0)
            {
                cboDomCP.Items.AddRange(new object[] { "Call", "Put" });
                cboDomCP.SelectedIndex = 0;
            }

            UpdateStrikesForSelectedExpiration();
        }

        private void RefreshDomesticFutureExpirations(string productId)
        {
            string? currentSelectedYM = cboDomYM.SelectedItem?.ToString();
            cboDomYM.Items.Clear();

            List<string> months = GetFuturesMonths(productId);
            foreach (var m in months)
            {
                cboDomYM.Items.Add(m);
            }

            if (!string.IsNullOrEmpty(currentSelectedYM) && cboDomYM.Items.Contains(currentSelectedYM))
            {
                cboDomYM.SelectedItem = currentSelectedYM;
            }
            else if (cboDomYM.Items.Count > 0)
            {
                cboDomYM.SelectedIndex = 0;
            }
        }

        private List<string> GetFuturesMonths(string productId)
        {
            var months = new List<string>();
            if (SymbolRepository.Instance.IsInitialized)
            {
                var contracts = SymbolRepository.Instance.GetContractsByProductId(productId);
                if (contracts.Count > 0)
                {
                    months = contracts.Select(c => c.Month).Where(m => !string.IsNullOrEmpty(m)).Distinct().OrderBy(m => m).ToList();
                }
            }

            if (months.Count == 0)
            {
                DateTime now = DateTime.Today;
                months.Add(now.ToString("yyyyMM"));
                months.Add(now.AddMonths(1).ToString("yyyyMM"));

                DateTime cursor = now.AddMonths(2);
                int addedQuarterly = 0;
                while (addedQuarterly < 3)
                {
                    if (cursor.Month == 3 || cursor.Month == 6 || cursor.Month == 9 || cursor.Month == 12)
                    {
                        string qm = cursor.ToString("yyyyMM");
                        if (!months.Contains(qm))
                        {
                            months.Add(qm);
                            addedQuarterly++;
                        }
                    }
                    cursor = cursor.AddMonths(1);
                }
            }
            return months;
        }

        private void UpdateStrikesForSelectedExpiration()
        {
            if (!rbTXO.Checked) return;

            string selectedYM = cboDomYM.SelectedItem?.ToString() ?? cboDomYM.Text;
            var chains = _marketDataService?.OptionChains ?? _marketDataService?.QuoteService?.OptionChains;

            if (chains != null && chains.TryGetValue(selectedYM, out OptionChain? chain) && chain != null)
            {
                var strikes = chain.Calls.Select(c => c.StrikePrice)
                                        .Union(chain.Puts.Select(p => p.StrikePrice))
                                        .OrderBy(s => s)
                                        .ToList();

                if (strikes.Count > 0)
                {
                    string? currentStrike = cboDomStrike.SelectedItem?.ToString() ?? cboDomStrike.Text;
                    cboDomStrike.Items.Clear();
                    foreach (var s in strikes)
                    {
                        cboDomStrike.Items.Add(s.ToString("F0"));
                    }

                    if (!string.IsNullOrEmpty(currentStrike) && cboDomStrike.Items.Contains(currentStrike))
                    {
                        cboDomStrike.SelectedItem = currentStrike;
                    }
                    else
                    {
                        double target = chain.UnderlyingPrice > 0 ? chain.UnderlyingPrice : strikes[strikes.Count / 2];
                        var atm = strikes.OrderBy(s => Math.Abs(s - target)).First();
                        cboDomStrike.SelectedItem = atm.ToString("F0");
                    }
                    return;
                }
            }

            // Fallback default strikes
            if (cboDomStrike.Items.Count == 0)
            {
                for (int p = 18000; p <= 26000; p += 100)
                {
                    cboDomStrike.Items.Add(p.ToString());
                }
                cboDomStrike.SelectedItem = "22200";
            }
        }

        #endregion

        private void InitForeignCombos()
        {
            cboForExch.Items.Clear();
            cboForExch.Items.AddRange(new object[] { "CME", "CBOT", "SGX", "HKEX", "ICE", "OSE", "KRX" });
            cboForExch.SelectedIndex = 0;

            cboForYM.Items.Clear();
            DateTime now = DateTime.Now;
            for (int i = 0; i < 12; i++)
            {
                cboForYM.Items.Add(now.AddMonths(i).ToString("yyyyMM"));
            }
            cboForYM.SelectedIndex = 0;
        }

        private void InitConditionCombos()
        {
            cboPriceType.Items.Clear();
            cboPriceType.Items.AddRange(new object[] { "限價 (LMT)", "市價 (MKT)", "一定範圍市價" });
            cboPriceType.SelectedIndex = 0;

            cboTIF.Items.Clear();
            cboTIF.Items.AddRange(new object[] { "ROD", "IOC", "FOK" });
            cboTIF.SelectedIndex = 0;

            cboPosEffect.Items.Clear();
            cboPosEffect.Items.AddRange(new object[] { "自動 (Auto)", "新倉 (Open)", "平倉 (Close)", "當沖 (DayTrade)" });
            cboPosEffect.SelectedIndex = 0;
        }

        #region Market & Product Switching

        private void MarketSwitch_CheckedChanged(object sender, EventArgs e)
        {
            pnlDomestic.Visible = rbDomestic.Checked;
            pnlForeign.Visible = rbForeign.Checked;

            if (rbDomestic.Checked)
            {
                nudPrice.DecimalPlaces = 1;
                nudPrice.Increment = 1.0M;
            }
            else
            {
                nudPrice.DecimalPlaces = 2;
                nudPrice.Increment = 0.25M;
            }

            UpdateSymbolDisplay();
        }

        private void DomProduct_CheckedChanged(object sender, EventArgs e)
        {
            bool isOption = rbTXO.Checked;
            lblDomCP.Visible = isOption;
            cboDomCP.Visible = isOption;
            lblDomStrike.Visible = isOption;
            cboDomStrike.Visible = isOption;

            RefreshDomesticExpirationsAndStrikes();
            UpdateSymbolDisplay();
        }

        private void DomInputs_Changed(object sender, EventArgs e)
        {
            if (sender == cboDomYM && rbTXO.Checked)
            {
                UpdateStrikesForSelectedExpiration();
            }
            UpdateSymbolDisplay();
        }

        private void ForInputs_Changed(object sender, EventArgs e)
        {
            string exch = cboForExch.SelectedItem?.ToString() ?? "CME";
            if (sender == cboForExch)
            {
                cboForSymbol.Items.Clear();
                if (_foreignExchSymbols.TryGetValue(exch, out var symbols))
                {
                    cboForSymbol.Items.AddRange(symbols.ToArray());
                    cboForSymbol.SelectedIndex = 0;
                }
            }

            UpdateSymbolDisplay();
        }

        private void PriceType_SelectedIndexChanged(object sender, EventArgs e)
        {
            bool isMarket = (cboPriceType.SelectedIndex == 1 || cboPriceType.SelectedIndex == 2);
            nudPrice.Enabled = !isMarket;
            if (isMarket)
            {
                nudPrice.Value = 0;
            }
        }

        #region TAIFEX Symbol Encoding Logic

        public static char GetTaifexMonthCode(int month, bool isCall)
        {
            int m = Math.Clamp(month, 1, 12);
            return isCall ? (char)('A' + (m - 1)) : (char)('M' + (m - 1));
        }

        private void UpdateSymbolDisplay()
        {
            if (rbDomestic.Checked)
            {
                string rawYM = cboDomYM.SelectedItem?.ToString() ?? cboDomYM.Text;
                string pid = "TXO";
                int year = DateTime.Now.Year;
                int month = DateTime.Now.Month;

                ParseExpirationKey(rawYM, ref pid, ref year, ref month);

                if (rbTXF.Checked)
                {
                    char mCode = GetTaifexMonthCode(month, true);
                    char yDigit = (char)('0' + (year % 10));
                    txtDomSymbol.Text = $"TXF{mCode}{yDigit}";
                }
                else if (rbMXF.Checked)
                {
                    char mCode = GetTaifexMonthCode(month, true);
                    char yDigit = (char)('0' + (year % 10));
                    txtDomSymbol.Text = $"MXF{mCode}{yDigit}";
                }
                else if (rbTMF.Checked)
                {
                    char mCode = GetTaifexMonthCode(month, true);
                    char yDigit = (char)('0' + (year % 10));
                    txtDomSymbol.Text = $"TMF{mCode}{yDigit}";
                }
                else
                {
                        // Option TXO / TX1 / TX2 / TX4 / TX5
                        bool isCall = (cboDomCP.SelectedItem?.ToString() == "Call");
                        string strikeText = cboDomStrike.SelectedItem?.ToString() ?? cboDomStrike.Text;
                        double.TryParse(strikeText, out double strikePrice);

                        // 1. Try to find the exact symbol from OptionChain directly
                        var chains = _marketDataService?.OptionChains ?? _marketDataService?.QuoteService?.OptionChains;
                        if (chains != null && chains.TryGetValue(rawYM, out OptionChain? chain) && chain != null)
                        {
                            var optList = isCall ? chain.Calls : chain.Puts;
                            var matchedOpt = optList.FirstOrDefault(o => Math.Abs(o.StrikePrice - strikePrice) < 0.01);
                            if (matchedOpt != null && !string.IsNullOrEmpty(matchedOpt.Symbol))
                            {
                                txtDomSymbol.Text = matchedOpt.Symbol;
                                return;
                            }
                        }

                        // 2. Synthesize using TAIFEX standard formula:
                        int strikeInt = (int)Math.Round(strikePrice);
                        string strike5 = strikeInt.ToString("D5");
                        char monthChar = GetTaifexMonthCode(month, isCall);
                        char yearDigit = (char)('0' + (year % 10));

                    txtDomSymbol.Text = $"{pid}{strike5}{monthChar}{yearDigit}";
                }
            }
            else
            {
                string exch = cboForExch.SelectedItem?.ToString() ?? "CME";
                string rawSym = cboForSymbol.SelectedItem?.ToString() ?? cboForSymbol.Text;
                string cleanSym = rawSym.Split('-')[0].Trim();
                string ym = cboForYM.SelectedItem?.ToString() ?? cboForYM.Text;
                txtForFullCode.Text = $"{exch}-{cleanSym}-{ym}";
            }
        }

        private static void ParseExpirationKey(string rawYM, ref string pid, ref int year, ref int month)
        {
            if (string.IsNullOrWhiteSpace(rawYM)) return;

            // Pattern 1: "TX4 2026-08-26" or "TXO 2026-09-16"
            var parts = rawYM.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                pid = parts[0].ToUpper();
                if (DateTime.TryParse(parts[1], out DateTime dt))
                {
                    year = dt.Year;
                    month = dt.Month;
                    return;
                }
            }

            // Pattern 2: "202608W4" -> Year=2026, Month=8, Pid=TX4
            var matchW = Regex.Match(rawYM, @"^(\d{4})(\d{2})W(\d)$", RegexOptions.IgnoreCase);
            if (matchW.Success)
            {
                year = int.Parse(matchW.Groups[1].Value);
                month = int.Parse(matchW.Groups[2].Value);
                string wNum = matchW.Groups[3].Value;
                pid = $"TX{wNum}";
                return;
            }

            // Pattern 3: "202609" -> Year=2026, Month=9
            var matchM = Regex.Match(rawYM, @"^(\d{4})(\d{2})");
            if (matchM.Success)
            {
                year = int.Parse(matchM.Groups[1].Value);
                month = int.Parse(matchM.Groups[2].Value);
                return;
            }
        }

        #endregion

        #endregion

        #region Quick Price & Quantity Buttons

        private void btnPriceMarket_Click(object? sender, EventArgs e)
        {
            cboPriceType.SelectedIndex = 1; // 市價
        }

        private void btnQtyMinus1_Click(object? sender, EventArgs e)
        {
            if (nudQty.Value > 1) nudQty.Value -= 1;
        }

        private void btnQtyPlus1_Click(object? sender, EventArgs e)
        {
            if (nudQty.Value < 999) nudQty.Value += 1;
        }

        private void btnQtyPlus5_Click(object? sender, EventArgs e)
        {
            nudQty.Value = Math.Min(999, nudQty.Value + 5);
        }

        private void btnQtyPlus10_Click(object? sender, EventArgs e)
        {
            nudQty.Value = Math.Min(999, nudQty.Value + 10);
        }

        private void QuickPrice_Click(object? sender, EventArgs e)
        {
            cboPriceType.SelectedIndex = 0; // Set to Limit Price

            Button? btn = sender as Button;
            if (btn == null) return;

            string symbol = rbDomestic.Checked ? txtDomSymbol.Text.Trim().ToUpper() : txtForFullCode.Text.Trim().ToUpper();
            if (string.IsNullOrEmpty(symbol))
            {
                lblMsg.Text = "狀態: 請先輸入或選擇商品代碼！";
                lblMsg.ForeColor = Color.Orange;
                return;
            }

            double price = 0;
            var quoteSvc = _marketDataService?.QuoteService;

            // 1. 優先從 MarketDataStruct (h_MData) 取得
            var md = quoteSvc?.GetMarketData(symbol);
            if (md == null && !symbol.StartsWith("TW."))
            {
                md = quoteSvc?.GetMarketData("TW." + symbol);
            }

            double bestBid = 0;
            double bestAsk = 0;
            double matchPrice = 0;

            if (md != null)
            {
                if (md.BuyPx != null && md.BuyPx.Length > 0 && md.BuyPx[0] > 0)
                    bestBid = (double)md.BuyPx[0];
                if (md.SellPx != null && md.SellPx.Length > 0 && md.SellPx[0] > 0)
                    bestAsk = (double)md.SellPx[0];
                if (md.MatchcPx > 0)
                    matchPrice = (double)md.MatchcPx;
                else if (md.LastPx > 0)
                    matchPrice = (double)md.LastPx;
            }

            // 2. 備援從 OptionChains 或即時報價快照中搜尋
            if (bestBid == 0 && bestAsk == 0 && matchPrice == 0 && quoteSvc != null)
            {
                var chains = quoteSvc.OptionChains;
                if (chains != null)
                {
                    foreach (var chain in chains.Values)
                    {
                        var opt = chain.Calls.Concat(chain.Puts).FirstOrDefault(o => o.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase));
                        if (opt != null)
                        {
                            bestBid = opt.Bid;
                            bestAsk = opt.Ask;
                            matchPrice = opt.LastPrice;
                            break;
                        }
                    }
                }
            }

            string priceTypeName = "";
            if (btn == btnPriceOpponent)
            {
                priceTypeName = "對手價";
                // 對手價：買進看賣價(Ask)，賣出看買價(Bid)。預設取 Ask，若無 Ask 則取 Bid
                price = (bestAsk > 0) ? bestAsk : bestBid;
            }
            else if (btn == btnPriceSame)
            {
                priceTypeName = "本方價";
                // 本方價：買進看買價(Bid)，賣出看賣價(Ask)。預設取 Bid，若無 Bid 則取 Ask
                price = (bestBid > 0) ? bestBid : bestAsk;
            }
            else if (btn == btnPriceMatch)
            {
                priceTypeName = "成交價";
                price = (matchPrice > 0) ? matchPrice : ((bestBid + bestAsk) > 0 ? (bestBid > 0 ? bestBid : bestAsk) : 0);
            }

            if (price > 0)
            {
                nudPrice.Value = (decimal)price;
                lblMsg.Text = $"狀態: 已從 QuoteServer 取得 [{symbol}] {priceTypeName}: {price}";
                lblMsg.ForeColor = Color.FromArgb(80, 220, 100);
            }
            else
            {
                // 若尚無報價，主動送出訂閱
                quoteSvc?.Subscribe(symbol);
                lblMsg.Text = $"狀態: 暫無 [{symbol}] {priceTypeName}，已向 QuoteServer 送出即時報價訂閱...";
                lblMsg.ForeColor = Color.Yellow;
            }
        }

        #endregion

        #region Order Execution

        private void BtnBuy_Click(object sender, EventArgs e)
        {
            ExecuteOrder(BuySell.Buy);
        }

        private void BtnSell_Click(object sender, EventArgs e)
        {
            ExecuteOrder(BuySell.Sell);
        }

        private void ExecuteOrder(BuySell side)
        {
            if (!_tradeService.IsLoggedIn)
            {
                MessageBox.Show("尚未登入交易伺服器，無法送出委託！", "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int qty = (int)nudQty.Value;
            if (qty <= 0)
            {
                MessageBox.Show("委託口數必須大於 0！", "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            bool isMarket = (cboPriceType.SelectedIndex == 1);
            double price = isMarket ? 0 : (double)nudPrice.Value;

            if (!isMarket && price <= 0)
            {
                MessageBox.Show("限價委託之價格必須大於 0！", "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            TimeInForce tif = cboTIF.SelectedIndex switch
            {
                1 => TimeInForce.IOC,
                2 => TimeInForce.FOK,
                _ => TimeInForce.ROD
            };

            PositionEffect pe = cboPosEffect.SelectedIndex switch
            {
                1 => PositionEffect.Open,
                2 => PositionEffect.Close,
                3 => PositionEffect.DayTrade,
                _ => PositionEffect.Auto
            };

            InstrumentKey instrument;
            string symbolText;

            if (rbDomestic.Checked)
            {
                symbolText = txtDomSymbol.Text.Trim().ToUpper();
                if (string.IsNullOrEmpty(symbolText))
                {
                    MessageBox.Show("請輸入或選擇商品代碼！", "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                bool isOption = rbTXO.Checked || symbolText.StartsWith("TXO") || symbolText.StartsWith("TX1") || symbolText.StartsWith("TX2") || symbolText.StartsWith("TX4") || symbolText.StartsWith("TX5");
                MarketType mType = isOption ? MarketType.DomesticOption : MarketType.DomesticFuture;
                instrument = new InstrumentKey
                {
                    Market = mType,
                    Exchange = "TAIFEX",
                    Symbol = symbolText,
                    ContractMonth = cboDomYM.SelectedItem?.ToString() ?? cboDomYM.Text
                };
            }
            else
            {
                string exch = cboForExch.SelectedItem?.ToString() ?? "CME";
                string rawSym = cboForSymbol.SelectedItem?.ToString() ?? cboForSymbol.Text;
                string cleanSym = rawSym.Split('-')[0].Trim();
                string ym = cboForYM.SelectedItem?.ToString() ?? cboForYM.Text;
                symbolText = txtForFullCode.Text.Trim();
                if (string.IsNullOrEmpty(symbolText))
                {
                    symbolText = $"{exch}-{cleanSym}-{ym}";
                }

                instrument = new InstrumentKey
                {
                    Market = MarketType.ForeignFuture,
                    Exchange = exch,
                    Symbol = cleanSym,
                    ContractMonth = ym
                };
            }

            string sideText = (side == BuySell.Buy) ? "買進 (BUY)" : "賣出 (SELL)";
            string priceText = isMarket ? "市價" : $"{price:F2}";

            if (chkConfirm.Checked)
            {
                string confirmMsg = $"【委託確認】\n\n" +
                                   $"市場/商品: {symbolText}\n" +
                                   $"買賣別: {sideText}\n" +
                                   $"委託價格: {priceText}\n" +
                                   $"委託口數: {qty} 口\n" +
                                   $"委託條件: {tif}, {pe}\n\n" +
                                   $"確定要送出此筆委託嗎？";

                if (MessageBox.Show(confirmMsg, "下單確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    return;
                }
            }

            var request = new OrderRequest
            {
                Instrument = instrument,
                BuySell = side,
                Price = price,
                Quantity = qty,
                TimeInForce = tif,
                PositionEffect = pe,
                BrokerId = _tradeService.BrokerID,
                Account = _tradeService.Account
            };

            lblMsg.Text = $"狀態: 正在送出 {symbolText} {sideText} {qty}口...";
            lblMsg.ForeColor = Color.Yellow;

            try
            {
                _orderManager.PlaceOrder(request);
            }
            catch (Exception ex)
            {
                lblMsg.Text = $"狀態: 送單發生錯誤: {ex.Message}";
                lblMsg.ForeColor = Color.Red;
            }
        }

        private void OrderManager_OnOrderStateChanged(OrderInfo order)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => OrderManager_OnOrderStateChanged(order)));
                return;
            }

            if (order.Status == OrderStatus.Accepted || order.Status == OrderStatus.Submitted || order.Status == OrderStatus.PartiallyFilled || order.Status == OrderStatus.Filled)
            {
                lblMsg.Text = $"狀態: 委託更新 [{order.Status}]！單號: {order.Identity?.OrdNo ?? order.Identity?.Cnt} ({order.Instrument?.Symbol} {order.BuySell} {order.OrderQty}口 @ {order.OrderPrice})";
                lblMsg.ForeColor = Color.FromArgb(80, 220, 100);
            }
        }

        private void OrderManager_OnOrderFailed(OrderInfo order, string reason)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => OrderManager_OnOrderFailed(order, reason)));
                return;
            }

            lblMsg.Text = $"狀態: 委託被拒絕: {reason}";
            lblMsg.ForeColor = Color.Red;
        }

        private void BtnOpenReport_Click(object sender, EventArgs e)
        {
            _mainForm?.OpenOrderReportForm();
        }

        #endregion

        #region Public Linking APIs

        public void SetDomesticOption(string symbol, string? month = null, string? callPut = null, double? strike = null, BuySell? bs = null, double? price = null)
        {
            rbDomestic.Checked = true;
            rbTXO.Checked = true;

            RefreshDomesticExpirationsAndStrikes();

            if (!string.IsNullOrEmpty(month))
            {
                var matchKey = cboDomYM.Items.Cast<string>().FirstOrDefault(k => k.Contains(month));
                if (matchKey != null)
                {
                    cboDomYM.SelectedItem = matchKey;
                }
                else
                {
                    if (!cboDomYM.Items.Contains(month)) cboDomYM.Items.Add(month);
                    cboDomYM.SelectedItem = month;
                }
            }

            if (!string.IsNullOrEmpty(callPut))
            {
                cboDomCP.SelectedItem = callPut.StartsWith("C", StringComparison.OrdinalIgnoreCase) ? "Call" : "Put";
            }

            if (strike.HasValue)
            {
                string sStr = ((int)strike.Value).ToString();
                if (!cboDomStrike.Items.Contains(sStr)) cboDomStrike.Items.Add(sStr);
                cboDomStrike.SelectedItem = sStr;
            }

            if (price.HasValue && price.Value > 0)
            {
                cboPriceType.SelectedIndex = 0;
                nudPrice.Value = (decimal)price.Value;
            }

            UpdateSymbolDisplay();
            if (!string.IsNullOrEmpty(symbol))
            {
                txtDomSymbol.Text = symbol;
            }

            Show();
            BringToFront();
        }

        public void SetDomesticFuture(string prod = "TXF", string? month = null, BuySell? bs = null, double? price = null)
        {
            rbDomestic.Checked = true;
            if (prod == "MXF") rbMXF.Checked = true;
            else if (prod == "TMF") rbTMF.Checked = true;
            else rbTXF.Checked = true;

            RefreshDomesticExpirationsAndStrikes();

            if (!string.IsNullOrEmpty(month))
            {
                if (!cboDomYM.Items.Contains(month)) cboDomYM.Items.Add(month);
                cboDomYM.SelectedItem = month;
            }

            if (price.HasValue && price.Value > 0)
            {
                cboPriceType.SelectedIndex = 0;
                nudPrice.Value = (decimal)price.Value;
            }

            UpdateSymbolDisplay();
            Show();
            BringToFront();
        }

        public void SetForeignFuture(string exchange, string symbol, string month, BuySell? bs = null, double? price = null)
        {
            rbForeign.Checked = true;

            if (!cboForExch.Items.Contains(exchange)) cboForExch.Items.Add(exchange);
            cboForExch.SelectedItem = exchange;

            cboForSymbol.Text = symbol;

            if (!cboForYM.Items.Contains(month)) cboForYM.Items.Add(month);
            cboForYM.SelectedItem = month;

            if (price.HasValue && price.Value > 0)
            {
                cboPriceType.SelectedIndex = 0;
                nudPrice.Value = (decimal)price.Value;
            }

            UpdateSymbolDisplay();
            Show();
            BringToFront();
        }

        #endregion
    }
}
