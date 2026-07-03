using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class VirtualCrossoverProjectFileTests
{
    [Fact]
    public void SaveAndLoad_RoundTripsTheProject()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            var original = new VirtualCrossoverProjectFile
            {
                ShowSumCurve = false,
                ShowLossCurve = true,
                ShowPhaseView = true,
                PhaseGateOffsetMs = 12.34,
                PhaseGateLeftMs = 0.25,
                PhaseGatePlateauMs = 6.5,
                PhaseGateRightMs = 2.0,
                PhaseDetrendMs = 13.07
            };
            original.Channels[0] = new VirtualCrossoverChannelSettings
            {
                Enabled = false,
                Bypass = true,
                DisplayName = "Woofer",
                SourceFilePath = @"C:\measurements\woofer.json",
                HistoryEntryId = Guid.NewGuid(),
                GainDb = -2.5,
                DelayMs = 0.42,
                InvertPolarity = true,
                CrossoverKind = CrossoverKind.LowPass,
                LowPassEdge = new CrossoverEdge(
                    CrossoverFilterFamily.Butterworth, 1_800, 18),
                PeqPreampDb = -1.5,
                PeqBands = [new PeqBand(120, 2.0, -4.0), new PeqBand(900, 1.0, 2.0)],
                PeqSourceName = "woofer-peq.txt",
                ShowRawCurve = true,
                ShowProcessedCurve = false
            };

            original.Save(root);
            VirtualCrossoverProjectFile loaded =
                VirtualCrossoverProjectFile.LoadOrDefault(root);

            Assert.Equal(original.ShowSumCurve, loaded.ShowSumCurve);
            Assert.Equal(original.ShowLossCurve, loaded.ShowLossCurve);
            Assert.Equal(original.ShowPhaseView, loaded.ShowPhaseView);
            Assert.Equal(original.PhaseGateOffsetMs, loaded.PhaseGateOffsetMs);
            Assert.Equal(original.PhaseGateLeftMs, loaded.PhaseGateLeftMs);
            Assert.Equal(original.PhaseGatePlateauMs, loaded.PhaseGatePlateauMs);
            Assert.Equal(original.PhaseGateRightMs, loaded.PhaseGateRightMs);
            Assert.Equal(original.PhaseDetrendMs, loaded.PhaseDetrendMs);
            Assert.Equal(original.Channels.Count, loaded.Channels.Count);

            VirtualCrossoverChannelSettings expected = original.Channels[0];
            VirtualCrossoverChannelSettings actual = loaded.Channels[0];
            Assert.Equal(expected.Enabled, actual.Enabled);
            Assert.Equal(expected.Bypass, actual.Bypass);
            Assert.Equal(expected.DisplayName, actual.DisplayName);
            Assert.Equal(expected.SourceFilePath, actual.SourceFilePath);
            Assert.Equal(expected.HistoryEntryId, actual.HistoryEntryId);
            Assert.Equal(expected.GainDb, actual.GainDb);
            Assert.Equal(expected.DelayMs, actual.DelayMs);
            Assert.Equal(expected.InvertPolarity, actual.InvertPolarity);
            Assert.Equal(expected.CrossoverKind, actual.CrossoverKind);
            Assert.Equal(expected.LowPassEdge, actual.LowPassEdge);
            Assert.Equal(expected.HighPassEdge, actual.HighPassEdge);
            Assert.Equal(expected.PeqPreampDb, actual.PeqPreampDb);
            Assert.Equal(expected.PeqBands, actual.PeqBands);
            Assert.Equal(expected.PeqSourceName, actual.PeqSourceName);
            Assert.Equal(expected.ShowRawCurve, actual.ShowRawCurve);
            Assert.Equal(expected.ShowProcessedCurve, actual.ShowProcessedCurve);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadOrDefault_FallsBackWhenTheFileIsMissingOrCorrupt()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            VirtualCrossoverProjectFile missing =
                VirtualCrossoverProjectFile.LoadOrDefault(root);
            Assert.Equal(3, missing.Channels.Count);
            Assert.True(missing.ShowSumCurve);

            File.WriteAllText(
                VirtualCrossoverProjectFile.GetPath(root),
                "{ not json ");
            VirtualCrossoverProjectFile corrupt =
                VirtualCrossoverProjectFile.LoadOrDefault(root);
            Assert.Equal(3, corrupt.Channels.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadOrDefault_FallsBackOnAnUnknownVersion()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            var future = new VirtualCrossoverProjectFile();
            future.Channels[0].GainDb = -10;
            future.Save(root);

            string path = VirtualCrossoverProjectFile.GetPath(root);
            File.WriteAllText(
                path,
                File.ReadAllText(path).Replace(
                    $"\"version\": {VirtualCrossoverProjectFile.CurrentVersion}",
                    $"\"version\": {VirtualCrossoverProjectFile.CurrentVersion + 1}"));

            VirtualCrossoverProjectFile loaded =
                VirtualCrossoverProjectFile.LoadOrDefault(root);
            Assert.Equal(0, loaded.Channels[0].GainDb);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Save_RejectsInvalidChannelValues()
    {
        var negativeDelay = new VirtualCrossoverProjectFile();
        negativeDelay.Channels[0].DelayMs = -1;
        Assert.Throws<InvalidDataException>(() => negativeDelay.Validate());

        var badSlope = new VirtualCrossoverProjectFile();
        badSlope.Channels[0].LowPassEdge = new CrossoverEdge(
            CrossoverFilterFamily.LinkwitzRiley, 1_000, 18);
        Assert.Throws<InvalidDataException>(() => badSlope.Validate());

        var badBand = new VirtualCrossoverProjectFile();
        badBand.Channels[0].PeqBands = [new PeqBand(0, 1.0, 3.0)];
        Assert.Throws<InvalidDataException>(() => badBand.Validate());

        var tooFewChannels = new VirtualCrossoverProjectFile();
        tooFewChannels.Channels.RemoveRange(1, 2);
        Assert.Throws<InvalidDataException>(() => tooFewChannels.Validate());

        var badSmoothing = new VirtualCrossoverProjectFile
        {
            SmoothingInverseOctaves = 7
        };
        Assert.Throws<InvalidDataException>(() => badSmoothing.Validate());

        var badGateOffset = new VirtualCrossoverProjectFile
        {
            PhaseGateOffsetMs = -1
        };
        Assert.Throws<InvalidDataException>(() => badGateOffset.Validate());

        var badDetrend = new VirtualCrossoverProjectFile
        {
            PhaseDetrendMs = double.NaN
        };
        Assert.Throws<InvalidDataException>(() => badDetrend.Validate());

        var emptyGate = new VirtualCrossoverProjectFile
        {
            PhaseGateLeftMs = 0,
            PhaseGatePlateauMs = 0,
            PhaseGateRightMs = 0
        };
        Assert.Throws<InvalidDataException>(() => emptyGate.Validate());
    }

    [Fact]
    public void SaveToAndLoadFrom_RoundTripAnExportedSession()
    {
        string root = CreateTemporaryDirectory();
        string path = Path.Combine(root, "session.json");
        try
        {
            var original = new VirtualCrossoverProjectFile { ShowLossCurve = true };
            original.Channels[0].DisplayName = "woofer";
            original.Channels[0].SourceFilePath = @"C:\m\woofer.json";
            original.Channels[0].DelayMs = 1.25;

            original.SaveTo(path);
            VirtualCrossoverProjectFile loaded = VirtualCrossoverProjectFile.LoadFrom(path);

            Assert.True(loaded.ShowLossCurve);
            Assert.Equal("woofer", loaded.Channels[0].DisplayName);
            Assert.Equal(1.25, loaded.Channels[0].DelayMs);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadFrom_ThrowsOnABrokenSessionFile()
    {
        string root = CreateTemporaryDirectory();
        string path = Path.Combine(root, "broken.json");
        try
        {
            File.WriteAllText(path, "{ not json ");
            Assert.ThrowsAny<Exception>(() => VirtualCrossoverProjectFile.LoadFrom(path));

            var wrongVersion = new VirtualCrossoverProjectFile();
            wrongVersion.SaveTo(path);
            File.WriteAllText(
                path,
                File.ReadAllText(path).Replace(
                    $"\"version\": {VirtualCrossoverProjectFile.CurrentVersion}",
                    $"\"version\": {VirtualCrossoverProjectFile.CurrentVersion + 1}"));
            Assert.Throws<InvalidDataException>(
                () => VirtualCrossoverProjectFile.LoadFrom(path));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ToChain_MapsTheSettingsToTheDspChain()
    {
        var channel = new VirtualCrossoverChannelSettings
        {
            GainDb = -3,
            DelayMs = 0.5,
            InvertPolarity = true,
            CrossoverKind = CrossoverKind.BandPass,
            HighPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 300, 24),
            LowPassEdge = new CrossoverEdge(CrossoverFilterFamily.Butterworth, 3_000, 12),
            PeqPreampDb = -1,
            PeqBands = [new PeqBand(1_000, 1.0, 3.0)]
        };

        DspChannelChain chain = channel.ToChain();

        Assert.Equal(-3, chain.GainDb);
        Assert.Equal(0.5, chain.DelayMs);
        Assert.True(chain.InvertPolarity);
        Assert.Equal(CrossoverKind.BandPass, chain.Crossover!.Kind);
        Assert.Equal(channel.LowPassEdge, chain.Crossover.LowPassEdge);
        Assert.Equal(channel.HighPassEdge, chain.Crossover.HighPassEdge);
        Assert.Equal(-1, chain.Peq!.PreampDb);
        Assert.Equal(channel.PeqBands, chain.Peq.Bands);
    }

    [Fact]
    public void ToChain_OffCrossoverAndEmptyPeq_YieldATransparentChain()
    {
        var channel = new VirtualCrossoverChannelSettings();

        DspChannelChain chain = channel.ToChain();

        Assert.Equal(CrossoverKind.Off, chain.Crossover!.Kind);
        Assert.Null(chain.Peq);
        Assert.Equal(1.0, chain.Response(1_000, 48_000).Magnitude, 12);
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "resonalyze-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
