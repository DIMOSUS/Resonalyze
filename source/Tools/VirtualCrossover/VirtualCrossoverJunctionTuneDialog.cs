using Resonalyze.Dsp;

namespace Resonalyze;

internal sealed record JunctionTuneRequest(
    int JunctionIndex,
    double MinHz,
    double MaxHz,
    IReadOnlyList<CrossoverFilterFamily> Families,
    IReadOnlyList<int> Slopes,
    bool IndependentSlopes,
    JunctionAcousticTarget? AcousticGoal,
    bool SplitCorners,
    double SumSlackDb = CrossoverJunctionTuner.DefaultSumSlackDb);

/// <param name="CornerHz">Where the junction is crossed now: a remembered window without it gives way to the default.</param>
internal sealed record JunctionTuneDefaults(
    double MinHz,
    double MaxHz,
    IReadOnlyList<CrossoverFilterFamily> Families,
    JunctionAcousticTarget? Goal,
    double? CornerHz = null);

/// <param name="Recommended">Whether Apply writes what the search advises; it is offered either way.</param>
internal sealed record JunctionTuneOutcome(
    IReadOnlyList<JunctionTuneLine> Report,
    bool CanApply,
    string Status,
    bool Refused,
    bool Recommended = false);

/// <summary>States one junction's tune and shows the answer; the search runs in the panel. See
/// docs/tech/crossover-auto-setup.md#junction-tuner.</summary>
internal sealed partial class VirtualCrossoverJunctionTuneDialog : Form
{
    private const string Nothing = "—";

    /// <summary>What the goal may cost against the best sum; measured in docs/tech/crossover-auto-setup.md#measured-on-the-battery.</summary>
    public const double DefaultSumBudgetDb = 1.0;

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

    /// <summary>Corner windows as left, per junction label.</summary>
    private readonly Dictionary<string, (decimal Min, decimal Max)> windows = new(StringComparer.Ordinal);

    private string? shownJunction;

    /// <summary>Only the first junction shown, with nothing remembered, opens on its own families and goal.</summary>
    private bool useJunctionDefaults = true;

    /// <summary>Bumped by every change to the question: the boxes stay live while the search runs.</summary>
    private int question;

    private CheckBox[] FamilyBoxes => [checkButterworth, checkLinkwitzRiley, checkBessel];

    /// <summary>As the crossover wizard offers them: from 12 dB/oct.</summary>
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

        comboBoxMinSlope.SelectedItem = SelectableSlopes[0];
        comboBoxMaxSlope.SelectedItem = SelectableSlopes[^1];
        comboBoxGoalFamily.SelectedIndexChanged += (_, _) => FillGoalSlopes();
        comboBoxJunction.SelectedIndexChanged += (_, _) => PresentJunction();
        radioSummation.CheckedChanged += (_, _) => PresentMode();
        radioAcoustic.CheckedChanged += (_, _) => PresentMode();
        // Every input retires the answer, so Apply never stands for a question nobody asked.
        numericMinHz.ValueChanged += (_, _) => InvalidateResult(Again);
        numericMaxHz.ValueChanged += (_, _) => InvalidateResult(Again);
        comboBoxGoalSlope.SelectedIndexChanged += (_, _) => InvalidateResult(Again);
        checkBoxIndependentSlopes.CheckedChanged += (_, _) => InvalidateResult(Again);
        foreach (CheckBox family in FamilyBoxes)
        {
            family.CheckedChanged += (_, _) => InvalidateResult(Again);
        }
        checkBoxSplitCorners.CheckedChanged += (_, _) => InvalidateResult(Again);
        numericSumBudget.ValueChanged += (_, _) => InvalidateResult(Again);
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
        PresentMode();
    }

    private void PresentMode()
    {
        bool acoustic = radioAcoustic.Checked;
        comboBoxGoalFamily.Enabled = acoustic;
        comboBoxGoalSlope.Enabled = acoustic && comboBoxGoalFamily.SelectedItem is CrossoverFamilyChoice;
        // The slope window is the summation mode's, the budget the acoustic mode's.
        comboBoxMinSlope.Enabled = !acoustic;
        comboBoxMaxSlope.Enabled = !acoustic;
        UiStyle.SetTextEnabledLook(labelSlopes, !acoustic);
        UiStyle.SetTextEnabledLook(labelSlopeTo, !acoustic);
        numericSumBudget.Enabled = acoustic;
        UiStyle.SetTextEnabledLook(labelSumBudget, acoustic);
        UiStyle.SetTextEnabledLook(labelSumBudgetUnit, acoustic);
        if (acoustic && comboBoxGoalFamily.SelectedItem is not CrossoverFamilyChoice)
        {
            comboBoxGoalFamily.SelectedItem = CrossoverFamilyChoice.Offered
                .First(choice => choice.Value == CrossoverFilterFamily.LinkwitzRiley);
        }

        labelGoalHint.Text = acoustic
            ? "Driver and filter together, which is steeper than the filter alone. Chosen among filters within " +
              "the budget of the best sum."
            : "Every allowed filter is read on the coherent sum at this junction; the one that sums best wins.";
        InvalidateResult(Again);
    }

    /// <summary>Null until a search has landed.</summary>
    public JunctionTuneRequest? Result { get; private set; }

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
        defaultsFor = defaults ?? throw new ArgumentNullException(nameof(defaults));
        runner = search ?? throw new ArgumentNullException(nameof(search));
        buttonUndo.Enabled = undoable != null;
        toolTip.SetToolTip(
            buttonUndo,
            (undoable == null ? "Nothing applied here to undo." : $"The last Apply was for {undoable}.") + "\r\n" +
            "Undo puts every channel back exactly as it was before it:" + "\r\n" +
            "crossovers, goals and anything changed since. One step; gone" + "\r\n" +
            "once a session is loaded.");
        comboBoxJunction.Items.Clear();
        foreach (string junction in junctions)
        {
            comboBoxJunction.Items.Add(junction);
        }

        if (remembered != null)
        {
            Restore(remembered);
        }

        if (junctions.Count > 0)
        {
            int last = remembered?.Junction is { } label ? IndexOf(junctions, label) : -1;
            comboBoxJunction.SelectedIndex = Math.Max(0, last);
        }
        else
        {
            buttonRun.Enabled = false;
            labelStatus.Text = "This view has no junction with two measured blocks.";
            labelStatus.ForeColor = UiPalette.Warning;
        }
    }

    /// <summary>Switching junction changes only its corner window; the rest of the question stays as set.</summary>
    private void PresentJunction()
    {
        if (defaultsFor is not { } defaults || comboBoxJunction.SelectedIndex < 0)
        {
            return;
        }

        KeepShownWindow();
        string label = comboBoxJunction.Items[comboBoxJunction.SelectedIndex]?.ToString() ?? string.Empty;
        JunctionTuneDefaults opening = defaults(comboBoxJunction.SelectedIndex);
        (decimal min, decimal max) = windows.TryGetValue(label, out (decimal Min, decimal Max) kept) &&
            (opening.CornerHz is not { } corner || ((double)kept.Min <= corner && corner <= (double)kept.Max))
                ? kept
                : (numericMinHz.ClampValue(opening.MinHz), numericMaxHz.ClampValue(opening.MaxHz));
        numericMinHz.Value = numericMinHz.ClampValue((double)min);
        numericMaxHz.Value = numericMaxHz.ClampValue((double)max);
        shownJunction = label;
        if (useJunctionDefaults)
        {
            useJunctionDefaults = false;
            checkButterworth.Checked = opening.Families.Contains(CrossoverFilterFamily.Butterworth);
            checkLinkwitzRiley.Checked = opening.Families.Contains(CrossoverFilterFamily.LinkwitzRiley);
            checkBessel.Checked = opening.Families.Contains(CrossoverFilterFamily.Bessel);
            if (opening.Goal is { } asked)
            {
                ShowGoal(asked);
                radioAcoustic.Checked = true;
            }
        }

        textBoxReport.Clear();
        InvalidateResult("Nothing searched yet.");
    }

    private void KeepShownWindow()
    {
        if (shownJunction != null)
        {
            windows[shownJunction] = (numericMinHz.Value, numericMaxHz.Value);
        }
    }

    private void ShowGoal(JunctionAcousticTarget goal)
    {
        comboBoxGoalFamily.SelectedItem = CrossoverFamilyChoice.Offered
            .FirstOrDefault(choice => choice.Value == goal.Family) ?? (object)Nothing;
        if (comboBoxGoalSlope.Items.Contains(goal.SlopeDbPerOctave))
        {
            comboBoxGoalSlope.SelectedItem = goal.SlopeDbPerOctave;
        }
    }

    /// <summary>Anything the menus no longer offer keeps its default.</summary>
    private void Restore(VirtualCrossoverJunctionTuneSettings remembered)
    {
        useJunctionDefaults = false;
        checkButterworth.Checked = remembered.Families.Contains(CrossoverFilterFamily.Butterworth);
        checkLinkwitzRiley.Checked = remembered.Families.Contains(CrossoverFilterFamily.LinkwitzRiley);
        checkBessel.Checked = remembered.Families.Contains(CrossoverFilterFamily.Bessel);
        checkBoxIndependentSlopes.Checked = remembered.IndependentSlopes;
        checkBoxSplitCorners.Checked = remembered.SplitCorners;
        if (remembered.MinSlopeDbPerOctave is { } low && SelectableSlopes.Contains(low))
        {
            comboBoxMinSlope.SelectedItem = low;
        }
        if (remembered.MaxSlopeDbPerOctave is { } high && SelectableSlopes.Contains(high))
        {
            comboBoxMaxSlope.SelectedItem = high;
        }
        if (remembered.Goal is { } goal)
        {
            ShowGoal(goal);
        }

        numericSumBudget.Value = numericSumBudget.ClampValue(remembered.SumBudgetDb ?? DefaultSumBudgetDb);
        (remembered.Acoustic ? radioAcoustic : radioSummation).Checked = true;
        foreach ((string label, double[] window) in remembered.Windows)
        {
            // Clamped as a double: a hand-edited file may hold a figure no decimal can.
            if (window is [var min, var max])
            {
                windows[label] = (numericMinHz.ClampValue(min), numericMaxHz.ClampValue(max));
            }
        }
    }

    public VirtualCrossoverJunctionTuneSettings Remembered()
    {
        KeepShownWindow();
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

        return new VirtualCrossoverJunctionTuneSettings
        {
            Junction = shownJunction,
            Families = families,
            IndependentSlopes = checkBoxIndependentSlopes.Checked,
            SplitCorners = checkBoxSplitCorners.Checked,
            Acoustic = radioAcoustic.Checked,
            MinSlopeDbPerOctave = comboBoxMinSlope.SelectedItem as int?,
            MaxSlopeDbPerOctave = comboBoxMaxSlope.SelectedItem as int?,
            SumBudgetDb = (double)numericSumBudget.Value,
            Goal = comboBoxGoalFamily.SelectedItem is CrossoverFamilyChoice family &&
                comboBoxGoalSlope.SelectedItem is int slope
                    ? new JunctionAcousticTarget(family.Value, slope)
                    : null,
            Windows = windows.ToDictionary(
                pair => pair.Key,
                pair => new[] { (double)pair.Value.Min, (double)pair.Value.Max },
                StringComparer.Ordinal)
        };
    }

    private static int IndexOf(IReadOnlyList<string> junctions, string label)
    {
        for (int i = 0; i < junctions.Count; i++)
        {
            if (string.Equals(junctions[i], label, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
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
        // Empty means every slope the families have.
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
            checkBoxSplitCorners.Checked,
            (double)numericSumBudget.Value);

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
                // The question moved while the search ran; its handler has said so on the status line.
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
