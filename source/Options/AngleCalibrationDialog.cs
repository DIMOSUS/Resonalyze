using System.Windows.Forms;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using Resonalyze.Dsp;

namespace Resonalyze.Options;

/// <summary>
/// Edits one angular calibration: which curve it is derived from, the angle, and
/// the geometry the estimate is built on. The preview draws what the model
/// produces — the angular correction and the spread of the reference
/// microphones it was taken from — so the user sees the uncertainty of the
/// estimate before accepting it.
/// <para>
/// The edited definition IS the one handed in: accepting the dialog writes the
/// controls back into it, and cancelling leaves it untouched. There is no second
/// result to read.
/// </para>
/// </summary>
internal sealed partial class AngleCalibrationDialog : Form
{
    private const double PreviewMinimumHz = 20.0;
    private const double PreviewMaximumHz = 20_000.0;
    private const int PreviewPointCount = 240;

    private readonly MicrophoneCalibrationDefinition definition;
    private bool initializing;

    public AngleCalibrationDialog(
        MicrophoneCalibrationDefinition definition,
        IReadOnlyList<MicrophoneCalibrationDefinition> baseCandidates)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(baseCandidates);
        this.definition = definition;
        InitializeComponent();

        initializing = true;
        try
        {
            textBoxName.Text = definition.Name;
            numericAngle.Value = numericAngle.ClampValue(definition.AngleDegrees);
            numericDiameter.Value = numericDiameter.ClampValue(definition.FrontDiameterMm);
            PopulateBaseCombo(baseCandidates);
            PopulateGridCombo();
            PopulateReferenceCombo();
        }
        finally
        {
            initializing = false;
        }

        numericAngle.ValueChanged += (_, _) => UpdatePreview();
        numericDiameter.ValueChanged += (_, _) => UpdatePreview();
        comboBoxGrid.SelectedIndexChanged += (_, _) => UpdatePreview();
        comboBoxReference.SelectedIndexChanged += (_, _) => UpdatePreview();
        buttonOk.Click += (_, _) => CommitToDefinition();
        UpdatePreview();
    }

    private void PopulateBaseCombo(
        IReadOnlyList<MicrophoneCalibrationDefinition> baseCandidates)
    {
        comboBoxBase.DropDownStyle = ComboBoxStyle.DropDownList;
        comboBoxBase.Items.Clear();
        comboBoxBase.Items.Add(new BaseOption(null, "The microphone's 0° calibration"));
        foreach (MicrophoneCalibrationDefinition candidate in baseCandidates)
        {
            comboBoxBase.Items.Add(new BaseOption(candidate.Id, candidate.Name));
        }

        comboBoxBase.SelectedIndex = 0;
        for (int index = 1; index < comboBoxBase.Items.Count; index++)
        {
            if (comboBoxBase.Items[index] is BaseOption option &&
                string.Equals(option.Id, definition.BaseId, StringComparison.OrdinalIgnoreCase))
            {
                comboBoxBase.SelectedIndex = index;
                break;
            }
        }
    }

    private void PopulateGridCombo()
    {
        comboBoxGrid.DropDownStyle = ComboBoxStyle.DropDownList;
        comboBoxGrid.Items.Clear();
        comboBoxGrid.Items.Add(new GridOption(
            MicrophoneProtectionGrid.Unknown, "unknown (widest uncertainty)"));
        comboBoxGrid.Items.Add(new GridOption(
            MicrophoneProtectionGrid.Fitted, "fitted"));
        comboBoxGrid.Items.Add(new GridOption(
            MicrophoneProtectionGrid.Removed, "removed"));
        comboBoxGrid.SelectedIndex = definition.Grid switch
        {
            MicrophoneProtectionGrid.Fitted => 1,
            MicrophoneProtectionGrid.Removed => 2,
            _ => 0
        };
    }

    private void PopulateReferenceCombo()
    {
        comboBoxReference.DropDownStyle = ComboBoxStyle.DropDownList;
        comboBoxReference.Items.Clear();
        comboBoxReference.Items.Add(new ReferenceOption(
            MicrophoneAngleReference.GrasGeometry, "GRAS geometry (size and grid)"));
        comboBoxReference.Items.Add(new ReferenceOption(
            MicrophoneAngleReference.SonarworksXref20, "Sonarworks XREF 20 (measured)"));
        comboBoxReference.SelectedIndex =
            definition.Reference == MicrophoneAngleReference.SonarworksXref20 ? 1 : 0;
    }

    private MicrophoneAngleRequest BuildRequest() => new(
        (double)numericAngle.Value,
        (double)numericDiameter.Value,
        comboBoxGrid.SelectedItem is GridOption grid
            ? grid.Grid
            : MicrophoneProtectionGrid.Unknown,
        comboBoxReference.SelectedItem is ReferenceOption reference
            ? reference.Reference
            : MicrophoneAngleReference.GrasGeometry);

    private void CommitToDefinition()
    {
        MicrophoneAngleRequest request = BuildRequest();
        definition.Name = textBoxName.Text;
        definition.Kind = MicrophoneCalibrationKind.Angle;
        definition.BaseId = comboBoxBase.SelectedItem is BaseOption option ? option.Id : null;
        definition.AngleDegrees = request.AngleDegrees;
        definition.FrontDiameterMm = request.FrontDiameterMm;
        definition.Grid = request.Grid;
        definition.Reference = request.Reference;
        definition.Normalize();
    }

    private void UpdatePreview()
    {
        if (initializing)
        {
            return;
        }

        MicrophoneAngleRequest request = BuildRequest();
        // The named microphone carries its own measured behaviour, so its size
        // and grid are not inputs; showing them editable would suggest otherwise.
        bool geometric = request.Reference == MicrophoneAngleReference.GrasGeometry;
        numericDiameter.Enabled = geometric;
        comboBoxGrid.Enabled = geometric;

        MicrophoneAngleEstimate estimate = MicrophoneAngleModel.Estimate(request);
        var center = new List<DataPoint>(PreviewPointCount);
        var band = new List<DataPoint>(PreviewPointCount);
        var bandUpper = new List<DataPoint>(PreviewPointCount);
        double widest = 0;
        double widestHz = PreviewMaximumHz;
        for (int index = 0; index < PreviewPointCount; index++)
        {
            double frequency = PreviewMinimumHz * Math.Pow(
                PreviewMaximumHz / PreviewMinimumHz,
                index / (double)(PreviewPointCount - 1));
            MicrophoneAngleBounds bounds = estimate.Deltas(frequency);
            center.Add(new DataPoint(frequency, bounds.CenterDb));
            band.Add(new DataPoint(frequency, bounds.LowerDb));
            bandUpper.Add(new DataPoint(frequency, bounds.UpperDb));
            double spread = bounds.UpperDb - bounds.LowerDb;
            if (spread > widest)
            {
                widest = spread;
                widestHz = frequency;
            }
        }

        plotViewPreview.Model = BuildPreviewModel(center, band, bandUpper);
        plotViewPreview.InvalidatePlot(true);
        labelSummary.Text = DescribeEstimate(estimate, center[^1].Y, widest, widestHz);
    }

    private static PlotModel BuildPreviewModel(
        List<DataPoint> center,
        List<DataPoint> lower,
        List<DataPoint> upper)
    {
        var model = new PlotModel
        {
            Background = OxyColor.FromRgb(32, 36, 46),
            PlotAreaBackground = OxyColor.FromRgb(32, 36, 46),
            TextColor = OxyColors.White,
            Title = "Angular correction",
            TitleColor = OxyColors.White,
            TitleFontSize = 10
        };
        model.Axes.Add(new LogarithmicAxis
        {
            Position = AxisPosition.Bottom,
            Minimum = PreviewMinimumHz,
            Maximum = PreviewMaximumHz,
            MajorGridlineColor = OxyColor.FromRgb(55, 62, 78),
            MajorGridlineStyle = LineStyle.Solid,
            MinorGridlineColor = OxyColor.FromRgb(48, 54, 70),
            MinorGridlineStyle = LineStyle.Dot,
            TextColor = OxyColors.White,
            TicklineColor = OxyColors.White,
            Title = "Hz",
            IsPanEnabled = false,
            IsZoomEnabled = false
        });
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            MajorGridlineColor = OxyColor.FromRgb(55, 62, 78),
            MajorGridlineStyle = LineStyle.Solid,
            MinorGridlineColor = OxyColor.FromRgb(48, 54, 70),
            MinorGridlineStyle = LineStyle.Dot,
            TextColor = OxyColors.White,
            TicklineColor = OxyColors.White,
            Title = "dB",
            IsPanEnabled = false,
            IsZoomEnabled = false
        });
        var uncertainty = new AreaSeries
        {
            Color = OxyColors.Transparent,
            Color2 = OxyColors.Transparent,
            Fill = OxyColor.FromAColor(110, OxyColor.FromRgb(0x37, 0xC8, 0xA0))
        };
        uncertainty.Points.AddRange(lower);
        uncertainty.Points2.AddRange(upper);
        model.Series.Add(uncertainty);
        var line = new LineSeries
        {
            Color = OxyColor.FromRgb(0x37, 0xC8, 0xA0),
            StrokeThickness = 2
        };
        line.Points.AddRange(center);
        model.Series.Add(line);
        return model;
    }

    private static string DescribeEstimate(
        MicrophoneAngleEstimate estimate,
        double topOfBandDb,
        double widestSpreadDb,
        double widestSpreadHz)
    {
        string references = estimate.References.Count == 0
            ? "no reference"
            : string.Join(" · ", estimate.References);
        // Where the references run out the curve holds its last value rather
        // than switching to another size mid-band, so say where that happens
        // instead of letting a flat top look like a measured result.
        string held = estimate.HighestSupportedFrequencyHz < PreviewMaximumHz
            ? $" Modelled to {FrequencyText.Format(estimate.HighestSupportedFrequencyHz)}, " +
              "held above that."
            : string.Empty;
        // With one reference of comparable geometry there is nothing to disagree,
        // and printing a 0.00 dB spread would read as a confidence the estimate
        // has not earned.
        string spread = widestSpreadDb >= 0.005
            ? $", references disagreeing by up to {widestSpreadDb:0.00} dB " +
              $"around {FrequencyText.Format(widestSpreadHz)}"
            : ", from a single reference, so no spread is shown";
        return
            $"Estimated, not measured: {topOfBandDb:+0.00;-0.00;0.00} dB at 20 kHz" +
            $"{spread}.\r\nBuilt from: {references}.{held}";
    }

    private sealed record BaseOption(string? Id, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record GridOption(MicrophoneProtectionGrid Grid, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record ReferenceOption(MicrophoneAngleReference Reference, string Label)
    {
        public override string ToString() => Label;
    }
}
