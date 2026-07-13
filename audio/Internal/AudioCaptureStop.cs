namespace Resonalyze.Audio;

/// <summary>
/// The stop-and-wait machinery shared by the capture classes: request a device
/// stop (a stop on an already-stopped device throws InvalidOperationException,
/// which counts as stopped) and await the stopped signal with the shared
/// timeout. Detaching and disposing the device stays with the caller.
/// </summary>
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
