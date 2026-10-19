namespace KGIOptionPricing.Strategies.NikkeiRlDirection;

public sealed record RlSessionWindow(string Name,DateTimeOffset Open,DateTimeOffset Close)
{
    public DateTimeOffset EntryCutoff=>Close.AddSeconds(-310);
    public bool AllowsEntry(DateTimeOffset at)=>at>=Open&&at<EntryCutoff;
}

public static class NikkeiRlSession
{
    public static RlSessionWindow? At(DateTimeOffset at,bool enableNight=true)
    {
        var local=at.ToOffset(TimeSpan.FromHours(8));var time=local.TimeOfDay;
        DateTimeOffset Time(DateTime date,double hours)=>new(date.AddHours(hours),local.Offset);
        if(time>=TimeSpan.FromHours(7.75)&&time<TimeSpan.FromHours(14.75))
            return new("日盤",Time(local.Date,7.75),Time(local.Date,14.75));
        if(enableNight&&(time>=TimeSpan.FromHours(16)||time<TimeSpan.FromHours(5)))
        {
            var date=time<TimeSpan.FromHours(5)?local.Date.AddDays(-1):local.Date;
            return new("夜盤",Time(date,16),Time(date.AddDays(1),5));
        }
        return null;
    }
}
