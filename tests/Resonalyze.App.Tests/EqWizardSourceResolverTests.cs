using System.Drawing;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class EqWizardSourceResolverTests
{
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
        file.CapturedYAxisKey = "coherence";

        Assert.False(EqWizardSourceResolver.IsEligible(file));
    }

    [Fact]
    public void IsEligible_AcceptsASweepCaptureOnTheNamedDecibelAxis()
    {
        OverlayFile file = CreateCapturedSlot(1);
        // Sweep modes attach curves to the dB axis by key; it is still the level axis, not a secondary one.
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
            Assert.True(File.Exists(corruptPath));
            Assert.False(File.Exists(corruptPath + ".corrupt"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

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
            // The offset is cosmetic; equalization needs the level as measured.
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
        // The text path must honour the kind like the slot menu, or an exported harmonic slot imports as a response.
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
        var curve = new OverlayTextCurve(
            [new OverlayPoint(100, 0), new OverlayPoint(1_000, -2)],
            new OverlayTextMetadata(role, CurveKind: AnalysisCurveKind.Primary));

        Assert.Throws<InvalidDataException>(
            () => EqWizardSourceResolver.CreateFromTextCurve(curve, "target.txt"));
    }

    [Fact]
    public void CreateFromOverlayFile_NoRawCapture_CarriesItsPointsCalibrationAndSmoothing()
    {
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
        // A dB SPL sweep smooths inside a Lanczos resample of linear amplitude, which cannot be replayed from the curve,
        // so it stays unsmoothable; only the RTA's band smoothing is replayable.
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
        // Normalization drops points; a separately normalized correction would shift to the wrong frequencies.
        OverlayFile file = CreateCapturedSlot(1);
        file.Points =
        [
            new OverlayPoint(1_000, 78),
            new OverlayPoint(100, 80),
            new OverlayPoint(100, 99)
        ];
        file.PointsCalibrationCorrectionDb = [3.0, 1.0, 9.0];

        EqWizardCurveSource source = EqWizardSourceResolver.CreateFromOverlayFile(file);

        Assert.Equal([100, 1_000], source.Points.Select(point => point.X));
        Assert.Equal([1.0, 3.0], source.PointsCalibrationCorrectionDb);
    }

    [Fact]
    public void CreateFromOverlayFile_MismatchedCorrectionIsDiscarded()
    {
        OverlayFile file = CreateCapturedSlot(1);
        file.Points = [new OverlayPoint(100, 80), new OverlayPoint(1_000, 78)];
        file.PointsCalibrationCorrectionDb = [1.0];

        EqWizardCurveSource source = EqWizardSourceResolver.CreateFromOverlayFile(file);

        Assert.Empty(source.PointsCalibrationCorrectionDb);
        Assert.False(source.SupportsCalibration);
    }

    [Fact]
    public void NormalizePoints_SortsDropsDuplicatesAndKeepsUnmeasuredBands()
    {
        IReadOnlyList<SignalPoint> result = EqWizardSourceResolver.NormalizePoints(
        [
            new SignalPoint(1_000, -3),
            new SignalPoint(100, -6),
            new SignalPoint(100, -99),
            new SignalPoint(double.NaN, -1),
            new SignalPoint(-5, -1),
            new SignalPoint(500, double.NaN),
            new SignalPoint(2_000, double.PositiveInfinity)
        ]);

        Assert.Equal([100, 500, 1_000], result.Select(point => point.X));
        Assert.Equal(-6, result[0].Y);
        // A NaN level marks an untrusted band; bridging it would invent data for the fitter.
        Assert.True(double.IsNaN(result[1].Y));
    }

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
