namespace Resonalyze;

/// <summary>
/// The Auto delay dialog: the steering layout (LHD/RHD), the stereo scene
/// offset and the gain-balance
/// opt-in, a Run command that computes a PROPOSAL (delays, polarities and
/// optionally gains) without touching the channels, the before/after report
/// with per-channel confidence, and Apply/Discard. Nothing is written until
/// Apply; Discard leaves every channel setting as it was. The panel supplies
/// the runner — the dialog owns no DSP.
/// </summary>
internal sealed partial class VirtualCrossoverAutoDelayDialog : Form
{
    private readonly WrappingToolTip toolTip = new()
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

    private Func<AutoDelayRunRequest, Task<AutoDelayRunResult>>? runner;
    private bool stereo;
    private bool running;

    public VirtualCrossoverAutoDelayDialog()
    {
        InitializeComponent();
        CancelButton = buttonCancel;
        buttonApply.Enabled = false;
        buttonRun.Click += async (_, _) => await RunAsync();
        // One handler covers both radios: any LHD<->RHD toggle flips RHD's
        // own Checked, so LHD needs no listener of its own.
        radioRightHandDrive.CheckedChanged += (_, _) => InvalidateResult();
        numericSceneOffset.ValueChanged += (_, _) => InvalidateResult();
        numericNearSideCut.ValueChanged += (_, _) => InvalidateResult();
        checkBoxGains.CheckedChanged += (_, _) =>
        {
            UpdateNearSideCutEnabled();
            InvalidateResult();
        };
        string layoutTip =
            "The steering position. The driver's side is the timing\r\n" +
            "reference: it aligns first and the far side is fitted to it,\r\n" +
            "leading by the scene offset.\r\n" +
            "LHD: the left side is the reference, the right side leads.\r\n" +
            "RHD: the right side is the reference, the left side leads\r\n" +
            "(the right lags by the offset).";
        toolTip.SetToolTip(radioLeftHandDrive, layoutTip);
        toolTip.SetToolTip(radioRightHandDrive, layoutTip);
        toolTip.SetToolTip(
            numericSceneOffset,
            "Stereo scene offset (ms).\r\n" +
            "The far side arrives earlier by this much, pulling the image\r\n" +
            "from the driver's axis toward the dash center: on LHD the\r\n" +
            "right side leads, on RHD the left side does.\r\n" +
            "Typical: 0.2–0.3 ms.\r\n" +
            "0 = image centered on the measurement position.");
        toolTip.SetToolTip(
            checkBoxGains,
            "Starting-point level balance, cut-only: channels whose band\r\n" +
            "meaningfully reaches above 300 Hz are levelled by their\r\n" +
            "per-octave band energy — the board to one target, left vs\r\n" +
            "right offset by the L-R level below. Subwoofers, mono channels\r\n" +
            "and channels without a crossover keep their gain. This is a\r\n" +
            "starting balance, not a final tonal decision — which is why it\r\n" +
            "is off unless you ask for it: a run then writes delays and\r\n" +
            "polarity, and leaves every level as you set it.");
        numericNearSideCut.ApplyToolTip(
            toolTip,
            "How much quieter the NEAR (driver's) side plays than the far\r\n" +
            "side (dB) — the level twin of the scene offset, pulling the\r\n" +
            "image from the driver's axis toward the dash center. Which\r\n" +
            "side is near comes from the LHD/RHD switch. Typical: 1–2 dB.\r\n" +
            "0 = both sides levelled to the same target.\r\n" +
            "Cut-only: produced by attenuating the near side's channels.");
        // The designer's Dispose releases the tooltip; no Disposed handler.
    }

    /// <summary>The proposal of the last completed Run, applied on OK.</summary>
    public AutoDelayRunResult? Result { get; private set; }

    /// <summary>
    /// Seeds the dialog: the run mode (a single-side run has no L/R relation
    /// to honor), the persisted steering layout, scene offset and near-side
    /// cut, and the panel's compute delegate (run inputs) -> proposal.
    /// <paramref name="polarityWarning"/> is a non-empty red heads-up shown at
    /// launch when a driver's left and right measured polarities disagree.
    /// </summary>
    public void Init(
        bool stereo,
        double sceneOffsetMs,
        bool rightHandDrive,
        double nearSideCutDb,
        Func<AutoDelayRunRequest, Task<AutoDelayRunResult>> runner,
        string? polarityWarning = null)
    {
        this.stereo = stereo;
        this.runner = runner;
        radioLeftHandDrive.Checked = !rightHandDrive;
        radioRightHandDrive.Checked = rightHandDrive;
        numericSceneOffset.Value = Math.Clamp(
            (decimal)sceneOffsetMs,
            numericSceneOffset.Minimum,
            numericSceneOffset.Maximum);
        numericNearSideCut.Value = Math.Clamp(
            (decimal)nearSideCutDb,
            numericNearSideCut.Minimum,
            numericNearSideCut.Maximum);
        UpdateNearSideCutEnabled();
        if (!stereo)
        {
            string singleSideTip =
                "Only one side is measured, so this run aligns a single\r\n" +
                "side and the L/R scene offset does not apply.";
            UiStyle.SetTextEnabledLook(radioLeftHandDrive, false, interactive: true);
            UiStyle.SetTextEnabledLook(radioRightHandDrive, false, interactive: true);
            UiStyle.SetTextEnabledLook(labelSceneOffset, false);
            numericSceneOffset.Enabled = false;
            toolTip.SetToolTip(radioLeftHandDrive, singleSideTip);
            toolTip.SetToolTip(radioRightHandDrive, singleSideTip);
            toolTip.SetToolTip(numericSceneOffset, singleSideTip);
            numericNearSideCut.ApplyToolTip(
                toolTip,
                "Only one side is measured, so there is no L/R relation to\r\n" +
                "tilt: the gain balance levels this side's board alone.");
        }

        textBoxReport.Text =
            (stereo
                ? "Stereo run: the driver's side aligns first, the far top " +
                  "is timed to it honoring the scene offset, and the far " +
                  "side descends from it."
                : "Single-side run: the displayed side's channels align " +
                  "against each other.") +
            Environment.NewLine + Environment.NewLine +
            "Run computes a proposal; nothing is applied until Apply.";

        if (!string.IsNullOrEmpty(polarityWarning))
        {
            SetStatus(polarityWarning, StatusError);
        }
    }

    // The near-side cut is an input to the gain balance only: with the
    // checkbox off no gain is written at all, and a single-side run has no L/R
    // relation to tilt. Kept visible-but-disabled either way, so the value the
    // next stereo run would use stays readable.
    private void UpdateNearSideCutEnabled()
    {
        bool enabled = stereo && checkBoxGains.Checked;
        UiStyle.SetTextEnabledLook(labelNearSideCut, enabled);
        numericNearSideCut.Enabled = enabled;
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
        UiStyle.SetTextEnabledLook(radioLeftHandDrive, false, interactive: true);
        UiStyle.SetTextEnabledLook(radioRightHandDrive, false, interactive: true);
        numericSceneOffset.Enabled = false;
        UiStyle.SetTextEnabledLook(checkBoxGains, false, interactive: true);
        UiStyle.SetTextEnabledLook(labelNearSideCut, false);
        numericNearSideCut.Enabled = false;
        SetStatus("Aligning…", StatusNeutral);
        UseWaitCursor = true;
        try
        {
            AutoDelayRunResult result = await runner(new AutoDelayRunRequest(
                (double)numericSceneOffset.Value,
                radioRightHandDrive.Checked,
                checkBoxGains.Checked,
                (double)numericNearSideCut.Value));
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
                UiStyle.SetTextEnabledLook(radioLeftHandDrive, stereo, interactive: true);
                UiStyle.SetTextEnabledLook(radioRightHandDrive, stereo, interactive: true);
                numericSceneOffset.Enabled = stereo;
                UiStyle.SetTextEnabledLook(checkBoxGains, true, interactive: true);
                UpdateNearSideCutEnabled();
                UseWaitCursor = false;
            }
        }
    }
}
