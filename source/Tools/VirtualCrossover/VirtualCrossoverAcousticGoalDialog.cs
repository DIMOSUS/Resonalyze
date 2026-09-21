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
        // A goal describes an edge the IIR crossover runs, the only one Auto Tune reads it for. One kept for an edge
        // the channel does not run is shown greyed: neither edited as if it worked nor lost, since it applies again
        // once the channel runs that edge, as the edge's own corner and slope do.
        Present(
            comboBoxHighPassFamily, comboBoxHighPassSlope, labelHighPassElectrical,
            settings.AcousticHighPass, settings.RunsHighPass ? settings.HighPassEdge : null, "high-pass");
        Present(
            comboBoxLowPassFamily, comboBoxLowPassSlope, labelLowPassElectrical,
            settings.AcousticLowPass, settings.RunsLowPass ? settings.LowPassEdge : null, "low-pass");
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
        CrossoverEdge? running,
        string what)
    {
        // Before the family is chosen: choosing it fills the slopes, and they follow the family box's state.
        family.Enabled = running != null;
        family.Items.Clear();
        family.Items.Add(Nothing);
        foreach (CrossoverFamilyChoice item in CrossoverFamilyChoice.Offered)
        {
            family.Items.Add(item);
        }

        family.SelectedItem = goal is { } asked
            ? CrossoverFamilyChoice.Offered.FirstOrDefault(choice => choice.Value == asked.Family)
                ?? (object)Nothing
            : Nothing;
        if (goal is { } wanted)
        {
            slope.SelectedItem = wanted.SlopeDbPerOctave;
        }

        electricalLabel.Text = running is { } edge
            ? $"filter: {FamilyName(edge.Family)}{edge.SlopeDbPerOctave} at {edge.FrequencyHz:0.###} Hz"
            : goal != null
                ? $"no {what}: kept"
                : $"no {what}";
        toolTip.SetToolTip(
            electricalLabel,
            running == null
                ? $"The channel runs no {what}, so a goal for it describes" + "\r\n" +
                  "nothing and Auto Tune does not read it. One stated before" + "\r\n" +
                  "stays with the edge and applies again once the channel" + "\r\n" +
                  "runs it; Clear drops it."
                : "The electrical filter this channel runs." + "\r\n" +
                  "The goal is what it should SOUND like, with the" + "\r\n" +
                  "driver's own roll-off included.");
    }

    private static void FillSlopes(ThemedComboBox family, ThemedComboBox slope)
    {
        if (family.SelectedItem is not CrossoverFamilyChoice selected)
        {
            slope.Items.Clear();
            slope.Enabled = false;
            return;
        }

        int? kept = slope.SelectedItem as int?;
        slope.Enabled = family.Enabled;
        slope.Items.Clear();
        foreach (int supported in CrossoverFilter.SupportedSlopes(selected.Value))
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
        family.SelectedItem is CrossoverFamilyChoice selected && slope.SelectedItem is int chosen
            ? new JunctionAcousticTarget(selected.Value, chosen)
            : null;

    private static string FamilyName(CrossoverFilterFamily family) =>
        FirCrossoverDescription.FamilyName(family);
}
