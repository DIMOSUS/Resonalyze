using System.Globalization;
using System.Text;

namespace Resonalyze.Dsp;

/// <summary>
/// A simple, self-describing CSV layout:
/// <code>
/// Preamp (dB),-6.0
/// Filter,Frequency (Hz),Gain (dB),Q
/// 1,600,6.0,4.0
/// </code>
/// Import is tolerant: the header row and comments are skipped, the preamp row is
/// recognised by its label, and each data row is read as frequency/gain/Q from its
/// numeric fields (a leading index column is ignored).
/// </summary>
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
        builder.AppendLine("Filter,Frequency (Hz),Gain (dB),Q");
        for (int i = 0; i < curve.Bands.Count; i++)
        {
            PeqBand band = curve.Bands[i];
            builder
                .Append((i + 1).ToString(CultureInfo.InvariantCulture))
                .Append(',')
                .Append(EqTextNumbers.Format(band.FrequencyHz, "0.###"))
                .Append(',')
                .Append(EqTextNumbers.Format(band.GainDb, "0.0"))
                .Append(',')
                .AppendLine(EqTextNumbers.Format(band.Q, "0.0"));
        }

        return builder.ToString();
    }

    public EqualizationCurve Import(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        double preampDb = 0;
        var bands = new List<PeqBand>();

        foreach (string rawLine in text.Split('\n'))
        {
            if (bands.Count >= EqualizationCurve.MaxBandCount)
            {
                break;
            }

            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            string[] fields = line.Split(',');

            if (fields.Length >= 2 &&
                fields[0].Trim().StartsWith("Preamp", StringComparison.OrdinalIgnoreCase))
            {
                foreach (string field in fields.Skip(1))
                {
                    if (EqTextNumbers.TryParse(field, out double gain))
                    {
                        preampDb = gain;
                        break;
                    }
                }

                continue;
            }

            var numbers = new List<double>();
            foreach (string field in fields)
            {
                if (EqTextNumbers.TryParse(field, out double value))
                {
                    numbers.Add(value);
                }
            }

            double frequencyHz;
            double gainDb;
            double q;
            if (numbers.Count >= 4)
            {
                // index, frequency, gain, Q
                frequencyHz = numbers[1];
                gainDb = numbers[2];
                q = numbers[3];
            }
            else if (numbers.Count == 3)
            {
                // frequency, gain, Q
                frequencyHz = numbers[0];
                gainDb = numbers[1];
                q = numbers[2];
            }
            else
            {
                continue;
            }

            if (!double.IsFinite(frequencyHz) || frequencyHz <= 0 ||
                !double.IsFinite(q) || q <= 0 ||
                !double.IsFinite(gainDb))
            {
                continue;
            }

            bands.Add(new PeqBand(frequencyHz, q, gainDb));
        }

        return new EqualizationCurve(bands, preampDb);
    }
}
