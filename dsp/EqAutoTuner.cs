using System.Numerics;

namespace Resonalyze.Dsp;

/// <summary>What Auto Tune may do above 0 dB. See docs/tech/eq-auto-tuner.md#boost-modes.</summary>
public enum EqAutoTuneBoosts
{
    /// <summary>Bands may boost, and the fit fills dips the boost mask trusts.</summary>
    Allowed,

    /// <summary>Boost bands only put back what the cuts dug: the bank's summed response stays at or below 0 dB.</summary>
    RefillOwnCuts,

    /// <summary>Every band cuts.</summary>
    Off
}

/// <summary>
/// Fits PEQ bands plus a preamp so that source + curve approximates a target: bands placed where the error is worst,
/// then frequency, gain and Q of the whole bank refined together. See docs/tech/eq-auto-tuner.md#fit.
/// </summary>
/// <remarks>All-pass bands are never fitted (they are flat). Callers replace the whole bank with the result.</remarks>
public static class EqAutoTuner
{
    public sealed record Options
    {
        public int MaxBands { get; init; } = EqualizationCurve.MaxBandCount;

        public double MinFrequencyHz { get; init; } = 20;
        public double MaxFrequencyHz { get; init; } = 20_000;

        public double BandGainMinDb { get; init; } = -15;

        /// <summary>Also bounds what the bells boost together at any frequency, so boosts do not stack.</summary>
        public double BandGainMaxDb { get; init; } = 6;

        public double QMin { get; init; } = 0.5;
        public double QMax { get; init; } = 10;

        public double PreampMinDb { get; init; } = -30;
        public double PreampMaxDb { get; init; } = 30;

        /// <summary>
        /// Ceiling on total EQ gain (preamp + bands) at any frequency; applied to the preamp AFTER bands are placed,
        /// so with boosts allowed prefer pinning the preamp. Unbounded by default; a clip-safe cuts-only caller passes 0.
        /// </summary>
        public double TotalGainMaxDb { get; init; } = double.PositiveInfinity;

        /// <summary>A deviation from the target smaller than this is never given a band.</summary>
        public double StopResidualDb { get; init; } = 0.5;

        /// <summary>Sample rate of the DSP that realises the fitted RBJ biquads.</summary>
        public double SampleRateHz { get; init; } = 48_000;

        /// <summary>Allowed here; the EQ Wizard defaults to refilling its own cuts (filling a cabin's nulls does harm).</summary>
        public EqAutoTuneBoosts Boosts { get; init; }

        /// <summary>Per-frequency boost reliability policy; consulted only when <see cref="Boosts"/> is Allowed.</summary>
        public EqBoostabilityMask.Options BoostMask { get; init; } = new();

        /// <summary>
        /// Max cumulative boost the bells may pour into a masked-off bin through their skirts (the mask only clears
        /// where a boost is aimed). +infinity disables the guard.
        /// </summary>
        public double ForbiddenRegionMaxBoostDb { get; init; } = 0.5;

        /// <summary>Lets the fit place a low and a high shelf. See docs/tech/eq-auto-tuner.md#shelves.</summary>
        public bool AllowShelves { get; init; }

        internal EqFitTuning Tuning { get; init; } = EqFitTuning.Default;
    }

    // About 50 points per octave on a tweeter's window; a Q 6 bell spans a dozen even across ten octaves.
    private const int GridSize = 256;

    /// <summary>
    /// Fits a curve so that source + curve best matches target; curves need not share frequency points.
    /// <paramref name="coherence"/> (Hz, γ²) is optional and only gates boosts.
    /// </summary>
    public static EqualizationCurve Tune(
        IReadOnlyList<SignalPoint> source,
        IReadOnlyList<SignalPoint> target,
        Options? options = null,
        IReadOnlyList<SignalPoint>? coherence = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        Options opt = options ?? new Options();
        if (!double.IsFinite(opt.SampleRateHz) || opt.SampleRateHz <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Sample rate must be positive.");
        }

        double maxFrequency = Math.Min(opt.MaxFrequencyHz, opt.SampleRateHz * 0.49);
        if (maxFrequency <= opt.MinFrequencyHz)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The fitting range must lie below the DSP Nyquist frequency.");
        }
        IReadOnlyList<double> grid = EqualizationCurve.LogFrequencyGrid(
            opt.MinFrequencyHz,
            maxFrequency,
            GridSize);
        int n = grid.Count;

        double[] sourceDb = Resample(source, grid);
        double[] targetDb = Resample(target, grid);

        var error = new double[n];
        var valid = new bool[n];
        int validCount = 0;
        double maxError = double.NegativeInfinity;
        for (int i = 0; i < n; i++)
        {
            if (double.IsFinite(sourceDb[i]) && double.IsFinite(targetDb[i]))
            {
                error[i] = targetDb[i] - sourceDb[i];
                valid[i] = true;
                maxError = Math.Max(maxError, error[i]);
                validCount++;
            }
        }

        if (validCount == 0)
        {
            return new EqualizationCurve(Array.Empty<PeqBand>());
        }

        // Preamp alignment: median error with boosts; max error when the bank may not lift the curve, so no point starts
        // below target. The ceiling is pre-applied so bands fit at the realised level. See docs/tech/eq-auto-tuner.md#preamp-alignment.
        double preamp;
        if (opt.Boosts != EqAutoTuneBoosts.Allowed)
        {
            double cutsCeiling = double.IsFinite(opt.TotalGainMaxDb)
                ? Math.Min(0.0, Math.Min(opt.PreampMaxDb, opt.TotalGainMaxDb))
                : Math.Min(0.0, opt.PreampMaxDb);
            preamp = Clamp(Math.Ceiling(maxError), opt.PreampMinDb, cutsCeiling);
        }
        else
        {
            // The median: a null or a roll-off is shape the bands answer, and a mean would carry it into the level.
            double[] errors = error.Where((_, i) => valid[i]).OrderBy(e => e).ToArray();
            double median = errors.Length % 2 == 1
                ? errors[errors.Length / 2]
                : (errors[errors.Length / 2 - 1] + errors[errors.Length / 2]) / 2;
            preamp = Clamp(Math.Round(median), opt.PreampMinDb, opt.PreampMaxDb);
        }

        var desired = new double[n];
        for (int i = 0; i < n; i++)
        {
            desired[i] = valid[i] ? error[i] - preamp : 0;
        }

        bool[] boostAllowed;
        if (opt.Boosts != EqAutoTuneBoosts.Allowed)
        {
            boostAllowed = new bool[n];
        }
        else
        {
            double[]? coherenceGrid = ResampleCoherence(coherence, grid);
            boostAllowed = EqBoostabilityMask.ComputeBoostAllowed(
                grid, sourceDb, valid, coherenceGrid, opt.BoostMask);
        }

        // Pre-computed so a band costs one biquad build; same arithmetic as DigitalEqualizationResponse.MagnitudeDbAt.
        var z1 = new Complex[n];
        var z2 = new Complex[n];
        for (int i = 0; i < n; i++)
        {
            z1[i] = Complex.Exp(new Complex(0, -Math.Tau * grid[i] / opt.SampleRateHz));
            z2[i] = z1[i] * z1[i];
        }

        List<PeqBand> bands = EqBandFitter.Fit(
            new EqFitProblem(opt, grid, z1, z2, valid, boostAllowed, desired));

        if (double.IsFinite(opt.TotalGainMaxDb))
        {
            var bank = new EqualizationCurve(bands, 0);
            double bandPeak = 0;
            for (int i = 0; i < n; i++)
            {
                if (valid[i])
                {
                    bandPeak = Math.Max(
                        bandPeak,
                        DigitalEqualizationResponse.MagnitudeDbAt(bank, grid[i], opt.SampleRateHz));
                }
            }

            // The tolerance keeps rounding noise on a bank that never lifts from costing the preamp a whole dB.
            preamp = Clamp(
                Math.Min(preamp, Math.Floor(opt.TotalGainMaxDb - bandPeak + EqBandFitter.SumToleranceDb)),
                opt.PreampMinDb,
                opt.PreampMaxDb);
        }

        return new EqualizationCurve(bands, preamp);
    }

    // Null curve means every point reliable; out-of-range frequencies hold the nearest value.
    private static double[]? ResampleCoherence(
        IReadOnlyList<SignalPoint>? coherence,
        IReadOnlyList<double> grid)
    {
        if (coherence == null || coherence.Count == 0)
        {
            return null;
        }

        var result = new double[grid.Count];
        for (int i = 0; i < grid.Count; i++)
        {
            result[i] = CurveSampling.InterpolateDbLog(coherence, grid[i], clampEnds: true);
        }

        return result;
    }

    private static double[] Resample(
        IReadOnlyList<SignalPoint> points,
        IReadOnlyList<double> grid)
    {
        var result = new double[grid.Count];
        for (int i = 0; i < grid.Count; i++)
        {
            // No end clamp: points outside the measured range read NaN and are excluded.
            result[i] = CurveSampling.InterpolateDbLog(points, grid[i], clampEnds: false);
        }

        return result;
    }

    private static double Clamp(double value, double min, double max) =>
        Math.Min(Math.Max(value, min), max);
}
