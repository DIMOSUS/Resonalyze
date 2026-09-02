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

            object undo = Invoke(panel, "CaptureAgentUndo")!;
            Invoke(panel, "ApplyAgentSpatialAverage",
                new UseSpatialAverageOperation("op-1", "", "MicArray", true));
            // A channel no row named, and the reordering the wizard can do.
            Channels(panel)[0].SideSettings(rightSide: false).GainDb = gain - 6;
            Invoke(panel, "MoveChannel", Channels(panel)[0], 1);
            Assert.NotEqual(order, Channels(panel));

            Set(panel, "agentUndo", undo);
            Set(panel, "agentUndoGeneration", Field(panel, "projectGeneration"));
            Invoke(panel, "UndoAiImport");

            Assert.Equal(VirtualCrossoverSpatialAverageMode.Off, Project(panel).SpatialAverageMode);
            Assert.False(Hybrid(panel).Checked);
            Assert.False(Project(panel).ShowHybridCurves);
            Assert.Equal(order, Channels(panel));
            Assert.Equal(gain, Channels(panel)[0].SideSettings(rightSide: false).GainDb);
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
            bool ran = (bool)Invoke(panel, "RunAgentEngineRequests",
                new List<AgentOperationVerdict>
                {
                    Row(new RunAutoCrossoverOperation("op-1", "split them"), "Auto crossover"),
                    Row(new UseSpatialAverageOperation("op-2", "unused arrays", "MicArray", true),
                        "Spatial average")
                },
                summary)!;

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

            bool ran = (bool)Invoke(panel, "RunAgentEngineRequests",
                new List<AgentOperationVerdict>
                {
                    Row(new RunAutoCrossoverOperation("op-1", ""), "Auto crossover")
                },
                summary)!;

            // The import reads this answer to decide whether Undo stays armed: a
            // refused wizard moved nothing, and arming it would offer to put back
            // a tune nobody touched.
            Assert.False(ran);
            Assert.Single(summary);
            Assert.Contains("skipped", summary[0]);
        });
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
