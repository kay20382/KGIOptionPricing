using System;
using System.Collections.Generic;
using System.Linq;
using MathNet.Numerics.Interpolation;
using MathNet.Numerics.Optimization;
using MathNet.Numerics.LinearAlgebra.Double;

namespace KGIOptionPricing
{
    public enum SmoothingMethod
    {
        Parabolic,
        CubicSpline,
        SABR
    }

    public class VolatilityCurve
    {
        public DateTime Expiration { get; set; }
        private SmoothingMethod _method;
        public SmoothingMethod Method
        {
            get => _method;
            set
            {
                if (_method != value)
                {
                    _method = value;
                    if (_strikes.Count > 0)
                    {
                        FitCurve();
                    }
                }
            }
        }
        public double UnderlyingPrice { get; set; }
        public double RiskFreeRate { get; set; }
        public double TTM { get; set; }

        // Raw Data Points
        private List<double> _strikes = new List<double>();
        private List<double> _impVols = new List<double>();

        // Interpolation/Fitting Models
        private double[] _parabolaCoeffs;
        private CubicSpline _cubicSpline;
        private SabrParameters _sabrParams;

        public VolatilityCurve(DateTime expiration, double underlyingPrice, double riskFreeRate, double ttm, SmoothingMethod method = SmoothingMethod.Parabolic)
        {
            Expiration = expiration;
            UnderlyingPrice = underlyingPrice;
            RiskFreeRate = riskFreeRate;
            TTM = ttm;
            Method = method;
        }

        public void UpdateDataPoints(IEnumerable<Option> options)
        {
            _strikes.Clear();
            _impVols.Clear();

            // Filter out invalid or zero-vol options
            // Combine calls and puts, usually we take OTM options to form the smile
            var validOptions = options
                .Where(o => o.ImpliedVolatility > 0 && o.ImpliedVolatility < 2.0) // Reasonable bounds
                .GroupBy(o => o.StrikePrice)
                .Select(g => new
                {
                    Strike = g.Key,
                    // Use OTM Option for Volatility (Call for K >= S, Put for K <= S)
                    ImpVol = g.Where(o => (o.IsCall && o.StrikePrice >= UnderlyingPrice) || (!o.IsCall && o.StrikePrice <= UnderlyingPrice))
                              .Select(o => o.ImpliedVolatility)
                              .DefaultIfEmpty(g.Average(o => o.ImpliedVolatility))
                              .Average()
                })
                .OrderBy(x => x.Strike)
                .ToList();

            foreach (var opt in validOptions)
            {
                _strikes.Add(opt.Strike);
                _impVols.Add(opt.ImpVol);
            }

            FitCurve();
        }

        private void FitCurve()
        {
            if (_strikes.Count < 3) return; // Need at least 3 points for meaningful fitting

            double[] x = _strikes.ToArray();
            double[] y = _impVols.ToArray();

            switch (Method)
            {
                case SmoothingMethod.Parabolic:
                    _parabolaCoeffs = MathNet.Numerics.Fit.Polynomial(x, y, 2);
                    break;
                case SmoothingMethod.CubicSpline:
                    _cubicSpline = CubicSpline.InterpolateNatural(x, y);
                    break;
                case SmoothingMethod.SABR:
                    CalibrateSABR(x, y);
                    break;
            }
        }

        public double GetSmoothedVolatility(double strike)
        {
            if (_strikes.Count < 3) return 0;

            switch (Method)
            {
                case SmoothingMethod.Parabolic:
                    return Math.Max(0.001, _parabolaCoeffs[0] + _parabolaCoeffs[1] * strike + _parabolaCoeffs[2] * Math.Pow(strike, 2));
                
                case SmoothingMethod.CubicSpline:
                    // Extrapolate flat if outside bounds
                    if (strike < _strikes.First()) return _impVols.First();
                    if (strike > _strikes.Last()) return _impVols.Last();
                    return Math.Max(0.001, _cubicSpline.Interpolate(strike));
                
                case SmoothingMethod.SABR:
                    return Math.Max(0.001, CalculateSABRVolatility(strike, _sabrParams));
                
                default:
                    return 0;
            }
        }

        #region SABR Model Implementation
        public struct SabrParameters
        {
            public double Alpha;
            public double Beta;
            public double Rho;
            public double Nu;
        }

        private void CalibrateSABR(double[] strikes, double[] vols)
        {
            // Simple heuristic calibration for SABR
            // Usually Beta is fixed to 0.5 or 1.0 (Lognormal). We use Beta = 1.0 here for simplicity
            double beta = 1.0;
            double f = UnderlyingPrice * Math.Exp(RiskFreeRate * TTM); // Forward price
            
            // Initial Guesses
            double alphaGuess = vols[vols.Length / 2]; // ATM Vol
            double rhoGuess = -0.1; // Typical equity skew
            double nuGuess = 0.5;   // Vol of Vol

            var initialGuess = new DenseVector(new[] { alphaGuess, rhoGuess, nuGuess });

            var objective = ObjectiveFunction.Value(v =>
            {
                double a = v[0];
                double r = Math.Max(-0.99, Math.Min(0.99, v[1])); // Constrain Rho
                double n = Math.Max(0.001, v[2]); // Constrain Nu

                double error = 0;
                var param = new SabrParameters { Alpha = a, Beta = beta, Rho = r, Nu = n };
                for (int i = 0; i < strikes.Length; i++)
                {
                    double calcVol = CalculateSABRVolatility(strikes[i], param);
                    error += Math.Pow(calcVol - vols[i], 2);
                }
                return error;
            });

            try
            {
                var solver = new NelderMeadSimplex(1e-5, 1000);
                var result = solver.FindMinimum(objective, initialGuess);

                _sabrParams = new SabrParameters
                {
                    Alpha = result.MinimizingPoint[0],
                    Beta = beta,
                    Rho = Math.Max(-0.99, Math.Min(0.99, result.MinimizingPoint[1])),
                    Nu = Math.Max(0.001, result.MinimizingPoint[2])
                };
            }
            catch
            {
                // Fallback if optimization fails
                _sabrParams = new SabrParameters { Alpha = alphaGuess, Beta = beta, Rho = rhoGuess, Nu = nuGuess };
            }
        }

        private double CalculateSABRVolatility(double K, SabrParameters p)
        {
            // Hagan's SABR approximation
            double F = UnderlyingPrice * Math.Exp(RiskFreeRate * TTM);
            double T = TTM;

            if (Math.Abs(F - K) < 1e-6)
            {
                double v1 = p.Alpha / Math.Pow(F, 1 - p.Beta);
                double v2 = ((1 - p.Beta) * (1 - p.Beta) / 24.0) * (p.Alpha * p.Alpha) / Math.Pow(F, 2 - 2 * p.Beta);
                double v3 = (p.Rho * p.Beta * p.Nu * p.Alpha) / (4.0 * Math.Pow(F, 1 - p.Beta));
                double v4 = (2 - 3 * p.Rho * p.Rho) / 24.0 * (p.Nu * p.Nu);
                return v1 * (1.0 + (v2 + v3 + v4) * T);
            }
            else
            {
                double logFK = Math.Log(F / K);
                double fkb = Math.Pow(F * K, (1 - p.Beta) / 2.0);
                double z = (p.Nu / p.Alpha) * fkb * logFK;
                double x = Math.Log((Math.Sqrt(1 - 2 * p.Rho * z + z * z) + z - p.Rho) / (1 - p.Rho));

                double term1 = p.Alpha / (fkb * (1 + Math.Pow(1 - p.Beta, 2) / 24.0 * Math.Pow(logFK, 2) + Math.Pow(1 - p.Beta, 4) / 1920.0 * Math.Pow(logFK, 4)));
                double term2 = z / x;
                double term3 = 1 + (((Math.Pow(1 - p.Beta, 2) / 24.0) * (p.Alpha * p.Alpha) / Math.Pow(F * K, 1 - p.Beta)) + (p.Rho * p.Beta * p.Nu * p.Alpha) / (4.0 * fkb) + ((2 - 3 * p.Rho * p.Rho) / 24.0) * (p.Nu * p.Nu)) * T;

                return term1 * term2 * term3;
            }
        }
        #endregion
    }
}
