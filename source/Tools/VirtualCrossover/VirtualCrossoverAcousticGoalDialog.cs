using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Edits one channel's acoustic crossover goal. See docs/tech/crossover-auto-setup.md#acoustic-slope-target.</summary>
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

    /// <summary>The goal the high-pass box states; read once the dialog is answered OK.</summary>
    public JunctionAcousticTarget? HighPassGoal => Read(comboBoxHighPassFamily, comboBoxHighPassSlope);

    public JunctionAcousticTarget? LowPassGoal => Read(comboBoxLowPassFamily, comboBoxLowPassSlope);

    public void Init(VirtualCrossoverChannelSettings settings, string channelName)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Text = $"Acoustic crossover goal — channel {channelName}";
        // A goal kept for an edge the channel does not run is shown greyed: not editable, not lost.
        Present(
            comboBoxHighPassFamily, comboBoxHighPassSlope, labelHighPassElectrical,
            settings.AcousticHighPass, settings.RunsHighPass ? settings.HighPassEdge : null, "high-pass");
        Present(
            comboBoxLowPassFamily, comboBoxLowPassSlope, labelLowPassElectrical,
            settings.AcousticLowPass, settings.RunsLowPass ? settings.LowPassEdge : null, "low-pass");
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
        // Before the family is chosen: the slopes it fills follow the family box's state.
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
        foreach (int supported in selected.GoalSlopes)
        {
            slope.Items.Add(supported);
        }

        slope.SelectedItem = selected.GoalSlope(kept);
    }

    private static JunctionAcousticTarget? Read(ThemedComboBox family, ThemedComboBox slope) =>
        family.SelectedItem is CrossoverFamilyChoice selected && slope.SelectedItem is int chosen
            ? new JunctionAcousticTarget(selected.Value, chosen)
            : null;

    private static string FamilyName(CrossoverFilterFamily family) =>
        FirCrossoverDescription.FamilyName(family);
}
