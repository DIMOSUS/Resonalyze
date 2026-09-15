namespace Resonalyze.Audio;

/// <summary>Stopping an already stopped device throws InvalidOperationException, which counts as stopped.</summary>
internal static class AudioCaptureStop
{
    public static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(3);

    public static async Task StopAndWaitAsync(
        Action requestStop,
        TaskCompletionSource<bool>? stoppedSignal,
        Task stoppedTask,
        string deviceDescription)
    {
        try
        {
            requestStop();
        }
        catch (InvalidOperationException)
        {
            stoppedSignal?.TrySetResult(true);
        }

        try
        {
            await stoppedTask.WaitAsync(StopTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            throw new TimeoutException(
                $"{deviceDescription} did not stop within {StopTimeout.TotalSeconds:0} seconds.",
                exception);
        }
    }
}
