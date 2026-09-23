using System.ComponentModel;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>One channel block of the Virtual DSP tool; source resolution and curve rendering stay with the host panel.</summary>
public partial class VirtualCrossoverChannelControl : UserControl
{
    private string channelName = "A";
    private bool suppressChangeEvents;
    private bool muted;
    private bool collapsed;
    private double peqPreampDb;
    private int bottomMargin;
    private bool phaseControlShown;
    private bool firControlShown;
    private int processorSampleRateHz = 48_000;
    private string? firSourceName;
    private FirFilter? firKernel;
    private FirCrossoverDesign? firDesign;

    public VirtualCrossoverChannelControl()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);

        InitializeComponent();
        // Designer height is the tall one (phase + FIR rows); the block ends at its lowest shown row plus this margin.
        // Measured once here in designer units: a hidden optional row would make it read zero later.
        bottomMargin = Math.Max(
            0, MaximumSize.Height - Controls.Cast<Control>().Max(child => child.Bottom));
        numericHighPassRipple.Maximum = (decimal)CrossoverFilter.MaximumChebyshevRippleDb;
        numericLowPassRipple.Maximum = (decimal)CrossoverFilter.MaximumChebyshevRippleDb;
        PopulateCrossoverCombos();
        WireEvents();
        UpdateZoneAvailability();
        UpdateCrossoverAvailability();
        UpdateDelayTooltip();
        UpdateTotalGain();
        // Applied here, not by the host: the flow list measures the block as soon as it is added.
        ApplyOptionalRows();
    }

    /// <summary>PEQ preamp (dB) pushed by the host, folded into the gain readout.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double PeqPreampDb
    {
        get => peqPreampDb;
        set
        {
            if (peqPreampDb == value)
            {
                return;
            }

            peqPreampDb = value;
            UpdateTotalGain();
        }
    }

    public event EventHandler? SettingsChanged;

    public event EventHandler? SourceClicked;

    public event EventHandler? SpatialAverageClicked;

    public event EventHandler? PeqMenuClicked;

    public event EventHandler? FirClicked;

    public event EventHandler? AcousticGoalClicked;

    /// <summary>Separate from <see cref="SettingsChanged"/>: the fold is persisted without recomputing curves.</summary>
    public event EventHandler? CollapsedChanged;

    public event EventHandler? MoveUpClicked;

    public event EventHandler? MoveDownClicked;

    public void SetMoveAvailability(bool canMoveUp, bool canMoveDown)
    {
        buttonMoveUp.Enabled = canMoveUp;
        buttonMoveDown.Enabled = canMoveDown;
    }

    [DefaultValue("A")]
    public string ChannelName
    {
        get => channelName;
        set
        {
            channelName = value;
            labelChannel.Text = $"Channel {value}";
        }
    }

    internal Button SourceButton => buttonSource;

    internal ThemedNumericUpDown GainInput => numericGain;
    internal ThemedNumericUpDown DelayInput => numericDelay;
    internal CheckBox InvertCheckBox => checkBoxInvert;
    internal CheckBox MonoCheckBox => checkBoxMono;
    internal ThemedComboBox ZoneComboBox => comboBoxZone;
    internal ThemedComboBox CrossoverKindComboBox => comboBoxCrossoverKind;
    internal ThemedNumericUpDown HighPassFrequencyInput => numericHighPassHz;
    internal ThemedComboBox HighPassFamilyComboBox => comboBoxHighPassFamily;
    internal ThemedComboBox HighPassSlopeComboBox => comboBoxHighPassSlope;
    internal ThemedNumericUpDown LowPassFrequencyInput => numericLowPassHz;
    internal ThemedComboBox LowPassFamilyComboBox => comboBoxLowPassFamily;
    internal ThemedComboBox LowPassSlopeComboBox => comboBoxLowPassSlope;
    internal ThemedNumericUpDown HighPassRippleInput => numericHighPassRipple;
    internal ThemedNumericUpDown LowPassRippleInput => numericLowPassRipple;
    internal Button MuteButton => buttonMute;
    internal Button CollapseButton => buttonCollapse;
    internal ThemedNumericUpDown PhaseInput => numericPhase;
    internal Label PhaseLabel => labelPhase;
    internal Label PhaseInfoLabel => labelPhaseInfo;
    internal Button FirButton => buttonFir;
    internal Button AcousticGoalButton => buttonAcousticGoal;
    internal Label FirLabel => labelFir;
    internal Label FirInfoLabel => labelFirInfo;
    internal Label PeqInfoLabel => labelPeqInfo;
    internal Button PeqMenuButton => buttonPeqMenu;
    internal Label TotalGainLabel => labelTotalGain;
    internal CheckBox ShowRawCheckBox => checkBoxShowRaw;
    internal CheckBox ShowProcessedCheckBox => checkBoxShowProcessed;
    internal CheckBox BypassCheckBox => checkBoxBypass;

    public VirtualCrossoverZone SelectedZone =>
        comboBoxZone.SelectedItem is VirtualCrossoverZone zone
            ? zone
            : VirtualCrossoverZone.Front;

    /// <summary>Processor rate: the all-pass corner and the FIR length in time depend on it.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int ProcessorSampleRateHz
    {
        get => processorSampleRateHz;
        set
        {
            if (processorSampleRateHz == value || value <= 0)
            {
                return;
            }

            processorSampleRateHz = value;
            UpdatePhaseReadout();
            UpdateFirReadout();
        }
    }

    [DefaultValue(false)]
    public bool Muted
    {
        get => muted;
        set
        {
            muted = value;
            buttonMute.Text = value ? "🔇" : "🔈";
            buttonMute.ForeColor = value
                ? UiPalette.Error
                : UiPalette.TextPrimary;
        }
    }

    private WrappingToolTip? tooltipHost;
    private string spatialAverageTooltip = string.Empty;

    /// <summary>Applies stored values without firing SettingsChanged per field; the host redraws once afterward.</summary>
    public void RunBatchUpdate(Action update)
    {
        suppressChangeEvents = true;
        try
        {
            update();
        }
        finally
        {
            suppressChangeEvents = false;
        }

        UpdateZoneAvailability();
        UpdateCrossoverAvailability();
        UpdateDelayTooltip();
        UpdateTotalGain();
    }

    private void WireEvents()
    {
        buttonSource.Click += (_, _) => SourceClicked?.Invoke(this, EventArgs.Empty);
        buttonSpatialAverage.Click +=
            (_, _) => SpatialAverageClicked?.Invoke(this, EventArgs.Empty);
        buttonMute.Click += (_, _) =>
        {
            Muted = !Muted;
            RaiseSettingsChanged();
        };
        buttonCollapse.Click += (_, _) => Collapsed = !Collapsed;
        buttonMoveUp.Click += (_, _) => MoveUpClicked?.Invoke(this, EventArgs.Empty);
        buttonMoveDown.Click += (_, _) => MoveDownClicked?.Invoke(this, EventArgs.Empty);
        buttonPeqMenu.Click += (_, _) => PeqMenuClicked?.Invoke(this, EventArgs.Empty);
        buttonFir.Click += (_, _) => FirClicked?.Invoke(this, EventArgs.Empty);
        buttonAcousticGoal.Click += (_, _) => AcousticGoalClicked?.Invoke(this, EventArgs.Empty);

        numericGain.ValueChanged += (_, _) =>
        {
            UpdateTotalGain();
            RaiseSettingsChanged();
        };
        numericDelay.ValueChanged += (_, _) =>
        {
            UpdateDelayTooltip();
            RaiseSettingsChanged();
        };
        checkBoxInvert.CheckedChanged += (_, _) => RaiseSettingsChanged();
        checkBoxMono.CheckedChanged += (_, _) => RaiseSettingsChanged();
        comboBoxZone.SelectedIndexChanged += (_, _) =>
        {
            UpdateZoneAvailability();
            UpdatePhaseReadout();
            RaiseSettingsChanged();
        };
        numericPhase.ValueChanged += (_, _) =>
        {
            // The device has 64 positions, so a user-typed angle snaps (re-entrant: the snapped value is already on the grid).
            // Host-loaded values pass through unsnapped: the file format accepts any angle, and silently rewriting it would desync field and project.
            if (suppressChangeEvents)
            {
                UpdatePhaseReadout();
                return;
            }

            var snapped = (decimal)PhaseRotationControl.SnapToGrid((double)numericPhase.Value);
            if (snapped != numericPhase.Value)
            {
                numericPhase.Value = snapped;
                return;
            }

            UpdatePhaseReadout();
            RaiseSettingsChanged();
        };
        comboBoxCrossoverKind.SelectedIndexChanged += (_, _) =>
        {
            UpdateCrossoverAvailability();
            UpdateFirReadout();
            RaiseSettingsChanged();
        };
        WireEdgeEvents(
            numericHighPassHz, comboBoxHighPassFamily, comboBoxHighPassSlope, numericHighPassRipple);
        WireEdgeEvents(
            numericLowPassHz, comboBoxLowPassFamily, comboBoxLowPassSlope, numericLowPassRipple);
        checkBoxShowRaw.CheckedChanged += (_, _) => RaiseSettingsChanged();
        checkBoxShowProcessed.CheckedChanged += (_, _) => RaiseSettingsChanged();
        checkBoxBypass.CheckedChanged += (_, _) => RaiseSettingsChanged();
    }

    private void RaiseSettingsChanged()
    {
        if (!suppressChangeEvents)
        {
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // A centre channel is derived from L and R, so Mono is forced on and locked; set through the normal path so the project stores it.
    private void UpdateZoneAvailability()
    {
        bool forced = VirtualCrossoverZones.RequiresMono(SelectedZone);
        if (forced)
        {
            checkBoxMono.Checked = true;
        }

        checkBoxMono.Enabled = !forced;
        UiStyle.SetTextEnabledLook(checkBoxMono, !forced, interactive: true);
    }
}
