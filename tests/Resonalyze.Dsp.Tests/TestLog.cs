namespace Resonalyze.Dsp.Tests;

/// <summary>
/// Shared log-text lookups for the alignment engine test classes, which pin
/// diagnostic markers to specific lines of the engine's run log.
/// </summary>
internal static class TestLog
{
    /// <summary>The first log line containing <paramref name="contains"/>.</summary>
    internal static string Line(string log, string contains) =>
        log.Split('\n').First(line => line.Contains(contains));
}
