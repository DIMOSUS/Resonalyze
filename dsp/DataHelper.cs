using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp
{
    public sealed class FrequencyResponseOptions
    {
        public int Window { get; set; } = 4096;
        public int LeftTukeyWindow { get; set; } = 256;
        public int RightTukeyWindow { get; set; } = 256;
        public double SmoothingInverseOctaves { get; set; } = 6;
        public int Offset { get; set; }
        public bool Unwrap { get; set; } = true;
        public bool UseCalibration { get; set; } = true;
    }

    public sealed class ImpulseResponseOptions
    {
        public int Length { get; set; } = 4096;
        public bool Logarithmic { get; set; }
    }

    /// <summary>
    /// Converts measured impulse responses into frequency-domain and time-domain plot data.
    /// </summary>
    public static class DataHelper
    {
        private const double MinimumAmplitude = 1e-8;

        public static double AmplitudeToDecibels(double amplitude)
        {
            return 20.0 * Math.Log10(Math.Max(amplitude, MinimumAmplitude));
        }

        public static double DecibelsToAmplitude(double decibels)
        {
            return Math.Pow(10.0, decibels / 20.0);
        }

        public static Complex[] ExtractWindow(
            IImpulseMeasurement measurement,
            int start,
            int length,
            double[]? window = null,
            bool wrap = false)
        {
            ArgumentNullException.ThrowIfNull(measurement);
            if (length <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }

            Complex[] source = measurement.ImpulseResponse
                ?? throw new InvalidOperationException("Impulse response is not available.");
            Complex[] result = new Complex[length];

            for (int i = 0; i < length; i++)
            {
                int sourceIndex = start + i;
                if (wrap)
                {
                    sourceIndex %= source.Length;
                    if (sourceIndex < 0)
                    {
                        sourceIndex += source.Length;
                    }
                }

                if ((uint)sourceIndex < (uint)source.Length)
                {
                    result[i] = source[sourceIndex] *
                        (window is { Length: > 0 } && i < window.Length ? window[i] : 1.0);
                }
            }

            return result;
        }

        public static List<SignalPoint> GetSpectrumData(
            IImpulseMeasurement measurement,
            int start,
            int length,
            double[]? window = null)
        {
            Complex[] spectrum = ExtractWindow(measurement, start, length, window);
            Fourier.Forward(spectrum, FourierOptions.Matlab);

            var data = new List<SignalPoint>();
            for (int i = 1; i < length / 2; i++)
            {
                double frequency = i * (measurement.SampleRate / (double)length);
                data.Add(new SignalPoint(frequency, AmplitudeToDecibels(spectrum[i].Magnitude)));
            }

            return data;
        }

        public static IReadOnlyList<AnalysisCurve> GetSpectrum(
            IImpulseMeasurement measurement,
            FrequencyResponseOptions frequencyResponseOptions,
            CalibrationFile calibration,
            bool includePrimary = true,
            bool includeHarmonics = true)
        {
            var curves = new List<AnalysisCurve>();
            int peakIndex = measurement.PeakIndex;

            if (includePrimary)
            {
                double leftTukeyWindow = (double)frequencyResponseOptions.LeftTukeyWindow / frequencyResponseOptions.Window * 2.0;
                double rightTukeyWindow = (double)frequencyResponseOptions.RightTukeyWindow / frequencyResponseOptions.Window * 2.0;

                double[] window = Windowing.TukeyWindow(frequencyResponseOptions.Window, leftTukeyWindow, rightTukeyWindow);

                int h1Length = frequencyResponseOptions.Window;
                int h1Start = peakIndex - frequencyResponseOptions.LeftTukeyWindow;

                var data = GetSpectrumData(measurement, h1Start, h1Length, window);
                data = LogarithmicResample(
                    data,
                    20,
                    20000,
                    1024,
                    frequencyResponseOptions.UseCalibration ? calibration : null,
                    frequencyResponseOptions.SmoothingInverseOctaves > 0
                        ? 1.0 / frequencyResponseOptions.SmoothingInverseOctaves
                        : 0.0);
                curves.Add(new AnalysisCurve("Frequency Response", data));
            }

            if (!includeHarmonics)
            {
                return curves;
            }

            for (int h = 2; h < 5; h++)
            {
                int peak = peakIndex - (int)measurement.HarmonicIROffset(h);

                int hStart = peakIndex - (int)measurement.HarmonicIROffset(h + 0.03);
                int hEnd = peakIndex - (int)measurement.HarmonicIROffset(h - 0.5);
                int hLength = hEnd - hStart;

                int leftOffset = peak - hStart;

                double leftTukeyWindow = (double)leftOffset / hLength * 2.0;
                double rightTukeyWindow = 0.5;
                double[] window = Windowing.TukeyWindow(hLength, leftTukeyWindow, rightTukeyWindow);

                var data = GetSpectrumData(measurement, hStart, hLength, window);
                data = LogarithmicResample(
                    data,
                    20,
                    20000,
                    1024,
                    frequencyResponseOptions.UseCalibration ? calibration : null,
                    frequencyResponseOptions.SmoothingInverseOctaves > 0
                        ? 2.0 / frequencyResponseOptions.SmoothingInverseOctaves
                        : 0.0);
                curves.Add(new AnalysisCurve(
                    $"HD{h}",
                    data,
                    h switch
                    {
                        2 => AnalysisCurveKind.SecondHarmonic,
                        3 => AnalysisCurveKind.ThirdHarmonic,
                        _ => AnalysisCurveKind.FourthHarmonic
                    }));
            }

            {
                int hStart = peakIndex - (int)measurement.HarmonicIROffset(5.5);
                int hEnd = peakIndex - (int)measurement.HarmonicIROffset(1.5);
                int hLength = hEnd - hStart;

                double leftTukeyWindow = 0.05;
                double rightTukeyWindow = 0.05;
                double[] window = Windowing.TukeyWindow(hLength, leftTukeyWindow, rightTukeyWindow);

                var data = GetSpectrumData(measurement, hStart, hLength, window);
                data = LogarithmicResample(
                    data,
                    20,
                    20000,
                    1024,
                    frequencyResponseOptions.UseCalibration ? calibration : null,
                    frequencyResponseOptions.SmoothingInverseOctaves > 0
                        ? 2.0 / frequencyResponseOptions.SmoothingInverseOctaves
                        : 0.0);
                curves.Add(new AnalysisCurve(
                    "THD+N",
                    data,
                    AnalysisCurveKind.ThdPlusNoise));
            }

            return curves;
        }

        public static List<SignalPoint> GetPhaseData(
            IImpulseMeasurement measurement,
            int offset,
            int length,
            double[] window,
            bool unwrap)
        {
            Complex[] spectrum = ExtractWindow(
                measurement,
                measurement.PeakIndex + offset,
                length,
                window);

            Fourier.Forward(spectrum, FourierOptions.Matlab);

            int n = spectrum.Length;
            List<SignalPoint> data = new();

            const double minFrequency = 100;

            double phaseAccumulator = 0.0;
            double prevWrapped = 0.0;

            for (int i = 1; i < n / 2; i++)
            {
                double f = i * measurement.SampleRate / (double)n;

                // Compensation for window start offset relative to peak.
                double phaseOffset = Math.Tau * i * offset / n;

                // First compensate, then wrap.
                double wrapped = spectrum[i].Phase - phaseOffset;
                wrapped = Math.Atan2(Math.Sin(wrapped), Math.Cos(wrapped));

                if (!unwrap)
                {
                    data.Add(new SignalPoint(f, wrapped));
                    continue;
                }

                if (i > 1 && f >= minFrequency)
                {
                    double delta = wrapped - prevWrapped;

                    if (delta > Math.PI)
                        phaseAccumulator -= Math.Tau;
                    else if (delta < -Math.PI)
                        phaseAccumulator += Math.Tau;
                }

                prevWrapped = wrapped;

                double phase = wrapped + phaseAccumulator;
                data.Add(new SignalPoint(f, phase));
            }

            return data;
        }

        public static AnalysisCurve GetPhase(
            IImpulseMeasurement measurement,
            int length,
            int leftTukeyWindow,
            int rightTukeyWindow,
            int offset,
            double smoothingInverseOctaves,
            bool unwrap)
        {
            int startOffset = -leftTukeyWindow + offset;

            double normalizedLeftWindow = (double)leftTukeyWindow / length * 2.0;
            double normalizedRightWindow = (double)rightTukeyWindow / length * 2.0;
            double[] window = Windowing.TukeyWindow(length, normalizedLeftWindow, normalizedRightWindow);

            var phaseData = GetPhaseData(measurement, startOffset, length, window, unwrap);

            List<SignalPoint> data = new List<SignalPoint>(length / 2);
            for (int i = 0; i < phaseData.Count; i++)
            {
                data.Add(new SignalPoint(phaseData[i].X, phaseData[i].Y / Math.PI * 180.0));
            }

            return new AnalysisCurve(
                "Phase",
                smoothingInverseOctaves > 0
                    ? SmoothLinear(data, 1.0 / smoothingInverseOctaves)
                    : data);
        }

        public static AnalysisCurve GetImpulse(
            IImpulseMeasurement measurement,
            ImpulseResponseOptions opt)
        {
            int offset = 512;
            int start = measurement.PeakIndex - offset;

            int length = offset + opt.Length;
            Complex[] impulse = ExtractWindow(measurement, start, length);

            List<SignalPoint> data = new List<SignalPoint> { };

            if (opt.Logarithmic)
            {
                for (int i = 0; i < length; i++)
                {
                    data.Add(new SignalPoint(i - offset, AmplitudeToDecibels(impulse[i].Magnitude)));
                }
            }
            else
            {
                for (int i = 0; i < length; i++)
                {
                    data.Add(new SignalPoint(i - offset, impulse[i].Real));
                }
            }

            return new AnalysisCurve("Impulse Response", data);
        }

        public static AnalysisCurve GetAutocorrelation(
            IImpulseMeasurement measurement,
            ImpulseResponseOptions opt)
        {
            int offset = 64;
            int length = 2048;
            const float timeWindowMilliseconds = 3.0f;

            List<SignalPoint> data = new List<SignalPoint> { };

            int start = measurement.PeakIndex - offset;
            float average = 0;

            //-- normalization
            Complex[] impulse = ExtractWindow(measurement, start, length);
            float[] impulseSamples = new float[length];
            for (int i = 0; i < length; i++)
            {
                impulseSamples[i] = (float)impulse[i].Real;
                average += impulseSamples[i];
            }
            average /= length;

            //-- self convolution
            float denominator = 0;
            for (int i = 0; i < length; i++)
            {
                denominator += (impulseSamples[i] - average) * (impulseSamples[i] - average);
            }

            float substep(int i, float step)
            {
                if (i < 1 || i > length - 3)
                    return impulseSamples[i];

                float wAcc = 0;
                float f = 0;
                for (int l = -1; l < 3; l++)
                {
                    float w = (float)LanczosKernel(l - step, 2.0);
                    wAcc += w;
                    f += w * impulseSamples[i + l];
                }

                return f / wAcc;
            }

            for (int k = 0; k < length; k++)
            {
                if (k / (float)measurement.SampleRate * 1000.0f > timeWindowMilliseconds)
                {
                    break;
                }

                // Sub-sample interpolation avoids the stair-step shape of integer-lag autocorrelation.
                for (float fractionalStep = 0; fractionalStep < 1.0f; fractionalStep += 0.1f)
                {
                    float numerator = 0;
                    for (int i = 0; i < length - k; i++)
                    {
                        numerator += (impulseSamples[i] - average) * (substep(i + k, fractionalStep) - average);
                    }

                    float timeMs = (k + fractionalStep) / (float)measurement.SampleRate * 1000.0f;
                    data.Add(new SignalPoint(timeMs, denominator > float.Epsilon ? numerator / denominator : 0));
                }
            }

            return new AnalysisCurve("Autocorrelation", data);
        }

        public static AnalysisCurve GetGroupDelay(
            IImpulseMeasurement measurement,
            int length,
            int leftTukeyWindow,
            int rightTukeyWindow,
            int offset,
            double smoothingInverseOctaves,
            double magnitudeGateDb = -30.0,
            bool wrapWindow = false)
        {
            int startOffset = -leftTukeyWindow + offset;

            double normalizedLeftWindow = (double)leftTukeyWindow / length * 2.0;
            double normalizedRightWindow = (double)rightTukeyWindow / length * 2.0;

            double[] window = Windowing.TukeyWindow(
                length,
                normalizedLeftWindow,
                normalizedRightWindow);

            Complex[] windowedImpulse = ExtractWindow(
                measurement,
                measurement.PeakIndex + startOffset,
                length,
                window,
                wrapWindow);

            Complex[] spectrum = new Complex[length];
            Complex[] timeWeightedSpectrum = new Complex[length];

            double invSampleRate = 1.0 / measurement.SampleRate;

            for (int i = 0; i < length; i++)
            {
                Complex imp = windowedImpulse[i];

                spectrum[i] = imp;
                timeWeightedSpectrum[i] = imp * (i * invSampleRate);
            }

            Fourier.Forward(spectrum, FourierOptions.Matlab);
            Fourier.Forward(timeWeightedSpectrum, FourierOptions.Matlab);

            int halfLength = length / 2;

            double maxMagnitude = 0.0;
            for (int i = 1; i < halfLength; i++)
            {
                maxMagnitude = Math.Max(maxMagnitude, spectrum[i].Magnitude);
            }

            if (maxMagnitude <= 0.0)
            {
                return new AnalysisCurve("Group Delay", new List<SignalPoint>());
            }

            double relativeGate = maxMagnitude * Math.Pow(10.0, magnitudeGateDb / 20.0);
            double absoluteGate = 1e-8;
            double minMagnitude = Math.Max(relativeGate, absoluteGate);

            List<SignalPoint> data = new();

            double absoluteStartTime = startOffset * invSampleRate;

            for (int i = 1; i < halfLength; i++)
            {
                double magnitude = spectrum[i].Magnitude;

                Complex groupDelay = timeWeightedSpectrum[i] / spectrum[i];

                double f = i * measurement.SampleRate / (double)length;

                double delayMilliseconds = (groupDelay.Real + absoluteStartTime) * 1000.0;

                if (magnitude < minMagnitude)
                {
                    delayMilliseconds = double.NaN;
                }

                data.Add(new SignalPoint(f, delayMilliseconds));
            }

            if (smoothingInverseOctaves > 0.0 && data.Count > 1)
            {
                data = SmoothLinear(data, 1.0 / smoothingInverseOctaves);
            }

            return new AnalysisCurve("Group Delay", data);
        }

        public static double LogPositionToFrequency(double x, double start, double stop)
        {
            double val = x * Math.Log10(stop / start);
            return Math.Pow(10.0, val) * start;
        }

        public static double FrequencyToLogPosition(double frequency, double start, double stop)
        {
            return Math.Log10(frequency / start) / Math.Log10(stop / start);
        }

        public static double LanczosKernel(double x, double a = 1)
        {
            if (Math.Abs(x) < 0.00001f)
            {
                return 1.0f;
            }
            if (Math.Abs(x) <= a)
            {
                return (a * Math.Sin(Math.PI * x) * Math.Sin(Math.PI * x / a)) / (Math.PI * Math.PI * x * x);
            }
            return 0.0f;
        }

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
            bool dBUnpack = true)
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

                double filteredValue = 0;
                if (dBUnpack)
                {
                    filteredValue = AmplitudeToDecibels(weightSum > 1e-12 ? weightedSum / weightSum : 0);
                }
                else
                {
                    filteredValue = weightSum > 1e-12 ? weightedSum / weightSum : 0;
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

                double filteredValue = 0;

                filteredValue = weightSum > 1e-12 ? weightedSum / weightSum : 0;

                output.Add(new SignalPoint(frequency, filteredValue));
            }

            return output;
        }
    }
}
