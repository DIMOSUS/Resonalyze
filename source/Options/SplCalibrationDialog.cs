namespace Resonalyze.Options;

/// <summary>Captures on the input being configured, so the anchor is pinned to that tract; the caller persists it. The
/// listen and its outcome are an <see cref="SplCalibrationSession"/>.</summary>
internal sealed partial class SplCalibrationDialog : Form
{
    private readonly IAudioSessionFactory audioSessionFactory;
    private readonly SplCalibrationSession session;
    private bool presenting;

    public SplCalibrationDialog(
        IAudioSessionFactory audioSessionFactory,
        AudioSessionRequest request,
        SplCalibration? existing = null)
    {
        this.audioSessionFactory = audioSessionFactory ??
            throw new ArgumentNullException(nameof(audioSessionFactory));
        session = new SplCalibrationSession(request, existing);

        InitializeComponent();

        foreach (double level in SplCalibrationSession.Levels)
        {
            comboBoxReference.Items.Add(SplCalibrationReport.ReferenceLabel(level));
        }

        Present();
        comboBoxReference.SelectedIndexChanged += (_, _) =>
        {
            if (!presenting)
            {
                session.SelectReference(comboBoxReference.SelectedIndex);
            }
        };
    }

    public SplCalibration? Result => session.Result;

    private async void buttonStart_Click(object? sender, EventArgs e)
    {
        if (session.Running)
        {
            session.Stop();
            return;
        }

        Task run = session.RunAsync(audioSessionFactory, () =>
        {
            if (!IsDisposed)
            {
                Present();
            }
        });
        Present();
        await run;
        if (IsDisposed)
        {
            return;
        }

        Present();
        if (session.CloseRequested)
        {
            Close();
        }
    }

    private void Present()
    {
        presenting = true;
        try
        {
            bool running = session.Running;
            comboBoxReference.SelectedIndex = session.ReferenceIndex;
            comboBoxReference.Enabled = !running;
            buttonStart.Text = running ? "Stop" : "Start calibration";
            progressBar.Value = session.ProgressPercent;
            progressBar.Visible = running;
            buttonCancel.Enabled = !running;
            buttonSave.Enabled = !running && session.Result != null;
            if (session.Status is { } status)
            {
                labelStatus.ForeColor = status.Tone switch
                {
                    SplStatusTone.Error => UiPalette.Error,
                    SplStatusTone.Success => UiPalette.Success,
                    _ => UiPalette.TextDefault
                };
                labelStatus.Text = status.Text;
            }
        }
        finally
        {
            presenting = false;
        }
    }

    private void SplCalibrationDialog_FormClosing(object? sender, FormClosingEventArgs e)
    {
        // Defer the close until the capture unwinds, so the task never touches a disposed form.
        if (!session.RequestClose())
        {
            e.Cancel = true;
        }
    }
}
