using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp
{
    public static partial class DataHelper
    {
        public static double LogPositionToFrequency(double x, double start, double stop)
        {
            double val = x * Math.Log10(stop / start);
            return Math.Pow(10.0, val) * start;
        }

        public static double FrequencyToLogPosition(double frequency, double start, double stop)
        {
            return Math.Log10(frequency / start) / Math.Log10(stop / start);
        }

        public static double LanczosKernel(double x, double a = 1) =>
            DspMath.LanczosKernel(x, a);

        /// <summary>Linear FFT bins onto a log grid through a Lanczos kernel (no nearest-bin aliasing).</summary>
        public static List<SignalPoint> LogarithmicResample(
            List<SignalPoint> input,
            double start,
            double stop,
            int steps,
            CalibrationFile? calibration = null,
            double smoothingOctaves = 1.0 / 6.0,
            bool dBUnpack = true,
            bool psychoacoustic = false)
        {
            if (input.Count < 2)
            {
                return new List<SignalPoint>();
            }
            if (steps < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(steps));
            }

            List<SignalPoint> output = new List<SignalPoint>(steps);

            double inputStep = input[1].X - input[0].X;
            double a = 2.0;
            double frequencyRatio = Math.Pow(2.0, smoothingOctaves * 0.5);

            int BinarySearchX(double searchedX)
            {
                searchedX += inputStep * 0.5;
                int left = 0;
                int right = input.Count - 1;

                if (searchedX <= input[0].X)
                    return 0;
                if (searchedX >= input[input.Count - 1].X)
                    return input.Count - 1;

                while (left <= right)
                {
                    var middle = (left + right) / 2;

                    if (searchedX >= input[middle].X && searchedX < input[middle + 1].X)
                    {
                        return middle;
                    }
                    else if (searchedX < input[middle].X)
                    {
                        right = middle - 1;
                    }
                    else
                    {
                        left = middle + 1;
                    }
                }
                return -1;
            }

            SignalPoint Sample(int index)
            {
                return input[Math.Clamp(index, 0, input.Count - 1)];
            }

            for (int i = 0; i < steps; i++)
            {
                double frequency = LogPositionToFrequency(i / (steps - 1.0), start, stop);
                double effectiveSmoothingOctaves = psychoacoustic
                    ? SpectrumSmoothing.PsychoacousticOctaves(frequency)
                    : smoothingOctaves;
                frequencyRatio = Math.Pow(2.0, effectiveSmoothingOctaves * 0.5);

                double halfDeltaFrequency = Math.Max(frequency * (frequencyRatio - 1), inputStep * a);
                double invHalfDeltaFrequency = 1.0 / halfDeltaFrequency * a;

                int centerIndex = BinarySearchX(frequency);
                int windowRadius = (int)Math.Ceiling(halfDeltaFrequency / inputStep);

                double weightSum = 0;
                double weightedSum = 0;

                // The kernel stops at the grid's ends, both of them, and the weight sum renormalises. Past the top it
                // used to read the last bin again for every virtual bin, each at the last bin's own position: under
                // an anti-alias roll-off at 44.1 kHz, 1/1-octave smoothing put 20 kHz at -9.2 dB instead of -3.4 dB.
                for (int sampleIndex = Math.Max(centerIndex - windowRadius, 0);
                    sampleIndex <= Math.Min(centerIndex + windowRadius, input.Count - 1);
                    sampleIndex++)
                {
                    SignalPoint samplePoint = Sample(sampleIndex);
                    double weight = LanczosKernel((frequency - samplePoint.X) * invHalfDeltaFrequency, a);

                    if (dBUnpack)
                    {
                        weightedSum += DecibelsToAmplitude(samplePoint.Y) * weight;
                    }
                    else
                    {
                        weightedSum += samplePoint.Y * weight;
                    }
                    weightSum += weight;
                }

                double filteredValue;
                if (weightSum > 1e-12)
                {
                    filteredValue = dBUnpack
                        ? AmplitudeToDecibels(weightedSum / weightSum)
                        : weightedSum / weightSum;
                }
                else
                {
                    // Signed Lanczos weights degenerate when the kernel leaves the input grid: hold the nearest sample, not the -160 dB floor.
                    filteredValue = Sample(centerIndex).Y;
                }

                if (psychoacoustic)
                {
                    filteredValue = PsychoacousticCubicMean(
                        input,
                        frequency,
                        effectiveSmoothingOctaves,
                        inputStep,
                        dBUnpack,
                        filteredValue);
                }

                if (calibration != null)
                {
                    output.Add(new SignalPoint(
                        frequency,
                        filteredValue - calibration.GetDecibelCorrection(frequency)));
                }
                else
                {
                    output.Add(new SignalPoint(frequency, filteredValue));
                }
            }

            return output;
        }

        // Gaussian cubic mean: FWHM follows the octave width; favours audible peaks without a hard lower envelope.
        private static double PsychoacousticCubicMean(
            List<SignalPoint> input,
            double centerFrequency,
            double smoothingOctaves,
            double inputStep,
            bool dBUnpack,
            double fallback)
        {
            const double GaussianRadiusSigma = 3.0;
            const double GaussianTaperStartSigma = 2.5;
            double sigmaOctaves = PsychoacousticGaussianSigmaOctaves(
                centerFrequency,
                smoothingOctaves,
                inputStep,
                GaussianRadiusSigma);
            if (sigmaOctaves <= 0.0 || inputStep <= 0.0)
            {
                return fallback;
            }

            double radiusOctaves = GaussianRadiusSigma * sigmaOctaves;
            double lowFrequency = centerFrequency / Math.Pow(2.0, radiusOctaves);
            double highFrequency = centerFrequency * Math.Pow(2.0, radiusOctaves);
            int firstIndex = Math.Max(
                0,
                (int)Math.Floor((lowFrequency - input[0].X) / inputStep));
            int lastIndex = Math.Min(
                input.Count - 1,
                (int)Math.Ceiling((highFrequency - input[0].X) / inputStep));

            double weightSum = 0.0;
            double weightedCubeSum = 0.0;
            for (int index = firstIndex; index <= lastIndex; index++)
            {
                SignalPoint point = input[index];
                if (!double.IsFinite(point.Y) || point.X <= 0.0)
                {
                    continue;
                }

                double distanceOctaves = Math.Log2(point.X / centerFrequency);
                double normalized = distanceOctaves / sigmaOctaves;
                double absoluteNormalized = Math.Abs(normalized);
                if (absoluteNormalized >= GaussianRadiusSigma)
                {
                    continue;
                }

                double taper = absoluteNormalized <= GaussianTaperStartSigma
                    ? 1.0
                    : 0.5 * (1.0 + Math.Cos(
                        Math.PI *
                        (absoluteNormalized - GaussianTaperStartSigma) /
                        (GaussianRadiusSigma - GaussianTaperStartSigma)));
                double weight =
                    Math.Exp(-0.5 * normalized * normalized) * taper;
                double value = dBUnpack ? DecibelsToAmplitude(point.Y) : point.Y;
                weightedCubeSum += value * value * value * weight;
                weightSum += weight;
            }

            if (weightSum <= 1e-12)
            {
                return fallback;
            }

            double cubicMean = Math.Cbrt(weightedCubeSum / weightSum);
            return dBUnpack ? AmplitudeToDecibels(cubicMean) : cubicMean;
        }

        private static double PsychoacousticGaussianSigmaOctaves(
            double centerFrequency,
            double smoothingOctaves,
            double inputStep,
            double radiusSigma)
        {
            const double GaussianFwhmToSigma = 1.0 / 2.354820045;
            double requestedSigma = smoothingOctaves * GaussianFwhmToSigma;
            if (centerFrequency <= 0.0 || inputStep <= 0.0 || radiusSigma <= 0.0)
            {
                return requestedSigma;
            }

            // Minimum half-width of two bins, using the larger side's octave radius; the lower side is bounded by the first bin,
            // or near DC the Gaussian covers everything and draws a spike (+33 dB at 47 Hz on 192 kHz / 2048 samples).
            double minimumRadiusHz = 2.0 * inputStep;
            double upperRadiusOctaves = Math.Log2(
                (centerFrequency + minimumRadiusHz) / centerFrequency);
            double lowerEdgeHz = Math.Max(
                centerFrequency - minimumRadiusHz, inputStep);
            double lowerRadiusOctaves = centerFrequency > lowerEdgeHz
                ? Math.Log2(centerFrequency / lowerEdgeHz)
                : upperRadiusOctaves;
            double minimumRadiusOctaves =
                Math.Max(upperRadiusOctaves, lowerRadiusOctaves);
            return Math.Max(
                requestedSigma,
                minimumRadiusOctaves / radiusSigma);
        }

        /// <summary>Power-integrated RTA band levels (relative dB), FFT-size-independent above the main-lobe limit. See docs/tech/phase-and-group-delay.md#rta-power-bands.</summary>
        /// <param name="amplitudeSpectrum">Tone-calibrated amplitude per bin (0..N/2), from <c>SpectrumAnalysis.ComputeInputMagnitudeSpectrum</c>.</param>
        public static List<SignalPoint> LogarithmicPowerBandResample(
            IReadOnlyList<double> amplitudeSpectrum,
            int fftLength,
            int sampleRate,
            double windowEnbwBins,
            double windowMainLobeBins,
            double start,
            double stop,
            int steps,
            double smoothingOctaves,
            bool psychoacoustic = false)
        {
            ArgumentNullException.ThrowIfNull(amplitudeSpectrum);
            if (steps < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(steps));
            }

            var output = new List<SignalPoint>(steps);
            double binWidth = fftLength > 0 ? (double)sampleRate / fftLength : 0.0;
            int maxBin = Math.Min(amplitudeSpectrum.Count - 1, fftLength / 2);
            if (binWidth <= 0.0 || maxBin < 1 || start <= 0.0 || stop <= start)
            {
                return output;
            }

            double enbw = windowEnbwBins > 0.0 ? windowEnbwBins : 1.0;

            // Main-lobe width (not ENBW): the band floor, so a tone keeps its whole lobe.
            double mainLobeBins = windowMainLobeBins > 0.0 ? windowMainLobeBins : 1.0;
            double resolutionHz = mainLobeBins * binWidth;

            // Fixed reference resolution, NOT the display smoothing: band power grows with bandwidth.
            const double referenceBandOctaves = 1.0 / 12.0;

            double preliminaryStop = Math.Min(stop, maxBin * binWidth);
            if (preliminaryStop <= start)
            {
                return output;
            }

            double gridHalfOctaves = 0.5 * Math.Log2(preliminaryStop / start) / (steps - 1);
            double halfOctaves = Math.Max(gridHalfOctaves, referenceBandOctaves * 0.5);
            double upperFactor = Math.Pow(2.0, halfOctaves);
            double lowerFactor = 1.0 / upperFactor;

            // The whole band (octave and resolution width) must fit inside the resolved range, or a flat input shows a false roll-off.
            double lowerEdge = 0.5 * binWidth;
            double upperEdge = (maxBin + 0.5) * binWidth;
            double effectiveStart = Math.Max(
                start,
                Math.Max(lowerEdge * upperFactor, lowerEdge + resolutionHz * 0.5));
            double effectiveStop = Math.Min(
                stop,
                Math.Min(upperEdge / upperFactor, upperEdge - resolutionHz * 0.5));
            if (effectiveStop <= effectiveStart)
            {
                return output;
            }

            var frequencies = new double[steps];
            var bandPowers = new double[steps];
            for (int i = 0; i < steps; i++)
            {
                double frequency = LogPositionToFrequency(i / (steps - 1.0), effectiveStart, effectiveStop);
                frequencies[i] = frequency;
                double lowFrequency = frequency * lowerFactor;
                double highFrequency = frequency * upperFactor;
                if (highFrequency - lowFrequency < resolutionHz)
                {
                    double half = resolutionHz * 0.5;
                    lowFrequency = frequency - half;
                    highFrequency = frequency + half;
                }

                // Fractional bin overlap keeps the level continuous at band edges; ENBW turns coherent-gain power into band power.
                double bandPower = 0.0;
                int firstBin = Math.Max(1, (int)Math.Ceiling(lowFrequency / binWidth - 0.5));
                int lastBin = Math.Min(maxBin, (int)Math.Floor(highFrequency / binWidth + 0.5));
                for (int bin = firstBin; bin <= lastBin; bin++)
                {
                    double binLow = (bin - 0.5) * binWidth;
                    double binHigh = (bin + 0.5) * binWidth;
                    double overlap =
                        Math.Min(highFrequency, binHigh) - Math.Max(lowFrequency, binLow);
                    if (overlap <= 0.0)
                    {
                        continue;
                    }

                    double amplitude = amplitudeSpectrum[bin];
                    bandPower += amplitude * amplitude * (overlap / binWidth);
                }

                bandPowers[i] = bandPower / enbw;
            }

            // Level-preserving mean of linear powers (a silent neighbour stays at zero power, not -160 dB).
            double octavesPerStep = Math.Log2(effectiveStop / effectiveStart) / (steps - 1);
            int smoothingHalfSteps = smoothingOctaves > 0.0 && octavesPerStep > 0.0
                ? (int)Math.Round(smoothingOctaves * 0.5 / octavesPerStep)
                : 0;

            if (smoothingHalfSteps <= 0)
            {
                for (int i = 0; i < steps; i++)
                {
                    output.Add(new SignalPoint(frequencies[i], AmplitudeToDecibels(Math.Sqrt(bandPowers[i]))));
                }

                return output;
            }

            double[] smoothedAmplitudes = SmoothBandPowersToAmplitudes(
                bandPowers, frequencies, octavesPerStep, smoothingHalfSteps, psychoacoustic);
            for (int i = 0; i < steps; i++)
            {
                output.Add(new SignalPoint(
                    frequencies[i],
                    AmplitudeToDecibels(smoothedAmplitudes[i])));
            }

            return output;
        }

        /// <summary>Replays the RTA display smoothing over stored dB band levels on their own log grid; shares the core of <see cref="LogarithmicPowerBandResample"/>. Non-finite bands pass through and are excluded from means.</summary>
        public static List<SignalPoint> SmoothBandLevels(
            IReadOnlyList<SignalPoint> bandLevelsDb,
            double smoothingOctaves,
            bool psychoacoustic)
        {
            ArgumentNullException.ThrowIfNull(bandLevelsDb);

            int steps = bandLevelsDb.Count;
            var result = new List<SignalPoint>(steps);
            if (steps < 2 || bandLevelsDb[0].X <= 0 || bandLevelsDb[^1].X <= bandLevelsDb[0].X)
            {
                result.AddRange(bandLevelsDb);
                return result;
            }

            double octavesPerStep =
                Math.Log2(bandLevelsDb[^1].X / bandLevelsDb[0].X) / (steps - 1);
            int smoothingHalfSteps = smoothingOctaves > 0.0 && octavesPerStep > 0.0
                ? (int)Math.Round(smoothingOctaves * 0.5 / octavesPerStep)
                : 0;
            if (smoothingHalfSteps <= 0)
            {
                result.AddRange(bandLevelsDb);
                return result;
            }

            var bandPowers = new double[steps];
            var frequencies = new double[steps];
            for (int i = 0; i < steps; i++)
            {
                frequencies[i] = bandLevelsDb[i].X;
                bandPowers[i] = double.IsFinite(bandLevelsDb[i].Y)
                    ? Math.Pow(10.0, bandLevelsDb[i].Y / 10.0)
                    : double.NaN;
            }

            double[] smoothedAmplitudes = SmoothBandPowersToAmplitudes(
                bandPowers, frequencies, octavesPerStep, smoothingHalfSteps, psychoacoustic);
            for (int i = 0; i < steps; i++)
            {
                result.Add(new SignalPoint(
                    frequencies[i],
                    double.IsFinite(smoothedAmplitudes[i])
                        ? AmplitudeToDecibels(smoothedAmplitudes[i])
                        : double.NaN));
            }

            return result;
        }

        // Shared by the live resampler and the replay so they cannot drift; non-finite bands stay non-finite.
        private static double[] SmoothBandPowersToAmplitudes(
            double[] bandPowers,
            double[] frequencies,
            double octavesPerStep,
            int smoothingHalfSteps,
            bool psychoacoustic)
        {
            int steps = bandPowers.Length;
            var result = new double[steps];

            // Prefix sums carry a count too, so gaps drop out of the mean.
            var powerPrefix = new double[steps + 1];
            var countPrefix = new int[steps + 1];
            for (int i = 0; i < steps; i++)
            {
                bool measured = double.IsFinite(bandPowers[i]);
                powerPrefix[i + 1] = powerPrefix[i] + (measured ? bandPowers[i] : 0.0);
                countPrefix[i + 1] = countPrefix[i] + (measured ? 1 : 0);
            }

            for (int i = 0; i < steps; i++)
            {
                if (!double.IsFinite(bandPowers[i]))
                {
                    result[i] = double.NaN;
                    continue;
                }

                if (psychoacoustic)
                {
                    result[i] = PsychoacousticPowerCubicMean(
                        bandPowers,
                        i,
                        SpectrumSmoothing.PsychoacousticOctaves(frequencies[i]),
                        octavesPerStep);
                    continue;
                }

                int lowIndex = Math.Max(0, i - smoothingHalfSteps);
                int highIndex = Math.Min(steps - 1, i + smoothingHalfSteps);
                int count = countPrefix[highIndex + 1] - countPrefix[lowIndex];
                double mean = count > 0
                    ? (powerPrefix[highIndex + 1] - powerPrefix[lowIndex]) / count
                    : bandPowers[i];
                result[i] = Math.Sqrt(mean);
            }

            return result;
        }

        private static double PsychoacousticPowerCubicMean(
            IReadOnlyList<double> bandPowers,
            int centerIndex,
            double smoothingOctaves,
            double octavesPerStep)
        {
            const double GaussianFwhmToSigma = 1.0 / 2.354820045;
            const double GaussianRadiusSigma = 3.0;
            const double GaussianTaperStartSigma = 2.5;
            double sigmaSteps =
                smoothingOctaves * GaussianFwhmToSigma / octavesPerStep;
            int radius = Math.Max(
                1,
                (int)Math.Ceiling(GaussianRadiusSigma * sigmaSteps));
            int firstIndex = Math.Max(0, centerIndex - radius);
            int lastIndex = Math.Min(bandPowers.Count - 1, centerIndex + radius);
            double weightSum = 0.0;
            double weightedCubeSum = 0.0;
            for (int index = firstIndex; index <= lastIndex; index++)
            {
                if (!double.IsFinite(bandPowers[index]))
                {
                    continue;
                }

                double normalized = (index - centerIndex) / sigmaSteps;
                double absoluteNormalized = Math.Abs(normalized);
                if (absoluteNormalized >= GaussianRadiusSigma)
                {
                    continue;
                }

                double taper = absoluteNormalized <= GaussianTaperStartSigma
                    ? 1.0
                    : 0.5 * (1.0 + Math.Cos(
                        Math.PI *
                        (absoluteNormalized - GaussianTaperStartSigma) /
                        (GaussianRadiusSigma - GaussianTaperStartSigma)));
                double weight =
                    Math.Exp(-0.5 * normalized * normalized) * taper;
                weightedCubeSum +=
                    Math.Pow(Math.Max(0.0, bandPowers[index]), 1.5) * weight;
                weightSum += weight;
            }

            return weightSum > 1e-12
                ? Math.Cbrt(weightedCubeSum / weightSum)
                : Math.Sqrt(Math.Max(0.0, bandPowers[centerIndex]));
        }

        /// <summary>Arithmetic dB mean for a ratio curve on the log display grid (e.g. summation loss). Never the magnitude path, and smooth the finished ratio, not its operands. See docs/tech/phase-and-group-delay.md#ratio-smoothing.</summary>
        public static List<SignalPoint> SmoothRatioLevels(
            IReadOnlyList<SignalPoint> ratioDb,
            double smoothingOctaves,
            bool psychoacoustic)
        {
            ArgumentNullException.ThrowIfNull(ratioDb);

            int steps = ratioDb.Count;
            var result = new List<SignalPoint>(steps);
            if (steps < 2 || ratioDb[0].X <= 0 || ratioDb[^1].X <= ratioDb[0].X)
            {
                result.AddRange(ratioDb);
                return result;
            }

            double octavesPerStep = Math.Log2(ratioDb[^1].X / ratioDb[0].X) / (steps - 1);
            int smoothingHalfSteps = smoothingOctaves > 0.0 && octavesPerStep > 0.0
                ? (int)Math.Round(smoothingOctaves * 0.5 / octavesPerStep)
                : 0;
            if (!psychoacoustic && smoothingHalfSteps <= 0)
            {
                result.AddRange(ratioDb);
                return result;
            }

            for (int i = 0; i < steps; i++)
            {
                if (!double.IsFinite(ratioDb[i].Y))
                {
                    result.Add(ratioDb[i]);
                    continue;
                }

                result.Add(new SignalPoint(
                    ratioDb[i].X,
                    psychoacoustic
                        ? GaussianDecibelMean(
                            ratioDb,
                            i,
                            SpectrumSmoothing.PsychoacousticOctaves(ratioDb[i].X),
                            octavesPerStep)
                        : BoxDecibelMean(ratioDb, i, smoothingHalfSteps)));
            }

            return result;
        }

        private static double BoxDecibelMean(
            IReadOnlyList<SignalPoint> curve,
            int centerIndex,
            int halfSteps)
        {
            int firstIndex = Math.Max(0, centerIndex - halfSteps);
            int lastIndex = Math.Min(curve.Count - 1, centerIndex + halfSteps);
            double total = 0.0;
            int count = 0;
            for (int index = firstIndex; index <= lastIndex; index++)
            {
                if (double.IsFinite(curve[index].Y))
                {
                    total += curve[index].Y;
                    count++;
                }
            }

            return count > 0 ? total / count : curve[centerIndex].Y;
        }

        // Same Gaussian as the magnitude kernel, averaging dB linearly instead of cubing powers.
        private static double GaussianDecibelMean(
            IReadOnlyList<SignalPoint> curve,
            int centerIndex,
            double smoothingOctaves,
            double octavesPerStep)
        {
            const double GaussianFwhmToSigma = 1.0 / 2.354820045;
            const double GaussianRadiusSigma = 3.0;
            const double GaussianTaperStartSigma = 2.5;
            double sigmaSteps = smoothingOctaves * GaussianFwhmToSigma / octavesPerStep;
            if (!(sigmaSteps > 0.0))
            {
                return curve[centerIndex].Y;
            }

            int radius = Math.Max(1, (int)Math.Ceiling(GaussianRadiusSigma * sigmaSteps));
            int firstIndex = Math.Max(0, centerIndex - radius);
            int lastIndex = Math.Min(curve.Count - 1, centerIndex + radius);
            double weightSum = 0.0;
            double weightedSum = 0.0;
            for (int index = firstIndex; index <= lastIndex; index++)
            {
                if (!double.IsFinite(curve[index].Y))
                {
                    continue;
                }

                double normalized = (index - centerIndex) / sigmaSteps;
                double absoluteNormalized = Math.Abs(normalized);
                if (absoluteNormalized >= GaussianRadiusSigma)
                {
                    continue;
                }

                double taper = absoluteNormalized <= GaussianTaperStartSigma
                    ? 1.0
                    : 0.5 * (1.0 + Math.Cos(
                        Math.PI *
                        (absoluteNormalized - GaussianTaperStartSigma) /
                        (GaussianRadiusSigma - GaussianTaperStartSigma)));
                double weight = Math.Exp(-0.5 * normalized * normalized) * taper;
                weightedSum += curve[index].Y * weight;
                weightSum += weight;
            }

            return weightSum > 1e-12 ? weightedSum / weightSum : curve[centerIndex].Y;
        }

        public static List<SignalPoint> SmoothLinear(List<SignalPoint> input, double smoothingOctaves = 1.0 / 6.0)
        {
            if (input.Count < 2)
            {
                return new List<SignalPoint>(input);
            }

            List<SignalPoint> output = new List<SignalPoint>(input.Count);

            double a = 2.0;
            double frequencyRatio = Math.Pow(2.0, smoothingOctaves * 0.5);

            SignalPoint Sample(int index)
            {
                return input[Math.Clamp(index, 0, input.Count - 1)];
            }

            double fStep = input[1].X - input[0].X;

            for (int i = 0; i < input.Count; i++)
            {
                var centerPoint = Sample(i);
                if (!double.IsFinite(centerPoint.Y))
                {
                    output.Add(centerPoint);
                    continue;
                }

                double frequency = centerPoint.X;

                double halfDeltaFrequency = Math.Max(frequency * (frequencyRatio - 1), fStep * a);

                int win = (int)Math.Max(2, Math.Ceiling(halfDeltaFrequency / fStep));

                double weightSum = 0;
                double weightedSum = 0;

                for (int sampleIndex = Math.Max(i - win, 0); sampleIndex <= i + win; sampleIndex++)
                {
                    SignalPoint samplePoint = Sample(sampleIndex);
                    if (!double.IsFinite(samplePoint.Y))
                    {
                        continue;
                    }

                    double weight = LanczosKernel((frequency - samplePoint.X) / halfDeltaFrequency, a);

                    weightedSum += samplePoint.Y * weight;

                    weightSum += weight;
                }

                // Degenerate weight sum: hold the centre sample, as LogarithmicResample does.
                double filteredValue = weightSum > 1e-12
                    ? weightedSum / weightSum
                    : centerPoint.Y;

                output.Add(new SignalPoint(frequency, filteredValue));
            }

            return output;
        }
    }
}
