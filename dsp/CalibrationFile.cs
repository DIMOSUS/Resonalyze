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

        public CalibrationFile(string file)
        {
            if (!System.IO.File.Exists(file))
            {
                return;
            }

            string[] lines = System.IO.File.ReadAllLines(file);

            foreach (string line in lines)
            {
                string l = line.Trim();
                l = l.Replace('\t', ' ');
                string[] words = l.Split(' ');
                if (words.Length == 2)
                {
                    double f = 0, db = 0;

                    bool valid =
                        double.TryParse(words[0], NumberStyles.Float, CultureInfo.InvariantCulture, out f) &&
                        double.TryParse(words[1], NumberStyles.Float, CultureInfo.InvariantCulture, out db);
                    if (valid)
                    {
                        calibration.Add(new SignalPoint(f, DataHelper.DecibelsToAmplitude(db)));
                    }
                }
            }

            calibration.Sort((left, right) => left.X.CompareTo(right.X));
        }

        public double GetDecibelCorrection(double frequency, double smoothingOctaves = 0.5)
        {
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
