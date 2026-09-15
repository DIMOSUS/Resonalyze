using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

// The logarithmic flag became a three-way scale: old files must open the view as their user left it, and new files stay readable by old builds.
public sealed class ImpulseResponseSettingsTests
{
    [Theory]
    [InlineData(true, ImpulseAmplitudeScale.Decibels)]
    [InlineData(false, ImpulseAmplitudeScale.Linear)]
    public void LegacyLogarithmicFlagSelectsTheScale(
        bool logarithmic, ImpulseAmplitudeScale expected)
    {
        var settings = new MeasurementSettingsFile();
        settings.ImpulseResponse.Logarithmic = logarithmic;
        settings.ImpulseResponse.AmplitudeScale = null;
        var options = new ImpulseResponseOptions();

        settings.ImpulseResponse.ApplyTo(options);

        Assert.Equal(expected, options.AmplitudeScale);
    }

    [Fact]
    public void AmplitudeScaleWinsOverTheLegacyFlagWhenBothArePresent()
    {
        var settings = new MeasurementSettingsFile();
        settings.ImpulseResponse.Logarithmic = true;
        settings.ImpulseResponse.AmplitudeScale = ImpulseAmplitudeScale.PercentOfPeak;
        var options = new ImpulseResponseOptions();

        settings.ImpulseResponse.ApplyTo(options);

        Assert.Equal(ImpulseAmplitudeScale.PercentOfPeak, options.AmplitudeScale);
    }

    [Fact]
    public void CaptureStillWritesTheLegacyFlagSoAnOlderBuildCanParseTheFile()
    {
        // Older builds read Logarithmic into a non-nullable bool: a null fails the whole settings file.
        var options = new ImpulseResponseOptions
        {
            AmplitudeScale = ImpulseAmplitudeScale.Decibels
        };

        MeasurementSettingsFile.ImpulseResponseSettings captured =
            MeasurementSettingsFile.ImpulseResponseSettings.Capture(options);

        Assert.True(captured.Logarithmic);
        Assert.Equal(ImpulseAmplitudeScale.Decibels, captured.AmplitudeScale);
    }

    [Fact]
    public void EveryViewSettingSurvivesARoundTrip()
    {
        var options = new ImpulseResponseOptions
        {
            Length = 16_384,
            AmplitudeScale = ImpulseAmplitudeScale.PercentOfPeak,
            TimeUnit = ImpulseTimeUnit.Samples,
            TimeOrigin = ImpulseTimeOrigin.FirstArrival,
            EnvelopeSmoothingMs = 0.25,
            Invert = true,
            NormalizeStepToImpulsePeak = false,
            ShowImpulse = false,
            ShowEnvelope = true,
            ShowStep = true,
            ShowAutocorrelation = false
        };

        var restored = new ImpulseResponseOptions();
        MeasurementSettingsFile.ImpulseResponseSettings.Capture(options).ApplyTo(restored);

        Assert.Equal(options.Length, restored.Length);
        Assert.Equal(options.AmplitudeScale, restored.AmplitudeScale);
        Assert.Equal(options.TimeUnit, restored.TimeUnit);
        Assert.Equal(options.TimeOrigin, restored.TimeOrigin);
        Assert.Equal(options.EnvelopeSmoothingMs, restored.EnvelopeSmoothingMs);
        Assert.Equal(options.Invert, restored.Invert);
        Assert.Equal(
            options.NormalizeStepToImpulsePeak, restored.NormalizeStepToImpulsePeak);
        Assert.Equal(options.ShowImpulse, restored.ShowImpulse);
        Assert.Equal(options.ShowEnvelope, restored.ShowEnvelope);
        Assert.Equal(options.ShowStep, restored.ShowStep);
        Assert.Equal(options.ShowAutocorrelation, restored.ShowAutocorrelation);
    }

    [Fact]
    public void AnOutOfRangeSmoothingDurationIsClamped()
    {
        var settings = new MeasurementSettingsFile();
        settings.ImpulseResponse.EnvelopeSmoothingMs = 5_000.0;
        var options = new ImpulseResponseOptions();

        settings.ImpulseResponse.ApplyTo(options);

        Assert.Equal(100.0, options.EnvelopeSmoothingMs);
    }
}
