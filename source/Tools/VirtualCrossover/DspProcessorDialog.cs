using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Names the processor a Virtual DSP project targets (catalog model or Custom); the choice is a
/// <see cref="DspProcessorSession"/>. Filters are built at its rate, not the measurement's, so bilinear warping is the
/// device's own (see <see cref="PreparedDspResponse"/>).</summary>
internal sealed partial class DspProcessorDialog : Form
{
    private const int MaximumNotesLength = DspProcessorSession.MaximumNotesLength;

    private static readonly object CustomItem = new();

    // "Follow the measurements" is kept apart from fixed rates: a stated 48 kHz stays 48 kHz when measurements change.
    private static readonly object FollowItem = new();

    private readonly DspProcessorSession choice;
    private bool presenting;

    public DspProcessorDialog(DspProcessorSession choice)
    {
        this.choice = choice ?? throw new ArgumentNullException(nameof(choice));
        InitializeComponent();
        AcceptButton = buttonOk;
        CancelButton = buttonCancel;

        comboBoxModel.FormattingEnabled = true;
        comboBoxModel.Format += (_, args) =>
        {
            if (ReferenceEquals(args.ListItem, CustomItem))
            {
                args.Value = "Custom";
            }
        };
        comboBoxModel.Items.Add(CustomItem);
        foreach (DspProcessorPreset preset in DspProcessorCatalog.Presets)
        {
            comboBoxModel.Items.Add(preset);
        }

        comboBoxSampleRate.FormattingEnabled = true;
        comboBoxSampleRate.Format += (_, args) =>
        {
            if (ReferenceEquals(args.ListItem, FollowItem))
            {
                args.Value = choice.MeasurementSampleRateHz > 0
                    ? $"Follow measurements ({choice.MeasurementSampleRateHz / 1000.0:0.###} kHz)"
                    : "Follow measurements";
            }
            else if (args.ListItem is int rate)
            {
                args.Value = $"{rate / 1000.0:0.###} kHz";
            }
        };
        comboBoxSampleRate.Items.Add(FollowItem);

        comboBoxQConvention.FormattingEnabled = true;
        comboBoxQConvention.Format += (_, args) =>
        {
            if (args.ListItem is PeqQConvention convention)
            {
                args.Value = PeqQConventions.Describe(convention);
            }
        };
        foreach (PeqQConvention convention in DspProcessorCatalog.SelectableQConventions)
        {
            comboBoxQConvention.Items.Add(convention);
        }

        textBoxNotes.Text = choice.Notes ?? string.Empty;
        Present();

        comboBoxModel.SelectedIndexChanged += (_, _) =>
            Edit(() => choice.SelectModel(comboBoxModel.SelectedItem as DspProcessorPreset));
        comboBoxSampleRate.SelectedIndexChanged += (_, _) =>
            Edit(() => choice.SelectSampleRate(comboBoxSampleRate.SelectedItem as int?));
        comboBoxQConvention.SelectedIndexChanged += (_, _) => Edit(() =>
        {
            if (comboBoxQConvention.SelectedItem is PeqQConvention convention)
            {
                choice.SelectQConvention(convention);
            }
        });
        checkBoxPhaseControl.CheckedChanged += (_, _) => Edit(() => choice.SetPhaseControl(checkBoxPhaseControl.Checked));
        checkBoxFirFilters.CheckedChanged += (_, _) => Edit(() => choice.SetFirFilters(checkBoxFirFilters.Checked));
        textBoxNotes.TextChanged += (_, _) =>
            choice.Notes = string.IsNullOrWhiteSpace(textBoxNotes.Text) ? null : textBoxNotes.Text;
    }

    private void Edit(Action change)
    {
        if (presenting)
        {
            return;
        }

        change();
        Present();
    }

    private void Present()
    {
        presenting = true;
        try
        {
            comboBoxModel.SelectedItem = (object?)choice.Model ?? CustomItem;
            foreach (int rate in choice.SampleRates.Where(rate => !comboBoxSampleRate.Items.Contains(rate)))
            {
                comboBoxSampleRate.Items.Add(rate);
            }

            comboBoxSampleRate.SelectedItem = (object?)choice.SampleRate ?? FollowItem;
            comboBoxQConvention.SelectedItem = choice.QConvention;
            checkBoxPhaseControl.Checked = choice.PhaseControl;
            checkBoxFirFilters.Checked = choice.FirFilters;
            bool custom = choice.CustomFields;
            comboBoxSampleRate.Enabled = custom;
            comboBoxQConvention.Enabled = custom;
            UiStyle.SetTextEnabledLook(labelSampleRate, custom);
            UiStyle.SetTextEnabledLook(labelQConvention, custom);
            labelStatus.Text = DspProcessorStatus.Text(choice);
        }
        finally
        {
            presenting = false;
        }
    }
}
