using System.Reflection;
using System.Runtime.CompilerServices;
using OxyPlot;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// "Own (as measured)": every curve read through the calibration ITS measurement
/// recorded, instead of one curve chosen for the whole project.
/// </summary>
/// <remarks>
/// The project's single calibration stopped being able to describe the measurements
/// when an array arrived — several capsules, each corrected by its own file before
/// the positions are averaged, and no one curve names that. It was never quite able
/// to: a project whose channels were measured on different days with different
/// microphones had the same problem, less visibly.
/// <para>
/// What the panel must not do is quietly average the difference away. A SUM is one
/// magnitude, and one correction subtracted from it cannot undo two microphones —
/// so channels that disagree leave their sum uncorrected, and that is said out loud
/// rather than left to look like summation loss.
/// </para>
/// </remarks>
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
        // What the panel holds when the selector names a curve — and deliberately
        // null under Own, where no single field could hold a per-channel answer.
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
        // And through the panel's under every other selection, which is what makes
        // the selector mean anything at all.
        Assert.Same(PanelCurve, Invoke<CalibrationFile?>(Panel(own: false), "CalibrationFor", channel));
    }

    [Fact]
    public void AMeasurementNamingNoCalibrationIsReadThroughNone()
    {
        // Not a fallback to the panel's: the file says it was read through none, and
        // substituting a curve it never passed would be the panel deciding what a
        // measurement means.
        Assert.Null(
            Invoke<CalibrationFile?>(Panel(own: true), "CalibrationFor", Channel("left", null)));
    }

    [Fact]
    public void ACaptureIsReadAsMeasuredRatherThanThroughTheResponseBesideIt()
    {
        // The mapping that was wrong. A stored spatial average — a moving-microphone
        // pass attached by hand, or an array recorded with the sweep — carries the
        // correction IT was taken through. Handing the hybrid this side's impulse
        // response calibration instead reads the capture through a microphone that
        // did not take it, and the error is the whole difference between the files.
        var state = new VirtualCrossoverChannelState
        {
            MicrophoneCalibration = VirtualCrossoverCalibrationSettings.From(
                CapsuleA, "the response's", null)
        };

        Assert.Equal(
            SpatialAverageCalibration.Own,
            Invoke<SpatialAverageCalibration>(
                Panel(own: true), "SpatialAverageCalibrationFor", state));

        // And under a named selection it is that curve, which is what makes the
        // selector mean anything for a capture that CAN be swapped.
        Assert.Equal(
            SpatialAverageCalibration.Specific(PanelCurve),
            Invoke<SpatialAverageCalibration>(
                Panel(own: false), "SpatialAverageCalibrationFor", state));
    }

    [Fact]
    public void ASumOfAgreeingChannelsKeepsTheirCalibration()
    {
        IReadOnlyList<ProcessedChannel> channels =
            [Channel("left", CapsuleA), Channel("right", CapsuleA)];

        Assert.Same(
            CapsuleA,
            Invoke<CalibrationFile?>(Panel(own: true), "CalibrationForSum", channels));
    }

    [Fact]
    public void ASumOfDisagreeingChannelsKeepsNoneAndSaysSo()
    {
        // One subtraction cannot undo two microphones, so the sum carries none — and
        // silence here would read as summation loss, which is exactly what it is not.
        IReadOnlyList<ProcessedChannel> channels =
            [Channel("left", CapsuleA), Channel("right", CapsuleB)];
        object panel = Panel(own: true);

        Assert.Null(Invoke<CalibrationFile?>(panel, "CalibrationForSum", channels));

        string? notice = Invoke<string?>(panel, "DescribeOwnCalibrationMismatch", channels);
        Assert.NotNull(notice);
        Assert.Contains("right", notice!);
        Assert.Contains("their SUM is drawn through none", notice);

        // Nothing to say when they agree, and nothing to say under a selection that
        // corrects everything with one curve by definition.
        Assert.Null(Invoke<string?>(
            panel,
            "DescribeOwnCalibrationMismatch",
            (IReadOnlyList<ProcessedChannel>)[Channel("left", CapsuleA), Channel("right", CapsuleA)]));
        Assert.Null(Invoke<string?>(Panel(own: false), "DescribeOwnCalibrationMismatch", channels));
    }
}
