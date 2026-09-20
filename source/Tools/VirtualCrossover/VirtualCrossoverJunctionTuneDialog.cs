using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>What one junction's tune was asked for: which junction, where its corner may sit, which filters are on
/// offer, and optionally the ACOUSTIC crossover it should add up to.</summary>
internal sealed record JunctionTuneRequest(
    int JunctionIndex,
    double MinHz,
    double MaxHz,
    IReadOnlyList<CrossoverFilterFamily> Families,
    bool IndependentSlopes,
    JunctionAcousticTarget? AcousticGoal);

/// <summary>What the search found, for the report and for Apply.</summary>
internal sealed record JunctionTuneOutcome(
    IReadOnlyList<string> Report,
    bool CanApply,
    string Status,
    bool Refused);

/// <summary>
/// Refines one junction of a finished tune: the lower channel's low-pass and the upper channel's high-pass, judged on
/// the coherent sum through the full chains, optionally against a stated acoustic slope. The search runs in the
/// panel; this dialog states the question and shows the answer. See docs/tech/crossover-auto-setup.md#junction-tuner.
/// </summary>
internal sealed partial class VirtualCrossoverJunctionTuneDialog : Form
{
    private const string Nothing = "—";

    private readonly WrappingToolTip toolTip = new()
    {
        AutoPopDelay = 20_000,
        InitialDelay = 400,
        ReshowDelay = 100
    };

    private Func<JunctionTuneRequest, Task<JunctionTuneOutcome>>? runner;
    private Func<int, (double MinHz, double MaxHz, JunctionAcousticTarget? Goal)>? defaultsFor;
    private bool running;

    public VirtualCrossoverJunctionTuneDialog()
    {
        InitializeComponent();
        foreach (CrossoverFilterFamily family in Enum.GetValues<CrossoverFilterFamily>())
        {
            checkedListFamilies.Items.Add(family);
        }

        comboBoxGoalFamily.Items.Add(Nothing);
        foreach (CrossoverFilterFamily family in Enum.GetValues<CrossoverFilterFamily>())
        {
            comboBoxGoalFamily.Items.Add(family);
        }

        comboBoxGoalFamily.SelectedItem = Nothing;
        comboBoxGoalSlope.Enabled = false;
        comboBoxGoalFamily.SelectedIndexChanged += (_, _) => FillGoalSlopes();
        comboBoxJunction.SelectedIndexChanged += (_, _) => PresentJunctionDefaults();
        // Every input retires the answer: Apply must never stand for a question nobody asked.
        const string Again = "The question changed — search again.";
        numericMinHz.ValueChanged += (_, _) => InvalidateResult(Again);
        numericMaxHz.ValueChanged += (_, _) => InvalidateResult(Again);
        comboBoxGoalSlope.SelectedIndexChanged += (_, _) => InvalidateResult(Again);
        checkBoxIndependentSlopes.CheckedChanged += (_, _) => InvalidateResult(Again);
        checkedListFamilies.ItemCheck += (_, _) => InvalidateResult(Again);
        buttonRun.Click += async (_, _) => await RunAsync().ConfigureAwait(true);
        buttonApply.Click += (_, _) =>
        {
            DialogResult = DialogResult.OK;
            Close();
        };
        Tips();
    }

    /// <summary>The request the Apply button stands for; null until a search has landed.</summary>
    public JunctionTuneRequest? Result { get; private set; }

    /// <param name="junctions">Labels as the panel's read-outs name them, lower to upper.</param>
    /// <param name="defaults">The corner window and the card's own acoustic goal for a junction, by index.</param>
    /// <param name="search">Runs the search off the UI thread; the dialog owns the await and the buttons.</param>
    public void Init(
        IReadOnlyList<string> junctions,
        Func<int, (double MinHz, double MaxHz, JunctionAcousticTarget? Goal)> defaults,
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

        (double minHz, double maxHz, JunctionAcousticTarget? goal) =
            defaults(comboBoxJunction.SelectedIndex);
        numericMinHz.Value = numericMinHz.ClampValue(minHz);
        numericMaxHz.Value = numericMaxHz.ClampValue(maxHz);
        // The card's own wish is what this junction already asks for, so the dialog opens on it.
        comboBoxGoalFamily.SelectedItem = goal is { } asked ? asked.Family : Nothing;
        if (goal is { } wanted)
        {
            comboBoxGoalSlope.SelectedItem = wanted.SlopeDbPerOctave;
        }

        InvalidateResult("Nothing searched yet.");
    }

    private void FillGoalSlopes()
    {
        if (comboBoxGoalFamily.SelectedItem is not CrossoverFilterFamily family)
        {
            comboBoxGoalSlope.Items.Clear();
            comboBoxGoalSlope.Enabled = false;
            InvalidateResult();
            return;
        }

        int? kept = comboBoxGoalSlope.SelectedItem as int?;
        comboBoxGoalSlope.Enabled = true;
        comboBoxGoalSlope.Items.Clear();
        foreach (int slope in CrossoverFilter.SupportedSlopes(family))
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
        foreach (object item in checkedListFamilies.CheckedItems)
        {
            if (item is CrossoverFilterFamily family)
            {
                families.Add(family);
            }
        }

        if (families.Count == 0)
        {
            labelStatus.Text = "Tick at least one filter family to search.";
            labelStatus.ForeColor = UiPalette.Warning;
            return;
        }

        var request = new JunctionTuneRequest(
            comboBoxJunction.SelectedIndex,
            (double)numericMinHz.Value,
            (double)numericMaxHz.Value,
            families,
            checkBoxIndependentSlopes.Checked,
            comboBoxGoalFamily.SelectedItem is CrossoverFilterFamily goalFamily &&
                comboBoxGoalSlope.SelectedItem is int goalSlope
                    ? new JunctionAcousticTarget(goalFamily, goalSlope)
                    : null);

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

            textBoxReport.Lines = outcome.Report.ToArray();
            labelStatus.Text = outcome.Status;
            labelStatus.ForeColor = outcome.Refused
                ? UiPalette.Error
                : outcome.CanApply ? UiPalette.Success : UiPalette.Warning;
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
        toolTip.SetToolTip(
            checkBoxIndependentSlopes,
            "Let the two sides take different slopes. The search then costs" + "\r\n" +
            "slopes squared per corner, and asymmetric pairs are rarely" + "\r\n" +
            "what a crossover wants.");
        toolTip.SetToolTip(
            checkedListFamilies,
            "Filter families on offer. Only what your processor can run" + "\r\n" +
            "belongs here; the tune writes one of these into both blocks.");
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
