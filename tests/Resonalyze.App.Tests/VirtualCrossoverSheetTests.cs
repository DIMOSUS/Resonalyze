using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class VirtualCrossoverSheetTests
{
    private static VirtualCrossoverProjectFile CreateProject()
    {
        var project = new VirtualCrossoverProjectFile();
        project.Channels[0] = new VirtualCrossoverChannelSettings
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
        project.Channels[1] = new VirtualCrossoverChannelSettings
        {
            DisplayName = "tweeter.json",
            SourceFilePath = @"C:\m\tweeter.json",
            CrossoverKind = CrossoverKind.HighPass,
            HighPassEdge = new CrossoverEdge(CrossoverFilterFamily.Butterworth, 2_000, 18)
        };
        // The third channel has no source and must not appear on the sheet.
        return project;
    }

    [Fact]
    public void FormatText_ListsEveryDspSettingOfParticipatingChannels()
    {
        string text = VirtualCrossoverSheet.FormatText(CreateProject(), "Sum loss avg: -1.8 dB");

        Assert.Contains("Sum loss avg: -1.8 dB", text);
        Assert.Contains("Channel A — woofer.json", text);
        Assert.Contains("-2.5 dB", text);
        Assert.Contains("0.42 ms", text);
        Assert.Contains("144.1 mm", text);
        Assert.Contains("Inverted", text);
        Assert.Contains("Low-pass Linkwitz-Riley 24 dB/oct @ 2000 Hz", text);
        Assert.Contains("woofer-peq.txt, preamp -1.5 dB", text);
        Assert.Contains("Filter 1: ON PK Fc 120 Hz Gain -4.0 dB Q 2.0", text);

        Assert.Contains("Channel B — tweeter.json", text);
        Assert.Contains("High-pass Butterworth 18 dB/oct @ 2000 Hz", text);
        Assert.Contains("Normal", text);

        Assert.DoesNotContain("Channel C", text);
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
