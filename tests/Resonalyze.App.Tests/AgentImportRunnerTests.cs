using System.Globalization;
using System.Text.RegularExpressions;
using Resonalyze.Dsp;
using Resonalyze.Integration.AgentBridge;
using static Resonalyze.App.Tests.AgentImportFixture;

namespace Resonalyze.App.Tests;

/// <summary>The import once the review is answered: probes, the re-check, the rows, the engines in their order, and undo,
/// which snapshots the whole chain, the spatial average, the Hybrid tick and the block order.</summary>
[Collection(AgentClipboardUsers.Name)]
public sealed class AgentImportRunnerTests : IDisposable
{
    private readonly AgentImportFixture import = new();

    public void Dispose() => import.Dispose();

    private AgentImportRunner Runner => import.Runner;

    private List<VirtualCrossoverChannel> Channels => import.Channels;

    private VirtualCrossoverProjectFile Project => import.Project;

    [Fact]
    public void UseSpatialAverage_HandsTheModeToThePanel_AndRefusesANameItDoesNotKnow()
    {
        Assert.True(Runner.UseSpatialAverage(new UseSpatialAverageOperation("op-1", "", "MicArray", true)));
        Assert.False(Runner.UseSpatialAverage(new UseSpatialAverageOperation("op-2", "", "Everywhere", true)));

        Assert.Equal(["spatial average MicArray"], import.Calls);
    }

    [Fact]
    public void Undo_PutsBackTheChainTheModeTheTickTheBlockOrderAndTheLevel()
    {
        Project.SpatialAverageMode = VirtualCrossoverSpatialAverageMode.Off;
        List<VirtualCrossoverChannel> order = Channels.ToList();
        double gain = Channels[0].SideSettings(rightSide: false).GainDb;
        double level = Project.TargetLevelDb;

        AgentImportUndo undo = Runner.CaptureUndo();
        Runner.UseSpatialAverage(new UseSpatialAverageOperation("op-1", "", "MicArray", true));
        Channels[0].SideSettings(rightSide: false).GainDb = gain - 6;
        import.Session.Reorder(import.Session.MoveOrder(Channels[0], 1)!);
        Project.TargetLevelDb = level + 3;

        (List<VirtualCrossoverChannel> written, bool reordered) = undo.Restore(import.Session, import.Reader);

        Assert.True(reordered);
        Assert.Equal(VirtualCrossoverSpatialAverageMode.Off, Project.SpatialAverageMode);
        Assert.False(Project.ShowHybridCurves);
        Assert.Equal(order, Channels);
        Assert.Equal(["A", "B", "C"], Channels.Select(channel => channel.Name));
        Assert.Equal(order.Select(channel => channel.Pair), Project.Pairs);
        Assert.Equal(gain, Channels[0].SideSettings(rightSide: false).GainDb);
        Assert.Equal(level, Project.TargetLevelDb);
        Assert.Equal(order, written.Distinct());
    }

    [Fact]
    public void Undo_PutsBackTheStereoSceneTheTiltAndTheRearFill()
    {
        Project.SetStereoScene(0.25, rightHandDrive: false);
        Project.StereoLevelDifferenceDb = -1.0;
        Project.RearFillOffsetMs = 15.0;

        AgentImportUndo undo = Runner.CaptureUndo();
        Project.SetStereoScene(0.6, rightHandDrive: true);
        Project.StereoLevelDifferenceDb = 2.5;
        Project.RearFillOffsetMs = 9.0;
        (_, bool reordered) = undo.Restore(import.Session, import.Reader);

        Assert.False(reordered);
        Assert.Equal(0.25, Project.StereoSceneOffsetMagnitudeMs);
        Assert.False(Project.StereoRightHandDrive);
        Assert.Equal(-1.0, Project.StereoLevelDifferenceDb);
        Assert.Equal(15.0, Project.RearFillOffsetMs);
    }

    [Fact]
    public async Task TheUndo_IsSpent_AndRefusedOnceAnotherProjectIsBound()
    {
        Channels[0].SideSettings(rightSide: false).GainDb = -1;
        var proposal = new AgentProposal(
            null, "trim it", [], [], [new SetGainOperation("op-1", "A:left", "too hot", -1, -3)], []);
        AgentSessionSnapshot reviewed = Runner.Snapshot();
        await Runner.CommitAsync(proposal, new HashSet<string> { "op-1" }, reviewed.Fingerprint, 1, [], null);
        Assert.NotNull(Runner.Undo);

        import.Session.NextProjectGeneration();
        (AgentImportUndo? undo, string? refusal) = Runner.TakeUndo();

        Assert.Null(undo);
        Assert.Equal("A session was loaded since the last AI import.", refusal);
        Assert.Null(Runner.Undo);
        Assert.Equal((null, null), Runner.TakeUndo());
    }

    [Fact]
    public async Task EngineRequests_RunInTheImportsOwnOrder_AndSayWhatWasSkipped()
    {
        Project.SpatialAverageMode = VirtualCrossoverSpatialAverageMode.Off;
        var summary = new List<string>();

        // Listed reversed on purpose: the spatial average always runs first, since it decides which curves later engines read.
        bool ran = await Runner.EnginesAsync(
            [
                Row(new RunAutoCrossoverOperation("op-1", "split them"), "Auto crossover"),
                Row(new UseSpatialAverageOperation("op-2", "unused arrays", "MicArray", true), "Spatial average")
            ],
            summary);

        Assert.Equal(
            [
                "Spatial average: MicArray, hybrid on.",
                "Auto crossover: skipped (fewer than two enabled channels have a measurement)."
            ],
            summary);
        Assert.True(ran);
        Assert.Equal(["spatial average MicArray", "wizard"], import.Calls);
    }

    [Fact]
    public async Task AnEngineRequestThatChangesNothing_LeavesRanFalse()
    {
        var summary = new List<string>();

        bool ran = await Runner.EnginesAsync([Row(new RunAutoCrossoverOperation("op-1", ""), "Auto crossover")], summary);

        Assert.False(ran);
        Assert.Contains("skipped", Assert.Single(summary));
    }

    [Fact]
    public async Task AWizardThatApplies_CountsAsARun()
    {
        import.WizardRefusal = null;
        var summary = new List<string>();

        bool ran = await Runner.EnginesAsync([Row(new RunAutoCrossoverOperation("op-1", ""), "Auto crossover")], summary);

        Assert.True(ran);
        Assert.Equal(["Auto crossover: applied."], summary);
    }

    [Fact]
    public void AutoDelayRequest_TakesWhatTheReplyStates_AndTheDialogsAnswerForTheRest()
    {
        var defaults = new AgentAutoDelaySettings(0.25, false, false, 1.0, 15.0);

        AutoDelayRunRequest partial = AgentEngineRequests.AutoDelayRequest(
            new RunAutoDelayOperation("op-1", "", 0.35, null, null, null, null), defaults);
        Assert.Equal(0.35, partial.SceneOffsetMs);
        Assert.False(partial.RightHandDrive);
        Assert.False(partial.AdjustGains);
        Assert.Equal(1.0, partial.NearSideCutDb);
        Assert.Equal(15.0, partial.RearFillOffsetMs);

        AutoDelayRunRequest full = AgentEngineRequests.AutoDelayRequest(
            new RunAutoDelayOperation("op-1", "", null, true, true, 2.0, 12.5), defaults);
        Assert.Equal(0.25, full.SceneOffsetMs);
        Assert.True(full.RightHandDrive);
        Assert.True(full.AdjustGains);
        Assert.Equal(2.0, full.NearSideCutDb);
        Assert.Equal(12.5, full.RearFillOffsetMs);
        Assert.Equal(2.0, full.LevelDifferenceDb);
    }

    [Fact]
    public void ImportTargetLevel_IsTheFirstStatedOne_ElseTheProjectsOwn_ForEveryFit()
    {
        // The level is decided once per import, so rows do not fit against different datums.
        AgentOperationVerdict stated = Row(
            new AutoTunePeqOperation("op-1", "B:left", "", -6, null, null, null, null, null, null), "Auto-tune");
        AgentOperationVerdict omitted = Row(
            new AutoTunePeqOperation("op-2", "A:left", "", null, null, null, null, null, null, null), "Auto-tune");
        AgentOperationVerdict rejected = Row(
            new AutoTunePeqOperation("op-3", "C:mono", "", -9, null, null, null, null, null, null), "Auto-tune")
            with { Status = AgentVerdictStatus.Rejected };

        Assert.Equal(-6, AgentEngineRequests.TargetLevelDb([omitted, stated], -4));
        Assert.Equal(-4, AgentEngineRequests.TargetLevelDb([omitted], -4));
        Assert.Equal(-4, AgentEngineRequests.TargetLevelDb([rejected, omitted], -4));
        Assert.Equal(-4, AgentEngineRequests.TargetLevelDb([], -4));
    }

    [Fact]
    public async Task JunctionTune_WithoutAMeasurement_IsRefusedWithThePhraseTheSummaryQuotes()
    {
        var summary = new List<string>();

        bool ran = await Runner.EnginesAsync(
            [Row(new TuneJunctionOperation("op-1", "", "left:A-B", null, null, null, null, null), "Junction tune")],
            summary);

        Assert.False(ran);
        string line = Assert.Single(summary);
        Assert.StartsWith("Junction tune left:A-B: skipped (", line);
        Assert.Contains("has no measurement", line);
    }

    [Fact]
    public async Task AnAutoTuneWhoseWindowHoldsNothingMeasured_SkipsThatRowAndRunsTheNext()
    {
        // Two channels crossed at 80..500 Hz but measured from 5 kHz up: an empty fit would replace the bank with nothing.
        import.Measure(2);
        foreach (VirtualCrossoverChannel channel in Channels.Take(2))
        {
            channel.SideState(channel.ActiveRight).MeasuredBand = new MeasuredBand(5_000, 8_000);
            VirtualCrossoverChannelSettings settings = channel.SideSettings(channel.ActiveRight);
            settings.CrossoverKind = CrossoverKind.BandPass;
            settings.HighPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 80, 24);
            settings.LowPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 500, 24);
            settings.PeqBands = [new PeqBand(3_000, 2, -4)];
        }
        AgentSessionSnapshot snapshot = Runner.Snapshot();
        List<AgentOperationVerdict> rows = Channels.Take(2)
            .Select((channel, index) => Row(
                new AutoTunePeqOperation(
                    $"op-{index + 1}", $"{channel.Name}:left", "", null, null, null, null, null, null, null),
                "Auto-tune")
                with
                {
                    Channel = snapshot.Channels.First(item => ReferenceEquals(
                        item.Settings, channel.SideSettings(channel.ActiveRight)))
                })
            .ToList();
        var summary = new List<string>();

        bool ran = await Runner.EnginesAsync(rows, summary);

        Assert.False(ran);
        Assert.Equal(2, summary.Count);
        Assert.All(summary, line => Assert.Contains("holds no measured point", line));
        Assert.All(Channels.Take(2), channel => Assert.Single(channel.SideSettings(channel.ActiveRight).PeqBands));
        Assert.DoesNotContain(import.Calls, call => call.StartsWith("level", StringComparison.Ordinal));
    }

    [Fact]
    public async Task JunctionTune_WritesOneCrossoverToBothSidesOfBothBlocks_AndUndoPutsItBack()
    {
        import.Measure(2);
        VirtualCrossoverChannel lower = Channels[0];
        VirtualCrossoverChannel upper = Channels[1];
        foreach (bool rightSide in new[] { false, true })
        {
            VirtualCrossoverChannelSettings lowerSettings = lower.SideSettings(rightSide);
            lowerSettings.CrossoverKind = CrossoverKind.LowPass;
            lowerSettings.LowPassEdge = new CrossoverEdge(CrossoverFilterFamily.Butterworth, 1_400, 12);
            lowerSettings.DelayMs = 0.5;
            VirtualCrossoverChannelSettings upperSettings = upper.SideSettings(rightSide);
            upperSettings.CrossoverKind = CrossoverKind.HighPass;
            // Corners crossed over each other: a bump no delay or polarity takes out, so the crossover is what is wrong.
            upperSettings.HighPassEdge = new CrossoverEdge(CrossoverFilterFamily.Butterworth, 700, 12);
            upperSettings.DelayMs = 0.5;
            upperSettings.GainDb = -2.5;
        }
        AgentImportUndo undo = Runner.CaptureUndo();
        var summary = new List<string>();

        bool ran = await Runner.EnginesAsync(
            [
                Row(new TuneJunctionOperation("op-1", "meet in one corner",
                    "left:A-B", 900, 1_100, ["LinkwitzRiley"], [24], null), "Junction tune")
            ],
            summary);

        Assert.True(ran, string.Join(" | ", summary));
        Assert.StartsWith("Junction tune A/B: applied — ", summary[0]);
        Assert.Contains("LP BW12 1400 Hz", summary[0]);
        Assert.Equal(3, summary.Count);
        Assert.StartsWith("  left: sum loss ", summary[1]);
        Assert.StartsWith("  right: sum loss ", summary[2]);
        Assert.Contains("after the best delay", summary[1]);
        Assert.Equal(["busy", "idle", "show A", "show B", "remember sides", "save"], import.Calls);
        foreach (bool rightSide in new[] { false, true })
        {
            VirtualCrossoverChannelSettings lowerSettings = lower.SideSettings(rightSide);
            VirtualCrossoverChannelSettings upperSettings = upper.SideSettings(rightSide);
            Assert.Equal(CrossoverFilterFamily.LinkwitzRiley, lowerSettings.LowPassEdge.Family);
            Assert.Equal(24, lowerSettings.LowPassEdge.SlopeDbPerOctave);
            Assert.InRange(lowerSettings.LowPassEdge.FrequencyHz, 900, 1_100);
            Assert.Equal(lowerSettings.LowPassEdge, upperSettings.HighPassEdge);
            Assert.Equal(lower.SideSettings(false).LowPassEdge, lowerSettings.LowPassEdge);
            Assert.Equal(0.5, lowerSettings.DelayMs);
            Assert.Equal(-2.5, upperSettings.GainDb);
        }

        undo.Restore(import.Session, import.Reader);

        foreach (bool rightSide in new[] { false, true })
        {
            Assert.Equal(
                new CrossoverEdge(CrossoverFilterFamily.Butterworth, 1_400, 12), lower.SideSettings(rightSide).LowPassEdge);
            Assert.Equal(
                new CrossoverEdge(CrossoverFilterFamily.Butterworth, 700, 12), upper.SideSettings(rightSide).HighPassEdge);
        }
    }

    [Fact]
    public async Task JunctionTune_KeepsATextbookJunction_AndSaysSo()
    {
        (VirtualCrossoverChannel lower, VirtualCrossoverChannel upper) = import.MeasuredJunction();
        var summary = new List<string>();

        bool ran = await Runner.EnginesAsync(
            [Row(new TuneJunctionOperation("op-1", "", "left:A-B", 900, 1_100, ["LinkwitzRiley"], [24], null), "Junction tune")],
            summary);

        Assert.False(ran);
        Assert.StartsWith("Junction tune A/B: kept — ", summary[0]);
        Assert.Contains("not 0.50 dB better", summary[0]);
        Assert.DoesNotContain("save", import.Calls);
        Assert.Equal(1_000, lower.SideSettings(true).LowPassEdge.FrequencyHz);
        Assert.Equal(1_000, upper.SideSettings(true).HighPassEdge.FrequencyHz);
    }

    [Fact]
    public async Task EngineOrder_PutsTheJunctionTuneAfterTheWizard_AndBeforeAutoDelay()
    {
        var summary = new List<string>();

        await Runner.EnginesAsync(
            [
                Row(new RunAutoDelayOperation("op-1", "", null, null, null, null, null), "Auto delay"),
                Row(new TuneJunctionOperation("op-2", "", "left:A-B", null, null, null, null, null), "Junction tune"),
                Row(new RunAutoCrossoverOperation("op-3", ""), "Auto crossover")
            ],
            summary);

        Assert.Equal(3, summary.Count);
        Assert.StartsWith("Auto crossover:", summary[0]);
        Assert.StartsWith("Junction tune", summary[1]);
        Assert.StartsWith("Auto delay:", summary[2]);
    }

    [Fact]
    public async Task AutoDelay_WithoutMeasurements_IsRefusedWithThePhraseTheSummaryQuotes()
    {
        var summary = new List<string>();

        bool ran = await Runner.EnginesAsync(
            [Row(new RunAutoDelayOperation("op-1", "", 0.35, null, null, null, null), "Auto delay")], summary);

        Assert.False(ran);
        Assert.Equal(["Auto delay: skipped (fewer than two enabled channels have a measurement)."], summary);
        Assert.Empty(import.Calls);
    }

    [Fact]
    public async Task AutoDelay_RunsWithThePanelDisabled_AndCommitsThroughIt()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        import.MeasuredJunction();
        var summary = new List<string>();

        bool ran = await Runner.EnginesAsync(
            [Row(new RunAutoDelayOperation("op-1", "", 0.3, null, null, null, null), "Auto delay")], summary);

        Assert.True(ran, string.Join(" | ", summary));
        Assert.StartsWith("Auto delay: applied (stereo, scene 0.30 ms LHD, gains kept).", summary[0]);
        Assert.Equal(["busy, disabled", "idle", "auto delay applied"], import.Calls);
        Assert.Equal(0.3, Project.StereoSceneOffsetMagnitudeMs, 12);
    }

    [Fact]
    public async Task AutoDelay_ForAPanelClosedDuringTheRun_WritesNothing()
    {
        import.MeasuredJunction();
        import.IsGone = true;
        var summary = new List<string>();

        bool ran = await Runner.EnginesAsync(
            [Row(new RunAutoDelayOperation("op-1", "", 0.3, null, null, null, null), "Auto delay")], summary);

        Assert.False(ran);
        Assert.Empty(summary);
        Assert.DoesNotContain("auto delay applied", import.Calls);
    }

    [Fact]
    public async Task Probe_ReadsEveryVariantOntoTheClipboard_AndChangesNothing()
    {
        (VirtualCrossoverChannel lower, VirtualCrossoverChannel upper) = import.MeasuredJunction();
        var lr = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24);
        foreach (bool rightSide in new[] { false, true })
        {
            lower.SideSettings(rightSide).PeqBands = [new PeqBand(1_000, 1.0, -9)];
            upper.SideSettings(rightSide).GainDb = -2;
        }
        var summary = new List<string>();

        (bool ran, string? copied) = await Copying(() => Runner.ProbesAsync(
            [
                Row(new ProbeOperation("op-1", "is the bell the problem", AgentProtocol.JunctionProbe, "left:A-B",
                    [
                        new AgentProbeVariant("no bell",
                            [new AgentProbeChange("A:left", null, null, null, null, new AgentPeqBank(0, []))]),
                        new AgentProbeVariant("BW48 instead",
                            [
                                new AgentProbeChange("A:left", null, null, null,
                                    new AgentCrossover("LowPass", null,
                                        new AgentCrossoverEdge("Butterworth", 1_000, 48, null)), null),
                                new AgentProbeChange("B:left", -3.5, null, null,
                                    new AgentCrossover("HighPass",
                                        new AgentCrossoverEdge("Butterworth", 1_000, 48, null), null), null)
                            ])
                    ]), "Probe")
            ],
            summary));

        Assert.True(ran, string.Join(" | ", summary));
        foreach (bool rightSide in new[] { false, true })
        {
            Assert.Equal(lr, lower.SideSettings(rightSide).LowPassEdge);
            Assert.Equal(lr, upper.SideSettings(rightSide).HighPassEdge);
            Assert.Single(lower.SideSettings(rightSide).PeqBands);
            Assert.Equal(-2, upper.SideSettings(rightSide).GainDb);
        }
        Assert.Null(Runner.Undo);
        Assert.Equal(["busy", "idle"], import.Calls);

        string text = Assert.IsType<string>(copied);
        Assert.Contains(AgentProtocol.ProbeHeader, text);
        Assert.Contains(AgentProtocol.ProbeJsonBegin, text);
        Assert.Contains("\"kind\":\"resonalyze.agent-probe\"", text);
        Assert.Contains("\"label\":\"current\"", text);
        Assert.Contains("\"label\":\"no bell\"", text);
        Assert.Contains("\"label\":\"BW48 instead\"", text);
        Assert.Contains("\"sumLossDb\"", text);
        Assert.Contains("\"afterBestDelay\"", text);
        string line = Assert.Single(summary);
        Assert.Contains("1 of 1 reading computed and copied to the clipboard", line);
        Assert.Contains("Nothing in the tune was changed", line);
    }

    [Fact]
    public async Task Probe_NamesTheOtherJunctionsAVariantsChannelHandsOverAt()
    {
        import.Measure(3);
        var lowJunction = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 300, 24);
        var highJunction = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 2_000, 24);
        foreach (bool rightSide in new[] { false, true })
        {
            Channels[0].SideSettings(rightSide).CrossoverKind = CrossoverKind.LowPass;
            Channels[0].SideSettings(rightSide).LowPassEdge = lowJunction;
            Channels[1].SideSettings(rightSide).CrossoverKind = CrossoverKind.BandPass;
            Channels[1].SideSettings(rightSide).HighPassEdge = lowJunction;
            Channels[1].SideSettings(rightSide).LowPassEdge = highJunction;
            Channels[2].SideSettings(rightSide).CrossoverKind = CrossoverKind.HighPass;
            Channels[2].SideSettings(rightSide).HighPassEdge = highJunction;
        }

        (bool ran, string? copied) = await Copying(() => Runner.ProbesAsync(
            [
                Row(new ProbeOperation("op-1", "steeper into the tweeter", AgentProtocol.JunctionProbe, "left:B-C",
                    [
                        new AgentProbeVariant("BW48 on the mid",
                            [new AgentProbeChange("B:left", null, null, null,
                                new AgentCrossover("BandPass",
                                    new AgentCrossoverEdge("LinkwitzRiley", 300, 24, null),
                                    new AgentCrossoverEdge("Butterworth", 2_000, 48, null)), null)]),
                        new AgentProbeVariant("trim the tweeter",
                            [new AgentProbeChange("C:left", -1.5, null, null, null, null)])
                    ]), "Probe")
            ],
            []));

        Assert.True(ran);
        // Neighbour lists belong to the entry: the tweeter-only variant must not inherit the mid variant's neighbour.
        string text = Assert.IsType<string>(copied);
        const string below = "\"affectedJunctions\":[\"left:A-B\"]";
        Assert.Contains(below, text);
        Assert.Equal(1, text.Split("\"affectedJunctions\":[").Length - 1);
        int mark = text.IndexOf(below, StringComparison.Ordinal);
        int steeper = text.IndexOf("\"label\":\"BW48 on the mid\"", StringComparison.Ordinal);
        int trim = text.IndexOf("\"label\":\"trim the tweeter\"", StringComparison.Ordinal);
        Assert.True(steeper > 0 && trim > steeper);
        Assert.InRange(mark, steeper, trim);
    }

    [Fact]
    public async Task Probe_MarksTheBaselineByItsPosition_NotByTheReplysLabel()
    {
        import.MeasuredJunction();

        (_, string? copied) = await Copying(() => Runner.ProbesAsync(
            [
                Row(new ProbeOperation("op-1", "", AgentProtocol.JunctionProbe, "left:A-B",
                    [new AgentProbeVariant("current", [new AgentProbeChange("B:left", -4, null, null, null, null)])]),
                    "Probe")
            ],
            []));

        string text = Assert.IsType<string>(copied);
        Assert.Equal(2, Regex.Matches(text, "\"label\":\"current\"").Count);
        Assert.Single(Regex.Matches(text, "\"current\":true"));
    }

    [Fact]
    public async Task Probe_TurnsAReadingThatThrowsIntoItsOwnUnavailableEntry()
    {
        (VirtualCrossoverChannel lower, VirtualCrossoverChannel upper) = import.MeasuredJunction();
        // A full-range pair resolves as a junction but has no corner to read a delay band from.
        foreach (bool rightSide in new[] { false, true })
        {
            lower.SideSettings(rightSide).CrossoverKind = CrossoverKind.Off;
            upper.SideSettings(rightSide).CrossoverKind = CrossoverKind.Off;
        }
        var summary = new List<string>();

        (bool ran, string? copied) = await Copying(() => Runner.ProbesAsync(
            [
                Row(new ProbeOperation("op-1", "", AgentProtocol.JunctionDelayProbe, "left:A-B", null), "Probe"),
                Row(new ProbeOperation("op-2", "", AgentProtocol.ExcessGroupDelayProbe, null, null), "Probe")
            ],
            summary));

        Assert.True(ran, string.Join(" | ", summary));
        Assert.Contains("1 of 2 readings computed", summary[0]);
        Assert.Contains("no crossover to read a band from", summary[1]);
        string text = Assert.IsType<string>(copied);
        Assert.Contains("\"probe\":\"excessGroupDelay\"", text);
        Assert.Contains("\"unavailable\"", text);
    }

    [Fact]
    public async Task Probe_SaysWhatItCouldNotRead_WithoutTakingTheOthersDown()
    {
        var summary = new List<string>();

        (_, string? copied) = await Copying(() => Runner.ProbesAsync(
            [
                Row(new ProbeOperation("op-1", "", AgentProtocol.JunctionDelayProbe, "left:A-B", null), "Probe"),
                Row(new ProbeOperation("op-2", "", AgentProtocol.ExcessGroupDelayProbe, null, null), "Probe")
            ],
            summary));

        Assert.NotNull(copied);
        Assert.Contains("0 of 2 readings computed", summary[0]);
        Assert.Contains("has no measurement", summary[1]);
        Assert.Contains("no channel has a measurement to read", summary[2]);
    }

    [Fact]
    public async Task Probe_DeclaresATuneThatMovedBetweenReadings()
    {
        import.MeasuredJunction();
        var summary = new List<string>();

        Action<string> write = AgentClipboard.WriteText;
        AgentClipboard.WriteText = _ => { };
        try
        {
            import.GroupView = VirtualCrossoverGroupView.FrontAndSub;
            Task<bool> probes = Runner.ProbesAsync(
                [
                    Row(new ProbeOperation("op-1", "", AgentProtocol.ExcessGroupDelayProbe, null, null), "Probe"),
                    Row(new ProbeOperation("op-2", "", AgentProtocol.ExcessGroupDelayProbe, null, null), "Probe")
                ],
                summary,
                null);
            Channels[0].SideSettings(rightSide: false).GainDb = -7;
            await probes;
        }
        finally
        {
            AgentClipboard.WriteText = write;
        }

        Assert.Contains(summary, line => line.Contains("changed while the readings were being taken", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Commit_WritesNothing_WhenTheTuneMovedWhileTheProbesRan()
    {
        VirtualCrossoverChannelSettings settings = Channels[0].SideSettings(rightSide: false);
        settings.GainDb = -1;
        var proposal = new AgentProposal(
            null, "trim it", [], [], [new SetGainOperation("op-1", "A:left", "too hot", -1, -3)], []);
        AgentSessionSnapshot reviewed = Runner.Snapshot();
        AgentProposalReview review = AgentProposalValidator.Review(proposal, reviewed);
        Assert.True(review.Verdicts[0].Applicable);
        var selected = new HashSet<string>(StringComparer.Ordinal) { "op-1" };

        // The user edits during the probe's seconds; the progress window does not block the panel.
        settings.GainDb = -2;
        var summary = new List<string>();
        bool ran = await Runner.CommitAsync(proposal, selected, reviewed.Fingerprint, review.Verdicts.Count, summary, null);

        Assert.False(ran);
        Assert.Equal(-2, settings.GainDb);
        string line = Assert.Single(summary);
        Assert.StartsWith("Nothing was written:", line);
        Assert.Contains("changed while the review was open", line);
        Assert.Null(Runner.Undo);
        Assert.Empty(import.Calls);
    }

    [Fact]
    public async Task Commit_WritesTheRows_ArmsTheUndo_AndLetsTheSideLockTakeThemAsWritten()
    {
        VirtualCrossoverChannelSettings settings = Channels[0].SideSettings(rightSide: false);
        settings.GainDb = -1;
        var proposal = new AgentProposal(
            null, "trim it", [], [], [new SetGainOperation("op-1", "A:left", "too hot", -1, -3)], []);
        AgentSessionSnapshot reviewed = Runner.Snapshot();
        var summary = new List<string>();

        bool engines = await Runner.CommitAsync(
            proposal, new HashSet<string> { "op-1" }, reviewed.Fingerprint, 1, summary, null);

        Assert.False(engines);
        Assert.Equal(-3, settings.GainDb);
        Assert.Equal(["Applied 1 of 1 proposed change."], summary);
        Assert.Equal(["show A", "remember sides"], import.Calls);
        AgentImportUndo undo = Assert.IsType<AgentImportUndo>(Runner.Undo);
        undo.Restore(import.Session, import.Reader);
        Assert.Equal(-1, settings.GainDb);
    }

    [Fact]
    public async Task ACommitThatWritesNothing_LeavesThePreviousUndoInPlace()
    {
        VirtualCrossoverChannelSettings settings = Channels[0].SideSettings(rightSide: false);
        settings.GainDb = -1;
        var trim = new AgentProposal(
            null, "", [], [], [new SetGainOperation("op-1", "A:left", "", -1, -3)], []);
        await Runner.CommitAsync(trim, new HashSet<string> { "op-1" }, Runner.Snapshot().Fingerprint, 1, [], null);
        AgentImportUndo? first = Runner.Undo;

        var wizard = new AgentProposal(null, "", [], [], [new RunAutoCrossoverOperation("op-1", "")], []);
        await Runner.CommitAsync(wizard, new HashSet<string> { "op-1" }, Runner.Snapshot().Fingerprint, 1, [], null);

        Assert.NotNull(first);
        Assert.Same(first, Runner.Undo);
    }

    [Fact]
    public void Fingerprint_MovesWithWhatAPackageVouchesFor()
    {
        Project.SpatialAverageMode = VirtualCrossoverSpatialAverageMode.Off;
        string baseline = Runner.Fingerprint();
        Assert.Equal(baseline, Runner.Fingerprint());

        VirtualCrossoverChannelSettings left = Channels[0].SideSettings(rightSide: false);
        var seen = new HashSet<string>(StringComparer.Ordinal) { baseline };
        void Moves(Action change)
        {
            change();
            Assert.True(seen.Add(Runner.Fingerprint()), "the fingerprint did not move");
        }

        Moves(() => left.SourceFilePath = @"D:\measurements\left mid (retaken).json");
        // Same reference, length and rate, other samples: the hash reads CONTENT.
        VirtualCrossoverChannelState leftState = Channels[0].SideState(rightSide: false);
        var samples = new System.Numerics.Complex[64];
        samples[3] = new System.Numerics.Complex(0.5, 0);
        Moves(() => leftState.TransferImpulseResponse = samples);
        var retaken = new System.Numerics.Complex[64];
        retaken[3] = new System.Numerics.Complex(0.4, 0);
        Moves(() => leftState.TransferImpulseResponse = retaken);
        leftState.TransferImpulseResponse = (System.Numerics.Complex[])retaken.Clone();
        Assert.Contains(Runner.Fingerprint(), seen);
        Moves(() => leftState.TransferCoherence = [0.9, 0.8]);
        Moves(() => leftState.MeasuredBand = new MeasuredBand(40, 8000));
        Moves(() => leftState.TransferPeakIndex += 1);
        import.Session.Calibration = import.Session.Calibration with { Own = true };
        Moves(() => leftState.MicrophoneCalibration =
            new VirtualCrossoverCalibrationSettings { Name = "mic", Points = [[1000, 0.5], [2000, 1.0]] });
        Moves(() => leftState.MicrophoneCalibration =
            new VirtualCrossoverCalibrationSettings { Name = "mic", Points = [[1000, 0.5], [2000, 1.5]] });
        Moves(() => Project.AiNotes = "Front: 6.5\" mids in the doors.");
        Moves(() => Project.Target = new VirtualCrossoverTargetSettings { ImportedCurve = [1, 2, 3] });
        Moves(() => Project.Target!.ImportedCurve = [1, 2, 4]);
        Moves(() => left.SpatialAveragePath = @"D:\measurements\left mid mmm.json");
        Moves(() => leftState.SpatialAverage = new LiveCaptureDocument { CaptureSessionId = Guid.NewGuid() });
        Moves(() => left.GainDb -= 1.5);
        Moves(() => left.PeqBands = [new PeqBand(820, 2.1, -2.4)]);
        Moves(() => import.Session.Reorder(import.Session.MoveOrder(Channels[0], 1)!));
        Moves(() => import.HybridTicked = true);
        Moves(() => Project.SpatialAverageMode = VirtualCrossoverSpatialAverageMode.MovingMic);
        Moves(() => Project.PhaseGateLeft.OffsetMs = 3.25);
        Moves(() => Project.StereoLevelDifferenceDb = -2.0);
        Moves(() => Project.Pairs[0].Mono = true);
        Moves(() => Project.ActiveSideRight = true);
        Moves(() => import.GroupView = VirtualCrossoverGroupView.Everything);
        Moves(() => Project.TargetLevelDb += 1);
    }

    [Fact]
    public void Fingerprint_IsBackWhereItWas_AfterUndo()
    {
        Project.SpatialAverageMode = VirtualCrossoverSpatialAverageMode.Off;
        string before = Runner.Fingerprint();

        AgentImportUndo undo = Runner.CaptureUndo();
        Runner.UseSpatialAverage(new UseSpatialAverageOperation("op-1", "", "MicArray", true));
        Channels[0].SideSettings(rightSide: false).GainDb -= 6;
        import.Session.Reorder(import.Session.MoveOrder(Channels[0], 1)!);
        Assert.NotEqual(before, Runner.Fingerprint());

        undo.Restore(import.Session, import.Reader);
        import.HybridTicked = undo.HybridTicked;

        Assert.Equal(before, Runner.Fingerprint());
    }
}
