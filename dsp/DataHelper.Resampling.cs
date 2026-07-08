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
