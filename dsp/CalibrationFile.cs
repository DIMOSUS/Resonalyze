using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Resonalyze.Dsp
{
    /// <summary>
    /// Loads a two-column microphone calibration curve and interpolates it in log-frequency space.
    /// </summary>
    public sealed class CalibrationFile
    {
        private readonly List<SignalPoint> calibration = new();
        private readonly CalibrationFile? baseCalibration;
        private readonly Func<double, double>? decibelOffset;

        public CalibrationFile(string file)
        {
            if (!System.IO.File.Exists(file))
            {
                return;
            }

            string[] lines = System.IO.File.ReadAllLines(file);

            foreach (string line in lines)
            {
                if (TryParseCalibrationPoint(line, out double f, out double db))
                {
                    calibration.Add(new SignalPoint(f, DataHelper.DecibelsToAmplitude(db)));
                }
            }

            calibration.Sort((left, right) => left.X.CompareTo(right.X));
        }

        private CalibrationFile(
            CalibrationFile baseCalibration,
            Func<double, double> decibelOffset)
        {
            this.baseCalibration = baseCalibration;
            this.decibelOffset = decibelOffset;
        }

        public static CalibrationFile CreateNinetyDegreeApproximation(
            CalibrationFile zeroDegreeCalibration) =>
            new(zeroDegreeCalibration, Delta90Minus0);

        public static double Delta90Minus0(double hz)
        {
            const double a = 0.202901338;
            const double fc = 1917.43333;
            const double p = 1.34178411;
            const double q = 2.23024128;

            return -a * Math.Pow(Math.Log2(1.0 + Math.Pow(hz / fc, p)), q);
        }

        private static bool TryParseCalibrationPoint(
            string line,
            out double frequency,
            out double decibels)
        {
            frequency = 0;
            decibels = 0;
            string trimmed = line.Trim();
            if (trimmed.Length == 0 ||
                trimmed.StartsWith("#", StringComparison.Ordinal) ||
                trimmed.StartsWith("*", StringComparison.Ordinal) ||
                trimmed.StartsWith("//", StringComparison.Ordinal) ||
                trimmed.StartsWith(";", StringComparison.Ordinal))
            {
                return false;
            }

            List<double> numbers = ExtractNumbers(trimmed, splitCommas: false);
            if (numbers.Count < 2)
            {
                numbers = ExtractNumbers(trimmed, splitCommas: true);
            }

            if (numbers.Count < 2)
            {
                return false;
            }

            frequency = numbers[0];
            decibels = numbers[1];
            return
                frequency > 0 &&
                double.IsFinite(decibels);
        }

        private static List<double> ExtractNumbers(string line, bool splitCommas)
        {
            char[] separators = splitCommas
                ? [' ', '\t', ';', ',']
                : [' ', '\t', ';'];
            string[] fields = line.Split(
                separators,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var numbers = new List<double>(fields.Length);
            foreach (string field in fields)
            {
                if (TryParseNumber(field, out double value))
                {
                    numbers.Add(value);
                }
            }

            return numbers;
        }

        private static bool TryParseNumber(string text, out double value) =>
            double.TryParse(
                text.Replace(',', '.'),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value) &&
            double.IsFinite(value);

        public double GetDecibelCorrection(double frequency, double smoothingOctaves = 0.5)
        {
            if (baseCalibration != null && decibelOffset != null)
            {
                return baseCalibration.GetDecibelCorrection(frequency, smoothingOctaves) +
                    decibelOffset(frequency);
            }

            if (calibration.Count < 2)
            {
                return 0;
            }

            double a = 2.0;
            double frequencyRatio = Math.Pow(2.0, smoothingOctaves * 0.5);
            double halfDeltaFrequency = frequency * (frequencyRatio - 1);

            int BinarySearchX(double searchedX)
            {
                if (searchedX <= calibration[0].X)
                    return 0;
                if (searchedX >= calibration[^1].X)
                    return calibration.Count - 1;

                int left = 0;
                int right = calibration.Count - 1;

                while (left <= right)
                {
                    var middle = (left + right) / 2;

                    if (searchedX >= calibration[middle].X && searchedX < calibration[middle + 1].X)
                    {
                        return middle;
                    }
                    else if (searchedX < calibration[middle].X)
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

            double LanczosKernel(double x)
            {
                if (Math.Abs(x) < 0.00001)
                {
                    return 1.0f;
                }
                if (Math.Abs(x) <= a)
                {
                    return (a * Math.Sin(Math.PI * x) * Math.Sin(Math.PI * x / a)) / (Math.PI * Math.PI * x * x);
                }
                return 0.0f;
            }

            int corner = BinarySearchX(frequency);

            if (corner < 1)
            {
                double fraction = (calibration[corner + 1].X - frequency) /
                    (calibration[corner + 1].X - calibration[corner].X);
                return DataHelper.AmplitudeToDecibels(
                    calibration[corner].Y * fraction +
                    calibration[corner + 1].Y * (1.0 - fraction));
            }

            if (corner >= a)
                halfDeltaFrequency = Math.Max(
                    halfDeltaFrequency,
                    calibration[corner].X - calibration[corner - (int)a].X);

            double weightSum = 0;
            double weightedSum = 0;

            void Accumulate(int step, int frequencyIndex)
            {
                while ((step < 0 ? frequencyIndex > 0 : frequencyIndex < calibration.Count - 1) &&
                    (Math.Abs(calibration[frequencyIndex].X - frequency) < halfDeltaFrequency ||
                     Math.Abs(frequencyIndex - corner) < 3))
                {
                    SignalPoint samplePoint = calibration[frequencyIndex];
                    double weight = LanczosKernel((frequency - samplePoint.X) / halfDeltaFrequency * a);
                    weightedSum += samplePoint.Y * weight;
                    weightSum += weight;
                    frequencyIndex += step;
                }
            }

            Accumulate(-1, corner);
            Accumulate(1, corner + 1);

            return weightSum > 0 ? DataHelper.AmplitudeToDecibels(weightedSum / weightSum) : 0;
        }
    }
}
