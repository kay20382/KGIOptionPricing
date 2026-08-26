using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace KGIOptionPricing
{
    public class KGIJSONModel
    {
        public decimal TotalDollarDelta { get; set; }
        public List<KGIRiskItem>? RiskData { get; set; }
        public bool Sucess { get; set; }
        public string Message { get; set; } = string.Empty;
        public object? Data { get; set; }
    }

    public class KGIRiskItem
    {
        public string? Commodity { get; set; }
        public string? SellteMonth { get; set; }
        public string? CallPut { get; set; }
        public decimal StrikePrice { get; set; }
        public string? BuySell { get; set; }
        public int OpenInterest { get; set; }
        public decimal TheoreticalPrice { get; set; }
        public decimal MarketPrice { get; set; }
        public decimal Delta { get; set; }
        public decimal DollarDelta { get; set; }
        public decimal ImpliedVolatility { get; set; }
    }

    /// <summary>
    /// 風控資料匯出器：計算部位的風險指標並輸出為指定格式的 JSON 檔案。
    /// </summary>
    public static class RiskDataExporter
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(typeof(RiskDataExporter));
        public static void ExportToJsonFile(string filePath, List<TradePositionItem> positions, Dictionary<string, OptionChain> currentChains, double underlyingPrice, SpreadConfigEvaluator spreadEvaluator)
        {
            var riskItems = new List<KGIRiskItem>();
            decimal totalDollarDelta = 0;

            if (positions != null && currentChains != null && positions.Count > 0)
            {
                foreach (var pos in positions)
                {
                    if (pos.ComType != "O") continue; // We only calculate options risk as per spec

                    bool isCall = pos.CallPut == "C";
                    bool isShort = pos.BuySell == "S";
                    int qty = pos.OpenInterest;

                    // Find matching option in our OptionChains
                    Option? targetOpt = null;
                    OptionChain? targetChain = null;

                    foreach (var chain in currentChains.Values)
                    {
                        string settleStr = chain.ExpirationDate.ToString("yyyyMM");
                        var optList = isCall ? chain.Calls : chain.Puts;
                        var found = optList.FirstOrDefault(o => Math.Abs(o.StrikePrice - pos.StrikePrice) < 0.1);
                        if (found != null && pos.SettleMonth.Contains(settleStr))
                        {
                            targetOpt = found;
                            targetChain = chain;
                            break;
                        }
                    }

                    if (targetOpt == null)
                    {
                        foreach (var chain in currentChains.Values)
                        {
                            var optList = isCall ? chain.Calls : chain.Puts;
                            targetOpt = optList.FirstOrDefault(o => Math.Abs(o.StrikePrice - pos.StrikePrice) < 0.1);
                            if (targetOpt != null) 
                            {
                                targetChain = chain;
                                break;
                            }
                        }
                    }

                    decimal deltaVal = 0;
                    decimal dollarDeltaVal = 0;
                    decimal impliedVol = 0;
                    decimal marketPrice = 0;
                    decimal theoreticalPrice = 0;

                    if (targetOpt != null && targetChain != null)
                    {
                        // 1. 根據選擇權持倉到期日，從 OptionChain 取得動態合成標的價 (CalculateSyntheticFuturesPrice)
                        double posUnderlyingPrice = targetChain.UnderlyingPrice > 0 
                            ? targetChain.UnderlyingPrice 
                            : (underlyingPrice > 0 ? underlyingPrice : 21000.0);

                        // 2. 統一使用與 UI 一致的 TradingCalendar 計算年化時間 T (傳入 DateTime.Now 支援即時秒級衰減)
                        double T = targetOpt.Calendar != null 
                            ? targetOpt.Calendar.CalculateTTM(DateTime.Now, targetChain.ExpirationDate)
                            : (targetChain.ExpirationDate > DateTime.Now ? (targetChain.ExpirationDate - DateTime.Now).TotalDays / 240.0 : 0.001);

                        double riskFreeRate = targetOpt.RiskFreeRate;
                        
                        double effectiveBid = targetOpt.TheoreticalBid > 0 ? targetOpt.TheoreticalBid : 0.1;
                        bool spreadOk = targetOpt.TheoreticalAsk > 0 && (targetOpt.TheoreticalAsk - effectiveBid) <= spreadEvaluator.GetMaxAllowedSpread(effectiveBid);

                        // 3. 直接優先取用合約 targetOpt 上與 UI 畫面 100% 完全相同的 ImpliedVolatility
                        double effectiveIv = targetOpt.ImpliedVolatility;
                        
                        if (effectiveIv <= 0)
                        {
                            double prevCurveIv = targetChain.VolatilityCurve.GetSmoothedVolatility(targetOpt.StrikePrice);
                            double atmShift = 0.0;
                            var atmOpt = isCall 
                                ? targetChain.Calls.OrderBy(o => Math.Abs(o.StrikePrice - posUnderlyingPrice)).FirstOrDefault()
                                : targetChain.Puts.OrderBy(o => Math.Abs(o.StrikePrice - posUnderlyingPrice)).FirstOrDefault();
                            if (atmOpt != null && atmOpt.PreviousCloseIMPV > 0 && atmOpt.ImpliedVolatility > 0)
                            {
                                atmShift = atmOpt.ImpliedVolatility - atmOpt.PreviousCloseIMPV;
                            }
                            effectiveIv = Math.Max(0.001, (prevCurveIv > 0 ? prevCurveIv : targetOpt.PreviousCloseIMPV) + atmShift);
                        }

                        impliedVol = (decimal)effectiveIv;

                        // 理論價計算：與 UI 中間價邏輯完全一致
                        double midPrice = targetOpt.TheoreticalMidPrice > 0 ? targetOpt.TheoreticalMidPrice : (effectiveBid + targetOpt.TheoreticalAsk) / 2.0;
                        if (spreadOk && midPrice > 0)
                        {
                            theoreticalPrice = (decimal)midPrice;
                        }
                        else
                        {
                            double bsPrice = isCall 
                                ? BlackScholesMath.CallPrice(posUnderlyingPrice, targetOpt.StrikePrice, T, riskFreeRate, effectiveIv)
                                : BlackScholesMath.PutPrice(posUnderlyingPrice, targetOpt.StrikePrice, T, riskFreeRate, effectiveIv);
                            theoreticalPrice = (decimal)bsPrice;
                        }

                        // 市場價：有成交價取成交價，否則 0
                        marketPrice = targetOpt.LastPrice > 0 ? (decimal)targetOpt.LastPrice : 0m;

                        // Delta 與 DollarDelta 計算：優先直接取用 targetOpt.Delta (保證與 UI 顯示完全 100% 所見即所得)
                        deltaVal = targetOpt.Delta != 0 
                            ? (decimal)targetOpt.Delta 
                            : (decimal)BlackScholesMath.Delta(posUnderlyingPrice, targetOpt.StrikePrice, T, riskFreeRate, effectiveIv, isCall);

                        int multiplier = 50; 
                        int sign = isShort ? -1 : 1;

                        dollarDeltaVal = deltaVal * (decimal)posUnderlyingPrice * multiplier * qty * sign;
                        totalDollarDelta += dollarDeltaVal;
                    }

                    var item = new KGIRiskItem
                    {
                        Commodity = pos.Commodity,
                        SellteMonth = pos.SettleMonth,
                        CallPut = pos.CallPut,
                        StrikePrice = (decimal)pos.StrikePrice,
                        BuySell = pos.BuySell,
                        OpenInterest = qty,
                        TheoreticalPrice = Math.Round(theoreticalPrice, 4),
                        MarketPrice = Math.Round(marketPrice, 4),
                        Delta = Math.Round(deltaVal, 4),
                        DollarDelta = Math.Round(dollarDeltaVal, 4),
                        ImpliedVolatility = Math.Round(impliedVol, 4)
                    };
                    riskItems.Add(item);
                    totalDollarDelta += item.DollarDelta;
                }
            }

            var model = new KGIJSONModel
            {
                TotalDollarDelta = totalDollarDelta,
                RiskData = riskItems,
                Sucess = true,
                Message = "",
                Data = null
            };

            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
                };
                string json = JsonSerializer.Serialize(model, options);

                // Create directory if not exists
                string dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(filePath, json);
                log.Info($"[RiskDataExporter] 風控資料已儲存至 {filePath}");
            }
            catch (Exception ex)
            {
                log.Error($"[RiskDataExporter] 儲存風控資料失敗: {ex.Message}", ex);
            }
        }
    }
}
