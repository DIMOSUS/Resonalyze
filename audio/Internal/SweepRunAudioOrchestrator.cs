using NAudio.Wave;

namespace Resonalyze.Audio;

internal interface ISweepCaptureSession
{
    /// <summary>Frames accepted into the current epoch, including queued worker blocks.</summary>
    int AcceptedSamples { get; }
    Task StartAsync(CancellationToken cancellationToken);
    void Reset();
    Task WaitForSamplesAsync(int sampleCount, CancellationToken cancellationToken);
    /// <summary>Faults when the capture device stops; otherwise never completes.</summary>
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
        // Observe a terminal capture failure that can happen while playback is
        // still running — before the sample waiter below even exists — so a dead
        // device fails the run instead of hanging it until an Abort. The
        // observation is cancelled when the run finishes normally.
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

            // AcceptedSamples includes queued worker blocks. Reading it again at
            // playback completion covers any packet accepted in the narrow window
            // between the initial baseline and playback start. The tail therefore
            // begins after both the nominal sweep end and every block accepted by
            // the time playback actually ended.
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
            // If the run bailed out on a stop, the abandoned work tasks are
            // observed here without blocking, so neither surfaces as an
            // unobserved task exception.
            Forget(playbackTask);
            Forget(sampleWaitTask);
        }
    }

    // Faults with the capture-stop exception; on run completion it is cancelled
    // and completes quietly, so only a real device stop propagates.
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
            // Rethrows the device-stop failure (or, if the run was already
            // cancelling, completes and lets the work task surface the cancel).
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
            // The stop observation faulting/cancelling is expected during teardown.
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
