namespace Resonalyze.Audio;

/// <summary>Opens a duplex session and plays brief silence to absorb cold-start latency; best-effort, short timeout.</summary>
internal static class PcmWarmup
{
    public static async Task WarmUpAsync(
        IAudioBackend backend,
        AudioSessionRequest request,
        CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(TimeSpan.FromSeconds(5));
        int samples = Math.Max(1, request.SampleRate / 5);
        var silence = new AudioPlaybackSignal(
            new float[samples],
            request.SampleRate,
            request.BitsPerSample,
            request.PlaybackChannel);
        await using IAudioDuplexSession session =
            await backend.OpenDuplexAsync(request, silence, linked.Token).ConfigureAwait(false);
        await session.PlayAndCaptureAsync(0, linked.Token).ConfigureAwait(false);
    }
}
