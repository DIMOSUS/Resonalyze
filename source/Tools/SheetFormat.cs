using System.Globalization;

namespace Resonalyze;

/// <summary>
/// Invariant-culture number formatting shared by the text and PDF tuning sheets.
/// </summary>
internal static class SheetFormat
{
    public static string Signed(double value) =>
        value.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture);

    public static string Number(double value, string format) =>
        value.ToString(format, CultureInfo.InvariantCulture);
}
