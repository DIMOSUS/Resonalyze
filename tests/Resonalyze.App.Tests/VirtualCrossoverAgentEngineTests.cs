using System.Reflection;
using System.Windows.Forms;
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
        ((Task<bool>)Invoke(panel, "RunAgentEngineRequests", rows, summary)!)
            .GetAwaiter().GetResult();

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
