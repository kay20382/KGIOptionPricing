namespace KGIOptionPricing.Strategies.TradeFlowTrend;
public sealed record TradeFlowSessionWindow(string SessionId,DateTimeOffset MarketOpen,DateTimeOffset Start,DateTimeOffset Stop,DateTimeOffset MarketClose);
public sealed class TradeFlowSessionSchedule(TradeFlowParameters parameters)
{
    public TradeFlowSessionWindow? Resolve(DateTimeOffset at)
    {
        var local=at.ToOffset(TimeSpan.FromHours(8));var day=DateOnly.FromDateTime(local.DateTime);var time=TimeOnly.FromDateTime(local.DateTime);
        if(time>=new TimeOnly(7,45) && time<new TimeOnly(14,45))return Window(day,false);
        if(time>=new TimeOnly(16,0))return Window(day,true);
        return time<new TimeOnly(5,0)?Window(day.AddDays(-1),true):null;
    }
    private TradeFlowSessionWindow Window(DateOnly date,bool night)
    {
        DateTimeOffset At(DateOnly d,TimeOnly t)=>new(d.ToDateTime(t),TimeSpan.FromHours(8));
        return night?new($"{date:yyyy-MM-dd}/N",At(date,new(16,0)),At(date,parameters.NightStartTime),At(date.AddDays(1),parameters.NightStopTime),At(date.AddDays(1),new(5,0))):
            new($"{date:yyyy-MM-dd}/D",At(date,new(7,45)),At(date,parameters.DayStartTime),At(date,parameters.DayStopTime),At(date,new(14,45)));
    }
    public IReadOnlyList<DateTimeOffset> StopsBetween(DateTimeOffset from,DateTimeOffset to)
    {
        var results=new List<DateTimeOffset>();var day=DateOnly.FromDateTime(from.ToOffset(TimeSpan.FromHours(8)).DateTime).AddDays(-1);
        var end=DateOnly.FromDateTime(to.ToOffset(TimeSpan.FromHours(8)).DateTime);
        for(;day<=end;day=day.AddDays(1))foreach(var night in new[]{false,true}){var stop=Window(day,night).Stop;if(stop>from && stop<=to)results.Add(stop);}
        return results.Order().ToArray();
    }
    public bool Enabled(TradeFlowSessionWindow w)=>w.SessionId.EndsWith("/N")?parameters.NightEnabled:parameters.DayEnabled;
}
