namespace Resonalyze;

/// <summary>
/// Helpers for combining a microphone capture and a loopback capture that come from two
/// independent Wave input devices. The two devices run on separate hardware clocks, so the
/// streams cannot be sample-accurately synchronised; this is a deliberate best-effort
/// alignment (both recorders are started before playback, so the streams are aligned at
/// their first recorded sample and trimmed to the shared length). Timing-sensitive results
/// (phase, group delay, time alignment) are therefore degraded compared to capturing both
/// signals as channels of a single device or via ASIO.
/// </summary>
internal static class DualDeviceCapture
{
    /// <summary>
    /// Builds a two-channel <c>[microphone, loopback]</c> array from the snapshots of two
    /// separate input devices, selecting one channel from each and trimming both to the
    /// shorter length so they share a common sample grid.
    /// </summary>
    public static float[][] MergeMicrophoneAndLoopback(
        float[][] microphoneChannels,
        int microphoneChannel,
        float[][] loopbackChannels,
        int loopbackChannel)
    {
        ArgumentNullException.ThrowIfNull(microphoneChannels);
        ArgumentNullException.ThrowIfNull(loopbackChannels);

        float[] microphone = ExtractChannel(microphoneChannels, microphoneChannel);
        float[] loopback = ExtractChannel(loopbackChannels, loopbackChannel);
        int count = Math.Min(microphone.Length, loopback.Length);

        return [Trim(microphone, count), Trim(loopback, count)];
    }

    private static float[] ExtractChannel(float[][] channels, int channel)
    {
        return (uint)channel < (uint)channels.Length
            ? channels[channel]
            : Array.Empty<float>();
    }

    private static float[] Trim(float[] samples, int count)
    {
        if (samples.Length == count)
        {
            return samples;
        }

        var trimmed = new float[count];
        Array.Copy(samples, trimmed, count);
        return trimmed;
    }
}
