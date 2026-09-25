namespace Resonalyze.App.Tests;

public sealed class ModeControllerTests
{
    [Fact]
    public async Task SelectAsync_RunsTheSwitchInOrder()
    {
        var calls = new List<string>();
        ModeController controller = CreateController(calls, stop: () => Task.CompletedTask);

        await controller.SelectAsync(ModeTab.Impulse);

        Assert.Equal(
            new[] { "leave", "stop", "enter:ImpulseResponse", "tab:Impulse", "present" },
            calls);
        Assert.Equal(ModeTab.Impulse, controller.ActiveTab);
    }

    // Live Spectrum draws its captures before the plot restores the overlays over them.
    [Fact]
    public async Task SelectAsync_TakesEveryViewThroughEachStepInOrder()
    {
        var calls = new List<string>();
        var controller = new ModeController(
            [new RecordingView(calls, "live"), new RecordingView(calls, "plot")],
            () =>
            {
                calls.Add("stop");
                return Task.CompletedTask;
            },
            descriptor => calls.Add($"tab:{descriptor.Tab}"));

        await controller.SelectAsync(ModeTab.LiveSpectrum);

        Assert.Equal(
            new[]
            {
                "leave:live", "leave:plot", "stop", "enter:LiveSpectrum:live", "enter:LiveSpectrum:plot",
                "tab:LiveSpectrum", "present:live", "present:plot"
            },
            calls);
    }

    [Fact]
    public async Task SelectAsync_SerializesOverlappingSwitches()
    {
        var calls = new List<string>();
        var firstSwitchBlocked = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int stops = 0;
        ModeController controller = CreateController(
            calls,
            stop: () =>
            {
                stops++;
                return stops == 1 ? firstSwitchBlocked.Task : Task.CompletedTask;
            });

        Task first = controller.SelectAsync(ModeTab.Impulse);
        Task second = controller.SelectAsync(ModeTab.Phase);

        // The second switch must queue behind the first, not interleave with it.
        Assert.Equal(1, stops);
        Assert.DoesNotContain("tab:Impulse", calls);

        firstSwitchBlocked.SetResult();
        await first;
        await second;

        Assert.Equal(2, stops);
        Assert.Equal(ModeTab.Phase, controller.ActiveTab);
        Assert.Equal(
            new[]
            {
                "leave", "stop", "enter:ImpulseResponse", "tab:Impulse", "present",
                "leave", "stop", "enter:PhaseResponse", "tab:Phase", "present"
            },
            calls);
    }

    [Fact]
    public async Task SelectAsync_ContinuesAfterFailedSwitch()
    {
        var calls = new List<string>();
        bool fail = true;
        ModeController controller = CreateController(
            calls,
            stop: () =>
            {
                if (fail)
                {
                    fail = false;
                    throw new InvalidOperationException("boom");
                }

                return Task.CompletedTask;
            });

        Task failed = controller.SelectAsync(ModeTab.Impulse);
        Task recovered = controller.SelectAsync(ModeTab.Phase);

        await Assert.ThrowsAsync<InvalidOperationException>(() => failed);
        await recovered;

        Assert.Equal(ModeTab.Phase, controller.ActiveTab);
        Assert.Contains("tab:Phase", calls);
        Assert.DoesNotContain("tab:Impulse", calls);
        Assert.DoesNotContain("enter:ImpulseResponse", calls);
    }

    [Fact]
    public async Task ChooseAsync_OfTheTabAlreadyShown_StopsNothing()
    {
        var calls = new List<string>();
        ModeController controller = CreateController(calls, stop: () => Task.CompletedTask);
        await controller.SelectAsync(ModeTab.LiveSpectrum);
        calls.Clear();

        await controller.ChooseAsync(ModeTab.LiveSpectrum);
        Assert.Empty(calls);

        await controller.ChooseAsync(ModeTab.Frequency);
        Assert.Contains("stop", calls);
        Assert.Equal(ModeTab.Frequency, controller.ActiveTab);
    }

    [Fact]
    public async Task ChooseAsync_RetriesATabWhoseSwitchFailed()
    {
        var calls = new List<string>();
        bool fail = true;
        ModeController controller = CreateController(
            calls,
            stop: () =>
            {
                if (fail)
                {
                    fail = false;
                    throw new InvalidOperationException("boom");
                }

                return Task.CompletedTask;
            });

        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.ChooseAsync(ModeTab.Impulse));
        await controller.ChooseAsync(ModeTab.Impulse);

        Assert.Equal(ModeTab.Impulse, controller.ActiveTab);
    }

    [Fact]
    public async Task ChooseAsync_RetriesATabWhoseSwitchWasCancelled()
    {
        var calls = new List<string>();
        bool cancel = true;
        ModeController controller = CreateController(
            calls,
            stop: () =>
            {
                if (cancel)
                {
                    cancel = false;
                    throw new OperationCanceledException();
                }

                return Task.CompletedTask;
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => controller.ChooseAsync(ModeTab.Impulse));
        await controller.ChooseAsync(ModeTab.Impulse);

        Assert.Equal(ModeTab.Impulse, controller.ActiveTab);
    }

    private static ModeController CreateController(List<string> calls, Func<Task> stop) =>
        new(
            [new RecordingView(calls)],
            () =>
            {
                calls.Add("stop");
                return stop();
            },
            descriptor => calls.Add($"tab:{descriptor.Tab}"));

    private sealed class RecordingView(List<string> calls, string? name = null) : IModeView
    {
        private string Suffix => name == null ? "" : ":" + name;

        public void Leave() => calls.Add("leave" + Suffix);

        public void Enter(ModeDescriptor mode) => calls.Add($"enter:{mode.Mode}" + Suffix);

        public void Present() => calls.Add("present" + Suffix);
    }
}
