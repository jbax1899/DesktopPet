using System.Globalization;

namespace DesktopPet.App.Memory;

public static class TemporalContextFormatter
{
    public static string Format(DateTimeOffset now, TimeZoneInfo timeZone)
    {
        var localNow = TimeZoneInfo.ConvertTime(now, timeZone);
        return string.Join(
            Environment.NewLine,
            $"Current local date and time: {localNow.ToString("dddd, MMMM d, yyyy 'at' h:mm tt", CultureInfo.InvariantCulture)} ({FormatUtcOffset(localNow.Offset)})",
            $"Time zone: {timeZone.Id}",
            $"Current UTC date and time: {now.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)}");
    }

    public static string FormatUtcOffset(TimeSpan offset)
    {
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var absoluteOffset = offset.Duration();
        return $"UTC{sign}{absoluteOffset.Hours:00}:{absoluteOffset.Minutes:00}";
    }
}
