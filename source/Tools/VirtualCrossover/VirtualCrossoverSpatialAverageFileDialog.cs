using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Asks what a response file does not record before it is attached as a spatial average.
/// See docs/tech/spatial-average.md#imported-text-files.</summary>
internal sealed partial class VirtualCrossoverSpatialAverageFileDialog : Form
{
    private static readonly (ProtectiveHighPassKind Kind, string Label)[] HighPassKinds =
    [
        (ProtectiveHighPassKind.Off, "None"),
        (ProtectiveHighPassKind.Butterworth, "Butterworth"),
        (ProtectiveHighPassKind.LinkwitzRiley, "Linkwitz-Riley")
    ];

    private readonly WrappingToolTip toolTip = new()
    {
        AutoPopDelay = 20_000,
        InitialDelay = 400,
        ReshowDelay = 100
    };

    private int highPassSampleRateHz;

    public VirtualCrossoverSpatialAverageFileDialog()
    {
        InitializeComponent();
        foreach ((_, string label) in HighPassKinds)
        {
            comboBoxHighPassKind.Items.Add(label);
        }

        comboBoxHighPassKind.SelectedIndexChanged += (_, _) => FillSlopes();
        buttonCalibrationFile.Click += (_, _) => PickCalibrationFile();
        toolTip.SetToolTip(
            comboBoxHighPassKind,
            "The filter in your DSP between the sound card and the driver while the file was" + "\r\n" +
            "measured. A swept measurement here divides it out; a file measured through it" + "\r\n" +
            "carries it, so it is divided out of the file the same way.");
    }

    /// <summary>The answers as stated; read once the dialog is answered OK.</summary>
    public SpatialAverageFileSettings Answers => new()
    {
        Calibration = (comboBoxCalibration.SelectedItem as SpatialAverageFileCalibrationChoice)?.Calibration,
        HighPassKind = HighPassKinds[Math.Max(0, comboBoxHighPassKind.SelectedIndex)].Kind,
        HighPassFrequencyHz = (double)numericHighPassHz.Value,
        HighPassSlopeDbPerOctave = comboBoxHighPassSlope.SelectedItem as int? ?? 24,
        HighPassSampleRateHz = highPassSampleRateHz
    };

    public void Init(
        string channelName,
        string path,
        FrequencyResponseTextFile file,
        SpatialAverageFileSettings stated,
        IReadOnlyList<SpatialAverageFileCalibrationChoice> calibrations,
        ProtectiveHighPassConfiguration? measurementHighPass)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(stated);
        Text = $"Response file — channel {channelName}";
        labelFile.Text = SpatialAverageFileImport.Describe(file, path);
        toolTip.SetToolTip(labelFile, path);
        IReadOnlyList<string> warnings = SpatialAverageFileImport.Warnings(file);
        labelWarnings.ForeColor = warnings.Count > 0 ? UiPalette.Warning : UiPalette.TextMuted;
        labelWarnings.Text = warnings.Count > 0
            ? string.Join("\r\n", warnings.Select(warning => "•  " + warning))
            : "Nothing in the file itself looks wrong.";

        comboBoxCalibration.Items.Clear();
        foreach (SpatialAverageFileCalibrationChoice choice in calibrations)
        {
            comboBoxCalibration.Items.Add(choice);
        }

        comboBoxCalibration.SelectedIndex =
            SpatialAverageFileCalibrationChoice.IndexOf(calibrations, stated.Calibration);

        highPassSampleRateHz = stated.HighPassSampleRateHz;
        ProtectiveHighPassConfiguration highPass = stated.HighPass;
        comboBoxHighPassKind.SelectedIndex =
            Math.Max(0, Array.FindIndex(HighPassKinds, entry => entry.Kind == highPass.Kind));
        numericHighPassHz.Value = numericHighPassHz.ClampValue(highPass.FrequencyHz);
        comboBoxHighPassSlope.SelectedItem = highPass.SlopeDbPerOctave;
        labelHighPassHint.Text = measurementHighPass switch
        {
            null => "This channel's measurement does not say which high-pass it divided out.",
            { Enabled: false } => "This channel's measurement divided no high-pass out.",
            { } divided =>
                $"This channel's measurement divided out {Describe(divided)}. A file measured " +
                "through the same hardware carries it."
        };
    }

    private void FillSlopes()
    {
        ProtectiveHighPassKind kind = HighPassKinds[Math.Max(0, comboBoxHighPassKind.SelectedIndex)].Kind;
        int? kept = comboBoxHighPassSlope.SelectedItem as int?;
        comboBoxHighPassSlope.Items.Clear();
        foreach (int slope in ProtectiveHighPassConfiguration.SupportedSlopes(kind))
        {
            comboBoxHighPassSlope.Items.Add(slope);
        }

        comboBoxHighPassSlope.SelectedItem = kept is { } slopeKept && comboBoxHighPassSlope.Items.Contains(slopeKept)
            ? slopeKept
            : 24;
        bool enabled = kind != ProtectiveHighPassKind.Off;
        numericHighPassHz.Enabled = enabled;
        comboBoxHighPassSlope.Enabled = enabled;
    }

    private void PickCalibrationFile()
    {
        using var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = "Calibration file (*.txt;*.cal;*.frd)|*.txt;*.cal;*.frd|All files (*.*)|*.*",
            RestoreDirectory = true,
            Title = "Calibration the file's levels carry"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        string name = Path.GetFileNameWithoutExtension(dialog.FileName);
        CalibrationFile? curve = null;
        string? problem = null;
        try
        {
            curve = CalibrationFile.Parse(File.ReadAllText(dialog.FileName), name);
            problem = curve.LoadError;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            problem = exception.Message;
        }

        if (curve is not { HasData: true })
        {
            MessageBox.Show(
                this,
                "That file holds no calibration curve." +
                    (problem is { } error ? Environment.NewLine + Environment.NewLine + error : string.Empty),
                "Calibration",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var choice = new SpatialAverageFileCalibrationChoice(
            name,
            VirtualCrossoverCalibrationSettings.From(curve, name, Path.GetFileName(dialog.FileName)));
        comboBoxCalibration.Items.Add(choice);
        comboBoxCalibration.SelectedItem = choice;
    }

    private static string Describe(ProtectiveHighPassConfiguration filter) =>
        $"{(filter.Kind == ProtectiveHighPassKind.LinkwitzRiley ? "Linkwitz-Riley" : "Butterworth")} " +
        $"{filter.SlopeDbPerOctave} dB/oct at {filter.FrequencyHz:0.###} Hz";
}
