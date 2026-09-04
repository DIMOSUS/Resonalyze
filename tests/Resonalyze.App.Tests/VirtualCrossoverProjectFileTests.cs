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
    public void LoadOrDefault_V7ProjectWithAFullBank_KeepsTheAllPassAndSaysWhatItCost()
    {
        // A v7 side could hold all 32 bands AND an all-pass stage beside them; v8 has
        // no room for both. Something is lost either way, so the migration loses the
        // one that can be put back — a bell is a magnitude correction Auto Tune can
        // propose again, an all-pass sits on a junction aligned by ear — and says so
        // instead of reporting a clean conversion over a changed tune.
        string root = CreateTemporaryDirectory();
        try
        {
            var saved = new VirtualCrossoverProjectFile();
            for (int i = 0; i < EqualizationCurve.MaxBandCount; i++)
            {
                saved.Pairs[0].Left.PeqBands.Add(new PeqBand(100 + i, 2.0, -1.0));
            }

            saved.Save(root);

            string path = VirtualCrossoverProjectFile.GetPath(root);
            JsonNode file = JsonNode.Parse(File.ReadAllText(path))!;
            file["version"] = 7;
            JsonObject left = file["pairs"]![0]!["left"]!.AsObject();
            left["allPassType"] = "SecondOrder";
            left["allPassFrequencyHz"] = 120;
            left["allPassQ"] = 2.5;
            File.WriteAllText(path, file.ToJsonString());

            VirtualCrossoverProjectFile loaded =
                VirtualCrossoverProjectFile.LoadOrDefault(root);

            List<PeqBand> bands = loaded.Pairs[0].Left.PeqBands;
            Assert.Equal(EqualizationCurve.MaxBandCount, bands.Count);
            // The all-pass is there, and the band it displaced is the LAST bell — the
            // ones before it are untouched.
            Assert.Equal(
                new PeqBand(120, 2.5, 0, PeqBandType.AllPassSecondOrder), bands[^1]);
            Assert.Equal(
                Enumerable.Range(0, EqualizationCurve.MaxBandCount - 1)
                    .Select(i => new PeqBand(100 + i, 2.0, -1.0)),
                bands.Take(EqualizationCurve.MaxBandCount - 1));

            // And the load says what it cost, so the next save is not the first the
            // user hears of it.
            Assert.NotNull(loaded.MigrationNoticeText);
            Assert.Contains("all-pass", loaded.MigrationNoticeText!);
            loaded.Validate();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadOrDefault_V7ProjectThatLosesNothing_SaysNothing()
    {
        // The notice is for a migration that COST something. A file with room for its
        // all-pass converts silently, or every opened session would cry wolf.
        string root = CreateTemporaryDirectory();
        try
        {
            new VirtualCrossoverProjectFile().Save(root);
            string path = VirtualCrossoverProjectFile.GetPath(root);
            JsonNode file = JsonNode.Parse(File.ReadAllText(path))!;
            file["version"] = 7;
            JsonObject left = file["pairs"]![0]!["left"]!.AsObject();
            left["allPassType"] = "SecondOrder";
            left["allPassFrequencyHz"] = 120;
            left["allPassQ"] = 2.5;
            File.WriteAllText(path, file.ToJsonString());

            VirtualCrossoverProjectFile loaded =
                VirtualCrossoverProjectFile.LoadOrDefault(root);

            Assert.Single(loaded.Pairs[0].Left.PeqBands);
            Assert.Null(loaded.MigrationNoticeText);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadOrDefault_V7ProjectWithAnUnreadableAllPassType_KeepsTheRestOfTheFile()
    {
        // The tolerance the migration promises has to cover the TYPE as well as the
        // numbers. Typed as the enum it never could: the converter throws on a name
        // it does not know, and that throw lands during deserialization — before
        // Migrate runs — taking the whole session to .backup over one bad word.
        string root = CreateTemporaryDirectory();
        try
        {
            var saved = new VirtualCrossoverProjectFile();
            saved.Pairs[0].Left.DelayMs = 4.25;
            saved.Save(root);

            string path = VirtualCrossoverProjectFile.GetPath(root);
            JsonNode file = JsonNode.Parse(File.ReadAllText(path))!;
            file["version"] = 7;
            JsonObject left = file["pairs"]![0]!["left"]!.AsObject();
            left["allPassType"] = "SomeGarbage";
            left["allPassFrequencyHz"] = 120;
            left["allPassQ"] = 2.5;
            File.WriteAllText(path, file.ToJsonString());

            VirtualCrossoverProjectFile loaded =
                VirtualCrossoverProjectFile.LoadOrDefault(root);

            // The all-pass is gone, and NOTHING else is: the rest of the tune loaded.
            Assert.Empty(loaded.Pairs[0].Left.PeqBands);
            Assert.Equal(4.25, loaded.Pairs[0].Left.DelayMs);
            Assert.Null(loaded.BackupNoticePath);
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
    public void ANewProject_OpensOnAGateThatReachesTheBassJunctions()
    {
        // The tool exists to align channels across their junctions, and the lowest of
        // those sits in the bass — a junction-length gate (Phase Response mode's own
        // default) reads nothing below ~170 Hz, which is where the sub meets the
        // midbass. So the phase view opens on a long window, read through FDW so the
        // mid and high junctions still see the direct arrival rather than the whole
        // reflection tail, and unpinned, so it follows each side's own front.
        var project = new VirtualCrossoverProjectFile();

        Assert.Equal(
            24.0,
            FrequencyResponseOptions.GateMinReliableFrequencyHz(
                project.PhaseGateLeftMs,
                project.PhaseGatePlateauMs,
                project.PhaseGateRightMs),
            0);
        Assert.Equal(PhaseWindowMode.FrequencyDependent, project.PhaseWindowMode);
        Assert.Equal(8, project.PhaseFdwCycles);
        Assert.Equal(PhaseDetrendMode.Auto, project.PhaseDetrendMode);
        foreach (bool rightSide in new[] { false, true })
        {
            Assert.Null(project.PhaseGateFor(rightSide).OffsetMs);
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

        Complex response = settings.ToChain(VirtualCrossoverZone.Front)
            .Response(1_000, 48_000);

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
    public void AStatedProcessorRate_SurvivesTheMeasurementsBeingReplaced()
    {
        // "Follow the measurements" and "48 kHz" describe the same simulation while
        // the measurements are at 48 kHz, and they part company the moment those are
        // replaced — so the two must be stored apart. Stating 48 kHz keeps 48 kHz.
        var stated = new VirtualCrossoverProjectFile();
        stated.SetDspProcessor(
            DspProcessorProfile.Custom(48_000, PeqQConvention.Rbj),
            followsMeasurements: false);

        Assert.Equal(48_000, stated.DspProcessorSampleRateHz);
        Assert.False(stated.DspProcessorRateFollowsMeasurements);
        Assert.Equal(48_000, stated.ResolveDspProcessor(48_000).SampleRateHz);
        Assert.Equal(48_000, stated.ResolveDspProcessor(96_000).SampleRateHz);
    }

    [Fact]
    public void AFollowingProcessorRate_TracksWhateverTheMeasurementsAre()
    {
        var following = new VirtualCrossoverProjectFile();
        following.SetDspProcessor(
            DspProcessorProfile.Custom(48_000, PeqQConvention.Symmetric),
            followsMeasurements: true);

        Assert.Null(following.DspProcessorSampleRateHz);
        Assert.True(following.DspProcessorRateFollowsMeasurements);
        Assert.Equal(48_000, following.ResolveDspProcessor(48_000).SampleRateHz);
        Assert.Equal(96_000, following.ResolveDspProcessor(96_000).SampleRateHz);
        // The convention is the user's either way.
        Assert.Equal(
            PeqQConvention.Symmetric,
            following.ResolveDspProcessor(96_000).QConvention);
    }

    [Fact]
    public void ANamedModel_StatesItsOwnRateAndNeverFollows()
    {
        var project = new VirtualCrossoverProjectFile();
        DspProcessorProfile helix =
            DspProcessorCatalog.Preset("helix-dsp-ultra-s")!.ToProfile();

        // Even asked to follow: a device brings its own rate, and storing "follow"
        // for it would make the model's own preset a lie the next time it resolves.
        project.SetDspProcessor(helix, followsMeasurements: true);

        Assert.False(project.DspProcessorRateFollowsMeasurements);
        Assert.Equal(96_000, project.ResolveDspProcessor(44_100).SampleRateHz);
    }

    [Fact]
    public void SaveToAndLoadFrom_CarryTheNotesForAi_AndWriteNothingWhenThereAreNone()
    {
        // The notes are additive: a session without them must serialize exactly as
        // before the field existed (no key at all, so an older build resaves it
        // untouched), and one with them must bring them back verbatim, line breaks
        // included — they are the user's own words about the car.
        string root = CreateTemporaryDirectory();
        try
        {
            string path = Path.Combine(root, "session.json");
            var original = new VirtualCrossoverProjectFile();
            original.SaveTo(path);
            Assert.DoesNotContain("aiNotes", File.ReadAllText(path));
            Assert.Null(VirtualCrossoverProjectFile.LoadFrom(path).AiNotes);

            original.AiNotes = "   ";
            Assert.Null(original.AiNotes);

            const string notes = "2019 Passat B8, LHD.\r\nMids: Audiofrog GB60 in the doors.";
            original.AiNotes = notes;
            original.SaveTo(path);
            VirtualCrossoverProjectFile loaded = VirtualCrossoverProjectFile.LoadFrom(path);
            Assert.Equal(notes, loaded.AiNotes);
            Assert.Equal(VirtualCrossoverProjectFile.CurrentVersion, loaded.Version);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SaveToAndLoadFrom_CarryTheProcessorTheProjectIsDesignedFor()
    {
        // The processor decides the rate every simulated filter is BUILT at, so it is
        // part of the project rather than of the machine that opens it.
        string root = CreateTemporaryDirectory();
        try
        {
            var original = new VirtualCrossoverProjectFile
            {
                DspProcessorModelId = "helix-dsp-ultra-s",
                DspProcessorSampleRateHz = 96_000,
                DspProcessorQConvention = PeqQConvention.Symmetric
            };
            string path = Path.Combine(root, "session.json");

            original.SaveTo(path);
            VirtualCrossoverProjectFile loaded = VirtualCrossoverProjectFile.LoadFrom(path);

            Assert.Equal("helix-dsp-ultra-s", loaded.DspProcessorModelId);
            Assert.Equal(96_000, loaded.DspProcessorSampleRateHz);
            Assert.Equal(PeqQConvention.Symmetric, loaded.DspProcessorQConvention);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadOrDefault_AProjectFromBeforeTheProcessorSelector_FollowsItsMeasurements()
    {
        // Additive: an existing file names no processor, so it opens as Custom with no
        // stored rate — which the panel reads as "follow the measurements", the exact
        // simulation that file described.
        string root = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(root, "virtual-crossover.json"),
                """
                {
                  "format": "resonalyze-virtual-crossover",
                  "version": 8,
                  "pairs": [ { "left": { "displayName": "A" } } ]
                }
                """);

            VirtualCrossoverProjectFile loaded =
                VirtualCrossoverProjectFile.LoadOrDefault(root);

            Assert.Null(loaded.DspProcessorModelId);
            Assert.Null(loaded.DspProcessorSampleRateHz);
            Assert.Equal(PeqQConvention.Rbj, loaded.DspProcessorQConvention);
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

        DspChannelChain chain = channel.ToChain(VirtualCrossoverZone.Front);

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

        DspChannelChain chain = channel.ToChain(VirtualCrossoverZone.Front);

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
    public void CoherencePlotMode_RoundTripsAsALegacyValuePlusFlag()
    {
        // The second junction mode follows the correlation mode's additive
        // pattern, on its own flag; switching between the two junction modes
        // must move exactly one flag at a time — a file with both set would
        // silently open on the correlation view.
        string root = CreateTemporaryDirectory();
        try
        {
            var original = new VirtualCrossoverProjectFile();
            original.SetDspPlotMode(DspPlotMode.Coherence);
            Assert.Equal(DspPlotMode.Magnitude, original.DspPlotMode);
            Assert.True(original.DspPlotCoherenceView);
            Assert.False(original.DspPlotCorrelationView);
            Assert.Equal(DspPlotMode.Coherence, original.EffectiveDspPlotMode);

            original.Save(root);
            VirtualCrossoverProjectFile loaded =
                VirtualCrossoverProjectFile.LoadOrDefault(root);

            Assert.Equal(DspPlotMode.Coherence, loaded.EffectiveDspPlotMode);

            loaded.SetDspPlotMode(DspPlotMode.Correlation);
            Assert.True(loaded.DspPlotCorrelationView);
            Assert.False(loaded.DspPlotCoherenceView);

            loaded.SetDspPlotMode(DspPlotMode.Magnitude);
            Assert.False(loaded.DspPlotCorrelationView);
            Assert.False(loaded.DspPlotCoherenceView);
            Assert.Equal(DspPlotMode.Magnitude, loaded.DspPlotMode);
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
    public void Save_CarriesAnImportedTargetShapeByValue()
    {
        // A house curve is stored the way the rest of the target is — as what it
        // says, not as a path to the file it came from, which the session cannot
        // promise is still there (or still holds the same numbers) tomorrow.
        string root = CreateTemporaryDirectory();
        try
        {
            ImportedTargetCurve imported = ImportedTargetCurve.FromPoints(
                "house.txt",
                [
                    new OverlayPoint(30, 9),
                    new OverlayPoint(100, 6),
                    new OverlayPoint(1_000, 0),
                    new OverlayPoint(10_000, -3)
                ])!;
            var curve = new EqTargetCurve(
                TargetPreset.Car,
                TargetCurveSpec.FromPreset(TargetPreset.Car) with { Imported = imported },
                ToleranceDb: 3,
                TargetDeviationMode.Deviation,
                System.Drawing.Color.FromArgb(255, 55, 200, 160),
                StrokeThickness: 2,
                OverlayLineStyle.Dash,
                SmoothingInverseOctaves: 0);
            var saved = new VirtualCrossoverProjectFile
            {
                Target = VirtualCrossoverTargetSettings.FromCurve(curve)
            };
            saved.Save(root);

            VirtualCrossoverProjectFile loaded =
                VirtualCrossoverProjectFile.LoadOrDefault(root);

            Assert.NotNull(loaded.Target);
            EqTargetCurve restored = loaded.Target!.ToCurve();
            Assert.Equal(imported, restored.Spec.Imported);
            Assert.Equal(curve, restored);
            // The parametric terms travel beside it: they are what picking a
            // preset in the target dialog goes back to.
            Assert.Equal(
                TargetCurveSpec.FromPreset(TargetPreset.Car).BassShelfGainDb,
                restored.Spec.BassShelfGainDb);
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

    /// <summary>
    /// A session remembers which moving-microphone capture each side was tuned with.
    /// Re-picking seven files by hand on every open would make the hybrid view a
    /// ceremony rather than a toggle.
    /// </summary>
    [Fact]
    public void SaveToAndLoadFrom_RoundTripTheSpatialAverageReference()
    {
        string root = CreateTemporaryDirectory();
        string path = Path.Combine(root, "session.json");
        try
        {
            var original = new VirtualCrossoverProjectFile();
            original.Pairs[0].Left.SpatialAveragePath = Path.Combine(root, "sub mmm.json");

            original.SaveTo(path);
            VirtualCrossoverProjectFile loaded = VirtualCrossoverProjectFile.LoadFrom(path);

            Assert.Equal(
                original.Pairs[0].Left.SpatialAveragePath,
                loaded.Pairs[0].Left.SpatialAveragePath);
            // Additive: a session written before the hybrid view existed carries none,
            // and opens with nothing attached rather than failing to open.
            Assert.Null(loaded.Pairs[1].Left.SpatialAveragePath);
            // The view goes with the captures: bringing them back and then opening on
            // the point measurements would mean re-ticking the toggle every time.
            Assert.False(loaded.ShowHybridCurves);
            original.ShowHybridCurves = true;
            original.SaveTo(path);
            Assert.True(VirtualCrossoverProjectFile.LoadFrom(path).ShowHybridCurves);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// The capture travels with the measurements, so an export restates its path
    /// against the export's own folder — the same rescue the sources get, and the
    /// only thing that lets a session opened on another machine find it at all.
    /// </summary>
    [Fact]
    public void SaveTo_RestatesTheSpatialAveragePathAgainstTheExportsFolder()
    {
        string root = CreateTemporaryDirectory();
        string path = Path.Combine(root, "session.json");
        try
        {
            var original = new VirtualCrossoverProjectFile();
            original.Pairs[0].Left.SpatialAveragePath =
                Path.Combine(root, "captures", "sub mmm.json");

            original.SaveTo(path);
            VirtualCrossoverProjectFile loaded = VirtualCrossoverProjectFile.LoadFrom(path);

            Assert.Equal(
                Path.Combine("captures", "sub mmm.json"),
                loaded.Pairs[0].Left.SpatialAverageRelativePath);
            // Strictly a property of the WRITE: the live project keeps whatever it
            // was imported with, so a relink still has the hint it needs.
            Assert.Null(original.Pairs[0].Left.SpatialAverageRelativePath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadOrDefault_V8Project_GuessesEachBlockZoneFromItsMonoFlagAndFilter()
    {
        // v9 gave every block a zone. A v8 file records none, so it is guessed from
        // the two facts such a file does hold — and the three branches are pinned
        // here because a user's saved tune is what walks through them. Mono meant
        // "shared subwoofer" for the tool's whole history, so a mono block is a Sub
        // unless it HIGH-PASSES, which no subwoofer does: that one is a centre.
        string root = CreateTemporaryDirectory();
        try
        {
            new VirtualCrossoverProjectFile().Save(root);

            string path = VirtualCrossoverProjectFile.GetPath(root);
            JsonNode file = JsonNode.Parse(File.ReadAllText(path))!;
            file["version"] = 8;

            // A stereo pair — the front stage, and the only reading a v8 file
            // supports (a rear pair is byte-for-byte identical in it).
            file["pairs"]![0]!["mono"] = false;
            file["pairs"]![0]!["left"]!["crossoverKind"] = "BandPass";

            // A mono block playing low: the historical shared subwoofer.
            file["pairs"]![1]!["mono"] = true;
            file["pairs"]![1]!["left"]!["crossoverKind"] = "LowPass";

            // A mono block high-passed at 290 Hz: a centre, not a sub.
            file["pairs"]![2]!["mono"] = true;
            file["pairs"]![2]!["left"]!["crossoverKind"] = "HighPass";
            File.WriteAllText(path, file.ToJsonString());

            VirtualCrossoverProjectFile loaded =
                VirtualCrossoverProjectFile.LoadOrDefault(root);

            Assert.Equal(VirtualCrossoverProjectFile.CurrentVersion, loaded.Version);
            Assert.Equal(VirtualCrossoverZone.Front, loaded.Pairs[0].Zone);
            Assert.Equal(VirtualCrossoverZone.Sub, loaded.Pairs[1].Zone);
            Assert.Equal(VirtualCrossoverZone.Center, loaded.Pairs[2].Zone);
            loaded.Validate();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadOrDefault_V8Project_KeepsEveryTunedSettingThroughTheZoneMigration()
    {
        // The zone step is purely additive, and that is the promise this test holds
        // the code to: a user opening a tune saved by the previous version must find
        // every delay, gain, polarity, crossover corner and PEQ band exactly where
        // they left it. A mis-guessed zone then costs one combo box, never a tune.
        string root = CreateTemporaryDirectory();
        try
        {
            var original = new VirtualCrossoverProjectFile();
            VirtualCrossoverChannelSettings tuned = original.Pairs[0].Left;
            tuned.GainDb = -3.5;
            tuned.DelayMs = 4.87;
            tuned.InvertPolarity = true;
            tuned.CrossoverKind = CrossoverKind.BandPass;
            tuned.HighPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 50, 24);
            tuned.LowPassEdge = new CrossoverEdge(CrossoverFilterFamily.Butterworth, 110, 18);
            tuned.PeqPreampDb = -2.5;
            tuned.PeqBands.Add(new PeqBand(110, 8, 5.3, PeqBandType.Peaking));
            tuned.PeqBands.Add(new PeqBand(74, 5.6, -2.7, PeqBandType.Peaking));
            original.Pairs[0].Mono = true;
            original.StereoSceneOffsetMs = 0.25;
            original.Save(root);

            // Back-date the payload to v8 without touching anything else, so the
            // only difference the loader sees is the version it migrates from.
            string path = VirtualCrossoverProjectFile.GetPath(root);
            JsonNode file = JsonNode.Parse(File.ReadAllText(path))!;
            file["version"] = 8;
            File.WriteAllText(path, file.ToJsonString());

            VirtualCrossoverProjectFile loaded =
                VirtualCrossoverProjectFile.LoadOrDefault(root);

            VirtualCrossoverChannelSettings migrated = loaded.Pairs[0].Left;
            Assert.Equal(-3.5, migrated.GainDb);
            Assert.Equal(4.87, migrated.DelayMs);
            Assert.True(migrated.InvertPolarity);
            Assert.Equal(CrossoverKind.BandPass, migrated.CrossoverKind);
            Assert.Equal(50, migrated.HighPassEdge.FrequencyHz);
            Assert.Equal(24, migrated.HighPassEdge.SlopeDbPerOctave);
            Assert.Equal(CrossoverFilterFamily.Butterworth, migrated.LowPassEdge.Family);
            Assert.Equal(110, migrated.LowPassEdge.FrequencyHz);
            Assert.Equal(18, migrated.LowPassEdge.SlopeDbPerOctave);
            Assert.Equal(-2.5, migrated.PeqPreampDb);
            Assert.Equal(2, migrated.PeqBands.Count);
            Assert.Equal(110, migrated.PeqBands[0].FrequencyHz);
            Assert.Equal(5.3, migrated.PeqBands[0].GainDb);
            Assert.Equal(74, migrated.PeqBands[1].FrequencyHz);
            Assert.True(loaded.Pairs[0].Mono);
            Assert.Equal(0.25, loaded.StereoSceneOffsetMs);
            loaded.Validate();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadFrom_ZoneOutsideTheEnum_IsRejectedRatherThanReadAsFront()
    {
        // The enum rides the wire as a name, so a typo cannot deserialize — but a
        // NUMBER can, and would land silently outside the enum. Validate has to
        // catch it: a block in no zone would be dropped from every grouped view
        // without a word.
        string root = CreateTemporaryDirectory();
        string path = Path.Combine(root, "session.json");
        try
        {
            new VirtualCrossoverProjectFile().SaveTo(path);
            JsonNode file = JsonNode.Parse(File.ReadAllText(path))!;
            file["pairs"]![0]!["zone"] = 99;
            File.WriteAllText(path, file.ToJsonString());

            Assert.Throws<InvalidDataException>(
                () => VirtualCrossoverProjectFile.LoadFrom(path));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Reset writes the project in MEMORY, not the autosave beside it: the panel
    /// saves on a debounce, so the file lags the screen by up to two seconds and on
    /// a never-saved session does not exist at all. The copy is an ordinary session
    /// file, so Load session… brings the tune back whole.
    /// </summary>
    [Fact]
    public void SaveResetBackup_WritesTheProjectItIsCalledOnRatherThanTheAutosave()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            var saved = new VirtualCrossoverProjectFile();
            saved.Pairs[0].Left.DelayMs = 5.0;
            saved.Save(root);

            // The panel's live project, an edit ahead of what reached the disk.
            var live = new VirtualCrossoverProjectFile();
            live.Pairs[0].Left.DelayMs = 7.0;

            (string? path, string? error) = live.SaveResetBackup(root);

            Assert.Null(error);
            Assert.Equal(VirtualCrossoverProjectFile.ResetBackupPath(root), path);
            Assert.Equal(
                7.0,
                VirtualCrossoverProjectFile.LoadFrom(path!).Pairs[0].Left.DelayMs);
            // And the autosave it did not come from is untouched.
            Assert.Equal(
                5.0,
                VirtualCrossoverProjectFile.LoadOrDefault(root).Pairs[0].Left.DelayMs);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// A session that has never been written has the most to lose, not the least:
    /// the old copy-the-file backup reported "nothing here" and let the reset run
    /// with a whole tune standing in memory.
    /// </summary>
    [Fact]
    public void SaveResetBackup_WritesEvenWhenNoAutosaveExists()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            var live = new VirtualCrossoverProjectFile();
            live.Pairs[0].Left.DelayMs = 3.25;

            (string? path, string? error) = live.SaveResetBackup(root);

            Assert.Null(error);
            Assert.False(File.Exists(VirtualCrossoverProjectFile.GetPath(root)));
            Assert.Equal(
                3.25,
                VirtualCrossoverProjectFile.LoadFrom(path!).Pairs[0].Left.DelayMs);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// The backup lives in the application data folder, where no measurement sits,
    /// so it writes source paths the way the autosave does — none at all. An EXPORT
    /// states them relative to its own folder, and reusing that here would write a
    /// confident wrong answer into a file meant to be loaded back.
    /// </summary>
    [Fact]
    public void SaveResetBackup_WritesNoRelativeSourcePaths()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            var live = new VirtualCrossoverProjectFile();
            live.Pairs[0].Left.SourceFilePath = Path.Combine(root, "driver.json");
            live.Pairs[0].Left.SourceRelativePath = @"..\elsewhere\driver.json";

            (string? path, _) = live.SaveResetBackup(root);

            Assert.Null(
                VirtualCrossoverProjectFile.LoadFrom(path!).Pairs[0].Left.SourceRelativePath);
            // The live project keeps the hint it was imported with.
            Assert.Equal(@"..\elsewhere\driver.json", live.Pairs[0].Left.SourceRelativePath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// The reset copy has its own name because the other backup holds a file the tool
    /// could NOT read: overwriting that with a tune it read perfectly well would throw
    /// away the only copy of an unreadable project while the user was still deciding
    /// what to do about it.
    /// </summary>
    [Fact]
    public void SaveResetBackup_LeavesTheUnreadableFileBackupAlone()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            new VirtualCrossoverProjectFile().Save(root);
            string unusable = VirtualCrossoverProjectFile.GetPath(root) + ".backup";
            File.WriteAllText(unusable, "the project that could not be read");

            new VirtualCrossoverProjectFile().SaveResetBackup(root);

            Assert.Equal("the project that could not be read", File.ReadAllText(unusable));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// One copy is kept, and it is the reset just performed — the question a reset
    /// leaves is "undo THAT", and a pile of dated files would be a second archive
    /// beside the user's own exported sessions.
    /// </summary>
    [Fact]
    public void SaveResetBackup_KeepsOnlyTheMostRecentCopy()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            var first = new VirtualCrossoverProjectFile();
            first.Pairs[0].Left.DelayMs = 1.0;
            first.SaveResetBackup(root);

            var second = new VirtualCrossoverProjectFile();
            second.Pairs[0].Left.DelayMs = 2.0;
            (string? path, _) = second.SaveResetBackup(root);

            Assert.Equal(
                2.0,
                VirtualCrossoverProjectFile.LoadFrom(path!).Pairs[0].Left.DelayMs);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PhaseReference_IsTheLowPassOnASubwoofer_AndTheHighPassEverywhereElse()
    {
        // The device's own rule, and the reason ToChain has to be told the block's
        // zone: the side alone cannot know which of its two corners the angle is
        // stated at.
        var settings = new VirtualCrossoverChannelSettings
        {
            CrossoverKind = CrossoverKind.BandPass,
            HighPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 250, 24),
            LowPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 2_800, 24)
        };

        Assert.Equal(2_800, settings.PhaseReferenceHz(VirtualCrossoverZone.Sub));
        Assert.Equal(250, settings.PhaseReferenceHz(VirtualCrossoverZone.Front));
        Assert.Equal(250, settings.PhaseReferenceHz(VirtualCrossoverZone.Rear));
        Assert.Equal(250, settings.PhaseReferenceHz(VirtualCrossoverZone.Center));
    }

    [Fact]
    public void PhaseReference_IsTheConfiguredCorner_EvenWithTheFilterSwitchedOff()
    {
        // Measured on the bench: a bypassed filter and one set to slope = OFF both go
        // on supplying the reference. Reading the ACTIVE crossover instead would put
        // the all-pass somewhere else on every channel whose filter is disabled.
        var settings = new VirtualCrossoverChannelSettings
        {
            CrossoverKind = CrossoverKind.Off,
            HighPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 500, 24),
            LowPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 5_000, 24),
            PhaseRotationDegrees = 180
        };

        Assert.Equal(500, settings.PhaseReferenceHz(VirtualCrossoverZone.Front));

        DspChannelChain chain = settings.ToChain(VirtualCrossoverZone.Front);

        Assert.Equal(CrossoverKind.Off, chain.Crossover!.Kind);
        // 180 degrees puts the all-pass corner ON the reference, so the chain turns
        // the phase exactly half a turn at 500 Hz while leaving the level alone.
        Assert.Equal(-180.0, chain.Response(500, 96_000).Phase * 180.0 / Math.PI, 0.01);
        Assert.Equal(1.0, chain.Response(500, 96_000).Magnitude, 9);
    }

    [Fact]
    public void SaveToAndLoadFrom_CarryThePhaseRotationAndTheProcessorsPhaseSwitch()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            var original = new VirtualCrossoverProjectFile
            {
                DspProcessorPhaseControl = true
            };
            original.Pairs[0].Zone = VirtualCrossoverZone.Sub;
            original.Pairs[0].Left.PhaseRotationDegrees = 56.25;
            string path = Path.Combine(root, "session.json");

            original.SaveTo(path);
            VirtualCrossoverProjectFile loaded = VirtualCrossoverProjectFile.LoadFrom(path);

            Assert.Equal(56.25, loaded.Pairs[0].Left.PhaseRotationDegrees);
            Assert.True(loaded.DspProcessorPhaseControl);
            Assert.Equal(0, loaded.Pairs[1].Left.PhaseRotationDegrees);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadOrDefault_AProjectFromBeforeThePhaseControl_OpensWithNoRotation()
    {
        // v9 -> v10 is additive: an absent angle is no rotation and an absent switch
        // is off, so the session opens as the simulation it was saved as.
        string root = CreateTemporaryDirectory();
        try
        {
            new VirtualCrossoverProjectFile().Save(root);
            string path = VirtualCrossoverProjectFile.GetPath(root);
            JsonNode file = JsonNode.Parse(File.ReadAllText(path))!;
            file["version"] = 9;
            file["pairs"]![0]!["left"]!.AsObject().Remove("phaseRotationDegrees");
            File.WriteAllText(path, file.ToJsonString());

            VirtualCrossoverProjectFile loaded =
                VirtualCrossoverProjectFile.LoadOrDefault(root);

            Assert.Equal(VirtualCrossoverProjectFile.CurrentVersion, loaded.Version);
            Assert.Equal(0, loaded.Pairs[0].Left.PhaseRotationDegrees);
            Assert.Null(loaded.DspProcessorPhaseControl);
            Assert.Null(loaded.MigrationNoticeText);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(360)]
    [InlineData(double.NaN)]
    public void Validate_RejectsAPhaseRotationOutsideTheControlsRange(double degrees)
    {
        var settings = new VirtualCrossoverChannelSettings { PhaseRotationDegrees = degrees };

        Assert.Throws<InvalidDataException>(settings.Validate);
    }

    [Fact]
    public void Validate_AcceptsAnAngleBetweenTwoPositions()
    {
        // Range only: the editors snap to the device's 64 positions, but an angle a
        // hand-edited file states between two of them is still a filter this library
        // can build, and refusing to OPEN the session over it would be worse.
        var settings = new VirtualCrossoverChannelSettings { PhaseRotationDegrees = 7 };

        settings.Validate();
    }

    [Fact]
    public void ResolveDspPhaseControl_AsksTheCatalogUntilTheUserAnswers()
    {
        // A project naming a device that HAS the control finds it without hunting
        // through a dialog; one that names something else, or nothing, does not.
        var helix = new VirtualCrossoverProjectFile { DspProcessorModelId = "helix-dsp-ultra-s" };
        var panacea = new VirtualCrossoverProjectFile { DspProcessorModelId = "amp-panacea-v1-v2" };
        var custom = new VirtualCrossoverProjectFile();

        Assert.True(helix.ResolveDspPhaseControl());
        Assert.False(panacea.ResolveDspPhaseControl());
        Assert.False(custom.ResolveDspPhaseControl());

        // And a stored answer wins over the catalog in both directions.
        helix.DspProcessorPhaseControl = false;
        custom.DspProcessorPhaseControl = true;

        Assert.False(helix.ResolveDspPhaseControl());
        Assert.True(custom.ResolveDspPhaseControl());
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
