using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// The crossover wizard: shows each participating channel with its detected
/// usable band and driver type, lets the user confirm or override the types,
/// and previews the resulting proposal (LR24 splits at the level-aligned curve
/// intersections, cut-only gains) live. Apply hands the proposal back to the
/// panel; nothing is written until then.
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
    private bool initialized;

    public VirtualCrossoverAutoSetupDialog()
    {
        InitializeComponent();
        AcceptButton = buttonApply;
        CancelButton = buttonCancel;
        buttonApply.Click += ApplyClick;
        toolTip.SetToolTip(
            labelPreview,
            "The proposal that Apply writes into the channels:\r\n" +
            "Linkwitz-Riley 24 dB/oct splits where the level-aligned\r\n" +
            "responses intersect, and cut-only gains that level the\r\n" +
            "channels to the quietest one.");
    }

    /// <summary>The proposal computed on Apply, in the same order as the Init channels.</summary>
    public IReadOnlyList<CrossoverProposal>? Result { get; private set; }

    /// <summary>
    /// Seeds one row per participating channel: the display name, its accent
    /// color, the smoothed raw magnitude curve, and the auto-detected band/type.
    /// </summary>
    public void Init(
        IReadOnlyList<(string Name, Color Accent, IReadOnlyList<SignalPoint> MagnitudeDb,
            DriverBandEstimate Band)> channels)
    {
        var rowControls = new[]
        {
            (labelName1, labelBand1, comboType1),
            (labelName2, labelBand2, comboType2),
            (labelName3, labelBand3, comboType3)
        };

        for (int i = 0; i < rowControls.Length; i++)
        {
            (Label nameLabel, Label bandLabel, DarkComboBox typeComboBox) = rowControls[i];
            bool used = i < channels.Count;
            nameLabel.Visible = used;
            bandLabel.Visible = used;
            typeComboBox.Visible = used;
            if (!used)
            {
                continue;
            }

            (string name, Color accent, IReadOnlyList<SignalPoint> magnitude,
                DriverBandEstimate band) = channels[i];
            nameLabel.Text = name;
            nameLabel.ForeColor = accent;
            bandLabel.Text = $"{FormatHz(band.LowHz)} – {FormatHz(band.HighHz)}";

            typeComboBox.Items.AddRange(
            [
                DriverType.Woofer,
                DriverType.Midrange,
                DriverType.Tweeter
            ]);
            typeComboBox.SelectedItem = band.SuggestedType;
            typeComboBox.SelectedIndexChanged += (_, _) => UpdatePreview();

            rows.Add(new ChannelRow(name, magnitude, nameLabel, bandLabel, typeComboBox));
        }

        initialized = true;
        UpdatePreview();
    }

    private DriverType TypeOf(ChannelRow row) =>
        row.TypeComboBox.SelectedItem is DriverType type ? type : DriverType.Woofer;

    private IReadOnlyList<CrossoverProposal>? TryPropose()
    {
        var sources = rows
            .Select(row => new AutoSetupSource(row.MagnitudeDb, TypeOf(row)))
            .ToList();
        try
        {
            return CrossoverAutoSetup.Propose(sources);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private void UpdatePreview()
    {
        if (!initialized)
        {
            return;
        }

        IReadOnlyList<CrossoverProposal>? proposals = TryPropose();
        buttonApply.Enabled = proposals != null;
        if (proposals == null)
        {
            labelPreview.Text = "Assign a distinct driver type to every channel.";
            return;
        }

        labelPreview.Text = string.Join(
            Environment.NewLine,
            rows.Select((row, index) => FormatProposal(row, proposals[index])));
    }

    private static string FormatProposal(ChannelRow row, CrossoverProposal proposal)
    {
        var parts = new List<string>();
        if (proposal.HighPassEdge is { } highPass)
        {
            parts.Add($"HP {FormatHz(highPass.FrequencyHz)} LR{highPass.SlopeDbPerOctave}");
        }
        if (proposal.LowPassEdge is { } lowPass)
        {
            parts.Add($"LP {FormatHz(lowPass.FrequencyHz)} LR{lowPass.SlopeDbPerOctave}");
        }
        parts.Add($"gain {proposal.GainDb:0.0} dB");
        return $"{row.Name}:  {string.Join(",  ", parts)}";
    }

    private void ApplyClick(object? sender, EventArgs e)
    {
        Result = TryPropose();
        if (Result == null)
        {
            DialogResult = DialogResult.None;
            System.Media.SystemSounds.Beep.Play();
        }
    }

    private static string FormatHz(double frequencyHz) =>
        frequencyHz >= 1_000
            ? $"{frequencyHz / 1_000:0.##} kHz"
            : $"{frequencyHz:0} Hz";
}
