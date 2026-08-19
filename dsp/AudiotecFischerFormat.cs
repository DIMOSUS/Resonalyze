using System.Globalization;
using System.Text;

namespace Resonalyze.Dsp;

/// <summary>
/// The Audiotec-Fischer "Full EQ (30 bands)" bank: the tab-separated block REW
/// exports with its Audiotec Fischer equaliser selected, and the file the HELIX /
/// MATCH / BRAX DSP PC-Tool imports into one channel's EQ:
/// <code>
/// Audiotec_Fischer_Full_EQ_(30_bands)
/// Number	Enabled	Control	Type	Frequency(Hz)	Gain(dB)	Q	Bandwidth(Hz)	TargetT60(ms)
/// 1	True	Auto	PK	80.0	-2.0	4.00	20
/// 2	True	Auto	LS_Q	50.0	-1.0	0.70
/// 3	True	Auto	None
/// </code>
/// The bank is the channel's slot table, so it always holds exactly
/// <see cref="SlotCount"/> rows and an unused slot is a row of type <c>None</c>.
/// <c>PK</c> is a bell; <c>LS_Q</c> / <c>HS_Q</c> are the shelves stated with a
/// centre frequency and a Q — the shelf REW's "LS Q" / "HS Q" types describe and
/// this library realizes, so they travel unchanged. <c>Bandwidth(Hz)</c> is REW's
/// companion figure for a bell, <c>Fc / Q</c> — the RBJ relation, consistent with
/// the processors' place in the <see cref="PeqQConvention"/> crib — and
/// <c>TargetT60(ms)</c> carries REW's modal-filter target; import ignores both
/// (Q wins, and a row carrying a bandwidth but no Q reads Q = Fc / BW).
/// <c>Modal</c> is REW's room-mode filter: a bell whose width REW derives from that
/// T60, written into this bank with the Fc / Gain / Q of the filter it realizes, so
/// it is read as a <see cref="PeqBandType.Peaking"/> band like any other bell —
/// dropping it would quietly widen a hole in the tune. The T60 itself is REW's
/// optimizer metadata, has no slot in the processor, and is not kept.
/// </summary>
/// <remarks>
/// The bank holds equalization only. Crossovers, delay, polarity and the phase
/// stage live in other PC-Tool fields, and so does the channel gain: the file has
/// no slot for a preamp, so <see cref="CarriesPreamp"/> is false — an export leaves
/// the preamp out and an import reads 0, and the caller is expected to say so, as
/// it does for a dropped shelf. <c>AP1</c> / <c>AP2</c> rows — the PC-Tool's first-
/// and second-order all-pass slots — move phase only, which a magnitude profile has
/// no band shape for; they are skipped on import and never written. A row whose
/// <c>Enabled</c> reads <c>False</c> is skipped like an OFF filter elsewhere.
///
/// The row shapes follow REW's export byte for byte — a bell row ends in the empty
/// TargetT60 cell, a shelf row ends at its Q, an unused row at its <c>None</c> —
/// because that is the layout the PC-Tool is known to accept. Parsing is
/// defensive: a UTF-8 BOM, CR/LF, ragged rows, trailing tabs and spaces in place
/// of tabs are all tolerated (numbers read as in the other text formats), and the
/// file is recognised by its bank header together with a COMPLETE slot table —
/// exactly <see cref="SlotCount"/> rows numbered 1..30, in order. An empty bank
/// (thirty <c>None</c> rows) is valid and neutral; a truncated or renumbered one is
/// NOT recognised, because importing it as "no bands" would silently replace the
/// user's EQ with nothing, and a bank with more rows than the channel has slots
/// would import what this format then refuses to export.
/// </remarks>
public sealed class AudiotecFischerFormat : IEqProfileFormat
{
    /// <summary>
    /// The slots a bank holds — the per-channel band budget of these processors,
    /// two short of <see cref="EqualizationCurve.MaxBandCount"/>. A curve with more
    /// bands cannot be written honestly, so <see cref="Export"/> refuses it rather
    /// than dropping bands.
    /// </summary>
    public const int SlotCount = 30;

    private const string BankHeader = "Audiotec_Fischer_Full_EQ_(30_bands)";
    private const string ColumnHeader =
        "Number\tEnabled\tControl\tType\tFrequency(Hz)\tGain(dB)\tQ\tBandwidth(Hz)\tTargetT60(ms)\t";

    private const string BellType = "PK";
    // REW's room-mode filter: a bell in this bank, its T60 only optimizer metadata.
    private const string ModalType = "Modal";
    private const string LowShelfType = "LS_Q";
    private const string HighShelfType = "HS_Q";
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
                .Append(EqTextNumbers.Format(band.GainDb, "0.0#"))
                .Append('\t')
                .Append(EqTextNumbers.Format(band.Q, "0.00##"));
            if (band.Type.IsShelving())
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

        bool headerSeen = false;
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

            if (line.Contains(BankHeader, StringComparison.OrdinalIgnoreCase) ||
                IsColumnHeader(line))
            {
                headerSeen = true;
                continue;
            }

            if (!TryParseSlot(line, out int slotNumber, out PeqBand? band))
            {
                // Not a slot row at all: this is not the bank's table.
                tableIntact = false;
                continue;
            }

            slotsSeen++;
            // The bank is a fixed table: slot n is the nth row. A file that skips,
            // repeats or renumbers rows is not the channel's slot table, whatever
            // its header says.
            tableIntact &= slotNumber == slotsSeen && slotsSeen <= SlotCount;
            if (band.HasValue)
            {
                bands.Add(band.Value);
            }
        }

        // Recognition is the caller's only defence: a "successful" import replaces
        // the EQ on screen, so an incomplete or over-long bank must fail here
        // rather than arrive as an empty (or unexportable) curve.
        bool recognized = headerSeen && tableIntact && slotsSeen == SlotCount;
        curve = new EqualizationCurve(recognized ? bands : Array.Empty<PeqBand>());
        return recognized;
    }

    private static string TypeToken(PeqBandType type) => type switch
    {
        PeqBandType.LowShelf => LowShelfType,
        PeqBandType.HighShelf => HighShelfType,
        _ => BellType
    };

    // "Number<tab>Enabled<tab>Control<tab>Type ..." — the second line of every export.
    private static bool IsColumnHeader(string line)
    {
        string[] fields = SplitRow(line);
        return fields.Length >= 4 &&
            fields[0].Equals("Number", StringComparison.OrdinalIgnoreCase) &&
            fields[1].Equals("Enabled", StringComparison.OrdinalIgnoreCase) &&
            fields[3].Equals("Type", StringComparison.OrdinalIgnoreCase);
    }

    // Reads one slot row: whether the line IS a slot of this bank (its number), and
    // the band it holds, if any. Empty (None), all-pass, disabled, unknown-type and
    // malformed rows are slots holding no band — a bank is a fixed table, so most
    // rows of a real file are expected to be empty. Telling "no band here" from
    // "not this bank's table" is what lets an incomplete file be refused.
    private static bool TryParseSlot(string line, out int slotNumber, out PeqBand? band)
    {
        band = null;
        slotNumber = 0;

        string[] fields = SplitRow(line);
        if (fields.Length < 4 ||
            !int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out slotNumber) ||
            fields[3].Length == 0)
        {
            return false;
        }

        if (fields.Length < 6 ||
            fields[1].Equals("False", StringComparison.OrdinalIgnoreCase))
        {
            return true;
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
        else
        {
            // None, AP1, AP2 or anything else this layout may grow: a slot, no band.
            return true;
        }

        if (!EqTextNumbers.TryParse(fields[4], out double frequencyHz) ||
            !EqTextNumbers.TryParse(fields[5], out double gainDb))
        {
            return true;
        }

        // The Q cell may be blank on a hand-edited row: a bell can still be read
        // from REW's bandwidth column, a shelf comes in at the default knee.
        if (!EqTextNumbers.TryParse(FieldAt(fields, 6), out double q) || q <= 0)
        {
            if (type == PeqBandType.Peaking)
            {
                if (!EqTextNumbers.TryParse(FieldAt(fields, 7), out double bandwidthHz) ||
                    bandwidthHz <= 0)
                {
                    return true;
                }

                q = frequencyHz / bandwidthHz;
            }
            else
            {
                q = PeqTextFile.DefaultShelfQ;
            }
        }

        if (!double.IsFinite(frequencyHz) || frequencyHz <= 0 ||
            !double.IsFinite(q) || q <= 0 ||
            !double.IsFinite(gainDb))
        {
            return true;
        }

        band = new PeqBand(frequencyHz, q, gainDb, type);
        return true;
    }

    // Tab-separated as exported; a copy pasted through something that turned the
    // tabs into spaces still splits, since no cell of this layout contains a space.
    private static string[] SplitRow(string line) =>
        line.Contains('\t')
            ? line.Split('\t').Select(field => field.Trim()).ToArray()
            : line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    private static string? FieldAt(string[] fields, int index) =>
        index < fields.Length ? fields[index] : null;
}
