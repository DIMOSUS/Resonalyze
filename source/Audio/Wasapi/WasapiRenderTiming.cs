namespace Resonalyze;

internal static class WasapiRenderTiming
{
    private const double MissedDeadlineFactor = 1.5;

    public static bool IsUnderrun(
        int currentPaddingFrames,
        bool sourceEnded,
        TimeSpan elapsedSinceFill,
        TimeSpan bufferDuration)
    {
        return !sourceEnded &&
            currentPaddingFrames == 0 &&
            bufferDuration > TimeSpan.Zero &&
            elapsedSinceFill > bufferDuration * MissedDeadlineFactor;
    }
}
