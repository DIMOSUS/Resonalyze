using System.Globalization;
using System.Text;

namespace Resonalyze.Dsp;

/// <summary>
/// Audiotec-Fischer "Full EQ (30 bands)" bank (REW export, HELIX/MATCH/BRAX PC-Tool import): tab-separated, exactly
/// <see cref="SlotCount"/> rows, unused slots typed <c>None</c>. PK = bell, LS_Q/HS_Q = Q shelves, Modal = REW room-mode bell (T60 dropped),
/// AP1/AP2 = all-pass slots. Bandwidth and TargetT60 are ignored on import (Q wins; no Q reads Fc / BW).
/// </summary>
/// <remarks>EQ only, no preamp slot (<see cref="CarriesPreamp"/> is false). Row shapes follow REW's export byte for byte (known to import).
/// Recognised only with the bank header AND a complete 1..30 slot table: a truncated bank would silently replace the user's EQ.</remarks>
public sealed class AudiotecFischerFormat : IEqProfileFormat
{
    /// <summary>Export refuses curves with more bands rather than dropping them.</summary>
    public const int SlotCount = 30;

    private const string BankHeader = "Audiotec_Fischer_Full_EQ_(30_bands)";
    private const string ColumnHeader =
        "Number\tEnabled\tControl\tType\tFrequency(Hz)\tGain(dB)\tQ\tBandwidth(Hz)\tTargetT60(ms)\t";

    private const string BellType = "PK";
    private const string ModalType = "Modal";
    private const string LowShelfType = "LS_Q";
    private const string HighShelfType = "HS_Q";
    private const string FirstOrderAllPassType = "AP1";
    private const string SecondOrderAllPassType = "AP2";
    private const string EmptyType = "None";

    public string Name => "Audiotec Fischer 30-band bank";
    public string Extension => "txt";
    public bool CanImport => true;
    public bool CanExport => true;
    public bool CarriesPreamp => false;

    public string Export(EqualizationCurve curve)
    {
        ArgumentNullException.ThrowIfNull(curve);
        if (curve.Bands.Count > SlotCount)
        {
            throw new ArgumentException(
                $"An Audiotec-Fischer bank holds {SlotCount} slots; this curve has " +
                $"{curve.Bands.Count} bands.",
                nameof(curve));
        }

        var builder = new StringBuilder();
        builder.AppendLine(BankHeader);
        builder.AppendLine(ColumnHeader);
        for (int slot = 1; slot <= SlotCount; slot++)
        {
            builder
                .Append(slot.ToString(CultureInfo.InvariantCulture))
                .Append("\tTrue\tAuto\t");
            if (slot > curve.Bands.Count)
            {
                builder.Append(EmptyType).AppendLine("\t");
                continue;
            }

            PeqBand band = curve.Bands[slot - 1];
            builder
                .Append(TypeToken(band.Type))
                .Append('\t')
                .Append(EqTextNumbers.Format(band.FrequencyHz, "0.0##"))
                .Append('\t')
                // All-pass gain cell written as 0.0 to keep the uniform Type/Frequency/Gain/Q prefix.
                .Append(EqTextNumbers.Format(band.Type.IsAllPass() ? 0 : band.GainDb, "0.0#"))
                .Append('\t')
                .Append(EqTextNumbers.Format(band.Q, "0.00##"));
            if (band.Type.IsShelving() || band.Type.IsAllPass())
            {
                builder.AppendLine();
                continue;
            }

            builder
                .Append('\t')
                .Append(EqTextNumbers.Format(band.FrequencyHz / band.Q, "0.##"))
                .AppendLine("\t");
        }

        return builder.ToString();
    }

    public bool TryImport(string text, out EqualizationCurve curve)
    {
        ArgumentNullException.ThrowIfNull(text);

        bool bankHeaderSeen = false;
        int slotsSeen = 0;
        bool tableIntact = true;
        var bands = new List<PeqBand>();

        foreach (string rawLine in text.TrimStart('\uFEFF').Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.Contains(BankHeader, StringComparison.OrdinalIgnoreCase))
            {
                bankHeaderSeen = true;
                continue;
            }

            if (IsColumnHeader(line))
            {
                // REW writes this column line above every equaliser; only the bank header identifies the table.
                continue;
            }

            SlotKind kind = ReadSlot(line, out int slotNumber, out PeqBand band);
            if (kind == SlotKind.NotASlot)
            {
                tableIntact = false;
                continue;
            }

            slotsSeen++;
            // An unreadable filter slot would be silently dropped: refuse the file.
            tableIntact &= kind != SlotKind.Unreadable;
            tableIntact &= slotNumber == slotsSeen && slotsSeen <= SlotCount;
            if (kind == SlotKind.Band)
            {
                bands.Add(band);
            }
        }

        // A successful import replaces the EQ on screen, so incomplete or unheadered banks must fail here.
        bool recognized = bankHeaderSeen && tableIntact && slotsSeen == SlotCount;
        curve = new EqualizationCurve(recognized ? bands : Array.Empty<PeqBand>());
        return recognized;
    }

    private static string TypeToken(PeqBandType type) => type switch
    {
        PeqBandType.LowShelf => LowShelfType,
        PeqBandType.HighShelf => HighShelfType,
        PeqBandType.AllPassFirstOrder => FirstOrderAllPassType,
        PeqBandType.AllPassSecondOrder => SecondOrderAllPassType,
        _ => BellType
    };

    private static bool IsColumnHeader(string line)
    {
        string[] fields = SplitRow(line);
        return fields.Length >= 4 &&
            fields[0].Equals("Number", StringComparison.OrdinalIgnoreCase) &&
            fields[1].Equals("Enabled", StringComparison.OrdinalIgnoreCase) &&
            fields[3].Equals("Type", StringComparison.OrdinalIgnoreCase);
    }

    private enum SlotKind
    {
        NotASlot,

        Empty,

        Band,

        Unreadable
    }

    private static SlotKind ReadSlot(string line, out int slotNumber, out PeqBand band)
    {
        band = default;

        string[] fields = SplitRow(line);
        if (fields.Length < 4 ||
            !int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out slotNumber) ||
            fields[3].Length == 0)
        {
            slotNumber = 0;
            return SlotKind.NotASlot;
        }

        if (fields[1].Equals("False", StringComparison.OrdinalIgnoreCase))
        {
            return SlotKind.Empty;
        }

        if (fields[3].Equals(EmptyType, StringComparison.OrdinalIgnoreCase))
        {
            return SlotKind.Empty;
        }

        PeqBandType type;
        if (fields[3].Equals(BellType, StringComparison.OrdinalIgnoreCase) ||
            fields[3].Equals(ModalType, StringComparison.OrdinalIgnoreCase))
        {
            type = PeqBandType.Peaking;
        }
        else if (fields[3].Equals(LowShelfType, StringComparison.OrdinalIgnoreCase))
        {
            type = PeqBandType.LowShelf;
        }
        else if (fields[3].Equals(HighShelfType, StringComparison.OrdinalIgnoreCase))
        {
            type = PeqBandType.HighShelf;
        }
        else if (fields[3].Equals(FirstOrderAllPassType, StringComparison.OrdinalIgnoreCase))
        {
            type = PeqBandType.AllPassFirstOrder;
        }
        else if (fields[3].Equals(SecondOrderAllPassType, StringComparison.OrdinalIgnoreCase))
        {
            type = PeqBandType.AllPassSecondOrder;
        }
        else
        {
            // Unknown enabled type may shape magnitude; treating it as empty would change the tune.
            return SlotKind.Unreadable;
        }

        if (fields.Length < 5 ||
            !EqTextNumbers.TryParse(fields[4], out double frequencyHz))
        {
            return SlotKind.Unreadable;
        }

        double gainDb = 0;
        if (!type.IsAllPass() &&
            (fields.Length < 6 || !EqTextNumbers.TryParse(fields[5], out gainDb)))
        {
            return SlotKind.Unreadable;
        }

        // Blank Q: bell reads bandwidth, shelf takes default knee, AP1 has none; AP2 without Q is unreadable (Q is the phase turn).
        double q;
        if (type == PeqBandType.AllPassFirstOrder)
        {
            q = 1.0;
        }
        else if (!EqTextNumbers.TryParse(FieldAt(fields, 6), out q) || q <= 0)
        {
            if (type == PeqBandType.Peaking)
            {
                if (!EqTextNumbers.TryParse(FieldAt(fields, 7), out double bandwidthHz) ||
                    bandwidthHz <= 0)
                {
                    return SlotKind.Unreadable;
                }

                q = frequencyHz / bandwidthHz;
            }
            else if (type.IsShelving())
            {
                q = PeqTextFile.DefaultShelfQ;
            }
            else
            {
                return SlotKind.Unreadable;
            }
        }

        if (!double.IsFinite(frequencyHz) || frequencyHz <= 0 ||
            !double.IsFinite(q) || q <= 0 ||
            !double.IsFinite(gainDb))
        {
            return SlotKind.Unreadable;
        }

        band = new PeqBand(frequencyHz, q, gainDb, type);
        return SlotKind.Band;
    }

    // No cell contains a space, so space-converted tabs still split.
    private static string[] SplitRow(string line) =>
        line.Contains('\t')
            ? line.Split('\t').Select(field => field.Trim()).ToArray()
            : line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    private static string? FieldAt(string[] fields, int index) =>
        index < fields.Length ? fields[index] : null;
}
