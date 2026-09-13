using System.ComponentModel;
using System.Globalization;
using System.Numerics;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// The FIR Constructor: designs a linear-phase low-pass, high-pass or band-pass
/// kernel and shows what it does — its magnitude and its phase, with no measurement
/// behind them. It stands alone (a rate of its own, a file on the way out) or edits
/// one Virtual DSP channel side, which it returns the kernel to.
/// </summary>
/// <remarks>
/// <para>
/// The panel holds either a DESIGN, from which the kernel is built on every edit, or a
/// bare kernel that arrived without one — a file imported here, or a kernel a Virtual
/// DSP side imported. A bare kernel is only shown: there is nothing to edit in it,
/// and the first touch of any control replaces it with a design built from the
/// controls.
/// </para>
/// <para>
/// In a handoff the rate is the processor's and cannot be changed here. A design that
/// arrives made at another rate is therefore rebuilt at the processor's on the way in,
/// which is the rebuild the block's red FIR button asks for.
/// </para>
/// </remarks>
public partial class FirConstructorPanel : UserControl
{
    private static readonly int[] SampleRates = [44_100, 48_000, 88_200, 96_000, 176_400, 192_000];

    private static readonly OxyColor KernelColor = OxyColor.FromRgb(90, 180, 255);
    private static readonly OxyColor TargetColor = OxyColor.FromArgb(200, 230, 184, 0);

    private readonly PlotModel magnitudeModel;
    private readonly PlotModel phaseModel;
    private readonly LineSeries magnitudeSeries;
    private readonly LineSeries targetSeries;
    private readonly LineSeries phaseSeries;

    // What the plots show: the kernel, and the design it was built from — null for a
    // bare kernel. kernelName is the file a bare kernel came from.
    private FirFilter? kernel;
    private FirCrossoverDesign? design;
    private string? kernelName;

    // The running Virtual DSP session; null when the constructor stands alone.
    private FirConstructorReturnToken? virtualDspToken;
    private string? sessionLabel;

    // Set while the panel writes its own controls, so those writes are not edits.
    private bool suppressEdits;

    public FirConstructorPanel()
    {
        InitializeComponent();
        Ui.DarkScrollBars.Apply(this);

        magnitudeModel = CreateModel("Magnitude", "dB", -100, 10);
        targetSeries = new LineSeries
        {
            Title = "Target",
            Color = TargetColor,
            StrokeThickness = 1.5,
            LineStyle = LineStyle.Dash
        };
        magnitudeSeries = new LineSeries { Title = "Kernel", Color = KernelColor, StrokeThickness = 2 };
        magnitudeModel.Series.Add(targetSeries);
        magnitudeModel.Series.Add(magnitudeSeries);
        phaseModel = CreateModel("Phase, referenced to the kernel's peak", "°", -180, 180);
        ((LinearAxis)phaseModel.Axes[0]).MajorStep = 45;
        phaseSeries = new LineSeries { Color = KernelColor, StrokeThickness = 2 };
        phaseModel.Series.Add(phaseSeries);
        plotMagnitude.Model = magnitudeModel;
        plotPhase.Model = phaseModel;
        PlotInteraction.Enable(plotMagnitude);
        PlotInteraction.Enable(plotPhase);

        InitializeChoices();
        WireEvents();
        UpdateSessionControls();
        OnDesignEdited();
        Resize += (_, _) => LayoutPlots();
        LayoutPlots();
    }

    /// <summary>
    /// Raised when the user sends the designed kernel back to Virtual DSP. The host
    /// lands it and switches the mode, because the constructor knows nothing of other
    /// panels.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Action<FirConstructorReturnToken, FirFilter, FirCrossoverDesign>? ReturnFirRequested { get; set; }

    /// <summary>
    /// Raised when the user leaves the session without applying: nothing is written,
    /// and the constructor keeps the design.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Action? BackToVirtualDspRequested { get; set; }

    /// <summary>The design on screen, or null when a bare kernel is shown or the controls cannot build one.</summary>
    internal FirCrossoverDesign? CurrentDesign => design;

    /// <summary>The kernel on screen, or null when the controls cannot build one.</summary>
    internal FirFilter? CurrentKernel => kernel;

    /// <summary>Whether a Virtual DSP session is running.</summary>
    internal bool InVirtualDspHandoff => virtualDspToken != null;

    /// <summary>
    /// Installs a channel side sent over by Virtual DSP: its design (rebuilt at the
    /// processor's rate) or its bare kernel, or a first design started at the side's
    /// IIR corners; the rate is locked to the processor's and the Return button shows.
    /// </summary>
    internal void BeginVirtualDspHandoff(FirConstructorHandoffRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        virtualDspToken = request.Token;
        sessionLabel = request.ChannelLabel;
        string? rebuiltNote = null;
        suppressEdits = true;
        try
        {
            SelectRate(request.ProcessorSampleRateHz);
            if (request.Design is { } arrived)
            {
                WriteControls(arrived);
                if (arrived.SampleRateHz != request.ProcessorSampleRateHz)
                {
                    rebuiltNote =
                        $"It was designed at {FirCrossoverDescription.Rate(arrived.SampleRateHz)} and is " +
                        $"rebuilt here at {FirCrossoverDescription.Rate(request.ProcessorSampleRateHz)}.";
                }
            }
            else if (request.SeedCrossover is { } seed)
            {
                WriteSeed(seed);
            }
        }
        finally
        {
            suppressEdits = false;
        }

        UpdateSessionControls(rebuiltNote);
        if (request.Design == null && request.Kernel is { } bare)
        {
            ShowBareKernel(bare, request.KernelName);
        }
        else
        {
            OnDesignEdited();
        }
    }

    /// <summary>
    /// Ends the Virtual DSP session, if any: the Return button goes, and the rate is
    /// the constructor's own again. The design on screen stays.
    /// </summary>
    internal void EndVirtualDspHandoff()
    {
        virtualDspToken = null;
        sessionLabel = null;
        UpdateSessionControls();
        UpdateActions();
    }

    // ---------------------------------------------------------------- set-up

    private void InitializeChoices()
    {
        suppressEdits = true;
        try
        {
            comboBoxType.Items.AddRange(
            [
                new Choice<CrossoverKind>(CrossoverKind.LowPass, "Low pass"),
                new Choice<CrossoverKind>(CrossoverKind.HighPass, "High pass"),
                new Choice<CrossoverKind>(CrossoverKind.BandPass, "Band pass")
            ]);
            comboBoxType.SelectedIndex = 0;
            comboBoxMethod.Items.AddRange(
            [
                new Choice<FirCrossoverMethod>(FirCrossoverMethod.IirMagnitude, "IIR magnitude"),
                new Choice<FirCrossoverMethod>(FirCrossoverMethod.WindowedSinc, "Windowed sinc")
            ]);
            comboBoxMethod.SelectedIndex = 0;
            foreach (DarkComboBox family in new[] { comboBoxHighPassFamily, comboBoxLowPassFamily })
            {
                foreach (CrossoverFilterFamily value in FirCrossoverDesign.IirFamilies)
                {
                    family.Items.Add(new Choice<CrossoverFilterFamily>(value, FirCrossoverDescription.FamilyName(value)));
                }

                family.SelectedIndex = 0;
            }

            FillSlopes(comboBoxHighPassSlope, CrossoverFilterFamily.LinkwitzRiley, 24);
            FillSlopes(comboBoxLowPassSlope, CrossoverFilterFamily.LinkwitzRiley, 24);
            foreach (FirWindow window in Enum.GetValues<FirWindow>())
            {
                comboBoxWindow.Items.Add(new Choice<FirWindow>(window, window.ToString()));
            }

            SelectChoice(comboBoxWindow, FirWindow.Kaiser);
            foreach (int rate in SampleRates)
            {
                comboBoxSampleRate.Items.Add(new Choice<int>(rate, FirCrossoverDescription.Rate(rate)));
            }

            SelectChoice(comboBoxSampleRate, 48_000);
        }
        finally
        {
            suppressEdits = false;
        }
    }

    private void WireEvents()
    {
        foreach (DarkComboBox combo in new[]
                 {
                     comboBoxType, comboBoxMethod, comboBoxHighPassSlope, comboBoxLowPassSlope,
                     comboBoxWindow, comboBoxSampleRate
                 })
        {
            combo.SelectedIndexChanged += (_, _) => OnDesignEdited();
        }

        comboBoxHighPassFamily.SelectedIndexChanged += (_, _) =>
            OnFamilyChanged(comboBoxHighPassFamily, comboBoxHighPassSlope);
        comboBoxLowPassFamily.SelectedIndexChanged += (_, _) =>
            OnFamilyChanged(comboBoxLowPassFamily, comboBoxLowPassSlope);
        foreach (DarkNumericUpDown numeric in new[] { numericHighPassHz, numericLowPassHz, numericKaiserBeta })
        {
            numeric.ValueChanged += (_, _) => OnDesignEdited();
        }

        numericTaps.ValueChanged += (_, _) => OnTapsChanged();
        buttonImport.Click += (_, _) => ImportFile();
        buttonExport.Click += (_, _) => ExportFile();
        buttonReturnToDsp.Click += (_, _) => ReturnToVirtualDsp();
        buttonBackToDsp.Click += (_, _) => BackToVirtualDspRequested?.Invoke();
    }

    private static PlotModel CreateModel(string title, string unit, double minimum, double maximum)
    {
        PlotModel model = PlotModelStyle.CreateTitledModel(title);
        model.TitleFontSize = 12;
        PlotModelStyle.AddFrequencyAxis(model);
        PlotModelStyle.InsertAxis(model, 0, new LinearAxis
        {
            Position = AxisPosition.Left,
            Minimum = minimum,
            Maximum = maximum,
            MajorGridlineStyle = LineStyle.Solid,
            MinorGridlineStyle = LineStyle.Dot,
            Title = unit
        });
        return model;
    }

    // The two plots share the height beside the controls, half each.
    private void LayoutPlots()
    {
        int gap = plotPhase.Top - plotMagnitude.Bottom;
        int available = ClientSize.Height - plotMagnitude.Top * 2 - gap;
        if (available < 2)
        {
            return;
        }

        plotMagnitude.Height = available / 2;
        plotPhase.Top = plotMagnitude.Bottom + gap;
        plotPhase.Height = available - available / 2;
    }

    // ---------------------------------------------------------------- editing

    private void OnFamilyChanged(DarkComboBox family, DarkComboBox slope)
    {
        if (suppressEdits)
        {
            return;
        }

        int current = slope.SelectedItem is Choice<int> choice ? choice.Value : 24;
        suppressEdits = true;
        try
        {
            FillSlopes(slope, Selected(family, CrossoverFilterFamily.LinkwitzRiley), current);
        }
        finally
        {
            suppressEdits = false;
        }

        OnDesignEdited();
    }

    // An even count is stepped to the odd one beside it in the direction the user was
    // going, rather than refused: the arrows step by two from an odd start, so only a
    // typed number lands here.
    private void OnTapsChanged()
    {
        if (suppressEdits)
        {
            return;
        }

        int taps = (int)numericTaps.Value;
        if (taps % 2 == 0)
        {
            suppressEdits = true;
            try
            {
                numericTaps.Value = taps + 1 <= FirCrossoverDesign.MaximumTapCount ? taps + 1 : taps - 1;
            }
            finally
            {
                suppressEdits = false;
            }
        }

        OnDesignEdited();
    }

    /// <summary>
    /// Rebuilds the design and its kernel from the controls. A bare kernel on screen
    /// is replaced — any touch of a control means "design one".
    /// </summary>
    private void OnDesignEdited()
    {
        if (suppressEdits)
        {
            return;
        }

        UpdateControlAvailability();
        FirCrossoverDesign candidate = ReadControls();
        kernelName = null;
        if (candidate.Problem() is { } problem)
        {
            design = null;
            kernel = null;
            labelProblem.ForeColor = Ui.UiPalette.ErrorSoft;
            labelProblem.Text = problem;
        }
        else
        {
            design = candidate;
            kernel = candidate.Build();
            // Buildable, but measured to fool Auto delay at a low junction: said here,
            // where the length is chosen, rather than discovered as a wrong delay.
            labelProblem.ForeColor = Ui.UiPalette.WarningAmber;
            labelProblem.Text = candidate.MayMisleadAutoDelay
                ? $"Below {FirCrossoverDesign.AutoDelayLowCornerHz:0} Hz a kernel this long can mislead Auto delay " +
                  $"by tens of ms: check its result, or keep the latency under {FirCrossoverDesign.AutoDelaySafeLatencyMs:0} ms."
                : string.Empty;
        }

        Redraw();
    }

    private void ShowBareKernel(FirFilter bare, string? name)
    {
        design = null;
        kernel = bare;
        kernelName = name;
        labelProblem.Text = string.Empty;
        Redraw();
    }

    private FirCrossoverDesign ReadControls() =>
        new(
            Selected(comboBoxType, CrossoverKind.LowPass),
            new CrossoverEdge(
                Selected(comboBoxLowPassFamily, CrossoverFilterFamily.LinkwitzRiley),
                (double)numericLowPassHz.Value,
                Selected(comboBoxLowPassSlope, 24)),
            new CrossoverEdge(
                Selected(comboBoxHighPassFamily, CrossoverFilterFamily.LinkwitzRiley),
                (double)numericHighPassHz.Value,
                Selected(comboBoxHighPassSlope, 24)),
            Selected(comboBoxMethod, FirCrossoverMethod.IirMagnitude),
            Selected(comboBoxWindow, FirWindow.Kaiser),
            (double)numericKaiserBeta.Value,
            (int)numericTaps.Value,
            Selected(comboBoxSampleRate, 48_000));

    // Writes a design into the controls; the rate stays what the caller selected.
    private void WriteControls(FirCrossoverDesign source)
    {
        SelectChoice(comboBoxType, source.Kind);
        SelectChoice(comboBoxMethod, source.Method);
        WriteEdge(source.HighPassEdge, numericHighPassHz, comboBoxHighPassFamily, comboBoxHighPassSlope);
        WriteEdge(source.LowPassEdge, numericLowPassHz, comboBoxLowPassFamily, comboBoxLowPassSlope);
        SelectChoice(comboBoxWindow, source.Window);
        numericKaiserBeta.Value = numericKaiserBeta.ClampValue(source.KaiserBeta);
        numericTaps.Value = numericTaps.ClampValue(source.TapCount);
    }

    // The side's IIR crossover as a starting point: its kind and corners, and its
    // family and slope where the constructor offers them.
    private void WriteSeed(CrossoverSpec seed)
    {
        if (seed.Kind is CrossoverKind.Off)
        {
            return;
        }

        SelectChoice(comboBoxType, seed.Kind);
        if (seed.HighPassEdge is { } highPass)
        {
            WriteEdge(highPass, numericHighPassHz, comboBoxHighPassFamily, comboBoxHighPassSlope);
        }
        if (seed.LowPassEdge is { } lowPass)
        {
            WriteEdge(lowPass, numericLowPassHz, comboBoxLowPassFamily, comboBoxLowPassSlope);
        }
    }

    private static void WriteEdge(
        CrossoverEdge edge, DarkNumericUpDown frequency, DarkComboBox family, DarkComboBox slope)
    {
        frequency.Value = frequency.ClampValue(edge.FrequencyHz);
        CrossoverFilterFamily offered = FirCrossoverDesign.IirFamilies.Contains(edge.Family)
            ? edge.Family
            : CrossoverFilterFamily.LinkwitzRiley;
        SelectChoice(family, offered);
        FillSlopes(slope, offered, edge.SlopeDbPerOctave);
    }

    private void SelectRate(int rate)
    {
        if (!comboBoxSampleRate.Items.OfType<Choice<int>>().Any(choice => choice.Value == rate))
        {
            comboBoxSampleRate.Items.Add(new Choice<int>(rate, FirCrossoverDescription.Rate(rate)));
        }

        SelectChoice(comboBoxSampleRate, rate);
    }

    // The slopes a family offers, with the nearest one to the slope asked for selected.
    private static void FillSlopes(DarkComboBox slope, CrossoverFilterFamily family, int preferred)
    {
        IReadOnlyList<int> slopes = CrossoverFilter.SupportedSlopes(family);
        slope.Items.Clear();
        foreach (int value in slopes)
        {
            slope.Items.Add(new Choice<int>(value, $"{value} dB/oct"));
        }

        int nearest = slopes.OrderBy(value => Math.Abs(value - preferred)).First();
        SelectChoice(slope, nearest);
    }

    private void UpdateControlAvailability()
    {
        CrossoverKind kind = Selected(comboBoxType, CrossoverKind.LowPass);
        bool usesHigh = kind is CrossoverKind.HighPass or CrossoverKind.BandPass;
        bool usesLow = kind is CrossoverKind.LowPass or CrossoverKind.BandPass;
        bool iir = Selected(comboBoxMethod, FirCrossoverMethod.IirMagnitude) == FirCrossoverMethod.IirMagnitude;
        SetEnabled(usesHigh, labelHighPass, numericHighPassHz);
        SetEnabled(usesHigh && iir, comboBoxHighPassFamily, comboBoxHighPassSlope);
        SetEnabled(usesLow, labelLowPass, numericLowPassHz);
        SetEnabled(usesLow && iir, comboBoxLowPassFamily, comboBoxLowPassSlope);
        SetEnabled(Selected(comboBoxWindow, FirWindow.Kaiser) == FirWindow.Kaiser, labelKaiserBeta, numericKaiserBeta);
    }

    private static void SetEnabled(bool enabled, params Control[] controls)
    {
        foreach (Control control in controls)
        {
            if (control is Label label)
            {
                Ui.UiStyle.SetTextEnabledLook(label, enabled);
            }
            else
            {
                control.Enabled = enabled;
            }
        }
    }

    // ---------------------------------------------------------------- session

    private void UpdateSessionControls(string? note = null)
    {
        bool linked = virtualDspToken != null;
        buttonReturnToDsp.Visible = linked;
        buttonBackToDsp.Visible = linked;
        comboBoxSampleRate.Enabled = !linked;
        labelSession.Text = linked
            ? $"Editing {sessionLabel}. The rate is the processor's." + (note == null ? string.Empty : " " + note)
            : "Standalone: design a kernel and export it to a file.";
    }

    private void UpdateActions()
    {
        buttonExport.Enabled = kernel != null;
        // Only a DESIGN returns: a bare kernel came from a file, and a file is imported
        // on the Virtual DSP side, where it keeps its name.
        buttonReturnToDsp.Enabled = virtualDspToken != null && kernel != null && design != null;
    }

    private void ReturnToVirtualDsp()
    {
        if (virtualDspToken is { } token && kernel is { } built && design is { } designed)
        {
            ReturnFirRequested?.Invoke(token, built, designed);
        }
    }

    // ---------------------------------------------------------------- files

    private void ImportFile()
    {
        using var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = FirFilterFiles.ImportFileDialogFilter,
            Title = "Open FIR filter"
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        try
        {
            ShowBareKernel(FirFilterFiles.Load(dialog.FileName), Path.GetFileName(dialog.FileName));
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                FindForm(),
                "FIR filter could not be opened." + Environment.NewLine + Environment.NewLine + exception.Message,
                "FIR Constructor",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void ExportFile()
    {
        if (kernel is not { } exported)
        {
            return;
        }

        using var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = "wav",
            Filter = FirFilterFiles.ExportFileDialogFilter,
            FileName = design is { } named
                ? $"FIR {FirCrossoverDescription.Short(named)}"
                : Path.GetFileNameWithoutExtension(kernelName) is { Length: > 0 } stem ? stem : "FIR",
            OverwritePrompt = true,
            Title = "Export FIR filter"
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        try
        {
            FirFilterFiles.Save(
                dialog.FileName,
                exported,
                DisplayRate,
                kernelName,
                design is { } described ? FirCrossoverDescription.Long(described) : null);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                FindForm(),
                "FIR filter could not be exported." + Environment.NewLine + Environment.NewLine + exception.Message,
                "FIR Constructor",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    // The rate the kernel on screen is read at: its design's, or the selected one for a
    // bare kernel — which is what a Virtual DSP processor would run it at too.
    private int DisplayRate => design?.SampleRateHz ?? Selected(comboBoxSampleRate, 48_000);

    // ---------------------------------------------------------------- plots

    private void Redraw()
    {
        magnitudeSeries.Points.Clear();
        targetSeries.Points.Clear();
        phaseSeries.Points.Clear();
        int rate = DisplayRate;
        if (kernel is { } shown)
        {
            double highHz = Math.Min(20_000, rate / 2.0);
            const int Points = 800;
            double peakSamples = shown.PeakIndex;
            for (int i = 0; i <= Points; i++)
            {
                double frequency = 20 * Math.Pow(highHz / 20, (double)i / Points);
                Complex response = shown.Response(frequency, rate);
                double magnitudeDb = 20 * Math.Log10(Math.Max(response.Magnitude, 1e-10));
                magnitudeSeries.Points.Add(new DataPoint(frequency, magnitudeDb));
                if (design is { HasTargetMagnitude: true } target)
                {
                    double targetDb = 20 * Math.Log10(Math.Max(target.TargetMagnitude(frequency), 1e-10));
                    targetSeries.Points.Add(new DataPoint(frequency, targetDb));
                }

                // Referenced to the peak: a linear-phase kernel's delay removed, it reads
                // 0° in its passband (180° past a zero) instead of a phase wrapped
                // thousands of times; for a kernel that is not linear-phase the peak is
                // simply the stated reference.
                Complex aligned = response *
                    Complex.FromPolarCoordinates(1, Math.Tau * frequency * peakSamples / rate);
                double phase = response.Magnitude > 1e-9
                    ? aligned.Phase * 180 / Math.PI
                    : double.NaN;
                phaseSeries.Points.Add(new DataPoint(frequency, phase));
            }
        }

        UpdateReadouts(rate);
        UpdateActions();
        magnitudeModel.InvalidatePlot(true);
        phaseModel.InvalidatePlot(true);
    }

    private void UpdateReadouts(int rate)
    {
        if (kernel is not { } shown)
        {
            labelLatency.Text = string.Empty;
            labelDeviation.Text = string.Empty;
            return;
        }

        if (design is { } designed)
        {
            labelLatency.Text = string.Create(
                CultureInfo.InvariantCulture,
                $"Latency {designed.LatencyMs:0.00} ms ({designed.LatencySamples} samples at {FirCrossoverDescription.Rate(rate)})");
            double deviation = designed.WorstDeviationDb(shown);
            labelDeviation.Text = double.IsNaN(deviation)
                ? "A brick wall has no slope to compare with: read the plot."
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"Worst deviation from the target: {deviation:0.00} dB (where it is above −30 dB)");
        }
        else
        {
            labelLatency.Text = string.Create(
                CultureInfo.InvariantCulture,
                $"{kernelName ?? "Kernel"}: {shown.Length} taps, shown as it is at {FirCrossoverDescription.Rate(rate)}");
            labelDeviation.Text = "Any change to the controls designs a new kernel in its place.";
        }
    }

    // ---------------------------------------------------------------- choices

    private static T Selected<T>(DarkComboBox combo, T fallback) =>
        combo.SelectedItem is Choice<T> choice ? choice.Value : fallback;

    private static void SelectChoice<T>(DarkComboBox combo, T value)
    {
        foreach (object? item in combo.Items)
        {
            if (item is Choice<T> choice && EqualityComparer<T>.Default.Equals(choice.Value, value))
            {
                combo.SelectedItem = item;
                return;
            }
        }
    }

    // A combo entry: the value, and the text the list shows for it.
    private sealed record Choice<T>(T Value, string Text)
    {
        public override string ToString() => Text;
    }
}
