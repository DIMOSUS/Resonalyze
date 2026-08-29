using System.Numerics;
using OxyPlot;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// What the panel's "Own (as measured)" resolves to for an audition render.
/// </summary>
/// <remarks>
/// The panel can hold that selection because it corrects each channel separately; a
/// render bakes one filter into a side several channels have already been summed
/// into. Where the channels agree the rule still names a curve; where they do not,
/// the render has to refuse rather than pick one and label it as though it answered
/// for all of them.
/// </remarks>
public sealed class VirtualCrossoverAuditionOwnCalibrationTests
{
    /// <summary>
    /// One microphone measured the car — the ordinary case — so Own names its curve
    /// and the render carries it. This is the case that used to render UNCALIBRATED,
    /// because the app's calibration list has never heard of the Own id.
    /// </summary>
    [Fact]
    public void ChannelsThroughOneCalibration_ResolveToIt()
    {
        CalibrationFile curve = Curve(2.0);

        VirtualCrossoverAuditionOwnCalibration own = Resolve(
            [("Sub", curve, "XREF 20"), ("Tweeter", curve, "XREF 20")]);

        Assert.Null(own.Conflict);
        Assert.Equal("XREF 20", own.Name);
        Assert.True(CalibrationFile.SameCurve(curve, own.Curve));
    }

    /// <summary>
    /// Channels read through different calibrations have no single answer, and the
    /// refusal names them — an array of capsules, or channels measured on different
    /// days with different microphones.
    /// </summary>
    [Fact]
    public void ChannelsThroughDifferentCalibrations_Refuse()
    {
        VirtualCrossoverAuditionOwnCalibration own = Resolve(
            [("Sub", Curve(2.0), "XREF 20"), ("Tweeter", Curve(-3.0), "UMIK")]);

        Assert.NotNull(own.Conflict);
        Assert.Contains("XREF 20", own.Conflict);
        Assert.Contains("UMIK", own.Conflict);
        Assert.Null(own.Curve);
    }

    /// <summary>
    /// A measurement that recorded no calibration is an answer too — "none" — and a
    /// render with no correction is what Own means there. It must not read as a
    /// refusal.
    /// </summary>
    [Fact]
    public void MeasurementsThatRecordedNone_RenderWithNone()
    {
        VirtualCrossoverAuditionOwnCalibration own = Resolve(
            [("Sub", null, null), ("Tweeter", null, null)]);

        Assert.Null(own.Conflict);
        Assert.Null(own.Curve);
        Assert.Null(own.Name);
    }

    /// <summary>
    /// One channel corrected and another not is a disagreement like any other: the
    /// render cannot carry a correction for half a side.
    /// </summary>
    [Fact]
    public void OneChannelCorrectedAndAnotherNot_Refuses()
    {
        VirtualCrossoverAuditionOwnCalibration own = Resolve(
            [("Sub", null, null), ("Tweeter", Curve(2.0), "XREF 20")]);

        Assert.NotNull(own.Conflict);
        Assert.Contains("none recorded", own.Conflict);
    }

    // One side carrying the given channels, judged on its own — the borrowed-ear
    // shape, which is also the shape that keeps a mono pair from being counted twice.
    private static VirtualCrossoverAuditionOwnCalibration Resolve(
        IReadOnlyList<(string Name, CalibrationFile? Curve, string? CalibrationName)> channels)
    {
        var processed = new List<ProcessedChannel>();
        foreach ((string name, CalibrationFile? curve, string? calibrationName) in channels)
        {
            var channel = new VirtualCrossoverChannel(name);
            channel.SideState(false).MicrophoneCalibration = curve == null
                ? null
                : VirtualCrossoverCalibrationSettings.From(curve, calibrationName!, null);
            processed.Add(
                new ProcessedChannel(channel, [Complex.One], 0, 48_000, OxyColors.White));
        }

        var side = new VirtualCrossoverSideSum([Complex.One], 0, 48_000, processed);
        return VirtualCrossoverPanel.ResolveOwnCalibration(side, side, [false]);
    }

    private static CalibrationFile Curve(double db) =>
        CalibrationFile.FromPoints(
            [new CalibrationPoint(20, db), new CalibrationPoint(20_000, db)]);
}
