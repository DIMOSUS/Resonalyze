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
                PhaseDetrendMs = 13.07,
                PhaseWindowMode = PhaseWindowMode.FrequencyDependent,
                PhaseFdwCycles = 8,
                PhaseDetrendMode = PhaseDetrendMode.Manual
            };
            original.StereoSceneOffsetMs = -0.4;
            original.ActiveSideRight = true;
            original.Pairs[0].Mono = true;
            original.Pairs[0].Left = new VirtualCrossoverChannelSettings
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
            Assert.Equal(original.PhaseWindowMode, loaded.PhaseWindowMode);
            Assert.Equal(original.PhaseFdwCycles, loaded.PhaseFdwCycles);
            Assert.Equal(original.PhaseDetrendMode, loaded.PhaseDetrendMode);
            Assert.Equal(original.StereoSceneOffsetMs, loaded.StereoSceneOffsetMs);
            Assert.Equal(original.ActiveSideRight, loaded.ActiveSideRight);
            Assert.Equal(original.Pairs.Count, loaded.Pairs.Count);
            Assert.True(loaded.Pairs[0].Mono);

            VirtualCrossoverChannelSettings expected = original.Pairs[0].Left;
            VirtualCrossoverChannelSettings actual = loaded.Pairs[0].Left;
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
            Assert.Equal(3, missing.Pairs.Count);
            Assert.True(missing.ShowSumCurve);

            string path = VirtualCrossoverProjectFile.GetPath(root);
            File.WriteAllText(path, "{ not json ");
            VirtualCrossoverProjectFile corrupt =
                VirtualCrossoverProjectFile.LoadOrDefault(root);
            Assert.Equal(3, corrupt.Pairs.Count);

            // The unusable file is parked as .backup so the next scheduled
            // save cannot silently destroy it.
            Assert.False(File.Exists(path));
            Assert.Equal("{ not json ", File.ReadAllText(path + ".backup"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadOrDefault_FallsBackOnAnUnknownVersion_AndBacksTheFileUp()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            var future = new VirtualCrossoverProjectFile();
            future.Pairs[0].Left.GainDb = -10;
            future.Save(root);

            string path = VirtualCrossoverProjectFile.GetPath(root);
            string futureText = File.ReadAllText(path).Replace(
                $"\"version\": {VirtualCrossoverProjectFile.CurrentVersion}",
                $"\"version\": {VirtualCrossoverProjectFile.CurrentVersion + 1}");
            File.WriteAllText(path, futureText);

            VirtualCrossoverProjectFile loaded =
                VirtualCrossoverProjectFile.LoadOrDefault(root);
            Assert.Equal(0, loaded.Pairs[0].Left.GainDb);

            // A downgraded app keeps the newer session parked next to the
            // fresh default instead of overwriting it on the next save.
            Assert.False(File.Exists(path));
            Assert.Equal(futureText, File.ReadAllText(path + ".backup"));

            // The path is surfaced so the tool can tell the user a backup exists.
            Assert.Equal(path + ".backup", loaded.BackupNoticePath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadOrDefault_ReplacesAnOlderBackup()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            string path = VirtualCrossoverProjectFile.GetPath(root);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path + ".backup", "older backup");
            File.WriteAllText(path, "{ newer garbage ");

            VirtualCrossoverProjectFile.LoadOrDefault(root);

            Assert.Equal("{ newer garbage ", File.ReadAllText(path + ".backup"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadOrDefault_DoesNotDisturbAValidFileOrItsBackup()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            var original = new VirtualCrossoverProjectFile();
            original.Pairs[0].Left.GainDb = -4;
            original.Save(root);

            VirtualCrossoverProjectFile loaded =
                VirtualCrossoverProjectFile.LoadOrDefault(root);

            string path = VirtualCrossoverProjectFile.GetPath(root);
            Assert.Equal(-4, loaded.Pairs[0].Left.GainDb);
            Assert.True(File.Exists(path));
            Assert.False(File.Exists(path + ".backup"));
            Assert.Null(loaded.BackupNoticePath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SaveAndLoad_RoundTripsAMaxChannelProjectAndCalibrationMode()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            var original = new VirtualCrossoverProjectFile
            {
                CalibrationMode = MicrophoneCalibrationMode.Degrees90,
                DspPlotMode = DspPlotMode.GroupDelay
            };
            while (original.Pairs.Count <
                VirtualCrossoverProjectFile.MaximumChannelCount)
            {
                original.Pairs.Add(new VirtualCrossoverChannelPairSettings());
            }

            original.Save(root);
            VirtualCrossoverProjectFile loaded =
                VirtualCrossoverProjectFile.LoadOrDefault(root);

            Assert.Equal(
                VirtualCrossoverProjectFile.MaximumChannelCount,
                loaded.Pairs.Count);
            Assert.Equal(MicrophoneCalibrationMode.Degrees90, loaded.CalibrationMode);
            Assert.Equal(DspPlotMode.GroupDelay, loaded.DspPlotMode);
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
        negativeDelay.Pairs[0].Left.DelayMs = -1;
        Assert.Throws<InvalidDataException>(() => negativeDelay.Validate());

        var badSlope = new VirtualCrossoverProjectFile();
        badSlope.Pairs[0].Left.LowPassEdge = new CrossoverEdge(
            CrossoverFilterFamily.LinkwitzRiley, 1_000, 18);
        Assert.Throws<InvalidDataException>(() => badSlope.Validate());

        var badBand = new VirtualCrossoverProjectFile();
        badBand.Pairs[0].Right.PeqBands = [new PeqBand(0, 1.0, 3.0)];
        Assert.Throws<InvalidDataException>(() => badBand.Validate());

        var tooFewChannels = new VirtualCrossoverProjectFile();
        tooFewChannels.Pairs.RemoveRange(1, 2);
        Assert.Throws<InvalidDataException>(() => tooFewChannels.Validate());

        var tooManyChannels = new VirtualCrossoverProjectFile();
        while (tooManyChannels.Pairs.Count <=
            VirtualCrossoverProjectFile.MaximumChannelCount)
        {
            tooManyChannels.Pairs.Add(new VirtualCrossoverChannelPairSettings());
        }
        Assert.Throws<InvalidDataException>(() => tooManyChannels.Validate());

        var badSceneOffset = new VirtualCrossoverProjectFile
        {
            StereoSceneOffsetMs =
                VirtualCrossoverProjectFile.MaximumSceneOffsetMs + 1
        };
        Assert.Throws<InvalidDataException>(() => badSceneOffset.Validate());

        var badCalibrationMode = new VirtualCrossoverProjectFile
        {
            CalibrationMode = (MicrophoneCalibrationMode)42
        };
        Assert.Throws<InvalidDataException>(() => badCalibrationMode.Validate());

        var badDspPlotMode = new VirtualCrossoverProjectFile
        {
            DspPlotMode = (DspPlotMode)42
        };
        Assert.Throws<InvalidDataException>(() => badDspPlotMode.Validate());

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
            original.Pairs[0].Left.DisplayName = "woofer";
            original.Pairs[0].Left.SourceFilePath = @"C:\m\woofer.json";
            original.Pairs[0].Right.DelayMs = 1.25;

            original.SaveTo(path);
            VirtualCrossoverProjectFile loaded = VirtualCrossoverProjectFile.LoadFrom(path);

            Assert.True(loaded.ShowLossCurve);
            Assert.Equal("woofer", loaded.Pairs[0].Left.DisplayName);
            Assert.Equal(1.25, loaded.Pairs[0].Right.DelayMs);
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
    public void LoadOrDefault_MigratesAVersion1SingleSidedProjectToPairs()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            // A real v1 payload shape: a "channels" list, no "pairs".
            string path = VirtualCrossoverProjectFile.GetPath(root);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, """
                {
                  "format": "resonalyze-virtual-crossover",
                  "version": 1,
                  "channels": [
                    {
                      "displayName": "woofer.json",
                      "sourceFilePath": "C:\\m\\woofer.json",
                      "gainDb": -2.5,
                      "delayMs": 4.78,
                      "invertPolarity": true,
                      "crossoverKind": "LowPass",
                      "lowPassEdge": { "family": "Butterworth", "frequencyHz": 175, "slopeDbPerOctave": 24 }
                    },
                    { "displayName": "", "gainDb": 0, "delayMs": 0 }
                  ],
                  "showSumCurve": true
                }
                """);

            VirtualCrossoverProjectFile loaded =
                VirtualCrossoverProjectFile.LoadOrDefault(root);

            // The historical single-sided channels become the LEFT sides of
            // fresh pairs; the right sides start empty and nothing is lost.
            Assert.Equal(VirtualCrossoverProjectFile.CurrentVersion, loaded.Version);
            Assert.Null(loaded.BackupNoticePath);
            Assert.Equal(2, loaded.Pairs.Count);
            Assert.Empty(loaded.Channels);
            VirtualCrossoverChannelSettings woofer = loaded.Pairs[0].Left;
            Assert.Equal("woofer.json", woofer.DisplayName);
            Assert.Equal(-2.5, woofer.GainDb);
            Assert.Equal(4.78, woofer.DelayMs);
            Assert.True(woofer.InvertPolarity);
            Assert.Equal(CrossoverKind.LowPass, woofer.CrossoverKind);
            Assert.False(loaded.Pairs[0].Mono);
            Assert.False(loaded.Pairs[0].Right.HasSource);

            // The migrated project persists as v2 and round-trips.
            loaded.Save(root);
            VirtualCrossoverProjectFile reloaded =
                VirtualCrossoverProjectFile.LoadOrDefault(root);
            Assert.Equal(2, reloaded.Pairs.Count);
            Assert.Equal("woofer.json", reloaded.Pairs[0].Left.DisplayName);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SideFor_MonoPairAnswersWithTheLeftSideForBothViews()
    {
        var pair = new VirtualCrossoverChannelPairSettings { Mono = true };
        pair.Left.GainDb = -3;
        pair.Right.GainDb = 12;

        Assert.Same(pair.Left, pair.SideFor(rightSide: false));
        Assert.Same(pair.Left, pair.SideFor(rightSide: true));

        pair.Mono = false;
        Assert.Same(pair.Right, pair.SideFor(rightSide: true));
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
