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

        internal ThemedComboBox ComboBoxPlaybackDevice => comboBoxPlaybackDevice;

        internal Label LabelRecordingDevice => labelRecordingDevice;

        internal ThemedComboBox ComboBoxRecordingDevice => comboBoxRecordingDevice;

        internal Label LabelWaveInputChannel => labelWaveInputChannel;

        internal ThemedComboBox ComboBoxWaveInputChannel => comboBoxWaveInputChannel;

        internal Label LabelWaveLoopbackChannel => labelWaveLoopbackChannel;

        internal ThemedComboBox ComboBoxWaveLoopbackChannel => comboBoxWaveLoopbackChannel;

        internal Label LabelWaveLoopbackStatus => labelWaveLoopbackStatus;

        internal Label LabelDeviceSettings => labelDeviceSettings;

        internal Button ButtonDeviceSettings => buttonDeviceSettings;
    }
}
