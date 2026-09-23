using System.Windows.Forms;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace Resonalyze.Options;

/// <summary>Edits the handed-in definition in place on OK; the fields are an <see cref="AngleCalibrationSession"/> and
/// the preview an <see cref="AngleCalibrationPreview"/>.</summary>
internal sealed partial class AngleCalibrationDialog : Form
{
    private readonly MicrophoneCalibrationDefinition definition;
    private readonly AngleCalibrationSession session;
    private bool presenting;

    public AngleCalibrationDialog(
        MicrophoneCalibrationDefinition definition,
        IReadOnlyList<MicrophoneCalibrationDefinition> baseCandidates)
    {
        this.definition = definition ?? throw new ArgumentNullException(nameof(definition));
        session = new AngleCalibrationSession(definition, baseCandidates);
        InitializeComponent();
        PlotInteraction.Enable(plotViewPreview);
        numericAngle.ApplyFieldRange(AngleCalibrationSession.AngleRange);
        numericDiameter.ApplyFieldRange(AngleCalibrationSession.DiameterRange);
        Fill(comboBoxBase, session.Bases);
        Fill(comboBoxGrid, AngleCalibrationSession.Grids);
        Fill(comboBoxReference, AngleCalibrationSession.References);
        textBoxName.Text = session.Name;
        Present();

        textBoxName.TextChanged += (_, _) => session.Name = textBoxName.Text;
        comboBoxBase.SelectedIndexChanged += (_, _) => session.BaseIndex = comboBoxBase.SelectedIndex;
        numericAngle.ValueChanged += (_, _) => Edit(() => session.SetAngle(numericAngle.Value));
        numericDiameter.ValueChanged += (_, _) => Edit(() => session.SetDiameter(numericDiameter.Value));
        comboBoxGrid.SelectedIndexChanged += (_, _) => Edit(() => session.GridIndex = comboBoxGrid.SelectedIndex);
        comboBoxReference.SelectedIndexChanged += (_, _) =>
            Edit(() => session.ReferenceIndex = comboBoxReference.SelectedIndex);
        buttonOk.Click += (_, _) => session.CommitTo(this.definition);
    }

    private static void Fill<T>(ThemedComboBox combo, IReadOnlyList<T> options)
    {
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.Items.Clear();
        foreach (T option in options)
        {
            combo.Items.Add(option!);
        }
    }

    private void Edit(Action change)
    {
        if (presenting)
        {
            return;
        }

        change();
        Present();
    }

    private void Present()
    {
        presenting = true;
        try
        {
            if (numericAngle.Value != session.AngleDegrees)
            {
                numericAngle.Value = session.AngleDegrees;
            }

            if (numericDiameter.Value != session.DiameterMm)
            {
                numericDiameter.Value = session.DiameterMm;
            }

            comboBoxBase.SelectedIndex = session.BaseIndex;
            comboBoxGrid.SelectedIndex = session.GridIndex;
            comboBoxReference.SelectedIndex = session.ReferenceIndex;
            numericDiameter.Enabled = session.GeometryApplies;
            comboBoxGrid.Enabled = session.GeometryApplies;
            AngleCalibrationPreview preview = AngleCalibrationPreview.Build(session.Request);
            plotViewPreview.Model = BuildPreviewModel(preview.Points);
            plotViewPreview.InvalidatePlot(true);
            labelSummary.Text = preview.Summary;
        }
        finally
        {
            presenting = false;
        }
    }

    private static PlotModel BuildPreviewModel(IReadOnlyList<AngleCorrectionPoint> points)
    {
        PlotModel model = PlotModelStyle.CreatePreviewModel("Angular correction");
        PlotModelStyle.AddAxis(model, new LogarithmicAxis
        {
            Position = AxisPosition.Bottom,
            Minimum = AngleCalibrationPreview.MinimumHz,
            Maximum = AngleCalibrationPreview.MaximumHz,
            MajorGridlineStyle = LineStyle.Solid,
            MinorGridlineStyle = LineStyle.Dot,
            Title = "Hz",
            IsPanEnabled = false,
            IsZoomEnabled = false
        });
        PlotModelStyle.AddAxis(model, new LinearAxis
        {
            Position = AxisPosition.Left,
            MajorGridlineStyle = LineStyle.Solid,
            MinorGridlineStyle = LineStyle.Dot,
            Title = "dB",
            IsPanEnabled = false,
            IsZoomEnabled = false
        });
        var uncertainty = new AreaSeries
        {
            Color = OxyColors.Transparent,
            Color2 = OxyColors.Transparent,
            Fill = OxyColor.FromAColor(110, UiPalette.CurveTargetDefault.ToOxy())
        };
        uncertainty.Points.AddRange(points.Select(point => new DataPoint(point.FrequencyHz, point.LowerDb)));
        uncertainty.Points2.AddRange(points.Select(point => new DataPoint(point.FrequencyHz, point.UpperDb)));
        model.Series.Add(uncertainty);
        var line = new LineSeries
        {
            Color = UiPalette.CurveTargetDefault.ToOxy(),
            StrokeThickness = 2
        };
        line.Points.AddRange(points.Select(point => new DataPoint(point.FrequencyHz, point.CenterDb)));
        model.Series.Add(line);
        return model;
    }
}
