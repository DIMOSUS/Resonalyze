using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Crossover wizard; each group is fitted as its own chain, then levelled onto the front stage.
/// See docs/tech/crossover-auto-setup.md#groups-outside-the-chain.</summary>
internal sealed partial class VirtualCrossoverAutoSetupDialog : Form
{
    private sealed record ChannelRow(
        AutoSetupWizardRow Row,
        Label PositionLabel,
        Label NameLabel,
        Label BandLabel,
        ThemedComboBox TypeComboBox,
        Button Up,
        Button Down);

    private sealed record GroupPlan(
        VirtualCrossoverAlignmentStage Group,
        IReadOnlyList<int> InitIndices,
        IReadOnlyList<AutoSetupSource> Sources,
        IReadOnlyList<Complex[]>? ImpulseResponses,
        bool IsPrimary);

    private sealed record JunctionRow(
        AutoSetupWizardJunction Junction,
        Label NameLabel,
        ThemedNumericUpDown MinHz,
        Label RangeDash,
        ThemedNumericUpDown MaxHz,
        ThemedComboBox MinSlope,
        Label SlopeDash,
        ThemedComboBox MaxSlope,
        CheckBox Split,
        Label Verdict,
        Label Notes)
    {
        public bool Suppressed { get; set; }
    }

    private sealed record GroupFit(
        GroupPlan Plan,
        IReadOnlyList<CrossoverProposal> Proposals);

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

    private bool optionsPositioned;

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

    private ChannelRow BuildRow(AutoSetupWizardRow source)
    {
        AutoSetupWizardChannel channel = source.Source;
        var positionLabel = new Label
        {
            Anchor = AnchorStyles.Left,
            AutoSize = true,
            ForeColor = UiPalette.TextDisabled,
            Margin = new Padding(0, 4, 8, 4)
        };
        var nameLabel = new Label
        {
            Anchor = AnchorStyles.Left,
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204),
            ForeColor = channel.Accent,
            Margin = new Padding(0, 4, 24, 4),
            Text = channel.Name
        };
        var bandLabel = new Label
        {
            Anchor = AnchorStyles.Left,
            AutoSize = true,
            ForeColor = UiPalette.TextSecondary,
            Margin = new Padding(0, 4, 24, 4),
            Text = $"{FormatHz(channel.Band.LowHz)} – {FormatHz(channel.Band.HighHz)}"
        };
        toolTip.SetToolTip(bandLabel, BandTooltip(channel));
        var typeComboBox = new ThemedComboBox
        {
            Anchor = AnchorStyles.Left,
            BackColor = UiPalette.ControlSurface,
            ForeColor = UiPalette.TextPrimary,
            Margin = new Padding(0, 1, 0, 1),
            TabIndex = source.InitIndex
        };
        typeComboBox.Items.AddRange(
        [
            DriverType.Subwoofer,
            DriverType.Woofer,
            DriverType.Midbass,
            DriverType.Midrange,
            DriverType.Tweeter
        ]);
        typeComboBox.SelectedItem = source.Type;
        // A type change moves the class bounds, so the junction rows are re-resolved, not just re-scored.
        typeComboBox.SelectedIndexChanged += (_, _) =>
        {
            source.Type = typeComboBox.SelectedItem is DriverType type ? type : DriverType.Woofer;
            SchedulePreview();
        };

        var row = new ChannelRow(
            source, positionLabel, nameLabel, bandLabel, typeComboBox,
            BuildArrow("▲"), BuildArrow("▼"));
        row.Up.Click += (_, _) => MoveInChain(row, -1);
        row.Down.Click += (_, _) => MoveInChain(row, +1);
        foreach (Button arrow in new[] { row.Up, row.Down })
        {
            toolTip.SetToolTip(
                arrow,
                "Where this driver sits in its group's chain: the one above hands\r\n" +
                "over to the one below. The order starts from what each channel\r\n" +
                "measured (narrowed by any crossover corner it already carries) —\r\n" +
                "move it when two drivers are too alike for that to decide, as a\r\n" +
                "pair of subwoofers measured full-range will be.");
        }

        return row;
    }

    // The band shown is measured, but the chain is ordered by the effective band after the channel's own corners.
    private static string BandTooltip(AutoSetupWizardChannel channel)
    {
        const string measured = "The usable band read from the raw response — what\r\n" +
            "bounds where this driver may be crossed.";
        (double low, double high) = VirtualCrossoverAutoSetupOrder.EffectiveBand(
            channel.Band, channel.HighPassHz, channel.LowPassHz);
        bool narrowed = low > channel.Band.LowHz || high < channel.Band.HighHz;
        return narrowed
            ? measured + "\r\n\r\nIts crossover already narrows it to " +
                $"{FormatHz(low)} – {FormatHz(high)},\r\nwhich is what puts it here in the chain."
            : measured;
    }

    private Button BuildArrow(string glyph) =>
        new ReleaseClickButton
        {
            Anchor = AnchorStyles.Left,
            BackColor = UiPalette.InputSurface,
            FlatStyle = FlatStyle.Popup,
            ForeColor = UiPalette.TextPrimary,
            Margin = new Padding(2, 1, 0, 1),
            Text = glyph,
            UseVisualStyleBackColor = false
        };

    private List<ChannelRow> MembersOf(VirtualCrossoverAlignmentStage group) =>
        session.MembersOf(group).Select(row => rows[row]).ToList();

    /// <summary>Slopes the window fields offer. 6 dB/oct protects nothing and is excluded from the search anyway.</summary>
    private static readonly int[] SelectableSlopes = CrossoverFilter
        .SupportedSlopes(CrossoverFilterFamily.Butterworth)
        .Where(slope => slope >= 12)
        .ToArray();

    // One row per adjacent pair of every group, rebuilt whenever the chain order changes.
    private void RebuildJunctions()
    {
        foreach (JunctionRow junction in junctions)
        {
            foreach (Control control in JunctionControls(junction))
            {
                control.Dispose();
            }
        }

        junctions.Clear();
        foreach (AutoSetupWizardJunction junction in session.Junctions())
        {
            junctions.Add(BuildJunctionRow(junction));
        }

        PopulateJunctionTable();
        RefreshJunctionWindows();
    }

    private static IEnumerable<Control> JunctionControls(JunctionRow junction) =>
    [
        junction.NameLabel, junction.MinHz, junction.RangeDash, junction.MaxHz,
        junction.MinSlope, junction.SlopeDash, junction.MaxSlope, junction.Split,
        junction.Verdict, junction.Notes
    ];

    private JunctionRow BuildJunctionRow(AutoSetupWizardJunction junction)
    {
        ThemedNumericUpDown Frequency() => new()
        {
            Anchor = AnchorStyles.Left,
            BackColor = UiPalette.InputSurface,
            DecimalPlaces = 0,
            ForeColor = UiPalette.TextPrimary,
            Increment = 10m,
            LogarithmicFrequencyStep = true,
            Margin = new Padding(0, 1, 2, 1),
            Maximum = 20_000m,
            Minimum = 20m,
            MinimumSize = new Size(36, 19)
        };

        ThemedComboBox Slope()
        {
            var box = new ThemedComboBox
            {
                Anchor = AnchorStyles.Left,
                BackColor = UiPalette.ControlSurface,
                ForeColor = UiPalette.TextPrimary,
                Margin = new Padding(0, 1, 2, 1)
            };
            box.Items.AddRange(SelectableSlopes.Cast<object>().ToArray());
            return box;
        }

        Label Dash() => new()
        {
            Anchor = AnchorStyles.Left,
            AutoSize = true,
            ForeColor = UiPalette.TextSecondary,
            Margin = new Padding(0, 4, 2, 4),
            Text = "–"
        };

        var row = new JunctionRow(
            junction,
            new Label
            {
                Anchor = AnchorStyles.Left,
                AutoSize = true,
                ForeColor = UiPalette.TextDefault,
                Margin = new Padding(0, 4, 16, 4),
                Text = $"{junction.Lower.Name.Split(' ')[0]} → {junction.Upper.Name.Split(' ')[0]}"
            },
            Frequency(),
            Dash(),
            Frequency(),
            Slope(),
            Dash(),
            Slope(),
            new ReleaseClickCheckBox
            {
                Anchor = AnchorStyles.Left,
                AutoSize = true,
                ForeColor = UiPalette.TextPrimary,
                Margin = new Padding(12, 2, 0, 2),
                Text = "Split"
            },
            new Label
            {
                Anchor = AnchorStyles.Left,
                AutoSize = true,
                ForeColor = UiPalette.TextSecondary,
                Margin = new Padding(0, 0, 0, 2),
                Text = "—"
            },
            new Label
            {
                Anchor = AnchorStyles.Left,
                AutoSize = true,
                ForeColor = UiPalette.AccentMark,
                Margin = new Padding(0, 0, 0, 6),
                Visible = false
            });

        // Written as sentences: the tooltip wraps its own text at 64 characters, so a hand-broken line longer
        // than that gets broken a second time and comes out ragged.
        toolTip.SetToolTip(
            row.MinHz,
            "Lowest crossover the search may pick here. A value the drivers " +
            "cannot take is raised, and the row says why.");
        toolTip.SetToolTip(
            row.MaxHz,
            "Highest crossover the search may pick here. A value the drivers " +
            "cannot take is lowered, and the row says why.");
        toolTip.SetToolTip(
            row.MinSlope,
            "Gentlest slope the search may use here. 24 dB/oct always stays " +
            "inside the window.");
        toolTip.SetToolTip(
            row.MaxSlope,
            "Steepest slope the search may use here. The group-delay budget can " +
            "still rule out a slope this allows.");
        toolTip.SetToolTip(
            row.Split,
            "Lets the low-pass and the high-pass sit at different frequencies. " +
            "Off unless you ask for it.");

        // Before the handlers: showing what the user set is not an edit.
        AutoSetupJunctionEdits kept = session.EditsOf(junction);
        if (kept.MinHz is { } minHz)
        {
            row.MinHz.Value = Math.Clamp(minHz, row.MinHz.Minimum, row.MinHz.Maximum);
        }

        if (kept.MaxHz is { } maxHz)
        {
            row.MaxHz.Value = Math.Clamp(maxHz, row.MaxHz.Minimum, row.MaxHz.Maximum);
        }

        if (kept.MinSlope is { } minSlope)
        {
            row.MinSlope.SelectedItem = minSlope;
        }

        if (kept.MaxSlope is { } maxSlope)
        {
            row.MaxSlope.SelectedItem = maxSlope;
        }

        row.Split.Checked = kept.Split;

        row.MinHz.ValueChanged += (_, _) => JunctionEdited(row, edits => edits with { MinHz = row.MinHz.Value });
        row.MaxHz.ValueChanged += (_, _) => JunctionEdited(row, edits => edits with { MaxHz = row.MaxHz.Value });
        row.MinSlope.SelectedIndexChanged +=
            (_, _) => JunctionEdited(row, edits => edits with { MinSlope = (int)row.MinSlope.SelectedItem! });
        row.MaxSlope.SelectedIndexChanged +=
            (_, _) => JunctionEdited(row, edits => edits with { MaxSlope = (int)row.MaxSlope.SelectedItem! });
        row.Split.CheckedChanged += (_, _) => JunctionEdited(row, edits => edits with { Split = row.Split.Checked });
        return row;
    }

    // Writing the wizard's own answer back into a field must not read as the user setting it.
    private void JunctionEdited(JunctionRow row, Func<AutoSetupJunctionEdits, AutoSetupJunctionEdits> edit)
    {
        if (row.Suppressed)
        {
            return;
        }

        session.Edit(row.Junction, edit(session.EditsOf(row.Junction)));
        SchedulePreview();
    }

    private void PopulateJunctionTable()
    {
        tableJunctions.SuspendLayout();
        tableJunctions.Controls.Clear();
        tableJunctions.RowStyles.Clear();
        int line = 0;
        foreach (JunctionRow junction in junctions)
        {
            tableJunctions.Controls.Add(junction.NameLabel, 0, line);
            tableJunctions.Controls.Add(junction.MinHz, 1, line);
            tableJunctions.Controls.Add(junction.RangeDash, 2, line);
            tableJunctions.Controls.Add(junction.MaxHz, 3, line);
            tableJunctions.Controls.Add(junction.MinSlope, 4, line);
            tableJunctions.Controls.Add(junction.SlopeDash, 5, line);
            tableJunctions.Controls.Add(junction.MaxSlope, 6, line);
            tableJunctions.Controls.Add(junction.Split, 7, line);
            tableJunctions.Controls.Add(junction.Verdict, 8, line);
            line++;
            tableJunctions.Controls.Add(junction.Notes, 0, line);
            tableJunctions.SetColumnSpan(junction.Notes, 9);
            line++;
        }

        tableJunctions.RowCount = Math.Max(1, line);
        UiStyle.SetTextEnabledLook(labelJunctions, junctions.Count > 0);
        tableJunctions.ResumeLayout();
    }

    private IReadOnlyList<JunctionSearchWindow> WindowsFor(VirtualCrossoverAlignmentStage group) =>
        session.Junctions()
            .Where(junction => junction.Group == group)
            .OrderBy(junction => junction.IndexInGroup)
            .Select(junction => session.EditsOf(junction).ToWindow())
            .ToList();

    // Re-run after a reorder with the same controls, so device-unit sizing from LayoutBelowChannelTable survives.
    private void PopulateTable()
    {
        bool headers = session.GroupsInOrder().Count() > 1;
        tableChannels.SuspendLayout();
        tableChannels.Controls.Clear();
        tableChannels.RowStyles.Clear();
        int line = 0;
        foreach (VirtualCrossoverAlignmentStage group in session.GroupsInOrder())
        {
            List<ChannelRow> members = MembersOf(group);
            if (headers)
            {
                Label header = GroupHeader(group);
                header.Text = members.Count > 1
                    ? VirtualCrossoverAlignmentStages.DisplayName(group) +
                        "   —   lowest first, handing over downwards"
                    : VirtualCrossoverAlignmentStages.DisplayName(group);
                tableChannels.Controls.Add(header, 0, line);
                tableChannels.SetColumnSpan(header, 6);
                line++;
            }

            for (int i = 0; i < members.Count; i++)
            {
                ChannelRow member = members[i];
                // A group of one is not a chain: no number, no arrows.
                member.PositionLabel.Text = members.Count > 1 ? $"{i + 1}." : string.Empty;
                tableChannels.Controls.Add(member.PositionLabel, 0, line);
                tableChannels.Controls.Add(member.NameLabel, 1, line);
                tableChannels.Controls.Add(member.BandLabel, 2, line);
                tableChannels.Controls.Add(member.TypeComboBox, 3, line);
                if (members.Count > 1)
                {
                    member.Up.Enabled = i > 0;
                    member.Down.Enabled = i < members.Count - 1;
                    tableChannels.Controls.Add(member.Up, 4, line);
                    tableChannels.Controls.Add(member.Down, 5, line);
                }

                line++;
            }
        }

        tableChannels.RowCount = Math.Max(1, line);
        tableChannels.ResumeLayout(true);
    }

    private Label GroupHeader(VirtualCrossoverAlignmentStage group)
    {
        if (!groupHeaders.TryGetValue(group, out Label? header))
        {
            header = new Label
            {
                Anchor = AnchorStyles.Left,
                AutoSize = true,
                Font = new Font(
                    "Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204),
                ForeColor = UiPalette.TextDefault,
                Margin = new Padding(0, 8, 0, 2)
            };
            groupHeaders[group] = header;
        }

        return header;
    }

    private void MoveInChain(ChannelRow row, int delta)
    {
        if (!session.MoveInChain(row.Row, delta))
        {
            return;
        }

        PopulateTable();
        RebuildJunctions();
        SchedulePreview();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        LayoutBelowChannelTable();
    }

    // Runs once after scaling, so every measurement is already in device pixels.
    private void LayoutBelowChannelTable()
    {
        if (optionsPositioned)
        {
            return;
        }

        optionsPositioned = true;

        // Runtime-added controls miss the form's font autoscale; size them in device units before measuring the row.
        Size comboSize = LogicalToDeviceUnits(new Size(110, 19));
        Size arrowSize = LogicalToDeviceUnits(new Size(22, 19));
        foreach (ChannelRow row in rows.Values)
        {
            row.TypeComboBox.Size = comboSize;
            row.Up.Size = arrowSize;
            row.Down.Size = arrowSize;
        }

        Size fieldSize = LogicalToDeviceUnits(new Size(62, 19));
        Size slopeSize = LogicalToDeviceUnits(new Size(58, 19));
        foreach (JunctionRow junction in junctions)
        {
            junction.MinHz.Size = fieldSize;
            junction.MaxHz.Size = fieldSize;
            junction.MinSlope.Size = slopeSize;
            junction.MaxSlope.Size = slopeSize;
        }

        tableChannels.PerformLayout();
        int outsideMargin = LogicalToDeviceUnits(12);
        int shift = tableChannels.Bottom + outsideMargin - labelJunctions.Top;
        foreach (Control control in new Control[] { labelJunctions, tableJunctions })
        {
            control.Top += shift;
        }

        tableJunctions.PerformLayout();
        shift = tableJunctions.Bottom + outsideMargin - labelFilters.Top;
        foreach (Control control in new Control[]
                 {
                     labelFilters, checkButterworth, checkLinkwitzRiley, checkBessel,
                     labelRange, minCrossover, labelDash, maxCrossover, labelHz,
                     independentSlopes, reorderBlocks, labelSubElevation, subElevation,
                     labelSubElevationUnit, panelPreview, progressPreview
                 })
        {
            control.Top += shift;
        }

        // The AutoSize tables can exceed the designed width even at 100% DPI.
        int clientWidth = Math.Max(
            ClientSize.Width,
            Math.Max(tableChannels.Right, tableJunctions.Right) + outsideMargin);
        SizePreviewCard(clientWidth, outsideMargin);
        ClientSize = new Size(
            clientWidth,
            progressPreview.Bottom + outsideMargin + buttonApply.Height + outsideMargin);
    }

    /// <summary>Sizes the result card to its text and parks the progress bar under it. Measured as laid out
    /// (summaries wrap), floored at the structural line count so the card does not jump about between refits.</summary>
    private void SizePreviewCard(int clientWidth, int outsideMargin)
    {
        panelPreview.Width = clientWidth - panelPreview.Left - outsideMargin;
        labelPreview.Width =
            panelPreview.Width - panelPreview.Padding.Left - panelPreview.Padding.Right;
        labelPreview.Height = Math.Max(
            PreviewLineCount() * labelPreview.Font.Height,
            TextRenderer.MeasureText(
                labelPreview.Text,
                labelPreview.Font,
                new Size(labelPreview.Width, int.MaxValue),
                TextFormatFlags.WordBreak).Height);
        panelPreview.Height =
            labelPreview.Height + panelPreview.Padding.Top + panelPreview.Padding.Bottom;
        progressPreview.Top = panelPreview.Bottom + LogicalToDeviceUnits(6);
        progressPreview.Width = panelPreview.Width;
    }

    // From structure, not current text (which may be a one-line error).
    private int PreviewLineCount() =>
        session.Rows.Count + (2 * session.GroupsInOrder().Count());

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
        reorderBlocks.CheckedChanged += (_, _) => OptionChanged(() => session.ReorderBlocks = reorderBlocks.Checked);
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

    private static AutoSetupSource SourceOf(AutoSetupWizardRow row) =>
        new(row.Source.MagnitudeDb, row.Type, row.Source.Coherence, row.Source.Distortion);

    // Sub elevation applies to the primary group only; others keep their balance and are levelled as a whole.
    private CrossoverAutoSetupOptions OptionsFor(GroupPlan group) =>
        new(
            session.SelectedFamilies(),
            (double)session.MinCrossoverHz,
            (double)session.MaxCrossoverHz,
            session.IndependentSlopes,
            session.SampleRateHz,
            session.ProcessorSampleRateHz,
            group.IsPrimary && session.SubElevationInitialized ? (double)session.SubElevationDb : null,
            WindowsFor(group.Group));

    // Snapshot on the UI thread; the ranked search runs in the background.
    private List<GroupPlan> CurrentPlan(bool withImpulseResponses)
    {
        VirtualCrossoverAlignmentStage primary = session.PrimaryGroup();
        var plan = new List<GroupPlan>();
        foreach (VirtualCrossoverAlignmentStage group in session.GroupsInOrder())
        {
            List<AutoSetupWizardRow> members = session.MembersOf(group);
            bool ranked = withImpulseResponses &&
                members.Count > 1 &&
                members.All(row => row.Source.ImpulseResponse is { Length: > 0 });
            plan.Add(new GroupPlan(
                group,
                members.Select(row => row.InitIndex).ToList(),
                members.Select(SourceOf).ToList(),
                ranked ? members.Select(row => row.Source.ImpulseResponse!).ToList() : null,
                group == primary));
        }

        return plan;
    }

    // Pure, so Apply can run it off the UI thread.
    private static List<GroupFit> Fit(
        IReadOnlyList<GroupPlan> plan,
        Func<GroupPlan, CrossoverAutoSetupOptions> options,
        double sampleRateHz)
    {
        var fitted = new IReadOnlyList<CrossoverProposal>[plan.Count];
        double? reference = null;
        // Primary fitted first (others level onto it); stable sort keeps plan order for the rest.
        foreach (int index in Enumerable.Range(0, plan.Count)
                     .OrderByDescending(index => plan[index].IsPrimary))
        {
            GroupPlan group = plan[index];
            CrossoverAutoSetupOptions groupOptions = options(group);
            IReadOnlyList<CrossoverProposal> proposals = group.Sources.Count == 1
                ? [CrossoverAutoSetup.ProposeSingle(group.Sources[0], groupOptions)]
                : group.ImpulseResponses != null
                    ? CrossoverAutoSetup.ProposeRanked(
                        group.Sources, groupOptions, group.ImpulseResponses)[0].Proposals
                    : CrossoverAutoSetup.Propose(group.Sources, groupOptions);

            if (group.IsPrimary)
            {
                reference = CrossoverAutoSetup.ReferenceLevelDb(
                    group.Sources, proposals, sampleRateHz);
            }
            else if (reference is { } level)
            {
                proposals = CrossoverAutoSetup.OffsetToReferenceLevel(
                    group.Sources, proposals, sampleRateHz, level);
            }

            fitted[index] = proposals;
        }

        return plan.Select((group, index) => new GroupFit(group, fitted[index])).ToList();
    }

    private static CrossoverProposal[] InInitOrder(IReadOnlyList<GroupFit> fits, int count)
    {
        var result = new CrossoverProposal[count];
        foreach (GroupFit fit in fits)
        {
            for (int i = 0; i < fit.Plan.InitIndices.Count; i++)
            {
                result[fit.Plan.InitIndices[i]] = fit.Proposals[i];
            }
        }

        return result;
    }

    private List<GroupFit>? TryFit(bool withImpulseResponses)
    {
        if (session.SelectedFamilies().Count == 0)
        {
            return null;
        }

        try
        {
            return Fit(CurrentPlan(withImpulseResponses), OptionsFor, session.SampleRateHz);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Everything the preview needs that costs real time, computed off the UI thread.</summary>
    private sealed record PreviewComputation(
        IReadOnlyList<GroupFit> Fits,
        IReadOnlyList<GroupSummary> Summaries,
        decimal ElevationCeiling,
        decimal? ElevationValue);

    /// <summary>The summed span one group is predicted to have, and the band it was read over.</summary>
    private sealed record GroupSummary(double SpanDb, double LowHz, double HighHz);

    private int previewGeneration;
    private CancellationTokenSource? previewWork;

    // The ranked run owns Apply and the inputs from the moment it snapshots them. A preview already in flight when
    // Apply was pressed lands afterwards and would otherwise hand the button back mid-ranking.
    private bool rankingInProgress;

    // Long enough to swallow a held spinner arrow, short enough that a single click still feels immediate.
    private const int PreviewDebounceMilliseconds = 120;

    /// <summary>Called by every control that changes the proposal. Junction windows refresh synchronously — a couple
    /// of milliseconds, and a row must never show a window the search is not using — while the fit itself is hundreds
    /// of milliseconds on a four-way, so it runs off the UI thread with the progress bar up.</summary>
    private void SchedulePreview()
    {
        if (!initialized || presenting || rankingInProgress)
        {
            return;
        }

        // Before the early exits: a moved row must not keep its old colour.
        MarkChainOrder();
        RefreshJunctionWindows();
        if (session.SelectedFamilies().Count == 0)
        {
            CancelPreviewWork();
            SetPreviewBusy(false);
            buttonApply.Enabled = false;
            labelPreview.Text = "Enable at least one filter family.";
            return;
        }

        CancelPreviewWork();
        previewWork = new CancellationTokenSource();
        PendingPreview = RunPreviewAsync(previewWork.Token);
    }

    /// <summary>The preview run in flight. The dialog itself never waits on it — the point of the rework is that it
    /// does not — but a test has to know when the late half of the row has landed.</summary>
    internal Task? PendingPreview { get; private set; }

    private void CancelPreviewWork()
    {
        previewWork?.Cancel();
        previewWork?.Dispose();
        previewWork = null;
    }

    private async Task RunPreviewAsync(CancellationToken token)
    {
        int generation = ++previewGeneration;
        SetPreviewBusy(true);
        buttonApply.Enabled = false;
        try
        {
            await Task.Delay(PreviewDebounceMilliseconds, token);
            List<GroupPlan> plan = CurrentPlan(withImpulseResponses: false);
            Dictionary<VirtualCrossoverAlignmentStage, CrossoverAutoSetupOptions> snapshot =
                plan.ToDictionary(group => group.Group, OptionsFor);
            double rateHz = session.SampleRateHz;
            double processorHz = session.ProcessorSampleRateHz;
            bool elevationSet = session.SubElevationInitialized;
            decimal elevation = session.SubElevationDb;
            PreviewComputation? computed = await Task.Run(
                () => ComputePreview(
                    plan, snapshot, rateHz, processorHz, elevationSet, elevation),
                token);
            if (token.IsCancellationRequested || generation != previewGeneration || IsDisposed)
            {
                return;
            }

            ApplyPreview(computed);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer change; the newer run owns the UI from here.
        }
        finally
        {
            if (!IsDisposed && generation == previewGeneration)
            {
                SetPreviewBusy(false);
            }
        }
    }

    /// <summary>Pure: touches no control, so it is safe on a worker. A run that moves the elevation ceiling fits a
    /// second time here rather than bouncing back through the UI to do it.</summary>
    private static PreviewComputation? ComputePreview(
        IReadOnlyList<GroupPlan> plan,
        IReadOnlyDictionary<VirtualCrossoverAlignmentStage, CrossoverAutoSetupOptions> options,
        double sampleRateHz,
        double processorSampleRateHz,
        bool elevationInitialized,
        decimal elevation)
    {
        List<GroupFit>? fits = TryFit(plan, options, sampleRateHz);
        if (fits == null)
        {
            return null;
        }

        decimal ceiling = 0;
        decimal? value = null;
        GroupFit? primary = fits.FirstOrDefault(fit => fit.Plan.IsPrimary);
        if (primary != null && primary.Plan.Sources.Count > 1)
        {
            ceiling = (decimal)Math.Max(0, Math.Round(
                CrossoverAutoSetup.MeasuredSubElevationDb(
                    primary.Plan.Sources, primary.Proposals, sampleRateHz),
                1));
            if (!elevationInitialized && ceiling != elevation)
            {
                value = ceiling;
                // The first fit used a default elevation the user never sees; refit to the one about to be shown.
                fits = TryFit(
                    plan,
                    options.ToDictionary(
                        entry => entry.Key,
                        entry => entry.Key == primary.Plan.Group
                            ? entry.Value with { SubElevationDb = (double)ceiling }
                            : entry.Value),
                    sampleRateHz) ?? fits;
            }
        }

        var summaries = new List<GroupSummary>(fits.Count);
        foreach (GroupFit fit in fits)
        {
            summaries.Add(Summarize(fit, sampleRateHz, processorSampleRateHz));
        }

        return new PreviewComputation(fits, summaries, ceiling, value);
    }

    private static List<GroupFit>? TryFit(
        IReadOnlyList<GroupPlan> plan,
        IReadOnlyDictionary<VirtualCrossoverAlignmentStage, CrossoverAutoSetupOptions> options,
        double sampleRateHz)
    {
        try
        {
            return Fit(plan, group => options[group.Group], sampleRateHz);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static GroupSummary Summarize(
        GroupFit fit,
        double sampleRateHz,
        double processorSampleRateHz)
    {
        IReadOnlyList<AutoSetupSource> sources = fit.Plan.Sources;
        if (sources.Count < 2)
        {
            return new GroupSummary(0, 0, 0);
        }

        DriverBandEstimate low = CrossoverAutoSetup.EstimateBand(
            sources[0].MagnitudeDb, sources[0].Coherence);
        DriverBandEstimate high = CrossoverAutoSetup.EstimateBand(
            sources[^1].MagnitudeDb, sources[^1].Coherence);
        double trim = Math.Pow(2.0, 0.5);
        var window = CrossoverAutoSetup
            .SummedResponseDb(sources, fit.Proposals, sampleRateHz, processorSampleRateHz)
            .Where(point => point.X >= low.LowHz * trim && point.X <= high.HighHz / trim)
            .Select(point => point.Y)
            .ToList();
        return new GroupSummary(
            window.Count > 0 ? window.Max() - window.Min() : 0, low.LowHz, high.HighHz);
    }

    private void ApplyPreview(PreviewComputation? computed)
    {
        buttonApply.Enabled = computed != null && !rankingInProgress;
        if (computed == null)
        {
            labelPreview.Text = "No proposal fits these channels and settings.";
            return;
        }

        session.TakeElevation(computed.ElevationCeiling, computed.ElevationValue);
        presenting = true;
        try
        {
            subElevation.Maximum = session.ElevationRange.Maximum;
            subElevation.Value = session.SubElevationDb;
        }
        finally
        {
            presenting = false;
        }

        labelPreview.Text = string.Join(
            Environment.NewLine, PreviewLines(computed.Fits, computed.Summaries));
        UpdateJunctionVerdicts(computed.Fits);
        GrowToFitContents();
    }

    private void SetPreviewBusy(bool busy)
    {
        progressPreview.Visible = busy;
        UiStyle.SetTextEnabledLook(labelPreview, !busy);
    }

    /// <summary>Writes the window the search will run on back into every row, with the reason for any bound that
    /// moved. Cheap enough to run on the UI thread on every keystroke, and it must: a row showing a window the search
    /// is not using is worse than a row showing nothing. A field the user has touched keeps its own value.</summary>
    private void RefreshJunctionWindows()
    {
        if (!initialized)
        {
            return;
        }

        foreach (VirtualCrossoverAlignmentStage group in session.GroupsInOrder())
        {
            List<AutoSetupWizardRow> members = session.MembersOf(group);
            if (members.Count < 2)
            {
                continue;
            }

            var sources = members.Select(SourceOf).ToList();
            CrossoverAutoSetupOptions options = OptionsFor(new GroupPlan(
                group, [], sources, null, group == session.PrimaryGroup()));
            List<JunctionRow> rowsInGroup = junctions
                .Where(junction => junction.Junction.Group == group)
                .OrderBy(junction => junction.Junction.IndexInGroup)
                .ToList();
            for (int j = 0; j < rowsInGroup.Count; j++)
            {
                JunctionRow row = rowsInGroup[j];
                JunctionWindowResolution window;
                try
                {
                    window = CrossoverAutoSetup.ResolveJunctionWindow(sources, j, options);
                }
                catch (ArgumentException)
                {
                    continue;
                }

                AutoSetupJunctionEdits edits = session.EditsOf(row.Junction);
                row.Suppressed = true;
                try
                {
                    if (edits.MinHz == null)
                    {
                        row.MinHz.Value = Clamp(row.MinHz, window.LowHz);
                    }

                    if (edits.MaxHz == null)
                    {
                        row.MaxHz.Value = Clamp(row.MaxHz, window.HighHz);
                    }

                    if (edits.MinSlope == null)
                    {
                        row.MinSlope.SelectedItem = NearestSlope(window.MinSlopeDbPerOctave);
                    }

                    if (edits.MaxSlope == null)
                    {
                        row.MaxSlope.SelectedItem = NearestSlope(window.MaxSlopeDbPerOctave);
                    }
                }
                finally
                {
                    row.Suppressed = false;
                }

                // The fact goes beside the row; the reasoning goes in the tooltip, where it is read only by
                // someone who wants it.
                row.Notes.Text = string.Join(
                    "   ·   ", window.Notes.Select(note => note.Summary));
                row.Notes.Visible = window.Notes.Count > 0;
                toolTip.SetToolTip(
                    row.Notes,
                    string.Join(
                        Environment.NewLine + Environment.NewLine,
                        window.Notes.Select(note => note.Detail)));
            }
        }
    }

    /// <summary>The one part of a row that needs the fit, so the one part that arrives late.</summary>
    private void UpdateJunctionVerdicts(IReadOnlyList<GroupFit> fits)
    {
        foreach (GroupFit fit in fits)
        {
            List<JunctionRow> rowsInGroup = junctions
                .Where(junction => junction.Junction.Group == fit.Plan.Group)
                .OrderBy(junction => junction.Junction.IndexInGroup)
                .ToList();
            for (int j = 0; j < rowsInGroup.Count && j + 1 < fit.Proposals.Count; j++)
            {
                rowsInGroup[j].Verdict.Text =
                    DescribeJunction(fit.Proposals[j], fit.Proposals[j + 1]);
            }
        }
    }

    /// <summary>The verdict column and the amber notes arrive after the one-shot layout pass has sized the window,
    /// and both are AutoSize labels, so the dialog has to be allowed to grow around them. It only ever grows: a
    /// window that shrank back on every refit would twitch.</summary>
    private void GrowToFitContents()
    {
        if (!optionsPositioned)
        {
            return;
        }

        int margin = LogicalToDeviceUnits(12);
        tableJunctions.PerformLayout();
        int width = Math.Max(
            ClientSize.Width,
            Math.Max(tableChannels.Right, tableJunctions.Right) + margin);
        SizePreviewCard(width, margin);
        int height = progressPreview.Bottom + margin + buttonApply.Height + margin;
        if (width > ClientSize.Width || height > ClientSize.Height)
        {
            ClientSize = new Size(width, Math.Max(height, ClientSize.Height));
        }
    }

    private static decimal Clamp(ThemedNumericUpDown field, double value) =>
        Math.Clamp((decimal)Math.Round(value), field.Minimum, field.Maximum);

    private static int NearestSlope(int slopeDbPerOctave) =>
        SelectableSlopes.MinBy(slope => Math.Abs(slope - slopeDbPerOctave));

    /// <summary>What the junction ended up as. Split corners print both, so the row shows the split rather than
    /// hiding it behind one number.</summary>
    private static string DescribeJunction(CrossoverProposal lower, CrossoverProposal upper)
    {
        if (lower.LowPassEdge is not { } lowPass || upper.HighPassEdge is not { } highPass)
        {
            return "—";
        }

        string family = lowPass.Family == highPass.Family
            ? FamilyName(lowPass.Family)
            : $"{FamilyName(lowPass.Family)}/{FamilyName(highPass.Family)}";
        string corners = Math.Abs(lowPass.FrequencyHz - highPass.FrequencyHz) < 0.5
            ? FormatHz(lowPass.FrequencyHz)
            : $"{FormatHz(lowPass.FrequencyHz)} ↓ / {FormatHz(highPass.FrequencyHz)} ↑";
        string polarity = upper.InvertPolarity == lower.InvertPolarity ? string.Empty : ", inverted";
        return $"{family} {corners} · " +
            $"{lowPass.SlopeDbPerOctave}/{highPass.SlopeDbPerOctave} dB/oct{polarity}";
    }

    private static string FamilyName(CrossoverFilterFamily family) => family switch
    {
        CrossoverFilterFamily.LinkwitzRiley => "LR",
        CrossoverFilterFamily.Butterworth => "BW",
        CrossoverFilterFamily.Bessel => "Bessel",
        _ => family.ToString()
    };

    private IEnumerable<string> PreviewLines(
        IReadOnlyList<GroupFit> fits,
        IReadOnlyList<GroupSummary> summaries)
    {
        bool headers = fits.Count > 1;
        VirtualCrossoverAlignmentStage primary =
            fits.FirstOrDefault(fit => fit.Plan.IsPrimary)?.Plan.Group
            ?? VirtualCrossoverAlignmentStage.FrontChain;
        string anchor = primary == VirtualCrossoverAlignmentStage.FrontChain
            ? "front stage"
            : LowerFirst(VirtualCrossoverAlignmentStages.DisplayName(primary));
        for (int g = 0; g < fits.Count; g++)
        {
            GroupFit fit = fits[g];
            if (headers)
            {
                yield return VirtualCrossoverAlignmentStages.DisplayName(fit.Plan.Group) + ":";
            }

            for (int i = 0; i < fit.Plan.InitIndices.Count; i++)
            {
                AutoSetupWizardRow row = session.Rows.First(
                    candidate => candidate.InitIndex == fit.Plan.InitIndices[i]);
                yield return FormatProposal(row, fit.Proposals[i], headers);
            }

            yield return FormatSummary(fit, summaries[g], headers, anchor);
        }
    }

    private static string LowerFirst(string text) =>
        text.Length == 0 ? text : char.ToLowerInvariant(text[0]) + text[1..];

    // Target-curve gains make the sum an intentional downslope, so report its span, not a defect.
    private string FormatSummary(
        GroupFit fit,
        GroupSummary summary,
        bool indent,
        string anchor)
    {
        string prefix = indent ? "   " : string.Empty;
        string levelled = fit.Plan.IsPrimary
            ? string.Empty
            : $"  ·  levelled to the {anchor}";
        if (fit.Plan.Sources.Count == 1)
        {
            return prefix + (fit.Plan.IsPrimary
                ? "One driver, so nothing to cross: a protective high-pass only."
                : $"Protective high-pass, levelled to the {anchor} — balance by ear.");
        }

        // The span comes from the worker with the fit: reading it here would mean a second summed response on the
        // UI thread, which is the bulk of what the preview costs.
        string elevation = fit.Plan.IsPrimary && session.SubElevationInitialized
            ? $"  ·  bass +{(double)session.SubElevationDb:0.0} dB over mid/treble"
            : string.Empty;
        return $"{prefix}Predicted sum spans {summary.SpanDb:0.0} dB over " +
            $"{FormatHz(summary.LowHz)}–{FormatHz(summary.HighHz)}{elevation}{levelled}";
    }

    private static string FormatProposal(AutoSetupWizardRow row, CrossoverProposal proposal, bool indent)
    {
        var parts = new List<string>();
        if (proposal.HighPassEdge is { } highPass)
        {
            parts.Add($"HP {FormatHz(highPass.FrequencyHz)} {FormatFamily(highPass)}");
        }
        if (proposal.LowPassEdge is { } lowPass)
        {
            parts.Add($"LP {FormatHz(lowPass.FrequencyHz)} {FormatFamily(lowPass)}");
        }
        parts.Add($"gain {proposal.GainDb:0.0} dB");
        return $"{(indent ? "   " : string.Empty)}{row.Source.Name}:  {string.Join(",  ", parts)}";
    }

    private static string FormatFamily(CrossoverEdge edge)
    {
        string family = edge.Family switch
        {
            CrossoverFilterFamily.LinkwitzRiley => "LR",
            CrossoverFilterFamily.Butterworth => "BW",
            _ => "BE"
        };
        return $"{family}{edge.SlopeDbPerOctave}";
    }

    private List<(AutoSetupWizardRow Earlier, AutoSetupWizardRow Later, VirtualCrossoverChainOrder Verdict)>
        JudgedPairs()
    {
        var pairs =
            new List<(AutoSetupWizardRow, AutoSetupWizardRow, VirtualCrossoverChainOrder)>();
        foreach (VirtualCrossoverAlignmentStage group in session.GroupsInOrder())
        {
            List<AutoSetupWizardRow> members = session.MembersOf(group);
            for (int i = 0; i + 1 < members.Count; i++)
            {
                VirtualCrossoverChainOrder verdict = VirtualCrossoverAutoSetupOrder.Judge(
                    CenterOf(members[i]), CenterOf(members[i + 1]));
                if (verdict != VirtualCrossoverChainOrder.AsMeasured)
                {
                    pairs.Add((members[i], members[i + 1], verdict));
                }
            }
        }

        return pairs;
    }

    private static double CenterOf(AutoSetupWizardRow row) =>
        VirtualCrossoverAutoSetupOrder.CenterHz(
            row.Source.Band, row.Source.HighPassHz, row.Source.LowPassHz);

    // Amber: order undetermined; red: chain runs backwards.
    private void MarkChainOrder()
    {
        var doubtful = new Dictionary<AutoSetupWizardRow, Color>();
        foreach ((AutoSetupWizardRow earlier, AutoSetupWizardRow later, VirtualCrossoverChainOrder verdict)
                 in JudgedPairs())
        {
            Color color = verdict == VirtualCrossoverChainOrder.Reversed
                ? UiPalette.Danger
                : UiPalette.Warning;
            foreach (AutoSetupWizardRow row in new[] { earlier, later })
            {
                if (!doubtful.TryGetValue(row, out Color existing) ||
                    existing != UiPalette.Danger)
                {
                    doubtful[row] = color;
                }
            }
        }

        foreach (AutoSetupWizardRow row in session.Rows)
        {
            rows[row].BandLabel.ForeColor = doubtful.TryGetValue(row, out Color color)
                ? color
                : UiPalette.TextSecondary;
        }
    }

    // Asks rather than refuses: the user may know which sub is which.
    private bool ConfirmChainOrder()
    {
        List<(AutoSetupWizardRow Earlier, AutoSetupWizardRow Later, VirtualCrossoverChainOrder Verdict)>
            doubtful = JudgedPairs();
        if (doubtful.Count == 0)
        {
            return true;
        }

        var message = new List<string>();
        var reversed = doubtful
            .Where(pair => pair.Verdict == VirtualCrossoverChainOrder.Reversed)
            .ToList();
        if (reversed.Count > 0)
        {
            message.Add(
                "A group's chain runs from the lowest driver to the highest, and " +
                "these are the wrong way round — the second measures LOWER than " +
                "the one above it:");
            message.Add(string.Empty);
            message.AddRange(reversed.Select(pair =>
                $"    {pair.Earlier.Source.Name}  above  {pair.Later.Source.Name}"));
            message.Add(string.Empty);
        }

        var unclear = doubtful
            .Where(pair => pair.Verdict == VirtualCrossoverChainOrder.Unclear)
            .ToList();
        if (unclear.Count > 0)
        {
            message.Add(
                "These measure too much alike for their order to be read off the " +
                "measurement at all:");
            message.Add(string.Empty);
            message.AddRange(unclear.Select(pair =>
                $"    {pair.Earlier.Source.Name}  above  {pair.Later.Source.Name}"));
            message.Add(string.Empty);
        }

        message.Add(
            "The wizard will cross them in the order shown. Use the ▲▼ arrows to " +
            "change it, or set a crossover corner on one of them first — either " +
            "one says which plays lower. Continue anyway?");
        return MessageBox.Show(
            this,
            string.Join(Environment.NewLine, message),
            "Auto crossover",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning) == DialogResult.Yes;
    }

    // Frozen during ranking so the applied result matches the visible settings.
    private IEnumerable<Control> RankingInputControls()
    {
        foreach (AutoSetupWizardRow source in session.Rows)
        {
            ChannelRow row = rows[source];
            yield return row.TypeComboBox;
            yield return row.Up;
            yield return row.Down;
        }

        // The ranked run takes its options snapshot before it starts; a junction edited after that would show one
        // window while Apply wrote a proposal built from another.
        foreach (JunctionRow junction in junctions)
        {
            yield return junction.MinHz;
            yield return junction.MaxHz;
            yield return junction.MinSlope;
            yield return junction.MaxSlope;
            yield return junction.Split;
        }

        foreach ((CheckBox box, CrossoverFilterFamily _) in familyBoxes)
        {
            yield return box;
        }

        yield return minCrossover;
        yield return maxCrossover;
        yield return independentSlopes;
        yield return reorderBlocks;
        yield return subElevation;
    }

    private void SetRankingInputsEnabled(bool enabled)
    {
        foreach (Control control in RankingInputControls())
        {
            control.Enabled = enabled;
        }

        subElevation.Enabled = enabled && session.SubElevationApplies;
        if (enabled)
        {
            PopulateTable();
        }
    }

    private async void ApplyClick(object? sender, EventArgs e)
    {
        List<GroupFit>? quick = TryFit(withImpulseResponses: false);
        if (quick == null)
        {
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        if (!ConfirmChainOrder())
        {
            return;
        }

        List<GroupPlan> plan = CurrentPlan(withImpulseResponses: true);
        if (plan.All(group => group.ImpulseResponses == null))
        {
            Result = InInitOrder(quick, session.Rows.Count);
            ChainOrder = session.RequestedChainOrder();
            DialogResult = DialogResult.OK;
            return;
        }

        // Ranking takes seconds on a 4-way; the preview shows the magnitude-only proposal until it lands.
        IReadOnlyList<int>? order = session.RequestedChainOrder();
        // Snapshot per group on the UI thread: the ranked search runs off it and must not read the controls.
        Dictionary<VirtualCrossoverAlignmentStage, CrossoverAutoSetupOptions> snapshot =
            plan.ToDictionary(group => group.Group, OptionsFor);
        CrossoverAutoSetupOptions Options(GroupPlan group) => snapshot[group.Group];
        string previousPreview = labelPreview.Text;
        int count = session.Rows.Count;
        double rateHz = session.SampleRateHz;
        rankingInProgress = true;
        CancelPreviewWork();
        buttonApply.Enabled = false;
        SetRankingInputsEnabled(false);
        labelPreview.Text = "Ranking candidates against the measured responses…";
        try
        {
            List<GroupFit> ranked = await Task.Run(
                () => Fit(plan, Options, rateHz));
            if (IsDisposed)
            {
                return;
            }

            Result = InInitOrder(ranked, count);
            ChainOrder = order;
            DialogResult = DialogResult.OK;
        }
        catch (ArgumentException)
        {
            if (IsDisposed)
            {
                return;
            }

            labelPreview.Text = previousPreview;
            rankingInProgress = false;
            buttonApply.Enabled = true;
            SetRankingInputsEnabled(true);
            System.Media.SystemSounds.Beep.Play();
        }
        catch (Exception exception)
        {
            // An exception after await in async void would kill the process via the WinForms context.
            if (IsDisposed)
            {
                return;
            }

            labelPreview.Text = previousPreview;
            rankingInProgress = false;
            buttonApply.Enabled = true;
            SetRankingInputsEnabled(true);
            MessageBox.Show(
                this,
                $"Candidate ranking failed.\r\n\r\n{exception.Message}",
                "Auto crossover",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static string FormatHz(double frequencyHz) =>
        FrequencyText.Format(frequencyHz);
}
