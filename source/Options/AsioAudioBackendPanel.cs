using System.Windows.Forms;

namespace Resonalyze.Options
{
    public partial class AsioAudioBackendPanel : UserControl
    {
        public AsioAudioBackendPanel()
        {
            InitializeComponent();
        }

        internal Label LabelAsioDriver => labelAsioDriver;

        internal DarkComboBox ComboBoxAsioDriver => comboBoxAsioDriver;

        internal Label LabelAsioInputChannel => labelAsioInputChannel;

        internal DarkComboBox ComboBoxAsioInputChannel => comboBoxAsioInputChannel;

        internal Label LabelAsioOutputChannel => labelAsioOutputChannel;

        internal DarkComboBox ComboBoxAsioOutputChannel => comboBoxAsioOutputChannel;

        internal Label LabelAsioLoopbackChannel => labelAsioLoopbackChannel;

        internal DarkComboBox ComboBoxAsioLoopbackChannel => comboBoxAsioLoopbackChannel;

        internal Label LabelAsioSampleRate => labelAsioSampleRate;

        internal Label LabelAsioSampleRateStatus => labelAsioSampleRateStatus;

        internal Label LabelAsioPlaybackLatency => labelAsioPlaybackLatency;

        internal Label LabelAsioPlaybackLatencyValue => labelAsioPlaybackLatencyValue;

        internal Button ButtonAsioInputProbe => buttonAsioInputProbe;

        internal Button ButtonAsioControlPanel => buttonAsioControlPanel;
    }
}
