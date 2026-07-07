namespace Resonalyze.App.Tests;

public sealed class ModeControllerTests
{
    [Fact]
    public async Task SelectAsync_RunsCallbacksInOrder()
    {
        var calls = new List<string>();
        ModeController controller = CreateController(
            calls,
            changeMode: _ => Task.CompletedTask);

        await controller.SelectAsync(ModeTab.Impulse);

        Assert.Equal(
            new[] { "change:ImpulseResponse", "tab:Impulse", "draw:True", "restore" },
            calls);
        Assert.Equal(ModeTab.Impulse, controller.ActiveTab);
    }

    [Fact]
    public async Task SelectAsync_SerializesOverlappingSwitches()
    {
        var calls = new List<string>();
        var firstSwitchBlocked = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int changeModeCalls = 0;
        ModeController controller = CreateController(
            calls,
            changeMode: _ =>
            {
                changeModeCalls++;
                return changeModeCalls == 1
                    ? firstSwitchBlocked.Task
                    : Task.CompletedTask;
            });

        Task first = controller.SelectAsync(ModeTab.Impulse);
        Task second = controller.SelectAsync(ModeTab.Phase);

        // The second switch must queue behind the first, not interleave with it.
        Assert.Equal(1, changeModeCalls);
        Assert.DoesNotContain("tab:Impulse", calls);

        firstSwitchBlocked.SetResult();
        await first;
        await second;

        Assert.Equal(2, changeModeCalls);
        Assert.Equal(ModeTab.Phase, controller.ActiveTab);
        Assert.Equal(
            new[]
            {
                "change:ImpulseResponse", "tab:Impulse", "draw:True", "restore",
                "change:PhaseResponse", "tab:Phase", "draw:True", "restore"
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
            changeMode: _ =>
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
    }

    [Fact]
    public async Task SelectAsync_DrawSkipsCurvesWhenModeDoesNotSupportThem()
    {
        var calls = new List<string>();
        ModeController controller = CreateController(
            calls,
            changeMode: _ => Task.CompletedTask,
            supportsCurveDrawing: _ => false);

        await controller.SelectAsync(ModeTab.LiveSpectrum);

        Assert.Contains("draw:False", calls);
    }

    private static ModeController CreateController(
        List<string> calls,
        Func<Mode, Task> changeMode,
        Func<ModeTab, bool>? supportsCurveDrawing = null)
    {
        return new ModeController(
            mode =>
            {
                calls.Add($"change:{mode}");
                return changeMode(mode);
            },
            tab => calls.Add($"tab:{tab}"),
            includeCurves => calls.Add($"draw:{includeCurves}"),
            () => calls.Add("restore"),
            () => true,
            tab => GetMode(tab),
            supportsCurveDrawing ?? (_ => true));
    }

    private static Mode GetMode(ModeTab tab) => tab switch
    {
        ModeTab.Impulse => Mode.ImpulseResponse,
        ModeTab.Phase => Mode.PhaseResponse,
        ModeTab.LiveSpectrum => Mode.LiveSpectrum,
        _ => Mode.FrequencyResponse
    };
}
