using System;
using System.Collections.Generic;

namespace KGIOptionPricing;

public static class MeanReversionSessionParameterWriter
{
    public static void Add(IDictionary<string, string> values,
        string dayStart, string dayStop, string nightStart, string nightStop)
    {
        ArgumentNullException.ThrowIfNull(values);
        values["DayStartTime"] = dayStart.Trim();
        values["DayStopTime"] = dayStop.Trim();
        values["NightStartTime"] = nightStart.Trim();
        values["NightStopTime"] = nightStop.Trim();
    }
}
