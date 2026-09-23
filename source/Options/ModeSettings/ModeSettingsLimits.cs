namespace Resonalyze.Options;

/// <summary>The ranges of the mode settings panels' fields: the panels apply them to their fields, the sessions hold
/// values inside them.</summary>
internal static class ModeSettingsLimits
{
    public static readonly NumericFieldRange FrequencyResponseWindow = new(4, 32768, 0);

    public static readonly NumericFieldRange TukeyFade = new(0, 16384, 0);

    public static readonly NumericFieldRange GateOffsetMs = new(0, 2000, 3);

    public static readonly NumericFieldRange GateLengthMs = new(0, 680, 2);

    public static readonly NumericFieldRange DetrendMs = new(-2000, 2000, 3);

    public static readonly NumericFieldRange WaterfallWindow = new(32, 32768, 0);

    public static readonly NumericFieldRange Slices = new(4, 512, 0);

    public static readonly NumericFieldRange Step = new(-512, 512, 0);

    public static readonly NumericFieldRange SampleOffset = new(-32768, 32768, 0);

    public static readonly NumericFieldRange DbRange = new(-140, -10, 0);

    public static readonly NumericFieldRange Periods = new(1, 60, 0);

    public static readonly NumericFieldRange SampleRate = new(1, 192000, 0);

    public static readonly NumericFieldRange CaptureTimeMs = new(-999999999, 999999999, 2);

    public static readonly NumericFieldRange ImpulseLength = new(4, 32768, 0);

    public static readonly NumericFieldRange EnvelopeSmoothingMs = new(0, 100, 2);
}
