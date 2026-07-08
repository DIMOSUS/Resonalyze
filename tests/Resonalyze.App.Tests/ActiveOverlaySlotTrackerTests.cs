namespace Resonalyze.App.Tests;

/// <summary>
/// The per-mode active-overlay-slot memory moved off Form1 into
/// <see cref="ActiveOverlaySlotTracker"/>; these pin the capture/restore
/// roundtrip and the Virtual DSP arm-new-slot path.
/// </summary>
public sealed class ActiveOverlaySlotTrackerTests
{
    [Fact]
    public void TryGet_ReturnsFalseForAnUnknownMode()
    {
        var tracker = new ActiveOverlaySlotTracker();

        Assert.False(tracker.TryGet(Mode.FrequencyResponse, out List<int> slots));
        Assert.Empty(slots);
    }

    [Fact]
    public void Store_RoundTripsPerMode()
    {
        var tracker = new ActiveOverlaySlotTracker();
        tracker.Store(Mode.FrequencyResponse, [1, 3]);
        tracker.Store(Mode.PhaseResponse, [2]);

        Assert.True(tracker.TryGet(Mode.FrequencyResponse, out List<int> frequency));
        Assert.Equal([1, 3], frequency);
        Assert.True(tracker.TryGet(Mode.PhaseResponse, out List<int> phase));
        Assert.Equal([2], phase);
    }

    [Fact]
    public void MarkActive_CreatesTheModeListAndDeduplicates()
    {
        var tracker = new ActiveOverlaySlotTracker();

        tracker.MarkActive(Mode.FrequencyResponse, 5);
        tracker.MarkActive(Mode.FrequencyResponse, 5);

        Assert.True(tracker.TryGet(Mode.FrequencyResponse, out List<int> slots));
        Assert.Equal([5], slots);
    }

    [Fact]
    public void MarkActive_AppendsToAStoredSelection()
    {
        var tracker = new ActiveOverlaySlotTracker();
        tracker.Store(Mode.FrequencyResponse, [1]);

        tracker.MarkActive(Mode.FrequencyResponse, 4);

        Assert.True(tracker.TryGet(Mode.FrequencyResponse, out List<int> slots));
        Assert.Equal([1, 4], slots);
    }
}
