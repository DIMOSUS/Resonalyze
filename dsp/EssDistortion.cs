using System;
using System.Collections.Generic;
using System.Linq;

namespace Resonalyze.Dsp;

/// <summary>
/// Tuning for the relative distortion computation.
/// </summary>
public sealed record DistortionOptions(
    int MaxHarmonic = 5,
    double LowFrequencyHz = 20.0,
    double HighFrequencyHz = 20_000.0,
    int GridPoints = 1024,
    double MaxDenominatorDropDb = 45.0,
    double FadeFraction = 0.5,
    // Fractional-octave smoothing WIDTH (FWHM), e.g. 1/12 for 1/12-octave — the
    // same convention as the primary response. 0 disables smoothing.
    double SmoothingOctaves = 0.0,
    bool IncludeNoise = false,
    int NoiseWindowLength = 8_192,
    int NoiseWindowCount = 6,
    double MinNoiseConfidence = 0.5)
{
    public void Validate()
    {
        if (MaxHarmonic < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxHarmonic));
        }
        if (!(LowFrequencyHz > 0.0) || !(HighFrequencyHz > LowFrequencyHz))
        {
            throw new ArgumentOutOfRangeException(nameof(HighFrequencyHz));
        }
        if (GridPoints < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(GridPoints));
        }
        if (!(MaxDenominatorDropDb > 0.0))
        {
            throw new ArgumentOutOfRangeException(nameof(MaxDenominatorDropDb));
        }
    }
}

/// <summary>
/// Relative distortion on an excitation-frequency grid. Every quantity is a ratio
/// to the linear packet |H1| of the SAME ESS decomposition, so the numbers are
/// true HDn/THD fractions. Unreliable points (H1 near zero, or a harmonic outside
/// its observable range) are <see cref="double.NaN"/>, never a fictitious large
/// percentage.
/// </summary>
public sealed record DistortionSpectrum(
    double[] Frequencies,
    double[] LinearAmplitude,
    IReadOnlyDictionary<int, double[]> HarmonicAmplitude,
    IReadOnlyDictionary<int, double[]> HarmonicDistortionRatio,
    double[] ThdRatio,
    double[]? NoiseFloorRatio,
    NoiseEstimate? Noise,
    bool[] Reliable,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Computes relative harmonic distortion (HDn) and total harmonic distortion (THD)
/// from an <see cref="EssHarmonicDecomposition"/>. THD sums the harmonics'
/// ENERGY on a common excitation grid (not a complex sum of one shared window),
/// each harmonic is drawn against the excitation frequency, and microphone
/// calibration is applied at each product frequency n·f before the ratio, so a
/// calibration difference C(n·f) − C(f) is honoured.
/// </summary>
public static class EssDistortion
{
    public static DistortionSpectrum ComputeDistortion(
        EssHarmonicDecomposition decomposition,
        CalibrationFile? calibration,
        DistortionOptions options,
        NoiseEstimate? noise = null)
    {
        ArgumentNullException.ThrowIfNull(decomposition);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        double high = Math.Min(options.HighFrequencyHz, decomposition.Sweep.EndFrequencyHz);
        double low = Math.Min(options.LowFrequencyHz, high * 0.5);
        int gridPoints = options.GridPoints;

        double[] frequencies = new double[gridPoints];
        double logLow = Math.Log(low);
        double logHigh = Math.Log(high);
        for (int i = 0; i < gridPoints; i++)
        {
            frequencies[i] = Math.Exp(logLow + (logHigh - logLow) * i / (gridPoints - 1));
        }

        double[] linearAmplitude = ExcitationAmplitudeOnGrid(
            decomposition.Linear, decomposition.Sweep, calibration, frequencies);

        var harmonicAmplitude = new Dictionary<int, double[]>();
        foreach (HarmonicPacket packet in decomposition.Harmonics)
        {
            harmonicAmplitude[packet.Order] = ExcitationAmplitudeOnGrid(
                packet, decomposition.Sweep, calibration, frequencies);
        }

        // Denominator floor: where the linear response has dropped far below its
        // own peak the ratio divides noise by noise, so mark the point unreliable
        // rather than emitting a runaway percentage.
        double linearPeak = 0.0;
        for (int i = 0; i < gridPoints; i++)
        {
            if (double.IsFinite(linearAmplitude[i]))
            {
                linearPeak = Math.Max(linearPeak, linearAmplitude[i]);
            }
        }

        double denominatorFloor = linearPeak * Math.Pow(10.0, -options.MaxDenominatorDropDb / 20.0);
        bool[] reliable = new bool[gridPoints];

        // Orders whose packet overlaps a neighbour cannot be trusted: a leaking
        // packet reports a confident-looking curve, so it is dropped entirely (and
        // excluded from THD) rather than drawn, with the warning surfaced.
        var overlappingOrders = new HashSet<int>(
            decomposition.Validity.Packets
                .Where(packet => !packet.IsReliable)
                .Select(packet => packet.Order));

        int[] orders = harmonicAmplitude.Keys.OrderBy(order => order).ToArray();
        var harmonicDistortion = new Dictionary<int, double[]>();
        foreach (int order in orders)
        {
            harmonicDistortion[order] = new double[gridPoints];
        }

        // The noise floor is a SEPARATE trace (|N|/|H1|), not fused into THD — so
        // THD stays a clean harmonics-only figure and the noise floor needs no
        // bandwidth convention beyond its stated analysis resolution.
        bool useNoise = noise != null && noise.Confidence >= options.MinNoiseConfidence;
        double[]? noiseOnGrid = useNoise
            ? NoiseAmplitudeOnGrid(noise!, calibration, frequencies)
            : null;

        double[] thd = new double[gridPoints];
        double[]? noiseFloor = noiseOnGrid != null ? new double[gridPoints] : null;

        for (int i = 0; i < gridPoints; i++)
        {
            double denominator = linearAmplitude[i];
            reliable[i] = double.IsFinite(denominator) &&
                denominator > 0.0 &&
                denominator >= denominatorFloor;

            if (!reliable[i])
            {
                foreach (int order in orders)
                {
                    harmonicDistortion[order][i] = double.NaN;
                }
                thd[i] = double.NaN;
                if (noiseFloor != null)
                {
                    noiseFloor[i] = double.NaN;
                }
                continue;
            }

            double sumOfSquares = 0.0;
            int contributingHarmonics = 0;
            foreach (int order in orders)
            {
                double amplitude = harmonicAmplitude[order][i];
                if (!overlappingOrders.Contains(order) &&
                    double.IsFinite(amplitude) && amplitude > 0.0)
                {
                    harmonicDistortion[order][i] = amplitude / denominator;
                    sumOfSquares += amplitude * amplitude;
                    contributingHarmonics++;
                }
                else
                {
                    harmonicDistortion[order][i] = double.NaN;
                }
            }

            thd[i] = contributingHarmonics > 0
                ? Math.Sqrt(sumOfSquares) / denominator
                : double.NaN;

            if (noiseFloor != null)
            {
                double noiseAmplitude = noiseOnGrid![i];
                noiseFloor[i] = double.IsFinite(noiseAmplitude) && noiseAmplitude > 0.0
                    ? noiseAmplitude / denominator
                    : double.NaN;
            }
        }

        return new DistortionSpectrum(
            frequencies,
            linearAmplitude,
            harmonicAmplitude,
            harmonicDistortion,
            thd,
            noiseFloor,
            useNoise ? noise : null,
            reliable,
            decomposition.Validity.Warnings);
    }

    // Samples the noise magnitude onto the excitation grid (order 1, so the product
    // frequency is the grid frequency) and applies calibration at that frequency —
    // the same treatment as the linear packet, so noise and |H1| stay comparable.
    private static double[] NoiseAmplitudeOnGrid(
        NoiseEstimate noise,
        CalibrationFile? calibration,
        double[] grid)
    {
        double[] frequencies = noise.BinFrequenciesHz;
        double[] magnitude = noise.Magnitude;
        double[] result = new double[grid.Length];
        int cursor = 0;
        for (int i = 0; i < grid.Length; i++)
        {
            double frequency = grid[i];
            if (frequencies.Length < 2 ||
                frequency < frequencies[0] || frequency > frequencies[^1])
            {
                result[i] = double.NaN;
                continue;
            }

            while (cursor < frequencies.Length - 2 && frequencies[cursor + 1] < frequency)
            {
                cursor++;
            }

            double x0 = frequencies[cursor];
            double x1 = frequencies[cursor + 1];
            double t = x1 > x0 ? (frequency - x0) / (x1 - x0) : 0.0;
            double amplitude = magnitude[cursor] + t * (magnitude[cursor + 1] - magnitude[cursor]);
            if (calibration != null)
            {
                amplitude *= Math.Pow(10.0, -calibration.GetDecibelCorrection(frequency) / 20.0);
            }

            result[i] = amplitude;
        }

        return result;
    }

    /// <summary>
    /// The distortion display curves together with the diagnostics a display layer
    /// needs: the overlap/isolation warnings (a dropped or marginal order must be
    /// explained, not silently missing) and the per-order packet validity. Without
    /// this the warnings computed during decomposition never reach the UI.
    /// </summary>
    public sealed record DistortionCurveResult(
        IReadOnlyList<AnalysisCurve> Curves,
        IReadOnlyList<string> Warnings,
        IReadOnlyList<HarmonicPacketValidity> PacketValidity,
        bool IncludesNoise);

    /// <summary>
    /// Builds the distortion display curves (HD2/HD3/HD4 and THD, in dB relative to
    /// H1) for the requested set, keeping curves-only compatibility. Prefer
    /// <see cref="ComputeDistortionCurvesResult"/> to also receive the isolation
    /// warnings that explain a dropped order.
    /// </summary>
    public static IReadOnlyList<AnalysisCurve> ComputeDistortionCurves(
        ReadOnlySpan<double> deconvolvedImpulse,
        EssSweepMetadata sweep,
        DistortionOptions options,
        CalibrationFile? calibration,
        SpectrumCurves curves) =>
        ComputeDistortionCurvesResult(deconvolvedImpulse, sweep, options, calibration, curves).Curves;

    /// <summary>
    /// Runs the full distortion pipeline and returns the curves plus the isolation
    /// warnings and packet validity. The primary response is NOT produced here — it
    /// stays the loopback transfer curve.
    /// </summary>
    public static DistortionCurveResult ComputeDistortionCurvesResult(
        ReadOnlySpan<double> deconvolvedImpulse,
        EssSweepMetadata sweep,
        DistortionOptions options,
        CalibrationFile? calibration,
        SpectrumCurves curves)
    {
        ArgumentNullException.ThrowIfNull(sweep);
        ArgumentNullException.ThrowIfNull(options);

        var result = new List<AnalysisCurve>();
        if ((curves & SpectrumCurves.Distortion) == 0 || deconvolvedImpulse.Length == 0)
        {
            return new DistortionCurveResult(
                result, Array.Empty<string>(), Array.Empty<HarmonicPacketValidity>(), false);
        }

        EssHarmonicDecomposition decomposition = EssHarmonicAnalysis.AnalyzeEssHarmonics(
            deconvolvedImpulse,
            sweep,
            new HarmonicAnalysisOptions(
                MaxHarmonic: options.MaxHarmonic,
                FadeFraction: options.FadeFraction));
        NoiseEstimate? noise = options.IncludeNoise
            ? EssNoise.EstimateNoise(deconvolvedImpulse, decomposition, options)
            : null;
        DistortionSpectrum spectrum = ComputeDistortion(decomposition, calibration, options, noise);

        // Orders whose packet overlaps a neighbour are dropped entirely — no curve,
        // no THD contribution — and explained by a warning, rather than left as an
        // all-NaN line in the legend.
        var droppedOrders = new HashSet<int>(
            decomposition.Validity.Packets
                .Where(packet => !packet.IsReliable)
                .Select(packet => packet.Order));

        void AddHarmonic(int order, SpectrumCurves flag, AnalysisCurveKind kind, string name)
        {
            if ((curves & flag) == 0 ||
                droppedOrders.Contains(order) ||
                !spectrum.HarmonicDistortionRatio.TryGetValue(order, out double[]? ratio))
            {
                return;
            }

            result.Add(new AnalysisCurve(
                name,
                BuildDbCurve(spectrum.Frequencies, ratio, options.SmoothingOctaves),
                kind));
        }

        AddHarmonic(2, SpectrumCurves.SecondHarmonic, AnalysisCurveKind.SecondHarmonic, "HD2");
        AddHarmonic(3, SpectrumCurves.ThirdHarmonic, AnalysisCurveKind.ThirdHarmonic, "HD3");
        AddHarmonic(4, SpectrumCurves.FourthHarmonic, AnalysisCurveKind.FourthHarmonic, "HD4");

        bool includesNoise = spectrum.NoiseFloorRatio != null;
        if ((curves & SpectrumCurves.ThdPlusNoise) != 0)
        {
            // THD is harmonics only. The noise floor is a separate trace (REW-style)
            // under its own flag, so THD is never inflated by noise.
            result.Add(new AnalysisCurve(
                "THD",
                BuildDbCurve(spectrum.Frequencies, spectrum.ThdRatio, options.SmoothingOctaves),
                AnalysisCurveKind.ThdPlusNoise));
        }

        if ((curves & SpectrumCurves.NoiseFloor) != 0 && includesNoise)
        {
            // The noise floor is meaningful only at its analysis resolution — two
            // captures with the same physical noise but different usable tail lengths
            // read at different levels — so the equivalent noise bandwidth is named in
            // the curve label rather than left implicit.
            string label = spectrum.Noise is { } estimate
                ? $"Noise floor ({estimate.EquivalentNoiseBandwidthHz:0.##} Hz BW)"
                : "Noise floor";
            result.Add(new AnalysisCurve(
                label,
                BuildDbCurve(spectrum.Frequencies, spectrum.NoiseFloorRatio!, options.SmoothingOctaves),
                AnalysisCurveKind.NoiseFloor));
        }

        return new DistortionCurveResult(
            result, spectrum.Warnings, decomposition.Validity.Packets, includesNoise);
    }

    // Samples one packet's spectrum onto the excitation grid: each product-frequency
    // bin is calibrated at its own frequency, mapped to excitation frequency f = fp/n,
    // then linearly interpolated in AMPLITUDE (never dB) onto the grid.
    private static double[] ExcitationAmplitudeOnGrid(
        HarmonicPacket packet,
        EssSweepMetadata sweep,
        CalibrationFile? calibration,
        double[] grid)
    {
        WindowedSpectrum spectrum = packet.Spectrum;
        int order = packet.Order;
        int usableBins = spectrum.UsableBinCount;

        var excitationHz = new List<double>(usableBins);
        var amplitude = new List<double>(usableBins);
        for (int bin = 1; bin < usableBins; bin++)
        {
            double productHz = spectrum.BinFrequencyHz(bin);
            if (productHz <= 0.0)
            {
                continue;
            }

            double amp = spectrum.AmplitudeAt(bin);
            if (calibration != null)
            {
                amp *= Math.Pow(10.0, -calibration.GetDecibelCorrection(productHz) / 20.0);
            }

            excitationHz.Add(productHz / order);
            amplitude.Add(amp);
        }

        double maxExcitation = sweep.MaxExcitationHz(order);
        double[] result = new double[grid.Length];
        int cursor = 0;
        for (int i = 0; i < grid.Length; i++)
        {
            double frequency = grid[i];
            if (frequency > maxExcitation || excitationHz.Count < 2 ||
                frequency < excitationHz[0] || frequency > excitationHz[^1])
            {
                result[i] = double.NaN;
                continue;
            }

            while (cursor < excitationHz.Count - 2 && excitationHz[cursor + 1] < frequency)
            {
                cursor++;
            }

            double x0 = excitationHz[cursor];
            double x1 = excitationHz[cursor + 1];
            double t = x1 > x0 ? (frequency - x0) / (x1 - x0) : 0.0;
            result[i] = amplitude[cursor] + t * (amplitude[cursor + 1] - amplitude[cursor]);
        }

        return result;
    }

    // Converts a ratio to dB (NaN preserved as a gap), then applies a NaN-aware
    // fractional-octave Gaussian smooth over the log-frequency grid.
    private static List<SignalPoint> BuildDbCurve(
        double[] frequencies,
        double[] ratio,
        double smoothingOctaves)
    {
        int count = frequencies.Length;
        double[] db = new double[count];
        for (int i = 0; i < count; i++)
        {
            db[i] = double.IsFinite(ratio[i]) && ratio[i] > 0.0
                ? 20.0 * Math.Log10(ratio[i])
                : double.NaN;
        }

        double[] smoothed = SmoothOctaves(frequencies, db, smoothingOctaves);
        var points = new List<SignalPoint>(count);
        for (int i = 0; i < count; i++)
        {
            points.Add(new SignalPoint(frequencies[i], smoothed[i]));
        }

        return points;
    }

    /// <summary>
    /// NaN-aware fractional-octave Gaussian smooth of a dB curve on a
    /// log-frequency grid. <paramref name="widthOctaves"/> is a fractional-octave
    /// WINDOW WIDTH (the same 1/N the primary response uses, e.g. 1/12), not a
    /// Gaussian sigma: it is the full width at half maximum, so a "1/12-octave"
    /// smooth resolves detail at 1/12 octave rather than blurring across a whole
    /// octave. Masked points (NaN) stay NaN and are never used as neighbours, so a
    /// gap where a harmonic is unobservable is preserved rather than filled. A
    /// width &lt;= 0 returns the input unchanged.
    /// </summary>
    public static double[] SmoothOctaves(
        double[] frequencies,
        double[] db,
        double widthOctaves)
    {
        ArgumentNullException.ThrowIfNull(frequencies);
        ArgumentNullException.ThrowIfNull(db);

        int count = frequencies.Length;
        double[] output = new double[count];
        if (widthOctaves <= 0.0 || count < 2)
        {
            Array.Copy(db, output, count);
            return output;
        }

        // FWHM = 2·sqrt(2·ln2)·sigma, so convert the requested window width to the
        // Gaussian sigma that has that width at half maximum.
        const double fwhmToSigma = 2.354820045;
        double sigmaOctaves = widthOctaves / fwhmToSigma;
        double octavesPerStep =
            (Math.Log2(frequencies[^1]) - Math.Log2(frequencies[0])) / (count - 1);
        double sigmaIndices = octavesPerStep > 0.0 ? sigmaOctaves / octavesPerStep : 0.0;
        if (sigmaIndices <= 0.0)
        {
            Array.Copy(db, output, count);
            return output;
        }

        int radius = (int)Math.Ceiling(3.0 * sigmaIndices);
        for (int i = 0; i < count; i++)
        {
            // A masked point (harmonic above Nyquist/n, denominator rejected, or an
            // overlapping order) stays a gap: smoothing must not fill it from
            // neighbouring valid bins, or the plot would show finite distortion
            // where the harmonic is unobservable.
            if (!double.IsFinite(db[i]))
            {
                output[i] = double.NaN;
                continue;
            }

            double weightSum = 0.0;
            double accumulator = 0.0;
            int from = Math.Max(0, i - radius);
            int to = Math.Min(count - 1, i + radius);
            for (int j = from; j <= to; j++)
            {
                if (!double.IsFinite(db[j]))
                {
                    continue;
                }

                double distance = (j - i) / sigmaIndices;
                double weight = Math.Exp(-0.5 * distance * distance);
                accumulator += weight * db[j];
                weightSum += weight;
            }

            output[i] = weightSum > 0.0 ? accumulator / weightSum : double.NaN;
        }

        return output;
    }
}
