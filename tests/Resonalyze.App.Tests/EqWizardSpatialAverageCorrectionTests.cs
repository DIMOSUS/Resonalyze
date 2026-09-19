using OxyPlot;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// Display smoothing (peak-weighted cubic mean) does not commute with the bank: the two orders parted by up to 3.1 dB
/// on a field MMM tune, so the bank goes inside the chain as the Virtual DSP plot does.
/// </summary>
public sealed class EqWizardSpatialAverageCorrectionTests
{
    private const int SampleRate = 48_000;

    // A narrow boost between deeper cuts (from the field project): a peak-weighted mean pulls it down.
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
        var session = new EqWizardSession();
        EqWizardCurveSource source = Handoff();
        session.Load(source);
        session.SetSourceSmoothing(SpectrumSmoothing.PsychoacousticCode);
        session.Bank.Replace(Bank);

        IReadOnlyList<double> drawn = Corrected(session).Select(point => point.Y).ToList();
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
        // Guards that the previous equality is not merely the two orders agreeing on this data.
        var session = new EqWizardSession();
        session.Load(Handoff());
        session.SetSourceSmoothing(SpectrumSmoothing.PsychoacousticCode);
        session.Bank.Replace(Bank);

        IReadOnlyList<double> drawn = Corrected(session).Select(point => point.Y).ToList();
        IReadOnlyList<double> bare = Bare(session).Select(point => point.Y).ToList();
        IReadOnlyList<double> frequencies = Bare(session).Select(point => point.X).ToList();

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
        var session = new EqWizardSession();
        session.Load(Handoff());
        session.SetSourceSmoothing(SpectrumSmoothing.PsychoacousticCode);
        session.Bank.Replace(new EqualizationCurve([]));

        IReadOnlyList<double> drawn = Corrected(session).Select(point => point.Y).ToList();
        IReadOnlyList<double> bare = Bare(session).Select(point => point.Y).ToList();

        Assert.Equal(bare.Count, drawn.Count);
        for (int i = 0; i < bare.Count; i++)
        {
            Assert.Equal(bare[i], drawn[i], 9);
        }
    }

    [Fact]
    public void TheGapUnderAProtectiveHighPassStaysAGapOnBothCurves()
    {
        // Target, error fill and fit statistics pair curves by index, so the points must match.
        var session = new EqWizardSession();
        LiveCaptureDocument capture = Capture();
        for (int i = 0; i < capture.CurveDb.Length; i++)
        {
            if (capture.FrequencyAt(i) < 100)
            {
                capture.CurveDb[i] = double.NaN;
            }
        }

        session.Load(Handoff(capture));
        session.SetSourceSmoothing(SpectrumSmoothing.PsychoacousticCode);
        session.Bank.Replace(Bank);

        IReadOnlyList<double> drawn = Corrected(session).Select(point => point.Y).ToList();
        IReadOnlyList<double> bare = Bare(session).Select(point => point.Y).ToList();

        Assert.Equal(bare.Count, drawn.Count);
        Assert.Contains(drawn, double.IsNaN);
        for (int i = 0; i < bare.Count; i++)
        {
            Assert.Equal(double.IsNaN(bare[i]), double.IsNaN(drawn[i]));
        }
    }

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
            // Structure for the smoothing to work on; a smooth curve hides the ordering.
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

    private static IReadOnlyList<DataPoint> Corrected(EqWizardSession session) =>
        EqWizardRender.RenderSet(session, EqWizardRender.DisplayedEq(session)).SourcePlusEq!.Points;

    private static IReadOnlyList<DataPoint> Bare(EqWizardSession session) => session.SourceCurve!.Points;
}
