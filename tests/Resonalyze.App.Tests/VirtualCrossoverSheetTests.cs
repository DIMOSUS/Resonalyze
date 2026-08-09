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
    public void DescribeAllPass_CoversEveryType()
    {
        // The sheet is how a tuning leaves the app for the hardware, so the all-pass has
        // to be on it. Q is printed only where it exists: a first-order section has none.
        var channel = new VirtualCrossoverChannelSettings
        {
            AllPassFrequencyHz = 90,
            AllPassQ = 2.5
        };

        channel.AllPassType = AllPassType.Off;
        Assert.Equal("Off", VirtualCrossoverSheet.DescribeAllPass(channel));

        channel.AllPassType = AllPassType.FirstOrder;
        Assert.Equal("1st order @ 90 Hz", VirtualCrossoverSheet.DescribeAllPass(channel));

        channel.AllPassType = AllPassType.SecondOrder;
        Assert.Equal("2nd order @ 90 Hz Q 2.5", VirtualCrossoverSheet.DescribeAllPass(channel));
    }

    [Fact]
    public void FormatText_OmitsTheAllPassLineWhenTheStageIsOff()
    {
        // An Off stage is the default on every channel; printing "All-pass Off" on each
        // one would just be noise on the sheet.
        VirtualCrossoverProjectFile project = CreateProject();
        Assert.DoesNotContain("All-pass", VirtualCrossoverSheet.FormatText(project, string.Empty));

        project.Pairs[0].Left.AllPassType = AllPassType.SecondOrder;
        project.Pairs[0].Left.AllPassFrequencyHz = 90;
        project.Pairs[0].Left.AllPassQ = 2.5;

        Assert.Contains(
            "All-pass   2nd order @ 90 Hz Q 2.5",
            VirtualCrossoverSheet.FormatText(project, string.Empty));
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
}
