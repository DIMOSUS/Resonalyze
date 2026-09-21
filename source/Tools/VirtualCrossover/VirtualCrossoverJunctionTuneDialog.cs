using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>What one junction's tune was asked for: which junction, where its corner may sit, which filters are on
/// offer, and optionally the ACOUSTIC crossover it should add up to.</summary>
internal sealed record JunctionTuneRequest(
    int JunctionIndex,
    double MinHz,
    double MaxHz,
    IReadOnlyList<CrossoverFilterFamily> Families,
    IReadOnlyList<int> Slopes,
    bool IndependentSlopes,
    JunctionAcousticTarget? AcousticGoal,
    bool SplitCorners);

/// <summary>What a junction opens on: the window the assistant's tune would use, the families it already runs, and
/// the acoustic goal its channel cards already hold.</summary>
internal sealed record JunctionTuneDefaults(
    double MinHz,
    double MaxHz,
    IReadOnlyList<CrossoverFilterFamily> Families,
    JunctionAcousticTarget? Goal);

/// <summary>What the search found, for the report and for Apply.</summary>
/// <param name="Recommended">Whether Apply would write what the search advises, which colours the status: Apply
/// is also offered for a found crossover the report advises against, because the choice is the user's.</param>
internal sealed record JunctionTuneOutcome(
    IReadOnlyList<JunctionTuneLine> Report,
    bool CanApply,
    string Status,
    bool Refused,
    bool Recommended = false);

/// <summary>
/// Refines one junction of a finished tune: the lower channel's low-pass and the upper channel's high-pass, judged on
/// the coherent sum through the full chains, optionally against a stated acoustic slope. The search runs in the
/// panel; this dialog states the question and shows the answer. See docs/tech/crossover-auto-setup.md#junction-tuner.
/// </summary>
internal sealed partial class VirtualCrossoverJunctionTuneDialog : Form
{
    private const string Nothing = "—";

    /// <summary>Status for a question that changed after its answer landed.</summary>
    private const string Again = "The question changed — search again.";

    private readonly WrappingToolTip toolTip = new()
    {
        AutoPopDelay = 20_000,
        InitialDelay = 400,
        ReshowDelay = 100
    };

    private Func<JunctionTuneRequest, Task<JunctionTuneOutcome>>? runner;
    private Func<int, JunctionTuneDefaults>? defaultsFor;
    private bool running;

    /// <summary>Bumped by every change to the question. The boxes stay live while the search runs, so this is
    /// what tells an answer that came back for the question on screen from one that came back for a retired
    /// question.</summary>
    private int question;

    private CheckBox[] FamilyBoxes => [checkButterworth, checkLinkwitzRiley, checkBessel];

    /// <summary>Slopes the window offers, as the crossover wizard offers them: 6 dB/oct protects nothing and the
    /// search leaves it out anyway.</summary>
    private static readonly int[] SelectableSlopes = CrossoverFilter
        .SupportedSlopes(CrossoverFilterFamily.Butterworth)
        .Where(slope => slope >= CrossoverJunctionTuner.PracticalSlopeFloorDbPerOctave)
        .ToArray();

    public VirtualCrossoverJunctionTuneDialog()
    {
        InitializeComponent();
        comboBoxGoalFamily.Items.Add(Nothing);
        foreach (CrossoverFamilyChoice family in CrossoverFamilyChoice.Offered)
        {
            comboBoxGoalFamily.Items.Add(family);
        }

        comboBoxGoalFamily.SelectedItem = Nothing;
        comboBoxGoalSlope.Enabled = false;
        foreach (ThemedComboBox window in new[] { comboBoxMinSlope, comboBoxMaxSlope })
        {
            window.Items.AddRange(SelectableSlopes.Cast<object>().ToArray());
            window.SelectedIndexChanged += (_, _) => InvalidateResult(Again);
        }

        // The whole menu by default: narrowing it is the point of the field, not its normal state.
        comboBoxMinSlope.SelectedItem = SelectableSlopes[0];
        comboBoxMaxSlope.SelectedItem = SelectableSlopes[^1];
        comboBoxGoalFamily.SelectedIndexChanged += (_, _) => FillGoalSlopes();
        comboBoxJunction.SelectedIndexChanged += (_, _) => PresentJunctionDefaults();
        radioSummation.CheckedChanged += (_, _) => PresentMode();
        radioAcoustic.CheckedChanged += (_, _) => PresentMode();
        // Every input retires the answer: Apply must never stand for a question nobody asked.
        numericMinHz.ValueChanged += (_, _) => InvalidateResult(Again);
        numericMaxHz.ValueChanged += (_, _) => InvalidateResult(Again);
        comboBoxGoalSlope.SelectedIndexChanged += (_, _) => InvalidateResult(Again);
        checkBoxIndependentSlopes.CheckedChanged += (_, _) => InvalidateResult(Again);
        foreach (CheckBox family in FamilyBoxes)
        {
            family.CheckedChanged += (_, _) => InvalidateResult(Again);
        }
        checkBoxSplitCorners.CheckedChanged += (_, _) => InvalidateResult(Again);
        buttonRun.Click += async (_, _) => await RunAsync().ConfigureAwait(true);
        buttonApply.Click += (_, _) =>
        {
            DialogResult = DialogResult.OK;
            Close();
        };
        Tips();
        PresentMode();
    }

    /// <summary>
    /// Which of the two questions the search answers, stated where it cannot be missed: the best summation this
    /// junction can have, or the electrical filter that lands nearest a stated ACOUSTIC crossover among the
    /// candidates that sum as well. Without the switch the dialog never said what it was optimising.
    /// </summary>
    private void PresentMode()
    {
        bool acoustic = radioAcoustic.Checked;
        comboBoxGoalFamily.Enabled = acoustic;
        comboBoxGoalSlope.Enabled = acoustic && comboBoxGoalFamily.SelectedItem is CrossoverFamilyChoice;
        // The slope window belongs to the summation mode. Stating an ACOUSTIC slope already says what the answer
        // must come to, so tying the electrical slopes down as well only takes filters away from the search.
        comboBoxMinSlope.Enabled = !acoustic;
        comboBoxMaxSlope.Enabled = !acoustic;
        UiStyle.SetTextEnabledLook(labelSlopes, !acoustic);
        UiStyle.SetTextEnabledLook(labelSlopeTo, !acoustic);
        if (acoustic && comboBoxGoalFamily.SelectedItem is not CrossoverFamilyChoice)
        {
            // The mode IS the goal: entering it with nothing stated would search for nothing.
            comboBoxGoalFamily.SelectedItem = CrossoverFamilyChoice.Offered
                .First(choice => choice.Value == CrossoverFilterFamily.LinkwitzRiley);
        }

        labelGoalHint.Text = acoustic
            ? "Driver and filter together, which is steeper than the filter alone. Chosen among filters that sum " +
              "as well."
            : "Every allowed filter is read on the coherent sum at this junction; the one that sums best wins.";
        InvalidateResult(Again);
    }

    /// <summary>The request the Apply button stands for; null until a search has landed.</summary>
    public JunctionTuneRequest? Result { get; private set; }

    /// <param name="junctions">Labels as the panel's read-outs name them, lower to upper.</param>
    /// <param name="defaults">The corner window, the families in use and the card's own goal, by junction index.</param>
    /// <param name="search">Runs the search off the UI thread; the dialog owns the await and the buttons.</param>
    public void Init(
        IReadOnlyList<string> junctions,
        Func<int, JunctionTuneDefaults> defaults,
        Func<JunctionTuneRequest, Task<JunctionTuneOutcome>> search)
    {
        ArgumentNullException.ThrowIfNull(junctions);
        defaultsFor = defaults ?? throw new ArgumentNullException(nameof(defaults));
        runner = search ?? throw new ArgumentNullException(nameof(search));
        comboBoxJunction.Items.Clear();
        foreach (string junction in junctions)
        {
            comboBoxJunction.Items.Add(junction);
        }

        if (junctions.Count > 0)
        {
            comboBoxJunction.SelectedIndex = 0;
        }
        else
        {
            buttonRun.Enabled = false;
            labelStatus.Text = "This view has no junction with two measured blocks.";
            labelStatus.ForeColor = UiPalette.Warning;
        }
    }

    private void PresentJunctionDefaults()
    {
        if (defaultsFor is not { } defaults || comboBoxJunction.SelectedIndex < 0)
        {
            return;
        }

        JunctionTuneDefaults opening = defaults(comboBoxJunction.SelectedIndex);
        numericMinHz.Value = numericMinHz.ClampValue(opening.MinHz);
        numericMaxHz.Value = numericMaxHz.ClampValue(opening.MaxHz);
        // What the junction already runs is what it is offered, so a search asks about the filters in use first.
        checkButterworth.Checked = opening.Families.Contains(CrossoverFilterFamily.Butterworth);
        checkLinkwitzRiley.Checked = opening.Families.Contains(CrossoverFilterFamily.LinkwitzRiley);
        checkBessel.Checked = opening.Families.Contains(CrossoverFilterFamily.Bessel);
        // The card's own wish is what this junction already asks for, so the dialog opens on it.
        if (opening.Goal is { } asked)
        {
            // A junction whose cards already state a goal opens on it: that is the question it was last asked.
            comboBoxGoalFamily.SelectedItem = CrossoverFamilyChoice.Offered
                .FirstOrDefault(choice => choice.Value == asked.Family) ?? (object)Nothing;
            comboBoxGoalSlope.SelectedItem = asked.SlopeDbPerOctave;
            radioAcoustic.Checked = true;
        }

        InvalidateResult("Nothing searched yet.");
    }

    private void FillGoalSlopes()
    {
        if (comboBoxGoalFamily.SelectedItem is not CrossoverFamilyChoice choice)
        {
            comboBoxGoalSlope.Items.Clear();
            comboBoxGoalSlope.Enabled = false;
            InvalidateResult();
            return;
        }

        int? kept = comboBoxGoalSlope.SelectedItem as int?;
        comboBoxGoalSlope.Enabled = true;
        comboBoxGoalSlope.Items.Clear();
        foreach (int slope in CrossoverFilter.SupportedSlopes(choice.Value))
        {
            comboBoxGoalSlope.Items.Add(slope);
        }

        comboBoxGoalSlope.SelectedItem = kept is { } previous && comboBoxGoalSlope.Items.Contains(previous)
            ? previous
            : comboBoxGoalSlope.Items[Math.Min(1, comboBoxGoalSlope.Items.Count - 1)];
        InvalidateResult();
    }

    /// <summary>Any change to the question retires the answer: Apply must never stand for a search nobody ran.</summary>
    private void InvalidateResult(string? status = null)
    {
        question++;
        Result = null;
        buttonApply.Enabled = false;
        if (status != null)
        {
            labelStatus.Text = status;
            labelStatus.ForeColor = UiPalette.TextMuted;
        }
    }

    internal async Task RunAsync()
    {
        if (running || runner is not { } search || comboBoxJunction.SelectedIndex < 0)
        {
            return;
        }

        var families = new List<CrossoverFilterFamily>();
        if (checkButterworth.Checked)
        {
            families.Add(CrossoverFilterFamily.Butterworth);
        }
        if (checkLinkwitzRiley.Checked)
        {
            families.Add(CrossoverFilterFamily.LinkwitzRiley);
        }
        if (checkBessel.Checked)
        {
            families.Add(CrossoverFilterFamily.Bessel);
        }

        if (families.Count == 0)
        {
            labelStatus.Text = "Tick at least one filter family to search.";
            labelStatus.ForeColor = UiPalette.Warning;
            return;
        }

        int lowSlope = comboBoxMinSlope.SelectedItem as int? ?? SelectableSlopes[0];
        int highSlope = comboBoxMaxSlope.SelectedItem as int? ?? SelectableSlopes[^1];
        // Empty means "every slope the families have": what the acoustic mode always wants.
        List<int> slopes = radioAcoustic.Checked
            ? []
            : SelectableSlopes
                .Where(slope => slope >= Math.Min(lowSlope, highSlope) && slope <= Math.Max(lowSlope, highSlope))
                .ToList();
        var request = new JunctionTuneRequest(
            comboBoxJunction.SelectedIndex,
            (double)numericMinHz.Value,
            (double)numericMaxHz.Value,
            families,
            slopes,
            checkBoxIndependentSlopes.Checked,
            radioAcoustic.Checked &&
                comboBoxGoalFamily.SelectedItem is CrossoverFamilyChoice goalFamily &&
                comboBoxGoalSlope.SelectedItem is int goalSlope
                    ? new JunctionAcousticTarget(goalFamily.Value, goalSlope)
                    : null,
            checkBoxSplitCorners.Checked);

        int asked = question;
        running = true;
        buttonRun.Enabled = false;
        buttonApply.Enabled = false;
        buttonCancel.Enabled = false;
        UseWaitCursor = true;
        labelStatus.Text = "Searching…";
        labelStatus.ForeColor = UiPalette.TextMuted;
        try
        {
            JunctionTuneOutcome outcome = await search(request).ConfigureAwait(true);
            if (IsDisposed)
            {
                return;
            }

            if (asked != question)
            {
                // The question moved while the search ran: the report would describe the old one and Apply
                // would write it. The handler that moved it has already said so on the status line.
                return;
            }

            ShowReport(outcome.Report);
            labelStatus.Text = outcome.Status;
            labelStatus.ForeColor = outcome.Refused
                ? UiPalette.Error
                : outcome.Recommended ? UiPalette.Success : UiPalette.Warning;
            Result = outcome.CanApply ? request : null;
            buttonApply.Enabled = outcome.CanApply;
        }
        finally
        {
            running = false;
            if (!IsDisposed)
            {
                buttonRun.Enabled = true;
                buttonCancel.Enabled = true;
                UseWaitCursor = false;
            }
        }
    }

    /// <summary>Writes the report into the pane, colouring the figures that moved: green where the answer is
    /// better, red where it is worse. The colours are the only thing the pane does that a label could not.</summary>
    private void ShowReport(IReadOnlyList<JunctionTuneLine> report)
    {
        textBoxReport.BeginUpdate();
        try
        {
            textBoxReport.Clear();
            foreach (JunctionTuneLine line in report)
            {
                foreach (JunctionTuneSpan span in line.Spans)
                {
                    textBoxReport.SelectionStart = textBoxReport.TextLength;
                    textBoxReport.SelectionLength = 0;
                    textBoxReport.SelectionColor = span.Tone switch
                    {
                        JunctionTuneTone.Better => UiPalette.Success,
                        JunctionTuneTone.Worse => UiPalette.Error,
                        _ => textBoxReport.ForeColor
                    };
                    textBoxReport.AppendText(span.Text);
                }

                textBoxReport.AppendText(Environment.NewLine);
            }

            textBoxReport.SelectionStart = 0;
            textBoxReport.SelectionLength = 0;
            textBoxReport.SelectionColor = textBoxReport.ForeColor;
        }
        finally
        {
            textBoxReport.EndUpdate();
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        // A search writes nothing until Apply, but closing mid-search would leave the panel's await holding a
        // disposed dialog.
        if (running)
        {
            e.Cancel = true;
            return;
        }

        base.OnFormClosing(e);
    }

    private void Tips()
    {
        toolTip.SetToolTip(
            comboBoxJunction,
            "Which junction to refine: the lower block's low-pass and the" + "\r\n" +
            "upper block's high-pass. Everything else in both chains stays.");
        numericMinHz.ApplyToolTip(
            toolTip,
            "Corner frequencies the search may put the handover at." + "\r\n" +
            "Wider costs candidates; half an octave each way is the default.");
        numericMaxHz.ApplyToolTip(toolTip, "See the lower bound.");
        foreach (ThemedComboBox window in new[] { comboBoxMinSlope, comboBoxMaxSlope })
        {
            toolTip.SetToolTip(
                window,
                "Electrical slopes the search may use when it is tuning for the\r\n" +
                "best summation: narrow it to hold the junction near a steepness\r\n" +
                "you want. An acoustic goal states the answer instead, so the\r\n" +
                "window is left out of that mode altogether.");
        }

        toolTip.SetToolTip(
            checkBoxIndependentSlopes,
            "Let the two sides take different slopes. The search then costs" + "\r\n" +
            "slopes squared per corner, and asymmetric pairs are rarely" + "\r\n" +
            "what a crossover wants.");
        foreach (CheckBox family in FamilyBoxes)
        {
            toolTip.SetToolTip(
                family,
                "Filter families the search may use. Only what your processor" + "\r\n" +
                "can run belongs here; the tune writes one of these into both" + "\r\n" +
                "blocks.");
        }
        toolTip.SetToolTip(
            comboBoxGoalFamily,
            "Optional: the ACOUSTIC crossover this junction should add up to," + "\r\n" +
            "driver and filter together. The search then prefers the filter" + "\r\n" +
            "that lands on it, but only among candidates the summation calls" + "\r\n" +
            "equivalent — the sum stays the judge.");
        toolTip.SetToolTip(
            buttonRun,
            "Read every allowed filter on this junction and report what each" + "\r\n" +
            "would sum to. Nothing is written until Apply.");
        toolTip.SetToolTip(
            buttonApply,
            "Write the winning crossover into both sides of both blocks, as" + "\r\n" +
            "one undo step.");
    }
}
