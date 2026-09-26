using System;
using System.Collections.Generic;
using System.Linq;

namespace Resonalyze.Dsp;

public sealed record DistortionOptions(
    int MaxHarmonic = 5,
    double LowFrequencyHz = 20.0,
    double HighFrequencyHz = 20_000.0,
    int GridPoints = 1024,
    double MaxDenominatorDropDb = 45.0,
    double FadeFraction = 0.5,
    // Fractional-octave WIDTH (FWHM), same convention as the primary response; 0 disables.
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

/// <summary>Ratios to |H1| of the same decomposition; unreliable points are NaN, never a fictitious percentage.</summary>
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

/// <summary>HDn and THD from an <see cref="EssHarmonicDecomposition"/>: THD sums energy on the excitation grid,
/// and calibration is applied at each product frequency n·f before the ratio.</summary>
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

        // Where H1 is far below its peak the ratio divides noise by noise: mark unreliable.
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

        // Overlapping orders are dropped (and excluded from THD), with the warning surfaced.
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

        // Noise floor is a separate trace, never fused into THD.
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

    private static double[] NoiseAmplitudeOnGrid(
        NoiseEstimate noise,
        CalibrationFile? calibration,
        double[] grid)
    {
        double[] frequencies = noise.BinFrequenciesHz;
        double[] magnitude = noise.Magnitude;
        double[] result = new double[grid.Length];
        Func<double, double>? correction = calibration?.AscendingCorrections();
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
            if (correction != null)
            {
                amplitude *= Math.Pow(10.0, -correction(frequency) / 20.0);
            }

            result[i] = amplitude;
        }

        return result;
    }

    /// <summary>Curves plus the isolation warnings and packet validity a display needs to explain a dropped order.</summary>
    public sealed record DistortionCurveResult(
        IReadOnlyList<AnalysisCurve> Curves,
        IReadOnlyList<string> Warnings,
        IReadOnlyList<HarmonicPacketValidity> PacketValidity,
        bool IncludesNoise);

    /// <summary>Curves only; prefer <see cref="ComputeDistortionCurvesResult"/> to also get the warnings.</summary>
    public static IReadOnlyList<AnalysisCurve> ComputeDistortionCurves(
        ReadOnlySpan<double> deconvolvedImpulse,
        EssSweepMetadata sweep,
        DistortionOptions options,
        CalibrationFile? calibration,
        SpectrumCurves curves) =>
        ComputeDistortionCurvesResult(deconvolvedImpulse, sweep, options, calibration, curves).Curves;

    public static DistortionCurveResult ComputeDistortionCurvesResult(
        ReadOnlySpan<double> deconvolvedImpulse,
        EssSweepMetadata sweep,
        DistortionOptions options,
        CalibrationFile? calibration,
        SpectrumCurves curves)
    {
        ArgumentNullException.ThrowIfNull(sweep);
        ArgumentNullException.ThrowIfNull(options);

        if ((curves & SpectrumCurves.Distortion) == 0 || deconvolvedImpulse.Length == 0)
        {
            return new DistortionCurveResult(
                [], Array.Empty<string>(), Array.Empty<HarmonicPacketValidity>(), false);
        }

        EssHarmonicDecomposition decomposition = Decompose(deconvolvedImpulse, sweep, options);
        NoiseEstimate? noise = options.IncludeNoise
            ? EssNoise.EstimateNoise(deconvolvedImpulse, decomposition, options)
            : null;
        return ComputeDistortionCurvesResult(decomposition, noise, options, calibration, curves);
    }

    /// <summary>What the curves start from, set by the record, its sweep, MaxHarmonic and FadeFraction alone; a caller
    /// drawing one record again keeps it (with the noise estimate) instead of analysing each time.</summary>
    public static EssHarmonicDecomposition Decompose(
        ReadOnlySpan<double> deconvolvedImpulse,
        EssSweepMetadata sweep,
        DistortionOptions options) =>
        EssHarmonicAnalysis.AnalyzeEssHarmonics(
            deconvolvedImpulse,
            sweep,
            new HarmonicAnalysisOptions(
                MaxHarmonic: options.MaxHarmonic,
                FadeFraction: options.FadeFraction));

    /// <param name="noise">From <see cref="EssNoise.EstimateNoise"/> when <see cref="DistortionOptions.IncludeNoise"/>.</param>
    public static DistortionCurveResult ComputeDistortionCurvesResult(
        EssHarmonicDecomposition decomposition,
        NoiseEstimate? noise,
        DistortionOptions options,
        CalibrationFile? calibration,
        SpectrumCurves curves)
    {
        ArgumentNullException.ThrowIfNull(decomposition);
        ArgumentNullException.ThrowIfNull(options);

        var result = new List<AnalysisCurve>();
        if ((curves & SpectrumCurves.Distortion) == 0)
        {
            return new DistortionCurveResult(
                result, Array.Empty<string>(), Array.Empty<HarmonicPacketValidity>(), false);
        }

        DistortionSpectrum spectrum = ComputeDistortion(decomposition, calibration, options, noise);

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
            result.Add(new AnalysisCurve(
                "THD",
                BuildDbCurve(spectrum.Frequencies, spectrum.ThdRatio, options.SmoothingOctaves),
                AnalysisCurveKind.ThdPlusNoise));
        }

        if ((curves & SpectrumCurves.NoiseFloor) != 0 && includesNoise)
        {
            // The noise floor level depends on analysis resolution, so the bandwidth is named in the label.
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

    // Calibrated at the product frequency, mapped to f = fp/n, interpolated in amplitude (never dB).
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
        Func<double, double>? correction = calibration?.AscendingCorrections();
        for (int bin = 1; bin < usableBins; bin++)
        {
            double productHz = spectrum.BinFrequencyHz(bin);
            if (productHz <= 0.0)
            {
                continue;
            }

            double amp = spectrum.AmplitudeAt(bin);
            if (correction != null)
            {
                amp *= Math.Pow(10.0, -correction(productHz) / 20.0);
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

    /// <summary><paramref name="widthOctaves"/> is the FWHM (e.g. 1/12), not sigma. NaN points stay gaps and are never neighbours.</summary>
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
            // Masked points stay gaps: filling them would draw distortion where the harmonic is unobservable.
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
