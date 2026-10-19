using System;
using System.Collections.Generic;
using System.Linq;

namespace KGIOptionPricing
{
    public class OptionChain
    {
        public DateTime ExpirationDate { get; private set; }
        public bool IsWeekly { get; private set; }
        public double UnderlyingPrice { get; private set; }
        public VolatilityCurve VolatilityCurve { get; private set; }

        public List<Option> Calls { get; private set; } = new List<Option>();
        public List<Option> Puts { get; private set; } = new List<Option>();

        public OptionChain(DateTime expirationDate, bool isWeekly, double riskFreeRate, TradingCalendar calendar)
        {
            ExpirationDate = expirationDate;
            IsWeekly = isWeekly;
            double ttm = calendar.CalculateTTM(DateTime.Today, ExpirationDate);
            VolatilityCurve = new VolatilityCurve(ExpirationDate, 0, riskFreeRate, ttm, SmoothingMethod.CubicSpline);
        }

        public void AddOption(Option opt)
        {
            if (opt.IsCall) Calls.Add(opt);
            else Puts.Add(opt);
        }

        public void UpdateMarketData(double futuresPrice, DateTime currentDate)
        {
            // 1. Calculate Synthetic Futures Price
            double synthPrice = CalculateSyntheticFuturesPrice();
            UnderlyingPrice = synthPrice > 0 ? synthPrice : (futuresPrice > 0 ? futuresPrice : 0);

            if (UnderlyingPrice <= 0)
            {
                var strikes = Calls.Select(c => c.StrikePrice).Union(Puts.Select(p => p.StrikePrice)).OrderBy(s => s).ToList();
                if (strikes.Count > 0) UnderlyingPrice = strikes[strikes.Count / 2];
                else UnderlyingPrice = 45000;
            }

            VolatilityCurve.UnderlyingPrice = UnderlyingPrice;
            if (Calls.Count == 0 && Puts.Count == 0) return;

            // 2. Generate Theoretical Bid/Ask for Mid Price calculation
            GenerateTheoreticalBidAsk();

            foreach (var call in Calls)
            {
                call.UpdateMarketData(UnderlyingPrice, currentDate);
            }
            foreach (var put in Puts)
            {
                put.UpdateMarketData(UnderlyingPrice, currentDate);
            }

            // 3. Fit Volatility Curve
            var allOptions = Calls.Concat(Puts).ToList();
            VolatilityCurve.UpdateDataPoints(allOptions);

            // 4. Update Smoothed IMPV
            foreach (var opt in allOptions)
            {
                opt.SmoothedIMPV = VolatilityCurve.GetSmoothedVolatility(opt.StrikePrice);
            }
        }

        private double CalculateSyntheticFuturesPrice()
        {
            var callDict = Calls.Where(c => c.MarketPrice > 0 || c.Bid > 0 || c.LastPrice > 0)
                                .ToDictionary(c => c.StrikePrice, c => c);
            var putDict = Puts.Where(p => p.MarketPrice > 0 || p.Bid > 0 || p.LastPrice > 0)
                               .ToDictionary(p => p.StrikePrice, p => p);

            List<(double diff, double f)> candidates = new List<(double, double)>();

            foreach (var strike in callDict.Keys)
            {
                if (putDict.TryGetValue(strike, out var putOpt))
                {
                    var callOpt = callDict[strike];
                    double cPrice = callOpt.MarketPrice > 0 ? callOpt.MarketPrice : (callOpt.LastPrice > 0 ? callOpt.LastPrice : callOpt.Bid);
                    double pPrice = putOpt.MarketPrice > 0 ? putOpt.MarketPrice : (putOpt.LastPrice > 0 ? putOpt.LastPrice : putOpt.Bid);

                    if (cPrice > 0 && pPrice > 0)
                    {
                        double F = strike + cPrice - pPrice;
                        double diff = Math.Abs(cPrice - pPrice);
                        candidates.Add((diff, F));
                    }
                }
            }

            if (candidates.Count > 0)
            {
                // 優先取 |C - P| 最小 (最接近價平 ATM) 的前 5 檔，排序後取中位數
                var bestCandidates = candidates.OrderBy(x => x.diff).Take(5).Select(x => x.f).ToList();
                bestCandidates.Sort();
                return bestCandidates[bestCandidates.Count / 2];
            }
            
            // 若尚無報價，備援使用該選擇權鏈的履約價中位數
            var allStrikes = Calls.Select(c => c.StrikePrice).Union(Puts.Select(p => p.StrikePrice)).OrderBy(s => s).ToList();
            if (allStrikes.Count > 0)
            {
                return allStrikes[allStrikes.Count / 2];
            }

            return 0;
        }

        private void GenerateTheoreticalBidAsk()
        {
            var sortedCalls = Calls.OrderBy(o => o.StrikePrice).ToList();
            var sortedPuts = Puts.OrderBy(o => o.StrikePrice).ToList();

            foreach (var opt in sortedCalls)
            {
                opt.TheoreticalBid = opt.Bid;
                opt.TheoreticalAsk = opt.Ask;
            }
            foreach (var opt in sortedPuts)
            {
                opt.TheoreticalBid = opt.Bid;
                opt.TheoreticalAsk = opt.Ask;
            }

            for (int i = 1; i < sortedCalls.Count; i++)
            {
                if (sortedCalls[i - 1].TheoreticalAsk > 0)
                {
                    if (sortedCalls[i].TheoreticalAsk == 0 || sortedCalls[i].TheoreticalAsk > sortedCalls[i - 1].TheoreticalAsk)
                        sortedCalls[i].TheoreticalAsk = sortedCalls[i - 1].TheoreticalAsk;
                }
            }

            for (int i = sortedCalls.Count - 2; i >= 0; i--)
            {
                if (sortedCalls[i + 1].TheoreticalBid > 0)
                {
                    if (sortedCalls[i].TheoreticalBid < sortedCalls[i + 1].TheoreticalBid)
                        sortedCalls[i].TheoreticalBid = sortedCalls[i + 1].TheoreticalBid;
                }
            }

            for (int i = sortedPuts.Count - 2; i >= 0; i--)
            {
                if (sortedPuts[i + 1].TheoreticalAsk > 0)
                {
                    if (sortedPuts[i].TheoreticalAsk == 0 || sortedPuts[i].TheoreticalAsk > sortedPuts[i + 1].TheoreticalAsk)
                        sortedPuts[i].TheoreticalAsk = sortedPuts[i + 1].TheoreticalAsk;
                }
            }

            for (int i = 1; i < sortedPuts.Count; i++)
            {
                if (sortedPuts[i - 1].TheoreticalBid > 0)
                {
                    if (sortedPuts[i].TheoreticalBid < sortedPuts[i - 1].TheoreticalBid)
                        sortedPuts[i].TheoreticalBid = sortedPuts[i - 1].TheoreticalBid;
                }
            }
        }
    }
}
