using System.Reflection;
using OxyPlot.Series;
using OxyPlot.WindowsForms;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// What the wizard draws as <b>Source + EQ</b> for a spatial average, and why it is
/// not the source plus the bank's ideal magnitude.
/// </summary>
/// <remarks>
/// The display smoothing is the hybrid builder's LAST step, and it does not commute
/// with the bank: adding an unsmoothed filter to an already-smoothed curve reads
/// differently from smoothing the corrected curve, and the psychoacoustic width — a
/// peak-weighted cubic mean rather than a linear one — widens the gap. Measured on a
/// field MMM tune (13 bands, psychoacoustic) the two orders parted by up to 3.1 dB,
/// so the wizard promised a result the Virtual DSP plot would not draw when the bank
/// went back. The bank therefore goes INSIDE the chain, exactly as the gated preview
/// substitutes it for an impulse-response source and as the Virtual DSP plot
/// substitutes the channel's own PEQ.
/// </remarks>
public sealed class EqWizardSpatialAverageCorrectionTests
{
    private const int SampleRate = 48_000;

    // The shape that exposes the ordering: a narrow boost between deeper cuts, which a
    // peak-weighted mean pulls down and an ideal magnitude does not. Taken from the
    // field project that surfaced this.
    private static readonly EqualizationCurve Bank = new(
        [
            new PeqBand(269, 1.8, -9.5),
            new PeqBand(339, 6.0, 1.7),
            new PeqBand(395, 6.8, -1.4),
            new PeqBand(495, 3.5, -5.9)
        ],
        preampDb: -2);

    [Fact]
    public void TheCorrectedCurveIsTheChainWithTheBankInIt()
    {
        using var panel = new EqWizardPanel();
        EqWizardCurveSource source = Handoff();
        ApplySource(panel, source);
        SetSmoothing(panel, SpectrumSmoothing.PsychoacousticCode);
        ApplyBank(panel, Bank);

        IReadOnlyList<double> drawn = Curve(panel, "Source + EQ");
        IReadOnlyList<double> expected = HybridWith(source, Bank);

        Assert.Equal(expected.Count, drawn.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i], drawn[i], 9);
        }
    }

    [Fact]
    public void AddingTheBankAfterTheSmoothingWouldReadDifferently()
    {
        // The guard on the test above: without it that equality could hold merely
        // because the two orders agree on this data, and it would pin nothing.
        using var panel = new EqWizardPanel();
        ApplySource(panel, Handoff());
        SetSmoothing(panel, SpectrumSmoothing.PsychoacousticCode);
        ApplyBank(panel, Bank);

        IReadOnlyList<double> drawn = Curve(panel, "Source + EQ");
        IReadOnlyList<double> bare = Curve(panel, "Source");
        IReadOnlyList<double> frequencies = Frequencies(panel, "Source");

        double worst = 0;
        for (int i = 0; i < drawn.Count; i++)
        {
            double naive = bare[i] + DigitalEqualizationResponse.MagnitudeDbAt(
                Bank, frequencies[i], SampleRate);
            worst = Math.Max(worst, Math.Abs(naive - drawn[i]));
        }

        Assert.True(worst > 0.5, $"the two orders differ by only {worst:0.00} dB here.");
    }

    [Fact]
    public void AnEmptyBankLeavesTheSourceExactlyWhereItIs()
    {
        // Bypass, and the state before the first band is added: Source + EQ has to lie
        // on Source, which it only does while both come out of the same builder.
        using var panel = new EqWizardPanel();
        ApplySource(panel, Handoff());
        SetSmoothing(panel, SpectrumSmoothing.PsychoacousticCode);
        ApplyBank(panel, new EqualizationCurve([]));

        IReadOnlyList<double> drawn = Curve(panel, "Source + EQ");
        IReadOnlyList<double> bare = Curve(panel, "Source");

        Assert.Equal(bare.Count, drawn.Count);
        for (int i = 0; i < bare.Count; i++)
        {
            Assert.Equal(bare[i], drawn[i], 9);
        }
    }

    [Fact]
    public void TheGapUnderAProtectiveHighPassStaysAGapOnBothCurves()
    {
        // The curves are read against each other BY INDEX — the target, the error fill
        // and the fit statistics all pair them up — so the corrected one has to keep
        // exactly the points the bare one keeps.
        using var panel = new EqWizardPanel();
        LiveCaptureDocument capture = Capture();
        for (int i = 0; i < capture.CurveDb.Length; i++)
        {
            if (capture.FrequencyAt(i) < 100)
            {
                capture.CurveDb[i] = double.NaN;
            }
        }

        ApplySource(panel, Handoff(capture));
        SetSmoothing(panel, SpectrumSmoothing.PsychoacousticCode);
        ApplyBank(panel, Bank);

        IReadOnlyList<double> drawn = Curve(panel, "Source + EQ");
        IReadOnlyList<double> bare = Curve(panel, "Source");

        Assert.Equal(bare.Count, drawn.Count);
        Assert.Contains(drawn, double.IsNaN);
        for (int i = 0; i < bare.Count; i++)
        {
            Assert.Equal(double.IsNaN(bare[i]), double.IsNaN(drawn[i]));
        }
    }

    // What the Virtual DSP plot builds for the same capture: the whole chain with the
    // bank in it, and the display smoothing over the finished curve.
    private static IReadOnlyList<double> HybridWith(
        EqWizardCurveSource source,
        EqualizationCurve bank)
    {
        LiveCaptureDocument document = source.SpatialAverage!;
        List<double> grid = document.ToCurvePoints().Select(point => point.X).ToList();
        return SpatialAverageHybrid.BuildChannelCurve(
            document,
            source.PreviewChain! with { Peq = bank },
            SampleRate,
            SpatialAverageCalibration.Own,
            grid,
            SpectrumSmoothing.PsychoacousticCode)!
            .Select(point => point.Y + source.SpatialAverageOffsetDb)
            .ToList();
    }

    private static EqWizardCurveSource Handoff(LiveCaptureDocument? capture = null) =>
        new()
        {
            Kind = EqWizardSourceKind.VirtualDspChannel,
            DisplayName = "Ch C · L (DSP, MMM)",
            Description = "Spatial average through the channel's chain.",
            // The chain the wizard opens on: the channel's, with the bank it is
            // editing left out of it.
            PreviewChain = new DspChannelChain(
                GainDb: -3,
                Crossover: new CrossoverSpec(
                    CrossoverKind.BandPass,
                    new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 1_600, 24),
                    new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 160, 24))),
            SpatialAverage = capture ?? Capture(),
            SpatialAverageCalibration = SpatialAverageCalibration.Own,
            SpatialAverageOffsetDb = 4.5,
            SampleRateHz = SampleRate,
            CurveKind = AnalysisCurveKind.InputSpectrum
        };

    private static LiveCaptureDocument Capture()
    {
        var curve = new double[1_024];
        for (int i = 0; i < curve.Length; i++)
        {
            // A cabin's shape rather than a smooth one: the ordering only shows where
            // the curve has structure for the smoothing to work on.
            curve[i] = -40 + 3 * Math.Sin(i / 40.0) + 1.5 * Math.Sin(i / 7.0);
        }

        return new LiveCaptureDocument
        {
            SavedAtUtc = DateTimeOffset.UnixEpoch,
            Title = "l mid mmm",
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
        };
    }

    private static IReadOnlyList<double> Curve(EqWizardPanel panel, string title) =>
        Series(panel, title).Points.Select(point => point.Y).ToList();

    private static IReadOnlyList<double> Frequencies(EqWizardPanel panel, string title) =>
        Series(panel, title).Points.Select(point => point.X).ToList();

    private static LineSeries Series(EqWizardPanel panel, string title) =>
        (LineSeries)Field<PlotView>(panel, "plotWizard").Model!.Series
            .OfType<XYAxisSeries>()
            .First(series => series.Title == title);

    private static void ApplySource(EqWizardPanel panel, EqWizardCurveSource source) =>
        Invoke(panel, "ApplySource", source);

    private static void ApplyBank(EqWizardPanel panel, EqualizationCurve bank) =>
        Invoke(panel, "ApplyEqualizationCurve", bank);

    private static void SetSmoothing(EqWizardPanel panel, int code) =>
        Invoke(panel, "SetSourceSmoothing", code);

    private static void Invoke(EqWizardPanel panel, string name, params object[] arguments) =>
        typeof(EqWizardPanel)
            .GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(panel, arguments);

    private static T Field<T>(EqWizardPanel panel, string name) =>
        (T)typeof(EqWizardPanel)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(panel)!;
}
