using System.Windows.Forms;
using Resonalyze.Dsp;
using Resonalyze.Options;
using Resonalyze.Ui;

namespace Resonalyze.App.Tests;

public sealed class FROptionsTests
{
    [Fact]
    public void MagnitudeWindowModeRoundTripsThroughThePanel()
    {
        var measurement = new AnalyzerDocument();
        var options = new FrequencyResponseOptions
        {
            MagnitudeWindowMode = PhaseWindowMode.FrequencyDependent,
            MagnitudeFdwCycles = 8
        };
        using var panel = new FROptions();
        panel.Init(
            measurement,
            48_000,
            options,
            new CurveVisibilityOptions(),
            []);

        var written = new FrequencyResponseOptions();
        panel.SetOptions(written, new CurveVisibilityOptions());

        Assert.Equal(PhaseWindowMode.FrequencyDependent, written.MagnitudeWindowMode);
        Assert.Equal(8, written.MagnitudeFdwCycles);
    }

    [Fact]
    public void InvalidStoredCyclesFallBackToTheDefaultChoice()
    {
        var measurement = new AnalyzerDocument();
        var options = new FrequencyResponseOptions
        {
            MagnitudeWindowMode = PhaseWindowMode.Fixed,
            MagnitudeFdwCycles = 123
        };
        using var panel = new FROptions();
        panel.Init(
            measurement,
            48_000,
            options,
            new CurveVisibilityOptions(),
            []);

        var written = new FrequencyResponseOptions();
        panel.SetOptions(written, new CurveVisibilityOptions());

        Assert.Equal(PhaseWindowMode.Fixed, written.MagnitudeWindowMode);
        Assert.Equal(
            PhaseAnalysisSettings.DefaultFdwCycles, written.MagnitudeFdwCycles);
    }

    // Nothing tells an open panel a measurement landed; it follows the document.
    [Fact]
    public void TheSplChoiceTurnsAmberWhenAMeasurementWithoutAnAnchorLands()
    {
        var document = new AnalyzerDocument();
        using var panel = new FROptions();
        panel.Init(document, 48_000, new FrequencyResponseOptions(), new CurveVisibilityOptions(), []);
        var spl = (RadioButton)panel.Controls.Find("radioMagnitudeSpl", searchAllChildren: true).Single();
        Assert.NotEqual(UiPalette.Warning, spl.ForeColor);

        document.TryBegin()!.Install(
            TestMeasurementResults.Restored(
                20, 20_000, 48_000, 24, 1.0, PlaybackChannel.Mono,
                [System.Numerics.Complex.Zero, System.Numerics.Complex.One],
                sweepDeconvolutionPeakIndex: 1),
            "no anchor.json");

        Assert.Equal(UiPalette.Warning, spl.ForeColor);
    }
}
