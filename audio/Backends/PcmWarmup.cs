namespace Resonalyze.Audio;

/// <summary>
/// Shared warm-up for the PCM backends: briefly open a duplex session and play a
/// fraction of a second of silence so the first real measurement does not pay
/// cold-start device latency. Best-effort and bounded by a short timeout.
/// </summary>
internal static class PcmWarmup
{
    public static async Task WarmUpAsync(
        IAudioBackend backend,
        AudioSessionRequest request,
        CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(TimeSpan.FromSeconds(5));
        await using IAudioDuplexSession session =
            await backend.OpenDuplexAsync(request, linked.Token).ConfigureAwait(false);
        int samples = Math.Max(1, request.SampleRate / 5);
        var silence = new AudioPlaybackSignal(
            new float[samples],
            request.SampleRate,
            request.BitsPerSample,
            request.PlaybackChannel);
        await session.PlayAndCaptureAsync(silence, 0, linked.Token).ConfigureAwait(false);
    }
}
