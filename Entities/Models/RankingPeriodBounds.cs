namespace Entities.Models;

public static class RankingPeriodBounds
{
    public static (DateTimeOffset StartUtc, DateTimeOffset EndUtc) Current(RankingPeriod period, DateTimeOffset now)
    {
        var utc = now.ToUniversalTime();
        var date = utc.Date;
        var start = period switch
        {
            RankingPeriod.Daily => new DateTimeOffset(date, TimeSpan.Zero),
            RankingPeriod.Weekly => new DateTimeOffset(date.AddDays(-((7 + (int)date.DayOfWeek - (int)DayOfWeek.Monday) % 7)), TimeSpan.Zero),
            RankingPeriod.Monthly => new DateTimeOffset(new DateTime(date.Year, date.Month, 1), TimeSpan.Zero),
            _ => throw new ArgumentOutOfRangeException(nameof(period))
        };
        return (start, Next(period, start));
    }

    public static DateTimeOffset Next(RankingPeriod period, DateTimeOffset startUtc) => period switch
    {
        RankingPeriod.Daily => startUtc.AddDays(1),
        RankingPeriod.Weekly => startUtc.AddDays(7),
        RankingPeriod.Monthly => startUtc.AddMonths(1),
        _ => throw new ArgumentOutOfRangeException(nameof(period))
    };

    public static bool IsBoundary(RankingPeriod period, DateTimeOffset value)
    {
        if (value.Offset != TimeSpan.Zero) return false;
        var utc = value.ToUniversalTime();
        if (utc.TimeOfDay != TimeSpan.Zero) return false;
        return period switch
        {
            RankingPeriod.Daily => true,
            RankingPeriod.Weekly => utc.DayOfWeek == DayOfWeek.Monday,
            RankingPeriod.Monthly => utc.Day == 1,
            _ => false
        };
    }
}
