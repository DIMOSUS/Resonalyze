using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class VirtualCrossoverSheetTests
{
    private static VirtualCrossoverProjectFile CreateProject()
    {
        var project = new VirtualCrossoverProjectFile();
        project.Pairs[0].Mono = true;
        project.Pairs[0].Left = new VirtualCrossoverChannelSettings
        {
            DisplayName = "woofer.json",
            SourceFilePath = @"C:\m\woofer.json",
            GainDb = -2.5,
            DelayMs = 0.42,
            InvertPolarity = true,
            CrossoverKind = CrossoverKind.LowPass,
            LowPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 2_000, 24),
            PeqPreampDb = -1.5,
            PeqBands = [new PeqBand(120, 2.0, -4.0)],
            PeqSourceName = "woofer-peq.txt"
        };
        project.Pairs[1].Left = new VirtualCrossoverChannelSettings
        {
            DisplayName = "tweeter.json",
            SourceFilePath = @"C:\m\tweeter.json",
            CrossoverKind = CrossoverKind.HighPass,
            HighPassEdge = new CrossoverEdge(CrossoverFilterFamily.Butterworth, 2_000, 18)
        };
        project.Pairs[1].Right = new VirtualCrossoverChannelSettings
        {
            DisplayName = "tweeter R.json",
            SourceFilePath = @"C:\m\tweeter R.json",
            CrossoverKind = CrossoverKind.HighPass,
            HighPassEdge = new CrossoverEdge(CrossoverFilterFamily.Butterworth, 2_000, 18),
            DelayMs = 0.68
        };
        // The third pair has no source and must not appear on the sheet.
        return project;
    }

    // A sheet is transcribed into the DSP, so its Q column has to be the one that DSP
    // reads. The band is 120 Hz Q 2.0 -4 dB, a CUT — the case where the two proportional
    // conventions move Q in opposite directions: Symmetric to 2.0 * 10^(4/40) = 2.517,
    // Classic to 2.0 * 10^(-4/40) = 1.589.
    [Theory]
    [InlineData(PeqQConvention.Rbj, "Q 2")]
    [InlineData(PeqQConvention.Symmetric, "Q 2.52")]
    [InlineData(PeqQConvention.Classic, "Q 1.59")]
    public void FormatText_RestatesQInTheTargetDspConvention(
        PeqQConvention convention,
        string expectedQ)
    {
        string text = VirtualCrossoverSheet.FormatText(CreateProject(), null, convention);

        // Only Q moves: a mismatched convention never shifts a band or its gain.
        Assert.Contains($"Fc 120 Hz Gain -4.0 dB {expectedQ}", text);
    }

    // The text sheet is an instruction to type filters into a DSP, so it has to name
    // the shape as well as the numbers. A shelf printed as PK sends the tuner to the
    // wrong filter type with the right frequency — the shape reads plausible and the
    // curve is wrong. LSC/HSC are the Equalizer APO keywords that carry a Q, which is
    // what the sheet prints beside them.
    [Fact]
    public void FormatText_NamesTheFilterShapeOfEveryBand()
    {
        VirtualCrossoverProjectFile project = CreateProject();
        project.Pairs[0].Left!.PeqBands =
        [
            new PeqBand(120, 2.0, -4.0),
            new PeqBand(80, 0.7, 4.5, PeqBandType.LowShelf),
            new PeqBand(6_300, 1.1, -3.5, PeqBandType.HighShelf)
        ];

        string text = VirtualCrossoverSheet.FormatText(project, null);

        Assert.Contains("Filter 1: ON PK Fc 120 Hz Gain -4.0 dB Q 2", text);
        Assert.Contains("Filter 2: ON LSC Fc 80 Hz Gain +4.5 dB Q 0.7", text);
        Assert.Contains("Filter 3: ON HSC Fc 6300 Hz Gain -3.5 dB Q 1.1", text);
    }

    // A shelf's Q is a knee, not a bandwidth, so no convention restates it — while the
    // bell beside it is restated as usual.
    [Fact]
    public void FormatText_LeavesAShelfQAloneUnderAProportionalConvention()
    {
        VirtualCrossoverProjectFile project = CreateProject();
        project.Pairs[0].Left!.PeqBands =
        [
            new PeqBand(120, 2.0, -4.0),
            new PeqBand(80, 0.7, 12.0, PeqBandType.LowShelf)
        ];

        string text = VirtualCrossoverSheet.FormatText(
            project, null, PeqQConvention.Symmetric);

        Assert.Contains("Fc 120 Hz Gain -4.0 dB Q 2.52", text);
        Assert.Contains("Fc 80 Hz Gain +12.0 dB Q 0.7", text);
    }

    // Named on every sheet, including the default one — a sheet that does not say which
    // convention its Q belongs to is unreadable a month later.
    [Theory]
    [InlineData(PeqQConvention.Rbj, "RBJ Q — cookbook (constant)")]
    [InlineData(PeqQConvention.Symmetric, "Symmetric Q — Zölzer/DAFX (proportional)")]
    [InlineData(PeqQConvention.Classic, "Classic Q (asymmetric: boost wider, cut narrower)")]
    public void FormatText_NamesTheQConvention(PeqQConvention convention, string expected)
    {
        string text = VirtualCrossoverSheet.FormatText(CreateProject(), null, convention);

        Assert.Contains($"PEQ Q convention: {expected}", text);
    }

    [Fact]
    public void FormatText_ListsEveryDspSettingOfParticipatingChannels()
    {
        string text = VirtualCrossoverSheet.FormatText(CreateProject(), "Sum loss avg: -1.8 dB");

        Assert.Contains("Sum loss avg: -1.8 dB", text);
        // The mono pair prints ONE section; the stereo pair prints both sides.
        Assert.Contains("Channel A (mono) — woofer.json", text);
        Assert.Contains("-2.5 dB", text);
        Assert.Contains("0.42 ms", text);
        Assert.Contains("144.1 mm", text);
        Assert.Contains("Inverted", text);
        Assert.Contains("Low-pass Linkwitz-Riley 24 dB/oct @ 2000 Hz", text);
        Assert.Contains("woofer-peq.txt, preamp -1.5 dB", text);
        Assert.Contains("Filter 1: ON PK Fc 120 Hz Gain -4.0 dB Q 2.0", text);

        Assert.Contains("Channel B Left — tweeter.json", text);
        Assert.Contains("Channel B Right — tweeter R.json", text);
        // The suffixes are constants because the PDF matches on them to decide which
        // graph traces are dashed; a literal would stop matching if the wording changed.
        Assert.Equal(
            VirtualCrossoverSheet.RightSuffix,
            VirtualCrossoverSheet
                .SideSections(new VirtualCrossoverChannelPairSettings())
                .Last()
                .SideSuffix);
        Assert.Contains("0.68 ms", text);
        Assert.Contains("High-pass Butterworth 18 dB/oct @ 2000 Hz", text);
        Assert.Contains("Normal", text);

        Assert.DoesNotContain("Channel C", text);
    }

    [Fact]
    public void FormatText_PrintsAnAllPassBandWithNoGainToDialIn()
    {
        // The sheet is an instruction to type filters into a DSP. An all-pass has no
        // gain cell to fill — printing "Gain +0.0 dB" beside it invites the tuner to
        // look for one — and a first-order section has no Q either.
        VirtualCrossoverProjectFile project = CreateProject();
        project.Pairs[0].Left!.PeqBands =
        [
            new PeqBand(90, 2.5, 0, PeqBandType.AllPassSecondOrder),
            new PeqBand(300, 1.0, 0, PeqBandType.AllPassFirstOrder)
        ];

        string text = VirtualCrossoverSheet.FormatText(project, null);

        Assert.Contains("Filter 1: ON AP Fc 90 Hz Q 2.5" + Environment.NewLine, text);
        Assert.Contains("Filter 2: ON AP1 Fc 300 Hz" + Environment.NewLine, text);
    }

    [Fact]
    public void FormatText_LeavesAnAllPassQAloneUnderAProportionalConvention()
    {
        // A proportional convention rescales a bell's Q by its gain. An all-pass has no
        // gain, so the rescaling has nothing to say about it — and a restated Q would be
        // a phase turn the tuner never asked for.
        VirtualCrossoverProjectFile project = CreateProject();
        project.Pairs[0].Left!.PeqBands =
            [new PeqBand(90, 2.5, 0, PeqBandType.AllPassSecondOrder)];

        string text = VirtualCrossoverSheet.FormatText(
            project, null, PeqQConvention.Symmetric);

        Assert.Contains("Fc 90 Hz Q 2.5", text);
    }

    [Fact]
    public void DescribeCrossover_CoversEveryKind()
    {
        var channel = new VirtualCrossoverChannelSettings
        {
            HighPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 300, 12),
            LowPassEdge = new CrossoverEdge(CrossoverFilterFamily.Butterworth, 3_000, 6)
        };

        channel.CrossoverKind = CrossoverKind.Off;
        Assert.Equal("Off", VirtualCrossoverSheet.DescribeCrossover(channel));

        channel.CrossoverKind = CrossoverKind.BandPass;
        Assert.Equal(
            "High-pass Linkwitz-Riley 12 dB/oct @ 300 Hz + " +
            "Low-pass Butterworth 6 dB/oct @ 3000 Hz",
            VirtualCrossoverSheet.DescribeCrossover(channel));
    }

    // A project spanning several zones, laid out like the reference car: a
    // front stereo pair, a rear stereo pair, a mono centre and a mono sub —
    // deliberately in PANEL order (front first, sub last), so a sheet that
    // groups correctly must visibly reorder the sections.
    internal static VirtualCrossoverProjectFile GroupedProject()
    {
        var project = new VirtualCrossoverProjectFile();
        while (project.Pairs.Count < 4)
        {
            project.Pairs.Add(new VirtualCrossoverChannelPairSettings());
        }

        project.Pairs[0].Zone = VirtualCrossoverZone.Front;
        project.Pairs[0].Left.SourceFilePath = "front-l.json";
        project.Pairs[0].Left.DisplayName = "front L";
        project.Pairs[0].Right.SourceFilePath = "front-r.json";
        project.Pairs[0].Right.DisplayName = "front R";
        project.Pairs[1].Zone = VirtualCrossoverZone.Rear;
        project.Pairs[1].Left.SourceFilePath = "rear-l.json";
        project.Pairs[1].Left.DisplayName = "rear L";
        project.Pairs[1].Right.SourceFilePath = "rear-r.json";
        project.Pairs[1].Right.DisplayName = "rear R";
        project.Pairs[2].Zone = VirtualCrossoverZone.Center;
        project.Pairs[2].Mono = true;
        project.Pairs[2].Left.SourceFilePath = "centre.json";
        project.Pairs[2].Left.DisplayName = "centre";
        project.Pairs[3].Zone = VirtualCrossoverZone.Sub;
        project.Pairs[3].Mono = true;
        project.Pairs[3].Left.SourceFilePath = "sub.json";
        project.Pairs[3].Left.DisplayName = "sub";
        return project;
    }

    [Fact]
    public void FormatText_GroupsSectionsByZone_InTheOrderATuneIsTyped()
    {
        string text = VirtualCrossoverSheet.FormatText(GroupedProject(), null);

        // Sub first, then the front stage, then the groups placed against it —
        // the order the values are entered into a DSP, not the panel's order.
        int sub = text.IndexOf("=== Sub ===", StringComparison.Ordinal);
        int front = text.IndexOf("=== Front ===", StringComparison.Ordinal);
        int rear = text.IndexOf("=== Rear ===", StringComparison.Ordinal);
        int centre = text.IndexOf("=== Center ===", StringComparison.Ordinal);
        Assert.True(sub >= 0, "the Sub heading is missing");
        Assert.True(front > sub, "Front must follow Sub");
        Assert.True(rear > front, "Rear must follow Front");
        Assert.True(centre > rear, "Center must follow Rear");

        // Grouping moves SECTIONS, never names: the sub is the panel's block D
        // and its section — printed first — still says so.
        int subChannel = text.IndexOf(
            "Channel D (mono) — sub", StringComparison.Ordinal);
        Assert.InRange(subChannel, sub, front);
    }

    [Fact]
    public void SheetSectionOrder_CoversEveryZone()
    {
        // The grouping walks SectionOrder and keeps only zones it names, so a
        // zone added to the enum but forgotten here would silently DROP its
        // channels from the sheet — the worst possible failure for a document
        // whose whole job is completeness.
        Assert.Equal(
            VirtualCrossoverZones.All.Order(),
            VirtualCrossoverSheetGroups.SectionOrder.Order());
    }

    [Fact]
    public void FormatText_SingleZoneProject_KeepsTheFlatSheetItAlwaysHad()
    {
        // One zone means one group, and a heading naming the only group there
        // is would be scaffolding around nothing — the classic project's sheet
        // must not change shape because zones now exist.
        string text = VirtualCrossoverSheet.FormatText(CreateProject(), null);

        Assert.DoesNotContain("===", text);
    }
}
