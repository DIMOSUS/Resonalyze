using System.Globalization;
using System.Text.RegularExpressions;

namespace Resonalyze.Dsp;

/// <summary>
/// Reads a FIR kernel out of the text formats the filter designers write: one
/// coefficient per line (rePhase, REW's impulse export, miniDSP's and most others'
/// <c>.txt</c>, the <c>.fir</c> the same tools name their exports).
/// </summary>
/// <remarks>
/// <para>
/// Every line that parses as one number is a tap, in file order. ABOVE the first
/// tap anything goes: the designers differ in what they write there (<c>* Impulse
/// Response data saved by REW</c>, <c>// rePhase</c>, nothing at all) and a reader
/// that pinned one dialect would refuse the next. A line with several numbers on it
/// up there is skipped, not read as its first or last column: a two-column file is
/// another format, and guessing the column would load a kernel nobody designed. The
/// message for a file with no taps says what the reader wanted.
/// </para>
/// <para>
/// ONCE THE TAPS BEGIN the reader is strict: a line that is not a number, and not a
/// comment by its first character (<c>*</c>, <c>//</c>, <c>#</c>, <c>;</c>,
/// <c>%</c>), refuses the whole file and names the line. Skipping it would be far
/// worse than refusing: a tap that goes missing shifts every tap after it by one
/// sample, which is a different filter with the same file name — and <c>0.3 0.4</c>
/// or <c>0.25 garbage</c> in the middle of a kernel is exactly the line a reader
/// must not guess about.
/// </para>
/// <para>
/// A decimal COMMA is a decimal separator, not a column separator: a tool run under
/// a locale that writes <c>0,5</c> exports the same kernel as one that writes
/// <c>0.5</c>, and a reader that skipped those lines would load a shorter kernel
/// with a hole where every fraction stood, and say nothing. So a line that is one
/// number with a single comma and no point (<c>-0,25</c>, <c>1,5e-3</c>) is a tap.
/// The price is the two-column file of bare integers (<c>1,5</c>), read here as
/// 1.5 — a shape no designer exports. A file that mixes the two conventions is
/// refused: one of them is a column separator there, and the reader cannot tell
/// which.
/// </para>
/// <para>
/// A header line stating the sample rate (<c>Sample rate: 48000</c>, in any spelling
/// with the number after it) is picked up as the kernel's DECLARED rate, purely so
/// the editors can warn when it is not the processor's. The taps are never
/// resampled — see <see cref="FirFilter"/>.
/// </para>
/// </remarks>
public static partial class FirFilterTextFile
{
    /// <summary>Parses the file's text. Throws <see cref="InvalidDataException"/> when it holds no kernel.</summary>
    public static FirFilter Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var taps = new List<double>();
        int? declaredRate = null;
        int skippedNumeric = 0;
        bool sawDecimalPoint = false;
        bool sawDecimalComma = false;
        // Line numbers count every line of the file, blank ones included, so the
        // refusal names the line an editor shows.
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

            // A header line of several numbers: counted so the refusal can name the
            // shape it saw.
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

    // The comment markers the designers' exports and hand-edited files use.
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

    // One number whose only comma is its decimal separator: digits either side of
    // it, a sign and an exponent allowed, nothing else on the line.
    [GeneratedRegex(@"^[+-]?\d*,\d+(?:[eE][+-]?\d+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex DecimalCommaPattern();

    [GeneratedRegex(@"sample\s*rate\D*?(\d{4,6})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SampleRatePattern();
}
