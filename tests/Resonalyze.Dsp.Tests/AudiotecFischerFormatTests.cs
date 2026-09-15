using System.Text;

namespace Resonalyze.Dsp.Tests;

// Audiotec-Fischer bank: real REW export shape, written rows, and what the layout cannot carry (preamp, >30 bands).
public sealed class AudiotecFischerFormatTests
{
    // Real REW export of a 20-band channel, byte for byte (trailing tabs, ragged shelves, 4-digit bandwidths).
    private const string RewExport =
        "Audiotec_Fischer_Full_EQ_(30_bands)\r\n" +
        "Number\tEnabled\tControl\tType\tFrequency(Hz)\tGain(dB)\tQ\tBandwidth(Hz)\tTargetT60(ms)\t\r\n" +
        "1\tTrue\tAuto\tPK\t30.0\t-2.0\t3.00\t10.00\t\r\n" +
        "2\tTrue\tAuto\tPK\t45.0\t1.5\t3.00\t15.00\t\r\n" +
        "3\tTrue\tManual\tLS_Q\t50.0\t-1.0\t0.7\r\n" +
        "4\tTrue\tAuto\tPK\t80.0\t-2.0\t4.00\t20.00\t\r\n" +
        "5\tTrue\tAuto\tPK\t120.0\t2.0\t3.00\t40.00\t\r\n" +
        "6\tTrue\tAuto\tPK\t200.0\t-3.0\t2.00\t100.0\t\r\n" +
        "7\tTrue\tAuto\tPK\t315.0\t2.0\t3.00\t105.0\t\r\n" +
        "8\tTrue\tAuto\tPK\t500.0\t-2.0\t4.00\t125.0\t\r\n" +
        "9\tTrue\tAuto\tPK\t630.0\t1.5\t5.00\t126.0\t\r\n" +
        "10\tTrue\tAuto\tPK\t800.0\t-2.5\t5.00\t160.0\t\r\n" +
        "11\tTrue\tAuto\tPK\t1000.0\t2.0\t2.00\t500.0\t\r\n" +
        "12\tTrue\tAuto\tPK\t1250.0\t-2.0\t5.00\t250.0\t\r\n" +
        "13\tTrue\tAuto\tPK\t1600.0\t1.5\t4.00\t400.0\t\r\n" +
        "14\tTrue\tAuto\tPK\t2000.0\t-3.0\t5.00\t400.0\t\r\n" +
        "15\tTrue\tAuto\tPK\t2500.0\t2.0\t3.00\t833.0\t\r\n" +
        "16\tTrue\tAuto\tPK\t3150.0\t-2.0\t4.00\t787.0\t\r\n" +
        "17\tTrue\tManual\tPK\t5000.0\t1.0\t3.00\t1666\t\r\n" +
        "18\tTrue\tManual\tPK\t8000.0\t-2.0\t2.00\t4000\t\r\n" +
        "19\tTrue\tAuto\tPK\t12500.0\t-1.5\t2.00\t6250\t\r\n" +
        "20\tTrue\tManual\tHS_Q\t10000.0\t-1.0\t0.7\r\n" +
        "21\tTrue\tAuto\tNone\t\r\n" +
        "22\tTrue\tAuto\tNone\t\r\n" +
        "23\tTrue\tAuto\tNone\t\r\n" +
        "24\tTrue\tAuto\tNone\t\r\n" +
        "25\tTrue\tAuto\tNone\t\r\n" +
        "26\tTrue\tAuto\tNone\t\r\n" +
        "27\tTrue\tAuto\tNone\t\r\n" +
        "28\tTrue\tAuto\tNone\t\r\n" +
        "29\tTrue\tAuto\tNone\t\r\n" +
        "30\tTrue\tAuto\tNone\t\r\n";

    // Import() is a default interface member, not reachable through the class.
    private static readonly IEqProfileFormat Format = new AudiotecFischerFormat();

    private static EqualizationCurve Mixed() => new(
        new[]
        {
            new PeqBand(80, 0.7, 4.5, PeqBandType.LowShelf),
            new PeqBand(1_000, 4.0, -6.0),
            new PeqBand(6_300, 1.1, -3.5, PeqBandType.HighShelf)
        },
        -6.5);

    private static string[] Lines(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r'))
            .ToArray();

    // The layout is a fixed 30-slot table (see Import_RefusesATruncatedOrRenumberedBank).
    private static string Bank(params string[] rows)
    {
        var text = new StringBuilder("Audiotec_Fischer_Full_EQ_(30_bands)\n");
        for (int slot = 1; slot <= AudiotecFischerFormat.SlotCount; slot++)
        {
            text.AppendLine(slot <= rows.Length
                ? $"{slot}\t{rows[slot - 1]}"
                : $"{slot}\tTrue\tAuto\tNone\t");
        }

        return text.ToString();
    }

    [Fact]
    public void IsRegisteredForImportAndExport()
    {
        Assert.Contains(EqProfileFormats.Importable, format => format is AudiotecFischerFormat);
        Assert.Contains(EqProfileFormats.Exportable, format => format is AudiotecFischerFormat);
    }

    [Fact]
    public void Import_ReadsARealRewExport()
    {
        Assert.True(Format.TryImport(RewExport, out EqualizationCurve curve));

        Assert.Equal(20, curve.Bands.Count);
        Assert.Equal(0, curve.PreampDb);

        Assert.Equal(new PeqBand(30, 3.0, -2.0), curve.Bands[0]);
        Assert.Equal(new PeqBand(50, 0.7, -1.0, PeqBandType.LowShelf), curve.Bands[2]);
        Assert.Equal(new PeqBand(5_000, 3.0, 1.0), curve.Bands[16]);
        Assert.Equal(new PeqBand(10_000, 0.7, -1.0, PeqBandType.HighShelf), curve.Bands[19]);
        Assert.Equal(18, curve.Bands.Count(band => band.Type == PeqBandType.Peaking));
    }

    [Fact]
    public void Import_QWinsOverTheBandwidthColumn()
    {
        // Q is authoritative; a disagreeing bandwidth (500 Hz vs Q 4 at 1 kHz) must not move the band.
        EqualizationCurve curve = Format.Import(Bank("True\tAuto\tPK\t1000\t-3\t4.00\t500\t"));

        Assert.Equal(4.0, Assert.Single(curve.Bands).Q, 6);
    }

    [Fact]
    public void Import_ReadsQFromTheBandwidthWhenTheQCellIsBlank()
    {
        EqualizationCurve curve = Format.Import(Bank("True\tAuto\tPK\t1000\t-3\t\t250\t"));

        Assert.Equal(4.0, Assert.Single(curve.Bands).Q, 6);
    }

    [Fact]
    public void Import_ReadsAShelfWithoutAQAtTheDefaultKnee()
    {
        PeqBand band = Assert.Single(Format.Import(Bank("True\tAuto\tHS_Q\t8000\t-2")).Bands);

        Assert.Equal(PeqBandType.HighShelf, band.Type);
        Assert.Equal(PeqTextFile.DefaultShelfQ, band.Q, 9);
    }

    [Fact]
    public void Import_SkipsTheSlotsThatHoldNoBand()
    {
        EqualizationCurve curve = Format.Import(Bank(
            "True\tAuto\tPK\t100\t-2\t2.00\t50\t",
            "False\tAuto\tPK\t200\t-2\t2.00\t100\t",
            "False\tAuto\tPK\tnot-a-number\t-2\t2.00",
            "True\tAuto\tNone\t",
            "True\tAuto\tPK\t800\t-1\t1.00\t800\t"));

        Assert.Equal(2, curve.Bands.Count);
        Assert.Equal(100, curve.Bands[0].FrequencyHz);
        Assert.Equal(800, curve.Bands[1].FrequencyHz);
    }

    [Fact]
    public void Import_ReadsTheAllPassSlots()
    {
        // AP1/AP2 map to the all-pass band types; gain cell ignored, first order needs no Q, AP2 reads its own Q.
        EqualizationCurve curve = Format.Import(Bank(
            "True\tAuto\tAP1\t300",
            "True\tAuto\tAP2\t400\t\t1.50",
            "True\tAuto\tAP2\t500\t0.0\t2.00"));

        Assert.Equal(3, curve.Bands.Count);
        Assert.Equal(new PeqBand(300, 1.0, 0, PeqBandType.AllPassFirstOrder), curve.Bands[0]);
        Assert.Equal(new PeqBand(400, 1.5, 0, PeqBandType.AllPassSecondOrder), curve.Bands[1]);
        Assert.Equal(new PeqBand(500, 2.0, 0, PeqBandType.AllPassSecondOrder), curve.Bands[2]);
    }

    [Fact]
    public void Import_RefusesASecondOrderAllPassWithoutAQ()
    {
        // An AP2 without Q would be misread, so the bank is refused.
        Assert.False(Format.TryImport(
            Bank("True\tAuto\tAP2\t400\t0.0"), out EqualizationCurve refused));
        Assert.Empty(refused.Bands);
    }

    [Fact]
    public void Export_RoundTripsTheAllPassSlots()
    {
        var curve = new EqualizationCurve(new[]
        {
            new PeqBand(300, 1.0, 0, PeqBandType.AllPassFirstOrder),
            new PeqBand(400, 1.5, 6, PeqBandType.AllPassSecondOrder)
        });

        string text = Format.Export(curve);
        Assert.Contains("AP1\t300.0\t0.0\t1.00", text);
        Assert.Contains("AP2\t400.0\t0.0\t1.50", text);

        Assert.True(Format.TryImport(text, out EqualizationCurve reread));
        Assert.Equal(new PeqBand(300, 1.0, 0, PeqBandType.AllPassFirstOrder), reread.Bands[0]);
        Assert.Equal(new PeqBand(400, 1.5, 0, PeqBandType.AllPassSecondOrder), reread.Bands[1]);
    }

    [Fact]
    public void Import_RefusesASlotThatClaimsAFilterItCannotRead()
    {
        // Unreadable or unknown enabled rows would silently change a device tune: refuse the file.
        foreach (string row in new[]
        {
            "True\tAuto\tPK\tnot-a-number\t-2\t2.00\t50\t",   // unreadable centre
            "True\tAuto\tPK\t600\t-2\t\t\t",                  // neither Q nor bandwidth
            "True\tAuto\tPK\t600\t-2\t0\t0\t",                // a width of zero
            "True\tAuto\tHS_Q\t8000\tnot-a-number\t0.7",        // unreadable gain
            "True\tAuto\tNotch\t1000\t-6\t4.00\t250\t",       // a magnitude filter we do not model
        })
        {
            Assert.False(Format.TryImport(Bank(row), out EqualizationCurve refused), row);
            Assert.Empty(refused.Bands);
        }

        Assert.True(Format.TryImport(
            Bank("False\tAuto\tNotch\t1000\t-6\t4.00\t250\t"), out EqualizationCurve disabled));
        Assert.Empty(disabled.Bands);
    }

    [Fact]
    public void Import_ToleratesABomCrLfAndSpacesForTabs()
    {
        // No cell contains a space, so columns survive tab-to-space flattening.
        string text =
            "\uFEFFAudiotec_Fischer_Full_EQ_(30_bands)\r\n" +
            "Number Enabled Control Type Frequency(Hz) Gain(dB) Q Bandwidth(Hz) TargetT60(ms)\r\n" +
            "1 True Auto PK 2504.0 -12.2 1.27 1972\r\n" +
            "2 True Manual LS_Q 44.3 -11.0 0.7\r\n" +
            string.Concat(Enumerable.Range(3, AudiotecFischerFormat.SlotCount - 2)
                .Select(slot => $"{slot} True Auto None\r\n"));

        Assert.True(Format.TryImport(text, out EqualizationCurve curve));

        Assert.Equal(2, curve.Bands.Count);
        Assert.Equal(new PeqBand(2504, 1.27, -12.2), curve.Bands[0]);
        Assert.Equal(new PeqBand(44.3, 0.7, -11.0, PeqBandType.LowShelf), curve.Bands[1]);
    }

    [Fact]
    public void Import_RecognisesAnEmptyBankAndNotForeignText()
    {
        string empty = "Audiotec_Fischer_Full_EQ_(30_bands)\n" +
            string.Concat(Enumerable.Range(1, 30).Select(slot => $"{slot}\tTrue\tAuto\tNone\t\n"));
        Assert.True(Format.TryImport(empty, out EqualizationCurve curve));
        Assert.Empty(curve.Bands);

        Assert.False(Format.TryImport("Preamp: -6.0 dB\nFilter 1: ON PK Fc 600 Hz Gain 6.0 dB Q 4.0\n", out _));
        Assert.False(Format.TryImport("Preamp (dB),-6.0\nFilter,Frequency (Hz),Gain (dB),Q,Type\n1,600,6.0,4.0,PK\n", out _));
        Assert.False(Format.TryImport(string.Empty, out _));
    }

    [Fact]
    public void Import_ReadsARewModalRowAsABell()
    {
        // Modal rows realize a bell; read the Q column (120 Hz, -6 dB, T60 300 ms is Q 11.59, not pi*f0*T60/ln(1000)).
        EqualizationCurve curve = Format.Import(Bank(
            "True\tAuto\tModal\t120.0\t-6.0\t11.59\t10.35\t300\t",
            "True\tAuto\tModal\t45.0\t-4.5\t9.48\t4.75\t600\t",
            "True\tAuto\tPK\t200.0\t-2.0\t4.00\t50.00\t"));

        Assert.Equal(3, curve.Bands.Count);
        Assert.Equal(new PeqBand(120, 11.59, -6.0), curve.Bands[0]);
        Assert.Equal(new PeqBand(45, 9.48, -4.5), curve.Bands[1]);
        Assert.All(curve.Bands, band => Assert.Equal(PeqBandType.Peaking, band.Type));
        Assert.Equal(new PeqBand(200, 4.0, -2.0), curve.Bands[2]);

        // Written back as bells: the processor has no modal slot; T60 is REW optimizer metadata.
        Assert.Equal(
            "1\tTrue\tAuto\tPK\t120.0\t-6.0\t11.59\t10.35\t",
            Lines(Format.Export(curve))[2]);
    }

    [Fact]
    public void Import_RefusesATruncatedOrRenumberedBank()
    {
        // An import replaces the EQ on screen, so anything but the full table must fail recognition.
        string full = Bank("True\tAuto\tPK\t1000\t-3\t4.00\t250\t");
        Assert.True(Format.TryImport(full, out EqualizationCurve intact));
        Assert.Single(intact.Bands);

        string[] rows = full.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.False(Format.TryImport(string.Join("\n", rows.Take(rows.Length - 1)), out _));
        Assert.False(Format.TryImport("Audiotec_Fischer_Full_EQ_(30_bands)\n", out _));
        Assert.False(Format.TryImport(
            full + $"{AudiotecFischerFormat.SlotCount + 1}\tTrue\tAuto\tPK\t900\t-1\t2.00\t450\t\n",
            out _));
        Assert.False(Format.TryImport(full.Replace("\n2\tTrue", "\n1\tTrue"), out _));
    }

    [Fact]
    public void Import_RequiresTheBankHeaderNotTheColumnLine()
    {
        // REW prints the same column line for every equaliser; only the bank header identifies the format.
        string columnsOnly =
            "Number\tEnabled\tControl\tType\tFrequency(Hz)\tGain(dB)\tQ\tBandwidth(Hz)\tTargetT60(ms)\t\r\n" +
            string.Concat(Enumerable
                .Range(1, AudiotecFischerFormat.SlotCount)
                .Select(slot => $"{slot}\tTrue\tAuto\tNone\t\r\n"));
        Assert.False(Format.TryImport(columnsOnly, out _));

        Assert.True(Format.TryImport(Bank("True\tAuto\tPK\t1000\t-3\t4.00\t250\t"), out EqualizationCurve curve));
        Assert.Single(curve.Bands);

        Assert.True(Format.TryImport(RewExport, out _));
    }

    [Fact]
    public void Import_KeepsAFullBankOfBandsWithinTheSlotBudget()
    {
        string full = Bank(Enumerable.Range(1, AudiotecFischerFormat.SlotCount)
            .Select(slot => $"True\tAuto\tPK\t{100 * slot}\t-1.0\t2.00\t{50 * slot}\t")
            .ToArray());

        Assert.True(Format.TryImport(full, out EqualizationCurve curve));

        Assert.Equal(AudiotecFischerFormat.SlotCount, curve.Bands.Count);
        Assert.Equal(AudiotecFischerFormat.SlotCount, Format.Import(Format.Export(curve)).Bands.Count);
    }

    [Fact]
    public void Export_WritesTheBankHeaderAndExactlyThirtySlots()
    {
        string[] lines = Lines(Format.Export(Mixed()));

        Assert.Equal(2 + AudiotecFischerFormat.SlotCount, lines.Length);
        Assert.Equal("Audiotec_Fischer_Full_EQ_(30_bands)", lines[0]);
        Assert.Equal(
            "Number\tEnabled\tControl\tType\tFrequency(Hz)\tGain(dB)\tQ\tBandwidth(Hz)\tTargetT60(ms)\t",
            lines[1]);
        for (int slot = 4; slot <= AudiotecFischerFormat.SlotCount; slot++)
        {
            Assert.Equal($"{slot}\tTrue\tAuto\tNone\t", lines[slot + 1]);
        }
    }

    [Fact]
    public void Export_WritesBellsAndShelvesInRewsRowShapes()
    {
        string[] lines = Lines(Format.Export(Mixed()));

        Assert.Equal("1\tTrue\tAuto\tLS_Q\t80.0\t4.5\t0.70", lines[2]);
        Assert.Equal("2\tTrue\tAuto\tPK\t1000.0\t-6.0\t4.00\t250\t", lines[3]);
        Assert.Equal("3\tTrue\tAuto\tHS_Q\t6300.0\t-3.5\t1.10", lines[4]);
    }

    [Fact]
    public void Export_LeavesThePreampOutAndSaysSo()
    {
        Assert.False(Format.CarriesPreamp);
        Assert.True(((IEqProfileFormat)new EqualizerApoFormat()).CarriesPreamp);
        Assert.DoesNotContain("-6.5", Format.Export(Mixed()));
        Assert.Equal(0, Format.Import(Format.Export(Mixed())).PreampDb);
    }

    [Fact]
    public void Export_RefusesMoreBandsThanTheBankHasSlots()
    {
        // The library allows 32 bands, the processor 30: refuse rather than drop silently.
        var overfull = new EqualizationCurve(
            Enumerable.Range(1, AudiotecFischerFormat.SlotCount + 1)
                .Select(index => new PeqBand(100 * index, 2.0, -1.0)));

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => Format.Export(overfull));
        Assert.Contains("30", error.Message);

        var full = new EqualizationCurve(
            Enumerable.Range(1, AudiotecFischerFormat.SlotCount)
                .Select(index => new PeqBand(100 * index, 2.0, -1.0)));
        Assert.Equal(AudiotecFischerFormat.SlotCount, Format.Import(
            Format.Export(full)).Bands.Count);
    }

    [Fact]
    public void RoundTrip_KeepsShapeOrderAndValues()
    {
        EqualizationCurve original = Mixed();

        Assert.True(Format.TryImport(Format.Export(original), out EqualizationCurve read));

        Assert.Equal(original.Bands.Count, read.Bands.Count);
        for (int index = 0; index < original.Bands.Count; index++)
        {
            Assert.Equal(original.Bands[index].Type, read.Bands[index].Type);
            Assert.Equal(original.Bands[index].FrequencyHz, read.Bands[index].FrequencyHz, 3);
            Assert.Equal(original.Bands[index].Q, read.Bands[index].Q, 3);
            Assert.Equal(original.Bands[index].GainDb, read.Bands[index].GainDb, 3);
        }
    }

    [Fact]
    public void RoundTrip_SurvivesARealExportUnchanged()
    {
        // Bytes differ (bandwidth not truncated) but no band moves.
        EqualizationCurve first = Format.Import(RewExport);

        EqualizationCurve second = Format.Import(Format.Export(first));

        Assert.Equal(first.Bands, second.Bands);
    }
}
