using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Crossover wizard; each group is fitted as its own chain, then levelled onto the front stage.
/// See docs/tech/crossover-auto-setup.md#groups-outside-the-chain.</summary>
internal sealed partial class VirtualCrossoverAutoSetupDialog : Form
{
    private readonly WrappingToolTip toolTip = new()
    {
        InitialDelay = 500,
        ReshowDelay = 150,
        AutoPopDelay = 12_000,
        ShowAlways = true
    };

    private readonly Dictionary<AutoSetupWizardRow, ChannelRow> rows = new();
    private readonly List<JunctionRow> junctions = new();
    private readonly Dictionary<VirtualCrossoverAlignmentStage, Label> groupHeaders = new();
    private readonly List<(CheckBox Box, CrossoverFilterFamily Family)> familyBoxes = new();
    private AutoSetupWizardSession session = null!;
    private bool initialized;

    // Set while the dialog writes the session back into the controls, whose events must not read it as the user.
    private bool presenting;

    public VirtualCrossoverAutoSetupDialog()
    {
        InitializeComponent();
        AcceptButton = buttonApply;
        CancelButton = buttonCancel;
        // Apply ranks asynchronously; an automatic DialogResult would close the form at the first await.
        buttonApply.DialogResult = DialogResult.None;
        buttonApply.Click += ApplyClick;
        WireOptionControls();
        // Runtime tooltip and never-parented arrows (group of one) are outside the designer's components.
        Disposed += (_, _) =>
        {
            CancelPreviewWork();
            toolTip.Dispose();
            foreach (Control control in rows.Values.SelectMany(
                         row => new Control[] { row.PositionLabel, row.NameLabel,
                             row.BandLabel, row.TypeComboBox, row.Up, row.Down })
                     .Concat(groupHeaders.Values)
                     .Concat(junctions.SelectMany(JunctionControls))
                     .Where(control => control.Parent == null))
            {
                control.Dispose();
            }
        };
        toolTip.SetToolTip(
            labelPreview,
            "What Apply writes: crossovers, cut-only gains and polarity. It " +
            "assumes perfect alignment, so it is not what the panel measures.");
    }

    public IReadOnlyList<CrossoverProposal>? Result { get; private set; }

    /// <summary>Init indices in crossed order; null when <c>Reorder the channel blocks</c> is cleared (the panel decides what reordering means).</summary>
    public IReadOnlyList<int>? ChainOrder { get; private set; }

    public void Init(
        double sampleRateHz,
        double processorSampleRateHz,
        IReadOnlyList<AutoSetupWizardChannel> channels)
    {
        session = new AutoSetupWizardSession(sampleRateHz, processorSampleRateHz, channels);
        maxCrossover.Maximum = session.CrossoverRange.Maximum;
        minCrossover.Maximum = session.CrossoverRange.Maximum;
        foreach ((CheckBox box, CrossoverFilterFamily family) in familyBoxes)
        {
            session.SetFamily(family, box.Checked);
        }

        session.MinCrossoverHz = minCrossover.Value;
        session.MaxCrossoverHz = maxCrossover.Value;
        session.IndependentSlopes = independentSlopes.Checked;
        session.ReorderBlocks = reorderBlocks.Checked;
        session.SubElevationDb = subElevation.Value;

        rows.Clear();
        groupHeaders.Clear();
        foreach (AutoSetupWizardRow row in session.Rows)
        {
            rows.Add(row, BuildRow(row));
        }

        PopulateTable();
        RebuildJunctions();
        subElevation.Enabled = session.SubElevationApplies;
        UiStyle.SetTextEnabledLook(labelSubElevation, session.SubElevationApplies);
        UiStyle.SetTextEnabledLook(labelSubElevationUnit, session.SubElevationApplies);

        initialized = true;
        SchedulePreview();
        if (IsHandleCreated)
        {
            LayoutBelowChannelTable();
        }
    }

    private void WireOptionControls()
    {
        familyBoxes.Add((checkButterworth, CrossoverFilterFamily.Butterworth));
        familyBoxes.Add((checkLinkwitzRiley, CrossoverFilterFamily.LinkwitzRiley));
        familyBoxes.Add((checkBessel, CrossoverFilterFamily.Bessel));
        foreach ((CheckBox box, CrossoverFilterFamily family) in familyBoxes)
        {
            box.CheckedChanged += (_, _) => OptionChanged(() => session.SetFamily(family, box.Checked));
        }

        minCrossover.ValueChanged += (_, _) => OptionChanged(() => session.MinCrossoverHz = minCrossover.Value);
        maxCrossover.ValueChanged += (_, _) => OptionChanged(() => session.MaxCrossoverHz = maxCrossover.Value);
        // Two jobs, and the second is the one the old name described: it is the protective filter at the two
        // ends of the chain, which no junction row can reach because those ends are not junctions — and for a
        // group holding one driver it is the only crossover there is — AND it still bounds every junction
        // window on top of whatever that junction resolved for itself.
        toolTip.SetToolTip(
            minCrossover,
            "Protective high-pass under the lowest driver, and a floor under " +
            "every junction window. To narrow one junction, use its own row. A " +
            "group holding one driver gets its whole crossover from here.");
        toolTip.SetToolTip(
            maxCrossover,
            "Protective low-pass over the highest driver, and a ceiling over " +
            "every junction window. At 20 kHz it adds nothing.");
        independentSlopes.CheckedChanged +=
            (_, _) => OptionChanged(() => session.IndependentSlopes = independentSlopes.Checked);
        // Read only by Apply, so it changes no preview.
        reorderBlocks.CheckedChanged += (_, _) =>
        {
            if (initialized)
            {
                session.ReorderBlocks = reorderBlocks.Checked;
            }
        };
        subElevation.ValueChanged += (_, _) => OptionChanged(() => session.SubElevationDb = subElevation.Value);
        toolTip.SetToolTip(
            independentSlopes,
            "Lets a junction's two sides take different slopes. Off ties each " +
            "driver's own two shoulders to one slope.");
        toolTip.SetToolTip(
            reorderBlocks,
            "Put the panel's blocks in this dialog's order: the groups one\r\n" +
            "after another, each from the lowest driver up. Moved blocks are\r\n" +
            "re-lettered and recoloured; a sheet exported before this names\r\n" +
            "the OLD letters.");
        toolTip.SetToolTip(
            subElevation,
            "How far the lowest driver sits above the levelled\r\n" +
            "midrange/tweeter. Starts at, and is capped by, the measured\r\n" +
            "elevation; lower it to flatten the bottom.");
    }

    // Before Init the controls hold the designer's values, which Init reads into the session.
    private void OptionChanged(Action write)
    {
        if (!initialized || presenting)
        {
            return;
        }

        write();
        SchedulePreview();
    }
}
