using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>The tune under edit and how it is read: the project, its channel blocks, the calibration and the gates.
/// Everything that computes on the tune takes this rather than the panel; the panel writes it and nothing here reads a
/// control. State and the queries that follow from it only; a feature's own logic lives in the feature's type.</summary>
internal sealed class VirtualCrossoverSession
{
    private const int DefaultSampleRateHz = 48_000;

    public VirtualCrossoverProjectFile Project { get; set; } = new();

    /// <summary>In block order; a block's list position is its letter and its colour.</summary>
    public List<VirtualCrossoverChannel> Channels { get; } = [];

    public VirtualCrossoverCalibrationPolicy Calibration { get; set; } =
        VirtualCrossoverCalibrationPolicy.None;

    /// <summary>The Gate dialog's candidates while it is open; null once it closes.</summary>
    public VirtualCrossoverGatePreview? GatePreview { get; set; }

    /// <summary>Refreshed by each redraw on the UI thread, read by its workers: one frame reads one snapshot.</summary>
    public MagnitudeGateSnapshot MagnitudeGate { get; set; } = MagnitudeGateSnapshot.Initial;

    /// <summary>Extra search root from relinking an imported session's missing measurements; cleared on bind.</summary>
    public string? RelinkDirectory { get; set; }

    public bool ActiveSideRight => Project.ActiveSideRight;

    public VirtualCrossoverPhaseGate Gate => GateFor(Project.ActiveSideRight);

    // Each side keeps its own placement: its drivers arrive at different times.
    public VirtualCrossoverPhaseGate GateFor(bool rightSide) =>
        VirtualCrossoverPhaseGate.For(Project, rightSide, GatePreview);

    public VirtualCrossoverSpatialAverageMode SpatialAverageMode =>
        Project.SpatialAverageMode ?? (HasAnyArrayCapture()
            ? VirtualCrossoverSpatialAverageMode.MicArray
            : VirtualCrossoverSpatialAverageMode.MovingMic);

    /// <summary>Stores the guessed mode once, the first time there is anything to guess from; true when it wrote one.</summary>
    /// <remarks>Freezing keeps a later measurement from flipping the project's level source. See docs/tech/spatial-average.md#mode-selection.</remarks>
    public bool SettleSpatialAverageMode()
    {
        if (Project.SpatialAverageMode != null)
        {
            return false;
        }

        bool attachments = Channels.Any(channel =>
            channel.SideState(false).SpatialAverage != null ||
            channel.SideState(true).SpatialAverage != null);
        if (!attachments && !HasAnyArrayCapture())
        {
            return false;
        }

        Project.SpatialAverageMode = SpatialAverageMode;
        return true;
    }

    private bool HasAnyArrayCapture() =>
        Channels.Any(channel =>
            channel.SideState(false).ArrayCapture != null ||
            channel.SideState(true).ArrayCapture != null);

    /// <summary>Null while the project has no measurement, so a read-out can say "nothing yet".</summary>
    /// <remarks>One rate per project (disagreeing measurements are rejected), so the first resolved side answers. Both
    /// physical sides are read: the shown side may be empty.</remarks>
    public int? MeasuredSampleRateHz
    {
        get
        {
            foreach (VirtualCrossoverChannel channel in Channels)
            {
                int leftRate = channel.PhysicalSideState(rightSide: false).SampleRate;
                if (leftRate > 0)
                {
                    return leftRate;
                }

                int rightRate = channel.PhysicalSideState(rightSide: true).SampleRate;
                if (rightRate > 0)
                {
                    return rightRate;
                }
            }

            return null;
        }
    }

    /// <summary>A named model answers from the catalog; Custom without a stored rate follows the measurements.</summary>
    public DspProcessorProfile ProcessorProfile =>
        Project.ResolveDspProcessor(MeasuredSampleRateHz ?? DefaultSampleRateHz);

    /// <summary>The rate simulated filters are designed at, NOT the measurement rate (see <see cref="Dsp.PreparedDspResponse"/>).</summary>
    public int ProcessorSampleRateHz => ProcessorProfile.SampleRateHz;

    /// <summary>Ceiling for automatic delay proposals; manual delay fields keep a wider range on purpose.</summary>
    public double ProcessorMaxDelayMs => ProcessorProfile.MaxDelayMs;

    /// <summary>The block with this letter; null once the blocks moved under whatever named it.</summary>
    public VirtualCrossoverChannel? Block(string name) =>
        Channels.FirstOrDefault(channel => string.Equals(channel.Name, name, StringComparison.Ordinal));

    /// <summary>Every side a block exposes: both of a stereo pair, the left alone of a mono one.</summary>
    public IEnumerable<(VirtualCrossoverChannel Channel, bool RightSide)> Sides()
    {
        foreach (VirtualCrossoverChannel channel in Channels)
        {
            yield return (channel, false);
            if (!channel.Pair.Mono)
            {
                yield return (channel, true);
            }
        }
    }

    public IEnumerable<(VirtualCrossoverChannel Channel, bool RightSide, VirtualCrossoverChannelState State)>
        ResolvedSidesExcept(VirtualCrossoverChannelState? except) =>
        Sides()
            .Select(side => (side.Channel, side.RightSide, State: side.Channel.SideState(side.RightSide)))
            .Where(side => side.State != except && side.State.TransferImpulseResponse != null);

    /// <summary>Only sides naming a FILE: a history-only reference from another machine is not fixable by a folder.</summary>
    public IEnumerable<(VirtualCrossoverChannel Channel, bool RightSide)> MissingSourceSides() =>
        Sides().Where(side =>
            !string.IsNullOrWhiteSpace(side.Channel.SideSettings(side.RightSide).SourceFilePath) &&
            side.Channel.SideState(side.RightSide).TransferImpulseResponse == null);

    /// <summary>Brings a bound project's sources back. BOTH slots of EVERY block are wiped before any side resolves: the rate
    /// guard votes over the resolved sides. See docs/tech/virtual-dsp-panel.md#project-restore-order.</summary>
    public async Task RestoreSourcesAsync(
        Func<VirtualCrossoverChannel, bool, Task> resolveSide,
        Action<VirtualCrossoverChannel> channelRestored)
    {
        foreach (VirtualCrossoverChannel channel in Channels)
        {
            channel.PhysicalSideState(false).Clear();
            channel.PhysicalSideState(true).Clear();
        }

        foreach (VirtualCrossoverChannel channel in Channels)
        {
            await resolveSide(channel, false);
            if (!channel.Pair.Mono)
            {
                await resolveSide(channel, true);
            }

            channelRestored(channel);
        }
    }

    /// <summary>The stored path, else beside the project, else beside the relinked folder; null when none exists.</summary>
    public string? Locate(string? storedPath, string? relativePath) =>
        VirtualCrossoverSourceLocator.Locate(storedPath, relativePath, Project.ProjectDirectory)
        ?? VirtualCrossoverSourceLocator.Locate(storedPath, relativePath, RelinkDirectory);
}
