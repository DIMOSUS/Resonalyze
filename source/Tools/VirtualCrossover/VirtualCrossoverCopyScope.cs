using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Unticked parts are left as the target side had them.</summary>
internal readonly record struct VirtualCrossoverCopyScope(
    bool Gain,
    bool Delay,
    bool InvertPolarity,
    bool Crossover,
    bool AllPass,
    bool Phase,
    bool Peq,
    bool Fir = false)
{
    public bool IsEmpty =>
        !Gain && !Delay && !InvertPolarity && !Crossover && !AllPass && !Phase && !Peq && !Fir;

    // Defaults tick crossover and PEQ (driver shape); gain, delay, polarity and all-pass are opt-in because
    // they align against one side's geometry. See docs/tech/virtual-dsp-panel.md#copying-between-sides.
    public void Copy(VirtualCrossoverChannelSettings from, VirtualCrossoverChannelSettings to)
    {
        if (Gain)
        {
            to.GainDb = from.GainDb;
        }

        if (Delay)
        {
            to.DelayMs = from.DelayMs;
        }

        if (InvertPolarity)
        {
            to.InvertPolarity = from.InvertPolarity;
        }

        if (Crossover)
        {
            to.CrossoverKind = from.CrossoverKind;
            to.HighPassEdge = from.HighPassEdge;
            to.LowPassEdge = from.LowPassEdge;
            // The acoustic wish belongs to the edge it is stated for: left behind, it would aim the target of a
            // side that now has another filter.
            to.AcousticHighPass = from.AcousticHighPass;
            to.AcousticLowPass = from.AcousticLowPass;
        }

        // A timing decision like the delay, so its own tick; copied as the number, the reference follows the target's crossover.
        if (Phase)
        {
            to.PhaseRotationDegrees = from.PhaseRotationDegrees;
        }

        // Immutable kernel, shared by reference.
        if (Fir)
        {
            to.Fir = from.Fir;
            to.FirSourceName = from.FirSourceName;
            to.FirDesign = from.FirDesign;
        }

        // All-pass filters live in the PEQ bank; Peq and AllPass split that one list by band type.
        if (Peq || AllPass)
        {
            List<PeqBand> tonal = (Peq ? from : to)
                .PeqBands.Where(band => !band.Type.IsAllPass()).ToList();
            List<PeqBand> allPass = (AllPass ? from : to)
                .PeqBands.Where(band => band.Type.IsAllPass()).ToList();
            // Over the slot budget the COPIED kind gives way (an unticked scope promised the target's bands stay);
            // with both copied the all-pass stays.
            int overflow =
                tonal.Count + allPass.Count - EqualizationCurve.MaxBandCount;
            if (overflow > 0)
            {
                if (Peq)
                {
                    tonal = tonal.Take(Math.Max(0, tonal.Count - overflow)).ToList();
                }
                else
                {
                    allPass = allPass.Take(Math.Max(0, allPass.Count - overflow)).ToList();
                }
            }

            to.PeqBands = tonal.Concat(allPass).ToList();
        }

        if (Peq)
        {
            to.PeqPreampDb = from.PeqPreampDb;
            to.PeqSourceName = from.PeqSourceName;
        }
    }
}
