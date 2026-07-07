namespace Resonalyze;

/// <summary>
/// The shared arithmetic of which input channels a capture needs: how many Wave
/// channels to record and where the ASIO capture window starts and how wide it
/// is, given the microphone channel and an optional loopback channel.
/// </summary>
internal static class CaptureChannelLayout
{
    public static int RequiredWaveInputChannelCount(
        int microphoneOffset,
        int? loopbackOffset)
    {
        int last = microphoneOffset;
        if (loopbackOffset.HasValue)
        {
            last = Math.Max(last, loopbackOffset.Value);
        }

        return last + 1;
    }

    public static int AsioFirstInputOffset(int microphoneOffset, int? loopbackOffset) =>
        loopbackOffset.HasValue
            ? Math.Min(microphoneOffset, loopbackOffset.Value)
            : microphoneOffset;

    public static int AsioInputChannelCount(int microphoneOffset, int? loopbackOffset)
    {
        int first = AsioFirstInputOffset(microphoneOffset, loopbackOffset);
        int last = loopbackOffset.HasValue
            ? Math.Max(microphoneOffset, loopbackOffset.Value)
            : microphoneOffset;
        return last - first + 1;
    }
}
