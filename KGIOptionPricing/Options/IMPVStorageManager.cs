using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KGIOptionPricing
{
    public class IMPVStorageManager
    {
        private readonly string _storageDirectory = "IMPV_Data";

        public IMPVStorageManager()
        {
            if (!Directory.Exists(_storageDirectory))
            {
                Directory.CreateDirectory(_storageDirectory);
            }
        }

        public void SaveSnapshot(IEnumerable<OptionChain> chains)
        {
            string filename = $"TXO_IMPV_{DateTime.Now:yyyyMMdd_HHmm}.csv";
            string filepath = Path.Combine(_storageDirectory, filename);

            using (StreamWriter sw = new StreamWriter(filepath))
            {
                sw.WriteLine("ExpirationDate,IsCall,Moneyness(S/K),Bid,Ask,TheoreticalBid,TheoreticalAsk,ImpliedVolatility,SmoothedIMPV");
                
                foreach (var chain in chains)
                {
                    double S = chain.UnderlyingPrice;
                    var allOptions = chain.Calls.Concat(chain.Puts);
                    foreach (var opt in allOptions)
                    {
                        double moneyness = S > 0 ? S / opt.StrikePrice : 0;
                        sw.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, 
                            "{0:yyyy-MM-dd},{1},{2:F6},{3:F2},{4:F2},{5:F2},{6:F2},{7:F6},{8:F6}",
                            opt.ExpirationDate, opt.IsCall, moneyness, opt.Bid, opt.Ask, opt.TheoreticalBid, opt.TheoreticalAsk, opt.ImpliedVolatility, opt.SmoothedIMPV));
                    }
                }
            }
        }

        public void LoadLatestSnapshot(IEnumerable<OptionChain> chains)
        {
            var files = Directory.GetFiles(_storageDirectory, "TXO_IMPV_*.csv")
                                 .OrderByDescending(f => f)
                                 .ToList();

            if (files.Count == 0) return;

            string latestFile = files.First();
            
            // Key: "ExpirationDate_IsCall", Value: List of (Moneyness, SmoothedIMPV)
            var impvDict = new Dictionary<string, List<(double Moneyness, double IMPV)>>();

            // Read the CSV
            var lines = File.ReadAllLines(latestFile);
            foreach (var line in lines.Skip(1)) // Skip header
            {
                var parts = line.Split(',');
                // Parts: Exp, IsCall, Moneyness, Bid, Ask, TBid, TAsk, IV, SmoothedIMPV
                // Accept old formats (5 cols) and new formats (9 cols)
                if (parts.Length >= 5)
                {
                    string exp = parts[0];
                    string isCall = parts[1];
                    double moneyness = 0;
                    double smoothedImpv = 0;
                    
                    if (parts.Length >= 9) // New format
                    {
                        double.TryParse(parts[2], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out moneyness);
                        double.TryParse(parts[8], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out smoothedImpv);
                    }
                    else // Old format (Exp, IsCall, Strike, IV, SmoothedIV)
                    {
                        // Note: For old files, we don't have S to calculate S/K, so we'll skip or approximate. 
                        // It's better to just require the new format going forward.
                        continue;
                    }

                    if (smoothedImpv > 0)
                    {
                        string key = $"{exp}_{isCall}";
                        if (!impvDict.ContainsKey(key)) impvDict[key] = new List<(double, double)>();
                        impvDict[key].Add((moneyness, smoothedImpv));
                    }
                }
            }

            // Sort lists by moneyness for interpolation
            foreach (var key in impvDict.Keys)
            {
                impvDict[key] = impvDict[key].OrderBy(x => x.Moneyness).ToList();
            }

            // Apply to existing chains using interpolation (Sticky Moneyness)
            foreach (var chain in chains)
            {
                double S = chain.UnderlyingPrice;
                foreach (var opt in chain.Calls.Concat(chain.Puts))
                {
                    string key = $"{opt.ExpirationDate:yyyy-MM-dd}_{opt.IsCall}";
                    if (impvDict.TryGetValue(key, out var savedList) && savedList.Count > 0)
                    {
                        double currentMoneyness = S / opt.StrikePrice;
                        opt.PreviousCloseIMPV = Interpolate(savedList, currentMoneyness);
                    }
                }
            }
        }

        private double Interpolate(List<(double Moneyness, double IMPV)> data, double target)
        {
            if (data.Count == 0) return 0;
            if (data.Count == 1) return data[0].IMPV;

            if (target <= data.First().Moneyness) return data.First().IMPV;
            if (target >= data.Last().Moneyness) return data.Last().IMPV;

            for (int i = 0; i < data.Count - 1; i++)
            {
                if (target >= data[i].Moneyness && target <= data[i + 1].Moneyness)
                {
                    double x0 = data[i].Moneyness;
                    double y0 = data[i].IMPV;
                    double x1 = data[i + 1].Moneyness;
                    double y1 = data[i + 1].IMPV;

                    if (x0 == x1) return y0;

                    return y0 + (y1 - y0) * (target - x0) / (x1 - x0);
                }
            }
            return 0;
        }
    }
}
