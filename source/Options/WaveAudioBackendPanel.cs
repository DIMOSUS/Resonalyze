using System.Windows.Forms;

namespace Resonalyze.Options
{
    public partial class WaveAudioBackendPanel : UserControl
    {
        public WaveAudioBackendPanel()
        {
            InitializeComponent();
        }

        internal Label LabelPlaybackDevice => labelPlaybackDevice;

        internal DarkComboBox ComboBoxPlaybackDevice => comboBoxPlaybackDevice;

        internal Label LabelRecordingDevice => labelRecordingDevice;

        internal DarkComboBox ComboBoxRecordingDevice => comboBoxRecordingDevice;

        internal Label LabelWaveLoopbackDevice => labelWaveLoopbackDevice;

        internal DarkComboBox ComboBoxWaveLoopbackDevice => comboBoxWaveLoopbackDevice;

        internal Label LabelWaveInputChannel => labelWaveInputChannel;

        internal DarkComboBox ComboBoxWaveInputChannel => comboBoxWaveInputChannel;

        internal Label LabelWaveLoopbackChannel => labelWaveLoopbackChannel;

        internal DarkComboBox ComboBoxWaveLoopbackChannel => comboBoxWaveLoopbackChannel;

        internal Label LabelWaveLoopbackStatus => labelWaveLoopbackStatus;
    }
}
