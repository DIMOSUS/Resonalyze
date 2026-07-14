using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class EqWizardAutoTuneOrchestratorTests
{
    [Fact]
    public async Task TuneLatestAsync_SlowOlderFitCannotOverwriteNewerResult()
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int invocation = 0;
        var orchestrator = new EqWizardAutoTuneOrchestrator(_ =>
        {
            if (Interlocked.Increment(ref invocation) == 1)
            {
                firstStarted.SetResult();
                releaseFirst.Task.GetAwaiter().GetResult();
                return new EqualizationCurve([], preampDb: 1);
            }

            return new EqualizationCurve([], preampDb: 2);
        });
        EqWizardAutoTuneRequest request = CreateRequest();

        Task<EqualizationCurve?> first = orchestrator.TuneLatestAsync(request);
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        EqualizationCurve? second = await orchestrator.TuneLatestAsync(request);
        releaseFirst.SetResult();

        Assert.Equal(2, second?.PreampDb);
        Assert.Null(await first);
    }

    [Fact]
    public async Task Invalidate_OrphansInFlightFit()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var orchestrator = new EqWizardAutoTuneOrchestrator(_ =>
        {
            started.SetResult();
            release.Task.GetAwaiter().GetResult();
            return new EqualizationCurve([], preampDb: 3);
        });

        Task<EqualizationCurve?> fit = orchestrator.TuneLatestAsync(CreateRequest());
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        orchestrator.Invalidate();
        release.SetResult();

        Assert.Null(await fit);
    }

    [Fact]
    public void Request_CopiesMutableInputCurves()
    {
        var source = new List<SignalPoint> { new(100, 1) };
        var target = new List<SignalPoint> { new(100, 2) };

        EqWizardAutoTuneRequest request = new(source, target, new EqAutoTuner.Options());
        source[0] = new SignalPoint(200, 3);
        target.Clear();

        Assert.Equal(new SignalPoint(100, 1), Assert.Single(request.Source));
        Assert.Equal(new SignalPoint(100, 2), Assert.Single(request.Target));
    }

    private static EqWizardAutoTuneRequest CreateRequest() => new(
        [new SignalPoint(100, 0), new SignalPoint(1000, 0)],
        [new SignalPoint(100, 0), new SignalPoint(1000, 0)],
        new EqAutoTuner.Options());
}
