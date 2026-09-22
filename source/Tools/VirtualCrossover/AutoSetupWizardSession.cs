using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Existing corners are how the user says which of two similar drivers plays lower.</summary>
internal sealed record AutoSetupWizardChannel(
    string Name,
    Color Accent,
    VirtualCrossoverAlignmentStage Group,
    IReadOnlyList<SignalPoint> MagnitudeDb,
    IReadOnlyList<double>? Coherence,
    IReadOnlyList<SignalPoint>? Distortion,
    DriverBandEstimate Band,
    double? HighPassHz,
    double? LowPassHz,
    Complex[]? ImpulseResponse);

/// <summary>One channel as the wizard lists it; the driver type is the user's to confirm.</summary>
internal sealed class AutoSetupWizardRow(int initIndex, AutoSetupWizardChannel source)
{
    /// <summary>Its position in the list the wizard was opened on, which is the order proposals come back in.</summary>
    public int InitIndex { get; } = initIndex;

    public AutoSetupWizardChannel Source { get; } = source;

    public DriverType Type { get; set; } = source.Band.SuggestedType;
}

internal sealed record AutoSetupWizardJunction(
    VirtualCrossoverAlignmentStage Group,
    int IndexInGroup,
    AutoSetupWizardChannel Lower,
    AutoSetupWizardChannel Upper);

/// <summary>What the user set on one junction; a null field is the wizard's to resolve.</summary>
internal sealed record AutoSetupJunctionEdits(
    decimal? MinHz,
    decimal? MaxHz,
    int? MinSlope,
    int? MaxSlope,
    bool Split)
{
    public static readonly AutoSetupJunctionEdits None = new(null, null, null, null, false);

    public bool IsEmpty =>
        MinHz == null && MaxHz == null && MinSlope == null && MaxSlope == null && !Split;

    public JunctionSearchWindow ToWindow() =>
        new((double?)MinHz, (double?)MaxHz, MinSlope, MaxSlope, Split);
}

/// <summary>The crossover wizard's state: the channels in chain order with their types, what the user set on each
/// junction, and the options, each number held as its field shows it. See docs/tech/virtual-dsp-panel.md#crossover-wizard-code-map.</summary>
internal sealed class AutoSetupWizardSession
{
    public static readonly IReadOnlyList<CrossoverFilterFamily> OfferedFamilies =
        [CrossoverFilterFamily.Butterworth, CrossoverFilterFamily.LinkwitzRiley, CrossoverFilterFamily.Bessel];

    private static readonly NumericFieldRange CrossoverField = new(20m, 20_000m, 0);

    // Groups stay contiguous: nothing moves a channel across one.
    private readonly List<AutoSetupWizardRow> rows = [];

    // By pair, not by position: a reorder must not carry a window onto the pair that takes its place.
    private readonly Dictionary<(AutoSetupWizardChannel, AutoSetupWizardChannel), AutoSetupJunctionEdits>
        junctionEdits = new();

    private readonly HashSet<CrossoverFilterFamily> families = [.. OfferedFamilies];
    private decimal minCrossoverHz;
    private decimal maxCrossoverHz;
    private decimal subElevationDb;

    public AutoSetupWizardSession(
        double sampleRateHz,
        double processorSampleRateHz,
        IReadOnlyList<AutoSetupWizardChannel> channels)
    {
        SampleRateHz = sampleRateHz;
        ProcessorSampleRateHz = processorSampleRateHz;
        // At 44.1 kHz this keeps 20 kHz reachable.
        double ceiling = Math.Min(20_000, sampleRateHz * 0.49);
        CrossoverRange = CrossoverField.WithMaximum((decimal)Math.Round(ceiling));
        minCrossoverHz = CrossoverRange.Contain(CrossoverField.Minimum);
        maxCrossoverHz = CrossoverRange.Contain(CrossoverField.Maximum);
        foreach (VirtualCrossoverAlignmentStage group in VirtualCrossoverAlignmentStages.InOrder)
        {
            IEnumerable<(AutoSetupWizardChannel Channel, int Index)> members = channels
                .Select((channel, index) => (channel, index))
                .Where(item => item.channel.Group == group)
                // Seeded from each channel's effective band; the arrows override where the measurement cannot decide.
                .OrderBy(item => VirtualCrossoverAutoSetupOrder.CenterHz(
                    item.channel.Band, item.channel.HighPassHz, item.channel.LowPassHz));
            foreach ((AutoSetupWizardChannel channel, int index) in members)
            {
                rows.Add(new AutoSetupWizardRow(index, channel));
            }
        }

        SubElevationApplies = MembersOf(PrimaryGroup()).Count > 1;
    }

    public double SampleRateHz { get; }

    /// <summary>Independent of the measurement rate, which only bounds the analysis band.</summary>
    public double ProcessorSampleRateHz { get; }

    /// <summary>Groups as staged, chain order inside each.</summary>
    public IReadOnlyList<AutoSetupWizardRow> Rows => rows;

    public NumericFieldRange CrossoverRange { get; }

    public decimal MinCrossoverHz
    {
        get => minCrossoverHz;
        set => minCrossoverHz = CrossoverRange.Assign(value);
    }

    public decimal MaxCrossoverHz
    {
        get => maxCrossoverHz;
        set => maxCrossoverHz = CrossoverRange.Assign(value);
    }

    public bool IndependentSlopes { get; set; } = true;

    public bool ReorderBlocks { get; set; } = true;

    /// <summary>The bass elevation applies only where the primary group is a chain.</summary>
    public bool SubElevationApplies { get; }

    /// <summary>Capped by the measured elevation once a fit has read it.</summary>
    public NumericFieldRange ElevationRange { get; private set; } = new(0m, 60m, 1);

    public decimal SubElevationDb
    {
        get => subElevationDb;
        set => subElevationDb = ElevationRange.Assign(value);
    }

    /// <summary>Pre-filled once from the first measured elevation; until then the DSP uses the measured default.</summary>
    public bool SubElevationInitialized { get; private set; }

    public void SetFamily(CrossoverFilterFamily family, bool enabled)
    {
        if (enabled)
        {
            families.Add(family);
        }
        else
        {
            families.Remove(family);
        }
    }

    public IReadOnlyList<CrossoverFilterFamily> SelectedFamilies() =>
        OfferedFamilies.Where(families.Contains).ToList();

    /// <summary>The measured elevation a fit read: the cap moves with it, and the first one becomes the value.</summary>
    public void TakeElevation(decimal ceiling, decimal? firstValue)
    {
        ElevationRange = ElevationRange.WithMaximum(Math.Max(ceiling, ElevationRange.Minimum));
        subElevationDb = ElevationRange.Contain(subElevationDb);
        if (firstValue is { } value)
        {
            SubElevationInitialized = true;
            SubElevationDb = Math.Clamp(value, ElevationRange.Minimum, ElevationRange.Maximum);
        }
    }

    public IEnumerable<VirtualCrossoverAlignmentStage> GroupsInOrder() =>
        VirtualCrossoverAlignmentStages.InOrder
            .Where(group => rows.Any(row => row.Source.Group == group));

    public List<AutoSetupWizardRow> MembersOf(VirtualCrossoverAlignmentStage group) =>
        rows.Where(row => row.Source.Group == group).ToList();

    /// <summary>The front chain; without one, the first staged group.</summary>
    public VirtualCrossoverAlignmentStage PrimaryGroup() =>
        GroupsInOrder()
            .DefaultIfEmpty(VirtualCrossoverAlignmentStage.FrontChain)
            .First();

    /// <returns>False when the row is already at that end of its group.</returns>
    public bool MoveInChain(AutoSetupWizardRow row, int delta)
    {
        List<AutoSetupWizardRow> members = MembersOf(row.Source.Group);
        int at = members.IndexOf(row);
        int to = at + delta;
        if (at < 0 || to < 0 || to >= members.Count)
        {
            return false;
        }

        int one = rows.IndexOf(members[at]);
        int other = rows.IndexOf(members[to]);
        (rows[one], rows[other]) = (rows[other], rows[one]);
        return true;
    }

    public List<AutoSetupWizardJunction> Junctions()
    {
        var junctions = new List<AutoSetupWizardJunction>();
        foreach (VirtualCrossoverAlignmentStage group in GroupsInOrder())
        {
            List<AutoSetupWizardRow> members = MembersOf(group);
            for (int i = 0; i < members.Count - 1; i++)
            {
                junctions.Add(new AutoSetupWizardJunction(group, i, members[i].Source, members[i + 1].Source));
            }
        }

        return junctions;
    }

    public AutoSetupJunctionEdits EditsOf(AutoSetupWizardJunction junction) =>
        junctionEdits.GetValueOrDefault((junction.Lower, junction.Upper), AutoSetupJunctionEdits.None);

    public void Edit(AutoSetupWizardJunction junction, AutoSetupJunctionEdits edits)
    {
        if (edits.IsEmpty)
        {
            junctionEdits.Remove((junction.Lower, junction.Upper));
        }
        else
        {
            junctionEdits[(junction.Lower, junction.Upper)] = edits;
        }
    }

    /// <summary>Init indices in crossed order; null when the blocks are not to be reordered.</summary>
    public IReadOnlyList<int>? RequestedChainOrder() =>
        ReorderBlocks
            ? rows.Select(row => row.InitIndex).ToList()
            : null;
}
