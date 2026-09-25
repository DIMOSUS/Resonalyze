namespace Resonalyze.Audio;

/// <summary>A driver that loses its device may just stop calling back, with no stop or reset message: no callback for
/// <see cref="StallTimeout"/> reads as that. See docs/tech/audio-layer.md#driver-reset-and-silent-drivers.</summary>
internal sealed class AsioCallbackWatchdog
{
    public static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan CheckInterval = TimeSpan.FromMilliseconds(500);

    private long lastCallbackCount;
    private TimeSpan lastProgress;

    public AsioCallbackWatchdog(long callbackCount, TimeSpan now)
    {
        lastCallbackCount = callbackCount;
        lastProgress = now;
    }

    /// <param name="now">Any monotonic clock, as long as every call uses the same one.</param>
    public bool IsStalled(long callbackCount, TimeSpan now)
    {
        if (callbackCount != lastCallbackCount)
        {
            lastCallbackCount = callbackCount;
            lastProgress = now;
            return false;
        }

        return now - lastProgress >= StallTimeout;
    }
}
