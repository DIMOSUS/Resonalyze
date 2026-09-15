using System.Globalization;

namespace Resonalyze;

internal static class SheetFormat
{
    public static string Signed(double value) =>
        value.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture);

    public static string Number(double value, string format) =>
        value.ToString(format, CultureInfo.InvariantCulture);
}
