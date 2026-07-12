using NAudio.Wave;

namespace Resonalyze;

internal static class AudioPlaybackRunner
{
    public static async Task PlayToEndAsync(
        IAudioPlaybackDevice playback,
        IWaveProvider source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(playback);
        ArgumentNullException.ThrowIfNull(source);

        try
        {
            await playback.StartAsync(source, cancellationToken).ConfigureAwait(false);
            await playback.WaitForPlaybackEndAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await playback.StopAsync().ConfigureAwait(false);
            throw;
        }
    }
}
