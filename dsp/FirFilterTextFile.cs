using System.Globalization;
using System.Text.RegularExpressions;

namespace Resonalyze.Dsp;

/// <summary>One-coefficient-per-line FIR text (rePhase, REW, miniDSP .txt/.fir).</summary>
/// <remarks>Lenient above the first tap (multi-number lines skipped, never column-guessed); strict after it: a stray line refuses the file,
/// since a missing tap shifts every later tap. A lone decimal comma is a decimal point; mixed conventions are refused.
/// A "Sample rate" header is only the declared rate; taps are never resampled.</remarks>
public static partial class FirFilterTextFile
{
    public static FirFilter Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var taps = new List<double>();
        int? declaredRate = null;
        int skippedNumeric = 0;
        bool sawDecimalPoint = false;
        bool sawDecimalComma = false;
        // Counts blank lines so the refusal names the line an editor shows.
        int lineNumber = 0;
        foreach (string rawLine in text.Split('\n'))
        {
            lineNumber++;
            string line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (double.TryParse(line, NumberStyles.Float, CultureInfo.InvariantCulture, out double tap))
            {
                sawDecimalPoint |= line.Contains('.');
                AddTap(taps, tap);
                continue;
            }

            if (DecimalCommaPattern().IsMatch(line) &&
                double.TryParse(
                    line.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out tap))
            {
                sawDecimalComma = true;
                AddTap(taps, tap);
                continue;
            }

            if (taps.Count > 0)
            {
                if (IsComment(line))
                {
                    continue;
                }

                throw new InvalidDataException(
                    $"Line {lineNumber} of the file, inside the coefficients, is not one " +
                    $"number: \"{Abbreviate(line)}\". This reader expects ONE coefficient " +
                    "per line, first tap first; a line it skipped would shift every tap " +
                    "after it in time, so it refuses instead.");
            }

            if (declaredRate == null && SampleRatePattern().Match(line) is { Success: true } match &&
                int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int rate) &&
                rate > 0)
            {
                declaredRate = rate;
                continue;
            }

            if (LooksLikeNumberColumns(line))
            {
                skippedNumeric++;
            }
        }

        if (sawDecimalPoint && sawDecimalComma)
        {
            throw new InvalidDataException(
                "The file writes some numbers with a decimal point and some with a " +
                "decimal comma, so one of the two must be a column separator — and " +
                "this reader expects ONE coefficient per line, first tap first.");
        }

        if (taps.Count == 0)
        {
            throw new InvalidDataException(
                skippedNumeric > 0
                    ? "No FIR coefficients were found. The file holds several numbers per " +
                      "line; this reader expects ONE coefficient per line, first tap first."
                    : "No FIR coefficients were found. Expected one coefficient per line, " +
                      "first tap first, with any header lines above them.");
        }

        return new FirFilter(taps, declaredRate);
    }

    private static void AddTap(List<double> taps, double tap)
    {
        if (taps.Count == FirFilter.MaximumTaps)
        {
            throw new InvalidDataException(
                $"The file holds more than {FirFilter.MaximumTaps} coefficients, " +
                "which is longer than any FIR filter this simulation accepts.");
        }

        taps.Add(tap);
    }

    private static bool IsComment(string line) =>
        line[0] is '*' or '#' or ';' or '%' || line.StartsWith("//", StringComparison.Ordinal);

    private static string Abbreviate(string line) =>
        line.Length <= 40 ? line : line[..37] + "...";

    private static bool LooksLikeNumberColumns(string line)
    {
        string[] columns = line.Split(
            [' ', '\t', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return columns.Length > 1 && columns.All(column =>
            double.TryParse(column, NumberStyles.Float, CultureInfo.InvariantCulture, out _));
    }

    [GeneratedRegex(@"^[+-]?\d*,\d+(?:[eE][+-]?\d+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex DecimalCommaPattern();

    [GeneratedRegex(@"sample\s*rate\D*?(\d{4,6})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SampleRatePattern();
}
