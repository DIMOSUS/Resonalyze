namespace Resonalyze.App.Tests;

public sealed class SweepBandMigrationTests
{
    [Fact]
    public void Settings_LegacyOctaves_MigrateAndClampIntoTheAllowedRange()
    {
        // A pre-band settings file carries only the (read-only) octave count of 12
        // with no explicit band; the derived 5.4 Hz–22.05 kHz band clamps to the
        // 20 Hz–20 kHz default.
        var settings = new MeasurementSettingsFile.SweepMeasurementSettings
        {
            Octaves = 12,
            LowFrequencyHz = 0,
            HighFrequencyHz = 0,
            SampleRate = 44_100
        };

        (double lowHz, double highHz) = settings.ResolveBand(44_100);

        Assert.Equal(20.0, lowHz);
        Assert.Equal(20_000.0, highHz);
    }

    [Fact]
    public void Settings_ExplicitBand_IsPreservedWithinTheAllowedRange()
    {
        var settings = new MeasurementSettingsFile.SweepMeasurementSettings
        {
            LowFrequencyHz = 30,
            HighFrequencyHz = 18_000,
            SampleRate = 48_000
        };

        (double lowHz, double highHz) = settings.ResolveBand(48_000);

        Assert.Equal(30.0, lowHz);
        Assert.Equal(18_000.0, highHz);
    }

    [Fact]
    public void ImpulseResponseFile_LegacyOctaves_DeriveTheNyquistBand()
    {
        // Legacy impulse-response files keep their exact measured band: the sweep
        // ran from Nyquist / 2^octaves up to Nyquist (no [20, 20000] clamp, so the
        // harmonic geometry is unchanged on reload).
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
        // A pre-band file has no request to widen: its octave count already names
        // the band that was swept, so it must come back untouched. Feeding it back
        // through the guard bands would move the harmonic packets.
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
        // The ratio the harmonic offsets are keyed to survives exactly.
        Assert.Equal(4096.0, highHz / lowHz, 6);
    }

    [Fact]
    public void AchievedBand_WhenOnlyTheRequestWasStored_IsRederivedNotAssumed()
    {
        // Files written by the band-based generator before the achieved band was
        // stored alongside it. The request must not be mistaken for what was
        // swept: ComputeSpec is deterministic, so the real edges come back.
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
