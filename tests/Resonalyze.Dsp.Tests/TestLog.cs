namespace Resonalyze.Dsp.Tests;

internal static class TestLog
{
    internal static string Line(string log, string contains) =>
        log.Split('\n').First(line => line.Contains(contains));
}
