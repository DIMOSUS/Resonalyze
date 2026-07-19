namespace Resonalyze;

/// <summary>
/// The Auto delay dialog: the stereo scene offset and the gain-balance
/// opt-in, a Run command that computes a PROPOSAL (delays, polarities and
/// optionally gains) without touching the channels, the before/after report
/// with per-channel confidence, and Apply/Discard. Nothing is written until
/// Apply; Discard leaves every channel setting as it was. The panel supplies
/// the runner — the dialog owns no DSP.
/// </summary>
internal sealed partial class VirtualCrossoverAutoDelayDialog : Form
{
    private readonly ToolTip toolTip = new()
    {
        InitialDelay = 500,
        ReshowDelay = 150,
        AutoPopDelay = 12_000,
        ShowAlways = true
    };

    // Status colors follow the message's meaning, matching the app's dark
    // palette: an always-amber label read as a warning even for good news.
    private static readonly Color StatusNeutral = Color.FromArgb(185, 190, 200);
    private static readonly Color StatusSuccess = Color.FromArgb(96, 210, 120);
    private static readonly Color StatusWarning = Color.FromArgb(230, 184, 0);
    private static readonly Color StatusError = Color.FromArgb(240, 100, 110);

    private Func<double, bool, Task<AutoDelayRunResult>>? runner;
    private bool stereo;
    private bool running;

    public VirtualCrossoverAutoDelayDialog()
    {
        InitializeComponent();
        CancelButton = buttonCancel;
        buttonApply.Enabled = false;
        buttonRun.Click += async (_, _) => await RunAsync();
        numericSceneOffset.ValueChanged += (_, _) => InvalidateResult();
        checkBoxGains.CheckedChanged += (_, _) => InvalidateResult();
        toolTip.SetToolTip(
            numericSceneOffset,
            "Stereo scene offset (ms).\r\n" +
            "Positive: the RIGHT side arrives earlier by this much,\r\n" +
            "pulling the image from the driver's axis toward the\r\n" +
            "dash center on a left-hand-drive car. Typical: 0.2–0.3 ms.\r\n" +
            "Right-hand drive: enter a negative value.\r\n" +
            "0 = image centered on the measurement position.\r\n" +
            "With gain balancing on, the same offset also sets the\r\n" +
            "L/R level tilt (0.27 ms ≈ 2 dB, clamped at ±6 dB).");
        toolTip.SetToolTip(
            checkBoxGains,
            "Starting-point level balance, cut-only: channels whose band\r\n" +
            "meaningfully reaches above 300 Hz are levelled by their\r\n" +
            "per-octave band energy — the board to one target, right vs\r\n" +
            "left honoring the scene offset. Subwoofers, mono channels\r\n" +
            "and channels without a crossover keep their gain. This is a\r\n" +
            "starting balance, not a final tonal decision.");
        Disposed += (_, _) => toolTip.Dispose();
    }

    /// <summary>The proposal of the last completed Run, applied on OK.</summary>
    public AutoDelayRunResult? Result { get; private set; }

    /// <summary>
    /// Seeds the dialog: the run mode (a single-side run has no L/R offset to
    /// honor), the persisted scene offset, and the panel's compute delegate
    /// (scene offset ms, balance gains) -> proposal.
    /// </summary>
    public void Init(
        bool stereo,
        double sceneOffsetMs,
        Func<double, bool, Task<AutoDelayRunResult>> runner)
    {
        this.stereo = stereo;
        this.runner = runner;
        numericSceneOffset.Value = Math.Clamp(
            (decimal)sceneOffsetMs,
            numericSceneOffset.Minimum,
            numericSceneOffset.Maximum);
        if (!stereo)
        {
            labelSceneOffset.Enabled = false;
            numericSceneOffset.Enabled = false;
            toolTip.SetToolTip(
                numericSceneOffset,
                "Only one side is measured, so this run aligns a single\r\n" +
                "side and the L/R scene offset does not apply.");
        }

        textBoxReport.Text =
            (stereo
                ? "Stereo run: the left side aligns first, the right top is " +
                  "timed to the left one honoring the scene offset, and the " +
                  "right side descends from it."
                : "Single-side run: the displayed side's channels align " +
                  "against each other.") +
            Environment.NewLine + Environment.NewLine +
            "Run computes a proposal; nothing is applied until Apply.";
    }

    // Once a proposal exists, any input change makes it stale: Apply must
    // always write exactly what the report shows, computed from the inputs
    // the user sees.
    private void InvalidateResult()
    {
        if (Result == null)
        {
            return;
        }

        Result = null;
        buttonApply.Enabled = false;
        SetStatus("Settings changed — Run again to refresh the proposal.", StatusWarning);
    }

    private void SetStatus(string text, Color color)
    {
        labelStatus.Text = text;
        labelStatus.ForeColor = color;
    }

    // The compute runs seconds of FFT work off the UI thread; the dialog
    // (and, through modality, the whole panel) stays visible in a busy state
    // and must not close mid-run — the runner reads the live channel
    // configuration, which only modality keeps stable.
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (running)
        {
            e.Cancel = true;
        }

        base.OnFormClosing(e);
    }

    private async Task RunAsync()
    {
        if (running || runner == null)
        {
            return;
        }

        running = true;
        Result = null;
        buttonRun.Enabled = false;
        buttonApply.Enabled = false;
        buttonCancel.Enabled = false;
        numericSceneOffset.Enabled = false;
        checkBoxGains.Enabled = false;
        SetStatus("Aligning…", StatusNeutral);
        UseWaitCursor = true;
        try
        {
            AutoDelayRunResult result = await runner(
                (double)numericSceneOffset.Value, checkBoxGains.Checked);
            if (IsDisposed)
            {
                return;
            }

            Result = result;
            textBoxReport.Text = result.ReportText;
            buttonApply.Enabled = true;
            SetStatus(
                "Proposal ready — Apply writes it, Discard keeps the current settings.",
                StatusSuccess);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Auto delay failed: {exception}");
            if (IsDisposed)
            {
                return;
            }

            SetStatus("Auto delay failed.", StatusError);
            textBoxReport.Text =
                "Auto delay failed." + Environment.NewLine +
                Environment.NewLine + exception.Message;
        }
        finally
        {
            if (!IsDisposed)
            {
                running = false;
                buttonRun.Enabled = true;
                buttonCancel.Enabled = true;
                numericSceneOffset.Enabled = stereo;
                checkBoxGains.Enabled = true;
                UseWaitCursor = false;
            }
        }
    }
}
