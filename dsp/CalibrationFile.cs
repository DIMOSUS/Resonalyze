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
        // Matches File.ReadAllLines' line breaking so the text and file paths parse
        // byte-identically.
        private static readonly string[] LineSeparators = ["\r\n", "\r", "\n"];

        private readonly List<SignalPoint> calibration = new();
        private readonly CalibrationFile? baseCalibration;
        private readonly Func<double, double>? decibelOffset;

        /// <summary>
        /// Loads a calibration curve from a file on disk. Filesystem problems
        /// (missing or unreadable file) and the content problem (no parsable
        /// frequency/level pairs) are surfaced through <see cref="LoadError"/>
        /// rather than thrown.
        /// </summary>
        public CalibrationFile(string file)
            : this(LoadFromFile(file))
        {
        }

        private CalibrationFile(ParseResult result)
        {
            calibration.AddRange(result.Points);
            LoadError = result.LoadError;
        }

        private CalibrationFile(
            CalibrationFile baseCalibration,
            Func<double, double> decibelOffset)
        {
            this.baseCalibration = baseCalibration;
            this.decibelOffset = decibelOffset;
            LoadError = baseCalibration.LoadError;
        }

        /// <summary>
        /// Parses a calibration curve from in-memory text without touching the
        /// filesystem, mirroring the "parser accepts text" shape of the EQ profile
        /// formats. Only the content-level problem — fewer than two parsable
        /// frequency/level pairs — can arise here and surfaces through
        /// <see cref="LoadError"/>; <paramref name="sourceName"/>, when supplied, is
        /// woven into that message so a file-backed load reads identically.
        /// </summary>
        public static CalibrationFile Parse(string text, string? sourceName = null)
        {
            ArgumentNullException.ThrowIfNull(text);
            return new CalibrationFile(ParseText(text, sourceName));
        }

        private static ParseResult LoadFromFile(string file)
        {
            if (!System.IO.File.Exists(file))
            {
                return new ParseResult([], $"Calibration file not found: {file}");
            }

            string text;
            try
            {
                text = System.IO.File.ReadAllText(file);
            }
            catch (Exception exception)
            {
                return new ParseResult(
                    [],
                    $"Calibration file could not be read: {exception.Message}");
            }

            return ParseText(text, file);
        }

        private static ParseResult ParseText(string text, string? sourceName)
        {
            var points = new List<SignalPoint>();
            foreach (string line in text.Split(LineSeparators, StringSplitOptions.None))
            {
                if (TryParseCalibrationPoint(line, out double f, out double db))
                {
                    points.Add(new SignalPoint(f, DataHelper.DecibelsToAmplitude(db)));
                }
            }

            points.Sort((left, right) => left.X.CompareTo(right.X));
            string? loadError = points.Count >= 2
                ? null
                : sourceName is null
                    ? "Calibration file contains no frequency/level pairs."
                    : $"Calibration file contains no frequency/level pairs: {sourceName}";
            return new ParseResult(points, loadError);
        }

        private readonly record struct ParseResult(List<SignalPoint> Points, string? LoadError);

        /// <summary>
        /// True when at least two calibration points were loaded, i.e.
        /// <see cref="GetDecibelCorrection"/> returns a real correction rather than
        /// the 0 dB fallback of a missing or unparsable file.
        /// </summary>
        public bool HasData => baseCalibration?.HasData ?? calibration.Count >= 2;

        /// <summary>
        /// Human-readable reason why no usable correction was loaded (missing
        /// file, unreadable file, or no parsable frequency/level pairs), or null
        /// when the calibration loaded. Callers surface this instead of letting
        /// a measurement silently run uncalibrated.
        /// </summary>
        public string? LoadError { get; }

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

            int corner = BinarySearchX(frequency);

            if (corner < 1)
            {
                // Clamped so a frequency below the calibrated range holds the first
                // point's value: an unclamped fraction would linearly extrapolate the
                // first segment's slope arbitrarily far (a file starting at 100 Hz
                // queried at 20 Hz extrapolates ~80 segment widths), easily driving
                // the amplitude negative and the correction to the -160 dB floor.
                double fraction = Math.Clamp(
                    (calibration[corner + 1].X - frequency) /
                    (calibration[corner + 1].X - calibration[corner].X),
                    0.0,
                    1.0);
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
                    double weight = DspMath.LanczosKernel(
                        (frequency - samplePoint.X) / halfDeltaFrequency * a,
                        a);
                    weightedSum += samplePoint.Y * weight;
                    weightSum += weight;
                    frequencyIndex += step;
                }
            }

            Accumulate(-1, corner);
            Accumulate(1, corner + 1);

            // Lanczos weights are signed, so a degenerate window can sum to ~0 or
            // negative; fall back to the nearest point instead of a 0 dB correction
            // that would step discontinuously against the neighbouring frequencies.
            return weightSum > 1e-9
                ? DataHelper.AmplitudeToDecibels(weightedSum / weightSum)
                : DataHelper.AmplitudeToDecibels(calibration[corner].Y);
        }
    }
}
