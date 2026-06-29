using System.Windows.Forms;
using Resonalyze.Dsp;

namespace Resonalyze.Options
{
    public partial class ACOpt : Form
    {
        private readonly ToolTip toolTip = new();

        public ACOpt()
        {
            InitializeComponent();
            InitializeToolTips();
            FormClosed += (_, _) => toolTip.Dispose();
        }

        public void Init(ExpSweepMeasurement expSweepMeasurement, ImpulseResponseOptions opt)
        {
            checkBoxShowAutocorrelation.Checked = opt.ShowAutocorrelation;
        }

        public void SetOptions(ImpulseResponseOptions opt)
        {
            opt.ShowAutocorrelation = checkBoxShowAutocorrelation.Checked;
        }

        private void InitializeToolTips()
        {
            toolTip.SetToolTip(
                checkBoxShowAutocorrelation,
                "Shows the autocorrelation curve.");
        }
    }
}
