namespace Resonalyze.Audio;

public static class SampleRateCatalog
{
    /// <summary>The lowest rate a measurement is taken and stored at.</summary>
    public const int MinimumSampleRate = 44_100;

    private static readonly int[] StandardRates =
    [
        44_100,
        48_000,
        88_200,
        96_000,
        176_400,
        192_000,
        352_800,
        384_000
    ];

    public static IReadOnlyList<int> GetCandidateRates(int minimumSampleRate = MinimumSampleRate)
    {
        return StandardRates
            .Where(rate => rate >= minimumSampleRate)
            .ToArray();
    }
}
