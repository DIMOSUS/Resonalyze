using System.Numerics;
using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze.App.Tests;

public sealed class PlotModelFactoryTests
{
    [Fact]
    public void MeasurementPlotTitles_IncludeImpulseResponseFileName()
    {
        using var measurement = new ExpSweepMeasurement();
        using var noiseMeasurement = new NoiseMeasurement();
        measurement.RestoreImpulseResponse(
            octaves: 12,
            sampleRate: 44_100,
            bits: 24,
            sweepDurationSeconds: 1.0,
            playChannel: PlaybackChannel.Mono,
            sweepDeconvolutionImpulseResponse:
            [
                Complex.Zero,
                Complex.One,
                Complex.Zero
            ],
            sweepDeconvolutionPeakIndex: 1);

        PlotModelFactory factory = CreateFactory(measurement, noiseMeasurement);
        factory.SetImpulseResponseFileName(@"C:\Temp\My Measurement.json");

        Assert.Equal(
            "Impulse Response - My Measurement.json",
            factory.CreateImpulseResponse(includeCurves: false).Title);
        Assert.Equal(
            "Frequency Response - My Measurement.json",
            factory.CreateFrequencyResponse(includeCurves: false).Title);
        Assert.Equal(
            "Phase Response - My Measurement.json",
            factory.CreatePhaseResponse(includeCurves: false).Title);
        Assert.Equal(
            "Group Delay - My Measurement.json",
            factory.CreateGroupDelay(includeCurves: false).Title);
        Assert.Equal(
            "Fourier Waterfall - My Measurement.json",
            factory.CreateWaterfall(includeCurves: false).Title);
        Assert.Equal(
            "Burst Decay - My Measurement.json",
            factory.CreateBurstDecay(includeCurves: false).Title);
        Assert.Equal(
            "Autocorrelation - My Measurement.json",
            factory.CreateAutocorrelation(includeCurves: false).Title);
    }

    [Fact]
    public void MeasurementPlotTitles_FallBackToBaseTitlesWithoutFileName()
    {
        using var measurement = new ExpSweepMeasurement();
        using var noiseMeasurement = new NoiseMeasurement();
        measurement.RestoreImpulseResponse(
            octaves: 12,
            sampleRate: 44_100,
            bits: 24,
            sweepDurationSeconds: 1.0,
            playChannel: PlaybackChannel.Mono,
            sweepDeconvolutionImpulseResponse:
            [
                Complex.Zero,
                Complex.One,
                Complex.Zero
            ],
            sweepDeconvolutionPeakIndex: 1);

        PlotModelFactory factory = CreateFactory(measurement, noiseMeasurement);
        factory.SetImpulseResponseFileName(null);

        Assert.Equal(
            "Frequency Response",
            factory.CreateFrequencyResponse(includeCurves: false).Title);
    }

    [Fact]
    public void ImpulseResponse_RespectsShowImpulseFlag()
    {
        using var measurement = CreateTransferMeasurement();
        using var noiseMeasurement = new NoiseMeasurement();
        var impulseOptions = new ImpulseResponseOptions { ShowImpulse = false };
        PlotModelFactory factory =
            CreateFactory(measurement, noiseMeasurement, impulseOptions: impulseOptions);

        Assert.Empty(factory.CreateImpulseResponse(includeCurves: true).Series);

        impulseOptions.ShowImpulse = true;
        Assert.NotEmpty(factory.CreateImpulseResponse(includeCurves: true).Series);
    }

    [Fact]
    public void Autocorrelation_RespectsShowAutocorrelationFlag()
    {
        using var measurement = CreateTransferMeasurement();
        using var noiseMeasurement = new NoiseMeasurement();
        var impulseOptions = new ImpulseResponseOptions { ShowAutocorrelation = false };
        PlotModelFactory factory =
            CreateFactory(measurement, noiseMeasurement, impulseOptions: impulseOptions);

        Assert.Empty(factory.CreateAutocorrelation(includeCurves: true).Series);

        impulseOptions.ShowAutocorrelation = true;
        Assert.NotEmpty(factory.CreateAutocorrelation(includeCurves: true).Series);
    }

    [Fact]
    public void GroupDelay_RespectsShowGroupDelayFlag()
    {
        using var measurement = CreateTransferMeasurement();
        using var noiseMeasurement = new NoiseMeasurement();
        var groupDelayOptions = new FrequencyResponseOptions { ShowGroupDelay = false };
        PlotModelFactory factory =
            CreateFactory(measurement, noiseMeasurement, groupDelayOptions: groupDelayOptions);

        Assert.Empty(factory.CreateGroupDelay(includeCurves: true).Series);

        groupDelayOptions.ShowGroupDelay = true;
        Assert.NotEmpty(factory.CreateGroupDelay(includeCurves: true).Series);
    }

    private static ExpSweepMeasurement CreateTransferMeasurement()
    {
        var transferImpulse = new Complex[2048];
        transferImpulse[64] = Complex.One;

        var measurement = new ExpSweepMeasurement();
        measurement.RestoreImpulseResponse(
            octaves: 12,
            sampleRate: 44_100,
            bits: 24,
            sweepDurationSeconds: 1.0,
            playChannel: PlaybackChannel.Mono,
            sweepDeconvolutionImpulseResponse: transferImpulse,
            sweepDeconvolutionPeakIndex: 64,
            measurementMode: SweepMeasurementMode.LoopbackTransfer,
            transferImpulseResponse: transferImpulse,
            transferPeakIndex: 64);
        return measurement;
    }

    private static PlotModelFactory CreateFactory(
        ExpSweepMeasurement measurement,
        NoiseMeasurement noiseMeasurement,
        ImpulseResponseOptions? impulseOptions = null,
        FrequencyResponseOptions? groupDelayOptions = null)
    {
        string calibrationPath = Path.Combine(
            Path.GetTempPath(),
            $"resonalyze-calibration-{Guid.NewGuid():N}.txt");

        return new PlotModelFactory(
            measurement,
            noiseMeasurement,
            new CalibrationFile(calibrationPath),
            new FrequencyResponseOptions(),
            new FrequencyResponseOptions(),
            groupDelayOptions ?? new FrequencyResponseOptions(),
            impulseOptions ?? new ImpulseResponseOptions(),
            new LiveSpectrumOptions(),
            new WaterfallGenerateOptions(),
            new WaterfallGenerateOptions());
    }
}
