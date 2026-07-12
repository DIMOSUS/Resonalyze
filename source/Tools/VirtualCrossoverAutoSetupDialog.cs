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
    // The designer lays the options section out below three channel rows; extra
    // rows shift it down by this step and grow the dialog to match.
    private const int DesignRowCount = 3;
    private const int RowTop = 42;
    private const int RowStep = 28;
    private const int PreviewLineHeight = 15;

    public void Init(
        double sampleRateHz,
        IReadOnlyList<(string Name, Color Accent, IReadOnlyList<SignalPoint> MagnitudeDb,
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

        // One row per participating channel, created on the fly so the dialog
        // carries as many channels as the panel does. Below three rows (the
        // designer's layout) the options section slides down and the dialog and
        // its preview grow to fit.
        SuspendLayout();
        for (int i = 0; i < channels.Count; i++)
        {
            (string name, Color accent, IReadOnlyList<SignalPoint> magnitude,
                DriverBandEstimate band) = channels[i];
            int top = RowTop + i * RowStep;

            var nameLabel = new Label
            {
                AutoEllipsis = true,
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204),
                ForeColor = accent,
                Location = new Point(12, top),
                Size = new Size(190, 15),
                Text = name
            };
            var bandLabel = new Label
            {
                AutoSize = true,
                ForeColor = Color.FromArgb(170, 176, 190),
                Location = new Point(210, top),
                Text = $"{FormatHz(band.LowHz)} – {FormatHz(band.HighHz)}"
            };
            var typeComboBox = new DarkComboBox
            {
                BackColor = Color.FromArgb(55, 60, 72),
                ForeColor = Color.White,
                Location = new Point(346, top - 2),
                MinimumSize = new Size(36, 19),
                Size = new Size(110, 19),
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

            Controls.Add(nameLabel);
            Controls.Add(bandLabel);
            Controls.Add(typeComboBox);
            rows.Add(new ChannelRow(name, magnitude, nameLabel, bandLabel, typeComboBox));
        }

        int extraRows = Math.Max(0, channels.Count - DesignRowCount);
        if (extraRows > 0)
        {
            // Extra channel rows push the options down; the preview also gains one
            // line per extra channel. The Apply/Cancel buttons are bottom-anchored
            // and follow the client-size growth on their own.
            int rowShift = extraRows * RowStep;
            foreach (Control control in new Control[]
                     {
                         labelFilters, checkButterworth, checkLinkwitzRiley, checkBessel,
                         labelRange, minCrossover, labelDash, maxCrossover, labelHz,
                         independentSlopes, labelSubElevation, subElevation,
                         labelSubElevationUnit, labelPreview
                     })
            {
                control.Top += rowShift;
            }

            int previewGrowth = extraRows * PreviewLineHeight;
            labelPreview.Height += previewGrowth;
            ClientSize = new Size(ClientSize.Width, ClientSize.Height + rowShift + previewGrowth);
        }

        ResumeLayout();
        initialized = true;
        UpdatePreview();
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
        rows.Select(row => new AutoSetupSource(row.MagnitudeDb, TypeOf(row))).ToList();

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
        DriverBandEstimate low = CrossoverAutoSetup.EstimateBand(
            sources.OrderBy(source => source.Type).First().MagnitudeDb);
        DriverBandEstimate high = CrossoverAutoSetup.EstimateBand(
            sources.OrderBy(source => source.Type).Last().MagnitudeDb);
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
