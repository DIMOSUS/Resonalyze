using System.Numerics;
using System.Text.Json.Nodes;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class VirtualCrossoverProjectFileTests
{
    [Fact]
    public void LoadOrDefault_V7Project_MovesTheChannelAllPassIntoThePeqBank()
    {
        // v7 ran one all-pass per side as its own stage; v8 carries it as a band of the
        // PEQ bank. The migration is what stops a saved tune from silently losing its
        // phase rotation, so it is pinned per order: a second-order stage keeps its Q, a
        // first-order one has none to keep (the band stores the 1.0 the section ignores),
        // and a side that ran no all-pass must not gain a band out of nowhere.
        string root = CreateTemporaryDirectory();
        try
        {
            new VirtualCrossoverProjectFile().Save(root);

            // Rewrite the payload as v7 wrote it: the all-pass flat on each channel.
            string path = VirtualCrossoverProjectFile.GetPath(root);
            JsonNode file = JsonNode.Parse(File.ReadAllText(path))!;
            file["version"] = 7;
            JsonObject left = file["pairs"]![0]!["left"]!.AsObject();
            left["allPassType"] = "SecondOrder";
            left["allPassFrequencyHz"] = 120;
            left["allPassQ"] = 2.5;
            JsonObject right = file["pairs"]![0]!["right"]!.AsObject();
            right["allPassType"] = "FirstOrder";
            right["allPassFrequencyHz"] = 300;
            right["allPassQ"] = 4.0; // a first order has no Q; the migration drops it
            File.WriteAllText(path, file.ToJsonString());

            VirtualCrossoverProjectFile loaded =
                VirtualCrossoverProjectFile.LoadOrDefault(root);

            PeqBand migrated = Assert.Single(loaded.Pairs[0].Left.PeqBands);
            Assert.Equal(PeqBandType.AllPassSecondOrder, migrated.Type);
            Assert.Equal(120, migrated.FrequencyHz);
            Assert.Equal(2.5, migrated.Q);
            Assert.Equal(0, migrated.GainDb);

            PeqBand first = Assert.Single(loaded.Pairs[0].Right.PeqBands);
            Assert.Equal(PeqBandType.AllPassFirstOrder, first.Type);
            Assert.Equal(300, first.FrequencyHz);
            Assert.Equal(1.0, first.Q);

            // A side that carried no all-pass stays empty — the stage was off on every
            // channel of a default project but the first pair.
            Assert.Empty(loaded.Pairs[1].Left.PeqBands);

            // The migrated project must itself be valid, or the very next save would
            // throw on a tune the user only opened.
            loaded.Validate();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadOrDefault_V7ProjectWithANonsenseAllPass_DropsItRatherThanFailing()
    {
        // Migrate runs before Validate, so a hand-edited or truncated stage must degrade
        // to "no all-pass" instead of taking the whole session down: the user would lose
        // every other channel over one bad number.
        string root = CreateTemporaryDirectory();
        try
        {
            new VirtualCrossoverProjectFile().Save(root);

            string path = VirtualCrossoverProjectFile.GetPath(root);
            JsonNode file = JsonNode.Parse(File.ReadAllText(path))!;
            file["version"] = 7;
            JsonObject left = file["pairs"]![0]!["left"]!.AsObject();
            left["allPassType"] = "SecondOrder";
            left["allPassFrequencyHz"] = 0;
            left["allPassQ"] = 2.5;
            JsonObject right = file["pairs"]![0]!["right"]!.AsObject();
            right["allPassType"] = "SecondOrder";
            right["allPassFrequencyHz"] = 300;
            right["allPassQ"] = 0; // divides by zero in the section's alpha
            File.WriteAllText(path, file.ToJsonString());

            VirtualCrossoverProjectFile loaded =
                VirtualCrossoverProjectFile.LoadOrDefault(root);

            Assert.Empty(loaded.Pairs[0].Left.PeqBands);
            Assert.Empty(loaded.Pairs[0].Right.PeqBands);
            loaded.Validate();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadOrDefault_V4Project_CopiesItsOneGateOntoBothSides()
    {
        // v4 kept a single gate for the whole project. Both sides must inherit it, so a
        // migrated project draws exactly as it did before the split — the sides only
        // diverge once the user moves one of them.
        string root = CreateTemporaryDirectory();
        try
        {
            var saved = new VirtualCrossoverProjectFile();
            saved.Save(root);

            // Rewrite the payload as v4 wrote it: the gate flat on the project.
            string path = VirtualCrossoverProjectFile.GetPath(root);
            JsonNode file = JsonNode.Parse(File.ReadAllText(path))!;
            JsonObject root4 = file.AsObject();
            root4["version"] = 4;
            root4.Remove("phaseGateLeft");
            root4.Remove("phaseGateRight");
            root4["phaseGateOffsetMs"] = 12.34;
            root4["phaseGateLeftMs"] = 0.25;
            root4["phaseGatePlateauMs"] = 6.5;
            root4["phaseGateRightMs"] = 2.0;
            root4["phaseDetrendMs"] = 13.07;
            File.WriteAllText(path, file.ToJsonString());

            VirtualCrossoverProjectFile loaded =
                VirtualCrossoverProjectFile.LoadOrDefault(root);

            Assert.Equal(VirtualCrossoverProjectFile.CurrentVersion, loaded.Version);
            foreach (bool rightSide in new[] { false, true })
            {
                VirtualCrossoverPhaseGateSettings gate = loaded.PhaseGateFor(rightSide);
                Assert.Equal(12.34, gate.OffsetMs);
                Assert.Equal(13.07, gate.DetrendMs);
            }

            // The window's lengths never moved off the project, so they carry across
            // untouched rather than through the migration.
            Assert.Equal(0.25, loaded.PhaseGateLeftMs);
            Assert.Equal(6.5, loaded.PhaseGatePlateauMs);
            Assert.Equal(2.0, loaded.PhaseGateRightMs);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadOrDefault_V4ProjectOnAnAutoGate_KeepsBothSidesOnAuto()
    {
        // v4 spelled "follow the earliest arrival" as a null offset/detrend. That has to
        // migrate to the same thing, not to a pinned zero.
        string root = CreateTemporaryDirectory();
        try
        {
            new VirtualCrossoverProjectFile().Save(root);
            string path = VirtualCrossoverProjectFile.GetPath(root);
            JsonObject file = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            file["version"] = 4;
            file.Remove("phaseGateLeft");
            file.Remove("phaseGateRight");
            file["phaseGateOffsetMs"] = null;
            file["phaseDetrendMs"] = null;
            File.WriteAllText(path, file.ToJsonString());

            VirtualCrossoverProjectFile loaded =
                VirtualCrossoverProjectFile.LoadOrDefault(root);

            foreach (bool rightSide in new[] { false, true })
            {
                Assert.Null(loaded.PhaseGateFor(rightSide).OffsetMs);
                Assert.Null(loaded.PhaseGateFor(rightSide).DetrendMs);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadOrDefault_LegacyNegativeSceneOffset_BecomesRightHandDrive()
    {
        // The wire format carries the steering layout in the offset's SIGN
        // (negative = right-seated driver). A pre-flag payload must open as
        // an RHD project with the equivalent magnitude.
        string root = CreateTemporaryDirectory();
        try
        {
            new VirtualCrossoverProjectFile().Save(root);
            string path = VirtualCrossoverProjectFile.GetPath(root);
            JsonObject file = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            file["stereoSceneOffsetMs"] = -0.4;
            file.Remove("stereoRightHandDrive");
            File.WriteAllText(path, file.ToJsonString());

            VirtualCrossoverProjectFile loaded =
                VirtualCrossoverProjectFile.LoadOrDefault(root);

            Assert.True(loaded.StereoRightHandDrive);
            Assert.Equal(0.4, loaded.StereoSceneOffsetMagnitudeMs);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadOrDefault_RhdSurvivesAnOldBuildResaveThatDropsTheFlag()
    {
        // An older build knows nothing of stereoRightHandDrive: it reads the
        // layout from the offset's sign (the wire keeps the pre-flag
        // format exactly for this) and a resave DROPS the unknown flag. The
        // sign must carry the layout back in — without it the resave would
        // silently and permanently flip an RHD session to LHD.
        string root = CreateTemporaryDirectory();
        try
        {
            var saved = new VirtualCrossoverProjectFile();
            saved.SetStereoScene(0.25, rightHandDrive: true);
            saved.Save(root);
            string path = VirtualCrossoverProjectFile.GetPath(root);
            JsonObject file = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            Assert.Equal(-0.25, (double)file["stereoSceneOffsetMs"]!);
            file.Remove("stereoRightHandDrive");
            File.WriteAllText(path, file.ToJsonString());

            VirtualCrossoverProjectFile loaded =
                VirtualCrossoverProjectFile.LoadOrDefault(root);

            Assert.True(loaded.StereoRightHandDrive);
            Assert.Equal(0.25, loaded.StereoSceneOffsetMagnitudeMs);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadOrDefault_RhdWithZeroOffsetSurvivesAnOldBuildResave()
    {
        // The edge of the signed wire format: a zero magnitude has no sign
        // to carry the layout (IEEE -0.0 neither compares below zero nor
        // survives a decimal round-trip), so RHD+0 serializes as a tiny
        // negative marker the runtime reads back as zero. Without it, an
        // old build's resave — which drops the unknown flag — would
        // silently flip the session to LHD.
        string root = CreateTemporaryDirectory();
        try
        {
            var saved = new VirtualCrossoverProjectFile();
            saved.SetStereoScene(0, rightHandDrive: true);
            saved.Save(root);
            string path = VirtualCrossoverProjectFile.GetPath(root);
            JsonObject file = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            Assert.True((double)file["stereoSceneOffsetMs"]! < 0);
            file.Remove("stereoRightHandDrive");
            File.WriteAllText(path, file.ToJsonString());

            VirtualCrossoverProjectFile loaded =
                VirtualCrossoverProjectFile.LoadOrDefault(root);

            Assert.True(loaded.StereoRightHandDrive);
            Assert.Equal(0, loaded.StereoSceneOffsetMagnitudeMs);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AllPassBand_RoundTripsThroughTheProjectFile()
    {
        // The band type is the only thing separating a phase rotator from a bell with no
        // gain, and it travels as a string on the wire: a round trip that lost it would
        // reopen the tune with the all-pass silently turned into a transparent filter.
        string root = CreateTemporaryDirectory();
        try
        {
            var saved = new VirtualCrossoverProjectFile();
            saved.Pairs[1].Right.PeqBands.Add(
                new PeqBand(90, 3.5, 0, PeqBandType.AllPassSecondOrder));
            saved.Pairs[1].Right.PeqBands.Add(
                new PeqBand(300, 1.0, 0, PeqBandType.AllPassFirstOrder));
            saved.Save(root);

            VirtualCrossoverProjectFile loaded =
                VirtualCrossoverProjectFile.LoadOrDefault(root);

            Assert.Equal(saved.Pairs[1].Right.PeqBands, loaded.Pairs[1].Right.PeqBands);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ToChain_AppliesAnAllPassBandEvenWithTheCrossoverOff()
    {
        // The all-pass rides in the PEQ bank, and the bank is not gated by the crossover
        // kind. At its corner a second-order section is -180° with the magnitude
        // untouched — which is also what makes it invisible to a magnitude-only check.
        var settings = new VirtualCrossoverChannelSettings
        {
            CrossoverKind = CrossoverKind.Off,
            PeqBands =
            {
                new PeqBand(1_000, 1.0, 0, PeqBandType.AllPassSecondOrder)
            }
        };

        Complex response = settings.ToChain().Response(1_000, 48_000);

        Assert.Equal(1.0, response.Magnitude, 9);
        Assert.Equal(-1.0, response.Real, 6);
    }

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
                // Deliberately different per side: the round trip has to keep the
                // PLACEMENT apart, which is the whole point of it being per-side.
                PhaseGateLeft = new VirtualCrossoverPhaseGateSettings
                {
                    OffsetMs = 12.34,
                    DetrendMs = 13.07
                },
                PhaseGateRight = new VirtualCrossoverPhaseGateSettings
                {
                    OffsetMs = 15.5,
                    DetrendMs = 16.25
                },
                // …while the window's lengths are one setting for the whole project.
                PhaseGateLeftMs = 0.25,
                PhaseGatePlateauMs = 6.5,
                PhaseGateRightMs = 2.0,
                PhaseWindowMode = PhaseWindowMode.FrequencyDependent,
                PhaseFdwCycles = 8,
                PhaseDetrendMode = PhaseDetrendMode.Manual
            };
            original.SetStereoScene(0.4, rightHandDrive: true);
            original.StereoLevelDifferenceDb = -1.5;
            original.ActiveSideRight = true;
            original.Pairs[0].Mono = true;
            // Mute, Bypass and the curve toggles belong to the PAIR, not to a side.
            original.Pairs[0].Enabled = false;
            original.Pairs[0].Bypass = true;
            original.Pairs[0].ShowRawCurve = true;
            original.Pairs[0].ShowProcessedCurve = false;
            original.Pairs[0].Left = new VirtualCrossoverChannelSettings
            {
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
                PeqSourceName = "woofer-peq.txt"
            };

            original.Save(root);
            VirtualCrossoverProjectFile loaded =
                VirtualCrossoverProjectFile.LoadOrDefault(root);

            Assert.Equal(original.ShowSumCurve, loaded.ShowSumCurve);
            Assert.Equal(original.ShowLossCurve, loaded.ShowLossCurve);
            Assert.Equal(original.ShowPhaseView, loaded.ShowPhaseView);
            foreach (bool rightSide in new[] { false, true })
            {
                VirtualCrossoverPhaseGateSettings savedGate = original.PhaseGateFor(rightSide);
                VirtualCrossoverPhaseGateSettings loadedGate = loaded.PhaseGateFor(rightSide);
                Assert.Equal(savedGate.OffsetMs, loadedGate.OffsetMs);
                Assert.Equal(savedGate.DetrendMs, loadedGate.DetrendMs);
            }

            Assert.Equal(original.PhaseGateLeftMs, loaded.PhaseGateLeftMs);
            Assert.Equal(original.PhaseGatePlateauMs, loaded.PhaseGatePlateauMs);
            Assert.Equal(original.PhaseGateRightMs, loaded.PhaseGateRightMs);

            Assert.Equal(original.PhaseWindowMode, loaded.PhaseWindowMode);
            Assert.Equal(original.PhaseFdwCycles, loaded.PhaseFdwCycles);
            Assert.Equal(original.PhaseDetrendMode, loaded.PhaseDetrendMode);
            Assert.Equal(original.StereoSceneOffsetMs, loaded.StereoSceneOffsetMs);
            Assert.Equal(
                original.StereoRightHandDrive, loaded.StereoRightHandDrive);
            Assert.Equal(
                original.StereoLevelDifferenceDb, loaded.StereoLevelDifferenceDb);
            Assert.Equal(original.ActiveSideRight, loaded.ActiveSideRight);
            Assert.Equal(original.Pairs.Count, loaded.Pairs.Count);
            Assert.True(loaded.Pairs[0].Mono);
            Assert.Equal(original.Pairs[0].Enabled, loaded.Pairs[0].Enabled);
            Assert.Equal(original.Pairs[0].Bypass, loaded.Pairs[0].Bypass);
            Assert.Equal(
                original.Pairs[0].ShowRawCurve, loaded.Pairs[0].ShowRawCurve);
            Assert.Equal(
                original.Pairs[0].ShowProcessedCurve,
                loaded.Pairs[0].ShowProcessedCurve);

            VirtualCrossoverChannelSettings expected = original.Pairs[0].Left;
            VirtualCrossoverChannelSettings actual = loaded.Pairs[0].Left;
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
    public void SaveAndLoad_RoundTripsAMaxChannelProjectAndCalibrationSelection()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            var original = new VirtualCrossoverProjectFile
            {
                CalibrationId = "cal1",
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
            Assert.Equal("cal1", loaded.CalibrationId);
            Assert.Equal(DspPlotMode.GroupDelay, loaded.DspPlotMode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SaveToAndLoadFrom_CarryTheCalibrationCurveItself()
    {
        // The session states the correction it was tuned with as the CURVE, so a
        // machine that never configured that file draws what the author saw. The id
        // travels too, but only as a hint.
        string root = CreateTemporaryDirectory();
        try
        {
            CalibrationFile curve = CalibrationFile.Parse("16 0\n1000 -0.75\n20000 3.5\n");
            var original = new VirtualCrossoverProjectFile
            {
                CalibrationId = "90deg",
                Calibration = VirtualCrossoverCalibrationSettings.From(
                    curve, "90°", "ECM8000_90deg.txt")
            };
            string path = Path.Combine(root, "session.json");

            original.SaveTo(path);
            VirtualCrossoverProjectFile loaded = VirtualCrossoverProjectFile.LoadFrom(path);

            Assert.Equal("90deg", loaded.CalibrationId);
            Assert.NotNull(loaded.Calibration);
            Assert.Equal("90°", loaded.Calibration.Name);
            Assert.Equal("ECM8000_90deg.txt", loaded.Calibration.FileName);
            Assert.True(CalibrationFile.SameCurve(curve, loaded.Calibration.ToCalibrationFile()));

            // A curve from an estimate has no file: the name is what it has.
            string json = File.ReadAllText(path);
            Assert.Contains("\"calibration\"", json);
            Assert.Contains("\"fileName\"", json);
            original.Calibration.FileName = null;
            original.SaveTo(path);
            Assert.DoesNotContain("\"fileName\"", File.ReadAllText(path));
            Assert.Null(VirtualCrossoverProjectFile.LoadFrom(path).Calibration!.FileName);

            // Off, and a session written before the curve travelled, carry no block.
            original.Calibration = null;
            original.SaveTo(path);
            Assert.DoesNotContain("\"calibration\"", File.ReadAllText(path));
            Assert.Null(VirtualCrossoverProjectFile.LoadFrom(path).Calibration);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Validate_RejectsACalibrationCurveAFileCouldNotState()
    {
        var tooFew = new VirtualCrossoverProjectFile
        {
            Calibration = new VirtualCrossoverCalibrationSettings
            {
                Name = "x",
                Points = { new[] { 1000.0, 0.0 } }
            }
        };
        Assert.Throws<InvalidDataException>(() => tooFew.Validate());

        // Two points at one frequency merge into one knot on reading: no curve, and
        // the merged form would fail this check on the next save.
        var oneFrequency = new VirtualCrossoverProjectFile
        {
            Calibration = new VirtualCrossoverCalibrationSettings
            {
                Name = "x",
                Points = { new[] { 1000.0, 0.0 }, new[] { 1000.0, 1.0 } }
            }
        };
        Assert.Throws<InvalidDataException>(() => oneFrequency.Validate());

        var badFrequency = new VirtualCrossoverProjectFile
        {
            Calibration = new VirtualCrossoverCalibrationSettings
            {
                Name = "x",
                Points = { new[] { 0.0, 0.0 }, new[] { 1000.0, 0.0 } }
            }
        };
        Assert.Throws<InvalidDataException>(() => badFrequency.Validate());

        var badShape = new VirtualCrossoverProjectFile
        {
            Calibration = new VirtualCrossoverCalibrationSettings
            {
                Name = "x",
                Points = { new[] { 20.0 }, new[] { 1000.0, 0.0 } }
            }
        };
        Assert.Throws<InvalidDataException>(() => badShape.Validate());

        var badLevel = new VirtualCrossoverProjectFile
        {
            Calibration = new VirtualCrossoverCalibrationSettings
            {
                Name = "x",
                Points = { new[] { 20.0, double.NaN }, new[] { 1000.0, 0.0 } }
            }
        };
        Assert.Throws<InvalidDataException>(() => badLevel.Validate());

        var fine = new VirtualCrossoverProjectFile
        {
            Calibration = new VirtualCrossoverCalibrationSettings
            {
                Name = "  x  ",
                FileName = " ",
                Points = { new[] { 20.0, 0.0 }, new[] { 1000.0, 0.0 } }
            }
        };
        fine.Validate();
        Assert.Equal("x", fine.Calibration.Name);
        Assert.Null(fine.Calibration.FileName);
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

        var badLevelDifference = new VirtualCrossoverProjectFile
        {
            StereoLevelDifferenceDb = GainBalanceEngine.MaxLevelDifferenceDb + 1
        };
        Assert.Throws<InvalidDataException>(() => badLevelDifference.Validate());

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

        // Both sides are validated, not just the left: an imported project must not
        // smuggle a broken gate in through the side that happens to be off screen.
        var badGateOffset = new VirtualCrossoverProjectFile();
        badGateOffset.PhaseGateLeft.OffsetMs = -1;
        Assert.Throws<InvalidDataException>(() => badGateOffset.Validate());

        var badDetrend = new VirtualCrossoverProjectFile();
        badDetrend.PhaseGateRight.DetrendMs = double.NaN;
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
    public void SaveToAndLoadFrom_RoundTripTheFoldedBlocksAndOpenOlderFilesExpanded()
    {
        string root = CreateTemporaryDirectory();
        string path = Path.Combine(root, "folded.json");
        try
        {
            var original = new VirtualCrossoverProjectFile();
            original.Pairs[0].Collapsed = true;

            original.SaveTo(path);
            VirtualCrossoverProjectFile loaded = VirtualCrossoverProjectFile.LoadFrom(path);

            Assert.True(loaded.Pairs[0].Collapsed);
            // The flag is additive: a file written before it existed simply has no
            // such property, and its blocks open the way they always did.
            Assert.False(loaded.Pairs[1].Collapsed);
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

    [Fact]
    public void PsychoacousticSmoothing_RoundTripsAsAPlainWidthPlusFlag()
    {
        // The psychoacoustic mode persists as its plain base width plus a
        // separate additive flag, so an OLDER build opens the session as plain
        // 1/6-octave smoothing instead of rejecting an unknown code — the same
        // pattern as every other additive project field.
        string root = CreateTemporaryDirectory();
        try
        {
            var original = new VirtualCrossoverProjectFile();
            original.SetSmoothingCode(Dsp.SpectrumSmoothing.PsychoacousticCode);
            Assert.Equal(
                Dsp.SpectrumSmoothing.PsychoacousticBaseInverseOctaves,
                original.SmoothingInverseOctaves);
            Assert.True(original.PsychoacousticSmoothing);

            original.Save(root);
            VirtualCrossoverProjectFile loaded =
                VirtualCrossoverProjectFile.LoadOrDefault(root);

            Assert.Equal(
                Dsp.SpectrumSmoothing.PsychoacousticCode, loaded.SmoothingCode);
            // Selecting a plain width afterwards clears the flag.
            loaded.SetSmoothingCode(12);
            Assert.False(loaded.PsychoacousticSmoothing);
            Assert.Equal(12, loaded.SmoothingCode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CorrelationPlotMode_RoundTripsAsALegacyValuePlusFlag()
    {
        // Same additive pattern as the psychoacoustic smoothing: the stored
        // enum field keeps a value every build can parse (an older build opens
        // the session on the magnitude view), the correlation mode travels in
        // its own flag, and the selected pair index rides along.
        string root = CreateTemporaryDirectory();
        try
        {
            var original = new VirtualCrossoverProjectFile();
            original.SetDspPlotMode(DspPlotMode.Correlation);
            original.CorrelationPairIndex = 2;
            Assert.Equal(DspPlotMode.Magnitude, original.DspPlotMode);
            Assert.True(original.DspPlotCorrelationView);
            Assert.Equal(
                DspPlotMode.Correlation, original.EffectiveDspPlotMode);

            original.Save(root);
            VirtualCrossoverProjectFile loaded =
                VirtualCrossoverProjectFile.LoadOrDefault(root);

            Assert.Equal(DspPlotMode.Correlation, loaded.EffectiveDspPlotMode);
            Assert.Equal(2, loaded.CorrelationPairIndex);

            // Selecting a chain view afterwards clears the flag and stores the
            // mode plainly.
            loaded.SetDspPlotMode(DspPlotMode.GroupDelay);
            Assert.False(loaded.DspPlotCorrelationView);
            Assert.Equal(DspPlotMode.GroupDelay, loaded.DspPlotMode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ShowSumCurveOnPhase_WithNoAnswerOfItsOwn_InheritsTheMagnitudeOne()
    {
        // A session written before the two views answered separately carries one
        // flag, and that is what it used to draw on BOTH plots. Inheriting it is
        // what makes such a file open looking the way it was left.
        var project = new VirtualCrossoverProjectFile { ShowSumCurve = false };
        Assert.Null(project.ShowSumCurvePhase);
        Assert.False(project.ShowSumCurveOnPhase);

        project.ShowSumCurve = true;
        Assert.True(project.ShowSumCurveOnPhase);
    }

    [Fact]
    public void ShowSumCurveOnPhase_OnceAnswered_StopsFollowingTheMagnitudeOne()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            var saved = new VirtualCrossoverProjectFile
            {
                ShowSumCurve = false,
                ShowSumCurveOnPhase = true
            };
            Assert.False(saved.ShowSumCurve);
            Assert.True(saved.ShowSumCurveOnPhase);

            saved.Save(root);
            VirtualCrossoverProjectFile loaded =
                VirtualCrossoverProjectFile.LoadOrDefault(root);

            Assert.False(loaded.ShowSumCurve);
            Assert.True(loaded.ShowSumCurveOnPhase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadOrDefault_ProjectWithoutATarget_LoadsWithTheCurveHidden()
    {
        // The EQ target on the acoustic plot is additive, like the all-pass above:
        // a session written before it existed carries neither key. Where its
        // absence lands matters — a target the user never asked for must not
        // appear over their sum, and it must not appear at some inherited level.
        string root = CreateTemporaryDirectory();
        try
        {
            var saved = new VirtualCrossoverProjectFile
            {
                ShowTargetCurve = true,
                TargetLevelDb = -38
            };
            saved.Save(root);

            string path = VirtualCrossoverProjectFile.GetPath(root);
            JsonNode file = JsonNode.Parse(File.ReadAllText(path))!;
            JsonObject project = file.AsObject();
            Assert.True(project.Remove("showTargetCurve"));
            Assert.True(project.Remove("targetLevelDb"));
            File.WriteAllText(path, file.ToJsonString());

            VirtualCrossoverProjectFile loaded =
                VirtualCrossoverProjectFile.LoadOrDefault(root);

            Assert.False(loaded.ShowTargetCurve);
            Assert.Equal(0, loaded.TargetLevelDb);
            loaded.Validate();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Save_CarriesTheCustomTargetShapeUnchanged()
    {
        // The shape itself, not a preset name: a preset's numbers can change
        // between versions, while a session has to open aiming at exactly the
        // curve it was tuned against.
        string root = CreateTemporaryDirectory();
        try
        {
            var curve = new EqTargetCurve(
                TargetPreset.Custom,
                new TargetCurveSpec(
                    -0.65, 7.5, 92, 0.8, -2.5, 9_500, 0.65, 1.25, 2_650, 1.1),
                ToleranceDb: 2.5,
                TargetDeviationMode.Deviation,
                System.Drawing.Color.FromArgb(255, 240, 120, 40),
                StrokeThickness: 3.5,
                OverlayLineStyle.DashDot,
                SmoothingInverseOctaves: 6);
            var saved = new VirtualCrossoverProjectFile
            {
                Target = VirtualCrossoverTargetSettings.FromCurve(curve)
            };
            saved.Save(root);

            VirtualCrossoverProjectFile loaded =
                VirtualCrossoverProjectFile.LoadOrDefault(root);

            Assert.NotNull(loaded.Target);
            // Value equality on the record is the check: a field the mapping
            // dropped would hand back a different curve than the one tuned.
            Assert.Equal(curve, loaded.Target!.ToCurve());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadOrDefault_ProjectWithoutATarget_CarriesNone()
    {
        // Absence is a real state, and it means "no target of its own" rather
        // than a default one: the panel keeps the app's current target and
        // starts storing it, instead of retuning it to a flat line.
        string root = CreateTemporaryDirectory();
        try
        {
            new VirtualCrossoverProjectFile { ShowTargetCurve = true }.Save(root);

            string path = VirtualCrossoverProjectFile.GetPath(root);
            JsonNode file = JsonNode.Parse(File.ReadAllText(path))!;
            file.AsObject().Remove("target");
            File.WriteAllText(path, file.ToJsonString());

            VirtualCrossoverProjectFile loaded =
                VirtualCrossoverProjectFile.LoadOrDefault(root);

            Assert.Null(loaded.Target);
            loaded.Validate();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Save_KeepsTheTargetVisibilityAndLevel()
    {
        // The target SHAPE is the EQ Wizard's and is deliberately not stored
        // here; the level is, because it belongs to this plot's dB reference.
        string root = CreateTemporaryDirectory();
        try
        {
            var saved = new VirtualCrossoverProjectFile
            {
                ShowTargetCurve = true,
                TargetLevelDb = -38.5
            };
            saved.Save(root);

            VirtualCrossoverProjectFile loaded =
                VirtualCrossoverProjectFile.LoadOrDefault(root);

            Assert.True(loaded.ShowTargetCurve);
            Assert.Equal(-38.5, loaded.TargetLevelDb);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadOrDefault_V6Project_ReadsTheSwitchesOffTheLoadedSides()
    {
        // v6 kept Mute, Bypass and the curve toggles per side. The pair inherits the
        // sides that actually carry a measurement, so a mono pair (its right slot is
        // unreachable and still holds the defaults) opens exactly as it looked.
        string root = CreateTemporaryDirectory();
        try
        {
            var saved = new VirtualCrossoverProjectFile();
            saved.Save(root);

            string path = VirtualCrossoverProjectFile.GetPath(root);
            JsonNode file = JsonNode.Parse(File.ReadAllText(path))!;
            file["version"] = 6;
            JsonObject pair = file["pairs"]![0]!.AsObject();
            pair["mono"] = true;
            foreach (string moved in
                new[] { "enabled", "bypass", "showRawCurve", "showProcessedCurve" })
            {
                pair.Remove(moved);
            }

            JsonObject left = pair["left"]!.AsObject();
            left["sourceFilePath"] = @"C:\measurements\sub.json";
            left["enabled"] = false;
            left["bypass"] = true;
            left["showRawCurve"] = true;
            left["showProcessedCurve"] = false;
            // The unreachable right slot still holds v6 defaults; they must not win.
            JsonObject right = pair["right"]!.AsObject();
            right["enabled"] = true;
            right["bypass"] = false;
            right["showRawCurve"] = false;
            right["showProcessedCurve"] = true;
            File.WriteAllText(path, file.ToJsonString());

            VirtualCrossoverProjectFile loaded =
                VirtualCrossoverProjectFile.LoadOrDefault(root);

            Assert.Equal(VirtualCrossoverProjectFile.CurrentVersion, loaded.Version);
            Assert.False(loaded.Pairs[0].Enabled);
            Assert.True(loaded.Pairs[0].Bypass);
            Assert.True(loaded.Pairs[0].ShowRawCurve);
            Assert.False(loaded.Pairs[0].ShowProcessedCurve);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadOrDefault_V6ProjectWhoseSidesDisagree_KeepsTheLouderAnswer()
    {
        // Two loaded sides could hold opposite switches, and one answer has to win.
        // Muted, bypassed and "curve shown" each survive: a mute lost in a migration
        // is the one outcome the tuner has no way to see coming.
        string root = CreateTemporaryDirectory();
        try
        {
            var saved = new VirtualCrossoverProjectFile();
            saved.Save(root);

            string path = VirtualCrossoverProjectFile.GetPath(root);
            JsonNode file = JsonNode.Parse(File.ReadAllText(path))!;
            file["version"] = 6;
            JsonObject pair = file["pairs"]![0]!.AsObject();
            foreach (string moved in
                new[] { "enabled", "bypass", "showRawCurve", "showProcessedCurve" })
            {
                pair.Remove(moved);
            }

            JsonObject left = pair["left"]!.AsObject();
            left["sourceFilePath"] = @"C:\measurements	weeter-l.json";
            left["enabled"] = false;
            left["bypass"] = false;
            left["showRawCurve"] = true;
            JsonObject right = pair["right"]!.AsObject();
            right["sourceFilePath"] = @"C:\measurements	weeter-r.json";
            right["enabled"] = true;
            right["bypass"] = true;
            right["showRawCurve"] = false;
            File.WriteAllText(path, file.ToJsonString());

            VirtualCrossoverProjectFile loaded =
                VirtualCrossoverProjectFile.LoadOrDefault(root);

            Assert.False(loaded.Pairs[0].Enabled);
            Assert.True(loaded.Pairs[0].Bypass);
            Assert.True(loaded.Pairs[0].ShowRawCurve);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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
