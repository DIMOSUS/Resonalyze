using Resonalyze.Dsp;
using Resonalyze.Integration.AgentBridge;

namespace Resonalyze.App.Tests;

/// <summary>Tests that swap the clipboard transport, which is one static for the process, run one at a time.</summary>
[CollectionDefinition(Name)]
public sealed class AgentClipboardUsers
{
    public const string Name = "Agent clipboard";
}

/// <summary>An AI import over a bare session: the runner, its readers, and a host that records what the panel would show.</summary>
internal sealed class AgentImportFixture : IDisposable, IAgentImportHost
{
    private readonly VirtualCrossoverProcessingCoordinator coordinator = new();

    public AgentImportFixture()
    {
        for (int index = 0; index < Session.Project.Pairs.Count; index++)
        {
            Session.Channels.Add(new VirtualCrossoverChannel(VirtualCrossoverSheet.ChannelName(index))
            {
                Pair = Session.Project.Pairs[index],
                ActiveRightProvider = () => Session.ActiveSideRight,
                ProcessorSampleRateProvider = () => Session.ProcessorSampleRateHz
            });
        }

        var metrics = VirtualCrossoverMetrics.Through(
            coordinator, () => Session.MagnitudeGate, oppositeSide: false, channel => Session.Calibration.For(channel));
        var hybrid = new VirtualCrossoverHybrid(Session);
        Reader = new AgentSessionReader(Session, coordinator, metrics, hybrid);
        Runner = new AgentImportRunner(
            Session, Reader, new VirtualCrossoverEqHandoff(Session, coordinator, metrics, hybrid), this, UndoHistory);
    }

    public VirtualCrossoverSession Session { get; } = new();

    public VirtualCrossoverUndoHistory UndoHistory { get; } = new();

    public AgentSessionReader Reader { get; }

    public AgentImportRunner Runner { get; }

    public List<VirtualCrossoverChannel> Channels => Session.Channels;

    public VirtualCrossoverProjectFile Project => Session.Project;

    /// <summary>What the host was asked to do, in order.</summary>
    public List<string> Calls { get; } = [];

    public bool HybridTicked { get; set; }

    public VirtualCrossoverGroupView GroupView { get; set; } = VirtualCrossoverGroupView.FrontAndSub;

    public string? WizardRefusal { get; set; } = "fewer than two enabled channels have a measurement";

    public bool IsGone { get; set; }

    public GatePlacementVerdict? GatePlacement { get; set; }

    public bool HybridRequested { get; set; }

    public AgentViewInputs View() => new(GroupView, HybridTicked, HybridRequested, Project.TargetLevelDb, null);

    public EqAutoTunePolicy AutoTunePolicy() => EqAutoTunePolicy.Default;

    public void UseSpatialAverage(VirtualCrossoverSpatialAverageMode mode)
    {
        Calls.Add($"spatial average {mode}");
        Project.SpatialAverageMode = mode;
        HybridTicked = true;
        Project.ShowHybridCurves = true;
    }

    public string? OpenAutoSetupWizard()
    {
        Calls.Add("wizard");
        return WizardRefusal;
    }

    public Task ApplyAutoDelayAsync(AutoDelayRunResult result)
    {
        Calls.Add("auto delay applied");
        VirtualCrossoverAutoDelay.Commit(result, Project);
        return Task.CompletedTask;
    }

    public void ShowChannel(VirtualCrossoverChannel channel) => Calls.Add("show " + channel.Name);

    public void ShowBank(VirtualCrossoverChannel channel) => Calls.Add("bank " + channel.Name);

    public void SetTargetLevel(double levelDb)
    {
        Calls.Add($"level {levelDb}");
        Project.TargetLevelDb = levelDb;
    }

    public void RememberSides() => Calls.Add("remember sides");

    public void SaveAndRedraw() => Calls.Add("save");

    /// <summary>Runs as the work gives the panel back: an edit made while a fit ran.</summary>
    public Action? WhenIdle { get; set; }

    public IDisposable Busy(bool disable)
    {
        Calls.Add(disable ? "busy, disabled" : "busy");
        return new DisposeAction(() =>
        {
            Calls.Add("idle");
            WhenIdle?.Invoke();
        });
    }

    /// <summary>Both sides of the first <paramref name="count"/> blocks carry a unit impulse at 48 kHz.</summary>
    public void Measure(int count)
    {
        foreach (VirtualCrossoverChannel channel in Channels.Take(count))
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
    }

    /// <summary>A and B hand over at 1 kHz, LR24 on both sides.</summary>
    public (VirtualCrossoverChannel Lower, VirtualCrossoverChannel Upper) MeasuredJunction()
    {
        Measure(2);
        var lr = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24);
        foreach (bool rightSide in new[] { false, true })
        {
            Channels[0].SideSettings(rightSide).CrossoverKind = CrossoverKind.LowPass;
            Channels[0].SideSettings(rightSide).LowPassEdge = lr;
            Channels[1].SideSettings(rightSide).CrossoverKind = CrossoverKind.HighPass;
            Channels[1].SideSettings(rightSide).HighPassEdge = lr;
        }

        return (Channels[0], Channels[1]);
    }

    public static AgentOperationVerdict Row(AgentOperation operation, string parameter) =>
        new(operation.Id, AgentProposalValidator.AllChannels, parameter,
            string.Empty, string.Empty, AgentVerdictStatus.Warning, string.Empty,
            operation.Reason, operation, null);

    /// <summary>What a probe put on the clipboard, with the transport restored afterwards.</summary>
    public static async Task<(T Result, string? Copied)> Copying<T>(Func<Task<T>> run)
    {
        string? copied = null;
        Action<string> write = AgentClipboard.WriteText;
        AgentClipboard.WriteText = text => copied = text;
        try
        {
            return (await run(), copied);
        }
        finally
        {
            AgentClipboard.WriteText = write;
        }
    }

    public void Dispose() => coordinator.Dispose();
}
