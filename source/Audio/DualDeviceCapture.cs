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
    /// <param name="microphoneStart">
    /// Samples already captured on the microphone device when playback began; dropped from the
    /// front so both streams start near playback time (best-effort start alignment).
    /// </param>
    /// <param name="loopbackStart">As <paramref name="microphoneStart"/>, for the loopback device.</param>
    public static float[][] MergeMicrophoneAndLoopback(
        float[][] microphoneChannels,
        int microphoneChannel,
        float[][] loopbackChannels,
        int loopbackChannel,
        int microphoneStart = 0,
        int loopbackStart = 0)
    {
        ArgumentNullException.ThrowIfNull(microphoneChannels);
        ArgumentNullException.ThrowIfNull(loopbackChannels);

        float[] microphone = ExtractChannel(microphoneChannels, microphoneChannel, microphoneStart);
        float[] loopback = ExtractChannel(loopbackChannels, loopbackChannel, loopbackStart);
        int count = Math.Min(microphone.Length, loopback.Length);

        return [Trim(microphone, count), Trim(loopback, count)];
    }

    private static float[] ExtractChannel(float[][] channels, int channel, int start)
    {
        if ((uint)channel >= (uint)channels.Length)
        {
            return Array.Empty<float>();
        }

        float[] samples = channels[channel];
        int offset = Math.Clamp(start, 0, samples.Length);
        int count = samples.Length - offset;
        if (offset == 0)
        {
            return samples;
        }

        var trimmed = new float[count];
        Array.Copy(samples, offset, trimmed, 0, count);
        return trimmed;
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
