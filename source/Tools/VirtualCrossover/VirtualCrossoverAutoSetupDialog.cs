using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// The crossover wizard: shows each participating channel with its detected
/// usable band and driver type, lets the user confirm or override the types, and
/// asks which filter families and crossover-frequency window the optimizer may
/// use (and whether the two sides of a junction may take different slopes). The
/// resulting proposal — crossover frequencies, families, slopes and cut-only
/// gains chosen to flatten the magnitude sum — previews live. Apply hands it back
/// to the panel; nothing is written until then.
/// </summary>
internal sealed partial class VirtualCrossoverAutoSetupDialog : Form
{
    private sealed record ChannelRow(
        string Name,
        IReadOnlyList<SignalPoint> MagnitudeDb,
        IReadOnlyList<double>? Coherence,
        IReadOnlyList<SignalPoint>? Distortion,
        Label NameLabel,
        Label BandLabel,
        DarkComboBox TypeComboBox);

    private readonly ToolTip toolTip = new()
    {
        InitialDelay = 500,
        ReshowDelay = 150,
        AutoPopDelay = 12_000,
        ShowAlways = true
    };

    private readonly List<ChannelRow> rows = new();
    private readonly List<(CheckBox Box, CrossoverFilterFamily Family)> familyBoxes = new();
    private double sampleRateHz = 48_000;
    private bool initialized;
    // The sub-elevation field is pre-filled once, from the first valid proposal's
    // measured elevation (its default and upper limit). Until then options carry a
    // null elevation so the DSP uses that measured default itself.
    private bool subElevationInitialized;
    // The channels' measured transfer IRs (Init order). When present, Apply
    // re-ranks the top candidates by the junction loss achievable after the
    // best per-junction delay, instead of trusting the magnitude score alone.
    private IReadOnlyList<Complex[]>? impulseResponses;

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
        // its components container, so release it here.
        Disposed += (_, _) => toolTip.Dispose();
        toolTip.SetToolTip(
            labelPreview,
            "The proposal that Apply writes into the channels: crossover\r\n" +
            "frequencies, families and slopes chosen to flatten the summed\r\n" +
            "magnitude response, plus cut-only gains that level the channels.");
    }

    /// <summary>The proposal computed on Apply, in the same order as the Init channels.</summary>
    public IReadOnlyList<CrossoverProposal>? Result { get; private set; }

    /// <summary>
    /// Seeds one row per participating channel: the display name, its accent
    /// color, the smoothed raw magnitude curve, and the auto-detected band/type.
    /// The sample rate is needed because the optimizer evaluates the exact digital
    /// biquad cascades the DSP runs.
    /// </summary>
    private bool optionsPositioned;

    public void Init(
        double sampleRateHz,
        IReadOnlyList<(string Name, Color Accent, IReadOnlyList<SignalPoint> MagnitudeDb,
            IReadOnlyList<double>? Coherence, IReadOnlyList<SignalPoint>? Distortion,
            DriverBandEstimate Band)> channels,
        IReadOnlyList<Complex[]>? impulseResponses = null)
    {
        this.sampleRateHz = sampleRateHz;
        this.impulseResponses = impulseResponses;
        // Matches the optimizer's Nyquist ceiling; at 44.1 kHz this keeps the full
        // 20 kHz reachable instead of clamping to ~19.8 kHz.
        double ceiling = Math.Min(20_000, sampleRateHz * 0.49);
        maxCrossover.Maximum = (decimal)Math.Round(ceiling);
        minCrossover.Maximum = maxCrossover.Maximum;
        if ((double)maxCrossover.Value > ceiling)
        {
            maxCrossover.Value = maxCrossover.Maximum;
        }

        // One row per participating channel, laid out by the designer's
        // TableLayoutPanel so the cells scale with the font/DPI instead of the
        // old hand-computed pixel coordinates. OnLoad then slides the options
        // section below the finished table and grows the dialog to fit.
        rows.Clear();
        tableChannels.SuspendLayout();
        tableChannels.Controls.Clear();
        tableChannels.RowStyles.Clear();
        tableChannels.RowCount = channels.Count;
        for (int i = 0; i < channels.Count; i++)
        {
            (string name, Color accent, IReadOnlyList<SignalPoint> magnitude,
                IReadOnlyList<double>? coherence, IReadOnlyList<SignalPoint>? distortion,
                DriverBandEstimate band) = channels[i];

            var nameLabel = new Label
            {
                Anchor = AnchorStyles.Left,
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204),
                ForeColor = accent,
                Margin = new Padding(0, 4, 24, 4),
                Text = name
            };
            var bandLabel = new Label
            {
                Anchor = AnchorStyles.Left,
                AutoSize = true,
                ForeColor = Color.FromArgb(170, 176, 190),
                Margin = new Padding(0, 4, 24, 4),
                Text = $"{FormatHz(band.LowHz)} – {FormatHz(band.HighHz)}"
            };
            var typeComboBox = new DarkComboBox
            {
                Anchor = AnchorStyles.Left,
                BackColor = Color.FromArgb(55, 60, 72),
                ForeColor = Color.White,
                Margin = new Padding(0, 1, 0, 1),
                TabIndex = i
            };
            typeComboBox.Items.AddRange(
            [
                DriverType.Subwoofer,
                DriverType.Woofer,
                DriverType.Midbass,
                DriverType.Midrange,
                DriverType.Tweeter
            ]);
            typeComboBox.SelectedItem = band.SuggestedType;
            typeComboBox.SelectedIndexChanged += (_, _) => UpdatePreview();

            tableChannels.Controls.Add(nameLabel, 0, i);
            tableChannels.Controls.Add(bandLabel, 1, i);
            tableChannels.Controls.Add(typeComboBox, 2, i);
            rows.Add(new ChannelRow(
                name, magnitude, coherence, distortion, nameLabel, bandLabel, typeComboBox));
        }

        tableChannels.ResumeLayout(true);
        initialized = true;
        UpdatePreview();
        if (IsHandleCreated)
        {
            LayoutBelowChannelTable();
        }
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

        // The channel combos are DarkComboBox UserControls added to the table at
        // runtime, so the form's one-time font autoscale never reaches them and
        // their fixed 19-px height would clip the scaled text at high DPI. The
        // labels are AutoSize and size themselves; the combos must be sized in
        // device units here, after scaling, so the table row height accounts for
        // them before we measure it.
        Size comboSize = LogicalToDeviceUnits(new Size(110, 19));
        foreach (ChannelRow row in rows)
        {
            row.TypeComboBox.Size = comboSize;
        }

        tableChannels.PerformLayout();
        int shift = tableChannels.Bottom + LogicalToDeviceUnits(12) - labelFilters.Top;
        foreach (Control control in new Control[]
                 {
                     labelFilters, checkButterworth, checkLinkwitzRiley, checkBessel,
                     labelRange, minCrossover, labelDash, maxCrossover, labelHz,
                     independentSlopes, labelSubElevation, subElevation,
                     labelSubElevationUnit, labelPreview
                 })
        {
            control.Top += shift;
        }

        // The preview shows one line per channel plus a summary; size it to the
        // real font line height so it fits at any DPI, then grow the client area
        // to clear the bottom-anchored buttons.
        labelPreview.Height =
            (rows.Count + 1) * labelPreview.Font.Height + LogicalToDeviceUnits(6);
        ClientSize = new Size(
            ClientSize.Width,
            labelPreview.Bottom + LogicalToDeviceUnits(12) + buttonApply.Height
                + LogicalToDeviceUnits(12));
    }

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
            subElevation,
            "How far the lowest driver sits above the levelled midrange/tweeter.\r\n" +
            "Starts at (and is capped by) the measured elevation — the sub at its\r\n" +
            "own level; lower it to flatten the bottom. The midrange/tweeter are\r\n" +
            "levelled to each other and the remaining drivers are only cut, never\r\n" +
            "boosted, onto the resulting target.");
    }

    // One wizard source per channel row, in the row (Init) order.
    private List<AutoSetupSource> CurrentSources() =>
        rows.Select(row => new AutoSetupSource(
                row.MagnitudeDb, TypeOf(row), row.Coherence, row.Distortion))
            .ToList();

    private DriverType TypeOf(ChannelRow row) =>
        row.TypeComboBox.SelectedItem is DriverType type ? type : DriverType.Woofer;

    private IReadOnlyList<CrossoverFilterFamily> SelectedFamilies() =>
        familyBoxes.Where(item => item.Box.Checked).Select(item => item.Family).ToList();

    private CrossoverAutoSetupOptions CurrentOptions() =>
        new(
            SelectedFamilies(),
            (double)minCrossover.Value,
            (double)maxCrossover.Value,
            independentSlopes.Checked,
            sampleRateHz,
            subElevationInitialized ? (double)subElevation.Value : null);

    private IReadOnlyList<CrossoverProposal>? TryPropose()
    {
        if (SelectedFamilies().Count == 0)
        {
            return null;
        }

        try
        {
            return CrossoverAutoSetup.Propose(CurrentSources(), CurrentOptions());
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    // Pre-fills the sub-elevation field once, the first time the driver types
    // form a valid proposal: its default and upper limit are the measured
    // elevation of the lowest driver over the levelled mid/tweeter reference.
    private void TryInitializeSubElevation()
    {
        if (subElevationInitialized || SelectedFamilies().Count == 0)
        {
            return;
        }

        List<AutoSetupSource> sources = CurrentSources();
        try
        {
            IReadOnlyList<CrossoverProposal> proposals =
                CrossoverAutoSetup.Propose(sources, CurrentOptions());
            double measured = CrossoverAutoSetup.MeasuredSubElevationDb(
                sources, proposals, sampleRateHz);
            decimal max = (decimal)Math.Max(0, Math.Round(measured, 1));
            subElevation.Maximum = Math.Max(max, subElevation.Minimum);
            subElevationInitialized = true;
            subElevation.Value = max;
        }
        catch (ArgumentException)
        {
            // Types are not yet distinct; retry on the next change.
        }
    }

    private void UpdatePreview()
    {
        if (!initialized)
        {
            return;
        }

        if (SelectedFamilies().Count == 0)
        {
            buttonApply.Enabled = false;
            labelPreview.Text = "Enable at least one filter family.";
            return;
        }

        TryInitializeSubElevation();
        IReadOnlyList<CrossoverProposal>? proposals = TryPropose();
        buttonApply.Enabled = proposals != null;
        if (proposals == null)
        {
            labelPreview.Text = "Assign a distinct driver type to every channel.";
            return;
        }

        var lines = rows
            .Select((row, index) => FormatProposal(row, proposals[index]))
            .ToList();
        lines.Add(FormatSummary(proposals));
        labelPreview.Text = string.Join(Environment.NewLine, lines);
    }

    // The span of the predicted summed response and the sub elevation applied —
    // with the target-curve gains the sum is an intentional downslope (bass
    // lifted), not a flat line, so this reports the span rather than a defect.
    private string FormatSummary(IReadOnlyList<CrossoverProposal> proposals)
    {
        List<AutoSetupSource> sources = CurrentSources();
        AutoSetupSource lowSource = sources.OrderBy(source => source.Type).First();
        AutoSetupSource highSource = sources.OrderBy(source => source.Type).Last();
        DriverBandEstimate low = CrossoverAutoSetup.EstimateBand(
            lowSource.MagnitudeDb, lowSource.Coherence);
        DriverBandEstimate high = CrossoverAutoSetup.EstimateBand(
            highSource.MagnitudeDb, highSource.Coherence);
        double trim = Math.Pow(2.0, 0.5);

        var window = CrossoverAutoSetup
            .SummedResponseDb(sources, proposals, sampleRateHz)
            .Where(point => point.X >= low.LowHz * trim && point.X <= high.HighHz / trim)
            .Select(point => point.Y)
            .ToList();
        double span = window.Count > 0 ? window.Max() - window.Min() : 0;
        string elevation = subElevationInitialized
            ? $"  ·  sub +{(double)subElevation.Value:0.0} dB over mid/treble"
            : string.Empty;
        return $"Predicted sum spans {span:0.0} dB over " +
            $"{FormatHz(low.LowHz)}–{FormatHz(high.HighHz)}{elevation}";
    }

    private static string FormatProposal(ChannelRow row, CrossoverProposal proposal)
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
        return $"{row.Name}:  {string.Join(",  ", parts)}";
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

    // Every control whose value feeds TryPropose()/CurrentOptions(). Frozen
    // while the ranking task runs, so the applied result always matches the
    // settings the user sees; their change handlers would otherwise re-enable
    // Apply and overwrite the progress text mid-ranking.
    private IEnumerable<Control> RankingInputControls()
    {
        foreach (ChannelRow row in rows)
        {
            yield return row.TypeComboBox;
        }

        foreach ((CheckBox box, CrossoverFilterFamily _) in familyBoxes)
        {
            yield return box;
        }

        yield return minCrossover;
        yield return maxCrossover;
        yield return independentSlopes;
        yield return subElevation;
    }

    private void SetRankingInputsEnabled(bool enabled)
    {
        foreach (Control control in RankingInputControls())
        {
            control.Enabled = enabled;
        }
    }

    private async void ApplyClick(object? sender, EventArgs e)
    {
        IReadOnlyList<CrossoverProposal>? quick = TryPropose();
        if (quick == null)
        {
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        if (impulseResponses == null)
        {
            Result = quick;
            DialogResult = DialogResult.OK;
            return;
        }

        // The ranked search (candidate pool + achievability post-check on the
        // measured IRs) runs off the UI thread; a couple of seconds on a
        // 4-way. The live preview keeps showing the fast magnitude-only
        // proposal until the ranking lands.
        List<AutoSetupSource> sources = CurrentSources();
        CrossoverAutoSetupOptions options = CurrentOptions();
        IReadOnlyList<Complex[]> responses = impulseResponses;
        string previousPreview = labelPreview.Text;
        buttonApply.Enabled = false;
        SetRankingInputsEnabled(false);
        labelPreview.Text = "Ranking candidates against the measured responses…";
        try
        {
            IReadOnlyList<RankedCrossoverProposal> ranked = await Task.Run(
                () => CrossoverAutoSetup.ProposeRanked(sources, options, responses));
            if (IsDisposed)
            {
                return;
            }

            Result = ranked[0].Proposals;
            DialogResult = DialogResult.OK;
        }
        catch (ArgumentException)
        {
            // A user-input shape problem (duplicate types, unusable band):
            // the same quiet signal the synchronous path gives.
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
