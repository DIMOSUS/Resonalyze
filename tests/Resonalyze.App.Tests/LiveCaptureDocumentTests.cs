using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

// A capture document exists so a stored spatial average can be RE-RENDERED — at
// another smoothing, under another calibration — rather than only redrawn. That
// promise is worth exactly as much as the fidelity of the bins it stores and the
// completeness of the recipe beside them, so both are pinned here.
public sealed class LiveCaptureDocumentTests
{
    private const int SampleRate = 48_000;
    private const int SequenceLength = 32_768;

    [Fact]
    public void StoredBinsReproduceTheBandLevelsThroughARoundTrip()
    {
        double[] amplitude = BuildPinkishSpectrum();
        List<SignalPoint> expected = Resample(amplitude);

        LiveCaptureDocument document = BuildDocument(amplitude, expected);
        string path = Path.Combine(Path.GetTempPath(), $"live-capture-{Guid.NewGuid():N}.json");
        try
        {
            document.Save(path);
            LiveCaptureDocument loaded = LiveCaptureDocument.Load(path);
            List<SignalPoint> actual = Resample(loaded.ToAmplitudeSpectrum());

            Assert.Equal(expected.Count, actual.Count);
            for (int i = 0; i < expected.Count; i++)
            {
                Assert.Equal(expected[i].X, actual[i].X, 6);
                // The bins go to disk in dB; only that quantization separates the two
                // renders, and it sits three orders below any measurement uncertainty.
                Assert.Equal(expected[i].Y, actual[i].Y, 3);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void StoredBinsStopAtTheCeilingWithoutMovingTheCurve()
    {
        // At 48 kHz nothing is above the ceiling, so the whole half-spectrum is kept;
        // the point is that the trim never reaches into the band the curve is read
        // over. Checked here at the rate the owner captures at.
        double[] amplitude = BuildPinkishSpectrum();
        LiveCaptureDocument document = BuildDocument(amplitude, Resample(amplitude));

        double binWidth = (double)SampleRate / SequenceLength;
        double highestStoredHz = (document.SpectrumDb.Length - 1) * binWidth;

        Assert.True(document.SpectrumDb.Length <= SequenceLength / 2 + 1);
        Assert.True(highestStoredHz >= 20_000.0, $"stored only up to {highestStoredHz:0} Hz");
    }

    [Fact]
    public void ARecipeWithADifferentFrameIsNotTheSameSet()
    {
        LiveCaptureRecipe recipe = BuildRecipe();
        LiveCaptureRecipe longer = BuildRecipe();
        longer.SequenceLength = SequenceLength * 2;

        Assert.True(recipe.MatchesSetOf(BuildRecipe()));
        Assert.False(recipe.MatchesSetOf(longer));
    }

    [Fact]
    public void ARecipeWithoutSlopeCompensationIsNotTheSameSet()
    {
        // The one difference that reads as a smooth, plausible bass tilt rather than
        // as an obviously broken curve, so it must never pass a set check.
        LiveCaptureRecipe compensated = BuildRecipe();
        LiveCaptureRecipe raw = BuildRecipe();
        raw.SlopeCompensation = false;

        Assert.False(compensated.MatchesSetOf(raw));
    }

    [Fact]
    public void ChannelsFilteredDifferentlyAreStillOneSet()
    {
        // The protective high-pass belongs to the channel's own hardware path: a
        // tweeter has one and a subwoofer does not, and each capture has its own
        // divided back out. Rejecting a set for that difference threw out a correct
        // seven-channel measurement of a real car.
        LiveCaptureRecipe tweeter = BuildRecipe();
        tweeter.ProtectiveHighPassKind = ProtectiveHighPassKind.Butterworth;
        tweeter.ProtectiveHighPassFrequencyHz = 1000.0;
        tweeter.ProtectiveHighPassSlopeDbPerOctave = 48;

        LiveCaptureRecipe subwoofer = BuildRecipe();
        subwoofer.ProtectiveHighPassKind = ProtectiveHighPassKind.Off;

        Assert.True(tweeter.MatchesSetOf(subwoofer));
    }

    [Fact]
    public void AMisalignedCorrectionIsRejected()
    {
        double[] amplitude = BuildPinkishSpectrum();
        LiveCaptureDocument document = BuildDocument(amplitude, Resample(amplitude));
        document.CalibrationCorrectionDb = new double[LiveCaptureDocument.CurvePointCount - 1];

        // Silently accepting it would offset every point of the correction against the
        // curve it belongs to — a wrong answer that still looks like a measurement.
        Assert.Throws<InvalidDataException>(() => document.Validate());
    }

    [Fact]
    public void ACaptureWithoutBinsIsRejected()
    {
        double[] amplitude = BuildPinkishSpectrum();
        LiveCaptureDocument document = BuildDocument(amplitude, Resample(amplitude));
        document.SpectrumDb = [];

        Assert.Throws<InvalidDataException>(() => document.Validate());
    }

    [Fact]
    public void AFutureVersionIsRefusedRatherThanMisread()
    {
        double[] amplitude = BuildPinkishSpectrum();
        LiveCaptureDocument document = BuildDocument(amplitude, Resample(amplitude));
        document.Version = LiveCaptureDocument.CurrentVersion + 1;

        Assert.Throws<InvalidDataException>(() => document.Validate());
    }

    [Fact]
    public void TryLoadClaimsOnlyFilesThatSayTheyAreCaptures()
    {
        // The shared Load button asks this to decide whether a file belongs to the
        // live analyzer or to the impulse-response loader, so a foreign JSON must be
        // declined rather than claimed and then failed on a confusing complaint about
        // its recipe. A capture, conversely, must be claimed from any mode.
        string foreign = Path.Combine(Path.GetTempPath(), $"foreign-{Guid.NewGuid():N}.json");
        string capture = Path.Combine(Path.GetTempPath(), $"capture-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(foreign, "{\"version\":1,\"channels\":[]}");
            Assert.False(LiveCaptureDocument.TryLoad(foreign, out _));

            double[] amplitude = BuildPinkishSpectrum();
            BuildDocument(amplitude, Resample(amplitude)).Save(capture);
            Assert.True(LiveCaptureDocument.TryLoad(capture, out LiveCaptureDocument loaded));
            Assert.Equal(LiveAnalysisMode.Mmm, loaded.Recipe.AnalysisMode);
        }
        finally
        {
            File.Delete(foreign);
            File.Delete(capture);
        }
    }

    [Fact]
    public void ACaptureThatFailsValidationThrowsRatherThanBeingDisowned()
    {
        // Claiming is decided by the FORMAT alone: a file that says it is a capture
        // and is broken is a real error, and passing it on as "not ours" would have
        // the impulse-response loader misreport it as an unsupported format.
        string path = Path.Combine(Path.GetTempPath(), $"broken-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(
                path, $"{{\"format\":\"{LiveCaptureDocument.CurrentFormat}\",\"version\":1}}");
            Assert.Throws<InvalidDataException>(
                () => LiveCaptureDocument.TryLoad(path, out _));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ASnapshotCarriesTheFrameCountItsSpectraAverage()
    {
        // The count and the bins must come from one read: a capture that pairs this
        // snapshot's spectra with a count fetched later claims integration it does
        // not hold, and the count is what a stored spatial average is judged by.
        var snapshot = new LiveSpectrumSnapshot([1.0], null, [1.0], FrameCount: 130);
        Assert.Equal(130, snapshot.FrameCount);
    }

    private static List<SignalPoint> Resample(double[] amplitude) =>
        DataHelper.LogarithmicPowerBandResample(
            amplitude,
            SequenceLength,
            SampleRate,
            Windowing.EquivalentNoiseBandwidthBins(WindowType.Rectangular, SequenceLength),
            Windowing.MainLobeWidthBins(WindowType.Rectangular),
            20,
            20_000,
            LiveCaptureDocument.CurvePointCount,
            smoothingOctaves: 0,
            psychoacoustic: false);

    // Roughly 1/√f with a resonance on top, so the round trip is exercised over a real
    // dynamic range instead of a flat line.
    private static double[] BuildPinkishSpectrum()
    {
        double binWidth = (double)SampleRate / SequenceLength;
        var amplitude = new double[SequenceLength / 2 + 1];
        for (int bin = 1; bin < amplitude.Length; bin++)
        {
            double frequency = bin * binWidth;
            double resonance = 6.0 / (1.0 + Math.Pow((frequency - 55.0) / 4.0, 2.0));
            amplitude[bin] = (1.0 + resonance) / Math.Sqrt(frequency);
        }

        return amplitude;
    }

    /// <summary>
    /// Full coverage is not a coherent set. Seven captures taken at two frame
    /// lengths leave every channel attached while putting curves compensated by
    /// different amounts on one axis, under a single offset that fits none of them —
    /// and the spread warning is a heuristic that need not notice, since it reads a
    /// median over the working band and two recipes can differ mostly in the bass.
    /// </summary>
    [Fact]
    public void ASetTakenOnTwoRecipesIsRefusedAndSaysWhich()
    {
        LiveCaptureDocument first = BuildJudgeableCapture();
        LiveCaptureDocument second = BuildJudgeableCapture();
        second.Recipe.SequenceLength = SequenceLength * 2;

        LiveCaptureSetVerdict verdict =
            LiveCaptureDocument.JudgeSet([first, second]);

        Assert.False(verdict.Coherent);
        Assert.Contains("frame length", verdict.Reason);
        Assert.Contains($"{SequenceLength * 2}", verdict.Reason);
    }

    [Fact]
    public void CapturesFromOneSessionNeedNoAnchor()
    {
        // One analyzer session is one input gain, unchanged, so the levels are
        // comparable by construction and an absolute reference adds nothing.
        var session = Guid.NewGuid();
        LiveCaptureDocument first = BuildJudgeableCapture(session);
        LiveCaptureDocument second = BuildJudgeableCapture(session);

        Assert.True(LiveCaptureDocument.JudgeSet([first, second]).Coherent);
    }

    /// <summary>
    /// Across sessions only an absolute anchor vouches for the levels — but WITH one
    /// the set is legitimate, which is the case that matters: re-initializing the
    /// analyzer (editing the protective high-pass, say) mints a new session id
    /// between two perfectly good runs.
    /// </summary>
    [Fact]
    public void CapturesFromTwoSessionsNeedAnAnchorOnEveryOne()
    {
        LiveCaptureDocument first = BuildJudgeableCapture(Guid.NewGuid());
        LiveCaptureDocument second = BuildJudgeableCapture(Guid.NewGuid());

        Assert.False(LiveCaptureDocument.JudgeSet([first, second]).Coherent);

        first.Recipe.SplAnchorOffsetDb = 94.0;
        second.Recipe.SplAnchorOffsetDb = 94.0;

        Assert.True(LiveCaptureDocument.JudgeSet([first, second]).Coherent);
    }

    private static LiveCaptureDocument BuildJudgeableCapture(Guid? session = null) =>
        new()
        {
            SavedAtUtc = DateTimeOffset.UnixEpoch,
            Title = "capture",
            CaptureSessionId = session ?? Guid.Empty,
            CurveDb = Enumerable.Repeat(-20.0, 1_024).ToArray(),
            GridStartHz = 20,
            GridStopHz = 20_000,
            Recipe = BuildRecipe()
        };

    private static LiveCaptureDocument BuildDocument(
        double[] amplitude,
        List<SignalPoint> curve)
    {
        // The production storage rule, not a copy of it — a copy had already lost
        // the half-spectrum clamp, so the round-trip test was pinning the wrong thing.
        double[] spectrumDb = LiveCaptureDocument.StoreSpectrumBins(
            amplitude, SequenceLength, SampleRate);

        return new LiveCaptureDocument
        {
            SavedAtUtc = DateTimeOffset.UnixEpoch,
            Title = "l tw",
            CaptureSessionId = Guid.Empty,
            SpectrumDb = spectrumDb,
            CurveDb = curve.Select(point => point.Y).ToArray(),
            GridStartHz = curve[0].X,
            GridStopHz = curve[^1].X,
            Recipe = BuildRecipe()
        };
    }

    private static LiveCaptureRecipe BuildRecipe() => new()
    {
        AnalysisMode = LiveAnalysisMode.Mmm,
        SampleRateHz = SampleRate,
        SequenceLength = SequenceLength,
        FrameMilliseconds = 1000.0 * SequenceLength / SampleRate,
        WindowType = WindowType.Rectangular,
        WindowEnbwBins =
            Windowing.EquivalentNoiseBandwidthBins(WindowType.Rectangular, SequenceLength),
        WindowMainLobeBins = Windowing.MainLobeWidthBins(WindowType.Rectangular),
        OverlapPercent = 0,
        AveragingSpeed = AveragingSpeed.Infinite,
        AveragedFrameCount = 130,
        IntegratedSeconds = 130.0 * SequenceLength / SampleRate,
        NoiseColor = NoiseColor.PinkPeriodic,
        SlopeCompensation = true,
        MagnitudeScale = MagnitudeScale.SoundPressureLevel,
        SmoothingCode = 0
    };
}
