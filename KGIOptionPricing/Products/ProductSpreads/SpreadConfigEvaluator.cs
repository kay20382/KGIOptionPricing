using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KGIOptionPricing
{
    public class SpreadRule
    {
        [JsonPropertyName("minBid")]
        public double MinBid { get; set; }

        [JsonPropertyName("maxBid")]
        public double MaxBid { get; set; }

        [JsonPropertyName("percentage")]
        public double Percentage { get; set; }

        [JsonPropertyName("minSpread")]
        public double MinSpread { get; set; }

        [JsonPropertyName("fixedSpread")]
        public double FixedSpread { get; set; } = 0.0;
    }

    public class SpreadConfigEvaluator
    {
        private List<SpreadRule> _rules = new List<SpreadRule>();

        public SpreadConfigEvaluator(List<SpreadRule>? rules = null)
        {
            if (rules != null && rules.Count > 0)
            {
                _rules = rules;
            }
            else
            {
                LoadDefaultRules();
            }
        }

        private void LoadDefaultRules()
        {
            // Default TAIFEX Spread rules
            _rules = new List<SpreadRule>
            {
                new SpreadRule { MinBid = 0, MaxBid = 100, Percentage = 0.12, MinSpread = 3.0 },
                new SpreadRule { MinBid = 100, MaxBid = 400, Percentage = 0.08, MinSpread = 12.0 },
                new SpreadRule { MinBid = 400, MaxBid = 800, Percentage = 0.07, MinSpread = 32.0 },
                new SpreadRule { MinBid = 800, MaxBid = 1000, Percentage = 0.06, MinSpread = 56.0 },
                new SpreadRule { MinBid = 1000, MaxBid = 6000, Percentage = 0.04, MinSpread = 60.0 },
                new SpreadRule { MinBid = 6000, MaxBid = 999999, Percentage = 0.0, FixedSpread = 300.0 }
            };
        }

        public double GetMaxAllowedSpread(double bid)
        {
            if (bid < 0) return 3.0;

            foreach (var rule in _rules)
            {
                if (bid >= rule.MinBid && bid < rule.MaxBid)
                {
                    if (rule.FixedSpread > 0) return rule.FixedSpread;
                    return Math.Max(rule.Percentage * bid, rule.MinSpread);
                }
            }

            return 300.0;
        }
    }
}
