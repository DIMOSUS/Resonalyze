using System.Globalization;
using System.Text;

namespace Resonalyze.Dsp;

/// <summary>Self-describing CSV (<c>Preamp (dB),-6.0</c> then <c>Filter,Frequency (Hz),Gain (dB),Q,Type</c> rows). Tolerant import:
/// optional index column; a missing type column (pre-shelf files) reads every row as a bell.</summary>
public sealed class GenericCsvFormat : IEqProfileFormat
{
    public string Name => "Generic CSV";
    public string Extension => "csv";
    public bool CanImport => true;
    public bool CanExport => true;

    public string Export(EqualizationCurve curve)
    {
        ArgumentNullException.ThrowIfNull(curve);

        var builder = new StringBuilder();
        builder.Append("Preamp (dB),").AppendLine(EqTextNumbers.Format(curve.PreampDb, "0.0"));
        builder.AppendLine("Filter,Frequency (Hz),Gain (dB),Q,Type");
        for (int i = 0; i < curve.Bands.Count; i++)
        {
            PeqBand band = curve.Bands[i];
            builder
                .Append((i + 1).ToString(CultureInfo.InvariantCulture))
                .Append(',')
                .Append(EqTextNumbers.Format(band.FrequencyHz, "0.###"))
                .Append(',')
                // All-pass writes 0.0 gain: the slot may still hold its pre-all-pass gain.
                .Append(EqTextNumbers.Format(
                    band.Type.IsAllPass() ? 0 : band.GainDb, "0.0"))
                .Append(',')
                .Append(EqTextNumbers.Format(band.Q, EqTextNumbers.QFormat))
                .Append(',')
                .AppendLine(TypeToken(band.Type));
        }

        return builder.ToString();
    }

    public bool TryImport(string text, out EqualizationCurve curve)
    {
        ArgumentNullException.ThrowIfNull(text);

        string[] lines = text.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToArray();
        // Excel under a decimal-comma locale separates with semicolons; EqTextNumbers then reads the comma as a decimal.
        char separator = lines.Any(line => line.Contains(';')) ? ';' : ',';

        double preampDb = 0;
        bool recognized = false;
        var rows = new List<string[]>();
        foreach (string line in lines)
        {
            string[] fields = line.Split(separator);

            if (fields.Length >= 2 &&
                fields[0].Trim().StartsWith("Preamp", StringComparison.OrdinalIgnoreCase))
            {
                foreach (string field in fields.Skip(1))
                {
                    if (EqTextNumbers.TryParse(field, out double gain))
                    {
                        preampDb = gain;
                        recognized = true;
                        break;
                    }
                }

                continue;
            }

            rows.Add(fields);
        }

        // The index column is the file's layout, so a blank or junk cell skips its row rather than shifting the columns.
        int indexed = rows.Count(fields => TryReadNumbers(fields, 1, out _, out _, out _));
        int unindexed = rows.Count(fields =>
            !TryReadNumbers(fields, 1, out _, out _, out _) && TryReadNumbers(fields, 0, out _, out _, out _));
        int first = indexed >= unindexed ? 1 : 0;

        var bands = new List<PeqBand>();
        foreach (string[] fields in rows)
        {
            if (bands.Count >= EqualizationCurve.MaxBandCount)
            {
                break;
            }

            if (!TryReadNumbers(fields, first, out double frequencyHz, out double gainDb, out double q))
            {
                continue;
            }

            if (!double.IsFinite(frequencyHz) || frequencyHz <= 0 ||
                !double.IsFinite(q) || q <= 0 ||
                !double.IsFinite(gainDb))
            {
                continue;
            }

            bands.Add(new PeqBand(frequencyHz, q, gainDb, ReadType(fields)));
            recognized = true;
        }

        curve = new EqualizationCurve(bands, preampDb);
        return recognized;
    }

    private static bool TryReadNumbers(
        string[] fields, int first, out double frequencyHz, out double gainDb, out double q)
    {
        frequencyHz = 0;
        gainDb = 0;
        q = 0;
        return fields.Length >= first + 3 &&
            EqTextNumbers.TryParse(fields[first], out frequencyHz) &&
            EqTextNumbers.TryParse(fields[first + 1], out gainDb) &&
            EqTextNumbers.TryParse(fields[first + 2], out q);
    }

    private static string TypeToken(PeqBandType type) => type switch
    {
        PeqBandType.LowShelf => "LS",
        PeqBandType.HighShelf => "HS",
        PeqBandType.AllPassFirstOrder => "AP1",
        PeqBandType.AllPassSecondOrder => "AP2",
        _ => "PK"
    };

    // Type found by keyword, not column index, so the optional index column does not matter.
    private static PeqBandType ReadType(string[] fields)
    {
        foreach (string field in fields)
        {
            string token = field.Trim();
            if (token.Equals("LS", StringComparison.OrdinalIgnoreCase))
            {
                return PeqBandType.LowShelf;
            }

            if (token.Equals("HS", StringComparison.OrdinalIgnoreCase))
            {
                return PeqBandType.HighShelf;
            }

            if (token.Equals("AP1", StringComparison.OrdinalIgnoreCase))
            {
                return PeqBandType.AllPassFirstOrder;
            }

            if (token.Equals("AP2", StringComparison.OrdinalIgnoreCase))
            {
                return PeqBandType.AllPassSecondOrder;
            }
        }

        return PeqBandType.Peaking;
    }
}
