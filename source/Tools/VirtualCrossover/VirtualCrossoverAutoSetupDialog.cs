using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// One channel offered to the crossover wizard: its measured curve, the band and
/// type read off it, whatever crossover corners it already carries (which is how
/// the user says which of two similar drivers plays lower), and the group it
/// belongs to.
/// </summary>
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

/// <summary>
/// The crossover wizard: shows each participating channel with its detected
/// usable band and driver type, lets the user confirm or override the types and
/// the order their group hands over in, and asks which filter families and
/// crossover-frequency window the optimizer may use (and whether the two sides of
/// a junction may take different slopes). The resulting proposal — crossover
/// frequencies, families, slopes and cut-only gains chosen to flatten the
/// magnitude sum — previews live. Apply hands it back to the panel; nothing is
/// written until then.
/// <para>
/// Each GROUP is fitted on its own, because only a group is a crossover chain: a
/// rear fill and a centre play the same band as the front stage from other
/// places, with no filter handing anything between them, so a single chain drawn
/// through all of them would invent junctions that do not exist. The groups are
/// then levelled onto the front stage's own reference, which is a starting point
/// for the balance rather than an answer to it.
/// </para>
/// </summary>
internal sealed partial class VirtualCrossoverAutoSetupDialog : Form
{
    private sealed record ChannelRow(
        int InitIndex,
        AutoSetupWizardChannel Source,
        Label PositionLabel,
        Label NameLabel,
        Label BandLabel,
        DarkComboBox TypeComboBox,
        Button Up,
        Button Down);

    // One group's channels in chain order, ready to be fitted off the UI thread.
    private sealed record GroupPlan(
        VirtualCrossoverAlignmentStage Group,
        IReadOnlyList<int> InitIndices,
        IReadOnlyList<AutoSetupSource> Sources,
        IReadOnlyList<Complex[]>? ImpulseResponses,
        bool IsPrimary);

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

    // Every channel in DISPLAY order: the groups in the order they are staged,
    // and inside each group the chain order the optimizer will walk.
    private readonly List<ChannelRow> rows = new();
    private readonly Dictionary<VirtualCrossoverAlignmentStage, Label> groupHeaders = new();
    private readonly List<(CheckBox Box, CrossoverFilterFamily Family)> familyBoxes = new();
    private double sampleRateHz = 48_000;
    // The rate the target processor realizes its filters at — what the optimizer
    // must design against. Independent of the measurement rate above, which only
    // bounds the analysis band.
    private double processorSampleRateHz = 48_000;
    private bool initialized;
    // The sub-elevation field is pre-filled once, from the first valid proposal's
    // measured elevation (its default and upper limit). Until then options carry a
    // null elevation so the DSP uses that measured default itself.
    private bool subElevationInitialized;
    // False when the primary group is a lone driver: there is no levelled
    // mid/tweeter reference for a bass elevation to be measured against.
    private bool subElevationApplies = true;

    public VirtualCrossoverAutoSetupDialog()
    {
        InitializeComponent();
        AcceptButton = buttonApply;
        CancelButton = buttonCancel;
        // Apply ranks candidates asynchronously; the designer's automatic
        // DialogResult would close the form at the first await instead.
        buttonApply.DialogResult = DialogResult.None;
        buttonApply.Click += ApplyClick;
        WireOptionControls();
        // The designer file owns Dispose; the manually created tooltip is not in
        // its components container, so release it here. Neither is a row control
        // that never reached the table — the arrows of a group of one, which have
        // no order to change and so are never parented by anything that would
        // dispose them.
        Disposed += (_, _) =>
        {
            toolTip.Dispose();
            foreach (Control control in rows.SelectMany(
                         row => new Control[] { row.PositionLabel, row.NameLabel,
                             row.BandLabel, row.TypeComboBox, row.Up, row.Down })
                     .Concat(groupHeaders.Values)
                     .Where(control => control.Parent == null))
            {
                control.Dispose();
            }
        };
        toolTip.SetToolTip(
            labelPreview,
            "The proposal that Apply writes into the channels: crossover\r\n" +
            "frequencies, families and slopes chosen to flatten the summed\r\n" +
            "magnitude response, plus cut-only gains that level the channels.");
    }

    /// <summary>The proposal computed on Apply, in the same order as the Init channels.</summary>
    public IReadOnlyList<CrossoverProposal>? Result { get; private set; }

    /// <summary>
    /// The Init indices of the channels in the order the wizard crossed them —
    /// the groups in the order they are staged, and inside each the chain the
    /// optimizer walked. Null when the user cleared <c>Reorder the channel
    /// blocks</c>, which is the whole of the request: what reordering the blocks
    /// MEANS is the panel's to decide.
    /// </summary>
    public IReadOnlyList<int>? ChainOrder { get; private set; }

    private bool optionsPositioned;

    /// <summary>
    /// Seeds one row per participating channel, grouped and ordered. Both rates
    /// are needed: the measurement's bounds the analysis band, and the processor's
    /// is the one the optimizer evaluates the exact digital biquad cascades at —
    /// the cascades the DSP will actually run.
    /// </summary>
    public void Init(
        double sampleRateHz,
        double processorSampleRateHz,
        IReadOnlyList<AutoSetupWizardChannel> channels)
    {
        this.sampleRateHz = sampleRateHz;
        this.processorSampleRateHz = processorSampleRateHz;
        // Matches the optimizer's Nyquist ceiling; at 44.1 kHz this keeps the full
        // 20 kHz reachable instead of clamping to ~19.8 kHz.
        double ceiling = Math.Min(20_000, sampleRateHz * 0.49);
        maxCrossover.Maximum = (decimal)Math.Round(ceiling);
        minCrossover.Maximum = maxCrossover.Maximum;
        if ((double)maxCrossover.Value > ceiling)
        {
            maxCrossover.Value = maxCrossover.Maximum;
        }

        rows.Clear();
        groupHeaders.Clear();
        foreach (VirtualCrossoverAlignmentStage group in VirtualCrossoverAlignmentStages.InOrder)
        {
            IEnumerable<(AutoSetupWizardChannel Channel, int Index)> members = channels
                .Select((channel, index) => (channel, index))
                .Where(item => item.channel.Group == group)
                // The chain order the optimizer walks, seeded from what each
                // channel measures once its own corners are taken into account.
                // The arrows override it where the measurement cannot decide.
                .OrderBy(item => VirtualCrossoverAutoSetupOrder.CenterHz(
                    item.channel.Band, item.channel.HighPassHz, item.channel.LowPassHz));
            foreach ((AutoSetupWizardChannel channel, int index) in members)
            {
                rows.Add(BuildRow(index, channel));
            }
        }

        PopulateTable();
        // Nothing is elevated over a flat top the primary group has not got: with
        // one driver there is no mid/tweeter reference to lift the bass above.
        subElevationApplies = MembersOf(PrimaryGroup()).Count > 1;
        subElevation.Enabled = subElevationApplies;
        UiStyle.SetTextEnabledLook(labelSubElevation, subElevationApplies);
        UiStyle.SetTextEnabledLook(labelSubElevationUnit, subElevationApplies);

        initialized = true;
        UpdatePreview();
        if (IsHandleCreated)
        {
            LayoutBelowChannelTable();
        }
    }

    private ChannelRow BuildRow(int initIndex, AutoSetupWizardChannel channel)
    {
        // The channel's place in its group's chain, filled in by PopulateTable.
        // A column of 1, 2, 3 reading down is what makes it read as a sequence at
        // a glance — the arrows only say a row can move, not what the order means.
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
            ForeColor = UiPalette.TextSecondarySoft,
            Margin = new Padding(0, 4, 24, 4),
            Text = $"{FormatHz(channel.Band.LowHz)} – {FormatHz(channel.Band.HighHz)}"
        };
        toolTip.SetToolTip(bandLabel, BandTooltip(channel));
        var typeComboBox = new DarkComboBox
        {
            Anchor = AnchorStyles.Left,
            BackColor = UiPalette.ControlSurface,
            ForeColor = UiPalette.TextPrimary,
            Margin = new Padding(0, 1, 0, 1),
            TabIndex = initIndex
        };
        typeComboBox.Items.AddRange(
        [
            DriverType.Subwoofer,
            DriverType.Woofer,
            DriverType.Midbass,
            DriverType.Midrange,
            DriverType.Tweeter
        ]);
        typeComboBox.SelectedItem = channel.Band.SuggestedType;
        typeComboBox.SelectedIndexChanged += (_, _) => UpdatePreview();

        var row = new ChannelRow(
            initIndex, channel, positionLabel, nameLabel, bandLabel, typeComboBox,
            BuildArrow("▲"), BuildArrow("▼"));
        row.Up.Click += (_, _) => MoveInChain(row, -1);
        row.Down.Click += (_, _) => MoveInChain(row, +1);
        foreach (Button arrow in new[] { row.Up, row.Down })
        {
            toolTip.SetToolTip(
                arrow,
                "Where this driver sits in its group's chain: the one above hands\r\n" +
                "over to the one below it. The order starts from what each channel\r\n" +
                "measured (narrowed by any crossover corner it already carries) —\r\n" +
                "move it when two drivers are too alike for that to decide, as a\r\n" +
                "pair of subwoofers measured full-range will be.");
        }

        return row;
    }

    // Why this channel sits where it does. The band shown is what the driver
    // MEASURED, which is also what bounds the crossover search — but the chain is
    // ordered by what the channel is left playing once its own corners are
    // applied, and when those two differ the row order looks arbitrary without
    // this.
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
            BackColor = UiPalette.DialogSurface,
            FlatStyle = FlatStyle.Popup,
            ForeColor = UiPalette.TextPrimary,
            Margin = new Padding(2, 1, 0, 1),
            Text = glyph,
            UseVisualStyleBackColor = false
        };

    // The groups that have any channel, in the order they are staged.
    private IEnumerable<VirtualCrossoverAlignmentStage> GroupsInOrder() =>
        VirtualCrossoverAlignmentStages.InOrder
            .Where(group => rows.Any(row => row.Source.Group == group));

    private List<ChannelRow> MembersOf(VirtualCrossoverAlignmentStage group) =>
        rows.Where(row => row.Source.Group == group).ToList();

    // The group whose flat top the others are levelled onto: the front chain,
    // which is where the front stage and its subs are. A project with no front
    // chain at all levels onto whichever group runs first.
    private VirtualCrossoverAlignmentStage PrimaryGroup() =>
        GroupsInOrder()
            .DefaultIfEmpty(VirtualCrossoverAlignmentStage.FrontChain)
            .First();

    // Lays the rows into the table. Called again after a reorder with the SAME
    // controls, so nothing created here is re-created and the device-unit sizing
    // LayoutBelowChannelTable applied to the combos survives.
    private void PopulateTable()
    {
        bool headers = GroupsInOrder().Count() > 1;
        tableChannels.SuspendLayout();
        tableChannels.Controls.Clear();
        tableChannels.RowStyles.Clear();
        int line = 0;
        foreach (VirtualCrossoverAlignmentStage group in GroupsInOrder())
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
                // A group of one is not a chain, so it is neither numbered nor
                // given arrows: a "1." and two dead buttons on every rear-fill row
                // would only ask the reader to work out that there is nothing to
                // press and nowhere to go.
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
                ForeColor = UiPalette.TextHighlight,
                Margin = new Padding(0, 8, 0, 2)
            };
            groupHeaders[group] = header;
        }

        return header;
    }

    // Swaps a channel with its neighbour inside its own group. Groups stay
    // contiguous in `rows` because nothing ever moves a channel across one.
    private void MoveInChain(ChannelRow row, int delta)
    {
        List<ChannelRow> members = MembersOf(row.Source.Group);
        int at = members.IndexOf(row);
        int to = at + delta;
        if (at < 0 || to < 0 || to >= members.Count)
        {
            return;
        }

        int one = rows.IndexOf(members[at]);
        int other = rows.IndexOf(members[to]);
        (rows[one], rows[other]) = (rows[other], rows[one]);
        PopulateTable();
        UpdatePreview();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        LayoutBelowChannelTable();
    }

    // Slides the whole options block just below the auto-sized channel table and
    // grows the client area so the bottom-anchored buttons clear the preview.
    // Runs once, after the form has been scaled, so every measurement here is
    // already in device pixels — no hand-computed 96-DPI coordinates survive.
    private void LayoutBelowChannelTable()
    {
        if (optionsPositioned)
        {
            return;
        }

        optionsPositioned = true;

        // The channel combos and the order arrows are controls added to the table
        // at runtime, so the form's one-time font autoscale never reaches them and
        // their fixed height would clip the scaled text at high DPI. The labels are
        // AutoSize and size themselves; these must be sized in device units here,
        // after scaling, so the table row height accounts for them before we
        // measure it.
        Size comboSize = LogicalToDeviceUnits(new Size(110, 19));
        Size arrowSize = LogicalToDeviceUnits(new Size(22, 19));
        foreach (ChannelRow row in rows)
        {
            row.TypeComboBox.Size = comboSize;
            row.Up.Size = arrowSize;
            row.Down.Size = arrowSize;
        }

        tableChannels.PerformLayout();
        int shift = tableChannels.Bottom + LogicalToDeviceUnits(12) - labelFilters.Top;
        foreach (Control control in new Control[]
                 {
                     labelFilters, checkButterworth, checkLinkwitzRiley, checkBessel,
                     labelRange, minCrossover, labelDash, maxCrossover, labelHz,
                     independentSlopes, reorderBlocks, labelSubElevation, subElevation,
                     labelSubElevationUnit, labelPreview
                 })
        {
            control.Top += shift;
        }

        // The preview shows one line per channel plus a heading and a summary for
        // every group, and a summary long enough wraps onto a second line — so it
        // is measured as laid out rather than counted, with the structural count
        // as the floor. Both are in the real font's line height, so it fits at any
        // DPI; then the client area grows to clear the bottom-anchored buttons.
        labelPreview.Height = Math.Max(
                PreviewLineCount() * labelPreview.Font.Height,
                TextRenderer.MeasureText(
                    labelPreview.Text,
                    labelPreview.Font,
                    new Size(labelPreview.Width, int.MaxValue),
                    TextFormatFlags.WordBreak).Height)
            + LogicalToDeviceUnits(6);
        ClientSize = new Size(
            ClientSize.Width,
            labelPreview.Bottom + LogicalToDeviceUnits(12) + buttonApply.Height
                + LogicalToDeviceUnits(12));
    }

    // The tallest the preview can get: every channel, plus a heading and a
    // summary line per group. Computed from the structure rather than the current
    // text, which may be a one-line error while the options are being changed.
    private int PreviewLineCount() =>
        rows.Count + (2 * GroupsInOrder().Count());

    // Maps the designer's filter-family checkboxes to their families and wires the
    // option controls to refresh the live preview when the user changes them.
    private void WireOptionControls()
    {
        familyBoxes.Add((checkButterworth, CrossoverFilterFamily.Butterworth));
        familyBoxes.Add((checkLinkwitzRiley, CrossoverFilterFamily.LinkwitzRiley));
        familyBoxes.Add((checkBessel, CrossoverFilterFamily.Bessel));
        foreach ((CheckBox box, CrossoverFilterFamily _) in familyBoxes)
        {
            box.CheckedChanged += (_, _) => UpdatePreview();
        }

        minCrossover.ValueChanged += (_, _) => UpdatePreview();
        maxCrossover.ValueChanged += (_, _) => UpdatePreview();
        independentSlopes.CheckedChanged += (_, _) => UpdatePreview();
        subElevation.ValueChanged += (_, _) => UpdatePreview();
        toolTip.SetToolTip(
            independentSlopes,
            "Let the low-pass and high-pass of a junction take different slopes\r\n" +
            "(they still share one crossover frequency), to compensate a driver's\r\n" +
            "own roll-off. Off ties each DRIVER's two shoulders (its high-pass and\r\n" +
            "low-pass) to one slope, so no driver ends up steep on one side and\r\n" +
            "shallow on the other; different drivers may still take different\r\n" +
            "slopes — the textbook crossover.");
        toolTip.SetToolTip(
            reorderBlocks,
            "Put the channel blocks in the panel into the same order as this\r\n" +
            "dialog: the groups one after another, and inside each the chain\r\n" +
            "from the lowest driver up. The blocks are lettered by position, so\r\n" +
            "the ones that move are re-lettered and take a new plot colour — and\r\n" +
            "a tuning sheet exported before this names them by the OLD letters.\r\n" +
            "Nothing else moves with them: sources, settings and measurements\r\n" +
            "belong to the block.");
        toolTip.SetToolTip(
            subElevation,
            "How far the lowest driver (the sub, or the woofer/midbass when no\r\n" +
            "sub is present) sits above the levelled midrange/tweeter. Starts at\r\n" +
            "(and is capped by) the measured elevation — the bass at its own\r\n" +
            "level; lower it to flatten the bottom. The midrange/tweeter are\r\n" +
            "levelled to each other and the remaining drivers are only cut, never\r\n" +
            "boosted, onto the resulting target.");
    }

    // The order the wizard settled on, as Init indices, or null when the user
    // does not want the blocks touched.
    private IReadOnlyList<int>? RequestedChainOrder() =>
        reorderBlocks.Checked
            ? rows.Select(row => row.InitIndex).ToList()
            : null;

    private DriverType TypeOf(ChannelRow row) =>
        row.TypeComboBox.SelectedItem is DriverType type ? type : DriverType.Woofer;

    private AutoSetupSource SourceOf(ChannelRow row) =>
        new(row.Source.MagnitudeDb, TypeOf(row), row.Source.Coherence, row.Source.Distortion);

    private IReadOnlyList<CrossoverFilterFamily> SelectedFamilies() =>
        familyBoxes.Where(item => item.Box.Checked).Select(item => item.Family).ToList();

    // The sub elevation belongs to the group that carries the bass; the others
    // keep their own measured internal balance and are levelled as a whole
    // afterwards.
    private CrossoverAutoSetupOptions OptionsFor(bool primary) =>
        new(
            SelectedFamilies(),
            (double)minCrossover.Value,
            (double)maxCrossover.Value,
            independentSlopes.Checked,
            sampleRateHz,
            processorSampleRateHz,
            primary && subElevationInitialized ? (double)subElevation.Value : null);

    // Snapshots what the optimizer needs, on the UI thread: the ranked search
    // then runs on a background one and must not read a combo box.
    private List<GroupPlan> CurrentPlan(bool withImpulseResponses)
    {
        VirtualCrossoverAlignmentStage primary = PrimaryGroup();
        var plan = new List<GroupPlan>();
        foreach (VirtualCrossoverAlignmentStage group in GroupsInOrder())
        {
            List<ChannelRow> members = MembersOf(group);
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

    // Fits every group and levels the others onto the primary's flat top.
    // Pure — no control is touched — so Apply can run it off the UI thread.
    private static List<GroupFit> Fit(
        IReadOnlyList<GroupPlan> plan,
        Func<bool, CrossoverAutoSetupOptions> options,
        double sampleRateHz)
    {
        var fitted = new IReadOnlyList<CrossoverProposal>[plan.Count];
        double? reference = null;
        // The primary group is fitted FIRST whatever position the plan lists it
        // in: the others are levelled onto its reference, and nothing can be
        // levelled onto a fit that has not happened yet. The sort is stable, so
        // the rest keep their order, and the result stays in plan order.
        foreach (int index in Enumerable.Range(0, plan.Count)
                     .OrderByDescending(index => plan[index].IsPrimary))
        {
            GroupPlan group = plan[index];
            CrossoverAutoSetupOptions groupOptions = options(group.IsPrimary);
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

    // The fitted proposals scattered back into the order the channels came in.
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
        if (SelectedFamilies().Count == 0)
        {
            return null;
        }

        try
        {
            return Fit(CurrentPlan(withImpulseResponses), OptionsFor, sampleRateHz);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    // Pre-fills the sub-elevation field once, the first time the channels form a
    // valid proposal: its default and upper limit are the measured elevation of
    // the lowest driver over the levelled mid/tweeter reference, read off the
    // group that carries the bass.
    private void TryInitializeSubElevation(IReadOnlyList<GroupFit> fits)
    {
        GroupFit? primary = fits.FirstOrDefault(fit => fit.Plan.IsPrimary);
        if (subElevationInitialized || primary == null || primary.Plan.Sources.Count < 2)
        {
            return;
        }

        double measured = CrossoverAutoSetup.MeasuredSubElevationDb(
            primary.Plan.Sources, primary.Proposals, sampleRateHz);
        decimal max = (decimal)Math.Max(0, Math.Round(measured, 1));
        subElevation.Maximum = Math.Max(max, subElevation.Minimum);
        subElevationInitialized = true;
        subElevation.Value = max;
    }

    private void UpdatePreview()
    {
        if (!initialized)
        {
            return;
        }

        // Before the early exits: the order marking is about the rows, not about
        // whether a proposal came out, and a row that moved while no family was
        // enabled would otherwise keep the colour of where it used to be.
        MarkChainOrder();
        if (SelectedFamilies().Count == 0)
        {
            buttonApply.Enabled = false;
            labelPreview.Text = "Enable at least one filter family.";
            return;
        }

        List<GroupFit>? fits = TryFit(withImpulseResponses: false);
        buttonApply.Enabled = fits != null;
        if (fits == null)
        {
            labelPreview.Text = "No proposal fits these channels and settings.";
            return;
        }

        // The elevation control changes the gains, so re-fit once it is filled in
        // rather than previewing the pre-fill run's numbers.
        bool wasInitialized = subElevationInitialized;
        TryInitializeSubElevation(fits);
        if (!wasInitialized && subElevationInitialized)
        {
            fits = TryFit(withImpulseResponses: false) ?? fits;
        }

        labelPreview.Text = string.Join(Environment.NewLine, PreviewLines(fits));
    }

    private IEnumerable<string> PreviewLines(IReadOnlyList<GroupFit> fits)
    {
        bool headers = fits.Count > 1;
        // What the other groups were levelled onto. Usually the front chain, and
        // then "the front stage" says it in fewer words than the group's own
        // name; a project without one levels onto whichever group runs first, and
        // that one has to be named.
        VirtualCrossoverAlignmentStage primary =
            fits.FirstOrDefault(fit => fit.Plan.IsPrimary)?.Plan.Group
            ?? VirtualCrossoverAlignmentStage.FrontChain;
        string anchor = primary == VirtualCrossoverAlignmentStage.FrontChain
            ? "front stage"
            : LowerFirst(VirtualCrossoverAlignmentStages.DisplayName(primary));
        foreach (GroupFit fit in fits)
        {
            if (headers)
            {
                yield return VirtualCrossoverAlignmentStages.DisplayName(fit.Plan.Group) + ":";
            }

            for (int i = 0; i < fit.Plan.InitIndices.Count; i++)
            {
                ChannelRow row = rows.First(
                    candidate => candidate.InitIndex == fit.Plan.InitIndices[i]);
                yield return FormatProposal(row, fit.Proposals[i], headers);
            }

            yield return FormatSummary(fit, headers, anchor);
        }
    }

    private static string LowerFirst(string text) =>
        text.Length == 0 ? text : char.ToLowerInvariant(text[0]) + text[1..];

    // The span of the predicted summed response and the sub elevation applied —
    // with the target-curve gains the sum is an intentional downslope (bass
    // lifted), not a flat line, so this reports the span rather than a defect. A
    // group of one has no sum to speak of, so it says what it was levelled to
    // instead.
    private string FormatSummary(GroupFit fit, bool indent, string anchor)
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

        IReadOnlyList<AutoSetupSource> sources = fit.Plan.Sources;
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
        double span = window.Count > 0 ? window.Max() - window.Min() : 0;
        string elevation = fit.Plan.IsPrimary && subElevationInitialized
            ? $"  ·  bass +{(double)subElevation.Value:0.0} dB over mid/treble"
            : string.Empty;
        return $"{prefix}Predicted sum spans {span:0.0} dB over " +
            $"{FormatHz(low.LowHz)}–{FormatHz(high.HighHz)}{elevation}{levelled}";
    }

    private static string FormatProposal(ChannelRow row, CrossoverProposal proposal, bool indent)
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

    // A compact family + slope tag, e.g. "LR24", "BW18", "BE24".
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

    // Every adjacent pair of every chain the measurement has something to say
    // about: the pair of subs the grouping exists for, when neither carries a
    // corner yet, and any pair a row got moved the wrong way round.
    private List<(ChannelRow Earlier, ChannelRow Later, VirtualCrossoverChainOrder Verdict)>
        JudgedPairs()
    {
        var pairs =
            new List<(ChannelRow, ChannelRow, VirtualCrossoverChainOrder)>();
        foreach (VirtualCrossoverAlignmentStage group in GroupsInOrder())
        {
            List<ChannelRow> members = MembersOf(group);
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

    private static double CenterOf(ChannelRow row) =>
        VirtualCrossoverAutoSetupOrder.CenterHz(
            row.Source.Band, row.Source.HighPassHz, row.Source.LowPassHz);

    // Colours the band of every channel the order is in question over. The two
    // cases are not the same and must not look the same: amber for "nothing here
    // says which of these plays lower", red for "this one measures lower than the
    // channel above it", which is a chain running backwards.
    private void MarkChainOrder()
    {
        var doubtful = new Dictionary<ChannelRow, Color>();
        foreach ((ChannelRow earlier, ChannelRow later, VirtualCrossoverChainOrder verdict)
                 in JudgedPairs())
        {
            Color color = verdict == VirtualCrossoverChainOrder.Reversed
                ? UiPalette.WarningRed
                : UiPalette.WarningAmber;
            foreach (ChannelRow row in new[] { earlier, later })
            {
                if (!doubtful.TryGetValue(row, out Color existing) ||
                    existing != UiPalette.WarningRed)
                {
                    doubtful[row] = color;
                }
            }
        }

        foreach (ChannelRow row in rows)
        {
            row.BandLabel.ForeColor = doubtful.TryGetValue(row, out Color color)
                ? color
                : UiPalette.TextSecondarySoft;
        }
    }

    // Stops before writing a chain the measurement did not order, or ordered the
    // other way. The user may know perfectly well which sub is which — the arrows
    // are there for exactly that — so this asks rather than refuses.
    private bool ConfirmChainOrder()
    {
        List<(ChannelRow Earlier, ChannelRow Later, VirtualCrossoverChainOrder Verdict)>
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

    // Every control whose value feeds the proposal. Frozen while the ranking task
    // runs, so the applied result always matches the settings the user sees; their
    // change handlers would otherwise re-enable Apply and overwrite the progress
    // text mid-ranking.
    private IEnumerable<Control> RankingInputControls()
    {
        foreach (ChannelRow row in rows)
        {
            yield return row.TypeComboBox;
            yield return row.Up;
            yield return row.Down;
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

        subElevation.Enabled = enabled && subElevationApplies;
        if (enabled)
        {
            // The arrows' enabled state is positional, not global.
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
            Result = InInitOrder(quick, rows.Count);
            ChainOrder = RequestedChainOrder();
            DialogResult = DialogResult.OK;
            return;
        }

        // The ranked search (candidate pool + achievability post-check on the
        // measured IRs) runs off the UI thread; a couple of seconds on a
        // 4-way. The live preview keeps showing the fast magnitude-only
        // proposal until the ranking lands.
        IReadOnlyList<int>? order = RequestedChainOrder();
        CrossoverAutoSetupOptions primaryOptions = OptionsFor(true);
        CrossoverAutoSetupOptions otherOptions = OptionsFor(false);
        CrossoverAutoSetupOptions Options(bool primary) =>
            primary ? primaryOptions : otherOptions;
        string previousPreview = labelPreview.Text;
        int count = rows.Count;
        buttonApply.Enabled = false;
        SetRankingInputsEnabled(false);
        labelPreview.Text = "Ranking candidates against the measured responses…";
        try
        {
            List<GroupFit> ranked = await Task.Run(
                () => Fit(plan, Options, sampleRateHz));
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
            // A user-input shape problem (an unusable band): the same quiet
            // signal the synchronous path gives.
            if (IsDisposed)
            {
                return;
            }

            labelPreview.Text = previousPreview;
            buttonApply.Enabled = true;
            SetRankingInputsEnabled(true);
            System.Media.SystemSounds.Beep.Play();
        }
        catch (Exception exception)
        {
            // An unhandled exception after an await in an async void handler
            // would land in the WinForms synchronization context and kill the
            // process; the ranking spans PLINQ, FFTs and the alignment search,
            // so restore the dialog and report instead.
            if (IsDisposed)
            {
                return;
            }

            labelPreview.Text = previousPreview;
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
