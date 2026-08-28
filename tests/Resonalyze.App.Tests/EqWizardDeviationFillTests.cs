using System.Numerics;
using System.Reflection;
using OxyPlot;
using OxyPlot.Series;
using OxyPlot.WindowsForms;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// The shaded deviation between Source + EQ and the target. It is a statement about a
/// measurement, so it may only be drawn where there IS one: a driver swept over its
/// own band, or a capture that measured nothing under a protective high-pass, has no
/// level below its edge and nothing to deviate by.
/// </summary>
public sealed class EqWizardDeviationFillTests
{
    private const int SampleRate = 48_000;
    private const double GapBelowHz = 600;

    [Fact]
    public void NothingIsShadedWhereTheSourceCurveDoesNotExist()
    {
        using var panel = new EqWizardPanel();

        ApplySource(panel, GappedHandoff());

        IReadOnlyList<AreaSeries> fills = Fills(panel);
        Assert.NotEmpty(fills);
        foreach (AreaSeries fill in fills)
        {
            // A NaN vertex used to enter the polygon like any other, and the renderer
            // closed the shape across the gap: whole octaves came out shaded as a
            // deviation from the target with no measurement under them.
            Assert.All(fill.Points, point => Assert.True(double.IsFinite(point.Y)));
            Assert.All(fill.Points2, point => Assert.True(double.IsFinite(point.Y)));
            Assert.All(fill.Points, point => Assert.True(point.X >= GapBelowHz));
        }
    }

    [Fact]
    public void TheShadingStillCoversTheBandThatWasMeasured()
    {
        using var panel = new EqWizardPanel();

        ApplySource(panel, GappedHandoff());

        // Breaking the fill at the gap must not cost the part that belongs there:
        // the run above the edge is still shaded end to end.
        IReadOnlyList<AreaSeries> fills = Fills(panel);
        Assert.All(fills, fill => Assert.True(fill.Points.Count > 100));
        Assert.All(fills, fill => Assert.True(fill.Points[^1].X > 19_000));
    }

    // A tweeter handed over from Virtual DSP with a moving-microphone capture that
    // measured nothing below 600 Hz — the shape a real session has.
    private static EqWizardCurveSource GappedHandoff()
    {
        var response = new Complex[16_384];
        for (int i = 0; i < 96; i++)
        {
            response[600 + i] = Math.Exp(-i / 20.0) * Math.Cos(2 * Math.PI * i / 24.0);
        }

        var curve = new double[1_024];
        for (int i = 0; i < curve.Length; i++)
        {
            double hz = 20 * Math.Pow(1_000.0, i / (curve.Length - 1.0));
            curve[i] = hz < GapBelowHz ? double.NaN : -42 + 3 * Math.Sin(i / 30.0);
        }

        return new EqWizardCurveSource
        {
            Kind = EqWizardSourceKind.VirtualDspChannel,
            DisplayName = "Ch D · R (MMM)",
            Description = "Spatial average, as measured with the DSP bypassed.",
            Measurement = new ImpulseMeasurementView(response, 600, SampleRate),
            PreviewImpulseResponse = response,
            PreviewChain = DspChannelChain.Identity,
            SpatialAverage = new LiveCaptureDocument
            {
                SavedAtUtc = DateTimeOffset.UnixEpoch,
                Title = "r tw mmm",
                Method = SpatialAverageMethod.MovingMic,
                CurveDb = curve,
                GridStartHz = 20,
                GridStopHz = 20_000,
                Recipe = new LiveCaptureRecipe
                {
                    AnalysisMode = LiveAnalysisMode.Mmm,
                    SampleRateHz = SampleRate,
                    MagnitudeScale = MagnitudeScale.SoundPressureLevel,
                    SmoothingCode = 0,
                    IntegratedSeconds = 42
                }
            },
            SpatialAverageCalibration = SpatialAverageCalibration.Own,
            SampleRateHz = SampleRate,
            CurveKind = AnalysisCurveKind.Primary
        };
    }

    private static IReadOnlyList<AreaSeries> Fills(EqWizardPanel panel) =>
        Model(panel).Series.OfType<AreaSeries>().ToList();

    private static PlotModel Model(EqWizardPanel panel) =>
        ((PlotView)typeof(EqWizardPanel)
            .GetField("plotWizard", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(panel)!).Model!;

    private static void ApplySource(EqWizardPanel panel, EqWizardCurveSource source) =>
        typeof(EqWizardPanel)
            .GetMethod("ApplySource", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(panel, [source]);
}
