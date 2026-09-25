namespace Resonalyze.Audio.Tests;

public sealed class AsioDeviceCatalogTests
{
    private const string NotPresent = "Error code [ASE_NotPresent] while calling ASIO method <canSampleRate>";

    [Fact]
    public void ARateRefusedWithAnErrorCode_IsUnsupported_AndTheOtherRatesStillProbe()
    {
        static bool TopsAt192k(int rate) => rate <= 192_000
            ? true
            : throw new InvalidOperationException(NotPresent);

        (bool supportsSampleRate, int[] supported) = AsioDeviceCatalog.ProbeSampleRates(
            TopsAt192k,
            48_000,
            SampleRateCatalog.GetCandidateRates());

        Assert.True(supportsSampleRate);
        Assert.Equal([44_100, 48_000, 88_200, 96_000, 176_400, 192_000], supported);
    }

    [Fact]
    public void ADriverThatThrowsOnEveryRate_KeepsItsError()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => AsioDeviceCatalog.ProbeSampleRates(
            _ => throw new InvalidOperationException(NotPresent),
            48_000,
            SampleRateCatalog.GetCandidateRates()));

        Assert.Equal(NotPresent, exception.Message);
    }

    [Fact]
    public void ADriverWithoutAClock_RefusesEveryRateWithoutAnError()
    {
        (bool supportsSampleRate, int[] supported) = AsioDeviceCatalog.ProbeSampleRates(
            _ => false,
            48_000,
            SampleRateCatalog.GetCandidateRates());

        Assert.False(supportsSampleRate);
        Assert.Empty(supported);
    }

    [Fact]
    public void TheSelectedRateTaken_KeepsTheDriverUsable_WhenEveryCandidateThrows()
    {
        (bool supportsSampleRate, int[] supported) = AsioDeviceCatalog.ProbeSampleRates(
            rate => rate == 32_000 ? true : throw new InvalidOperationException(NotPresent),
            32_000,
            SampleRateCatalog.GetCandidateRates());

        Assert.True(supportsSampleRate);
        Assert.Empty(supported);
    }
}
