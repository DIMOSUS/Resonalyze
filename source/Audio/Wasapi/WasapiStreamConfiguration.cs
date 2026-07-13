using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace Resonalyze;

internal static class WasapiStreamConfiguration
{
    public const int BufferSizeNotAlignedHResult = unchecked((int)0x88890019);
    private const long ReferenceTimesPerMillisecond = 10_000;
    private const long ReferenceTimesPerSecond = 10_000_000;

    public static (long BufferDuration, long Periodicity) GetDurations(
        AudioClientShareMode shareMode,
        int bufferMilliseconds)
    {
        if (shareMode == AudioClientShareMode.Shared)
        {
            return (0, 0);
        }

        long duration = checked(bufferMilliseconds * ReferenceTimesPerMillisecond);
        return (duration, duration);
    }

    public static long GetAlignedExclusiveDuration(int bufferFrames, int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bufferFrames);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        return (long)Math.Ceiling(
            (double)ReferenceTimesPerSecond * bufferFrames / sampleRate);
    }

    public static int GetRenderFrames(
        AudioClientShareMode shareMode,
        int bufferFrames,
        int currentPaddingFrames)
    {
        return shareMode == AudioClientShareMode.Exclusive
            ? bufferFrames
            : Math.Max(0, bufferFrames - currentPaddingFrames);
    }

    public static bool IsBufferSizeNotAligned(COMException exception) =>
        exception.HResult == BufferSizeNotAlignedHResult;
}
