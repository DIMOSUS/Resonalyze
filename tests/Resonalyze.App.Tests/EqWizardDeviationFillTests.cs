using System.Numerics;
using OxyPlot.Series;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class EqWizardDeviationFillTests
{
    private const int SampleRate = 48_000;
    private const double GapBelowHz = 600;
    private const double SweptFromHz = 200;
    private const double SweptToHz = 19_999.96;

    [Fact]
    public void NothingIsShadedWhereTheSourceCurveDoesNotExist()
    {
        var session = new EqWizardSession();

        session.Load(GappedHandoff());

        IReadOnlyList<AreaSeries> fills = Fills(session);
        Assert.NotEmpty(fills);
        foreach (AreaSeries fill in fills)
        {
            // A NaN vertex let the renderer close the polygon across the gap.
            Assert.All(fill.Points, point => Assert.True(double.IsFinite(point.Y)));
            Assert.All(fill.Points2, point => Assert.True(double.IsFinite(point.Y)));
            Assert.All(fill.Points, point => Assert.True(point.X >= GapBelowHz));
        }
    }

    [Fact]
    public void TheShadingStillCoversTheBandThatWasMeasured()
    {
        var session = new EqWizardSession();

        session.Load(GappedHandoff());

        IReadOnlyList<AreaSeries> fills = Fills(session);
        Assert.All(fills, fill => Assert.True(fill.Points.Count > 100));
        Assert.All(fills, fill => Assert.True(fill.Points[^1].X > 19_000));
    }

    [Fact]
    public async Task TheShadingReadsTheTargetAtItsOwnFrequencies()
    {
        EqWizardSession session = await WithCorrectedCurveAsync(BandLimitedHandoff());

        // Result and target vertices must share frequencies; one render dropping NaN bins slid the target sideways.
        IReadOnlyList<AreaSeries> fills = Fills(session);
        Assert.NotEmpty(fills);
        foreach (AreaSeries fill in fills)
        {
            Assert.Equal(fill.Points.Count, fill.Points2.Count);
            for (int i = 0; i < fill.Points.Count; i++)
            {
                Assert.Equal(fill.Points[i].X, fill.Points2[i].X, 9);
            }
        }
    }

    [Fact]
    public async Task TheShadingCoversEverythingTheSweepMeasured()
    {
        EqWizardSession session = await WithCorrectedCurveAsync(BandLimitedHandoff());

        // Symptom: a 200 Hz sweep's shading closed in a wedge near 2 kHz (200²/20).
        IReadOnlyList<AreaSeries> fills = Fills(session);
        Assert.NotEmpty(fills);
        Assert.True(
            fills.Max(fill => fill.Points[^1].X) > 19_000,
            "The shading should reach the top of the swept band.");
        Assert.All(
            fills,
            fill => Assert.All(
                fill.Points, point => Assert.True(point.X >= SweptFromHz)));
    }

    private static EqWizardCurveSource BandLimitedHandoff()
    {
        var response = new Complex[16_384];
        for (int i = 0; i < 96; i++)
        {
            response[600 + i] = Math.Exp(-i / 20.0) * Math.Cos(2 * Math.PI * i / 24.0);
        }

        return new EqWizardCurveSource
        {
            Kind = EqWizardSourceKind.VirtualDspChannel,
            DisplayName = "Ch C · R",
            Description = "Through its own chain, as the panel drew it.",
            Measurement = new ImpulseMeasurementView(response, 600, SampleRate)
            {
                LowestMeasuredFrequencyHz = SweptFromHz,
                HighestMeasuredFrequencyHz = SweptToHz
            },
            PreviewImpulseResponse = response,
            PreviewChain = DspChannelChain.Identity,
            GateSettings = new PhaseAnalysisSettings(
                PhaseWindowMode.Fixed,
                PhaseAnalysisSettings.DefaultFdwCycles,
                PhaseDetrendMode.Off,
                ManualDetrendMilliseconds: 0.0,
                GateOffsetMs: 600 * 1_000.0 / SampleRate,
                LeftMs: 1,
                PlateauMs: 30,
                RightMs: 10,
                Unwrap: false,
                SmoothingInverseOctaves: 0.0),
            SampleRateHz = SampleRate,
            CurveKind = AnalysisCurveKind.Primary
        };
    }

    // A gated source's corrected curve is convolved off the UI thread.
    private static async Task<EqWizardSession> WithCorrectedCurveAsync(EqWizardCurveSource source)
    {
        var session = new EqWizardSession();
        session.Load(source);
        Task? preview = session.Previews.RequestGatedPreview(EqWizardRender.DisplayedEq(session));
        Assert.NotNull(preview);
        await preview;
        Assert.NotNull(session.Previews.GatedPreview);
        return session;
    }

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

    private static IReadOnlyList<AreaSeries> Fills(EqWizardSession session) =>
        EqWizardTestPlots.Draw(session).Series.OfType<AreaSeries>().ToList();
}
