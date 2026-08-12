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
        public List<KGIRiskItem>? Data { get; set; }
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

    public static class RiskDataExporter
    {
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
                        // 1. 根據選擇權持倉的到期日，直接從 OptionChain 取得對應的期貨標的價格 (CalculateSyntheticFuturesPrice 合成價)
                        double posUnderlyingPrice = targetChain.UnderlyingPrice > 0 
                            ? targetChain.UnderlyingPrice 
                            : (underlyingPrice > 0 ? underlyingPrice : 21000.0);

                        double T = targetChain.ExpirationDate > DateTime.Today 
                            ? (targetChain.ExpirationDate - DateTime.Today).TotalDays / 365.0 
                            : 0.001;

                        double riskFreeRate = targetOpt.RiskFreeRate;
                        
                        // 規則檢查 (Max Allowed Spread)
                        double effectiveBid = targetOpt.TheoreticalBid > 0 ? targetOpt.TheoreticalBid : 0.1;
                        bool spreadOk = targetOpt.TheoreticalAsk > 0 && (targetOpt.TheoreticalAsk - effectiveBid) <= spreadEvaluator.GetMaxAllowedSpread(effectiveBid);
                        
                        // 2. Tier 2 (中間價 IV)：直接從 OptionChain / targetOpt 取用 ImpliedVolatility，不需要重新計算
                        double midIv = targetOpt.ImpliedVolatility;
                        double midPrice = targetOpt.TheoreticalMidPrice > 0 ? targetOpt.TheoreticalMidPrice : (effectiveBid + targetOpt.TheoreticalAsk) / 2.0;

                        if (spreadOk && (midIv < 0.05 || midIv > 1.50)) 
                            spreadOk = false;

                        // 計算 ATM Shift 與 Rule 3 IV
                        double atmShift = 0.0;
                        double prevCurveIv = targetChain.VolatilityCurve.GetSmoothedVolatility(targetOpt.StrikePrice);

                        var atmOpt = isCall 
                            ? targetChain.Calls.OrderBy(o => Math.Abs(o.StrikePrice - posUnderlyingPrice)).FirstOrDefault()
                            : targetChain.Puts.OrderBy(o => Math.Abs(o.StrikePrice - posUnderlyingPrice)).FirstOrDefault();
                        if (atmOpt != null && atmOpt.PreviousCloseIMPV > 0)
                        {
                            double currentAtmIv = atmOpt.ImpliedVolatility > 0 
                                ? atmOpt.ImpliedVolatility 
                                : BlackScholesMath.CalculateImpliedVolatility(isCall, posUnderlyingPrice, atmOpt.StrikePrice, T, riskFreeRate, atmOpt.MarketPrice);
                            if (currentAtmIv > 0) atmShift = currentAtmIv - atmOpt.PreviousCloseIMPV;
                        }

                        double rule3Iv = Math.Max(0.001, (prevCurveIv > 0 ? prevCurveIv : targetOpt.PreviousCloseIMPV) + atmShift);

                        // 3-Tier 生效 IV 判定：LastPrice > 0 => 成交 IV, 否則 Spread OK => 直接取用 OptionChain 中間價 IV, 否則 => Rule 3 IV
                        double effectiveIv = 0.0;
                        if (targetOpt.LastPrice > 0)
                        {
                            double tradedIv = BlackScholesMath.CalculateImpliedVolatility(isCall, posUnderlyingPrice, targetOpt.StrikePrice, T, riskFreeRate, targetOpt.LastPrice);
                            if (tradedIv >= 0.05 && tradedIv <= 1.50)
                                effectiveIv = tradedIv;
                            else if (spreadOk && midIv >= 0.05 && midIv <= 1.50)
                                effectiveIv = midIv;
                            else
                                effectiveIv = rule3Iv;
                        }
                        else if (spreadOk && midIv >= 0.05 && midIv <= 1.50)
                        {
                            effectiveIv = midIv;
                        }
                        else
                        {
                            effectiveIv = rule3Iv;
                        }

                        impliedVol = (decimal)effectiveIv;

                        // 理論價計算：Spread OK => 中間價, 否則 => BS Price of Rule 3 IV
                        if (spreadOk && midPrice > 0)
                        {
                            theoreticalPrice = (decimal)midPrice;
                        }
                        else
                        {
                            double bsPrice = isCall 
                                ? BlackScholesMath.CallPrice(posUnderlyingPrice, targetOpt.StrikePrice, T, riskFreeRate, rule3Iv)
                                : BlackScholesMath.PutPrice(posUnderlyingPrice, targetOpt.StrikePrice, T, riskFreeRate, rule3Iv);
                            theoreticalPrice = (decimal)bsPrice;
                        }

                        // 市場價：有成交價取成交價，否則 0
                        marketPrice = targetOpt.LastPrice > 0 ? (decimal)targetOpt.LastPrice : 0m;

                        // Delta 與 DollarDelta 計算 (使用合約到期日對應的 posUnderlyingPrice)
                        if (effectiveIv > 0)
                        {
                            deltaVal = (decimal)BlackScholesMath.Delta(posUnderlyingPrice, targetOpt.StrikePrice, T, riskFreeRate, effectiveIv, isCall);
                        }

                        int multiplier = 50; 
                        int sign = isShort ? -1 : 1;

                        dollarDeltaVal = deltaVal * (decimal)posUnderlyingPrice * multiplier * qty * sign;
                        totalDollarDelta += dollarDeltaVal;
                    }

                    riskItems.Add(new KGIRiskItem
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
                    });
                }
            }

            var model = new KGIJSONModel
            {
                TotalDollarDelta = Math.Round(totalDollarDelta, 4),
                Data = riskItems
            };

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.Create(UnicodeRanges.BasicLatin, UnicodeRanges.CjkUnifiedIdeographs)
            };

            string jsonOutput = JsonSerializer.Serialize(model, options);
            
            try
            {
                string dir = Path.GetDirectoryName(filePath) ?? string.Empty;
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(filePath, jsonOutput);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RiskDataExporter] Error saving JSON: {ex.Message}");
            }
        }
    }
}
