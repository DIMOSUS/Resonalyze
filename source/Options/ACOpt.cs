using Resonalyze.Dsp;

namespace Resonalyze.Options
{
    /// <summary>Binds the Autocorrelation curve's switch to an <see cref="ImpulseViewSettingsSession"/>.</summary>
    public partial class ACOpt : ModeSettingsForm
    {
        private readonly ImpulseViewSettingsSession session = new();

        public ACOpt()
        {
            InitializeComponent();
            Bind(checkBoxShowAutocorrelation, on => session.ShowAutocorrelation = on);
            toolTip.SetToolTip(
                checkBoxShowAutocorrelation,
                "Shows the autocorrelation curve.");
        }

        public void Init(ImpulseResponseOptions opt)
        {
            session.Load(opt, sampleRate: 0);
            Present();
        }

        public void SetOptions(ImpulseResponseOptions opt) => session.WriteAutocorrelation(opt);

        private protected override void PresentControls() =>
            checkBoxShowAutocorrelation.Checked = session.ShowAutocorrelation;
    }
}
