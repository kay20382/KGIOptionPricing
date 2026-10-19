using System;

namespace KGIOptionPricing
{
    public class Option
    {
        public string Symbol { get; set; } = string.Empty;
        public bool IsCall { get; set; }
        public double StrikePrice { get; set; }
        public DateTime ExpirationDate { get; set; }
        public double RiskFreeRate { get; set; }
        public TradingCalendar Calendar { get; set; }

        public double Bid { get; set; }
        public double Ask { get; set; }
        public double LastPrice { get; set; }
        public double MarketPrice 
        {
            get 
            {
                if (LastPrice > 0) return LastPrice;
                double effectiveBid = Bid > 0 ? Bid : 0.1;
                if (Ask > 0) return (effectiveBid + Ask) / 2.0;
                return 0.0;
            }
        }
        
        public double TheoreticalBid { get; set; }
        public double TheoreticalAsk { get; set; }
        public double TheoreticalMidPrice 
        {
            get
            {
                double effectiveBid = TheoreticalBid > 0 ? TheoreticalBid : 0.1;
                if (TheoreticalAsk > 0) return (effectiveBid + TheoreticalAsk) / 2.0;
                return 0.0;
            }
        }
        public double PreviousCloseIMPV { get; set; }
        public double SmoothedIMPV { get; set; }
        public double TheoreticalPrice { get; set; }
        public bool HasLastPrice => LastPrice > 0 || Bid > 0 || Ask > 0;

        public double ImpliedVolatility { get; private set; }
        public double Delta { get; private set; }
        public double Gamma { get; private set; }
        public double Theta { get; private set; }
        public double Vega { get; private set; }
        public double Rho { get; private set; }

        public Option(bool isCall, double strikePrice, DateTime expirationDate, double riskFreeRate, TradingCalendar calendar)
        {
            IsCall = isCall;
            StrikePrice = strikePrice;
            ExpirationDate = expirationDate;
            RiskFreeRate = riskFreeRate;
            Calendar = calendar ?? new TradingCalendar();
        }

        public void UpdateMarketData(double currentUnderlyingPrice, DateTime currentDate)
        {
            double T = Calendar.CalculateTTM(currentDate, ExpirationDate);

            if (T <= 0)
            {
                ImpliedVolatility = 0;
                Delta = IsCall ? (currentUnderlyingPrice >= StrikePrice ? 1 : 0) : (currentUnderlyingPrice <= StrikePrice ? -1 : 0);
                Gamma = 0;
                Theta = 0;
                Vega = 0;
                Rho = 0;
                TheoreticalPrice = IsCall ? Math.Max(currentUnderlyingPrice - StrikePrice, 0.0) : Math.Max(StrikePrice - currentUnderlyingPrice, 0.0);
                return;
            }

            // Use TheoreticalMidPrice first so the IV strictly matches the Bid/Ask displayed on the UI
            double priceToUse = TheoreticalMidPrice > 0 ? TheoreticalMidPrice : MarketPrice;

            if (priceToUse > 0)
            {
                ImpliedVolatility = BlackScholesMath.CalculateImpliedVolatility(
                    IsCall, currentUnderlyingPrice, StrikePrice, T, RiskFreeRate, priceToUse);
                SmoothedIMPV = ImpliedVolatility;
            }
            else
            {
                ImpliedVolatility = PreviousCloseIMPV > 0 ? PreviousCloseIMPV : 0.0;
                SmoothedIMPV = ImpliedVolatility;
            }

            if (ImpliedVolatility > 0)
            {
                TheoreticalPrice = IsCall ? BlackScholesMath.CallPrice(currentUnderlyingPrice, StrikePrice, T, RiskFreeRate, ImpliedVolatility) : BlackScholesMath.PutPrice(currentUnderlyingPrice, StrikePrice, T, RiskFreeRate, ImpliedVolatility);
                Delta = BlackScholesMath.Delta(currentUnderlyingPrice, StrikePrice, T, RiskFreeRate, ImpliedVolatility, IsCall);
                Gamma = BlackScholesMath.Gamma(currentUnderlyingPrice, StrikePrice, T, RiskFreeRate, ImpliedVolatility);
                Theta = BlackScholesMath.Theta(currentUnderlyingPrice, StrikePrice, T, RiskFreeRate, ImpliedVolatility, IsCall);
                Vega = BlackScholesMath.Vega(currentUnderlyingPrice, StrikePrice, T, RiskFreeRate, ImpliedVolatility);
                Rho = BlackScholesMath.Rho(currentUnderlyingPrice, StrikePrice, T, RiskFreeRate, ImpliedVolatility, IsCall);
            }
        }
    }
}
