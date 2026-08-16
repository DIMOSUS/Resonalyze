using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// Asks which <see cref="PeqQConvention"/> a tuning sheet's Q column should be
/// stated in, right before the sheet is written. Virtual DSP exports a sheet for
/// whatever processor is being tuned, which is not necessarily the one the EQ
/// Wizard's "DSP Q" selector was last set for; taking that selector silently made
/// a sheet whose Q numbers belonged to another device, with nothing on screen to
/// say so. The selector still supplies the pre-selection, so the common case is
/// one confirming click.
/// </summary>
internal sealed partial class TuningSheetQConventionDialog : Form
{
    public TuningSheetQConventionDialog(PeqQConvention selected)
    {
        InitializeComponent();

        // The option texts come from the DSP layer rather than the designer, so
        // the dialog cannot drift from the descriptions printed on the sheet.
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

    /// <summary>The convention the sheet should be stated in.</summary>
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

    // The crib for the pick: what the convention does to a band's width, and which
    // processors are known to read Q that way — the question is usually settled by
    // recognising the device rather than by the maths. Both come from the DSP layer,
    // which is where the conversion itself lives.
    private void UpdateCheatSheet()
    {
        PeqQConvention convention = SelectedConvention;
        textCheatSheet.Text = string.Join(
            Environment.NewLine + Environment.NewLine,
            PeqQConventions.DescribeBandwidth(convention),
            "Processors reading Q this way: " + PeqQConventions.DescribeDevices(convention));
        // A convention swapped while the box was scrolled would otherwise show the
        // new text from the old scroll position — often past its end.
        textCheatSheet.SelectionStart = 0;
        textCheatSheet.ScrollToCaret();
    }
}
