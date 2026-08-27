namespace Resonalyze.Dsp;

/// <summary>
/// What a set of microphones says about one driver: every microphone's curve
/// placed on the anchor's level, their spatial average, and how far apart they
/// were.
/// </summary>
/// <param name="TrimmedCurvesDb">
/// Each microphone's curve shifted onto the anchor's level, in input order, and
/// null for a microphone that could not be placed at all (see
/// <see cref="SpatialAverage.ResolveTrimDb"/>). Null rather than a curve of NaN
/// so a caller that draws the individual microphones can tell "this one has
/// nothing to say" from "this one is silent here": the first is a microphone to
/// leave out of the picture, the second is a measurement.
/// </param>
/// <param name="TrimsDb">
/// The offset added to each microphone, in input order; 0 for the anchor and
/// null for a microphone left out. Kept beside the curves because it is the
/// diagnostic: a trim of tenths of a dB is a matched pair, a trim of 8 dB is a
/// sensitivity difference the calibration files did not carry, and a trim of 40
/// dB is the wrong channel.
/// </param>
/// <param name="AverageDb">The spatial average — see <see cref="SpatialAverage.RmsAverageDb"/>.</param>
/// <param name="SpreadDb">
/// How far apart the placed microphones sit at each frequency. This is the
/// number the whole method exists to produce alongside the average: it says
/// where a single-point measurement is telling the truth about the seat and
/// where it is telling the truth only about its own 3 cm.
/// </param>
public sealed record SpatialAverageResult(
    IReadOnlyList<double[]?> TrimmedCurvesDb,
    IReadOnlyList<double?> TrimsDb,
    double[] AverageDb,
    double[] SpreadDb);

/// <summary>
/// Averaging a driver's magnitude over several microphone positions.
/// <para>
/// The average is the root mean square of pressure over the positions, and a
/// linear filter — a crossover, an EQ band, a whole DSP chain — factors straight
/// out of it, because <c>⟨|D·H|²⟩ = |D|²·⟨|H|²⟩</c> when D does not depend on
/// position. That is what lets a spatially averaged curve carry an analytically
/// predicted chain on top of it and stay a prediction of what the average would
/// measure.
/// </para>
/// <para>
/// Close to what a moving microphone performs, and NOT identical to it:
/// <see cref="Average"/> places each microphone on the anchor's level before the
/// mean, which a moving microphone has no need to do because it is one capsule at
/// one gain throughout. An array is several capsules, and the trim is what keeps a
/// sensitivity difference from being averaged in as if it were sound — at the cost
/// of removing a genuine level difference between positions along with it, since a
/// single scalar per microphone cannot tell the two apart. See
/// <see cref="Average"/> for what that costs, measured.
/// </para>
/// <para>
/// Everything here works on levels already integrated onto one shared
/// logarithmic grid (<see cref="BuildGrid"/>), never on FFT bins: the
/// microphones may have been read at different resolutions, and the grid is what
/// makes them comparable.
/// </para>
/// </summary>
public static class SpatialAverage
{
    /// <summary>The lowest band of the shared grid, in hertz.</summary>
    public const double GridStartHz = 20.0;

    /// <summary>The top band of the shared grid, in hertz.</summary>
    public const double GridStopHz = 20_000.0;

    /// <summary>Bands on the shared grid.</summary>
    public const int GridBandCount = 1_024;

    /// <summary>
    /// The frequencies every array curve lives on, ascending, in hertz.
    /// </summary>
    /// <remarks>
    /// Deliberately the grid the rest of the application already draws on — the
    /// one <c>ResampleGatedMagnitude</c> produces for every frequency response,
    /// and the one the spatial averages captured by the moving-microphone mode
    /// are stored on. A second grid of its own would be defensible in isolation
    /// (1024 points across these ten octaves is a step of 1/103 octave, finer
    /// than any of this is measured at) and would then have to be resampled at
    /// every boundary it met: the plot, an overlay, the Virtual DSP hybrid, the
    /// EQ wizard. Sharing one costs nothing and removes all of them.
    /// </remarks>
    public static IReadOnlyList<double> BuildGrid() =>
        EqualizationCurve.LogFrequencyGrid(GridStartHz, GridStopHz, GridBandCount);

    /// <summary>
    /// How far below the anchor's own peak a band may sit and still be used to
    /// place a microphone's level.
    /// <para>
    /// A trim measured over the WHOLE grid is measured mostly over noise: an
    /// array on a tweeter reads the driver over two octaves of the ten the grid
    /// spans, and the other eight hold each microphone's own noise floor, which
    /// differs by self-noise and preamp gain rather than by the sensitivity the
    /// trim is looking for. Restricting the comparison to the driver's own
    /// working band is the same 20 dB the hybrid's channel offsets already use,
    /// for the same reason.
    /// </para>
    /// </summary>
    public const double DefaultTrimBandDb = 20.0;

    /// <summary>
    /// One microphone's steady-state transfer magnitude, read off its bins onto
    /// <see cref="BuildGrid"/> as levels in dB.
    /// </summary>
    /// <remarks>
    /// The band mean of POWER, and neither resampler already in the library does
    /// that. <c>LogarithmicResample</c> interpolates amplitude across a handful of
    /// bins around each grid point, which is right for a GATED curve — a short
    /// window makes the spectrum smooth on a far coarser scale than the grid — and
    /// wrong here: an ungated response carries every mode at full bin resolution,
    /// and sampling five bins out of the sixty a high band spans reports whichever
    /// modal notch the grid point happened to land in.
    /// <c>LogarithmicPowerBandResample</c> integrates the band instead of
    /// averaging it, which is right for a noise spectrum, where power genuinely
    /// grows with bandwidth, and wrong for a transfer function, whose level must
    /// not depend on how wide the band that measured it was.
    /// <para>
    /// A bin the excitation gate closed contributes nothing — those are bins the
    /// sweep never reached — and a band with no measured bin at all comes back
    /// NaN. Below the sweep's start frequency that is precisely what the curve
    /// should say: nothing.
    /// </para>
    /// </remarks>
    /// <param name="magnitude">Linear |H| per bin, index 0..N/2, as
    /// <c>TransferFunction.ComputeAveragedMagnitude</c> returns it.</param>
    /// <param name="binWidthHz">Hertz per bin — the rate over the transform length.</param>
    public static double[] FromTransferMagnitude(
        IReadOnlyList<double> magnitude,
        double binWidthHz)
    {
        ArgumentNullException.ThrowIfNull(magnitude);
        if (!double.IsFinite(binWidthHz) || binWidthHz <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(binWidthHz));
        }

        IReadOnlyList<double> grid = BuildGrid();
        var levels = new double[grid.Count];
        // Bands meet at the geometric midpoints between grid points, so together
        // they tile the axis without overlapping or leaving a gap.
        double halfStep = Math.Sqrt(grid[1] / grid[0]);
        int highestBin = magnitude.Count - 1;
        for (int band = 0; band < grid.Count; band++)
        {
            int firstBin = (int)Math.Ceiling(grid[band] / halfStep / binWidthHz);
            int lastBin = (int)Math.Floor(grid[band] * halfStep / binWidthHz);
            // DC is not a measurement, and a band narrower than the bin spacing —
            // every low band of a long sweep — contains no bin centre at all, so
            // it reads the bin it sits in.
            firstBin = Math.Max(firstBin, 1);
            lastBin = Math.Min(lastBin, highestBin);
            if (firstBin > lastBin)
            {
                int nearest = (int)Math.Round(grid[band] / binWidthHz);
                firstBin = Math.Clamp(nearest, 1, highestBin);
                lastBin = firstBin;
            }

            double power = 0.0;
            int measured = 0;
            for (int bin = firstBin; bin <= lastBin; bin++)
            {
                double value = magnitude[bin];
                if (value > 0.0)
                {
                    power += value * value;
                    measured++;
                }
            }

            levels[band] = measured == 0
                ? double.NaN
                : 10.0 * Math.Log10(power / measured);
        }

        return levels;
    }

    /// <summary>
    /// Places every microphone on the anchor's level and averages them.
    /// </summary>
    /// <remarks>
    /// The anchor is the measurement microphone — the one that also produced the
    /// impulse response and carries the SPL calibration. Levelling to it rather
    /// than to the set's own mean is what keeps the average tethered: a mean
    /// moves whenever the set gains or loses a microphone, and the absolute level
    /// of the average would then drift with the composition of the array rather
    /// than stay where the measurement put it.
    /// <para>
    /// The trim is applied BEFORE the mean, so this is a power average of levelled
    /// positions rather than of the field as it stands, and the difference is real:
    /// two positions at 70 and 76 dB average to 74 dB as pressure and to 70 dB here.
    /// It is deliberate — the microphones are different capsules and a sensitivity
    /// difference is not sound — and it was measured before it was kept. Across the
    /// owner's seven-position sets the trims run from −1.8 to +2.6 dB, and against a
    /// pure power average of the same positions the answer differs by 0.15 to 1.95 dB
    /// of LEVEL, which the raw-impulse-response offset re-anchors downstream, and by
    /// 0.04 to 0.24 dB of SHAPE on average (0.31 to 1.08 dB at the worst single band),
    /// which is what a tune is fitted to. Positions gathered around one head differ
    /// far less in broadband level than the arithmetic allows for.
    /// </para>
    /// </remarks>
    /// <param name="curvesDb">
    /// One level curve per microphone, all on the same grid.
    /// </param>
    /// <param name="anchorIndex">Which of them is the measurement microphone.</param>
    /// <param name="trimBandDb">See <see cref="DefaultTrimBandDb"/>.</param>
    public static SpatialAverageResult Average(
        IReadOnlyList<IReadOnlyList<double>> curvesDb,
        int anchorIndex,
        double trimBandDb = DefaultTrimBandDb)
    {
        int bandCount = RequireCommonGrid(curvesDb);
        if ((uint)anchorIndex >= (uint)curvesDb.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(anchorIndex));
        }

        IReadOnlyList<double> anchor = curvesDb[anchorIndex];
        var trims = new double?[curvesDb.Count];
        var trimmed = new double[]?[curvesDb.Count];
        for (int microphone = 0; microphone < curvesDb.Count; microphone++)
        {
            double? trim = microphone == anchorIndex
                ? 0.0
                : ResolveTrimDb(curvesDb[microphone], anchor, trimBandDb);
            trims[microphone] = trim;
            if (trim is not { } offset)
            {
                continue;
            }

            IReadOnlyList<double> curve = curvesDb[microphone];
            var placed = new double[bandCount];
            for (int band = 0; band < bandCount; band++)
            {
                // A gap stays a gap: shifting NaN by a finite offset leaves NaN,
                // and spelling it out keeps that a decision rather than a
                // side effect of the arithmetic.
                placed[band] = double.IsFinite(curve[band])
                    ? curve[band] + offset
                    : double.NaN;
            }

            trimmed[microphone] = placed;
        }

        var placedCurves = new List<double[]>(curvesDb.Count);
        foreach (double[]? curve in trimmed)
        {
            if (curve != null)
            {
                placedCurves.Add(curve);
            }
        }

        return new SpatialAverageResult(
            trimmed,
            trims,
            RmsAverageDb(placedCurves),
            SpreadDb(placedCurves));
    }

    /// <summary>
    /// The offset that puts <paramref name="curveDb"/> on
    /// <paramref name="anchorDb"/>'s level, or null when the two have no common
    /// working band to compare over.
    /// </summary>
    /// <remarks>
    /// A median, not a mean: the two curves are the same driver heard from
    /// different places, so they part company by tens of dB at an interference
    /// notch that one microphone sits in and the other does not. A mean lets one
    /// such notch drag the whole placement; the median asks where the two curves
    /// AGREE, which is what a level difference is.
    /// <para>
    /// Null is a real answer and callers must handle it — a microphone that was
    /// unplugged, muted or plugged into the wrong input has no overlap with the
    /// anchor's working band, and averaging it in at its raw level would put the
    /// noise floor of a dead channel into the spatial average.
    /// </para>
    /// </remarks>
    public static double? ResolveTrimDb(
        IReadOnlyList<double> curveDb,
        IReadOnlyList<double> anchorDb,
        double trimBandDb = DefaultTrimBandDb)
    {
        ArgumentNullException.ThrowIfNull(curveDb);
        ArgumentNullException.ThrowIfNull(anchorDb);
        if (curveDb.Count != anchorDb.Count)
        {
            throw new ArgumentException(
                "The microphone and the anchor must be on the same grid.",
                nameof(curveDb));
        }
        if (!double.IsFinite(trimBandDb) || trimBandDb <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(trimBandDb));
        }

        // The peak is taken over the bands where BOTH curves exist, or the band it
        // sets could sit where this microphone has nothing to say.
        double peak = double.NegativeInfinity;
        for (int band = 0; band < anchorDb.Count; band++)
        {
            if (double.IsFinite(anchorDb[band]) && double.IsFinite(curveDb[band]))
            {
                peak = Math.Max(peak, anchorDb[band]);
            }
        }

        if (double.IsNegativeInfinity(peak))
        {
            return null;
        }

        double floor = peak - trimBandDb;
        var differences = new List<double>();
        for (int band = 0; band < anchorDb.Count; band++)
        {
            if (double.IsFinite(anchorDb[band]) &&
                double.IsFinite(curveDb[band]) &&
                anchorDb[band] >= floor)
            {
                differences.Add(anchorDb[band] - curveDb[band]);
            }
        }

        return differences.Count == 0 ? null : Median(differences);
    }

    /// <summary>
    /// The spatial average: the root mean square of pressure across the
    /// microphones, band by band.
    /// </summary>
    /// <remarks>
    /// Averaged as POWER and returned as a level, which is what makes the result
    /// an average of the sound field rather than of its logarithm. Averaging
    /// decibels instead would be a geometric mean of pressure: a position sitting
    /// in a 25 dB notch would pull the average down as hard as a position 25 dB
    /// hot would push it up, and a set of positions half of which are in a null
    /// would read far below the energy actually present. The power mean is also
    /// the one a linear filter factors out of.
    /// <para>
    /// A band no microphone could measure stays NaN; a band only some could is
    /// the mean of those, which is why the count is per band and not per set.
    /// </para>
    /// </remarks>
    public static double[] RmsAverageDb(IReadOnlyList<IReadOnlyList<double>> curvesDb)
    {
        int bandCount = RequireCommonGrid(curvesDb);
        var average = new double[bandCount];
        for (int band = 0; band < bandCount; band++)
        {
            double power = 0.0;
            int count = 0;
            foreach (IReadOnlyList<double> curve in curvesDb)
            {
                double level = curve[band];
                if (!double.IsFinite(level))
                {
                    continue;
                }

                // 10^(dB/10) is the power that level stands for; the mean of those
                // powers, back in dB, is 20·log10 of the RMS pressure.
                power += Math.Pow(10.0, level / 10.0);
                count++;
            }

            average[band] = count == 0
                ? double.NaN
                : 10.0 * Math.Log10(power / count);
        }

        return average;
    }

    /// <summary>
    /// The spread between the microphones at each band — the loudest minus the
    /// quietest, in dB.
    /// </summary>
    /// <remarks>
    /// Read it as the confidence of the average: near zero the positions agree
    /// and a single microphone would have said the same thing, while 20 dB means
    /// the dip one of them measured is a property of that seat centimetre and
    /// nothing an equalizer should be asked to fill.
    /// <para>
    /// NaN below two microphones, at every band and for a set of one. A lone
    /// microphone has no spread — not a spread of zero, which would read as
    /// perfect agreement and is the one answer that must not be given here.
    /// </para>
    /// </remarks>
    public static double[] SpreadDb(IReadOnlyList<IReadOnlyList<double>> curvesDb)
    {
        int bandCount = RequireCommonGrid(curvesDb);
        var spread = new double[bandCount];
        for (int band = 0; band < bandCount; band++)
        {
            double lowest = double.PositiveInfinity;
            double highest = double.NegativeInfinity;
            int count = 0;
            foreach (IReadOnlyList<double> curve in curvesDb)
            {
                double level = curve[band];
                if (!double.IsFinite(level))
                {
                    continue;
                }

                lowest = Math.Min(lowest, level);
                highest = Math.Max(highest, level);
                count++;
            }

            spread[band] = count < 2 ? double.NaN : highest - lowest;
        }

        return spread;
    }

    private static int RequireCommonGrid(IReadOnlyList<IReadOnlyList<double>> curvesDb)
    {
        ArgumentNullException.ThrowIfNull(curvesDb);
        if (curvesDb.Count == 0)
        {
            throw new ArgumentException(
                "There are no microphones to average.",
                nameof(curvesDb));
        }

        int bandCount = -1;
        for (int i = 0; i < curvesDb.Count; i++)
        {
            IReadOnlyList<double> curve = curvesDb[i] ?? throw new ArgumentException(
                "A microphone curve is missing.",
                nameof(curvesDb));
            if (bandCount < 0)
            {
                bandCount = curve.Count;
                if (bandCount == 0)
                {
                    throw new ArgumentException(
                        "A microphone curve is empty.",
                        nameof(curvesDb));
                }
                continue;
            }
            if (curve.Count != bandCount)
            {
                throw new ArgumentException(
                    "Every microphone must be on the same grid.",
                    nameof(curvesDb));
            }
        }

        return bandCount;
    }

    /// <summary>
    /// The middle of a set of values — the mean of the two central ones when
    /// there is an even number of them, not the upper one, which would bias every
    /// even-sized array upward by half the gap between its middle microphones.
    /// The list is sorted in place.
    /// </summary>
    private static double Median(List<double> values)
    {
        values.Sort();
        int middle = values.Count / 2;
        return values.Count % 2 == 1
            ? values[middle]
            : 0.5 * (values[middle - 1] + values[middle]);
    }
}
