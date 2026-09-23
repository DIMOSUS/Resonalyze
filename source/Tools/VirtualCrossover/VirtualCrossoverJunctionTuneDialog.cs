using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>States one junction's tune and shows the answer; the question is a <see cref="VirtualCrossoverJunctionTuneQuestion"/>
/// and the search runs in the panel. See docs/tech/crossover-auto-setup.md#junction-tuner.</summary>
internal sealed partial class VirtualCrossoverJunctionTuneDialog : Form
{
    private const string Nothing = "—";

    private readonly WrappingToolTip toolTip = new()
    {
        AutoPopDelay = 20_000,
        InitialDelay = 400,
        ReshowDelay = 100
    };

    private readonly VirtualCrossoverJunctionTuneQuestion question = new();
    private Func<JunctionTuneRequest, Task<JunctionTuneOutcome>>? runner;
    private IReadOnlyList<JunctionTuneLine>? shownReport;
    private bool presenting;

    private CheckBox[] FamilyBoxes => [checkButterworth, checkLinkwitzRiley, checkBessel];

    public VirtualCrossoverJunctionTuneDialog()
    {
        InitializeComponent();
        numericMinHz.ApplyFieldRange(VirtualCrossoverJunctionTuneQuestion.CornerRange);
        numericMaxHz.ApplyFieldRange(VirtualCrossoverJunctionTuneQuestion.CornerRange);
        numericSumBudget.ApplyFieldRange(VirtualCrossoverJunctionTuneQuestion.BudgetRange);
        comboBoxGoalFamily.Items.Add(Nothing);
        foreach (CrossoverFamilyChoice family in CrossoverFamilyChoice.Offered)
        {
            comboBoxGoalFamily.Items.Add(family);
        }

        foreach (ThemedComboBox window in new[] { comboBoxMinSlope, comboBoxMaxSlope })
        {
            window.Items.AddRange(VirtualCrossoverJunctionTuneQuestion.SelectableSlopes.Cast<object>().ToArray());
            window.SelectedIndexChanged += (_, _) => Edit(() => question.SetSlopeWindow(
                comboBoxMinSlope.SelectedItem as int? ?? question.MinSlope,
                comboBoxMaxSlope.SelectedItem as int? ?? question.MaxSlope));
        }

        comboBoxGoalFamily.SelectedIndexChanged += (_, _) =>
            Edit(() => question.SetGoalFamily(comboBoxGoalFamily.SelectedItem as CrossoverFamilyChoice));
        comboBoxJunction.SelectedIndexChanged += (_, _) => Edit(() => question.ShowJunction(comboBoxJunction.SelectedIndex));
        radioSummation.CheckedChanged += (_, _) => Edit(() => question.SetMode(radioAcoustic.Checked));
        radioAcoustic.CheckedChanged += (_, _) => Edit(() => question.SetMode(radioAcoustic.Checked));
        numericMinHz.ValueChanged += (_, _) => Edit(() => question.SetWindow(numericMinHz.Value, numericMaxHz.Value));
        numericMaxHz.ValueChanged += (_, _) => Edit(() => question.SetWindow(numericMinHz.Value, numericMaxHz.Value));
        comboBoxGoalSlope.SelectedIndexChanged += (_, _) => Edit(() =>
        {
            if (comboBoxGoalSlope.SelectedItem is int slope)
            {
                question.SetGoalSlope(slope);
            }
        });
        checkBoxIndependentSlopes.CheckedChanged += (_, _) =>
            Edit(() => question.SetIndependentSlopes(checkBoxIndependentSlopes.Checked));
        checkButterworth.CheckedChanged += (_, _) =>
            Edit(() => question.SetFamily(CrossoverFilterFamily.Butterworth, checkButterworth.Checked));
        checkLinkwitzRiley.CheckedChanged += (_, _) =>
            Edit(() => question.SetFamily(CrossoverFilterFamily.LinkwitzRiley, checkLinkwitzRiley.Checked));
        checkBessel.CheckedChanged += (_, _) =>
            Edit(() => question.SetFamily(CrossoverFilterFamily.Bessel, checkBessel.Checked));
        checkBoxSplitCorners.CheckedChanged += (_, _) => Edit(() => question.SetSplitCorners(checkBoxSplitCorners.Checked));
        numericSumBudget.ValueChanged += (_, _) => Edit(() => question.SetSumBudget(numericSumBudget.Value));
        buttonRun.Click += async (_, _) => await RunAsync().ConfigureAwait(true);
        buttonApply.Click += (_, _) =>
        {
            DialogResult = DialogResult.OK;
            Close();
        };
        buttonUndo.Click += (_, _) =>
        {
            UndoRequested = true;
            DialogResult = DialogResult.Cancel;
            Close();
        };
        Tips();
        Present();
    }

    /// <summary>Null until a search has landed.</summary>
    public JunctionTuneRequest? Result => question.Result;

    public bool UndoRequested { get; private set; }

    /// <param name="undoable">The junction the last Apply was for, while it can be undone.</param>
    public void Init(
        IReadOnlyList<string> junctions,
        Func<int, JunctionTuneDefaults> defaults,
        Func<JunctionTuneRequest, Task<JunctionTuneOutcome>> search,
        VirtualCrossoverJunctionTuneSettings? remembered = null,
        string? undoable = null)
    {
        ArgumentNullException.ThrowIfNull(junctions);
        ArgumentNullException.ThrowIfNull(defaults);
        runner = search ?? throw new ArgumentNullException(nameof(search));
        buttonUndo.Enabled = undoable != null;
        toolTip.SetToolTip(
            buttonUndo,
            (undoable == null ? "Nothing applied here to undo." : $"The last Apply was for {undoable}.") + "\r\n" +
            "Undo puts every channel back exactly as it was before it:" + "\r\n" +
            "crossovers, goals and anything changed since. One step; gone" + "\r\n" +
            "once a session is loaded.");
        presenting = true;
        try
        {
            comboBoxJunction.Items.Clear();
            foreach (string junction in junctions)
            {
                comboBoxJunction.Items.Add(junction);
            }
        }
        finally
        {
            presenting = false;
        }

        question.Open(junctions, defaults, remembered);
        Present();
    }

    public VirtualCrossoverJunctionTuneSettings Remembered() => question.Remembered();

    private void Edit(Action change)
    {
        if (presenting)
        {
            return;
        }

        change();
        Present();
    }

    // Writes the question back into the controls without raising their handlers.
    private void Present()
    {
        presenting = true;
        try
        {
            bool acoustic = question.Acoustic;
            bool searching = question.Searching;
            comboBoxJunction.SelectedIndex = question.JunctionIndex;
            numericMinHz.Value = question.MinHz;
            numericMaxHz.Value = question.MaxHz;
            checkButterworth.Checked = question.Butterworth;
            checkLinkwitzRiley.Checked = question.LinkwitzRiley;
            checkBessel.Checked = question.Bessel;
            checkBoxIndependentSlopes.Checked = question.IndependentSlopes;
            checkBoxSplitCorners.Checked = question.SplitCorners;
            radioAcoustic.Checked = acoustic;
            radioSummation.Checked = !acoustic;
            comboBoxMinSlope.SelectedItem = question.MinSlope;
            comboBoxMaxSlope.SelectedItem = question.MaxSlope;
            numericSumBudget.Value = question.SumBudget;
            comboBoxGoalFamily.SelectedItem = (object?)question.GoalFamily ?? Nothing;
            if (!comboBoxGoalSlope.Items.Cast<int>().SequenceEqual(question.GoalSlopes))
            {
                comboBoxGoalSlope.Items.Clear();
                comboBoxGoalSlope.Items.AddRange(question.GoalSlopes.Cast<object>().ToArray());
            }

            comboBoxGoalSlope.SelectedItem = question.GoalSlope;
            comboBoxGoalFamily.Enabled = acoustic;
            comboBoxGoalSlope.Enabled = acoustic && question.GoalFamily != null;
            // The slope window is the summation mode's, the budget the acoustic mode's.
            comboBoxMinSlope.Enabled = !acoustic;
            comboBoxMaxSlope.Enabled = !acoustic;
            UiStyle.SetTextEnabledLook(labelSlopes, !acoustic);
            UiStyle.SetTextEnabledLook(labelSlopeTo, !acoustic);
            numericSumBudget.Enabled = acoustic;
            UiStyle.SetTextEnabledLook(labelSumBudget, acoustic);
            UiStyle.SetTextEnabledLook(labelSumBudgetUnit, acoustic);
            labelGoalHint.Text = acoustic
                ? "Driver and filter together, which is steeper than the filter alone. Chosen among filters within " +
                  "the budget of the best sum."
                : "Every allowed filter is read on the coherent sum at this junction; the one that sums best wins.";
            labelStatus.Text = question.Status;
            labelStatus.ForeColor = question.StatusTone switch
            {
                JunctionTuneStatusTone.Warning => UiPalette.Warning,
                JunctionTuneStatusTone.Error => UiPalette.Error,
                JunctionTuneStatusTone.Success => UiPalette.Success,
                _ => UiPalette.TextMuted
            };
            if (!ReferenceEquals(shownReport, question.Report))
            {
                shownReport = question.Report;
                ShowReport(question.Report);
            }

            buttonRun.Enabled = question.Junctions.Count > 0 && !searching;
            buttonApply.Enabled = question.Result != null && !searching;
            buttonCancel.Enabled = !searching;
            UseWaitCursor = searching;
        }
        finally
        {
            presenting = false;
        }
    }

    private async Task RunAsync()
    {
        if (runner is not { } search)
        {
            return;
        }

        if (question.Ask() is not { } request)
        {
            Present();
            return;
        }

        int asked = question.BeginSearch();
        Present();
        try
        {
            JunctionTuneOutcome outcome = await search(request).ConfigureAwait(true);
            if (IsDisposed)
            {
                return;
            }

            question.Land(asked, request, outcome);
        }
        finally
        {
            question.EndSearch();
            if (!IsDisposed)
            {
                Present();
            }
        }
    }

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
        // Closing mid-search would leave the panel's await holding a disposed dialog.
        if (question.Searching)
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
            "Let the two sides take different slopes. On by default: the" + "\r\n" +
            "drivers' own falls rarely match, and the search then costs" + "\r\n" +
            "slopes squared per corner.");
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
            "The ACOUSTIC crossover this junction should add up to, driver" + "\r\n" +
            "and filter together. The search prefers the filter that lands" + "\r\n" +
            "on it among candidates within the budget of the best sum.");
        numericSumBudget.ApplyToolTip(
            toolTip,
            "How much summation score the goal may cost against the best" + "\r\n" +
            "candidate, every one read after re-aligning. 0.2 keeps the sum;" + "\r\n" +
            "1.0 lets a slope that lands on the goal in at a moderate price.");
        toolTip.SetToolTip(
            buttonRun,
            "Read every allowed filter on this junction and report what each" + "\r\n" +
            "would sum to. Nothing is written until Apply.");
        toolTip.SetToolTip(
            buttonApply,
            "Write the found crossover into both sides of both blocks, and" + "\r\n" +
            "a stated goal onto the cards. Undo last Apply puts them back.");
    }
}
