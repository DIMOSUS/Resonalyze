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
    double SmoothingOctaves = 0.0)
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
    bool[] Reliable);

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
        DistortionOptions options)
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

        int[] orders = harmonicAmplitude.Keys.OrderBy(order => order).ToArray();
        var harmonicDistortion = new Dictionary<int, double[]>();
        foreach (int order in orders)
        {
            harmonicDistortion[order] = new double[gridPoints];
        }

        double[] thd = new double[gridPoints];

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
                continue;
            }

            double sumOfSquares = 0.0;
            int contributingHarmonics = 0;
            foreach (int order in orders)
            {
                double amplitude = harmonicAmplitude[order][i];
                if (double.IsFinite(amplitude) && amplitude > 0.0)
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
        }

        return new DistortionSpectrum(
            frequencies,
            linearAmplitude,
            harmonicAmplitude,
            harmonicDistortion,
            thd,
            reliable);
    }

    /// <summary>
    /// Builds the distortion display curves (HD2/HD3/HD4 and THD, in dB relative to
    /// H1) for the requested set. Runs the full pipeline: decompose, compute the
    /// relative ratios, convert to dB and smooth. The primary response is NOT
    /// produced here — it stays the loopback transfer curve.
    /// </summary>
    public static IReadOnlyList<AnalysisCurve> ComputeDistortionCurves(
        ReadOnlySpan<double> deconvolvedImpulse,
        EssSweepMetadata sweep,
        DistortionOptions options,
        CalibrationFile? calibration,
        SpectrumCurves curves)
    {
        ArgumentNullException.ThrowIfNull(sweep);
        ArgumentNullException.ThrowIfNull(options);

        var result = new List<AnalysisCurve>();
        if ((curves & SpectrumCurves.Harmonics) == 0 || deconvolvedImpulse.Length == 0)
        {
            return result;
        }

        EssHarmonicDecomposition decomposition = EssHarmonicAnalysis.AnalyzeEssHarmonics(
            deconvolvedImpulse,
            sweep,
            new HarmonicAnalysisOptions(
                MaxHarmonic: options.MaxHarmonic,
                FadeFraction: options.FadeFraction));
        DistortionSpectrum spectrum = ComputeDistortion(decomposition, calibration, options);

        void AddHarmonic(int order, SpectrumCurves flag, AnalysisCurveKind kind, string name)
        {
            if ((curves & flag) == 0 ||
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

        if ((curves & SpectrumCurves.ThdPlusNoise) != 0)
        {
            result.Add(new AnalysisCurve(
                "THD",
                BuildDbCurve(spectrum.Frequencies, spectrum.ThdRatio, options.SmoothingOctaves),
                AnalysisCurveKind.ThdPlusNoise));
        }

        return result;
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

        var points = new List<SignalPoint>(count);
        if (smoothingOctaves <= 0.0 || count < 2)
        {
            for (int i = 0; i < count; i++)
            {
                points.Add(new SignalPoint(frequencies[i], db[i]));
            }

            return points;
        }

        double octavesPerStep =
            (Math.Log2(frequencies[^1]) - Math.Log2(frequencies[0])) / (count - 1);
        double sigmaIndices = octavesPerStep > 0.0 ? smoothingOctaves / octavesPerStep : 0.0;
        if (sigmaIndices <= 0.0)
        {
            for (int i = 0; i < count; i++)
            {
                points.Add(new SignalPoint(frequencies[i], db[i]));
            }

            return points;
        }

        int radius = (int)Math.Ceiling(3.0 * sigmaIndices);
        for (int i = 0; i < count; i++)
        {
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

            points.Add(new SignalPoint(
                frequencies[i],
                weightSum > 0.0 ? accumulator / weightSum : double.NaN));
        }

        return points;
    }
}
