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

        /// <summary>
        /// Builds a calibration from (Hz, dB) points already in memory — the
        /// curve a Virtual DSP session carries inside itself. The points go
        /// through the same sort-and-merge as a parsed file, so a curve written
        /// out by <see cref="Points"/> and read back here corrects identically.
        /// </summary>
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

        /// <summary>
        /// The curve as ascending (Hz, dB) points, which is what a file states
        /// and what a session stores. A file-backed calibration returns its own
        /// points; an angular estimate — a function, not a table — is sampled on
        /// a 1/24-octave grid spanning the base file plus the base points
        /// themselves, which reproduces <see cref="GetDecibelCorrection"/> at
        /// every knot and to within the estimate's own smoothness between them.
        /// Empty when nothing loaded.
        /// </summary>
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
                double first = basePoints[0].FrequencyHz;
                double last = basePoints[^1].FrequencyHz;
                for (double frequency = first; frequency < last; frequency *= SampleGridStep)
                {
                    frequencies.Add(frequency);
                }

                return frequencies
                    .Select(frequency => new CalibrationPoint(
                        frequency,
                        GetDecibelCorrection(frequency)))
                    .ToArray();
            }
        }

        // 1/24 octave: fine enough that the piecewise-linear reading of the
        // sampled angular estimate stays within hundredths of a dB of the model.
        private static readonly double SampleGridStep = Math.Pow(2.0, 1.0 / 24.0);

        /// <summary>
        /// Whether two calibrations correct identically: the same points, Hz and
        /// dB alike, within rounding. Two nulls are the same (no correction), a
        /// null and a curve are not. This — not an id — is what says whether a
        /// curve that arrived inside a session is one the machine already has.
        /// </summary>
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

        // Far below anything a calibration file states (three decimals at most)
        // and far above the dB -> amplitude -> dB round trip the points take.
        private const double FrequencyTolerance = 1e-9;
        private const double DecibelTolerance = 1e-6;

        /// <summary>
        /// The curve as the plain two-column text every calibration reader —
        /// this one included — accepts, one <c>frequency level</c> pair per line.
        /// <see cref="Parse"/> of the result yields the same points.
        /// </summary>
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

        // The one path from raw points to a usable table, shared by the text
        // parser and the in-memory constructor so both read identically.
        private static ParseResult Normalize(List<SignalPoint> points, string? sourceName)
        {
            points.Sort((left, right) => left.X.CompareTo(right.X));

            // Duplicate frequencies would make an interpolation segment
            // zero-width (a division by zero straight into the correction);
            // merge them by averaging the amplitudes.
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

        /// <summary>
        /// The calibration <paramref name="zeroDegreeCalibration"/> becomes when
        /// the microphone is turned by an angle, given that angle's estimated
        /// difference. The file states the microphone's own response, which the
        /// measurement then divides out (see
        /// <see cref="DataHelper.LogarithmicResample"/>), so the angular
        /// difference ADDS to it: turning the microphone off-axis really does
        /// lower what it reports at high frequency.
        /// </summary>
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

        // The correction is EXACT piecewise-linear interpolation in
        // (log frequency, dB) — the natural reading of a calibration file. The
        // previous Lanczos-smoothed lookup silently half-octave-smoothed every
        // correction (a +12 dB point read back as ~+5.6 dB) and overshot near
        // steps even at zero smoothing; a calibration must reproduce its own
        // points, and any smoothing belongs to the measurement's display
        // smoothing, not to the correction.
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

            // Outside the calibrated range the nearest point's value holds:
            // extrapolating an edge segment arbitrarily far invents corrections
            // the file never measured.
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

    /// <summary>One calibration point: the correction the file states at a frequency.</summary>
    public readonly record struct CalibrationPoint(double FrequencyHz, double Decibels);
}
