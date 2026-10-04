namespace Resonalyze;

/// <summary>What a slot's level field does to the values it draws: y × Gain + Shift.</summary>
internal readonly record struct OverlayLevel(double Gain, double Shift)
{
    public static OverlayLevel Unchanged { get; } = new(1.0, 0.0);

    public double Apply(double y) => y * Gain + Shift;
}

/// <summary>The Impulse Response view's level field: the slot's amplitude in percent, where every other view offsets it.</summary>
internal static class OverlayScale
{
    public const decimal DefaultPercent = 100m;

    public const decimal Increment = 5m;

    public static NumericFieldRange Range { get; } = new(1m, 1_000m, 0);

    public static bool Applies(Mode mode) => mode == Mode.ImpulseResponse;
}
