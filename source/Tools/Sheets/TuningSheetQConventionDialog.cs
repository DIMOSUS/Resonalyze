using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Asks the Q convention per export: the EQ Wizard's DSP Q selector may belong to another device; it only pre-selects.</summary>
internal sealed partial class TuningSheetQConventionDialog : Form
{
    public TuningSheetQConventionDialog(PeqQConvention selected)
    {
        InitializeComponent();

        // Texts from the DSP layer so the dialog matches the sheet's descriptions.
        radioRbj.Text = PeqQConventions.Describe(PeqQConvention.Rbj);
        radioSymmetric.Text = PeqQConventions.Describe(PeqQConvention.Symmetric);
        radioClassic.Text = PeqQConventions.Describe(PeqQConvention.Classic);

        foreach (RadioButton radio in Controls.OfType<RadioButton>())
        {
            radio.CheckedChanged += (_, _) => UpdateCheatSheet();
        }

        AcceptButton = buttonExport;
        CancelButton = buttonCancel;
        SelectedConvention = selected;
        UpdateCheatSheet();
    }

    public PeqQConvention SelectedConvention
    {
        get
        {
            if (radioSymmetric.Checked)
            {
                return PeqQConvention.Symmetric;
            }

            return radioClassic.Checked ? PeqQConvention.Classic : PeqQConvention.Rbj;
        }
        private set
        {
            radioSymmetric.Checked = value == PeqQConvention.Symmetric;
            radioClassic.Checked = value == PeqQConvention.Classic;
            radioRbj.Checked = value is not PeqQConvention.Symmetric
                and not PeqQConvention.Classic;
        }
    }

    private void UpdateCheatSheet()
    {
        PeqQConvention convention = SelectedConvention;
        textCheatSheet.Text = string.Join(
            Environment.NewLine + Environment.NewLine,
            PeqQConventions.DescribeBandwidth(convention),
            "Processors reading Q this way: " + PeqQConventions.DescribeDevices(convention));
        // Otherwise new text shows from the old scroll position, often past its end.
        textCheatSheet.SelectionStart = 0;
        textCheatSheet.ScrollToCaret();
    }
}
