using System.Numerics;
using OxyPlot;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <remarks>A render bakes one filter into a summed side, so disagreeing channels must refuse rather than pick one.</remarks>
public sealed class VirtualCrossoverAuditionOwnCalibrationTests
{
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

    /// <summary>A microphone array is NOT this case: it shares one sweep with the measurement microphone.</summary>
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

    [Fact]
    public void MeasurementsThatRecordedNone_RenderWithNone()
    {
        VirtualCrossoverAuditionOwnCalibration own = Resolve(
            [("Sub", null, null), ("Tweeter", null, null)]);

        Assert.Null(own.Conflict);
        Assert.Null(own.Curve);
        Assert.Null(own.Name);
    }

    [Fact]
    public void OneChannelCorrectedAndAnotherNot_Refuses()
    {
        VirtualCrossoverAuditionOwnCalibration own = Resolve(
            [("Sub", null, null), ("Tweeter", Curve(2.0), "XREF 20")]);

        Assert.NotNull(own.Conflict);
        Assert.Contains("none recorded", own.Conflict);
    }

    /// <summary>Half a tune renders both ears from the side that HAS sources; reversed, per-side lookups read the empty side.</summary>
    [Theory]
    [InlineData(true, true, new[] { false, true })]
    [InlineData(true, false, new[] { false })]
    [InlineData(false, true, new[] { true })]
    public void TheMeasuredSidesAreTheSidesThatHaveSources(
        bool hasLeft, bool hasRight, bool[] expected) =>
        Assert.Equal(expected, VirtualCrossoverPanel.MeasuredSides(hasLeft, hasRight));

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
