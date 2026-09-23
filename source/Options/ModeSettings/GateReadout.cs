using Resonalyze.Dsp;

namespace Resonalyze.Options;

/// <summary>What a millisecond gate's fields say about it: the lowest frequency it resolves, and their tooltips.</summary>
internal static class GateReadout
{
    public const string Offset =
        "Gate position: time from the IR start to the end of the left Tukey shoulder. Auto keeps it snapped to the detected IR start.";

    public const string Auto =
        "Keep the gate offset snapped to the detected IR start (band-limited first-arrival front), following every new measurement. Release to set the offset manually.";

    public const string Plateau = "Flat (weight 1) part of the gate after the peak, in milliseconds.";

    public const string Left = "Tukey fade-in before the peak, in milliseconds. Keep short.";

    public const string Right =
        "Tukey fade-out gate after the plateau, in milliseconds. End it before the first reflection.";

    public const string MinFrequency =
        "Lowest frequency the current gate can resolve (≈ 1 / gate length). Below it the curve is not reliable.";

    public static string ReliableFrom(double leftMs, double plateauMs, double rightMs)
    {
        double hz = FrequencyResponseOptions.GateMinReliableFrequencyHz(leftMs, plateauMs, rightMs);
        return hz > 0
            ? $"Reliable from ≈ {hz:0}+ Hz"
            : "Reliable from ≈ — Hz";
    }
}
