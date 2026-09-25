using System.Numerics;

namespace Resonalyze.Dsp;

/// <summary>The objective's weights and the price of a slot. See docs/tech/eq-auto-tuner.md#the-objective.</summary>
/// <param name="SlotWorth">dB²·octave a band must remove from the objective to earn its slot.</param>
/// <param name="AboveWeight">Where a point may not be lifted, how much dearer a dB left above the target is than a dB dug below.</param>
/// <param name="OverCutFreeDb">Dug depth charged at the plain rate; beyond it <paramref name="OverCutWeight"/> is added.</param>
/// <param name="DigDepthScaleDb">A point this far under the target already is charged half for being dug deeper.</param>
internal sealed record EqFitTuning(
    double SlotWorth,
    double AboveWeight,
    double OverCutFreeDb,
    double OverCutWeight,
    double DigDepthScaleDb)
{
    public static EqFitTuning Default { get; } = new(
        SlotWorth: 0.03,
        AboveWeight: 4,
        OverCutFreeDb: 1,
        OverCutWeight: 5,
        DigDepthScaleDb: 3);
}

/// <summary>One fit on the tuner's grid. <see cref="Desired"/> is the correction each point asks for (target − source − preamp).</summary>
internal sealed class EqFitProblem
{
    // The ceiling grid: dense enough that the narrowest Q a strip allows (20, about 0.07 octave wide) still gets tens of
    // points, and wide enough to hold every skirt the biquads reach.
    // See docs/tech/eq-auto-tuner.md#ceilings-on-the-finished-bank.
    private const int CeilingGridSize = 4_096;
    private const double CeilingNyquistFraction = 0.49;

    private EqFitProblem? ceilings;

    public EqFitProblem(
        EqAutoTuner.Options options,
        IReadOnlyList<double> hz,
        Complex[] z1,
        Complex[] z2,
        bool[] valid,
        bool[] boostAllowed,
        double[] desired)
    {
        Options = options;
        Hz = hz;
        Z1 = z1;
        Z2 = z2;
        Valid = valid;
        BoostAllowed = boostAllowed;
        Desired = desired;
        Count = hz.Count;
        Weight = new double[Count];
        Chases = new bool[Count];
        for (int i = 0; i < Count; i++)
        {
            // Octaves each point stands for, so the objective is an integral over log frequency.
            double lo = Math.Log2(hz[Math.Max(0, i - 1)]);
            double hi = Math.Log2(hz[Math.Min(Count - 1, i + 1)]);
            Weight[i] = valid[i] ? (hi - lo) / 2 : 0;
            Chases[i] = valid[i] && boostAllowed[i] && options.Boosts == EqAutoTuneBoosts.Allowed;
        }

        LogMinHz = Math.Log(hz[0]);
        LogMaxHz = Math.Log(hz[^1]);
    }

    private EqFitProblem BuildCeilings()
    {
        double lowestHz = Math.Max(1, Math.Min(Hz[0], 20) / 2);
        double highestHz = Math.Max(lowestHz * 2, Options.SampleRateHz * CeilingNyquistFraction);
        IReadOnlyList<double> hz = EqualizationCurve.LogFrequencyGrid(lowestHz, highestHz, CeilingGridSize);
        var z1 = new Complex[hz.Count];
        var z2 = new Complex[hz.Count];
        var valid = new bool[hz.Count];
        var allowed = new bool[hz.Count];
        for (int i = 0; i < hz.Count; i++)
        {
            z1[i] = Complex.Exp(new Complex(0, -Math.Tau * hz[i] / Options.SampleRateHz));
            z2[i] = z1[i] * z1[i];
            // Which points a ceiling answers for. A bank that may not lift promises clip safety, so it answers
            // everywhere; the stacking limit on boosts answers where the source was measured, or a boost would be
            // trimmed to nothing for what its skirt does in an unmeasured octave nobody asked it to correct.
            int near = Nearest(hz[i]);
            valid[i] = Refills || (near >= 0 && Valid[near]);
            allowed[i] = near < 0 || BoostAllowed[near];
        }

        return new EqFitProblem(Options, hz, z1, z2, valid, allowed, new double[hz.Count]);
    }

    // The nearest fitted bin, or −1 outside the fitted band, where nothing was measured.
    private int Nearest(double hz)
    {
        if (hz < Hz[0] || hz > Hz[^1])
        {
            return -1;
        }

        int lo = 0;
        int hi = Count - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            if (Hz[mid] <= hz)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }

        return Math.Log(hz / Hz[lo]) <= Math.Log(Hz[hi] / hz) ? lo : hi;
    }

    public EqAutoTuner.Options Options { get; }
    public EqFitTuning Tuning => Options.Tuning;
    public IReadOnlyList<double> Hz { get; }
    public Complex[] Z1 { get; }
    public Complex[] Z2 { get; }
    public bool[] Valid { get; }
    public bool[] BoostAllowed { get; }
    public double[] Desired { get; }
    public double[] Weight { get; }

    /// <summary>Points whose deficit the fit fills; elsewhere only what the bank itself digs is charged.</summary>
    public bool[] Chases { get; }

    public int Count { get; }
    public double LogMinHz { get; }
    public double LogMaxHz { get; }
    public bool Refills => Options.Boosts == EqAutoTuneBoosts.RefillOwnCuts;

    /// <summary>
    /// The same options over a grid the ceilings are checked on: every point valid (a filter's skirt runs through
    /// unmeasured octaves too), dense, and spanning the whole band rather than the fitted window.
    /// </summary>
    public EqFitProblem Ceilings() => ceilings ??= BuildCeilings();

    public double[] Response(PeqBand band)
    {
        var result = new double[Count];
        if (band.IsTransparent)
        {
            return result;
        }

        BiquadCoefficients c = PeqBiquad.Compute(band, Options.SampleRateHz);
        for (int i = 0; i < Count; i++)
        {
            if (Valid[i])
            {
                Complex h = BiquadResponse.Evaluate(c, Z1[i], Z2[i]);
                result[i] = 10.0 * Math.Log10(Math.Max(h.Real * h.Real + h.Imaginary * h.Imaginary, 1e-300));
            }
        }

        return result;
    }
}

/// <summary>
/// Places bands where the error is worst, refines frequency, gain and Q of the whole bank together (bounded
/// Levenberg–Marquardt), and keeps a band only while it pays for its slot. See docs/tech/eq-auto-tuner.md#fit.
/// </summary>
internal sealed class EqBandFitter
{
    /// <summary>A bank summing this little over 0 dB is treated as not lifting.</summary>
    public const double SumToleranceDb = 1e-6;

    // Soft constraints: a dB over the limit costs as much as 20 dB of error.
    private const double ConstraintWeight = 400;

    // Weak next to the error: a skirt brushing an opposite band costs little, a pair cancelling over octaves a lot.
    private const double OverlapWeight = 0.1;

    // A refill IS overlap, so a tenth of that: enough to stop boosts reshaping a cut's own skirt and centre.
    private const double RefillOverlapWeight = 0.01;

    // The widest bell the fit places, whatever the strips accept: wider is a tilt, which is a shelf's or the preamp's job.
    private const double BellQFloor = 0.5;
    private const double BellDriftOctaves = 1.0 / 3.0;
    private const double ShelfDriftOctaves = 0.5;

    // Above 1/sqrt(2) an RBJ shelf overshoots its own gain, so a cutting shelf would boost. See docs/tech/eq-auto-tuner.md#shelves.
    private const double ShelfKneeMin = 0.3;
    private const double ShelfKneeMax = 0.7;
    private const double ShelfMinGainDb = 0.5;
    private const double ShelfSettledMarginOctaves = 2.0;
    private const double ShelfPlateauSpanOctaves = 1.0;
    private const double ShelfPlateauMarginOctaves = 0.5;
    private const double ShelfPlateauUsableFraction = 0.75;
    // Ceiling, not rounding: a seed drifts at most ShelfDriftOctaves, so a wider gap between seeds is a corner no
    // shelf can reach.
    private const int ShelfSeedsPerOctave = 1;
    private const int ShelfBoundStepsPerOctave = 8;

    // Refused seeds block their lobe plus this much around the peak, so the pass cannot retry the same error.
    private const double SeedBlockOctaves = 0.05;
    private const double NullWallBlockOctaves = 0.1;

    private const int SeedIterations = 30;
    private const int GlobalIterations = 12;
    private const int FinalIterations = 60;
    private const int MaxPasses = 4;

    // A removal whose bare cost exceeds this many slots is not refitted to find out.
    private const double PruneScreenSlots = 6;

    private readonly EqFitProblem p;
    private readonly List<Band> bands;
    private readonly double[] s;
    private readonly double[] up;
    private readonly double[] dn;
    private int nextId;

    private EqBandFitter(EqFitProblem problem, IEnumerable<Band> start)
    {
        p = problem;
        bands = start.Select(band => band.Clone()).ToList();
        nextId = bands.Count == 0 ? 0 : bands.Max(band => band.Id) + 1;
        s = new double[p.Count];
        up = new double[p.Count];
        dn = new double[p.Count];
    }

    private sealed class Band
    {
        public int Id;
        public PeqBandType Type;
        public double U;
        public double G;
        public double V;
        public double ULo;
        public double UHi;
        public double GLo;
        public double GHi;
        public double VLo;
        public double VHi;
        public double[] Response = Array.Empty<double>();

        public bool IsBell => Type == PeqBandType.Peaking;

        // Bells are seeded with a sign they keep: 1 boosts, 2 cuts, 0 a shelf.
        public int Role => !IsBell ? 0 : GLo >= 0 ? 1 : 2;

        public PeqBand ToPeq() => new(Math.Exp(U), Math.Exp(V), G, Type);

        public Band Clone()
        {
            var copy = (Band)MemberwiseClone();
            copy.Response = (double[])Response.Clone();
            return copy;
        }
    }

    public static List<PeqBand> Fit(EqFitProblem problem)
    {
        int budget = Math.Max(0, problem.Options.MaxBands);
        if (budget == 0)
        {
            return new List<PeqBand>();
        }

        List<Band> start = problem.Options.AllowShelves
            ? ChooseShelves(problem, budget)
            : new List<Band>();
        var fitter = new EqBandFitter(problem, start);
        fitter.Run(budget, MaxPasses, prune: true);
        return fitter.Quantize();
    }

    // Joint refinement moves bands, so an error refused a band in one pass may earn one in the next; and a pruned band
    // leaves both a free slot and a changed residual, which is another reason to look again.
    private void Run(int budget, int passes, bool prune)
    {
        for (int pass = 0; pass < passes; pass++)
        {
            int added = InsertionPass(budget);
            Refine(All(), FinalIterations);
            int removed = prune ? Prune() : 0;
            if (!AnotherPass(added, removed, bands.Count, budget))
            {
                return;
            }
        }
    }

    /// <summary>
    /// Whether the fit looks again: the last pass has to have changed the bank — inserting, or dropping a band, which
    /// leaves a free slot and a changed residual — and there has to be room left.
    /// </summary>
    internal static bool AnotherPass(int added, int removed, int bands, int budget) =>
        (added > 0 || removed > 0) && bands < budget;

    private int[] All() => Enumerable.Range(0, bands.Count).ToArray();

    // The objective plus the price of every slot spent: what the shelf stage ranks finished fits by.
    private double Cost()
    {
        Sum();
        return Objective() + p.Tuning.SlotWorth * bands.Count;
    }

    private int InsertionPass(int budget)
    {
        var blocked = new bool[p.Count];
        Sum();
        double current = Objective();
        int added = 0;
        for (int attempt = 0; bands.Count < budget && attempt < 4 * budget + 16; attempt++)
        {
            int peak = WorstPoint(blocked, out double fixable);
            if (peak < 0 || fixable < p.Options.StopResidualDb)
            {
                break;
            }

            (int left, int right) = Lobe(peak, fixable);
            Band? band = SeedBell(peak, left, right, out bool nullWall);
            if (band == null)
            {
                // Beside a null only the wall is refused: further out on the same dip a narrower boost may fit.
                if (nullWall)
                {
                    Block(blocked, peak, peak, peak, NullWallBlockOctaves);
                }
                else
                {
                    Block(blocked, left, right, peak, SeedBlockOctaves);
                }

                continue;
            }

            bands.Add(band);
            Refine(new[] { bands.Count - 1 }, SeedIterations);
            Sum();
            if (current - Objective() < p.Tuning.SlotWorth)
            {
                bands.RemoveAt(bands.Count - 1);
                Sum();
                Block(blocked, left, right, peak, SeedBlockOctaves);
                continue;
            }

            added++;
            Refine(All(), GlobalIterations);
            Sum();
            current = Objective();
        }

        return added;
    }

    // Removes the band that costs least to lose while the rest, refitted, make up for all but a slot's worth of it.
    /// <returns>How many bands were dropped.</returns>
    private int Prune()
    {
        var kept = new HashSet<int>();
        int removed = 0;
        while (bands.Count > 0)
        {
            Sum();
            double current = Objective();
            Band? weakest = null;
            double weakestCost = double.MaxValue;
            foreach (Band band in bands)
            {
                if (kept.Contains(band.Id))
                {
                    continue;
                }

                double cost = ObjectiveWithout(band) - current;
                if (cost < weakestCost)
                {
                    weakestCost = cost;
                    weakest = band;
                }
            }

            if (weakest == null || weakestCost > PruneScreenSlots * p.Tuning.SlotWorth)
            {
                return removed;
            }

            List<Band> snapshot = bands.Select(band => band.Clone()).ToList();
            bands.Remove(weakest);
            Refine(All(), GlobalIterations);
            Sum();
            if (Objective() - current < p.Tuning.SlotWorth)
            {
                removed++;
                continue;
            }

            bands.Clear();
            bands.AddRange(snapshot);
            kept.Add(weakest.Id);
        }

        return removed;
    }

    private double ObjectiveWithout(Band band)
    {
        for (int i = 0; i < p.Count; i++)
        {
            s[i] -= band.Response[i];
            if (band.Role == 1)
            {
                up[i] -= band.Response[i];
            }
            else if (band.Role == 2)
            {
                dn[i] -= band.Response[i];
            }
        }

        double result = Objective();
        Sum();
        return result;
    }

    // Strip precision: whole Hz, a tenth of a dB, a tenth of Q.
    private List<PeqBand> Quantize()
    {
        var result = new List<PeqBand>();
        foreach (Band band in bands)
        {
            double gain = Math.Round(band.G, 1);
            if (Math.Abs(gain) < (band.IsBell ? 0.05 : ShelfMinGainDb))
            {
                continue;
            }

            // A range narrower than a tenth holds no strip value: keep the fitted Q, at two decimals, inside it.
            double qLow = Math.Ceiling(Math.Exp(band.VLo) * 10 - 1e-9) / 10;
            double qHigh = Math.Floor(Math.Exp(band.VHi) * 10 + 1e-9) / 10;
            double q = qLow <= qHigh
                ? Math.Clamp(Math.Round(Math.Exp(band.V), 1), qLow, qHigh)
                : Math.Clamp(Math.Round(Math.Exp(band.V), 2), Math.Exp(band.VLo), Math.Exp(band.VHi));
            result.Add(new PeqBand(Math.Max(1, Math.Round(Math.Exp(band.U))), q, gain, band.Type));
        }

        // The fitted bins first (their boost mask is exact), then the dense grid, which also holds what the fit never saw.
        EnforceCeilings(p, result);
        EnforceCeilings(p.Ceilings(), result);

        return result;
    }

    // The objective holds the ceilings softly and rounding moves bands, so the boost doing most of any excess is trimmed
    // in the strips' tenth-of-a-dB steps until none is left.
    private static void EnforceCeilings(EqFitProblem p, List<PeqBand> result)
    {
        List<double[]> responses = result.Select(p.Response).ToList();
        while (true)
        {
            int worst = WorstExcess(p, result, responses);
            int culprit = -1;
            double most = 0;
            for (int k = 0; worst >= 0 && k < result.Count; k++)
            {
                if (result[k].GainDb > 0 && result[k].Type == PeqBandType.Peaking && responses[k][worst] > most)
                {
                    most = responses[k][worst];
                    culprit = k;
                }
            }

            if (culprit < 0)
            {
                return;
            }

            PeqBand trimmed = result[culprit] with { GainDb = Math.Round(result[culprit].GainDb - 0.1, 1) };
            if (trimmed.GainDb < 0.05)
            {
                result.RemoveAt(culprit);
                responses.RemoveAt(culprit);
            }
            else
            {
                result[culprit] = trimmed;
                responses[culprit] = p.Response(trimmed);
            }
        }
    }

    // The point furthest over its ceiling, or −1: the bank's sum when refilling, the boosting bells' sum when boosting.
    private static int WorstExcess(EqFitProblem p, List<PeqBand> result, List<double[]> responses)
    {
        EqAutoTuner.Options opt = p.Options;
        if (opt.Boosts == EqAutoTuneBoosts.Off)
        {
            return -1;
        }

        int worst = -1;
        double largest = SumToleranceDb;
        for (int i = 0; i < p.Count; i++)
        {
            if (!p.Valid[i])
            {
                continue;
            }

            double total = 0;
            double boosts = 0;
            for (int k = 0; k < result.Count; k++)
            {
                total += responses[k][i];
                if (result[k].Type == PeqBandType.Peaking && result[k].GainDb > 0)
                {
                    boosts += responses[k][i];
                }
            }

            double excess = opt.Boosts == EqAutoTuneBoosts.RefillOwnCuts
                ? total
                : Math.Max(
                    boosts - opt.BandGainMaxDb,
                    p.BoostAllowed[i] ? double.NegativeInfinity : boosts - opt.ForbiddenRegionMaxBoostDb);
            if (excess > largest)
            {
                largest = excess;
                worst = i;
            }
        }

        return worst;
    }

    private void Block(bool[] blocked, int left, int right, int peak, double octaves)
    {
        double centre = p.Hz[peak];
        for (int i = 0; i < p.Count; i++)
        {
            if ((i >= left && i <= right) || Math.Abs(Math.Log2(p.Hz[i] / centre)) <= octaves)
            {
                blocked[i] = true;
            }
        }
    }

    // In dB: the error a band could remove here. A refill counts only what the bank dug, and only past the depth the
    // objective lets it dig for free: a shallower dent refilled is a cut's skirt reshaped by a boost.
    private double Fixable(int i)
    {
        double r = p.Desired[i] - s[i];
        if (p.Chases[i])
        {
            return Math.Abs(r);
        }

        if (r < 0)
        {
            return -r;
        }

        double dug = p.Refills && s[i] < 0 ? Math.Min(r, -s[i]) : 0;
        return dug >= p.Tuning.OverCutFreeDb ? dug * Math.Sqrt(DigCharge(i)) : 0;
    }

    private int WorstPoint(bool[] blocked, out double worst)
    {
        int index = -1;
        worst = 0;
        for (int i = 0; i < p.Count; i++)
        {
            if (!p.Valid[i] || blocked[i])
            {
                continue;
            }

            double fixable = Fixable(i);
            if (fixable > worst)
            {
                worst = fixable;
                index = i;
            }
        }

        return index;
    }

    // The contiguous run around the peak whose error has the peak's sign and at least half its size.
    private (int Left, int Right) Lobe(int peak, double fixable)
    {
        int sign = Math.Sign(p.Desired[peak] - s[peak]);
        bool Member(int i) =>
            p.Valid[i] && Math.Sign(p.Desired[i] - s[i]) == sign && Fixable(i) >= fixable / 2;

        int left = peak;
        while (left > 0 && Member(left - 1))
        {
            left--;
        }

        int right = peak;
        while (right < p.Count - 1 && Member(right + 1))
        {
            right++;
        }

        return (left, right);
    }

    private Band? SeedBell(int peak, int left, int right, out bool nullWall)
    {
        nullWall = false;
        EqAutoTuner.Options opt = p.Options;
        double r = p.Desired[peak] - s[peak];
        double gain;
        if (r <= 0)
        {
            gain = Math.Max(r, opt.BandGainMinDb);
        }
        else if (p.Chases[peak])
        {
            gain = Math.Min(r, opt.BandGainMaxDb - up[peak]);
            if (Spills(peak, gain, BellQRange().Hi))
            {
                nullWall = true;
                return null;
            }
        }
        else if (p.Refills && s[peak] < 0)
        {
            gain = Math.Min(Math.Min(r, -s[peak]), opt.BandGainMaxDb);
        }
        else
        {
            return null;
        }

        if (Math.Abs(gain) < 0.05)
        {
            return null;
        }

        // A lobe of B octaves between half-height points is roughly the bell of that bandwidth.
        double octaves = Math.Max(
            Math.Log2(p.Hz[Math.Min(p.Count - 1, right + 1)] / p.Hz[Math.Max(0, left - 1)]),
            0.05);
        double width = Math.Pow(2, octaves);
        (double qLo, double qHi) = BellQRange();
        double q = Math.Clamp(Math.Sqrt(width) / (width - 1), qLo, qHi);
        // Seeded no wider than keeps its skirt out of the masked bins, or the refinement starts by paying to shrink it.
        while (gain > 0 && p.Chases[peak] && q < qHi && Spills(peak, gain, q))
        {
            q = Math.Min(qHi, q * 1.25);
        }

        double u = Math.Log(p.Hz[peak]);
        double drift = BellDriftOctaves * Math.Log(2);
        (double uLo, double uHi) = (Math.Max(p.LogMinHz, u - drift), Math.Min(p.LogMaxHz, u + drift));
        if (gain > 0 && p.Chases[peak])
        {
            // The mask clears where a boost is aimed: its centre stays in the trusted run it was seeded in.
            int first = peak;
            while (first > 0 && p.Chases[first - 1])
            {
                first--;
            }

            int last = peak;
            while (last < p.Count - 1 && p.Chases[last + 1])
            {
                last++;
            }

            (uLo, uHi) = (Math.Max(uLo, Math.Log(p.Hz[first])), Math.Min(uHi, Math.Log(p.Hz[last])));
        }

        return Create(
            PeqBandType.Peaking,
            u,
            gain,
            Math.Log(q),
            (uLo, uHi),
            gain > 0 ? (0, opt.BandGainMaxDb) : (opt.BandGainMinDb, 0),
            (Math.Log(qLo), Math.Log(qHi)));
    }

    // Whether this boost alone pours past the limit into a masked bin; at the narrowest Q that marks a null's wall, not a
    // dip. Sharing the limit with other boosts is the refinement's job.
    private bool Spills(int peak, double gain, double q)
    {
        EqAutoTuner.Options opt = p.Options;
        if (!double.IsFinite(opt.ForbiddenRegionMaxBoostDb))
        {
            return false;
        }

        double[] response = p.Response(new PeqBand(p.Hz[peak], q, gain));
        for (int i = 0; i < p.Count; i++)
        {
            if (p.Valid[i] && !p.BoostAllowed[i] && response[i] > opt.ForbiddenRegionMaxBoostDb)
            {
                return true;
            }
        }

        return false;
    }

    private (double Lo, double Hi) BellQRange()
    {
        double lo = Math.Min(Math.Max(p.Options.QMin, BellQFloor), p.Options.QMax);
        return (lo, Math.Max(lo, p.Options.QMax));
    }

    private Band Create(
        PeqBandType type,
        double u,
        double g,
        double v,
        (double Lo, double Hi) uRange,
        (double Lo, double Hi) gRange,
        (double Lo, double Hi) vRange)
    {
        var band = new Band
        {
            Id = nextId++,
            Type = type,
            U = Math.Clamp(u, uRange.Lo, uRange.Hi),
            G = Math.Clamp(g, gRange.Lo, gRange.Hi),
            V = Math.Clamp(v, vRange.Lo, vRange.Hi),
            ULo = uRange.Lo,
            UHi = uRange.Hi,
            GLo = gRange.Lo,
            GHi = gRange.Hi,
            VLo = vRange.Lo,
            VHi = vRange.Hi
        };
        band.Response = p.Response(band.ToPeq());
        return band;
    }

    private void Sum()
    {
        Array.Clear(s);
        Array.Clear(up);
        Array.Clear(dn);
        foreach (Band band in bands)
        {
            double[] response = band.Response;
            double[]? part = band.Role == 1 ? up : band.Role == 2 ? dn : null;
            for (int i = 0; i < p.Count; i++)
            {
                s[i] += response[i];
            }

            if (part != null)
            {
                for (int i = 0; i < p.Count; i++)
                {
                    part[i] += response[i];
                }
            }
        }
    }

    // dB² · octave over the window, read against the current sums.
    private double Objective()
    {
        double total = 0;
        for (int i = 0; i < p.Count; i++)
        {
            if (p.Valid[i])
            {
                total += p.Weight[i] * PointTerms(i, out _);
            }
        }

        return total;
    }

    // A point already deep under the target changes little by being dug deeper; one at the target shows a dent.
    private double DigCharge(int i)
    {
        double depth = Math.Max(0, p.Desired[i]) / p.Tuning.DigDepthScaleDb;
        return 1 / (1 + depth * depth);
    }

    // Slope and Gauss–Newton curvature of one point's loss in the bank's sum and in the boosting and cutting bells' sums.
    private struct PointSlopes
    {
        public double Gs;
        public double Hs;
        public double Gu;
        public double Hu;
        public double Gd;
        public double Hd;
    }

    // See docs/tech/eq-auto-tuner.md#the-objective.
    private double PointTerms(int i, out PointSlopes d)
    {
        EqFitTuning t = p.Tuning;
        double want = p.Desired[i];
        double sum = s[i];
        double loss = 0;
        d = default;
        if (p.Chases[i])
        {
            double r = want - sum;
            loss = r * r;
            d.Gs = -2 * r;
            d.Hs = 2;
        }
        else
        {
            double above = sum - want;
            if (above > 0)
            {
                loss += t.AboveWeight * above * above;
                d.Gs += 2 * t.AboveWeight * above;
                d.Hs += 2 * t.AboveWeight;
            }

            if (sum < 0 && want - sum > 0)
            {
                double k = DigCharge(i);
                double dug = Math.Min(want - sum, -sum);
                loss += k * dug * dug;
                d.Gs -= 2 * k * dug;
                d.Hs += 2 * k;
                double excess = dug - t.OverCutFreeDb;
                if (excess > 0)
                {
                    loss += k * t.OverCutWeight * excess * excess;
                    d.Gs -= 2 * k * t.OverCutWeight * excess;
                    d.Hs += 2 * k * t.OverCutWeight;
                }
            }
        }

        switch (p.Options.Boosts)
        {
            case EqAutoTuneBoosts.RefillOwnCuts:
                Hinge(sum, ConstraintWeight, ref loss, ref d.Gs, ref d.Hs);
                break;
            case EqAutoTuneBoosts.Allowed:
                // Ceilings read the boosting bells alone, so a cut cannot buy a boost room over them.
                Hinge(up[i] - p.Options.BandGainMaxDb, ConstraintWeight, ref loss, ref d.Gu, ref d.Hu);
                if (!p.BoostAllowed[i] && double.IsFinite(p.Options.ForbiddenRegionMaxBoostDb))
                {
                    Hinge(up[i] - p.Options.ForbiddenRegionMaxBoostDb, ConstraintWeight, ref loss, ref d.Gu, ref d.Hu);
                }

                break;
        }

        // A boost and a cut working against each other at one point spend two slots on the difference.
        double overlapWeight = p.Refills ? RefillOverlapWeight : OverlapWeight;
        if (up[i] <= -dn[i])
        {
            Hinge(up[i], overlapWeight, ref loss, ref d.Gu, ref d.Hu);
        }
        else
        {
            double gd = 0;
            Hinge(-dn[i], overlapWeight, ref loss, ref gd, ref d.Hd);
            d.Gd -= gd;
        }

        return loss;
    }

    private static void Hinge(double over, double weight, ref double loss, ref double grad, ref double curvature)
    {
        if (over > 0)
        {
            loss += weight * over * over;
            grad += 2 * weight * over;
            curvature += 2 * weight;
        }
    }

    // Bounded Levenberg–Marquardt over (ln f, gain, ln Q) of the listed bands, Jacobian by forward differences.
    private void Refine(int[] which, int iterations)
    {
        int m = which.Length * 3;
        if (m == 0)
        {
            return;
        }

        int n = p.Count;
        Sum();
        double current = Objective();
        double mu = 1e-3;
        var jacobian = new double[m][];
        for (int k = 0; k < m; k++)
        {
            jacobian[k] = new double[n];
        }

        var role = new int[m];
        for (int k = 0; k < which.Length; k++)
        {
            role[3 * k] = role[3 * k + 1] = role[3 * k + 2] = bands[which[k]].Role;
        }

        var grad = new double[m];
        var hess = new double[m, m];
        var slopes = new PointSlopes[n];
        var free = new bool[m];
        for (int iteration = 0; iteration < iterations; iteration++)
        {
            for (int k = 0; k < which.Length; k++)
            {
                Differentiate(bands[which[k]], jacobian[3 * k], jacobian[3 * k + 1], jacobian[3 * k + 2]);
            }

            for (int i = 0; i < n; i++)
            {
                slopes[i] = default;
                if (p.Valid[i])
                {
                    PointTerms(i, out slopes[i]);
                }
            }

            for (int a = 0; a < m; a++)
            {
                double[] ja = jacobian[a];
                double sum = 0;
                for (int i = 0; i < n; i++)
                {
                    PointSlopes d = slopes[i];
                    sum += p.Weight[i] * (d.Gs + (role[a] == 1 ? d.Gu : role[a] == 2 ? d.Gd : 0)) * ja[i];
                }

                grad[a] = sum;
                for (int c = a; c < m; c++)
                {
                    double[] jc = jacobian[c];
                    int shared = role[a] == role[c] ? role[a] : 0;
                    double h = 0;
                    for (int i = 0; i < n; i++)
                    {
                        PointSlopes d = slopes[i];
                        h += p.Weight[i] * (d.Hs + (shared == 1 ? d.Hu : shared == 2 ? d.Hd : 0)) * ja[i] * jc[i];
                    }

                    hess[a, c] = h;
                    hess[c, a] = h;
                }
            }

            for (int k = 0; k < which.Length; k++)
            {
                Band band = bands[which[k]];
                free[3 * k] = Free(band.U, band.ULo, band.UHi, grad[3 * k]);
                free[3 * k + 1] = Free(band.G, band.GLo, band.GHi, grad[3 * k + 1]);
                free[3 * k + 2] = Free(band.V, band.VLo, band.VHi, grad[3 * k + 2]);
            }

            bool improved = false;
            while (mu < 1e10)
            {
                double[] step = Solve(hess, grad, free, mu);
                List<Band> before = which.Select(index => bands[index].Clone()).ToList();
                for (int k = 0; k < which.Length; k++)
                {
                    Band band = bands[which[k]];
                    band.U = Math.Clamp(band.U + step[3 * k], band.ULo, band.UHi);
                    band.G = Math.Clamp(band.G + step[3 * k + 1], band.GLo, band.GHi);
                    band.V = Math.Clamp(band.V + step[3 * k + 2], band.VLo, band.VHi);
                    band.Response = p.Response(band.ToPeq());
                }

                Sum();
                double trial = Objective();
                if (trial < current)
                {
                    improved = current - trial > 1e-5 * current + 1e-7;
                    current = trial;
                    mu = Math.Max(mu / 3, 1e-9);
                    break;
                }

                for (int k = 0; k < which.Length; k++)
                {
                    bands[which[k]] = before[k];
                }

                Sum();
                mu *= 5;
            }

            if (!improved)
            {
                return;
            }
        }
    }

    // A parameter pinned at a bound the step would push it through sits the step out.
    private static bool Free(double x, double lo, double hi, double gradient) =>
        hi > lo && !(x <= lo && gradient > 0) && !(x >= hi && gradient < 0);

    private void Differentiate(Band band, double[] du, double[] dg, double[] dv)
    {
        const double StepU = 1e-4;
        const double StepV = 1e-4;
        // Stepped inward at the upper gain bound, so a band pinned at 0 dB never evaluates as the opposite sign.
        double stepG = band.G + 1e-3 > band.GHi ? -1e-3 : 1e-3;
        double[] pu = p.Response(new PeqBand(Math.Exp(band.U + StepU), Math.Exp(band.V), band.G, band.Type));
        double[] pg = p.Response(new PeqBand(Math.Exp(band.U), Math.Exp(band.V), band.G + stepG, band.Type));
        double[] pv = p.Response(new PeqBand(Math.Exp(band.U), Math.Exp(band.V + StepV), band.G, band.Type));
        for (int i = 0; i < p.Count; i++)
        {
            du[i] = (pu[i] - band.Response[i]) / StepU;
            dg[i] = (pg[i] - band.Response[i]) / stepG;
            dv[i] = (pv[i] - band.Response[i]) / StepV;
        }
    }

    // (H + mu·diag H) x = −g over the free parameters by Cholesky; the others take no step.
    private static double[] Solve(double[,] hess, double[] grad, bool[] free, double mu)
    {
        int m = grad.Length;
        var index = new List<int>(m);
        for (int a = 0; a < m; a++)
        {
            if (free[a])
            {
                index.Add(a);
            }
        }

        var result = new double[m];
        int k = index.Count;
        if (k == 0)
        {
            return result;
        }

        var l = new double[k, k];
        var rhs = new double[k];
        for (int r = 0; r < k; r++)
        {
            for (int c = 0; c <= r; c++)
            {
                l[r, c] = hess[index[r], index[c]];
            }

            l[r, r] += mu * Math.Max(hess[index[r], index[r]], 1e-9) + 1e-12;
            rhs[r] = -grad[index[r]];
        }

        for (int j = 0; j < k; j++)
        {
            double diagonal = l[j, j];
            for (int q = 0; q < j; q++)
            {
                diagonal -= l[j, q] * l[j, q];
            }

            l[j, j] = Math.Sqrt(Math.Max(diagonal, 1e-12));
            for (int r = j + 1; r < k; r++)
            {
                double value = l[r, j];
                for (int q = 0; q < j; q++)
                {
                    value -= l[r, q] * l[j, q];
                }

                l[r, j] = value / l[j, j];
            }
        }

        var y = new double[k];
        for (int r = 0; r < k; r++)
        {
            double value = rhs[r];
            for (int q = 0; q < r; q++)
            {
                value -= l[r, q] * y[q];
            }

            y[r] = value / l[r, r];
        }

        var x = new double[k];
        for (int r = k - 1; r >= 0; r--)
        {
            double value = y[r];
            for (int q = r + 1; q < k; q++)
            {
                value -= l[q, r] * x[q];
            }

            x[r] = value / l[r, r];
        }

        for (int r = 0; r < k; r++)
        {
            result[index[r]] = x[r];
        }

        return result;
    }

    // Every candidate goes through a finished fit and is ranked there against placing none, slots included.
    // See docs/tech/eq-auto-tuner.md#shelves.
    private static List<Band> ChooseShelves(EqFitProblem problem, int budget)
    {
        PeqBandType[] directions = { PeqBandType.LowShelf, PeqBandType.HighShelf };
        var placed = new List<Band>();
        double best = Trial(problem, placed, budget, -1).Cost;
        for (int round = 0; round < directions.Length && placed.Count < budget; round++)
        {
            Band? chosen = null;
            foreach (PeqBandType type in directions)
            {
                if (placed.Any(band => band.Type == type))
                {
                    continue;
                }

                foreach (Band seed in ShelfSeeds(problem, type, placed))
                {
                    (double cost, Band? refined) = Trial(problem, placed.Append(seed).ToList(), budget, seed.Id);
                    if (refined != null && cost < best)
                    {
                        best = cost;
                        chosen = refined;
                    }
                }
            }

            // A round in which nothing wins ends the stage: the next would search the same residual.
            if (chosen == null)
            {
                break;
            }

            placed.Add(chosen);
        }

        return placed;
    }

    private static (double Cost, Band? Shelf) Trial(EqFitProblem problem, List<Band> start, int budget, int shelfId)
    {
        var trial = new EqBandFitter(problem, start);
        trial.Run(budget, passes: 1, prune: false);
        Band? shelf = trial.bands.FirstOrDefault(band => band.Id == shelfId);
        return (trial.Cost(), shelf is { } kept && Math.Abs(kept.G) >= ShelfMinGainDb ? kept.Clone() : null);
    }

    // One seed per octave of the corners a shelf may take, each bounded to where its plateau stays usable.
    private static IEnumerable<Band> ShelfSeeds(EqFitProblem problem, PeqBandType type, List<Band> placed)
    {
        EqAutoTuner.Options opt = problem.Options;
        double kneeLo = Math.Max(opt.QMin, ShelfKneeMin);
        bool low = type == PeqBandType.LowShelf;
        double lowestHz = problem.Hz[0] * Math.Pow(2, low ? ShelfPlateauSpanOctaves : ShelfSettledMarginOctaves);
        double highestHz = problem.Hz[^1] / Math.Pow(2, low ? ShelfSettledMarginOctaves : ShelfPlateauSpanOctaves);
        if (kneeLo > ShelfKneeMax || highestHz <= lowestHz || lowestHz < 1)
        {
            yield break;
        }

        var existing = new double[problem.Count];
        foreach (Band band in placed)
        {
            for (int i = 0; i < problem.Count; i++)
            {
                existing[i] += band.Response[i];
            }
        }

        int count = Math.Max(2, (int)Math.Ceiling(Math.Log2(highestHz / lowestHz) * ShelfSeedsPerOctave) + 1);
        var factory = new EqBandFitter(problem, placed);
        foreach (double cornerHz in EqualizationCurve.LogFrequencyGrid(lowestHz, highestHz, count))
        {
            double gain = PlateauGain(problem, type, cornerHz, existing);
            bool boosting = gain > 0;
            if (Math.Abs(gain) < ShelfMinGainDb ||
                (boosting && opt.Boosts != EqAutoTuneBoosts.Allowed) ||
                !HasUsablePlateau(problem, type, cornerHz, boosting))
            {
                continue;
            }

            (double loHz, double hiHz) = UsableCorners(problem, type, cornerHz, boosting, lowestHz, highestHz);
            yield return factory.Create(
                type,
                Math.Log(cornerHz),
                gain,
                Math.Log(Math.Clamp(0.5, kneeLo, ShelfKneeMax)),
                (Math.Log(loHz), Math.Log(hiHz)),
                boosting ? (0, opt.BandGainMaxDb) : (opt.BandGainMinDb, 0),
                (Math.Log(kneeLo), Math.Log(ShelfKneeMax)));
        }
    }

    // Mean of what the plateau asks for; where a point may not be lifted, only what stands above the target counts.
    private static double PlateauGain(EqFitProblem problem, PeqBandType type, double cornerHz, double[] existing)
    {
        double sum = 0;
        double weight = 0;
        for (int i = 0; i < problem.Count; i++)
        {
            if (!problem.Valid[i] || !InPlateau(problem.Hz[i], type, cornerHz))
            {
                continue;
            }

            double r = problem.Desired[i] - existing[i];
            sum += problem.Weight[i] * (problem.Chases[i] ? r : Math.Min(0, r));
            weight += problem.Weight[i];
        }

        return weight > 0
            ? Math.Clamp(sum / weight, problem.Options.BandGainMinDb, problem.Options.BandGainMaxDb)
            : 0;
    }

    // The contiguous corners within the drift whose plateau stays usable for the seed's direction.
    private static (double Lo, double Hi) UsableCorners(
        EqFitProblem problem,
        PeqBandType type,
        double cornerHz,
        bool boosting,
        double lowestHz,
        double highestHz)
    {
        double step = Math.Pow(2, 1.0 / ShelfBoundStepsPerOctave);
        double limit = Math.Pow(2, ShelfDriftOctaves);
        double lo = cornerHz;
        while (lo / step >= Math.Max(lowestHz, cornerHz / limit) &&
            HasUsablePlateau(problem, type, lo / step, boosting))
        {
            lo /= step;
        }

        double hi = cornerHz;
        while (hi * step <= Math.Min(highestHz, cornerHz * limit) &&
            HasUsablePlateau(problem, type, hi * step, boosting))
        {
            hi *= step;
        }

        return (lo, hi);
    }

    private static bool InPlateau(double hz, PeqBandType type, double cornerHz)
    {
        double octaves = Math.Log2(hz / cornerHz);
        return type == PeqBandType.LowShelf
            ? octaves <= -ShelfPlateauMarginOctaves
            : octaves >= ShelfPlateauMarginOctaves;
    }

    // Two points minimum whatever the grid: one point is a bin, not a plateau.
    private static bool HasUsablePlateau(EqFitProblem problem, PeqBandType type, double cornerHz, bool boosting)
    {
        int total = 0;
        int usable = 0;
        for (int i = 0; i < problem.Count; i++)
        {
            if (!InPlateau(problem.Hz[i], type, cornerHz))
            {
                continue;
            }

            total++;
            if (boosting ? problem.Valid[i] && problem.BoostAllowed[i] : problem.Valid[i])
            {
                usable++;
            }
        }

        return total >= 2 && usable >= total * ShelfPlateauUsableFraction;
    }
}
