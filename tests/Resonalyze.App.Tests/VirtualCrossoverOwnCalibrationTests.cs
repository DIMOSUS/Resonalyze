using OxyPlot;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>"Own (as measured)": each curve is read through the calibration its measurement recorded (arithmetic pinned in MeasuredSumTests).</summary>
public sealed class VirtualCrossoverOwnCalibrationTests
{
    private static readonly CalibrationFile PanelCurve =
        CalibrationFile.Parse("20 0\n20000 5\n");

    private static readonly CalibrationFile CapsuleA =
        CalibrationFile.Parse("20 0\n20000 -2\n");

    private static readonly CalibrationFile CapsuleB =
        CalibrationFile.Parse("20 0\n20000 3\n");

    private static VirtualCrossoverCalibrationPolicy Policy(bool own) =>
        new(own, own ? null : PanelCurve, SelectedName: null);

    private static VirtualCrossoverWarnings Warnings(
        bool own, VirtualCrossoverProjectFile? project = null) =>
        new(new VirtualCrossoverSession
        {
            Project = project ?? new VirtualCrossoverProjectFile(),
            Calibration = Policy(own)
        });

    private static ProcessedChannel Channel(string name, CalibrationFile? own) =>
        new(
            new VirtualCrossoverChannel(name),
            new System.Numerics.Complex[8],
            PeakIndex: 0,
            SampleRate: 48_000,
            OxyColors.White,
            default,
            default,
            own);

    [Fact]
    public void AChannelIsReadThroughItsOwnCalibration()
    {
        ProcessedChannel channel = Channel("left", CapsuleA);

        Assert.Same(CapsuleA, Policy(own: true).For(channel));
        Assert.Same(PanelCurve, Policy(own: false).For(channel));
    }

    [Fact]
    public void AMeasurementNamingNoCalibrationIsReadThroughNone()
    {
        // No fallback to the panel's curve: the file says it was read through none.
        Assert.Null(
            Policy(own: true).For(Channel("left", null)));
    }

    [Fact]
    public void ACaptureIsReadAsMeasuredRatherThanThroughTheResponseBesideIt()
    {
        // A stored capture carries the correction IT was taken through, not the IR's.
        var state = new VirtualCrossoverChannelState
        {
            MicrophoneCalibration = VirtualCrossoverCalibrationSettings.From(
                CapsuleA, "the response's", null)
        };

        Assert.Equal(
            SpatialAverageCalibration.Own,
            Policy(own: true).SpatialAverageFor());

        Assert.Equal(
            SpatialAverageCalibration.Specific(PanelCurve),
            Policy(own: false).SpatialAverageFor());
    }

    [Fact]
    public void ANamedCalibrationAnAggregateCannotTakeIsSaidOutLoud()
    {
        // A multi-capsule capture has no single mic to swap, so it keeps its own and the note must say so.
        var project = new VirtualCrossoverProjectFile
        {
            SpatialAverageMode = VirtualCrossoverSpatialAverageMode.MovingMic
        };
        var channel = new VirtualCrossoverChannel("left");
        channel.SideState(false).SpatialAverage = new LiveCaptureDocument
        {
            SavedAtUtc = DateTimeOffset.UnixEpoch,
            Title = "several capsules",
            CurveDb = [70.0, 70.0],
            CalibrationCorrectionDb = [1.0, 1.0],
            CalibrationIsAggregate = true,
            GridStartHz = 20,
            GridStopHz = 20_000,
            Recipe = new LiveCaptureRecipe { SampleRateHz = 48_000 }
        };

        VirtualCrossoverWarnings warnings = Warnings(own: false, project);
        IReadOnlyList<ProcessedChannel> drawn = [Channel("left", CapsuleA) with
        {
            Channel = channel
        }];

        string? notice = warnings.DescribeUnappliedCalibration(drawn);
        Assert.NotNull(notice);
        Assert.Contains("belongs to no single microphone", notice!);

        Assert.Null(Warnings(own: true, project).DescribeUnappliedCalibration(drawn));

        channel.SideState(false).SpatialAverage!.CalibrationIsAggregate = false;
        Assert.Null(warnings.DescribeUnappliedCalibration(drawn));
    }

    [Fact]
    public void ChannelsMeasuredThroughDifferentMicrophonesAreSaidOutLoud()
    {
        IReadOnlyList<ProcessedChannel> channels =
            [Channel("left", CapsuleA), Channel("right", CapsuleB)];
        VirtualCrossoverWarnings warnings = Warnings(own: true);

        string? notice = warnings.DescribeOwnCalibrationMismatch(channels);
        Assert.NotNull(notice);
        Assert.Contains("right", notice!);
        Assert.Contains("the sum carries each channel's correction with it", notice);
        Assert.DoesNotContain("drawn through none", notice);

        Assert.Null(warnings.DescribeOwnCalibrationMismatch(
            [Channel("left", CapsuleA), Channel("right", CapsuleA)]));
        Assert.Null(Warnings(own: false).DescribeOwnCalibrationMismatch(channels));
    }

    [Fact]
    public void ASelectionOtherThanTheMeasurementsOwn_IsSaidOutLoud_OffIncluded()
    {
        IReadOnlyList<ProcessedChannel> channels = [Channel("left", CapsuleA), Channel("right", null)];

        string? named = Warnings(own: false).DescribeForeignCalibration(channels, hybrid: null);
        string? off = new VirtualCrossoverWarnings(new VirtualCrossoverSession
        {
            Project = new VirtualCrossoverProjectFile(),
            Calibration = VirtualCrossoverCalibrationPolicy.None
        }).DescribeForeignCalibration(channels, hybrid: null);

        Assert.NotNull(named);
        Assert.Contains("left", named!);
        Assert.DoesNotContain("right", named);
        Assert.NotNull(off);
        Assert.Contains("no calibration at all", off!);
        Assert.Null(Warnings(own: true).DescribeForeignCalibration(channels, hybrid: null));
        Assert.Null(Warnings(own: false).DescribeForeignCalibration([Channel("left", PanelCurve)], hybrid: null));
    }

    // A hybrid draws the capture, so the capture's own file is what the selection is compared with.
    [Theory]
    [InlineData(VirtualCrossoverSpatialAverageMode.MovingMic, true, false)]
    [InlineData(VirtualCrossoverSpatialAverageMode.MovingMic, false, true)]
    [InlineData(VirtualCrossoverSpatialAverageMode.MicArray, true, true)]
    public void UnderAHybridTheDrawnCurvesSourceDecides(
        VirtualCrossoverSpatialAverageMode mode, bool selectCapturesFile, bool expectNotice)
    {
        // The IR through the panel's curve, the capture (none for the array case) through capsule A.
        ProcessedChannel channel = Channel("left", PanelCurve);
        VirtualCrossoverChannelState state = channel.Channel.SideState(false);
        state.MicrophoneCalibration = Settings(PanelCurve, "panel");
        if (mode == VirtualCrossoverSpatialAverageMode.MovingMic)
        {
            state.SpatialAverage = new LiveCaptureDocument
            {
                Method = SpatialAverageMethod.MovingMic,
                Calibration = Settings(CapsuleA, "capsule A")
            };
        }

        var warnings = new VirtualCrossoverWarnings(new VirtualCrossoverSession
        {
            Project = new VirtualCrossoverProjectFile { SpatialAverageMode = mode },
            Calibration = new(false, selectCapturesFile ? CapsuleA : PanelCurve, SelectedName: null)
        });
        var hybrid = new HybridMagnitudes([], [], [null], 0);

        string? notice = warnings.DescribeForeignCalibration([channel], hybrid);

        Assert.Equal(expectNotice, notice != null);
    }

    private static VirtualCrossoverCalibrationSettings Settings(CalibrationFile curve, string name) =>
        VirtualCrossoverCalibrationSettings.From(curve, name, null);
}
