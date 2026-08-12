using System;

namespace KGIOptionPricing
{
    public static class BlackScholesMath
    {
        private const double ONE_OVER_SQRT_2PI = 0.39894228040143267793994605993438;

        /// <summary>
        /// Standard Normal Probability Density Function
        /// </summary>
        public static double NormalPDF(double x)
        {
            return ONE_OVER_SQRT_2PI * Math.Exp(-0.5 * x * x);
        }

        /// <summary>
        /// Standard Normal Cumulative Distribution Function (Abramowitz & Stegun approximation)
        /// </summary>
        public static double NormalCDF(double x)
        {
            double k = 1.0 / (1.0 + 0.2316419 * Math.Abs(x));
            double approx = 1.0 - NormalPDF(x) * (0.319381530 * k
                - 0.356563782 * k * k
                + 1.781477937 * Math.Pow(k, 3)
                - 1.821255978 * Math.Pow(k, 4)
                + 1.330274429 * Math.Pow(k, 5));

            if (x < 0.0)
            {
                return 1.0 - approx;
            }
            return approx;
        }

        public static double CalculateD1(double S, double K, double T, double r, double sigma)
        {
            if (T <= 0 || sigma <= 0) return 0;
            return (Math.Log(S / K) + (r + sigma * sigma / 2.0) * T) / (sigma * Math.Sqrt(T));
        }

        public static double CalculateD2(double d1, double T, double sigma)
        {
            if (T <= 0 || sigma <= 0) return 0;
            return d1 - sigma * Math.Sqrt(T);
        }

        public static double CallPrice(double S, double K, double T, double r, double sigma)
        {
            if (T <= 0.0) return Math.Max(S - K, 0.0);
            
            double d1 = CalculateD1(S, K, T, r, sigma);
            double d2 = CalculateD2(d1, T, sigma);
            
            return S * NormalCDF(d1) - K * Math.Exp(-r * T) * NormalCDF(d2);
        }

        public static double PutPrice(double S, double K, double T, double r, double sigma)
        {
            if (T <= 0.0) return Math.Max(K - S, 0.0);
            
            double d1 = CalculateD1(S, K, T, r, sigma);
            double d2 = CalculateD2(d1, T, sigma);
            
            return K * Math.Exp(-r * T) * NormalCDF(-d2) - S * NormalCDF(-d1);
        }

        public static double Delta(double S, double K, double T, double r, double sigma, bool isCall)
        {
            if (T <= 0.0) return isCall ? (S >= K ? 1 : 0) : (S <= K ? -1 : 0);
            double d1 = CalculateD1(S, K, T, r, sigma);
            return isCall ? NormalCDF(d1) : NormalCDF(d1) - 1.0;
        }

        public static double Gamma(double S, double K, double T, double r, double sigma)
        {
            if (T <= 0.0) return 0.0;
            double d1 = CalculateD1(S, K, T, r, sigma);
            return NormalPDF(d1) / (S * sigma * Math.Sqrt(T));
        }

        public static double Theta(double S, double K, double T, double r, double sigma, bool isCall)
        {
            if (T <= 0.0) return 0.0;
            double d1 = CalculateD1(S, K, T, r, sigma);
            double d2 = CalculateD2(d1, T, sigma);
            
            double term1 = -(S * NormalPDF(d1) * sigma) / (2.0 * Math.Sqrt(T));
            
            if (isCall)
            {
                double term2 = r * K * Math.Exp(-r * T) * NormalCDF(d2);
                return term1 - term2;
            }
            else
            {
                double term2 = r * K * Math.Exp(-r * T) * NormalCDF(-d2);
                return term1 + term2;
            }
        }

        public static double Vega(double S, double K, double T, double r, double sigma)
        {
            if (T <= 0.0) return 0.0;
            double d1 = CalculateD1(S, K, T, r, sigma);
            return S * NormalPDF(d1) * Math.Sqrt(T);
        }

        public static double Rho(double S, double K, double T, double r, double sigma, bool isCall)
        {
            if (T <= 0.0) return 0.0;
            double d1 = CalculateD1(S, K, T, r, sigma);
            double d2 = CalculateD2(d1, T, sigma);
            
            if (isCall)
            {
                return K * T * Math.Exp(-r * T) * NormalCDF(d2);
            }
            else
            {
                return -K * T * Math.Exp(-r * T) * NormalCDF(-d2);
            }
        }

        public static double CalculateImpliedVolatility(bool isCall, double S, double K, double T, double r, double marketPrice, double maxIterations = 100, double tolerance = 1e-5)
        {
            if (T <= 0.0) return 0.0;

            // Initial guess (e.g. Brenner and Subrahmanyam 1988)
            double sigma = Math.Sqrt(2.0 * Math.PI / T) * (marketPrice / S);
            if (sigma == 0.0) sigma = 0.3; // Fallback initial guess

            for (int i = 0; i < maxIterations; i++)
            {
                double price = isCall ? CallPrice(S, K, T, r, sigma) : PutPrice(S, K, T, r, sigma);
                double diff = price - marketPrice;
                
                if (Math.Abs(diff) < tolerance)
                {
                    return sigma;
                }
                
                double vega = Vega(S, K, T, r, sigma);
                
                if (vega == 0.0 || double.IsNaN(vega)) 
                {
                    return CalculateImpliedVolatilityBisection(isCall, S, K, T, r, marketPrice, maxIterations, tolerance);
                }
                
                sigma = sigma - diff / vega;
                
                if (sigma <= 0.0) sigma = 0.001; // Avoid negative volatility
                if (sigma > 5.0) sigma = 5.0;    // Avoid astronomical volatility divergence
            }

            return CalculateImpliedVolatilityBisection(isCall, S, K, T, r, marketPrice, maxIterations, tolerance);
        }

        public static double CalculateImpliedVolatilityBisection(bool isCall, double S, double K, double T, double r, double marketPrice, double maxIterations = 100, double tolerance = 1e-5)
        {
            double low = 0.0001;
            double high = 5.0; // 500% IV max
            
            double maxPrice = isCall ? CallPrice(S, K, T, r, high) : PutPrice(S, K, T, r, high);
            if (marketPrice > maxPrice) return high;

            for (int i = 0; i < maxIterations; i++)
            {
                double mid = (low + high) / 2.0;
                double price = isCall ? CallPrice(S, K, T, r, mid) : PutPrice(S, K, T, r, mid);
                
                if (Math.Abs(price - marketPrice) < tolerance)
                {
                    return mid;
                }
                
                if (price > marketPrice)
                {
                    high = mid;
                }
                else
                {
                    low = mid;
                }
            }
            
            return (low + high) / 2.0;
        }
        #region Decimal Overloads

        public static decimal NormalCDF(decimal x) => (decimal)NormalCDF((double)x);
        public static decimal NormalPDF(decimal x) => (decimal)NormalPDF((double)x);
        
        public static decimal CallPrice(decimal S, decimal K, decimal T, decimal r, decimal sigma)
            => (decimal)CallPrice((double)S, (double)K, (double)T, (double)r, (double)sigma);

        public static decimal PutPrice(decimal S, decimal K, decimal T, decimal r, decimal sigma)
            => (decimal)PutPrice((double)S, (double)K, (double)T, (double)r, (double)sigma);

        public static decimal Delta(decimal S, decimal K, decimal T, decimal r, decimal sigma, bool isCall)
            => (decimal)Delta((double)S, (double)K, (double)T, (double)r, (double)sigma, isCall);

        public static decimal Gamma(decimal S, decimal K, decimal T, decimal r, decimal sigma)
            => (decimal)Gamma((double)S, (double)K, (double)T, (double)r, (double)sigma);

        public static decimal Theta(decimal S, decimal K, decimal T, decimal r, decimal sigma, bool isCall)
            => (decimal)Theta((double)S, (double)K, (double)T, (double)r, (double)sigma, isCall);

        public static decimal Vega(decimal S, decimal K, decimal T, decimal r, decimal sigma)
            => (decimal)Vega((double)S, (double)K, (double)T, (double)r, (double)sigma);

        public static decimal Rho(decimal S, decimal K, decimal T, decimal r, decimal sigma, bool isCall)
            => (decimal)Rho((double)S, (double)K, (double)T, (double)r, (double)sigma, isCall);

        public static decimal CalculateImpliedVolatility(bool isCall, decimal S, decimal K, decimal T, decimal r, decimal marketPrice, double maxIterations = 100, double tolerance = 1e-5)
            => (decimal)CalculateImpliedVolatility(isCall, (double)S, (double)K, (double)T, (double)r, (double)marketPrice, maxIterations, tolerance);

        #endregion
    }
}
