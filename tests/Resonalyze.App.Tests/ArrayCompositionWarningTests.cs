namespace Resonalyze.App.Tests;

/// <summary>Array sets are levelled by the loopback, so this warning is the only check on composition; it reads the whole project.</summary>
public sealed class ArrayCompositionWarningTests
{
    private static LiveCaptureDocument Array(
        int microphones,
        double[]? aggregateCorrectionDb = null) =>
        new()
        {
            SavedAtUtc = DateTimeOffset.UnixEpoch,
            Title = $"{microphones} mics",
            Method = SpatialAverageMethod.MicArray,
            CurveDb = [0.0, 0.0],
            GridStartHz = 20,
            GridStopHz = 20_000,
            CalibrationIsAggregate = aggregateCorrectionDb != null,
            CalibrationCorrectionDb = aggregateCorrectionDb ?? [],
            Recipe = new LiveCaptureRecipe
            {
                SampleRateHz = 48_000,
                MicrophoneCount = microphones
            }
        };

    private static VirtualCrossoverChannel Channel(
        string name,
        int? leftMicrophones,
        int? rightMicrophones,
        bool enabled = true)
    {
        var channel = new VirtualCrossoverChannel(name);
        channel.Pair.Enabled = enabled;
        if (leftMicrophones is { } left)
        {
            channel.SideState(rightSide: false).ArrayCapture = Array(left);
        }
        if (rightMicrophones is { } right)
        {
            channel.SideState(rightSide: true).ArrayCapture = Array(right);
        }

        return channel;
    }

    private static string? Describe(params VirtualCrossoverChannel[] channels)
    {
        var session = new VirtualCrossoverSession
        {
            Project = new VirtualCrossoverProjectFile
            {
                SpatialAverageMode = VirtualCrossoverSpatialAverageMode.MicArray
            }
        };
        session.Channels.AddRange(channels);
        return new VirtualCrossoverWarnings(session).DescribeArrayCompositionMismatch();
    }

    [Fact]
    public void SidesAveragedOverDifferentArraysAreReported()
    {
        // Each side is consistent (7 left, 5 right), but the dashed opposite-side sum compares two volumes.
        string? mismatch = Describe(
            Channel("A", leftMicrophones: 7, rightMicrophones: 5),
            Channel("B", leftMicrophones: 7, rightMicrophones: 5));

        Assert.NotNull(mismatch);
        Assert.Contains("A L    7 microphone(s)", mismatch);
        Assert.Contains("A R    5 microphone(s)", mismatch);
        Assert.Contains("B R    5 microphone(s)", mismatch);
    }

    [Fact]
    public void TwoDIFFERENTAggregateCorrectionsAreReported()
    {
        // Mixed-calibration arrays all declare null; compare on the declared correction, not the named curve.
        var a = new VirtualCrossoverChannel("A");
        a.SideState(rightSide: false).ArrayCapture = Array(7, [0.0, 1.5]);
        a.SideState(rightSide: true).ArrayCapture = Array(7, [0.0, 1.5]);
        var b = new VirtualCrossoverChannel("B");
        b.SideState(rightSide: false).ArrayCapture = Array(7, [0.0, -2.0]);
        b.SideState(rightSide: true).ArrayCapture = Array(7, [0.0, -2.0]);

        Assert.NotNull(Describe(a, b));

        b.SideState(rightSide: false).ArrayCapture = Array(7, [0.0, 1.5]);
        b.SideState(rightSide: true).ArrayCapture = Array(7, [0.0, 1.5]);
        Assert.Null(Describe(a, b));
    }

    [Fact]
    public void AnAggregateIsNotTheSameAsNoCalibrationAtAll()
    {
        var mixed = new VirtualCrossoverChannel("A");
        mixed.SideState(rightSide: false).ArrayCapture = Array(7, [0.0, 1.5]);
        mixed.SideState(rightSide: true).ArrayCapture = Array(7, [0.0, 1.5]);

        Assert.NotNull(Describe(mixed, Channel("B", 7, 7)));
    }

    [Fact]
    public void OneArrayThroughoutTheProjectIsNotReported()
    {
        Assert.Null(Describe(
            Channel("A", leftMicrophones: 7, rightMicrophones: 7),
            Channel("B", leftMicrophones: 7, rightMicrophones: 7)));
    }

    [Fact]
    public void AMutedChannelIsJudgedAndMarked()
    {
        string? mismatch = Describe(
            Channel("A", leftMicrophones: 7, rightMicrophones: 7),
            Channel("B", leftMicrophones: 4, rightMicrophones: 4, enabled: false));

        Assert.NotNull(mismatch);
        Assert.Contains("B L    4 microphone(s)", mismatch);
        Assert.Contains("(muted)", mismatch);
    }

    [Fact]
    public void AMonoPairIsListedOnceRatherThanComparedWithItself()
    {
        var sub = new VirtualCrossoverChannel("Sub");
        sub.Pair.Mono = true;
        sub.SideState(rightSide: false).ArrayCapture = Array(7);

        Assert.Null(Describe(sub, Channel("A", leftMicrophones: 7, rightMicrophones: 7)));

        string? mismatch = Describe(
            sub,
            Channel("A", leftMicrophones: 5, rightMicrophones: 5));
        Assert.NotNull(mismatch);
        Assert.Contains("Sub    7 microphone(s)", mismatch);
        Assert.DoesNotContain("Sub L", mismatch);
        Assert.DoesNotContain("Sub R", mismatch);
    }
}
