namespace Resonalyze;

/// <summary>The channel gain with the PEQ preamp folded in, the one level to dial on a DSP whose equalizer has no
/// preamp (the tuning sheets add the same two); blank without a preamp.</summary>
internal static class VirtualCrossoverChannelTotalGain
{
    public static string Text(double gainDb, double peqPreampDb) =>
        peqPreampDb == 0 ? string.Empty : $"All {gainDb + peqPreampDb:+0.0;-0.0;0.0}";
}
