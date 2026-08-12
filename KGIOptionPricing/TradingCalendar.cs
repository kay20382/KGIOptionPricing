using System;
using System.Collections.Generic;

namespace KGIOptionPricing
{
    public class TradingCalendar
    {
        private HashSet<DateTime> holidays;
        public double DaysInYear { get; set; } = 240.0;

        public TradingCalendar()
        {
            holidays = new HashSet<DateTime>();
            LoadHolidaysFromFile("holidays.txt");
        }

        private void LoadHolidaysFromFile(string filePath)
        {
            try
            {
                if (System.IO.File.Exists(filePath))
                {
                    var lines = System.IO.File.ReadAllLines(filePath);
                    foreach (var line in lines)
                    {
                        if (DateTime.TryParse(line.Trim(), out DateTime holiday))
                        {
                            holidays.Add(holiday.Date);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Calendar Error] Failed to load {filePath}: {ex.Message}");
            }
        }

        public void AddHoliday(DateTime holiday)
        {
            holidays.Add(holiday.Date);
        }

        public void AddHolidays(IEnumerable<DateTime> dates)
        {
            foreach (var d in dates)
            {
                holidays.Add(d.Date);
            }
        }

        public void RemoveHoliday(DateTime holiday)
        {
            holidays.Remove(holiday.Date);
        }

        public bool IsTradingDay(DateTime date)
        {
            if (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
                return false;

            if (holidays.Contains(date.Date))
                return false;

            return true;
        }

        public enum TtmMethod
        {
            CalendarDays,
            TradingDays,
            ExactSeconds,
            WeightedIntraday
        }

        public TtmMethod Method { get; set; } = TtmMethod.TradingDays;

        public class IntradayWeight
        {
            public TimeSpan StartTime { get; set; }
            public TimeSpan EndTime { get; set; }
            public double Weight { get; set; }

            public bool CrossesMidnight => StartTime > EndTime;
        }

        public List<IntradayWeight> IntradayWeights { get; set; } = new List<IntradayWeight>()
        {
            new IntradayWeight { StartTime = new TimeSpan(8, 45, 0), EndTime = new TimeSpan(13, 45, 0), Weight = 0.5 },
            new IntradayWeight { StartTime = new TimeSpan(15, 0, 0), EndTime = new TimeSpan(9, 30, 0), Weight = 0.1 }, // Crosses midnight
            new IntradayWeight { StartTime = new TimeSpan(9, 30, 0), EndTime = new TimeSpan(5, 0, 0), Weight = 0.4 }   // Example config
        };

        public double CalculateTTM(DateTime startDate, DateTime endDate)
        {
            if (startDate.Date > endDate.Date) return 0.0;

            double ttm = 0.0;
            switch (Method)
            {
                case TtmMethod.CalendarDays:
                    ttm = (endDate.Date - startDate.Date).TotalDays / 365.0;
                    break;

                case TtmMethod.ExactSeconds:
                    // Assume 13:30 expiration for TXO
                    DateTime exactEndTime = endDate.Date.AddHours(13).AddMinutes(30);
                    ttm = (exactEndTime - startDate).TotalSeconds / (365.0 * 24 * 60 * 60);
                    break;

                case TtmMethod.WeightedIntraday:
                    ttm = CalculateWeightedIntradayTTM(startDate, endDate) / DaysInYear;
                    break;

                case TtmMethod.TradingDays:
                default:
                    int tradingDays = 0;
                    DateTime current = startDate.Date;
                    DateTime end = endDate.Date;
                    while (current < end)
                    {
                        if (IsTradingDay(current)) tradingDays++;
                        current = current.AddDays(1);
                    }
                    ttm = tradingDays / DaysInYear;
                    break;
            }

            // Provide a minimum non-zero TTM on expiration day to prevent Black-Scholes IV calculation failure
            if (ttm <= 0.0)
            {
                ttm = 0.001; // Small fraction of a year (roughly 0.25 of a trading day)
            }

            return ttm;
        }

        private double CalculateWeightedIntradayTTM(DateTime start, DateTime end)
        {
            // A simplified heuristic: count full trading days + partial day weighting.
            // For a robust intraday curve, we iterate minute by minute or exact overlap.
            // To keep performance high, we'll calculate exact seconds of overlap for each interval.
            double totalWeight = 0;
            DateTime current = start;

            // Step by 1 minute
            while (current < end)
            {
                if (IsTradingDay(current))
                {
                    TimeSpan tod = current.TimeOfDay;
                    foreach (var w in IntradayWeights)
                    {
                        bool inInterval = false;
                        if (w.CrossesMidnight)
                        {
                            inInterval = (tod >= w.StartTime || tod < w.EndTime);
                        }
                        else
                        {
                            inInterval = (tod >= w.StartTime && tod < w.EndTime);
                        }

                        if (inInterval)
                        {
                            // Weight per minute (assuming weight is for the whole interval block)
                            double totalMinutes = w.CrossesMidnight 
                                ? (TimeSpan.FromHours(24) - w.StartTime + w.EndTime).TotalMinutes
                                : (w.EndTime - w.StartTime).TotalMinutes;
                            
                            totalWeight += w.Weight / totalMinutes;
                            break;
                        }
                    }
                }
                current = current.AddMinutes(1);
            }

            return totalWeight;
        }
    }
}
