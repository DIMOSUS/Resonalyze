using System.Drawing;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class EqWizardSourceResolverTests
{
    // ------------------------------------------------------------ slot eligibility

    [Theory]
    [InlineData(AnalysisCurveKind.Primary, true)]
    [InlineData(AnalysisCurveKind.InputSpectrum, true)]
    [InlineData(AnalysisCurveKind.SecondHarmonic, false)]
    [InlineData(AnalysisCurveKind.ThdPlusNoise, false)]
    [InlineData(AnalysisCurveKind.MinimumPhase, false)]
    public void IsEligible_AcceptsOnlyMagnitudeResponseKinds(
        AnalysisCurveKind kind,
        bool expected)
    {
        OverlayFile file = CreateCapturedSlot(1);
        file.CapturedCurveKind = kind;

        Assert.Equal(expected, EqWizardSourceResolver.IsEligible(file));
    }

    [Fact]
    public void IsEligible_RejectsALegacyCaptureWithNoDeclaredKind()
    {
        OverlayFile file = CreateCapturedSlot(1);
        file.CapturedCurveKind = null;

        Assert.False(EqWizardSourceResolver.IsEligible(file));
    }

    [Fact]
    public void IsEligible_RejectsACoherenceCapture()
    {
        OverlayFile file = CreateCapturedSlot(1);
        // Coherence is drawn on its own 0..1 axis; it is a confidence, not a level.
        file.CapturedYAxisKey = "coherence";

        Assert.False(EqWizardSourceResolver.IsEligible(file));
    }

    [Fact]
    public void IsEligible_AcceptsASweepCaptureOnTheNamedDecibelAxis()
    {
        OverlayFile file = CreateCapturedSlot(1);
        // Sweep modes attach every curve to the dB axis BY KEY, so a Frequency Response
        // capture records that key rather than null. It is still the plot's own level
        // axis — reading it as "a secondary axis" hid every sweep capture from the
        // wizard while the RTA (whose series carries no key) sailed through.
        file.CapturedYAxisKey = PlotModelFactory.DecibelAxisKey;

        Assert.True(EqWizardSourceResolver.IsEligible(file));
    }

    [Theory]
    [InlineData(OverlayKind.Operation)]
    [InlineData(OverlayKind.Target)]
    public void IsEligible_RejectsCalculatedAndTargetSlots(OverlayKind kind)
    {
        OverlayFile file = CreateCapturedSlot(1);
        file.Kind = kind;

        Assert.False(EqWizardSourceResolver.IsEligible(file));
    }

    [Fact]
    public void ListEligibleSlots_ReturnsOnlyUsableSlotsInSlotOrder()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            OverlayFile sweep = CreateCapturedSlot(3);
            // A sweep FR capture stores the named dB axis key; the file round trip must
            // still surface it — this is the path the live slot menu takes.
            sweep.CapturedYAxisKey = PlotModelFactory.DecibelAxisKey;
            Save(sweep, root);
            Save(CreateCapturedSlot(1), root);

            OverlayFile ineligible = CreateCapturedSlot(2);
            ineligible.CapturedCurveKind = null;
            Save(ineligible, root);

            IReadOnlyList<EqWizardSlotOption> slots =
                new EqWizardSourceResolver(root).ListEligibleSlots();

            Assert.Equal([1, 3], slots.Select(slot => slot.Slot));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ListEligibleSlots_SkipsAnUnreadableSlotWithoutTouchingIt()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            Save(CreateCapturedSlot(1), root);
            string corruptPath = OverlayFile.GetPath(Mode.FrequencyResponse, 2, root);
            Directory.CreateDirectory(Path.GetDirectoryName(corruptPath)!);
            File.WriteAllText(corruptPath, "{ this is not a slot file");

            IReadOnlyList<EqWizardSlotOption> slots =
                new EqWizardSourceResolver(root).ListEligibleSlots();

            Assert.Equal([1], slots.Select(slot => slot.Slot));
            // Quarantining a damaged slot belongs to the overlay UI that owns it; the
            // wizard is a reader and must leave the file exactly where it was.
            Assert.True(File.Exists(corruptPath));
            Assert.False(File.Exists(corruptPath + ".corrupt"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // ----------------------------------------------------------------- slot import

    [Fact]
    public void TryCreateFromOverlaySlot_CarriesUnitRateAndRawReference()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            OverlayFile file = CreateCapturedSlot(4);
            file.CapturedCurveKind = AnalysisCurveKind.InputSpectrum;
            file.CapturedMagnitudeScale = MagnitudeScale.SoundPressureLevel;
            file.SampleRateHz = 44_100;
            file.RawSpectrum = [new OverlayPoint(20, 80), new OverlayPoint(20_000, 70)];
            file.RawCalibrationCorrectionDb = new double[RawCurveRenderer.PointCount];
            Save(file, root);

            EqWizardCurveSource? source =
                new EqWizardSourceResolver(root).TryCreateFromOverlaySlot(4);

            Assert.NotNull(source);
            Assert.Equal(EqWizardSourceKind.OverlaySlot, source!.Kind);
            Assert.Equal(MagnitudeScale.SoundPressureLevel, source.Scale);
            Assert.Equal(44_100, source.SampleRateHz);
            Assert.Equal(AnalysisCurveKind.InputSpectrum, source.CurveKind);
            Assert.NotNull(source.RawSpectrum);
            // A stored raw reference is what makes re-calibration and re-smoothing valid.
            Assert.True(source.SupportsCalibration);
            Assert.True(source.SupportsSmoothing);
            Assert.Null(source.Coherence);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TryCreateFromOverlaySlot_WithoutRawDisablesCalibrationAndSmoothing()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            // A slot filled by importing text has points but no unsmoothed reference:
            // whatever calibration and smoothing it carries are already baked in.
            Save(CreateCapturedSlot(5), root);

            EqWizardCurveSource? source =
                new EqWizardSourceResolver(root).TryCreateFromOverlaySlot(5);

            Assert.NotNull(source);
            Assert.Null(source!.RawSpectrum);
            Assert.False(source.SupportsCalibration);
            Assert.False(source.SupportsSmoothing);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TryCreateFromOverlaySlot_IgnoresTheSlotsDisplayOffset()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            OverlayFile file = CreateCapturedSlot(6);
            // The offset pulls curves apart on the plot; equalization needs the level
            // as measured, or every imported tune would inherit a cosmetic shift.
            file.Offset = 12;
            file.Points = [new OverlayPoint(100, -6), new OverlayPoint(1_000, -3)];
            Save(file, root);

            EqWizardCurveSource? source =
                new EqWizardSourceResolver(root).TryCreateFromOverlaySlot(6);

            Assert.NotNull(source);
            Assert.Equal([-6, -3], source!.Points.Select(point => point.Y));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TryCreateFromOverlaySlot_ReturnsNullForAnIneligibleSlot()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            OverlayFile file = CreateCapturedSlot(7);
            file.CapturedCurveKind = null;
            Save(file, root);

            Assert.Null(new EqWizardSourceResolver(root).TryCreateFromOverlaySlot(7));
            Assert.Null(new EqWizardSourceResolver(root).TryCreateFromOverlaySlot(8));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // ----------------------------------------------------------------- text import

    [Theory]
    [InlineData(OverlayCurveRole.Deviation)]
    [InlineData(OverlayCurveRole.EqCorrection)]
    public void CreateFromTextCurve_RejectsADifferenceCurve(OverlayCurveRole role)
    {
        var curve = new OverlayTextCurve(
            [new OverlayPoint(100, -3), new OverlayPoint(1_000, 2)],
            new OverlayTextMetadata(role));

        InvalidDataException error = Assert.Throws<InvalidDataException>(
            () => EqWizardSourceResolver.CreateFromTextCurve(curve, "deviation.txt"));
        Assert.Contains("difference", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateFromTextCurve_AcceptsAResponseAndAnUndeclaredCurve()
    {
        var declared = new OverlayTextCurve(
            [new OverlayPoint(100, 80), new OverlayPoint(1_000, 78)],
            new OverlayTextMetadata(
                OverlayCurveRole.Response,
                Scale: MagnitudeScale.SoundPressureLevel,
                SampleRateHz: 48_000));
        var foreign = new OverlayTextCurve(
            [new OverlayPoint(100, -3), new OverlayPoint(1_000, -4)],
            OverlayTextMetadata.Empty);

        EqWizardCurveSource fromDeclared =
            EqWizardSourceResolver.CreateFromTextCurve(declared, "rta.txt");
        EqWizardCurveSource fromForeign =
            EqWizardSourceResolver.CreateFromTextCurve(foreign, "rew-export.txt");

        Assert.Equal(MagnitudeScale.SoundPressureLevel, fromDeclared.Scale);
        Assert.Equal(48_000, fromDeclared.SampleRateHz);
        // An undeclared file states no unit and no rate; relative dB is the safe reading
        // and the rate falls to the panel's own selector.
        Assert.Equal(MagnitudeScale.Relative, fromForeign.Scale);
        Assert.Null(fromForeign.SampleRateHz);
        Assert.False(fromForeign.SupportsCalibration);
    }

    [Theory]
    [InlineData(AnalysisCurveKind.SecondHarmonic)]
    [InlineData(AnalysisCurveKind.ThdPlusNoise)]
    [InlineData(AnalysisCurveKind.MinimumPhase)]
    public void CreateFromTextCurve_RejectsANonResponseKindEvenWhenRoleSaysResponse(
        AnalysisCurveKind kind)
    {
        // A harmonic/THD/phase slot exports as role=Response but keeps its kind. The text
        // path must honour the kind, exactly like the slot menu, so exporting such a slot
        // and loading the text file cannot smuggle it in as a measured response.
        var curve = new OverlayTextCurve(
            [new OverlayPoint(100, -40), new OverlayPoint(1_000, -55)],
            new OverlayTextMetadata(OverlayCurveRole.Response, CurveKind: kind));

        InvalidDataException error = Assert.Throws<InvalidDataException>(
            () => EqWizardSourceResolver.CreateFromTextCurve(curve, "harmonic.txt"));
        Assert.Contains("cannot be equalized", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(OverlayCurveRole.Response, AnalysisCurveKind.Primary, true)]
    [InlineData(OverlayCurveRole.Response, AnalysisCurveKind.InputSpectrum, true)]
    [InlineData(null, null, true)]
    [InlineData(OverlayCurveRole.Deviation, AnalysisCurveKind.Primary, false)]
    [InlineData(OverlayCurveRole.EqCorrection, null, false)]
    // A target or calculated curve exported with a (possibly stale) response kind is still
    // refused on its role alone.
    [InlineData(OverlayCurveRole.Target, AnalysisCurveKind.Primary, false)]
    [InlineData(OverlayCurveRole.Calculated, AnalysisCurveKind.Primary, false)]
    [InlineData(OverlayCurveRole.Response, AnalysisCurveKind.SecondHarmonic, false)]
    [InlineData(OverlayCurveRole.Response, AnalysisCurveKind.ExcessPhase, false)]
    public void IsEqualizableResponse_GatesBothRoleAndKind(
        OverlayCurveRole? role,
        AnalysisCurveKind? kind,
        bool expected)
    {
        Assert.Equal(expected, EqWizardSourceResolver.IsEqualizableResponse(role, kind));
    }

    [Theory]
    [InlineData(OverlayCurveRole.Target)]
    [InlineData(OverlayCurveRole.Calculated)]
    public void CreateFromTextCurve_RejectsATargetOrCalculatedCurve(OverlayCurveRole role)
    {
        // Even with a response kind attached, a target/calculated file must not import as a
        // source — the exact leak of a slot exported to text and loaded straight in.
        var curve = new OverlayTextCurve(
            [new OverlayPoint(100, 0), new OverlayPoint(1_000, -2)],
            new OverlayTextMetadata(role, CurveKind: AnalysisCurveKind.Primary));

        Assert.Throws<InvalidDataException>(
            () => EqWizardSourceResolver.CreateFromTextCurve(curve, "target.txt"));
    }

    // ------------------------------------- no-raw captures (dB SPL calibration/smoothing)

    [Fact]
    public void CreateFromOverlayFile_NoRawCapture_CarriesItsPointsCalibrationAndSmoothing()
    {
        // A dB SPL RTA has no raw spectrum, but the correction frozen onto its points and
        // the smoothing it was taken under travel with it — which is what lets the wizard
        // offer the calibration selector, and the smoothing one for an unsmoothed capture.
        OverlayFile file = CreateCapturedSlot(1);
        file.CapturedCurveKind = AnalysisCurveKind.InputSpectrum;
        file.CapturedMagnitudeScale = MagnitudeScale.SoundPressureLevel;
        file.Points = [new OverlayPoint(100, 80), new OverlayPoint(1_000, 78)];
        file.PointsCalibrationCorrectionDb = [1.5, -2.0];
        file.CapturedSmoothingCode = 0;

        EqWizardCurveSource source = EqWizardSourceResolver.CreateFromOverlayFile(file);

        Assert.Null(source.RawSpectrum);
        Assert.Equal([1.5, -2.0], source.PointsCalibrationCorrectionDb);
        Assert.True(source.HasOwnCalibration);
        Assert.True(source.SupportsCalibration);
        Assert.True(source.SupportsSmoothing);
    }

    [Fact]
    public void CreateFromOverlayFile_NoRawCaptureTakenSmoothed_DoesNotOfferSmoothing()
    {
        // Its points already carry 1/6-octave smoothing; smoothing them again compounds it.
        OverlayFile file = CreateCapturedSlot(1);
        file.CapturedCurveKind = AnalysisCurveKind.InputSpectrum;
        file.Points = [new OverlayPoint(100, 80), new OverlayPoint(1_000, 78)];
        file.PointsCalibrationCorrectionDb = [0, 0];
        file.CapturedSmoothingCode = 6;

        EqWizardCurveSource source = EqWizardSourceResolver.CreateFromOverlayFile(file);

        Assert.True(source.SupportsCalibration);
        Assert.False(source.SupportsSmoothing);
    }

    [Fact]
    public void CreateFromOverlayFile_UnsmoothedSplSweep_OffersCalibrationButNotSmoothing()
    {
        // A dB SPL SWEEP has no raw form here either, and its calibration can still be
        // swapped. But its smoothing happens inside a Lanczos resample of linear
        // amplitude, which cannot be replayed from the finished curve — so it is left
        // unsmoothable rather than smoothed by a near-enough algorithm, which would feed
        // Auto Tune a shape the measurement never had. Only the RTA's smoothing is a
        // replayable second pass over stored band levels.
        OverlayFile file = CreateCapturedSlot(1);
        file.CapturedCurveKind = AnalysisCurveKind.Primary;
        file.CapturedMagnitudeScale = MagnitudeScale.SoundPressureLevel;
        file.Points = [new OverlayPoint(100, 80), new OverlayPoint(1_000, 78)];
        file.PointsCalibrationCorrectionDb = [1.0, -1.0];
        file.CapturedSmoothingCode = 0;

        EqWizardCurveSource source = EqWizardSourceResolver.CreateFromOverlayFile(file);

        Assert.True(source.SupportsCalibration);
        Assert.False(source.SupportsSmoothing);
    }

    [Fact]
    public void CreateFromOverlayFile_LegacyCaptureWithoutAnnotations_OffersNeither()
    {
        // A slot from before these fields existed says nothing: its calibration cannot be
        // undone and its points may already be smoothed, so both selectors stay closed.
        OverlayFile file = CreateCapturedSlot(1);
        file.Points = [new OverlayPoint(100, 80), new OverlayPoint(1_000, 78)];

        EqWizardCurveSource source = EqWizardSourceResolver.CreateFromOverlayFile(file);

        Assert.False(source.HasOwnCalibration);
        Assert.False(source.SupportsCalibration);
        Assert.False(source.SupportsSmoothing);
    }

    [Fact]
    public void CreateFromOverlayFile_DroppedPointsTakeTheirCorrectionWithThem()
    {
        // Normalization reorders and drops points (a duplicate frequency here). If the
        // correction were normalized separately it would shift by one and be applied at
        // the wrong frequencies — a silent mis-calibration.
        OverlayFile file = CreateCapturedSlot(1);
        file.Points =
        [
            new OverlayPoint(1_000, 78),
            new OverlayPoint(100, 80),
            new OverlayPoint(100, 99)   // duplicate frequency: dropped
        ];
        file.PointsCalibrationCorrectionDb = [3.0, 1.0, 9.0];

        EqWizardCurveSource source = EqWizardSourceResolver.CreateFromOverlayFile(file);

        Assert.Equal([100, 1_000], source.Points.Select(point => point.X));
        // 100 Hz keeps ITS correction (1.0) and 1 kHz keeps its own (3.0).
        Assert.Equal([1.0, 3.0], source.PointsCalibrationCorrectionDb);
    }

    [Fact]
    public void CreateFromOverlayFile_MismatchedCorrectionIsDiscarded()
    {
        // Not aligned to these points, so it cannot be trusted to name their frequencies.
        OverlayFile file = CreateCapturedSlot(1);
        file.Points = [new OverlayPoint(100, 80), new OverlayPoint(1_000, 78)];
        file.PointsCalibrationCorrectionDb = [1.0];

        EqWizardCurveSource source = EqWizardSourceResolver.CreateFromOverlayFile(file);

        Assert.Empty(source.PointsCalibrationCorrectionDb);
        Assert.False(source.SupportsCalibration);
    }

    // ------------------------------------------------------------- point hygiene

    [Fact]
    public void NormalizePoints_SortsDropsDuplicatesAndKeepsUnmeasuredBands()
    {
        IReadOnlyList<SignalPoint> result = EqWizardSourceResolver.NormalizePoints(
        [
            new SignalPoint(1_000, -3),
            new SignalPoint(100, -6),
            new SignalPoint(100, -99),            // duplicate frequency
            new SignalPoint(double.NaN, -1),      // no place on the axis
            new SignalPoint(-5, -1),              // ditto
            new SignalPoint(500, double.NaN),     // an unmeasured band: kept
            new SignalPoint(2_000, double.PositiveInfinity)
        ]);

        Assert.Equal([100, 500, 1_000], result.Select(point => point.X));
        Assert.Equal(-6, result[0].Y);
        // A NaN level is how a curve records a band it could not trust; bridging it
        // would invent data for the fitter to correct.
        Assert.True(double.IsNaN(result[1].Y));
    }

    // ----------------------------------------------------------------- helpers

    private static OverlayFile CreateCapturedSlot(int slot) => new()
    {
        SavedAtUtc = DateTimeOffset.UtcNow,
        Mode = Mode.FrequencyResponse,
        Slot = slot,
        Kind = OverlayKind.Captured,
        Title = $"Overlay {slot}: Magnitude",
        ColorArgb = Color.OrangeRed.ToArgb(),
        CapturedCurveKind = AnalysisCurveKind.Primary,
        Points = [new OverlayPoint(20, -10), new OverlayPoint(20_000, -20)]
    };

    private static void Save(OverlayFile file, string root) => file.Save(root);

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"Resonalyze.Tests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
