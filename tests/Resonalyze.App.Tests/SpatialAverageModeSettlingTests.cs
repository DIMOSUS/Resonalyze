using System.Reflection;
using System.Runtime.CompilerServices;

namespace Resonalyze.App.Tests;

/// <remarks>Recomputed live, one new array measurement flipped a legacy attachments project onto the array method.</remarks>
public sealed class SpatialAverageModeSettlingTests
{
    private static LiveCaptureDocument Capture(SpatialAverageMethod method) =>
        new()
        {
            SavedAtUtc = DateTimeOffset.UnixEpoch,
            Title = method.ToString(),
            Method = method,
            CurveDb = [0.0, 0.0],
            GridStartHz = 20,
            GridStopHz = 20_000,
            Recipe = new LiveCaptureRecipe { SampleRateHz = 48_000, MicrophoneCount = 7 }
        };

    private static VirtualCrossoverChannel Attached(string name)
    {
        var channel = new VirtualCrossoverChannel(name);
        channel.SideState(rightSide: false).SpatialAverage =
            Capture(SpatialAverageMethod.MovingMic);
        return channel;
    }

    private static VirtualCrossoverChannel WithArray(string name)
    {
        var channel = new VirtualCrossoverChannel(name);
        channel.SideState(rightSide: false).ArrayCapture =
            Capture(SpatialAverageMethod.MicArray);
        return channel;
    }

    private static VirtualCrossoverSpatialAverageMode? Settle(
        VirtualCrossoverProjectFile project,
        params VirtualCrossoverChannel[] channels)
    {
        object panel = RuntimeHelpers.GetUninitializedObject(typeof(VirtualCrossoverPanel));
        typeof(VirtualCrossoverPanel)
            .GetField("project", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(panel, project);
        typeof(VirtualCrossoverPanel)
            .GetField("channels", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(panel, channels.ToList());
        typeof(VirtualCrossoverPanel)
            .GetMethod("SettleSpatialAverageMode", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(panel, []);
        return project.SpatialAverageMode;
    }

    [Fact]
    public void ALegacyProjectWithAttachmentsSettlesOnTheMovingMicrophone()
    {
        var project = new VirtualCrossoverProjectFile();
        Assert.Equal(
            VirtualCrossoverSpatialAverageMode.MovingMic,
            Settle(project, Attached("A"), new VirtualCrossoverChannel("B")));
    }

    [Fact]
    public void AnArrayArrivingLaterDoesNotChangeIt()
    {
        var project = new VirtualCrossoverProjectFile();
        VirtualCrossoverChannel attached = Attached("A");
        Settle(project, attached, new VirtualCrossoverChannel("B"));

        Assert.Equal(
            VirtualCrossoverSpatialAverageMode.MovingMic,
            Settle(project, attached, WithArray("B")));
    }

    [Fact]
    public void AProjectWhoseFirstMeasurementCarriesAnArrayUsesIt()
    {
        var project = new VirtualCrossoverProjectFile();
        Assert.Equal(
            VirtualCrossoverSpatialAverageMode.MicArray,
            Settle(project, WithArray("A"), new VirtualCrossoverChannel("B")));
    }

    [Fact]
    public void AProjectHOLDINGBothKeepsWhatItAlreadyShowed()
    {
        // Field case: arrays plus comparison moving-mic files; settling stores the answer already shown, not a new one.
        var project = new VirtualCrossoverProjectFile();
        VirtualCrossoverChannel both = Attached("A");
        both.SideState(rightSide: false).ArrayCapture = Capture(SpatialAverageMethod.MicArray);

        Assert.Equal(
            VirtualCrossoverSpatialAverageMode.MicArray,
            Settle(project, both, WithArray("B")));
    }

    [Fact]
    public void AnEmptyProjectStoresNothingYet()
    {
        // Settling an empty project would freeze a guess the first array could never override.
        var project = new VirtualCrossoverProjectFile();
        Assert.Null(Settle(project, new VirtualCrossoverChannel("A")));
    }

    [Fact]
    public void AStoredChoiceIsNeverOverwritten()
    {
        var project = new VirtualCrossoverProjectFile
        {
            SpatialAverageMode = VirtualCrossoverSpatialAverageMode.Off
        };
        Assert.Equal(
            VirtualCrossoverSpatialAverageMode.Off,
            Settle(project, Attached("A"), WithArray("B")));
    }
}
