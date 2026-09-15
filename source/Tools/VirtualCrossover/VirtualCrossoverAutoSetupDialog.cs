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

/// <summary>Crossover wizard; each group is fitted as its own chain, then levelled onto the front stage.
/// See docs/tech/crossover-auto-setup.md#groups-outside-the-chain.</summary>
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

    // Display order: groups as staged, chain order inside each.
    private readonly List<ChannelRow> rows = new();
    private readonly Dictionary<VirtualCrossoverAlignmentStage, Label> groupHeaders = new();
    private readonly List<(CheckBox Box, CrossoverFilterFamily Family)> familyBoxes = new();
    private double sampleRateHz = 48_000;
    // Independent of the measurement rate, which only bounds the analysis band.
    private double processorSampleRateHz = 48_000;
    private bool initialized;
    // Pre-filled once from the first measured elevation; null until then so the DSP uses the measured default.
    private bool subElevationInitialized;
    private bool subElevationApplies = true;

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

    public IReadOnlyList<CrossoverProposal>? Result { get; private set; }

    /// <summary>Init indices in crossed order; null when <c>Reorder the channel blocks</c> is cleared (the panel decides what reordering means).</summary>
    public IReadOnlyList<int>? ChainOrder { get; private set; }

    private bool optionsPositioned;

    public void Init(
        double sampleRateHz,
        double processorSampleRateHz,
        IReadOnlyList<AutoSetupWizardChannel> channels)
    {
        this.sampleRateHz = sampleRateHz;
        this.processorSampleRateHz = processorSampleRateHz;
        // At 44.1 kHz this keeps 20 kHz reachable.
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
                // Seeded from each channel's effective band; the arrows override where the measurement cannot decide.
                .OrderBy(item => VirtualCrossoverAutoSetupOrder.CenterHz(
                    item.channel.Band, item.channel.HighPassHz, item.channel.LowPassHz));
            foreach ((AutoSetupWizardChannel channel, int index) in members)
            {
                rows.Add(BuildRow(index, channel));
            }
        }

        PopulateTable();
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
            BackColor = UiPalette.DialogSurface,
            FlatStyle = FlatStyle.Popup,
            ForeColor = UiPalette.TextPrimary,
            Margin = new Padding(2, 1, 0, 1),
            Text = glyph,
            UseVisualStyleBackColor = false
        };

    private IEnumerable<VirtualCrossoverAlignmentStage> GroupsInOrder() =>
        VirtualCrossoverAlignmentStages.InOrder
            .Where(group => rows.Any(row => row.Source.Group == group));

    private List<ChannelRow> MembersOf(VirtualCrossoverAlignmentStage group) =>
        rows.Where(row => row.Source.Group == group).ToList();

    // The front chain; without one, the first staged group.
    private VirtualCrossoverAlignmentStage PrimaryGroup() =>
        GroupsInOrder()
            .DefaultIfEmpty(VirtualCrossoverAlignmentStage.FrontChain)
            .First();

    // Re-run after a reorder with the same controls, so device-unit sizing from LayoutBelowChannelTable survives.
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
                ForeColor = UiPalette.TextHighlight,
                Margin = new Padding(0, 8, 0, 2)
            };
            groupHeaders[group] = header;
        }

        return header;
    }

    // Groups stay contiguous in `rows`: nothing moves a channel across one.
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
        foreach (ChannelRow row in rows)
        {
            row.TypeComboBox.Size = comboSize;
            row.Up.Size = arrowSize;
            row.Down.Size = arrowSize;
        }

        tableChannels.PerformLayout();
        int outsideMargin = LogicalToDeviceUnits(12);
        int shift = tableChannels.Bottom + outsideMargin - labelFilters.Top;
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

        // The AutoSize table can exceed the designed width even at 100% DPI.
        int clientWidth = Math.Max(ClientSize.Width, tableChannels.Right + outsideMargin);
        labelPreview.Width = clientWidth - labelPreview.Left - outsideMargin;

        // Measured as laid out (summaries wrap), floored at the structural line count.
        labelPreview.Height = Math.Max(
                PreviewLineCount() * labelPreview.Font.Height,
                TextRenderer.MeasureText(
                    labelPreview.Text,
                    labelPreview.Font,
                    new Size(labelPreview.Width, int.MaxValue),
                    TextFormatFlags.WordBreak).Height)
            + LogicalToDeviceUnits(6);
        ClientSize = new Size(
            clientWidth,
            labelPreview.Bottom + outsideMargin + buttonApply.Height + outsideMargin);
    }

    // From structure, not current text (which may be a one-line error).
    private int PreviewLineCount() =>
        rows.Count + (2 * GroupsInOrder().Count());

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
            "Let a junction's low-pass and high-pass take different slopes\r\n" +
            "(one frequency still). Off ties each driver's two shoulders\r\n" +
            "to one slope — the textbook crossover.");
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

    // Sub elevation applies to the primary group only; others keep their balance and are levelled as a whole.
    private CrossoverAutoSetupOptions OptionsFor(bool primary) =>
        new(
            SelectedFamilies(),
            (double)minCrossover.Value,
            (double)maxCrossover.Value,
            independentSlopes.Checked,
            sampleRateHz,
            processorSampleRateHz,
            primary && subElevationInitialized ? (double)subElevation.Value : null);

    // Snapshot on the UI thread; the ranked search runs in the background.
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

    // Pure, so Apply can run it off the UI thread.
    private static List<GroupFit> Fit(
        IReadOnlyList<GroupPlan> plan,
        Func<bool, CrossoverAutoSetupOptions> options,
        double sampleRateHz)
    {
        var fitted = new IReadOnlyList<CrossoverProposal>[plan.Count];
        double? reference = null;
        // Primary fitted first (others level onto it); stable sort keeps plan order for the rest.
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

    /// <summary>Recomputes the ceiling on every fit (reorders and type changes move the bass anchor); sets the value only on the first fit.</summary>
    private void UpdateSubElevationRange(IReadOnlyList<GroupFit> fits)
    {
        GroupFit? primary = fits.FirstOrDefault(fit => fit.Plan.IsPrimary);
        if (primary == null || primary.Plan.Sources.Count < 2)
        {
            return;
        }

        double measured = CrossoverAutoSetup.MeasuredSubElevationDb(
            primary.Plan.Sources, primary.Proposals, sampleRateHz);
        decimal max = (decimal)Math.Max(0, Math.Round(measured, 1));
        subElevation.Maximum = Math.Max(max, subElevation.Minimum);
        if (!subElevationInitialized)
        {
            subElevationInitialized = true;
            subElevation.Value = max;
        }
    }

    private void UpdatePreview()
    {
        if (!initialized)
        {
            return;
        }

        // Before the early exits: a moved row must not keep its old colour.
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

        // Re-fit when this run moved the value, or the preview prints an elevation the proposal does not have.
        decimal before = subElevation.Value;
        UpdateSubElevationRange(fits);
        if (subElevation.Value != before)
        {
            fits = TryFit(withImpulseResponses: false) ?? fits;
        }

        labelPreview.Text = string.Join(Environment.NewLine, PreviewLines(fits));
    }

    private IEnumerable<string> PreviewLines(IReadOnlyList<GroupFit> fits)
    {
        bool headers = fits.Count > 1;
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

    // Target-curve gains make the sum an intentional downslope, so report its span, not a defect.
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

    // Amber: order undetermined; red: chain runs backwards.
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

    // Asks rather than refuses: the user may know which sub is which.
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

    // Frozen during ranking so the applied result matches the visible settings.
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

        // Ranking takes seconds on a 4-way; the preview shows the magnitude-only proposal until it lands.
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
            // An exception after await in async void would kill the process via the WinForms context.
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
