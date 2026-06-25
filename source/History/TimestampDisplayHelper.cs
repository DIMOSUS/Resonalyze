namespace Resonalyze.History;

internal static class TimestampDisplayHelper
{
    public static string Format(DateTimeOffset timestamp) =>
        timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
}
