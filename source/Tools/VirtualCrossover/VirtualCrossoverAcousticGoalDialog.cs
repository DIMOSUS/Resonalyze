using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// Reads and edits one channel's ACOUSTIC crossover goal: what driver and filter should add up to at each of its
/// edges. Only a family and a slope — the corner is always the electrical filter's, so the wish follows a corner
/// that moves and there is no hidden state to go stale. See docs/specs/acoustic-crossover-target.md.
/// </summary>
internal sealed partial class VirtualCrossoverAcousticGoalDialog : Form
{
    private const string Nothing = "—";

    private readonly WrappingToolTip toolTip = new()
    {
        AutoPopDelay = 20_000,
        InitialDelay = 400,
        ReshowDelay = 100
    };

    public VirtualCrossoverAcousticGoalDialog()
    {
        InitializeComponent();
        comboBoxHighPassFamily.SelectedIndexChanged += (_, _) => FillSlopes(
            comboBoxHighPassFamily, comboBoxHighPassSlope);
        comboBoxLowPassFamily.SelectedIndexChanged += (_, _) => FillSlopes(
            comboBoxLowPassFamily, comboBoxLowPassSlope);
        buttonClear.Click += (_, _) =>
        {
            comboBoxHighPassFamily.SelectedItem = Nothing;
            comboBoxLowPassFamily.SelectedItem = Nothing;
        };
        toolTip.SetToolTip(
            buttonClear,
            "Leave both edges unstated, so Auto Tune aims at the electrical" + "\r\n" +
            "filter as it does by default.");
    }

    /// <summary>The high-pass edge's goal as edited, or null for "nothing stated".</summary>
    public JunctionAcousticTarget? HighPassGoal { get; private set; }

    /// <inheritdoc cref="HighPassGoal"/>
    public JunctionAcousticTarget? LowPassGoal { get; private set; }

    /// <param name="settings">Read only: the dialog hands its answer back through the two properties.</param>
    /// <param name="channelName">Shown in the title, so a card's dialog is recognisable.</param>
    public void Init(VirtualCrossoverChannelSettings settings, string channelName)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Text = $"Acoustic crossover goal — channel {channelName}";
        CrossoverSpec electrical = settings.EffectiveCrossover;
        Present(
            comboBoxHighPassFamily, comboBoxHighPassSlope, labelHighPassElectrical,
            settings.AcousticHighPass, electrical.HighPassHz, electrical.HighPassEdge, "high-pass");
        Present(
            comboBoxLowPassFamily, comboBoxLowPassSlope, labelLowPassElectrical,
            settings.AcousticLowPass, electrical.LowPassHz, electrical.LowPassEdge, "low-pass");
        // The driver's own fall is what makes the two differ, and it is measured by the junction tune rather than
        // known here, so the note says where to look instead of inventing a number.
        labelNote.Text =
            "A driver's own roll-off adds to the filter, so the acoustic slope is the steeper of the two. " +
            "Tune junction measures what these drivers actually manage and says whether a stated slope is " +
            "reachable at all.";
    }

    private void Present(
        ThemedComboBox family,
        ThemedComboBox slope,
        Label electricalLabel,
        JunctionAcousticTarget? goal,
        double? electricalHz,
        CrossoverEdge? electricalEdge,
        string what)
    {
        family.Items.Clear();
        family.Items.Add(Nothing);
        foreach (CrossoverFilterFamily item in Enum.GetValues<CrossoverFilterFamily>())
        {
            family.Items.Add(item);
        }

        family.SelectedItem = goal is { } asked ? asked.Family : Nothing;
        if (goal is { } wanted)
        {
            slope.SelectedItem = wanted.SlopeDbPerOctave;
        }

        electricalLabel.Text = electricalHz is { } hz && electricalEdge is { } edge
            ? $"filter: {FamilyName(edge.Family)}{edge.SlopeDbPerOctave} at {hz:0.###} Hz"
            : $"no {what} on this channel";
        toolTip.SetToolTip(
            electricalLabel,
            electricalHz == null
                ? $"The channel runs no {what}, so a goal for it would describe nothing."
                : "The electrical filter this channel runs." + "\r\n" +
                  "The goal is what it should SOUND like, with the" + "\r\n" +
                  "driver's own roll-off included.");
    }

    private static void FillSlopes(ThemedComboBox family, ThemedComboBox slope)
    {
        if (family.SelectedItem is not CrossoverFilterFamily selected)
        {
            slope.Items.Clear();
            slope.Enabled = false;
            return;
        }

        int? kept = slope.SelectedItem as int?;
        slope.Enabled = true;
        slope.Items.Clear();
        foreach (int supported in CrossoverFilter.SupportedSlopes(selected))
        {
            slope.Items.Add(supported);
        }

        // Keep the slope across a family change where that family has it; the default is the middle of the list.
        slope.SelectedItem = kept is { } previous && slope.Items.Contains(previous)
            ? previous
            : slope.Items[Math.Min(1, slope.Items.Count - 1)];
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (DialogResult == DialogResult.OK)
        {
            HighPassGoal = Read(comboBoxHighPassFamily, comboBoxHighPassSlope);
            LowPassGoal = Read(comboBoxLowPassFamily, comboBoxLowPassSlope);
        }

        base.OnFormClosing(e);
    }

    private static JunctionAcousticTarget? Read(ThemedComboBox family, ThemedComboBox slope) =>
        family.SelectedItem is CrossoverFilterFamily selected && slope.SelectedItem is int chosen
            ? new JunctionAcousticTarget(selected, chosen)
            : null;

    private static string FamilyName(CrossoverFilterFamily family) => family switch
    {
        CrossoverFilterFamily.LinkwitzRiley => "LR",
        CrossoverFilterFamily.Butterworth => "BW",
        CrossoverFilterFamily.Bessel => "BE",
        _ => "CH"
    };
}
