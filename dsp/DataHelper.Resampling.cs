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

        /// <summary>
        /// Resamples linearly spaced FFT bins onto a logarithmic frequency grid.
        /// A Lanczos kernel is used to avoid the aliasing and jagged traces produced by nearest-bin lookup.
        /// </summary>
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

                for (int sampleIndex = Math.Max(centerIndex - windowRadius, 0);
                    sampleIndex <= centerIndex + windowRadius;
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
                    // Lanczos weights are signed, so the sum degenerates when the
                    // kernel window falls outside the input grid (e.g. resampling to
                    // 20 kHz from a spectrum that ends below it). Hold the nearest
                    // input sample instead of pinning the point to the -160 dB floor.
                    filteredValue = Sample(centerIndex).Y;
                }

                if (psychoacoustic)
                {
                    // A Gaussian cubic mean gives peaks more perceptual weight
                    // without a hard lower envelope that can kink smooth valleys.
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

        // Direct Gaussian approximation of the psychoacoustic smoother. Its FWHM
        // follows the frequency-dependent octave width and its cubic mean favours
        // audible peaks without clipping the lower side of the response.
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

            // Match the ordinary resampler's minimum half-width of two FFT bins.
            // On a log axis equal Hz distances are asymmetric, so use whichever
            // side requires the larger octave radius. Below two bins only the
            // upper distance is defined; DC bounds the available lower side.
            double minimumRadiusHz = 2.0 * inputStep;
            double upperRadiusOctaves = Math.Log2(
                (centerFrequency + minimumRadiusHz) / centerFrequency);
            double lowerRadiusOctaves = centerFrequency > minimumRadiusHz
                ? Math.Log2(
                    centerFrequency / (centerFrequency - minimumRadiusHz))
                : upperRadiusOctaves;
            double minimumRadiusOctaves =
                Math.Max(upperRadiusOctaves, lowerRadiusOctaves);
            return Math.Max(
                requestedSigma,
                minimumRadiusOctaves / radiusSigma);
        }

        /// <summary>
        /// FFT-size-independent band levels for an RTA shown in absolute units, ABOVE the
        /// FFT resolution limit. Where <see cref="LogarithmicResample"/> interpolates and
        /// averages AMPLITUDE (correct for a relative / transfer trace), this integrates
        /// POWER over each display band, so a broadband level does not shift with the FFT
        /// length: the summed bin power in a fixed frequency band is invariant to N (at
        /// higher N there are more, narrower bins, each carrying proportionally less
        /// power). The summed power is divided by the window's equivalent noise bandwidth
        /// so a noise band reads its true power rather than the coherent-gain (tone)
        /// over-estimate, while a bin-centred full-scale tone under a rectangular
        /// window still reads its calibrated level. Each bin contributes only the fraction
        /// of its power overlapping the band, and no band is narrower than the window's
        /// spectral main lobe, so the level is continuous (no jump as a bin centre crosses
        /// a band edge), never sub-bin, and a tone keeps its whole main lobe.
        /// <para>
        /// The N-invariance holds only where the fixed <c>1/12</c>-octave reference band is
        /// WIDER than that main lobe. Below the crossover (a low frequency, a long window
        /// such as Flat Top, or a short FFT) the band is floored to the main lobe, whose
        /// width in Hz — <c>mainLobeBins·Fs/N</c> — DOES shrink with N, so a broadband
        /// level there drops ~3 dB per doubling of N. This is the unavoidable resolution
        /// limit of a single FFT (the <c>1/12</c>-octave band is simply finer than the FFT
        /// resolves), shared by every FFT RTA, and it is the deliberate cost of the
        /// main-lobe floor: without it a coherent tone in that region would read low. Use a
        /// longer FFT for finer low-frequency resolution.
        /// </para>
        /// The
        /// integration band is a FIXED reference resolution, NOT the display smoothing:
        /// because band power grows with bandwidth, tying it to smoothing would lift a
        /// quiet spectrum by many dB. <paramref name="smoothingOctaves"/> instead applies
        /// afterwards as a level-preserving dB average that only smooths scatter. The grid
        /// is bounded so every emitted band fits WHOLE inside the resolved range: none
        /// straddles Nyquist or DC (a half-empty band would show a false roll-off on a flat
        /// input), and nothing above the last bin is synthesized from it.
        /// <para>
        /// Returns RELATIVE band levels in dB (<c>10·log10</c> of the band power); the
        /// caller adds any microphone-calibration correction and the SPL offset.
        /// </para>
        /// </summary>
        /// <param name="amplitudeSpectrum">
        /// Tone-calibrated amplitude per bin (index = bin, 0..N/2), as produced by
        /// <c>SpectrumAnalysis.ComputeInputMagnitudeSpectrum</c>.
        /// </param>
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

            // The window's spectral resolution, in Hz: the main-lobe width, not the
            // (narrower) equivalent NOISE bandwidth. A band is never integrated narrower
            // than this, so a sub-bin band does not read a random fraction of a bin, and
            // a coherent tone keeps its whole main lobe.
            double mainLobeBins = windowMainLobeBins > 0.0 ? windowMainLobeBins : 1.0;
            double resolutionHz = mainLobeBins * binWidth;

            // The band the power is integrated over is a FIXED reference resolution — a
            // fixed fractional octave, widened to the window main lobe or the grid cell
            // where those are coarser — NOT the display smoothing. Tying the band to
            // smoothing lifts the level, because band power grows with bandwidth: a wide
            // smoothing swept ever more Hz into each band and raised a quiet spectrum by
            // many dB at high frequencies. Smoothing is applied afterwards, as a
            // level-preserving average that only smooths scatter.
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

            // Keep the WHOLE band inside the resolved spectrum, not just its centre. A
            // band whose fractional-octave OR main-lobe width spilled past the last bin
            // (near Nyquist) or below the first bin (near 20 Hz with a wide main lobe)
            // would integrate a half-empty band and show a false roll-off on an input
            // that is actually flat. Bound the centre so both the octave band
            // [f/upperFactor, f·upperFactor] and the resolution band [f ± resolutionHz/2]
            // fit within the resolved range [firstBinLowEdge, lastBinHighEdge].
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

            // First pass: the fixed-resolution band POWER (linear) at each grid point.
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

                // Integrate the fraction of every bin's power that overlaps the band —
                // each bin owns the frequency interval [(k-½)·Δf, (k+½)·Δf], and only
                // its overlap with the band counts — so the level is continuous as
                // bins cross band edges instead of jumping when a centre lands inside.
                // Dividing by the window ENBW turns the coherent-gain bin power into
                // true band power (a rectangular-window tone still reads its level).
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

            // Second pass: display smoothing as a level-preserving moving MEAN of the
            // band POWERS over the requested fractional octave. A mean (not a sum) leaves
            // a flat or sloped spectrum's level unchanged and only smooths scatter; a
            // tone is diluted toward the surrounding level, as any smoothing softens a
            // spike. Averaging in the power domain (not dB) keeps a silent neighbour at
            // zero power rather than -160 dB, so it does not swamp a nearby tone. It never
            // lifts the level the way widening the integration band would.
            double octavesPerStep = Math.Log2(effectiveStop / effectiveStart) / (steps - 1);
            int smoothingHalfSteps = smoothingOctaves > 0.0 && octavesPerStep > 0.0
                ? (int)Math.Round(smoothingOctaves * 0.5 / octavesPerStep)
                : 0;

            // 10·log10(power) == 20·log10(sqrt(power)); reuse the amplitude floor.
            if (smoothingHalfSteps <= 0)
            {
                for (int i = 0; i < steps; i++)
                {
                    output.Add(new SignalPoint(frequencies[i], AmplitudeToDecibels(Math.Sqrt(bandPowers[i]))));
                }

                return output;
            }

            var prefix = new double[steps + 1];
            for (int i = 0; i < steps; i++)
            {
                prefix[i + 1] = prefix[i] + bandPowers[i];
            }

            for (int i = 0; i < steps; i++)
            {
                double smoothedAmplitude;
                if (psychoacoustic)
                {
                    smoothedAmplitude = PsychoacousticPowerCubicMean(
                        bandPowers,
                        i,
                        SpectrumSmoothing.PsychoacousticOctaves(frequencies[i]),
                        octavesPerStep);
                }
                else
                {
                    int lowIndex = Math.Max(0, i - smoothingHalfSteps);
                    int highIndex = Math.Min(steps - 1, i + smoothingHalfSteps);
                    double mean =
                        (prefix[highIndex + 1] - prefix[lowIndex]) /
                        (highIndex - lowIndex + 1);
                    smoothedAmplitude = Math.Sqrt(mean);
                }

                output.Add(new SignalPoint(
                    frequencies[i],
                    AmplitudeToDecibels(smoothedAmplitude)));
            }

            return output;
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

                // Same degenerate-weight-sum fallback as LogarithmicResample: hold
                // the centre sample rather than collapsing the point to 0.
                double filteredValue = weightSum > 1e-12
                    ? weightedSum / weightSum
                    : centerPoint.Y;

                output.Add(new SignalPoint(frequency, filteredValue));
            }

            return output;
        }
    }
}
