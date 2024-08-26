namespace Arius.Core.New.UnitTests;

internal static class DateTimeExtensions
{
    public static DateTime TruncateToSeconds(this DateTime dateTime)
    {
        return dateTime.AddTicks(-(dateTime.Ticks % TimeSpan.TicksPerSecond));
    }
}