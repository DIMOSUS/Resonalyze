using System.Reflection;
using System.Runtime.CompilerServices;

namespace Resonalyze.App.Tests;

/// <summary>
/// What the "not averaged over the same array" warning is judged over.
/// </summary>
/// <remarks>
/// A spatial average describes the volume its microphones stood in, so two captures
/// averaged over different numbers of positions answer slightly different questions.
/// Nothing else in the app objects to that: an array set is levelled by the loopback
/// each measurement carries, so the set verdict has no levelling complaint to make
/// and — rightly — returns Ok. This warning is the only thing that notices, which is
/// why the set it reads has to be every array in the PROJECT rather than the ones
/// currently on screen.
/// </remarks>
public sealed class ArrayCompositionWarningTests
{
    private static LiveCaptureDocument Array(int microphones) =>
        new()
        {
            SavedAtUtc = DateTimeOffset.UnixEpoch,
            Title = $"{microphones} mics",
            Method = SpatialAverageMethod.MicArray,
            CurveDb = [0.0, 0.0],
            GridStartHz = 20,
            GridStopHz = 20_000,
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
        object panel = RuntimeHelpers.GetUninitializedObject(typeof(VirtualCrossoverPanel));
        typeof(VirtualCrossoverPanel)
            .GetField("project", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(panel, new VirtualCrossoverProjectFile
            {
                SpatialAverageMode = VirtualCrossoverSpatialAverageMode.MicArray
            });
        typeof(VirtualCrossoverPanel)
            .GetField("channels", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(panel, channels.ToList());
        return (string?)typeof(VirtualCrossoverPanel)
            .GetMethod(
                "DescribeArrayCompositionMismatch",
                BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(panel, []);
    }

    [Fact]
    public void SidesAveragedOverDifferentArraysAreReported()
    {
        // The cross-side case, and the reason the warning cannot be judged one side
        // at a time: each side is internally consistent — every left capture is a
        // seven-position average, every right one a five — so a side-by-side view
        // finds nothing to say. The dashed opposite-side sum then draws two
        // different listening volumes against each other as though the difference
        // were the car.
        string? mismatch = Describe(
            Channel("A", leftMicrophones: 7, rightMicrophones: 5),
            Channel("B", leftMicrophones: 7, rightMicrophones: 5));

        Assert.NotNull(mismatch);
        Assert.Contains("A L    7 microphone(s)", mismatch);
        Assert.Contains("A R    5 microphone(s)", mismatch);
        Assert.Contains("B R    5 microphone(s)", mismatch);
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
        // A mute says which curves to draw. What a set is MADE OF is a property of
        // the measurements, so a warning that came and went with the mute buttons
        // would be describing the buttons.
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
        // A mono pair answers both sides from one slot. Listed twice it would be an
        // entry per side of one measurement — and if it were ever the only channel
        // with an array, a difference reported between a capture and itself.
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
