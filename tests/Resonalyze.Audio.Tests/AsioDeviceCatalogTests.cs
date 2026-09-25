namespace Resonalyze.Audio.Tests;

public sealed class AsioDeviceCatalogTests
{
    [Fact]
    public void ARateRefusedWithAnErrorCode_IsUnsupported_AndTheOtherRatesStillProbe()
    {
        static bool TopsAt192k(int rate) => rate <= 192_000
            ? true
            : throw new InvalidOperationException(
                "Error code [ASE_NotPresent] while calling ASIO method <canSampleRate>");

        int[] supported = SampleRateCatalog.GetCandidateRates()
            .Where(rate => AsioDeviceCatalog.SafeIsSampleRateSupported(TopsAt192k, rate))
            .ToArray();

        Assert.Equal([44_100, 48_000, 88_200, 96_000, 176_400, 192_000], supported);
    }
}
