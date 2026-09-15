using System.Reflection;
using System.Runtime.CompilerServices;
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

    private static object Panel(bool own)
    {
        object panel = RuntimeHelpers.GetUninitializedObject(typeof(VirtualCrossoverPanel));
        SetField(panel, "ownCalibrationSelected", own);
        SetProperty(panel, "Calibration", own ? null : PanelCurve);
        return panel;
    }

    private static void SetField(object target, string name, object? value) =>
        typeof(VirtualCrossoverPanel)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(target, value);

    private static void SetProperty(object target, string name, object? value) =>
        typeof(VirtualCrossoverPanel)
            .GetProperty(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(target, value);

    private static T Invoke<T>(object panel, string name, params object?[] arguments) =>
        (T)typeof(VirtualCrossoverPanel)
            .GetMethod(
                name,
                BindingFlags.NonPublic | BindingFlags.Instance,
                arguments.Select(argument => argument switch
                {
                    ProcessedChannel => typeof(ProcessedChannel),
                    VirtualCrossoverChannelState => typeof(VirtualCrossoverChannelState),
                    _ => typeof(IReadOnlyList<ProcessedChannel>)
                }).ToArray())!
            .Invoke(panel, arguments)!;

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

        Assert.Same(CapsuleA, Invoke<CalibrationFile?>(Panel(own: true), "CalibrationFor", channel));
        Assert.Same(PanelCurve, Invoke<CalibrationFile?>(Panel(own: false), "CalibrationFor", channel));
    }

    [Fact]
    public void AMeasurementNamingNoCalibrationIsReadThroughNone()
    {
        // No fallback to the panel's curve: the file says it was read through none.
        Assert.Null(
            Invoke<CalibrationFile?>(Panel(own: true), "CalibrationFor", Channel("left", null)));
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
            Invoke<SpatialAverageCalibration>(
                Panel(own: true), "SpatialAverageCalibrationFor", state));

        Assert.Equal(
            SpatialAverageCalibration.Specific(PanelCurve),
            Invoke<SpatialAverageCalibration>(
                Panel(own: false), "SpatialAverageCalibrationFor", state));
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

        object panel = Panel(own: false);
        SetField(panel, "project", project);
        SetField(panel, "comboBoxCalibration", new DarkComboBox());
        IReadOnlyList<ProcessedChannel> drawn = [Channel("left", CapsuleA) with
        {
            Channel = channel
        }];

        string? notice = Invoke<string?>(panel, "DescribeUnappliedCalibration", drawn);
        Assert.NotNull(notice);
        Assert.Contains("belongs to no single microphone", notice!);

        object own = Panel(own: true);
        SetField(own, "project", project);
        SetField(own, "comboBoxCalibration", new DarkComboBox());
        Assert.Null(Invoke<string?>(own, "DescribeUnappliedCalibration", drawn));

        channel.SideState(false).SpatialAverage!.CalibrationIsAggregate = false;
        Assert.Null(Invoke<string?>(panel, "DescribeUnappliedCalibration", drawn));
    }

    [Fact]
    public void ChannelsMeasuredThroughDifferentMicrophonesAreSaidOutLoud()
    {
        IReadOnlyList<ProcessedChannel> channels =
            [Channel("left", CapsuleA), Channel("right", CapsuleB)];
        object panel = Panel(own: true);

        string? notice = Invoke<string?>(panel, "DescribeOwnCalibrationMismatch", channels);
        Assert.NotNull(notice);
        Assert.Contains("right", notice!);
        Assert.Contains("the sum carries each channel's correction with it", notice);
        Assert.DoesNotContain("drawn through none", notice);

        Assert.Null(Invoke<string?>(
            panel,
            "DescribeOwnCalibrationMismatch",
            (IReadOnlyList<ProcessedChannel>)[Channel("left", CapsuleA), Channel("right", CapsuleA)]));
        Assert.Null(Invoke<string?>(Panel(own: false), "DescribeOwnCalibrationMismatch", channels));
    }
}
