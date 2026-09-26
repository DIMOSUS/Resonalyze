namespace Resonalyze;

/// <summary>Auto delay dialog: Run computes a proposal without touching channels; nothing is written until Apply. The panel supplies the runner.</summary>
internal sealed partial class VirtualCrossoverAutoDelayDialog : Form
{
    private readonly WrappingToolTip toolTip = new()
    {
        InitialDelay = 500,
        ReshowDelay = 150,
        AutoPopDelay = 12_000,
        ShowAlways = true
    };

    // Colors follow meaning: an always-amber label read as a warning even for good news.
    private static readonly Color StatusNeutral = UiPalette.TextSecondary;
    private static readonly Color StatusSuccess = UiPalette.Success;
    private static readonly Color StatusWarning = UiPalette.Warning;
    private static readonly Color StatusError = UiPalette.Error;

    private Func<AutoDelayRunRequest, Task<AutoDelayRunResult>>? runner;
    private bool stereo;
    private bool running;
    private string? undoable;

    public VirtualCrossoverAutoDelayDialog()
    {
        InitializeComponent();
        numericSceneOffset.ApplyFieldRange(VirtualCrossoverLimits.SceneOffset);
        numericNearSideCut.ApplyFieldRange(VirtualCrossoverLimits.NearSideCut);
        numericRearFill.ApplyFieldRange(VirtualCrossoverLimits.RearFillOffset);
        CancelButton = buttonCancel;
        buttonApply.Enabled = false;
        buttonRun.Click += async (_, _) => await RunAsync();
        buttonUndo.Click += (_, _) =>
        {
            UndoRequested = true;
            DialogResult = DialogResult.Cancel;
            Close();
        };
        // Any LHD/RHD toggle flips RHD's Checked, so one handler suffices.
        radioRightHandDrive.CheckedChanged += (_, _) => InvalidateResult();
        numericSceneOffset.ValueChanged += (_, _) => InvalidateResult();
        numericRearFill.ValueChanged += (_, _) => InvalidateResult();
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
            "Stereo scene offset (ms): the far side arrives earlier by this\r\n" +
            "much, pulling the image toward the dash centre.\r\n" +
            "Typical 0.2–0.3 ms; 0 = centred on the mic position.");
        toolTip.SetToolTip(
            checkBoxGains,
            "Starting level balance, cut-only, for channels reaching above\r\n" +
            "300 Hz. Subs, mono and crossover-less channels keep their\r\n" +
            "gain. Off: a run leaves every level as you set it.");
        numericNearSideCut.ApplyToolTip(
            toolTip,
            "How much quieter the NEAR (driver's) side plays than the far\r\n" +
            "side (dB) — the level twin of the scene offset, pulling the\r\n" +
            "image from the driver's axis toward the dash center. Which\r\n" +
            "side is near comes from the LHD/RHD switch. Typical: 1–2 dB.\r\n" +
            "0 = both sides levelled to the same target.\r\n" +
            "Cut-only: produced by attenuating the near side's channels.");
    }

    public AutoDelayRunResult? Result { get; private set; }

    public bool UndoRequested { get; private set; }

    /// <summary><paramref name="polarityWarning"/>: shown at launch when a driver's L and R measured polarities disagree.</summary>
    /// <param name="undoable">Which sides the last Apply aligned, while it can be undone.</param>
    public void Init(
        bool stereo,
        double sceneOffsetMs,
        bool rightHandDrive,
        double nearSideCutDb,
        Func<AutoDelayRunRequest, Task<AutoDelayRunResult>> runner,
        string? polarityWarning = null,
        bool hasRearFill = false,
        double rearFillOffsetMs = VirtualCrossoverLimits.DefaultRearFillOffsetMs,
        string? undoable = null)
    {
        this.stereo = stereo;
        this.runner = runner;
        this.undoable = undoable;
        buttonUndo.Enabled = undoable != null;
        toolTip.SetToolTip(
            buttonUndo,
            (undoable == null ? "Nothing applied here to undo." : $"The last Apply aligned {undoable}.") + "\r\n" +
            "Undo puts every channel back exactly as it was before it:\r\n" +
            "delays, polarity, gains, the scene and anything changed since.\r\n" +
            "One step; gone once a session is loaded.");
        numericRearFill.Value = Math.Clamp(
            (decimal)rearFillOffsetMs,
            numericRearFill.Minimum,
            numericRearFill.Maximum);
        ApplyRearFillAvailability(hasRearFill);
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

    private void ApplyRearFillAvailability(bool hasRearFill)
    {
        UiStyle.SetTextEnabledLook(labelRearFill, hasRearFill);
        UiStyle.SetTextEnabledLook(labelRearFillHint, hasRearFill);
        numericRearFill.Enabled = hasRearFill;
        numericRearFill.ApplyToolTip(
            toolTip,
            hasRearFill
                ? "How far behind the front stage the rear fill should arrive,\r\n" +
                    "measured acoustically at the listening position.\r\n" +
                    "\r\n" +
                    "10-20 ms (start at 15): the precedence effect keeps the image\r\n" +
                    "on the dash while the rear adds room. Note the rear speakers\r\n" +
                    "are often CLOSER to your ears than the front, so some delay\r\n" +
                    "is needed just to reach zero - this offset is on top of that.\r\n" +
                    "0 ms: co-arrival, which is what a second row of listeners\r\n" +
                    "wants and what collapses the image for the front seats.\r\n" +
                    "\r\n" +
                    "Level is not set here. Rear fill usually sits 6-12 dB under\r\n" +
                    "the front (raise it until you notice it as a separate source,\r\n" +
                    "then take 2-3 dB back); the read-out's vs Front block gives\r\n" +
                    "you the current figure to adjust against by ear.\r\n" +
                    "\r\n" +
                    "Polarity between front and rear is not judged at a Haas\r\n" +
                    "offset - at that distance the two no longer sum in any way\r\n" +
                    "the ear resolves."
                : "This project has no block in the Rear zone, so there is no\r\n" +
                    "rear fill to place. Set a block's Zone to Rear to use it.");
    }

    private void UpdateNearSideCutEnabled()
    {
        bool enabled = stereo && checkBoxGains.Checked;
        UiStyle.SetTextEnabledLook(labelNearSideCut, enabled);
        numericNearSideCut.Enabled = enabled;
    }

    // Any input change makes the proposal stale: Apply writes exactly what the report shows.
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

    // Must not close mid-run: the runner reads live channel configuration, which only modality keeps stable.
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
        // The dialog cannot close mid-run: a click would stay armed and turn the next Apply into an Undo.
        buttonUndo.Enabled = false;
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
                (double)numericNearSideCut.Value,
                (double)numericRearFill.Value));
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
                buttonUndo.Enabled = undoable != null;
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
