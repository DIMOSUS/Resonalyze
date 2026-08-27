using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// The paths that draw a measured curve from something OTHER than the measurement —
/// a captured overlay, a wizard preview — and have to break where it breaks.
/// </summary>
/// <remarks>
/// Each of them stores or re-renders a raw spectrum, and the spectrum is deliberately
/// stored UNMASKED so a later re-smoothing at any width is exact. That makes the band
/// something these paths must carry and re-apply, and each one that forgot drew a
/// confident curve beside a broken one, from the same response.
/// </remarks>
public sealed class MeasuredBandCarryTests
{
    private const double LowHz = 565.0;

    // A flat spectrum on the linear grid the renderer resamples from.
    private static List<SignalPoint> Spectrum() =>
        Enumerable.Range(1, 4_000)
            .Select(bin => new SignalPoint(bin * 6.0, -30.0))
            .ToList();

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(12)]
    [InlineData(48)]
    public void AReRenderedRawSpectrumBreaksAtTheBand_AtEveryWidth(int smoothing)
    {
        List<SignalPoint> curve = RawCurveRenderer.Render(
            Spectrum(), [], smoothing, new MeasuredBand(LowHz, 8_000.0));

        Assert.NotEmpty(curve);
        // The break lands where the measurement ended, not where the smoothing window
        // happens to reach — which is the whole reason the mask is applied to the
        // finished curve rather than to the spectrum feeding it.
        Assert.All(
            curve,
            point => Assert.Equal(
                point.X >= LowHz && point.X <= 8_000.0, double.IsFinite(point.Y)));
    }

    [Fact]
    public void ARawSpectrumWithNoBandIsLeftAlone()
    {
        // The default, and what a legacy overlay, an imported text curve and a live
        // RTA capture all read as: nothing was ever masked for them and nothing is now.
        List<SignalPoint> curve = RawCurveRenderer.Render(Spectrum(), [], 6);

        Assert.All(curve, point => Assert.True(double.IsFinite(point.Y)));
    }

    [Fact]
    public void TheBandIsAppliedAfterTheCalibrationToo()
    {
        // A break is not a level: correcting it would turn NaN into NaN anyway, but the
        // order matters for the opposite case — a masked point must not consume a
        // correction entry and shift the rest.
        var correction = new double[RawCurveRenderer.PointCount];
        Array.Fill(correction, 2.0);

        List<SignalPoint> curve = RawCurveRenderer.Render(
            Spectrum(), correction, 0, new MeasuredBand(LowHz, double.PositiveInfinity));

        Assert.All(
            curve,
            point => Assert.Equal(point.X >= LowHz, double.IsFinite(point.Y)));
        SignalPoint measured = curve.First(point => point.X >= 1_000);
        Assert.Equal(-32.0, measured.Y, 6);
    }

    [Fact]
    public void AnOverlayFileCarriesTheBandAcrossASaveAndLoad()
    {
        string root = Path.Combine(Path.GetTempPath(), $"overlay-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var file = new OverlayFile
            {
                SavedAtUtc = DateTimeOffset.UtcNow,
                Mode = Mode.FrequencyResponse,
                Slot = 3,
                Title = "tweeter",
                Points = [new OverlayPoint(20, -30), new OverlayPoint(20_000, -30)],
                RawSpectrum = [new OverlayPoint(20, -30), new OverlayPoint(20_000, -30)],
                MeasuredLowFrequencyHz = LowHz,
                MeasuredHighFrequencyHz = 28_299.0
            };
            file.Save(root);

            OverlayFile? loaded = OverlayFile.Load(Mode.FrequencyResponse, 3, root);

            Assert.NotNull(loaded);
            Assert.Equal(LowHz, loaded!.MeasuredLowFrequencyHz, 6);
            Assert.Equal(28_299.0, loaded.MeasuredHighFrequencyHz, 6);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AFileWrittenBeforeTheBandExistedMeasuresEverything()
    {
        // Zero and zero, which MeasuredBand reads as "not narrowed" — an older overlay
        // must keep drawing exactly what it drew.
        var band = new MeasuredBand(
            new OverlayFile().MeasuredLowFrequencyHz,
            new OverlayFile().MeasuredHighFrequencyHz);

        Assert.Equal(0.0, band.LowEdgeHz);
        Assert.True(double.IsPositiveInfinity(band.HighEdgeHz));
    }

    [Fact]
    public void AnImportedOverlayTakesItsBandIntoTheWizard()
    {
        var file = new OverlayFile
        {
            Title = "tweeter",
            Points = [new OverlayPoint(20, -30), new OverlayPoint(20_000, -30)],
            RawSpectrum = [new OverlayPoint(20, -30), new OverlayPoint(20_000, -30)],
            MeasuredLowFrequencyHz = LowHz,
            MeasuredHighFrequencyHz = 28_299.0
        };

        EqWizardCurveSource source = EqWizardSourceResolver.CreateFromOverlayFile(file);

        Assert.Equal(LowHz, source.RawSpectrumBand.LowEdgeHz, 6);
        Assert.Equal(28_299.0, source.RawSpectrumBand.HighEdgeHz, 6);
    }

    [Fact]
    public void TheWizardsGatedPreviewStopsWhereItsSourceDoes()
    {
        // Both curves the wizard draws for a Virtual DSP handoff come from this
        // renderer — the bare one and the corrected one — so a band it ignored showed
        // as a break on the source and a continuous line on the preview beside it.
        var impulse = new System.Numerics.Complex[8_192];
        impulse[64] = System.Numerics.Complex.One;
        var gate = new PhaseAnalysisSettings(
            PhaseWindowMode.Fixed,
            PhaseAnalysisSettings.DefaultFdwCycles,
            PhaseDetrendMode.Off,
            ManualDetrendMilliseconds: 0.0,
            GateOffsetMs: 0.0,
            LeftMs: FrequencyResponseOptions.SteadyStateLeftMs,
            PlateauMs: FrequencyResponseOptions.SteadyStatePlateauMs,
            RightMs: FrequencyResponseOptions.SteadyStateRightMs,
            Unwrap: false,
            SmoothingInverseOctaves: 0.0);

        IReadOnlyList<SignalPoint> preview = EqWizardGatedPreview.Render(
            new EqWizardGatedPreviewRequest(
                impulse,
                DspChannelChain.Identity,
                Bank: null,
                AnchorIndex: 64,
                SampleRate: 48_000,
                ProcessorSampleRate: 48_000,
                gate,
                Calibration: null,
                SmoothingInverseOctaves: 6,
                new MeasuredBand(LowHz, 8_000.0)));

        Assert.NotEmpty(preview);
        Assert.All(
            preview,
            point => Assert.Equal(
                point.X >= LowHz && point.X <= 8_000.0, double.IsFinite(point.Y)));
    }
}
