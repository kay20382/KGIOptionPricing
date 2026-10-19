using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace KGIOptionPricing
{
    public class TradingCalendar
    {
        private HashSet<DateTime> holidays;
        public double DaysInYear { get; set; } = 240.0;
        public double MinTValue { get; set; } = 0.001;
        public TimeSpan ExpirationTime { get; set; } = new TimeSpan(13, 30, 0);

        public enum TtmMethod
        {
            Daily,
            ExactSeconds
        }

        public TtmMethod Method { get; set; } = TtmMethod.ExactSeconds;

        public class IntradayWeight
        {
            public string Name { get; set; } = string.Empty;
            public TimeSpan StartTime { get; set; }
            public TimeSpan EndTime { get; set; }
            public double Weight { get; set; }

            public bool CrossesMidnight => StartTime > EndTime;
        }

        public List<IntradayWeight> IntradayWeights { get; set; } = new List<IntradayWeight>()
        {
            new IntradayWeight { Name = "DaySession", StartTime = new TimeSpan(8, 45, 0), EndTime = new TimeSpan(13, 45, 0), Weight = 0.5 },
            new IntradayWeight { Name = "NightSession", StartTime = new TimeSpan(15, 0, 0), EndTime = new TimeSpan(5, 0, 0), Weight = 0.5 }
        };

        public TradingCalendar()
        {
            holidays = new HashSet<DateTime>();
            LoadHolidaysFromFile("holidays.txt");
        }

        public void LoadConfigFromJson(JsonElement root)
        {
            try
            {
                if (root.TryGetProperty("TTM_Config", out var ttmElem) && ttmElem.ValueKind == JsonValueKind.Object)
                {
                    if (ttmElem.TryGetProperty("Method", out var mElem) && !string.IsNullOrWhiteSpace(mElem.GetString()))
                    {
                        string mStr = mElem.GetString()!;
                        if (Enum.TryParse<TtmMethod>(mStr, true, out var parsedMethod))
                        {
                            Method = parsedMethod;
                        }
                    }

                    if (ttmElem.TryGetProperty("DaysInYear", out var diyElem) && diyElem.TryGetDouble(out double diyVal))
                    {
                        DaysInYear = diyVal > 0 ? diyVal : 240.0;
                    }

                    if (ttmElem.TryGetProperty("MinTValue", out var mtvElem) && mtvElem.TryGetDouble(out double mtvVal))
                    {
                        MinTValue = mtvVal >= 0 ? mtvVal : 0.001;
                    }

                    if (ttmElem.TryGetProperty("ExpirationTime", out var expElem) && TimeSpan.TryParse(expElem.GetString(), out TimeSpan expTs))
                    {
                        ExpirationTime = expTs;
                    }

                    if (ttmElem.TryGetProperty("IntradayWeights", out var weightsElem) && weightsElem.ValueKind == JsonValueKind.Array)
                    {
                        var newWeights = new List<IntradayWeight>();
                        foreach (var wObj in weightsElem.EnumerateArray())
                        {
                            string name = wObj.TryGetProperty("Name", out var nEl) ? nEl.GetString() ?? "" : "";
                            TimeSpan start = wObj.TryGetProperty("StartTime", out var sEl) && TimeSpan.TryParse(sEl.GetString(), out var st) ? st : TimeSpan.Zero;
                            TimeSpan end = wObj.TryGetProperty("EndTime", out var eEl) && TimeSpan.TryParse(eEl.GetString(), out var et) ? et : TimeSpan.Zero;
                            double wVal = wObj.TryGetProperty("Weight", out var wEl) && wEl.TryGetDouble(out var wv) ? wv : 0.0;

                            newWeights.Add(new IntradayWeight
                            {
                                Name = name,
                                StartTime = start,
                                EndTime = end,
                                Weight = wVal
                            });
                        }

                        if (newWeights.Count > 0)
                        {
                            IntradayWeights = newWeights;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Calendar Config Error] {ex.Message}");
            }
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

        public bool IsTradingDay(DateTime date)
        {
            if (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
                return false;

            if (holidays.Contains(date.Date))
                return false;

            return true;
        }

        public double CalculateTTM(DateTime startNow, DateTime endDate)
        {
            DateTime exactExpDateTime = endDate.Date.Add(ExpirationTime);
            if (startNow >= exactExpDateTime) return MinTValue;

            double daysRemaining = 0.0;

            if (Method == TtmMethod.Daily)
            {
                int tradingDays = 0;
                DateTime current = startNow.Date;
                while (current < endDate.Date)
                {
                    if (IsTradingDay(current)) tradingDays++;
                    current = current.AddDays(1);
                }
                daysRemaining = tradingDays;
            }
            else
            {
                daysRemaining = CalculateExactSecondsRemainingDays(startNow, exactExpDateTime);
            }

            double ttm = daysRemaining / DaysInYear;
            return Math.Max(ttm, MinTValue);
        }

        private double CalculateExactSecondsRemainingDays(DateTime startNow, DateTime exactExpDateTime)
        {
            double totalWeightDays = 0.0;

            // 1. Calculate remaining weight for TODAY (startNow -> 24:00)
            if (IsTradingDay(startNow.Date))
            {
                totalWeightDays += CalculateDayRemainingWeight(startNow);
            }

            // 2. Calculate full trading days (tomorrow -> day before expiration)
            DateTime current = startNow.Date.AddDays(1);
            DateTime expDateOnly = exactExpDateTime.Date;
            while (current < expDateOnly)
            {
                if (IsTradingDay(current))
                {
                    totalWeightDays += 1.0;
                }
                current = current.AddDays(1);
            }

            // 3. Calculate expiration day weight (00:00 -> exactExpDateTime)
            if (startNow.Date <= expDateOnly && IsTradingDay(expDateOnly))
            {
                if (startNow.Date == expDateOnly)
                {
                    totalWeightDays = CalculateIntervalWeight(startNow, exactExpDateTime);
                }
                else
                {
                    DateTime expDayStart = expDateOnly;
                    totalWeightDays += CalculateIntervalWeight(expDayStart, exactExpDateTime);
                }
            }

            return totalWeightDays;
        }

        private double CalculateDayRemainingWeight(DateTime startNow)
        {
            DateTime endOfDay = startNow.Date.AddDays(1);
            return CalculateIntervalWeight(startNow, endOfDay);
        }

        private double CalculateIntervalWeight(DateTime intervalStart, DateTime intervalEnd)
        {
            if (intervalStart >= intervalEnd) return 0.0;

            double weightSum = 0.0;
            TimeSpan startTime = intervalStart.TimeOfDay;
            TimeSpan endTime = intervalEnd.TimeOfDay;

            foreach (var w in IntradayWeights)
            {
                if (w.Weight <= 0) continue;

                if (!w.CrossesMidnight)
                {
                    double totalSec = (w.EndTime - w.StartTime).TotalSeconds;
                    if (totalSec <= 0) continue;

                    TimeSpan overlapStart = startTime > w.StartTime ? startTime : w.StartTime;
                    TimeSpan overlapEnd = endTime < w.EndTime ? endTime : w.EndTime;

                    if (overlapStart < overlapEnd)
                    {
                        double overlapSec = (overlapEnd - overlapStart).TotalSeconds;
                        weightSum += (overlapSec / totalSec) * w.Weight;
                    }
                }
                else
                {
                    double part1TotalSec = (TimeSpan.FromHours(24) - w.StartTime).TotalSeconds;
                    double part2TotalSec = w.EndTime.TotalSeconds;
                    double totalSec = part1TotalSec + part2TotalSec;
                    if (totalSec <= 0) continue;

                    // Overlap with Part 1 (15:00 -> 24:00)
                    if (startTime < TimeSpan.FromHours(24) && endTime > w.StartTime)
                    {
                        TimeSpan oStart = startTime > w.StartTime ? startTime : w.StartTime;
                        TimeSpan oEnd = endTime;
                        if (oStart < oEnd)
                        {
                            double oSec = (oEnd - oStart).TotalSeconds;
                            weightSum += (oSec / totalSec) * w.Weight;
                        }
                    }

                    // Overlap with Part 2 (00:00 -> 05:00)
                    if (startTime < w.EndTime)
                    {
                        TimeSpan oStart = startTime;
                        TimeSpan oEnd = endTime < w.EndTime ? endTime : w.EndTime;
                        if (oStart < oEnd)
                        {
                            double oSec = (oEnd - oStart).TotalSeconds;
                            weightSum += (oSec / totalSec) * w.Weight;
                        }
                    }
                }
            }

            return weightSum;
        }
    }
}
