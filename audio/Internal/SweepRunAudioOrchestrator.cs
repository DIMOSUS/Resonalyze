using NAudio.Wave;

namespace Resonalyze.Audio;

internal interface ISweepCaptureSession
{
    /// <summary>Includes queued worker blocks.</summary>
    int AcceptedSamples { get; }
    Task StartAsync(CancellationToken cancellationToken);
    void Reset();
    Task WaitForSamplesAsync(int sampleCount, CancellationToken cancellationToken);
    Task WaitForStopAsync(CancellationToken cancellationToken);
    float[][] CompleteCaptureSnapshot();
}

internal sealed class SweepRunAudioOrchestrator
{
    private readonly ISweepCaptureSession capture;
    private readonly IAudioPlaybackDevice playback;
    private bool captureStarted;

    public SweepRunAudioOrchestrator(
        ISweepCaptureSession capture,
        IAudioPlaybackDevice playback)
    {
        this.capture = capture;
        this.playback = playback;
    }

    public async Task<float[][]> CaptureAsync(
        IWaveProvider source,
        int sweepSamples,
        int tailSamples,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sweepSamples);
        ArgumentOutOfRangeException.ThrowIfNegative(tailSamples);

        if (!captureStarted)
        {
            await capture.StartAsync(cancellationToken).ConfigureAwait(false);
            captureStarted = true;
        }
        else
        {
            capture.Reset();
        }

        int recordingStart = capture.AcceptedSamples;
        // A device can die during playback, before the sample waiter exists: observe the stop so the run fails, not hangs.
        using var stopObservation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        Task stopped = ObserveStopAsync(stopObservation.Token);
        Task? playbackTask = null;
        Task? sampleWaitTask = null;
        try
        {
            playbackTask = AudioPlaybackRunner.PlayToEndAsync(
                playback, source, cancellationToken);
            await AwaitUnlessStoppedAsync(playbackTask, stopped).ConfigureAwait(false);

            // Re-read at playback end: the tail starts after every block accepted by then.
            int nominalSweepEnd = checked(recordingStart + sweepSamples);
            int recordingEnd = Math.Max(nominalSweepEnd, capture.AcceptedSamples);
            int requiredSamples = checked(recordingEnd + tailSamples);
            sampleWaitTask = capture.WaitForSamplesAsync(requiredSamples, cancellationToken);
            await AwaitUnlessStoppedAsync(sampleWaitTask, stopped).ConfigureAwait(false);

            return capture.CompleteCaptureSnapshot();
        }
        finally
        {
            stopObservation.Cancel();
            await ObserveQuietlyAsync(stopped).ConfigureAwait(false);
            Forget(playbackTask);
            Forget(sampleWaitTask);
        }
    }

    private async Task ObserveStopAsync(CancellationToken token)
    {
        try
        {
            await capture.WaitForStopAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    private static async Task AwaitUnlessStoppedAsync(Task work, Task stopped)
    {
        Task first = await Task.WhenAny(work, stopped).ConfigureAwait(false);
        if (ReferenceEquals(first, stopped))
        {
            await stopped.ConfigureAwait(false);
        }
        await work.ConfigureAwait(false);
    }

    private static async Task ObserveQuietlyAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private static void Forget(Task? task)
    {
        if (task == null)
        {
            return;
        }
        if (task.IsCompleted)
        {
            _ = task.Exception;
            return;
        }
        task.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
