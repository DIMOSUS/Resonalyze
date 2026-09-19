using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// Where the EQ Wizard's plot puts a band's handle, and what dragging or wheeling it asks of the band. The bank clamps
/// and rounds every result as a strip would (<see cref="EqWizardBank.Edit"/>).
/// </summary>
internal static class EqBandHandles
{
    // A sixth of an octave of bandwidth per wheel notch, and at least one step of the Q field.
    private static readonly double QRatioPerNotch = Math.Pow(2, 1.0 / 6);
    private const double QFieldStep = 0.1;

    /// <summary>On the band's own curve at its frequency: a bell's gain, half a shelf's (its transition middle), 0 dB for an all-pass.</summary>
    public static double LevelDb(PeqBand band) =>
        band.Type.IsAllPass() ? 0
        : band.Type.IsShelving() ? band.GainDb / 2
        : band.GainDb;

    /// <summary>The band whose handle sits at this point; an all-pass has no gain and follows frequency only.</summary>
    public static PeqBand MoveTo(PeqBand band, double frequencyHz, double levelDb) => band with
    {
        FrequencyHz = frequencyHz,
        GainDb = band.Type.IsAllPass() ? band.GainDb
            : band.Type.IsShelving() ? 2 * levelDb
            : levelDb
    };

    public static bool HasQ(PeqBandType type) => type != PeqBandType.AllPassFirstOrder;

    /// <summary>Positive notches narrow the band.</summary>
    public static PeqBand StepQ(PeqBand band, int notches)
    {
        if (notches == 0 || !HasQ(band.Type))
        {
            return band;
        }

        double q = band.Q * Math.Pow(QRatioPerNotch, notches);
        if (Math.Abs(q - band.Q) < QFieldStep * Math.Abs(notches))
        {
            q = band.Q + (QFieldStep * notches);
        }

        return band with { Q = q };
    }

    /// <summary>What the strip shows, in one line: number, type, frequency, then gain and Q where the type has them.</summary>
    public static string Readout(int number, PeqBand band)
    {
        string text = $"{number} {PeqBandToken.Of(band.Type)}   {band.FrequencyHz:0} Hz";
        if (!band.Type.IsAllPass())
        {
            text += $"   {band.GainDb:+0.0;-0.0;0.0} dB";
        }

        return HasQ(band.Type) ? text + $"   Q {band.Q:0.0}" : text;
    }
}
