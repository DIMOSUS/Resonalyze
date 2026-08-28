using System.Reflection;
using System.Runtime.CompilerServices;

namespace Resonalyze.App.Tests;

/// <summary>
/// The spatial-average method is chosen once and kept, not recomputed from what
/// the project currently holds.
/// </summary>
/// <remarks>
/// It decides where every channel's LEVELS come from, so recomputing it live made
/// it change under a project that never chose. The migration case is the sharp one:
/// a session written before arrays existed carries attachments and no stored mode,
/// and loading one new measurement that happens to carry an array flipped the whole
/// project onto the array method — the attachments then went unread and every
/// channel without an array fell back to its point response. One channel's source
/// changed and the source of every channel's levels changed with it.
/// </remarks>
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

    // The panel, its project, and the settling call — the whole decision, with no
    // control involved.
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
        // The migration scenario, in order: the project binds with its attachments
        // and settles, and only then does a measurement carrying an array land in
        // another channel.
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
        // The other half of the rule: nothing was attached, so nothing was chosen,
        // and the measurement gets to decide. A user who just recorded an array has
        // already said what they wanted.
        var project = new VirtualCrossoverProjectFile();
        Assert.Equal(
            VirtualCrossoverSpatialAverageMode.MicArray,
            Settle(project, WithArray("A"), new VirtualCrossoverChannel("B")));
    }

    [Fact]
    public void AnEmptyProjectStoresNothingYet()
    {
        // Settling on an empty project would freeze a guess made from no evidence,
        // and the first array to arrive could then never choose.
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
