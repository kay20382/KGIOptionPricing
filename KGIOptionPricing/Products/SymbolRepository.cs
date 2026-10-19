using Intelligence;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Package;
using System.Reflection;
using static Package.PT01805;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading;
using System.Windows.Forms;

namespace KGIOptionPricing
{
    public class SymbolRepository
    {
        #region Field
        private Dictionary<string, ContractInfo> _symbolIndex { get; set; } = new();
        private ILookup<string, ContractInfo> _productIndex { get; set; } = Enumerable.Empty<ContractInfo>().ToLookup(x => x.ProductId, StringComparer.OrdinalIgnoreCase);
        private IReadOnlyDictionary<string, HotMonthContractDateInput> _hotMonthDateInputs =
            new Dictionary<string, HotMonthContractDateInput>(StringComparer.OrdinalIgnoreCase);
        private int _isInitialized;
        public bool IsInitialized => Volatile.Read(ref _isInitialized) == 1;

        // 1. 使用 Lazy<T> 確保只會實例化一次，且保證多執行緒安全
        private static readonly Lazy<SymbolRepository> _instance =
            new Lazy<SymbolRepository>(() => new SymbolRepository());

        // 2. 開放一個公開的靜態屬性，讓外部取得這唯一實體
        public static SymbolRepository Instance => _instance.Value;
        #endregion

        public void Clear()
        {
            _symbolIndex = new Dictionary<string, ContractInfo>();
            _productIndex = Enumerable.Empty<ContractInfo>().ToLookup(x => x.ProductId, StringComparer.OrdinalIgnoreCase);
            Volatile.Write(ref _isInitialized, 0);
        }

        public void SetHotMonthContractDates(IEnumerable<HotMonthContractDateInput>? inputs)
        {
            _hotMonthDateInputs = (inputs ?? Array.Empty<HotMonthContractDateInput>())
                .Where(input => !string.IsNullOrWhiteSpace(input.ProductID) && !string.IsNullOrWhiteSpace(input.ContractMonth))
                .GroupBy(input => DateInputKey(input.ProductID, input.ContractMonth), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        }

        #region Method
        private SymbolRepository()
        {
            // 這裡可以放一些一開始的初始化設定
        }
        /// <summary>
        /// 一鍵載入國內外所有商品
        /// </summary>
        public bool LoadAllProducts(dynamic quote) // 請替換為真實的 QuoteCom 型別
        {
            var allContracts = new List<ContractInfo>();

            try
            {
                // 1. 載入國內商品 (沿用之前的邏輯)
                LoadDomesticProducts(quote, allContracts);

                // 2. 載入國外商品 (新邏輯)
                LoadForeignProducts(quote, allContracts);

                ApplyHotMonthContractDates(allContracts);

                // 3. 建立統一的雙重索引
                BuildInternalIndexes(allContracts);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                return false;
            }
        }
        /// <summary>
        /// 載入海期與海選商品
        /// </summary>
        private void LoadForeignProducts(dynamic quote, List<ContractInfo> allContracts)
        {
            //TODO 建立外期基本資料，TICK、LastPx,ContValue...
            try
            {
                // <Exchange,ProductID,<>>
                Dictionary<string, Tuple<string, string>> h_Name = new Dictionary<string, Tuple<string, string>>();
                List<string> lst_FProd = quote.GetProductBaseList();//Get CME,ES,小SP指數
                foreach (var item in lst_FProd)
                {
                    string[] split = item.Split(',');
                    if(split.Length > 2)
                    {
                        string[] split2 = split[2].Split('_');
                        if (split2.Length == 2)
                        {
                            if (h_Name.ContainsKey(split[1]) == false)
                                h_Name.Add(split[1], new Tuple<string, string>(split[0], split2[1]));
                        }
                        else
                        {
                            if (split[2].StartsWith(split[1]))
                            {
                                string ComCName = split[2].Substring(split[1].Length, split[2].Length - split[1].Length);
                                if (h_Name.ContainsKey(split[1]) == false)
                                    h_Name.Add(split[1], new Tuple<string, string>(split[0], ComCName));
                            }
                            else
                            {
                                if (h_Name.ContainsKey(split[1]) == false)
                                    h_Name.Add(split[1], new Tuple<string, string>(split[0], split[2]));
                            }
                        }
                    }
                }
                var exchanges = Enum.GetValues(typeof(MARKET_EXCHANGE)).Cast<MARKET_EXCHANGE>();
                var productBaseCache = new Dictionary<(MARKET_EXCHANGE Exchange, MARKET_FLAG Flag, string ProductId), PT01805?>();

                List<PI40003> GetForeignProductList(MARKET_EXCHANGE exchange, MARKET_FLAG flag)
                {
                    var products = new Dictionary<string, PI40003>(StringComparer.OrdinalIgnoreCase);
                    foreach (FOREIGN_SESSION session in Enum.GetValues(typeof(FOREIGN_SESSION)).Cast<FOREIGN_SESSION>())
                    {
                        try
                        {
                            List<PI40003>? sessionProducts = quote.GetProductList(exchange, flag, session);
                            if (sessionProducts is null) continue;
                            foreach (PI40003 product in sessionProducts)
                            {
                                if (product is null || string.IsNullOrWhiteSpace(product.Symbol) || string.IsNullOrWhiteSpace(product.Comid)) continue;
                                products.TryAdd(product.Symbol, product);
                            }
                        }
                        catch (Exception ex)
                        {
                            // 部分交易所不支援所有 session；單一查詢失敗不能中止後續 JPX 載入。
                            Debug.WriteLine($"GetProductList({exchange}, {flag}, {session}) failed: {ex.Message}");
                        }
                    }
                    return products.Values.ToList();
                }

                PT01805? GetProductBaseCached(MARKET_EXCHANGE exchange, MARKET_FLAG flag, PI40003 product)
                {
                    string productKey = !string.IsNullOrWhiteSpace(product.Comid)
                        ? product.Comid
                        : product.Symbol ?? string.Empty;
                    var key = (exchange, flag, productKey);
                    if (!productBaseCache.TryGetValue(key, out PT01805? productBase))
                    {
                        try
                        {
                            productBase = quote.GetProductBase(exchange, flag, product.Symbol);
                        }
                        catch (Exception ex)
                        {
                            productBase = null;
                            Debug.WriteLine($"GetProductBase({exchange}, {flag}, {product.Symbol}) failed: {ex.Message}");
                        }
                        productBaseCache[key] = productBase;
                    }

                    return productBase;
                }

                // 期貨
                foreach (var exchange in exchanges)
                {
                    // 請求下載期貨與選擇權
                    List<PI40003> futResult = GetForeignProductList(exchange, MARKET_FLAG.MF_FUT);
                    if (futResult.Count == 0) continue;

                    foreach (PI40003 p40003 in futResult)
                    {
                        PT01805? p001805 = GetProductBaseCached(exchange, MARKET_FLAG.MF_FUT, p40003);
                        string producName = h_Name.ContainsKey(p40003.Comid) ? h_Name[p40003.Comid].Item2 : "";
                      
                         Debug.WriteLine($"{exchange.ToString()},{p40003.Symbol ?? ""},{p40003.Comid},{producName},{p001805.ContractValue}");
                         var contract = new ContractInfo
                        {
                            Symbol = p40003.Symbol ?? "",                // 完整20碼交易代碼 (如: TXFB6, TXO08900C1)
                            ProductId = p40003.Comid.Trim(),                // 群組代碼 (如: TXF)
                            ProductName = producName, // 中文名稱 (如: 台指期)
                            SymbolEName = p40003.Comid,
                            SymbolCName = producName,
                            Exchange = (MARKET_EXCHANGE)exchange,                         // 🔥 標註為台灣期交所
                            ExchangeNm = p40003.ExchangeNm,
                            ComType = p40003.ComType,
                            PxDecimal = GetPxDecimal(p40003.Comid, p001805),
                            StrikePxDecimal = 0,
                            TaxRate = 0,
                            TickSize = 1,
                            EndDate = ResolveForeignExpiryDate(p40003.ExpiredDate, p40003.LastTradeDate),
                            LastTradeDate = ResolveForeignExpiryDate(p40003.ExpiredDate, p40003.LastTradeDate),
                            ExpiryDate = ResolveForeignExpiryDate(p40003.ExpiredDate, p40003.LastTradeDate),
                            FallPx = 0,
                            RisePx = 0,
                            ContractValue = GetContValue(p40003.Comid, p001805),
                            Hot = false,
                            StrikePx = 0m,
                            CPF = CPFEnum.Futures,
                            ComYM = p40003.ComYM,
                            ComYM2 = p40003.ComYM2,
                            CP = p40003.CP,
                            PreClosePx = p40003.ClosePrice,
                            TickType = PriceTick.getTickType(p40003.Comid),
                            CharMonth = (!string.IsNullOrEmpty(p40003.Symbol) && p40003.Symbol.Length >= 2)
                                           ? p40003.Symbol.Substring(p40003.Symbol.Length - 2, 2)
                                           : "",
                            // 月份處理 (通常 EndDate 格式為 YYYYMMDD，我們取前 6 碼 YYYYMM)
                            Month = (p40003.ExpiredDate != null && p40003.ExpiredDate.Length >= 6)
                                           ? p40003.ExpiredDate.Substring(0, 6)
                                           : "",
                        };
                        allContracts.Add(contract);
                    }
                }
                // 選擇權
                foreach (var exchange in exchanges)
                {
                    // 請求下載選擇權
                    List<PI40003> optResult = GetForeignProductList(exchange, MARKET_FLAG.MF_OPT);
                    if (optResult.Count == 0) continue;
                    foreach (PI40003 p40003 in optResult)
                    {

                        PT01805? p001805 = GetProductBaseCached(exchange, MARKET_FLAG.MF_OPT, p40003);
                        string producName = h_Name.ContainsKey(p40003.Comid) ? h_Name[p40003.Comid].Item2 : "";

                        //double lastPx = quote.RetriveLastPrice(exchange, MARKET_FLAG.MF_OPT, FOREIGN_SESSION.FS_EC, p40003.Symbol);
                        var contract = new ContractInfo
                        {
                            Symbol = p40003.Symbol ?? "",                // 完整20碼交易代碼 (如: TXFB6, TXO08900C1)
                            ProductId = p40003.Comid.Trim(),                // 群組代碼 (如: TXF)
                            ProductName = producName, // 中文名稱 (如: 台指期)
                            SymbolEName = p40003.Comid,
                            SymbolCName = producName,
                            Exchange = (MARKET_EXCHANGE)exchange,                         // 🔥 標註為台灣期交所
                            ExchangeNm = p40003.ExchangeNm,
                            ComType = p40003.ComType,
                            PxDecimal = GetPxDecimal(p40003.Comid, p001805),
                            StrikePxDecimal = 0,
                            TaxRate = 0,
                            TickSize = 1,
                            EndDate = ResolveForeignExpiryDate(p40003.ExpiredDate, p40003.LastTradeDate),
                            LastTradeDate = ResolveForeignExpiryDate(p40003.ExpiredDate, p40003.LastTradeDate),
                            ExpiryDate = ResolveForeignExpiryDate(p40003.ExpiredDate, p40003.LastTradeDate),
                            FallPx = 0,
                            RisePx = 0,
                            ContractValue = GetContValue(p40003.Comid, p001805),
                            Hot = false,
                            StrikePx = SafeDecimal(p40003.SPrice),
                            CPF = GetForeignOptionCpf(p40003.CP),
                            ComYM = p40003.ComYM,
                            ComYM2 = p40003.ComYM2,
                            CP = p40003.CP,
                            PreClosePx = p40003.ClosePrice,
                            TickType = PriceTick.getTickType(p40003.Comid),
                            CharMonth = (!string.IsNullOrEmpty(p40003.Symbol) && p40003.Symbol.Length >= 2)
                                           ? p40003.Symbol.Substring(p40003.Symbol.Length - 2, 2)
                                           : "",
                            // 月份處理 (通常 EndDate 格式為 YYYYMMDD，我們取前 6 碼 YYYYMM)
                            Month = (p40003.ExpiredDate != null && p40003.ExpiredDate.Length >= 6)
                                           ? p40003.ExpiredDate.Substring(0, 6)
                                           : "",
                        };
                        allContracts.Add(contract);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
       
            }
      
        }
        /// <summary>
        /// 載入國內商品 (我們上一篇寫的邏輯，搬到這裡)
        /// </summary>
        private void LoadDomesticProducts(dynamic quote, List<ContractInfo> allContracts)
        {
          

            // 1. 取得國內期貨與選擇權的 ProductID 清單
            // 注意：請根據你實際的 Enum 名稱替換 MARKET_FLAG.MF_FUT
            var futList = quote.GetTaifexProductList(MARKET_FLAG.MF_FUT) as List<string> ?? new List<string>();
            var optList = quote.GetTaifexProductList(MARKET_FLAG.MF_OPT) as List<string> ?? new List<string>();

            // 合併清單並去重 (例如 TXF, TXO, MXF 等)
            var allProductIds = futList.Concat(optList).Distinct().ToList();

            foreach (var fullProdId in allProductIds)
            {
               
                // 台灣商品的 ProductId 通常是前 3 碼 (例如: TXF, MTX, TXO)
                string productId = fullProdId.Length >= 3 ? fullProdId.Substring(0, 3) : fullProdId;
                string productName = (!string.IsNullOrEmpty(fullProdId) && fullProdId.Length > 3)
                                    ? fullProdId.Substring(3, fullProdId.Length - 3)
                                    : "";
            
                // 2. 取得商品基本資料 PT01805 (包含：跳動點、契約乘數、中文名稱)
                PT01805 pt01805 = quote.GetTaifexProductBase(productId);
                if (pt01805 == null) continue; // 如果查無基本資料，跳過換下一個

                // 3. 取得該商品底下的所有可交易合約明細 PT01802 (包含：月份、各履約價代碼)
                var detailList = quote.GetTaifexProductDetailList(productId);
                if (detailList == null) continue;
          
               
                foreach (PT01802 pt01802 in detailList)
                {
                    // 4. 取得單一合約的詳細屬性 BasicSymbol (用來拿履約價與 Call/Put)
                    // 註：有些版本的 QuoteCom 是傳入 ComId 字串查詢，請依據你的 API 簽章調整
                    // var basicSymbol = quote.GetBasicSymbol(pt01802.ComId);
                    if (pt01802.ComId != null && pt01802.ComId.StartsWith("TMF"))
                        Console.WriteLine("");
                    // 5. 將所有分散的資料聚合為統一的 ContractInfo 模型
                    var contract = new ContractInfo
                    {
                        Symbol = pt01802.ComId ?? "",                // 完整20碼交易代碼 (如: TXFB6, TXO08900C1)
                        ProductId = pt01805.ComId,                       // 群組代碼 (如: TXF)
                        ProductName = productName, // 中文名稱 (如: 台指期)
                        SymbolEName = pt01805.ComEName ?? "",
                        SymbolCName = pt01805.ComCName ?? "",
                        //Exchange = pt01805.Exchange,                         // 🔥 標註為台灣期交所
                        ExchangeNm = pt01805.Exchange,
                        ComType = pt01805.ComType,
                        PxDecimal = GetPxDecimal(pt01805.ComId, pt01805),
                        StrikePxDecimal = pt01805.StkPriceDecimal,
                        TaxRate = pt01805.TaxRate,
                        TickSize = pt01805.Tick,
                        EndDate = pt01802.EndDate ?? "",
                        LastTradeDate = pt01802.EndDate ?? "",
                        ExpiryDate = pt01802.EndDate ?? "",
                        FallPx = pt01802.FallPrice,
                        RisePx = pt01802.RisePrice,
                        ContractValue = GetContValue(pt01802.ComId ?? "", pt01805),
                        Hot = pt01802.Hot,
                        StrikePx = GetStrikePx(pt01802.ComId,pt01802.StkPriceDecimal),
                        TickType = (pt01805.ContractValue == 2000 || pt01805.ContractValue == 100) ? 
                                    TickTypeEnum.ttSTF :
                                    PriceTick.getTickType(pt01805.ComId),
                        CPF = GetCPF(pt01805.ComType, pt01802.ComId),
                        CharMonth = (!string.IsNullOrEmpty(pt01802.ComId) && pt01802.ComId.Length >= 2)
                                    ? pt01802.ComId.Substring(pt01802.ComId.Length - 2, 2)
                                    : "",
                        // 月份處理 (通常 EndDate 格式為 YYYYMMDD，我們取前 6 碼 YYYYMM)
                        Month = (pt01802.EndDate != null && pt01802.EndDate.Length >= 6)
                                    ? pt01802.EndDate.Substring(0, 6)
                                    : "",
                    };

                    allContracts.Add(contract);
                }
               //ExportToCsv(allContracts, ".\\TAIFEXCont.csv");
            }
            
        }
        /// <summary>
        /// 將 P40003 傳回的交易所代碼 (0, 1, 2...) 轉為 KGI API 的 MARKET_EXCHANGE 列舉
        /// </summary>
        private MARKET_EXCHANGE GetMarketExchangeEnum(string exchangeCodeStr)
        {
            // 如果傳進來的是數字字串 (例如 "0")
            if (int.TryParse(exchangeCodeStr, out int exchangeInt))
            {
                // 直接強制轉型為 MARKET_EXCHANGE 列舉 (因為列舉底層其實就是 int)
                // 這樣 0 就會自動對應到 MARKET_EXCHANGE 的第 0 個項目
                return (MARKET_EXCHANGE)exchangeInt;
            }

            // 如果 API 傳來的不是數字，或是解析失敗，可以給一個預設值，或是印出 Log
            // 這裡假設 Enum 的預設值為 0
            return (MARKET_EXCHANGE)0;
        }
        // ToDo 不完整還需要修改
        private int GetPxDecimal(string productID, PT01805? productBase = null)
        {
            // QuoteCom PT01805.PriceDecimal is the authoritative display scale.
            // Some foreign products (for example TPXM) require fractional prices;
            // formatting those with the old default of zero made multiple ticks look identical.

          
            int rv = 0;
            switch (productID)
            {
                // 個股期
                case "CDF":
                case "CCF":
                    rv = 2;
                    break;
                case "SI":
                case "SIL":
                    rv = 3;
                    break;
                case "GC":
                case "MGC":
                    rv = 1;
                    break;
                case "TXF":
                case "MXF":
                case "TMF":
                    rv = 0;
                    break;
                case "TXO":
                case "TX1":
                case "TX2":
                case "TX4":
                case "TX5":
                case "TXU":
                case "TXV":
                case "TXX":
                case "TXY":
                case "TXZ":
                    rv = 2;
                    break;
                case "ES":
                case "E1C":
                case "E2C":
                case "E3C":
                case "E4C":
                    rv = 2;
                    break;
                case "CAU":
                    rv = 4;
                    break;
                default:
                    if (productBase != null && productBase.PriceDecimal >= 0)
                        rv = productBase.PriceDecimal;
                    else
                        rv = PriceTick.getPrcDigital(productID);
                    break;
            }
            return rv;
        }
        private void ExportToCsv(List<ContractInfo> contracts, string filePath)
        {
            if (contracts == null || contracts.Count == 0)
            {
                Console.WriteLine("沒有資料可匯出！");
                return;
            }

            try
            {
                // 🔥 老兵關鍵：new UTF8Encoding(true) 這裡的 true 代表產生 BOM，保證 Excel 開啟中文不亂碼！
                using StreamWriter sw = new StreamWriter(filePath, false, new UTF8Encoding(true));

                // 取得 ContractInfo 的所有屬性 (Properties)
                PropertyInfo[] properties = typeof(ContractInfo).GetProperties();

                // 1. 自動產生標題列 (Header)
                string header = string.Join(",", properties.Select(p => p.Name));
                sw.WriteLine(header);

                // 2. 逐行寫入資料列 (Data)
                foreach (var contract in contracts)
                {
                    var values = properties.Select(p =>
                    {
                        // 取得每個欄位的值
                        string value = p.GetValue(contract)?.ToString() ?? "";

                        // 🔥 防呆機制：如果中文名稱或資料裡面剛好有「逗號 (,)」或「雙引號 (")」
                        // 必須遵守 CSV 規範，將字串用雙引號包起來，否則欄位會錯位
                        if (value.Contains(",") || value.Contains("\""))
                        {
                            value = $"\"{value.Replace("\"", "\"\"")}\"";
                        }

                        return value;
                    });

                    // 將所有值用逗號串接並寫入
                    sw.WriteLine(string.Join(",", values));
                }

                Console.WriteLine($"[成功] 匯出 {contracts.Count} 筆商品至：{filePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[錯誤] 匯出 CSV 失敗：{ex.Message}");
            }
        }
        private CPFEnum GetCPF(char comType, string? comId)
        {
       
            if ( string.IsNullOrEmpty( comId ))
                return CPFEnum.None;

            if (comType == 'F')
            {
                return CPFEnum.Futures;
            }
            else
            {
                // 防禦性設計：字串長度不足 2，無法取倒數第二碼，避免 IndexOutOfRangeException
                if (comId.Length < 2)
                    return CPFEnum.None;

                // 3. 取得倒數第二碼 (使用 C# 8+ 現代語法 ^2，並轉為大寫以防萬一)
                char monthCode = char.ToUpper(comId[^2]);

                // 4. 判斷邏輯：如果字元大於 'L' (即 M~X)，就是 Put
                if (monthCode > 'L')
                {
                    return CPFEnum.Put;
                }
                else
                {
                    // A~L 就是 Call
                    return CPFEnum.Call;
                }
            }
        }

        /// <summary>
        /// 智能解析履約價 (支援台指選與個股選)
        /// </summary>
        /// <param name="comId">商品代碼，例如 "TXO20000C6" 或 "CZO08500C6"</param>
        /// <param name="stkPriceDecimal">履約價格小數位 (來自 PT01805.StkPriceDecimal)，預設為 0</param>
        /// <returns>真實的履約價</returns>
        private decimal GetStrikePx(string? comId, int stkPriceDecimal = 0)
        {
            // 1. 防呆：若是空字串或長度不足 (正常為10碼，至少也要大於5碼才有意義)，直接回傳 0
            if (string.IsNullOrEmpty(comId) || comId.Length < 6)
                return 0;

            try
            {
                // 2. 切割字串：跳過前 3 碼，並去掉最後 2 碼。
                // 例如 "TXO20000C6" -> "20000"
                // 例如 "CZO08500C6" -> "08500"
                string strikeStr = comId[3..^2];

                // 3. 嘗試轉為數字
                if (decimal.TryParse(strikeStr, out decimal rawStrike))
                {
                    // 4. 🔥 核心邏輯：還原個股選擇權的隱含小數點
                    if (stkPriceDecimal > 0)
                    {
                        // 如果小數位是 1，就除以 10 (10的1次方)
                        // 如果小數位是 2，就除以 100 (10的2次方)
                        return rawStrike / (decimal)Math.Pow(10, stkPriceDecimal);
                    }

                    // 若 stkPriceDecimal 為 0 (如台指選)，直接回傳
                    return rawStrike;
                }
            }
            catch
            {
                // 捕捉任何字串切割或轉換造成的極端異常，確保系統不崩潰
            }

            return 0; // 解析失敗則回傳 0
        }
        private decimal GetContValue(string productID, PT01805? productBase = null)
        {
            // KGI's product-base ContractValue for 225MC is currently incorrect.
            // Use the exchange contract rule: JPY 10 per index point (JPY 50 per 5-point tick).
            if (string.Equals(productID, "225MC", StringComparison.OrdinalIgnoreCase))
                return 10m;

            if (productBase != null && productBase.ContractValue > 0)
                return productBase.ContractValue;

            decimal rv = 1;
            switch (productID)
            {
                case "GC": rv = 100; break;
                case "MGC": rv = 10; break;
                case "SI": rv = 5000; break;
                case "SIL": rv = 1000; break;
                case "225":rv = 1000;break;
                case "225M": rv = 100; break;
                case "225MC": rv = 10; break;
                case "TPX": rv = 10000; break;
                case "TPXM": rv = 1000; break;
            }
            return rv;
        }
        // --- 其他輔助方法保留 ---
        private void BuildInternalIndexes(List<ContractInfo> contracts)
        {
            Dictionary<string, ContractInfo> symbolIndex = contracts
                .Where(c => !string.IsNullOrWhiteSpace(c.Symbol))
                .GroupBy(c => c.Symbol, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            ILookup<string, ContractInfo> productIndex = symbolIndex.Values.ToLookup(c => c.ProductId, StringComparer.OrdinalIgnoreCase);

            // 背景建立完成後才一次替換索引；讀取端不會看到半成品。
            _productIndex = productIndex;
            _symbolIndex = symbolIndex;
            Volatile.Write(ref _isInitialized, 1);
        }
        #endregion

        #region API
        // 查詢方法...
        public ContractInfo GetContract(string symbol)
        {
            Dictionary<string, ContractInfo> snapshot = _symbolIndex;
            return snapshot.TryGetValue(symbol, out var contract) ? contract : new ContractInfo();
        }

        public ContractInfo? GetContractByProductAndMonth(string productId, string month)
        {
            ILookup<string, ContractInfo> snapshot = _productIndex;
            return snapshot[productId].FirstOrDefault(c => c.Month == month);
        }

        public List<ContractInfo> GetContractsByProductId(string productId)
        {
            ILookup<string, ContractInfo> snapshot = _productIndex;
            return snapshot[productId].ToList();
        }

        private static CPFEnum GetForeignOptionCpf(string? cp)
        {
            return cp?.Trim().ToUpperInvariant() switch
            {
                "C" => CPFEnum.Call,
                "P" => CPFEnum.Put,
                _ => CPFEnum.None
            };
        }

        private static decimal SafeDecimal(object? value)
        {
            if (value is null) return 0m;
            try
            {
                return Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (Exception) when (value is string or IConvertible)
            {
                return 0m;
            }
        }

        private static string ResolveForeignExpiryDate(string? expiredDate, string? lastTradeDate)
        {
            foreach (string? value in new[] { expiredDate, lastTradeDate })
            {
                string digits = new((value ?? string.Empty).Where(char.IsDigit).ToArray());
                if (digits.Length < 8) continue;
                string dateText = digits.Substring(0, 8);
                if (DateTime.TryParseExact(
                    dateText,
                    "yyyyMMdd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out _))
                {
                    return dateText;
                }
            }

            return expiredDate?.Trim() ?? lastTradeDate?.Trim() ?? string.Empty;
        }

        /// <summary>依商品活躍月份與 FND/LTD/Expiry 換倉規則取得最近月期貨。</summary>
        public ContractInfo? GetHotMonthContract(string productId, DateTime? tradingDate = null)
        {
            ILookup<string, ContractInfo> snapshot = _productIndex;
            return SelectHotMonthContract(snapshot[productId], tradingDate ?? DateTime.Today);
        }

        public static ContractInfo? SelectHotMonthContract(IEnumerable<ContractInfo> contracts, DateTime tradingDate) =>
            HotMonthSelector.Select(contracts, tradingDate);

        private void ApplyHotMonthContractDates(List<ContractInfo> contracts)
        {
            IReadOnlyDictionary<string, HotMonthContractDateInput> inputs = _hotMonthDateInputs;
            foreach (ContractInfo contract in contracts)
            {
                string month = HotMonthSelector.ContractMonth(contract);
                if (!inputs.TryGetValue(DateInputKey(contract.ProductId, month), out HotMonthContractDateInput? input))
                    continue;

                contract.FirstNoticeDate = input.FirstNoticeDate.Trim();
                if (!string.IsNullOrWhiteSpace(input.LastTradeDate))
                    contract.LastTradeDate = input.LastTradeDate.Trim();
                if (!string.IsNullOrWhiteSpace(input.ExpiryDate))
                {
                    contract.ExpiryDate = input.ExpiryDate.Trim();
                    contract.EndDate = input.ExpiryDate.Trim();
                }
            }
        }

        private static string DateInputKey(string productId, string contractMonth) =>
            $"{(productId ?? string.Empty).Trim().ToUpperInvariant()}:{new string((contractMonth ?? string.Empty).Where(char.IsDigit).ToArray()).PadRight(6).Substring(0, 6)}";

        /// <summary>取得商品主檔的不可變快照，供商品選擇器建立完整樹狀目錄。</summary>
        public IReadOnlyList<ContractInfo> GetAllContracts()
        {
            Dictionary<string, ContractInfo> snapshot = _symbolIndex;
            return snapshot.Values
                .OrderBy(c => c.ExchangeNm)
                .ThenBy(c => c.ProductId)
                .ThenBy(c => c.Month)
                .ThenBy(c => c.StrikePx)
                .ThenBy(c => c.CPF)
                .ToArray();
        }
        #endregion
        // (GetAvailableMonths 等方法依然有效且不需修改！)
    }
    #region 1. 領域模型 (Domain Models)

    /// <summary>
    /// 買賣權定義
    /// </summary>
    public enum CPFEnum
    {
        None =0,
        Futures = 1,
        Call = 2,
        Put = 3,
    }

    /// <summary>
    /// 商品基本檔核心模型 (唯讀、不可變、執行緒安全)
    /// 負責將底層 PT01805, PT01802 聚合為 UI 與邏輯層容易使用的格式
    /// </summary>
    public record ContractInfo
    {
        // (已幫你換成相容舊版 C# 的寫法，消除 required 報錯)
        public string ProductId { get; init; } = string.Empty;     // 商品代碼 (如: TXF, TW)
        public string ProductName { get; init; } = string.Empty;   // 商品中文名稱 (如: 台指期, 摩台期)
        public string Symbol { get; init; } = string.Empty;        // 全碼 (如: TXFB6, TW-J13)
        public string SymbolEName { get; init; } = string.Empty;   // 合約中文名稱 (如: 台指期, 摩台期)
        public string SymbolCName { get; init; } = string.Empty;   // 合約中文名稱 (如: 台指期, 摩台期)
        public string Month { get; init; } = string.Empty;         // 交割年月 (如: 202606)
        // 🔥 新增：交易所名稱，用來區分國內與國外 (如: TAIFEX, SGX, CME)
        public MARKET_EXCHANGE Exchange { get; init; } 
        public string ExchangeNm { get; set; } = string.Empty;
        public int PxDecimal { get; init; }
        public int StrikePxDecimal { get; set; } = 0;
        public decimal TaxRate { get; set; }
        public string EndDate { get; set; } = string.Empty;
        public string FirstNoticeDate { get; set; } = string.Empty;
        public string LastTradeDate { get; set; } = string.Empty;
        public string ExpiryDate { get; set; } = string.Empty;
        public decimal FallPx { get; set; }
        public decimal RisePx { get; set; }
        public bool Hot { get; set; }
        public char ComType { get; init; }
        public decimal TickSize { get; init; }
        public decimal ContractValue { get; init; }
        public decimal StrikePx { get; init; }
        public CPFEnum CPF { get; init; }
        public TickTypeEnum TickType { get; init; }
        public string CharMonth { get; set; } = string.Empty;
        public decimal PreClosePx { get; set; }
        public string ComYM { get; set; } = string.Empty;
        public string ComYM2 { get; set; } = string.Empty;
        public string CP { get; set; } = string.Empty;
    }
    /// <summary>
    /// 選擇權 T 字報價專用 ViewModel (方便直接綁定給 WinForm DataGridView)
    /// </summary>
    public record OptionTQuoteRow
    {
        public double StrikePrice { get; init; }
        public ContractInfo? CallContract { get; init; }
        public ContractInfo? PutContract { get; init; }
    }
    #endregion
}
