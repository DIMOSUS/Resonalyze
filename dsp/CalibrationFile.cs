using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Resonalyze.Dsp
{
    /// <summary>Two-column microphone calibration, interpolated in log frequency.</summary>
    public sealed class CalibrationFile
    {
        // Matches File.ReadAllLines' line breaking so text and file parse identically.
        private static readonly string[] LineSeparators = ["\r\n", "\r", "\n"];

        private readonly List<SignalPoint> calibration = new();
        private readonly CalibrationFile? baseCalibration;
        private readonly Func<double, double>? decibelOffset;

        /// <summary>Filesystem and content problems surface through <see cref="LoadError"/>, not exceptions.</summary>
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

        public static CalibrationFile Parse(string text, string? sourceName = null)
        {
            ArgumentNullException.ThrowIfNull(text);
            return new CalibrationFile(ParseText(text, sourceName));
        }

        /// <summary>From in-memory points (a session's curve); same sort-and-merge as a parsed file.</summary>
        public static CalibrationFile FromPoints(
            IEnumerable<CalibrationPoint> points,
            string? sourceName = null)
        {
            ArgumentNullException.ThrowIfNull(points);
            var signalPoints = new List<SignalPoint>();
            foreach (CalibrationPoint point in points)
            {
                if (point.FrequencyHz > 0 &&
                    double.IsFinite(point.FrequencyHz) &&
                    double.IsFinite(point.Decibels))
                {
                    signalPoints.Add(new SignalPoint(
                        point.FrequencyHz,
                        DataHelper.DecibelsToAmplitude(point.Decibels)));
                }
            }

            return new CalibrationFile(Normalize(signalPoints, sourceName));
        }

        /// <summary>Ascending points. An angular estimate is sampled on a 1/24-octave grid over the whole read range (not just the base file),
        /// since the angular difference keeps moving beyond the file's edges and the audition FIR reads up to Nyquist.</summary>
        public IReadOnlyList<CalibrationPoint> Points
        {
            get
            {
                if (baseCalibration == null)
                {
                    return calibration
                        .Select(point => new CalibrationPoint(
                            point.X,
                            DataHelper.AmplitudeToDecibels(point.Y)))
                        .ToArray();
                }

                IReadOnlyList<CalibrationPoint> basePoints = baseCalibration.Points;
                if (basePoints.Count == 0)
                {
                    return Array.Empty<CalibrationPoint>();
                }

                var frequencies = new SortedSet<double>(
                    basePoints.Select(point => point.FrequencyHz));
                double first = Math.Min(basePoints[0].FrequencyHz, SampleGridLowHz);
                double last = Math.Max(basePoints[^1].FrequencyHz, SampleGridHighHz);
                for (double frequency = first; frequency < last; frequency *= SampleGridStep)
                {
                    frequencies.Add(frequency);
                }

                frequencies.Add(last);
                return frequencies
                    .Select(frequency => new CalibrationPoint(
                        frequency,
                        GetDecibelCorrection(frequency)))
                    .ToArray();
            }
        }

        private static readonly double SampleGridStep = Math.Pow(2.0, 1.0 / 24.0);

        // 192 kHz = Nyquist of 384 kHz, above every audition rate.
        private const double SampleGridLowHz = 1.0;
        private const double SampleGridHighHz = 192_000.0;

        /// <summary>Same points within rounding; this, not an id, decides whether a session's curve is already known.</summary>
        public static bool SameCurve(CalibrationFile? left, CalibrationFile? right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null)
            {
                return false;
            }

            IReadOnlyList<CalibrationPoint> a = left.Points;
            IReadOnlyList<CalibrationPoint> b = right.Points;
            if (a.Count != b.Count)
            {
                return false;
            }

            for (int i = 0; i < a.Count; i++)
            {
                if (Math.Abs(a[i].FrequencyHz - b[i].FrequencyHz) >
                        FrequencyTolerance * Math.Abs(a[i].FrequencyHz) ||
                    Math.Abs(a[i].Decibels - b[i].Decibels) > DecibelTolerance)
                {
                    return false;
                }
            }

            return true;
        }

        private const double FrequencyTolerance = 1e-9;
        private const double DecibelTolerance = 1e-6;

        public string ToText()
        {
            var text = new System.Text.StringBuilder();
            foreach (CalibrationPoint point in Points)
            {
                text.Append(point.FrequencyHz.ToString("R", CultureInfo.InvariantCulture))
                    .Append(' ')
                    .Append(point.Decibels.ToString("R", CultureInfo.InvariantCulture))
                    .AppendLine();
            }

            return text.ToString();
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

            return Normalize(points, sourceName);
        }

        private static ParseResult Normalize(List<SignalPoint> points, string? sourceName)
        {
            points.Sort((left, right) => left.X.CompareTo(right.X));

            // Duplicate frequencies would make a zero-width segment; average them.
            for (int i = points.Count - 1; i > 0; i--)
            {
                if (points[i].X == points[i - 1].X)
                {
                    points[i - 1] = new SignalPoint(
                        points[i - 1].X,
                        (points[i - 1].Y + points[i].Y) / 2.0);
                    points.RemoveAt(i);
                }
            }

            string? loadError = points.Count >= 2
                ? null
                : sourceName is null
                    ? "Calibration file contains no frequency/level pairs."
                    : $"Calibration file contains no frequency/level pairs: {sourceName}";
            return new ParseResult(points, loadError);
        }

        private readonly record struct ParseResult(List<SignalPoint> Points, string? LoadError);

        public bool HasData => baseCalibration?.HasData ?? calibration.Count >= 2;

        public string? LoadError { get; }

        /// <summary>The angular difference ADDS to the 0° file, which the measurement divides out.</summary>
        public static CalibrationFile CreateAngled(
            CalibrationFile zeroDegreeCalibration,
            Func<double, double> angleDeltaDb)
        {
            ArgumentNullException.ThrowIfNull(zeroDegreeCalibration);
            ArgumentNullException.ThrowIfNull(angleDeltaDb);
            return new CalibrationFile(zeroDegreeCalibration, angleDeltaDb);
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

        // Exact piecewise-linear in (log f, dB): a calibration must reproduce its own points; smoothing belongs to display.
        public double GetDecibelCorrection(double frequency)
        {
            if (baseCalibration != null && decibelOffset != null)
            {
                return baseCalibration.GetDecibelCorrection(frequency) +
                    decibelOffset(frequency);
            }

            if (calibration.Count == 0)
            {
                return 0;
            }
            if (calibration.Count == 1)
            {
                return DataHelper.AmplitudeToDecibels(calibration[0].Y);
            }

            // Hold the edge value; extrapolation invents corrections.
            if (frequency <= calibration[0].X)
            {
                return DataHelper.AmplitudeToDecibels(calibration[0].Y);
            }
            if (frequency >= calibration[^1].X)
            {
                return DataHelper.AmplitudeToDecibels(calibration[^1].Y);
            }

            int left = 0;
            int right = calibration.Count - 1;
            while (right - left > 1)
            {
                int middle = (left + right) / 2;
                if (calibration[middle].X <= frequency)
                {
                    left = middle;
                }
                else
                {
                    right = middle;
                }
            }

            double lowDb = DataHelper.AmplitudeToDecibels(calibration[left].Y);
            double highDb = DataHelper.AmplitudeToDecibels(calibration[right].Y);
            double lowX = calibration[left].X;
            double highX = calibration[right].X;
            double position = lowX > 0
                ? Math.Log(frequency / lowX) / Math.Log(highX / lowX)
                : (frequency - lowX) / (highX - lowX);
            return lowDb + (highDb - lowDb) * position;
        }
    }

    public readonly record struct CalibrationPoint(double FrequencyHz, double Decibels);
}
