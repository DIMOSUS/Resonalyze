using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Resonalyze.Dsp;
using Resonalyze.Integration.AgentBridge;

namespace Resonalyze.App.Tests;

/// <summary>
/// The panel's half of an engine request: an operation the review passed is
/// carried out by the same code a button click runs, and Undo AI import puts
/// back everything the import could have moved — not only the channels a row
/// named. An engine writes channels no row mentions, and the crossover wizard
/// can reorder the blocks, so the undo snapshot is the whole project's chain,
/// the spatial average mode, the Hybrid tick and the block order.
/// </summary>
public sealed class VirtualCrossoverAgentEngineTests
{
    private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;

    [Fact]
    public void UseSpatialAverage_SetsTheModeAndTicksHybridTogether()
    {
        StaTest.Run(() =>
        {
            using VirtualCrossoverPanel panel = Loaded();
            Project(panel).SpatialAverageMode = VirtualCrossoverSpatialAverageMode.Off;
            Hybrid(panel).Checked = false;

            Invoke(panel, "ApplyAgentSpatialAverage",
                new UseSpatialAverageOperation("op-1", "arrays are attached", "MicArray", true));

            // Either half alone leaves the point measurement in charge, which is
            // the state the operation exists to get out of.
            Assert.Equal(
                VirtualCrossoverSpatialAverageMode.MicArray, Project(panel).SpatialAverageMode);
            Assert.True(Hybrid(panel).Checked);
            Assert.True(Project(panel).ShowHybridCurves);
        });
    }

    [Fact]
    public void UndoAiImport_PutsBackTheChainTheModeTheTickAndTheBlockOrder()
    {
        StaTest.Run(() =>
        {
            using VirtualCrossoverPanel panel = Loaded();
            Project(panel).SpatialAverageMode = VirtualCrossoverSpatialAverageMode.Off;
            Hybrid(panel).Checked = false;
            List<VirtualCrossoverChannel> order = Channels(panel).ToList();
            double gain = Channels(panel)[0].SideSettings(rightSide: false).GainDb;
            var targetLevel = (DarkNumericUpDown)Field(panel, "numericTargetLevel");
            double level = Project(panel).TargetLevelDb;
            Assert.Equal(level, (double)targetLevel.Value);

            object undo = Invoke(panel, "CaptureAgentUndo")!;
            Invoke(panel, "ApplyAgentSpatialAverage",
                new UseSpatialAverageOperation("op-1", "", "MicArray", true));
            // A channel no row named, and the reordering the wizard can do.
            Channels(panel)[0].SideSettings(rightSide: false).GainDb = gain - 6;
            Invoke(panel, "MoveChannel", Channels(panel)[0], 1);
            Assert.NotEqual(order, Channels(panel));
            // The datum an Auto-tune landing moves: the field, and through its
            // ValueChanged, the project.
            targetLevel.Value += 3;
            Assert.Equal(level + 3, Project(panel).TargetLevelDb);

            Set(panel, "agentUndo", undo);
            Set(panel, "agentUndoGeneration", Field(panel, "projectGeneration"));
            Invoke(panel, "UndoAiImport");

            Assert.Equal(VirtualCrossoverSpatialAverageMode.Off, Project(panel).SpatialAverageMode);
            Assert.False(Hybrid(panel).Checked);
            Assert.False(Project(panel).ShowHybridCurves);
            Assert.Equal(order, Channels(panel));
            Assert.Equal(gain, Channels(panel)[0].SideSettings(rightSide: false).GainDb);
            // Both halves of the datum: the undo sets the field with the project
            // events suppressed, so the project's copy — what the package and the
            // saved session read — has to be written by hand, and once was not.
            Assert.Equal(level, (double)targetLevel.Value);
            Assert.Equal(level, Project(panel).TargetLevelDb);
            Assert.Null(Field(panel, "agentUndo"));
        });
    }

    [Fact]
    public void EngineRequests_RunInTheImportsOwnOrder_AndSayWhatWasSkipped()
    {
        StaTest.Run(() =>
        {
            using VirtualCrossoverPanel panel = Loaded();
            Project(panel).SpatialAverageMode = VirtualCrossoverSpatialAverageMode.Off;
            Hybrid(panel).Checked = false;
            var summary = new List<string>();

            // Listed the other way round on purpose: the import runs the spatial
            // average first whatever order the reply gave, because it decides
            // which curves the engines after it are read on.
            bool ran = RunEngines(panel,
                [
                    Row(new RunAutoCrossoverOperation("op-1", "split them"), "Auto crossover"),
                    Row(new UseSpatialAverageOperation("op-2", "unused arrays", "MicArray", true),
                        "Spatial average")
                ],
                summary);

            Assert.Equal(
                [
                    "Spatial average: MicArray, hybrid on.",
                    // No block here carries a measurement, so the wizard refuses
                    // before it opens — and the summary says which one did not run.
                    "Auto crossover: skipped (fewer than two enabled channels have a measurement)."
                ],
                summary);
            Assert.True(ran);
            Assert.Equal(
                VirtualCrossoverSpatialAverageMode.MicArray, Project(panel).SpatialAverageMode);
            Assert.True(Hybrid(panel).Checked);
        });
    }

    [Fact]
    public void AnEngineRequestThatChangesNothing_LeavesRanFalse()
    {
        StaTest.Run(() =>
        {
            using VirtualCrossoverPanel panel = Loaded();
            var summary = new List<string>();

            bool ran = RunEngines(panel,
                [Row(new RunAutoCrossoverOperation("op-1", ""), "Auto crossover")],
                summary);

            // The import reads this answer to decide whether Undo stays armed: a
            // refused wizard moved nothing, and arming it would offer to put back
            // a tune nobody touched.
            Assert.False(ran);
            Assert.Single(summary);
            Assert.Contains("skipped", summary[0]);
        });
    }

    [Fact]
    public void AutoDelayRequest_TakesWhatTheReplyStates_AndTheDialogsAnswerForTheRest()
    {
        var defaults = new AgentAutoDelaySettings(0.25, false, false, 1.0, 15.0);

        // Only the scene offset stated: everything else is what the dialog would
        // have opened with, the gain balance unticked included.
        AutoDelayRunRequest partial = VirtualCrossoverPanel.BuildAutoDelayRequest(
            new RunAutoDelayOperation("op-1", "", 0.35, null, null, null, null), defaults);
        Assert.Equal(0.35, partial.SceneOffsetMs);
        Assert.False(partial.RightHandDrive);
        Assert.False(partial.AdjustGains);
        Assert.Equal(1.0, partial.NearSideCutDb);
        Assert.Equal(15.0, partial.RearFillOffsetMs);

        AutoDelayRunRequest full = VirtualCrossoverPanel.BuildAutoDelayRequest(
            new RunAutoDelayOperation("op-1", "", null, true, true, 2.0, 12.5), defaults);
        Assert.Equal(0.25, full.SceneOffsetMs);
        Assert.True(full.RightHandDrive);
        Assert.True(full.AdjustGains);
        Assert.Equal(2.0, full.NearSideCutDb);
        Assert.Equal(12.5, full.RearFillOffsetMs);
        // The tilt in the gain engine's convention follows the layout, as the
        // dialog's would.
        Assert.Equal(2.0, full.LevelDifferenceDb);
    }

    [Fact]
    public void ImportTargetLevel_IsTheFirstStatedOne_ElseTheProjectsOwn_ForEveryFit()
    {
        // Decided once for the whole import: a row that states no level must not
        // fit at the old datum while the next row moves it.
        AgentOperationVerdict stated = Row(
            new AutoTunePeqOperation("op-1", "B:left", "", -6, null, null, null, null, null), "Auto-tune");
        AgentOperationVerdict omitted = Row(
            new AutoTunePeqOperation("op-2", "A:left", "", null, null, null, null, null, null), "Auto-tune");
        AgentOperationVerdict rejected = Row(
            new AutoTunePeqOperation("op-3", "C:mono", "", -9, null, null, null, null, null), "Auto-tune")
            with { Status = AgentVerdictStatus.Rejected };

        Assert.Equal(-6, VirtualCrossoverPanel.ImportTargetLevelDb([omitted, stated], -4));
        Assert.Equal(-4, VirtualCrossoverPanel.ImportTargetLevelDb([omitted], -4));
        Assert.Equal(-4, VirtualCrossoverPanel.ImportTargetLevelDb([rejected, omitted], -4));
        Assert.Equal(-4, VirtualCrossoverPanel.ImportTargetLevelDb([], -4));
    }

    [Fact]
    public void JunctionTune_RunHeadless_IsRefusedWithThePhraseTheSummaryQuotes()
    {
        StaTest.Run(() =>
        {
            using VirtualCrossoverPanel panel = Loaded();
            var summary = new List<string>();

            // The run resolves the junction on the live session the way the
            // review did: no block here carries a measurement, so the junction
            // cannot be read, and the summary says which one and why.
            bool ran = RunEngines(panel,
                [Row(new TuneJunctionOperation("op-1", "", "left:A-B", null, null, null, null, null), "Junction tune")],
                summary);

            Assert.False(ran);
            string line = Assert.Single(summary);
            Assert.StartsWith("Junction tune left:A-B: skipped (", line);
            Assert.Contains("has no measurement", line);
        });
    }

    [Fact]
    public void JunctionTune_WritesOneCrossoverToBothSidesOfBothBlocks_AndUndoPutsItBack()
    {
        StaTest.Run(() =>
        {
            using VirtualCrossoverPanel panel = Loaded();
            VirtualCrossoverChannel lower = Channels(panel)[0];
            VirtualCrossoverChannel upper = Channels(panel)[1];
            // Two flat drivers on both sides, crossed gently and apart: A's
            // low-pass at 700 Hz, B's high-pass at 1200 Hz, 12 dB/oct each.
            foreach (bool rightSide in new[] { false, true })
            {
                foreach (VirtualCrossoverChannel channel in new[] { lower, upper })
                {
                    VirtualCrossoverChannelState state = channel.SideState(rightSide);
                    var impulse = new System.Numerics.Complex[16_384];
                    impulse[480] = System.Numerics.Complex.One;
                    state.TransferImpulseResponse = impulse;
                    state.TransferPeakIndex = 480;
                    state.SampleRate = 48_000;
                }
                VirtualCrossoverChannelSettings lowerSettings = lower.SideSettings(rightSide);
                lowerSettings.CrossoverKind = CrossoverKind.LowPass;
                lowerSettings.LowPassEdge = new CrossoverEdge(CrossoverFilterFamily.Butterworth, 700, 12);
                lowerSettings.DelayMs = 0.5;
                VirtualCrossoverChannelSettings upperSettings = upper.SideSettings(rightSide);
                upperSettings.CrossoverKind = CrossoverKind.HighPass;
                upperSettings.HighPassEdge = new CrossoverEdge(CrossoverFilterFamily.Butterworth, 1_200, 12);
                // The same delay on both: aligned, so the crossover is what is
                // wrong with this junction (with the two apart by 0.5 ms the
                // tuner rightly keeps the crossover and points at the timing).
                upperSettings.DelayMs = 0.5;
                upperSettings.GainDb = -2.5;
            }
            object undo = Invoke(panel, "CaptureAgentUndo")!;
            var summary = new List<string>();

            bool ran = RunEnginesPumping(panel,
                [
                    Row(new TuneJunctionOperation("op-1", "meet in one corner",
                        $"left:{lower.Name}-{upper.Name}", 900, 1_100, ["LinkwitzRiley"], [24], null), "Junction tune")
                ],
                summary);

            Assert.True(ran, string.Join(" | ", summary));
            Assert.StartsWith($"Junction tune {lower.Name}/{upper.Name}: applied — ", summary[0]);
            Assert.Contains("LP BW12 700 Hz", summary[0]);
            Assert.Contains("→", summary[0]);
            // Per side, the readings before and after, and the after-delay figure.
            Assert.Equal(3, summary.Count);
            Assert.StartsWith("  left: sum loss ", summary[1]);
            Assert.StartsWith("  right: sum loss ", summary[2]);
            Assert.Contains("after the best delay", summary[1]);
            foreach (bool rightSide in new[] { false, true })
            {
                VirtualCrossoverChannelSettings lowerSettings = lower.SideSettings(rightSide);
                VirtualCrossoverChannelSettings upperSettings = upper.SideSettings(rightSide);
                Assert.Equal(CrossoverKind.LowPass, lowerSettings.CrossoverKind);
                Assert.Equal(CrossoverKind.HighPass, upperSettings.CrossoverKind);
                Assert.Equal(CrossoverFilterFamily.LinkwitzRiley, lowerSettings.LowPassEdge.Family);
                Assert.Equal(24, lowerSettings.LowPassEdge.SlopeDbPerOctave);
                Assert.InRange(lowerSettings.LowPassEdge.FrequencyHz, 900, 1_100);
                // One crossover: the same corner on both edges, and on both sides.
                Assert.Equal(lowerSettings.LowPassEdge, upperSettings.HighPassEdge);
                Assert.Equal(lower.SideSettings(false).LowPassEdge, lowerSettings.LowPassEdge);
                // Everything the tune must not touch.
                Assert.Equal(0.5, lowerSettings.DelayMs);
                Assert.Equal(0.5, upperSettings.DelayMs);
                Assert.Equal(-2.5, upperSettings.GainDb);
            }

            Set(panel, "agentUndo", undo);
            Set(panel, "agentUndoGeneration", Field(panel, "projectGeneration"));
            Invoke(panel, "UndoAiImport");

            foreach (bool rightSide in new[] { false, true })
            {
                Assert.Equal(
                    new CrossoverEdge(CrossoverFilterFamily.Butterworth, 700, 12),
                    lower.SideSettings(rightSide).LowPassEdge);
                Assert.Equal(
                    new CrossoverEdge(CrossoverFilterFamily.Butterworth, 1_200, 12),
                    upper.SideSettings(rightSide).HighPassEdge);
            }
        });
    }

    [Fact]
    public void Probe_ReadsEveryVariantOntoTheClipboard_AndChangesNothing()
    {
        StaTest.Run(() =>
        {
            using VirtualCrossoverPanel panel = Loaded();
            VirtualCrossoverChannel lower = Channels(panel)[0];
            VirtualCrossoverChannel upper = Channels(panel)[1];
            var lr = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24);
            foreach (bool rightSide in new[] { false, true })
            {
                foreach (VirtualCrossoverChannel channel in new[] { lower, upper })
                {
                    VirtualCrossoverChannelState state = channel.SideState(rightSide);
                    var impulse = new System.Numerics.Complex[16_384];
                    impulse[480] = System.Numerics.Complex.One;
                    state.TransferImpulseResponse = impulse;
                    state.TransferPeakIndex = 480;
                    state.SampleRate = 48_000;
                }
                lower.SideSettings(rightSide).CrossoverKind = CrossoverKind.LowPass;
                lower.SideSettings(rightSide).LowPassEdge = lr;
                lower.SideSettings(rightSide).PeqBands = [new PeqBand(1_000, 1.0, -9)];
                upper.SideSettings(rightSide).CrossoverKind = CrossoverKind.HighPass;
                upper.SideSettings(rightSide).HighPassEdge = lr;
                upper.SideSettings(rightSide).GainDb = -2;
            }

            string? copied = null;
            Action<string> write = AgentClipboard.WriteText;
            AgentClipboard.WriteText = text => copied = text;
            var summary = new List<string>();
            try
            {
                bool ran = RunProbes(panel,
                    [
                        Row(new ProbeOperation("op-1", "is the bell the problem",
                            AgentProtocol.JunctionProbe, $"left:{lower.Name}-{upper.Name}",
                            [
                                new AgentProbeVariant("no bell",
                                    [new AgentProbeChange(
                                        $"{lower.Name}:left", null, null, null, null, new AgentPeqBank(0, []))]),
                                new AgentProbeVariant("BW48 instead",
                                    [
                                        new AgentProbeChange($"{lower.Name}:left", null, null, null,
                                            new AgentCrossover("LowPass", null,
                                                new AgentCrossoverEdge("Butterworth", 1_000, 48, null)), null),
                                        new AgentProbeChange($"{upper.Name}:left", -3.5, null, null,
                                            new AgentCrossover("HighPass",
                                                new AgentCrossoverEdge("Butterworth", 1_000, 48, null), null), null)
                                    ])
                            ]), "Probe")
                    ],
                    summary);

                Assert.True(ran, string.Join(" | ", summary));
            }
            finally
            {
                AgentClipboard.WriteText = write;
            }

            // The tune is exactly as it was: a probe reads copies.
            foreach (bool rightSide in new[] { false, true })
            {
                Assert.Equal(lr, lower.SideSettings(rightSide).LowPassEdge);
                Assert.Equal(lr, upper.SideSettings(rightSide).HighPassEdge);
                Assert.Single(lower.SideSettings(rightSide).PeqBands);
                Assert.Equal(-2, upper.SideSettings(rightSide).GainDb);
            }
            Assert.Null(Field(panel, "agentUndo"));

            string text = Assert.IsType<string>(copied);
            Assert.Contains(AgentProtocol.ProbeHeader, text);
            Assert.Contains(AgentProtocol.ProbeJsonBegin, text);
            Assert.Contains("\"kind\":\"resonalyze.agent-probe\"", text);
            // The junction as it stands is read beside the variants, and every
            // entry carries the figures the reply asked the question for.
            Assert.Contains("\"label\":\"current\"", text);
            Assert.Contains("\"label\":\"no bell\"", text);
            Assert.Contains("\"label\":\"BW48 instead\"", text);
            Assert.Contains("\"sumLossDb\"", text);
            Assert.Contains("\"afterBestDelay\"", text);
            Assert.Contains("\"phase\"", text);
            Assert.Contains("\"shared\"", text);
            string line = Assert.Single(summary);
            Assert.Contains("1 of 1 reading computed and copied to the clipboard", line);
            Assert.Contains("Nothing in the tune was changed", line);
            Assert.Contains("paste the clipboard into the same chat", line);
        });
    }

    [Fact]
    public void Probe_NamesTheOtherJunctionsAVariantsChannelHandsOverAt()
    {
        StaTest.Run(() =>
        {
            using VirtualCrossoverPanel panel = Loaded();
            VirtualCrossoverChannel woofer = Channels(panel)[0];
            VirtualCrossoverChannel mid = Channels(panel)[1];
            VirtualCrossoverChannel tweeter = Channels(panel)[2];
            var lowJunction = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 300, 24);
            var highJunction = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 2_000, 24);
            foreach (VirtualCrossoverChannel channel in new[] { woofer, mid, tweeter })
            {
                foreach (bool rightSide in new[] { false, true })
                {
                    VirtualCrossoverChannelState state = channel.SideState(rightSide);
                    var impulse = new System.Numerics.Complex[16_384];
                    impulse[480] = System.Numerics.Complex.One;
                    state.TransferImpulseResponse = impulse;
                    state.TransferPeakIndex = 480;
                    state.SampleRate = 48_000;
                }
            }

            // A three-way: the midrange hands over twice, which is the whole
            // point of the field the probe reports.
            foreach (bool rightSide in new[] { false, true })
            {
                woofer.SideSettings(rightSide).CrossoverKind = CrossoverKind.LowPass;
                woofer.SideSettings(rightSide).LowPassEdge = lowJunction;
                mid.SideSettings(rightSide).CrossoverKind = CrossoverKind.BandPass;
                mid.SideSettings(rightSide).HighPassEdge = lowJunction;
                mid.SideSettings(rightSide).LowPassEdge = highJunction;
                tweeter.SideSettings(rightSide).CrossoverKind = CrossoverKind.HighPass;
                tweeter.SideSettings(rightSide).HighPassEdge = highJunction;
            }

            string? copied = null;
            Action<string> write = AgentClipboard.WriteText;
            AgentClipboard.WriteText = text => copied = text;
            var summary = new List<string>();
            try
            {
                bool ran = RunProbes(panel,
                    [
                        Row(new ProbeOperation("op-1", "steeper into the tweeter",
                            AgentProtocol.JunctionProbe, $"left:{mid.Name}-{tweeter.Name}",
                            [
                                new AgentProbeVariant("BW48 on the mid",
                                    [new AgentProbeChange($"{mid.Name}:left", null, null, null,
                                        new AgentCrossover("BandPass",
                                            new AgentCrossoverEdge("LinkwitzRiley", 300, 24, null),
                                            new AgentCrossoverEdge("Butterworth", 2_000, 48, null)), null)]),
                                new AgentProbeVariant("trim the tweeter",
                                    [new AgentProbeChange(
                                        $"{tweeter.Name}:left", -1.5, null, null, null, null)])
                            ]), "Probe")
                    ],
                    summary);

                Assert.True(ran, string.Join(" | ", summary));
            }
            finally
            {
                AgentClipboard.WriteText = write;
            }

            // The list belongs to the entry, not to the probe: the variant that
            // rewrites the midrange moves the junction BELOW it, and the one
            // that only trims the tweeter — which hands over nowhere else —
            // moves nothing, so it must not inherit the other's neighbour.
            string text = Assert.IsType<string>(copied);
            string below = $"\"affectedJunctions\":[\"left:{woofer.Name}-{mid.Name}\"]";
            Assert.Contains(below, text);
            Assert.Equal(1, text.Split("\"affectedJunctions\":[").Length - 1);
            int mark = text.IndexOf(below, StringComparison.Ordinal);
            int steeper = text.IndexOf("\"label\":\"BW48 on the mid\"", StringComparison.Ordinal);
            int trim = text.IndexOf("\"label\":\"trim the tweeter\"", StringComparison.Ordinal);
            Assert.True(steeper > 0 && trim > steeper);
            Assert.InRange(mark, steeper, trim);
            // The baseline changes nothing, so it carries no list at all.
            Assert.True(mark > text.IndexOf("\"current\":true", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Commit_WritesNothing_WhenTheTuneMovedWhileTheProbesRan()
    {
        StaTest.Run(() =>
        {
            using VirtualCrossoverPanel panel = Loaded();
            VirtualCrossoverChannelSettings settings =
                Channels(panel)[0].SideSettings(rightSide: false);
            settings.GainDb = -1;

            // A reply that trims the gain, reviewed and prepared against the
            // tune as it was.
            var proposal = new AgentProposal(
                null, "trim it", [], [],
                [new SetGainOperation("op-1", "A:left", "too hot", -1, -3)], []);
            AgentSessionSnapshot reviewed = Snapshot(panel);
            AgentProposalReview review = AgentProposalValidator.Review(proposal, reviewed);
            Assert.True(review.Verdicts[0].Applicable);
            var selected = new HashSet<string>(StringComparer.Ordinal) { "op-1" };
            Assert.Null(AgentProposalApplier.Prepare(
                proposal, selected, reviewed.Fingerprint, Snapshot(panel),
                out List<AgentOperationVerdict> preparedBefore, out _));

            // Then the user moves the very channel the row was judged against —
            // which is what the seconds a probe takes leave room for, since the
            // progress window takes nothing away from the panel.
            settings.GainDb = -2;

            var summary = new List<string>();
            bool ran = Commit(panel, proposal, selected, reviewed.Fingerprint, review.Verdicts.Count, summary);

            Assert.False(ran);
            Assert.Equal(-2, settings.GainDb);
            string line = Assert.Single(summary);
            Assert.StartsWith("Nothing was written:", line);
            Assert.Contains("changed while the review was open", line);
            Assert.Null(Field(panel, "agentUndo"));

            // What that guard is between: the applier re-checks nothing, so the
            // rows prepared BEFORE the probes ran — the list the import used to
            // carry across them — go straight over the edit the user just made.
            AgentProposalApplier.Apply(preparedBefore);
            Assert.Equal(-3, settings.GainDb);
        });
    }

    [Fact]
    public void Commit_WritesTheRows_WhenTheTuneStoodStill()
    {
        StaTest.Run(() =>
        {
            using VirtualCrossoverPanel panel = Loaded();
            VirtualCrossoverChannelSettings settings =
                Channels(panel)[0].SideSettings(rightSide: false);
            settings.GainDb = -1;
            var proposal = new AgentProposal(
                null, "trim it", [], [],
                [new SetGainOperation("op-1", "A:left", "too hot", -1, -3)], []);
            AgentSessionSnapshot reviewed = Snapshot(panel);
            AgentProposalReview review = AgentProposalValidator.Review(proposal, reviewed);

            var summary = new List<string>();
            Commit(
                panel, proposal, new HashSet<string> { "op-1" },
                reviewed.Fingerprint, review.Verdicts.Count, summary);

            Assert.Equal(-3, settings.GainDb);
            Assert.Contains("Applied 1 of 1 proposed change.", summary);
        });
    }

    [Fact]
    public void Probe_MarksTheBaselineByItsPosition_NotByTheReplysLabel()
    {
        StaTest.Run(() =>
        {
            using VirtualCrossoverPanel panel = Loaded();
            MeasuredJunction(panel, out VirtualCrossoverChannel lower, out VirtualCrossoverChannel upper);
            string? copied = null;
            Action<string> write = AgentClipboard.WriteText;
            AgentClipboard.WriteText = text => copied = text;
            var summary = new List<string>();
            try
            {
                // The label is the reply's own text, and a reply may call its
                // variant whatever it likes — "current" included.
                RunProbes(panel,
                    [
                        Row(new ProbeOperation("op-1", "", AgentProtocol.JunctionProbe,
                            $"left:{lower.Name}-{upper.Name}",
                            [
                                new AgentProbeVariant("current",
                                    [new AgentProbeChange($"{upper.Name}:left", -4, null, null, null, null)])
                            ]), "Probe")
                    ],
                    summary);
            }
            finally
            {
                AgentClipboard.WriteText = write;
            }

            string text = Assert.IsType<string>(copied);
            Assert.Equal(2, Regex.Matches(text, "\"label\":\"current\"").Count);
            // Exactly one of them is the tune as it stands.
            Assert.Single(Regex.Matches(text, "\"current\":true"));
        });
    }

    [Fact]
    public void Probe_TurnsAReadingThatThrowsIntoItsOwnUnavailableEntry()
    {
        StaTest.Run(() =>
        {
            using VirtualCrossoverPanel panel = Loaded();
            MeasuredJunction(panel, out VirtualCrossoverChannel lower, out VirtualCrossoverChannel upper);
            // Both channels full range: the pair still resolves as a junction
            // (they overlap everywhere), but there is no corner to read a delay
            // band from — the state the review refuses and a session that moved
            // between the review and Apply can still reach.
            foreach (bool rightSide in new[] { false, true })
            {
                lower.SideSettings(rightSide).CrossoverKind = CrossoverKind.Off;
                upper.SideSettings(rightSide).CrossoverKind = CrossoverKind.Off;
            }

            string? copied = null;
            Action<string> write = AgentClipboard.WriteText;
            AgentClipboard.WriteText = text => copied = text;
            var summary = new List<string>();
            try
            {
                bool ran = RunProbes(panel,
                    [
                        Row(new ProbeOperation("op-1", "", AgentProtocol.JunctionDelayProbe,
                            $"left:{lower.Name}-{upper.Name}", null), "Probe"),
                        Row(new ProbeOperation("op-2", "", AgentProtocol.ExcessGroupDelayProbe, null, null), "Probe")
                    ],
                    summary);

                Assert.True(ran, string.Join(" | ", summary));
            }
            finally
            {
                AgentClipboard.WriteText = write;
            }

            // The one that could not be read says so; the other still answered
            // and the document still reached the clipboard.
            Assert.Contains("1 of 2 readings computed", summary[0]);
            Assert.Contains("no crossover to read a band from", summary[1]);
            string text = Assert.IsType<string>(copied);
            Assert.Contains("\"probe\":\"excessGroupDelay\"", text);
            Assert.Contains("\"unavailable\"", text);
        });
    }

    // Two blocks with a measurement each and a Linkwitz-Riley pair between them.
    private static void MeasuredJunction(
        VirtualCrossoverPanel panel,
        out VirtualCrossoverChannel lower,
        out VirtualCrossoverChannel upper)
    {
        lower = Channels(panel)[0];
        upper = Channels(panel)[1];
        var lr = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24);
        foreach (bool rightSide in new[] { false, true })
        {
            foreach (VirtualCrossoverChannel channel in new[] { lower, upper })
            {
                VirtualCrossoverChannelState state = channel.SideState(rightSide);
                var impulse = new System.Numerics.Complex[16_384];
                impulse[480] = System.Numerics.Complex.One;
                state.TransferImpulseResponse = impulse;
                state.TransferPeakIndex = 480;
                state.SampleRate = 48_000;
            }
            lower.SideSettings(rightSide).CrossoverKind = CrossoverKind.LowPass;
            lower.SideSettings(rightSide).LowPassEdge = lr;
            upper.SideSettings(rightSide).CrossoverKind = CrossoverKind.HighPass;
            upper.SideSettings(rightSide).HighPassEdge = lr;
        }
    }

    [Fact]
    public void Probe_SaysWhatItCouldNotRead_WithoutTakingTheOthersDown()
    {
        StaTest.Run(() =>
        {
            using VirtualCrossoverPanel panel = Loaded();
            string? copied = null;
            Action<string> write = AgentClipboard.WriteText;
            AgentClipboard.WriteText = text => copied = text;
            var summary = new List<string>();
            try
            {
                // No block here carries a measurement: the junction cannot be
                // resolved, and the curve has nothing to read.
                RunProbes(panel,
                    [
                        Row(new ProbeOperation("op-1", "", AgentProtocol.JunctionDelayProbe, "left:A-B", null), "Probe"),
                        Row(new ProbeOperation("op-2", "", AgentProtocol.ExcessGroupDelayProbe, null, null), "Probe")
                    ],
                    summary);
            }
            finally
            {
                AgentClipboard.WriteText = write;
            }

            Assert.NotNull(copied);
            Assert.Contains("0 of 2 readings computed", summary[0]);
            Assert.Contains("has no measurement", summary[1]);
            Assert.Contains("no channel has a measurement to read", summary[2]);
        });
    }

    [Fact]
    public void JunctionTune_KeepsATextbookJunction_AndSaysSo()
    {
        StaTest.Run(() =>
        {
            using VirtualCrossoverPanel panel = Loaded();
            VirtualCrossoverChannel lower = Channels(panel)[0];
            VirtualCrossoverChannel upper = Channels(panel)[1];
            var lr = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24);
            foreach (bool rightSide in new[] { false, true })
            {
                foreach (VirtualCrossoverChannel channel in new[] { lower, upper })
                {
                    VirtualCrossoverChannelState state = channel.SideState(rightSide);
                    var impulse = new System.Numerics.Complex[16_384];
                    impulse[480] = System.Numerics.Complex.One;
                    state.TransferImpulseResponse = impulse;
                    state.TransferPeakIndex = 480;
                    state.SampleRate = 48_000;
                }
                lower.SideSettings(rightSide).CrossoverKind = CrossoverKind.LowPass;
                lower.SideSettings(rightSide).LowPassEdge = lr;
                upper.SideSettings(rightSide).CrossoverKind = CrossoverKind.HighPass;
                upper.SideSettings(rightSide).HighPassEdge = lr;
            }
            var summary = new List<string>();

            bool ran = RunEnginesPumping(panel,
                [
                    Row(new TuneJunctionOperation("op-1", "", $"left:{lower.Name}-{upper.Name}",
                        900, 1_100, ["LinkwitzRiley"], [24], null), "Junction tune")
                ],
                summary);

            // Nothing beats a Linkwitz-Riley pair on flat drivers by the keep
            // margin, so nothing is written and Undo has nothing to arm for.
            Assert.False(ran);
            Assert.StartsWith($"Junction tune {lower.Name}/{upper.Name}: kept — ", summary[0]);
            Assert.Contains("not 0.50 dB better", summary[0]);
            Assert.Equal(lr, lower.SideSettings(true).LowPassEdge);
            Assert.Equal(lr, upper.SideSettings(true).HighPassEdge);
        });
    }

    [Fact]
    public void EngineOrder_PutsTheJunctionTuneAfterTheWizard_AndBeforeAutoDelay()
    {
        StaTest.Run(() =>
        {
            using VirtualCrossoverPanel panel = Loaded();
            var summary = new List<string>();

            // Listed backwards on purpose; every engine refuses on this empty
            // panel, so the summary is the order alone.
            RunEngines(panel,
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
        });
    }

    [Fact]
    public void AutoDelay_RunHeadless_IsRefusedWithThePhraseTheSummaryQuotes()
    {
        StaTest.Run(() =>
        {
            using VirtualCrossoverPanel panel = Loaded();
            var summary = new List<string>();

            // The button's own first check, answered as a phrase instead of a
            // beep: no block here carries a measurement.
            bool ran = RunEngines(panel,
                [Row(new RunAutoDelayOperation("op-1", "", 0.35, null, null, null, null), "Auto delay")],
                summary);

            Assert.False(ran);
            Assert.Equal(
                ["Auto delay: skipped (fewer than two enabled channels have a measurement)."],
                summary);
        });
    }

    [Fact]
    public void UndoAiImport_PutsBackTheStereoSceneTheTiltAndTheRearFill()
    {
        StaTest.Run(() =>
        {
            // What an Auto delay run commits beside the channels
            // (CommitAutoDelayResult): the scene offset with its layout, the
            // level tilt, the rear-fill offset.
            using VirtualCrossoverPanel panel = Loaded();
            VirtualCrossoverProjectFile project = Project(panel);
            project.SetStereoScene(0.25, rightHandDrive: false);
            project.StereoLevelDifferenceDb = -1.0;
            project.RearFillOffsetMs = 15.0;

            object undo = Invoke(panel, "CaptureAgentUndo")!;
            project.SetStereoScene(0.6, rightHandDrive: true);
            project.StereoLevelDifferenceDb = 2.5;
            project.RearFillOffsetMs = 9.0;

            Set(panel, "agentUndo", undo);
            Set(panel, "agentUndoGeneration", Field(panel, "projectGeneration"));
            Invoke(panel, "UndoAiImport");

            Assert.Equal(0.25, project.StereoSceneOffsetMagnitudeMs);
            Assert.False(project.StereoRightHandDrive);
            Assert.Equal(-1.0, project.StereoLevelDifferenceDb);
            Assert.Equal(15.0, project.RearFillOffsetMs);
        });
    }

    // The engines run as the import runs them — awaited; on a panel with no
    // measurement every engine answers before it would await anything, so the
    // task is complete by the time it is handed back.
    [Fact]
    public void Fingerprint_MovesWithWhatAPackageVouchesFor_AndComesBackWithUndo()
    {
        StaTest.Run(() =>
        {
            using VirtualCrossoverPanel panel = Loaded();
            Project(panel).SpatialAverageMode = VirtualCrossoverSpatialAverageMode.Off;
            Hybrid(panel).Checked = false;
            string baseline = Fingerprint(panel);
            Assert.Equal(baseline, Fingerprint(panel));

            // Each of these was once a path that had to remember to forget the
            // package, and the two the review found missing — a source picked by
            // hand, a capture attached — are the reason it is a hash now.
            VirtualCrossoverChannelSettings left = Channels(panel)[0].SideSettings(rightSide: false);
            var seen = new HashSet<string>(StringComparer.Ordinal) { baseline };
            void Moves(Action change)
            {
                change();
                Assert.True(seen.Add(Fingerprint(panel)), "the fingerprint did not move");
            }

            Moves(() => left.SourceFilePath = @"D:\measurements\left mid (retaken).json");
            // A measurement saved over its own file: same reference, same length,
            // same rate, other samples — the CONTENT is what the hash reads.
            VirtualCrossoverChannelState leftState = Channels(panel)[0].SideState(rightSide: false);
            var samples = new System.Numerics.Complex[64];
            samples[3] = new System.Numerics.Complex(0.5, 0);
            Moves(() => leftState.TransferImpulseResponse = samples);
            var retaken = new System.Numerics.Complex[64];
            retaken[3] = new System.Numerics.Complex(0.4, 0);
            Moves(() => leftState.TransferImpulseResponse = retaken);
            // The same samples in another array are the same measurement.
            leftState.TransferImpulseResponse = (System.Numerics.Complex[])retaken.Clone();
            Assert.Contains(Fingerprint(panel), seen);
            Moves(() => leftState.TransferCoherence = [0.9, 0.8]);
            Moves(() => leftState.MeasuredBand = new MeasuredBand(40, 8000));
            Moves(() => leftState.TransferPeakIndex += 1);
            // The correction the side is read through, by its points: the same
            // file re-read with one value edited is another correction.
            Set(panel, "ownCalibrationSelected", true);
            Moves(() => leftState.MicrophoneCalibration =
                new VirtualCrossoverCalibrationSettings { Name = "mic", Points = [[1000, 0.5], [2000, 1.0]] });
            Moves(() => leftState.MicrophoneCalibration =
                new VirtualCrossoverCalibrationSettings { Name = "mic", Points = [[1000, 0.5], [2000, 1.5]] });
            Moves(() => Project(panel).AiNotes = "Front: 6.5\" mids in the doors.");
            Moves(() => Project(panel).Target = new VirtualCrossoverTargetSettings { ImportedCurve = [1, 2, 3] });
            Moves(() => Project(panel).Target!.ImportedCurve = [1, 2, 4]);
            Moves(() => left.SpatialAveragePath = @"D:\measurements\left mid mmm.json");
            Moves(() => leftState.SpatialAverage = new LiveCaptureDocument { CaptureSessionId = Guid.NewGuid() });
            Moves(() => leftState.SpatialAverage = new LiveCaptureDocument { CaptureSessionId = Guid.NewGuid() });
            Moves(() => left.GainDb -= 1.5);
            Moves(() => left.PeqBands = [new Resonalyze.Dsp.PeqBand(820, 2.1, -2.4)]);
            Moves(() => Invoke(panel, "MoveChannel", Channels(panel)[0], 1));
            Moves(() => Hybrid(panel).Checked = true);
            Moves(() => Project(panel).SpatialAverageMode = VirtualCrossoverSpatialAverageMode.MovingMic);
            Moves(() => Project(panel).PhaseGateLeft.OffsetMs = 3.25);
            Moves(() => Project(panel).StereoLevelDifferenceDb = -2.0);
            Moves(() => Project(panel).Pairs[0].Mono = true);
            // The side on screen and the view: what Auto crossover and a
            // single-sided Auto delay read, and what decides Auto-tune's source.
            Moves(() => Project(panel).ActiveSideRight = true);
            dynamic groupView = Field(panel, "comboBoxGroupView");
            Moves(() => groupView.SelectedItem = VirtualCrossoverGroupView.Everything);
            Moves(() => ((DarkNumericUpDown)Field(panel, "numericTargetLevel")).Value += 1);
        });
    }

    [Fact]
    public void Fingerprint_IsBackWhereItWas_AfterUndoAiImport()
    {
        StaTest.Run(() =>
        {
            using VirtualCrossoverPanel panel = Loaded();
            Project(panel).SpatialAverageMode = VirtualCrossoverSpatialAverageMode.Off;
            Hybrid(panel).Checked = false;
            string before = Fingerprint(panel);

            object undo = Invoke(panel, "CaptureAgentUndo")!;
            Invoke(panel, "ApplyAgentSpatialAverage",
                new UseSpatialAverageOperation("op-1", "", "MicArray", true));
            Channels(panel)[0].SideSettings(rightSide: false).GainDb -= 6;
            Invoke(panel, "MoveChannel", Channels(panel)[0], 1);
            Assert.NotEqual(before, Fingerprint(panel));

            Set(panel, "agentUndo", undo);
            Set(panel, "agentUndoGeneration", Field(panel, "projectGeneration"));
            Invoke(panel, "UndoAiImport");

            // The guide's diagnostic pass: clear the banks, copy a package, undo.
            // The session is then the one BEFORE the import again — not the one
            // that package described — and the review reads it off the hash.
            Assert.Equal(before, Fingerprint(panel));
        });
    }

    private static string Fingerprint(VirtualCrossoverPanel panel) =>
        (string)Invoke(panel, "ComputeAgentFingerprint")!;

    private static bool RunEngines(
        VirtualCrossoverPanel panel, List<AgentOperationVerdict> rows, List<string> summary) =>
        ((Task<bool>)Invoke(panel, "RunAgentEngineRequests", rows, summary, null)!)
            .GetAwaiter().GetResult();

    private static AgentSessionSnapshot Snapshot(VirtualCrossoverPanel panel) =>
        (AgentSessionSnapshot)Invoke(panel, "BuildAgentSessionSnapshot")!;

    private static bool Commit(
        VirtualCrossoverPanel panel,
        AgentProposal proposal,
        IReadOnlySet<string> selected,
        string? reviewedFingerprint,
        int proposedRows,
        List<string> summary)
    {
        var task = (Task<bool>)Invoke(
            panel, "CommitAgentImportAsync",
            proposal, selected, reviewedFingerprint, proposedRows, summary, null)!;
        while (!task.IsCompleted)
        {
            Application.DoEvents();
            Thread.Sleep(5);
        }

        return task.GetAwaiter().GetResult();
    }

    private static bool RunProbes(
        VirtualCrossoverPanel panel, List<AgentOperationVerdict> rows, List<string> summary)
    {
        // No progress window in a test: the panel takes a null reporter.
        var task = (Task<bool>)Invoke(panel, "RunAgentProbesAsync", rows, summary, null)!;
        while (!task.IsCompleted)
        {
            Application.DoEvents();
            Thread.Sleep(5);
        }

        return task.GetAwaiter().GetResult();
    }

    // For an engine that awaits its compute: the continuation is posted to the
    // STA thread's WinForms context, so the wait has to pump messages or the
    // two deadlock — which is exactly what the panel's own UI thread does.
    private static bool RunEnginesPumping(
        VirtualCrossoverPanel panel, List<AgentOperationVerdict> rows, List<string> summary)
    {
        var task = (Task<bool>)Invoke(panel, "RunAgentEngineRequests", rows, summary, null)!;
        while (!task.IsCompleted)
        {
            Application.DoEvents();
            Thread.Sleep(5);
        }

        return task.GetAwaiter().GetResult();
    }

    private static AgentOperationVerdict Row(AgentOperation operation, string parameter) =>
        new(operation.Id, AgentProposalValidator.AllChannels, parameter,
            string.Empty, string.Empty, AgentVerdictStatus.Warning, string.Empty,
            operation.Reason, operation, null);

    // A panel bound to its project's pairs, the way applying a project binds
    // them: without that the two lists are unrelated objects and a reorder has
    // nothing to permute.
    private static VirtualCrossoverPanel Loaded()
    {
        var panel = new VirtualCrossoverPanel();
        List<VirtualCrossoverChannel> channels = Channels(panel);
        for (int index = 0; index < channels.Count; index++)
        {
            channels[index].Pair = Project(panel).Pairs[index];
        }

        return panel;
    }

    private static object Field(object target, string name) =>
        target.GetType().GetField(name, Hidden)!.GetValue(target)!;

    private static void Set(object target, string name, object? value) =>
        target.GetType().GetField(name, Hidden)!.SetValue(target, value);

    private static object? Invoke(object target, string name, params object?[] arguments) =>
        target.GetType().GetMethod(name, Hidden)!.Invoke(target, arguments);

    private static List<VirtualCrossoverChannel> Channels(VirtualCrossoverPanel panel) =>
        (List<VirtualCrossoverChannel>)Field(panel, "channels");

    private static VirtualCrossoverProjectFile Project(VirtualCrossoverPanel panel) =>
        (VirtualCrossoverProjectFile)Field(panel, "project");

    private static CheckBox Hybrid(VirtualCrossoverPanel panel) =>
        (CheckBox)Field(panel, "checkBoxHybrid");
}
