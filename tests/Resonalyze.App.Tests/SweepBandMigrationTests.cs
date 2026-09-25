using System.Text.Json;

namespace Resonalyze.App.Tests;

public sealed class SweepBandMigrationTests
{
    [Fact]
    public void Settings_WithoutABand_SweepTheWholeAllowedRange()
    {
        var settings = new MeasurementSettingsFile.SweepMeasurementSettings { SampleRate = 44_100 };

        (double lowHz, double highHz) = settings.ResolveBand();

        Assert.Equal(2.0, lowHz);
        Assert.Equal(20_000.0, highHz);
    }

    [Fact]
    public void Settings_APreBandFile_SweepsTheWholeAllowedRange()
    {
        var settings = JsonSerializer.Deserialize<MeasurementSettingsFile.SweepMeasurementSettings>(
            """{ "Octaves": 10, "SampleRate": 48000 }""")!;

        (double lowHz, double highHz) = settings.ResolveBand();

        Assert.Equal(2.0, lowHz);
        Assert.Equal(20_000.0, highHz);
    }

    [Theory]
    [InlineData(30, 18_000, 30, 18_000)]
    [InlineData(2, 20_000, 2, 20_000)]
    [InlineData(1, 200, 2, 200)]
    [InlineData(10, 40_000, 10, 20_000)]
    public void Settings_ExplicitBand_IsPreservedWithinTheAllowedRange(
        double low, double high, double expectedLow, double expectedHigh)
    {
        var settings = new MeasurementSettingsFile.SweepMeasurementSettings
        {
            LowFrequencyHz = low,
            HighFrequencyHz = high,
            SampleRate = 48_000
        };

        (double lowHz, double highHz) = settings.ResolveBand();

        Assert.Equal(expectedLow, lowHz);
        Assert.Equal(expectedHigh, highHz);
    }

    [Fact]
    public void ImpulseResponseFile_LegacyOctaves_DeriveTheNyquistBand()
    {
        // Legacy IR files keep Nyquist/2^octaves..Nyquist unclamped, so harmonic geometry is unchanged.
        (double lowHz, double highHz) = ImpulseResponseFile.ResolveSweepBand(
            lowFrequencyHz: 0,
            highFrequencyHz: 0,
            octaves: 10,
            sampleRate: 48_000);

        Assert.Equal(24_000.0, highHz);
        Assert.Equal(24_000.0 / 1024.0, lowHz, 6);
    }

    [Fact]
    public void ImpulseResponseFile_ExplicitBand_IsReturnedUnchanged()
    {
        (double lowHz, double highHz) = ImpulseResponseFile.ResolveSweepBand(
            lowFrequencyHz: 25,
            highFrequencyHz: 19_000,
            octaves: 0,
            sampleRate: 48_000);

        Assert.Equal(25.0, lowHz);
        Assert.Equal(19_000.0, highHz);
    }

    [Fact]
    public void AchievedBand_StoredExplicitly_IsReturnedUnchanged()
    {
        (double lowHz, double highHz) = ImpulseResponseFile.ResolveAchievedSweepBand(
            achievedLowFrequencyHz: 14.103,
            achievedHighFrequencyHz: 23_996.8,
            lowFrequencyHz: 20,
            highFrequencyHz: 20_000,
            octaves: 0,
            sampleRate: 48_000,
            sweepDurationSeconds: 10.55);

        Assert.Equal(14.103, lowHz, 6);
        Assert.Equal(23_996.8, highHz, 6);
    }

    [Fact]
    public void AchievedBand_ForALegacyFile_IsTheBandTheSweepRan()
    {
        (double lowHz, double highHz) = ImpulseResponseFile.ResolveAchievedSweepBand(
            achievedLowFrequencyHz: 0,
            achievedHighFrequencyHz: 0,
            lowFrequencyHz: 0,
            highFrequencyHz: 0,
            octaves: 12,
            sampleRate: 48_000,
            sweepDurationSeconds: 1.0);

        Assert.Equal(24_000.0, highHz);
        Assert.Equal(24_000.0 / 4096.0, lowHz, 9);
        Assert.Equal(4096.0, highHz / lowHz, 6);
    }

    [Fact]
    public void AchievedBand_WhenOnlyTheRequestWasStored_IsRederivedNotAssumed()
    {
        // ComputeSpec is deterministic, so the real edges are recovered from the stored request.
        ExpSweepSpec expected = ExponentialSineSweep.ComputeSpec(20, 20_000, 10.55, 48_000);
        Assert.True(expected.IsValid);

        (double lowHz, double highHz) = ImpulseResponseFile.ResolveAchievedSweepBand(
            achievedLowFrequencyHz: 0,
            achievedHighFrequencyHz: 0,
            lowFrequencyHz: 20,
            highFrequencyHz: 20_000,
            octaves: 0,
            sampleRate: 48_000,
            sweepDurationSeconds: 10.55);

        Assert.Equal(expected.LowFrequencyHz, lowHz, 9);
        Assert.Equal(expected.HighFrequencyHz, highHz, 9);
        Assert.True(lowHz < 20.0, "the swept band encloses the request");
        Assert.True(highHz > 20_000.0, "the swept band encloses the request");
    }
}
