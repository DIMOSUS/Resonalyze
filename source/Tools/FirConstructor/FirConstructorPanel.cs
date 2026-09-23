using System.ComponentModel;
using System.Globalization;
using System.Numerics;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Designs linear-phase LP/HP/BP FIR kernels, standalone or for one Virtual DSP channel side.</summary>
/// <remarks>Binds the controls and plots to a <see cref="FirConstructorSession"/>. Every edit designs a new kernel in the
/// background after a short settle; only the latest lands, and Export/Return wait meanwhile.</remarks>
public partial class FirConstructorPanel : UserControl
{
    private static readonly int[] SampleRates = [44_100, 48_000, 88_200, 96_000, 176_400, 192_000];

    private static readonly OxyColor KernelColor = UiPalette.CurveKernel.ToOxy();
    private static readonly OxyColor TargetColor = OxyColor.FromAColor(200, UiPalette.CurveTarget.ToOxy());
    private static readonly OxyColor PhaseColor = UiPalette.CurvePhase.ToOxy();

    private const string MagnitudeAxisKey = "magnitude";
    private const string PhaseAxisKey = "phase";
    private const string AmplitudeAxisKey = "amplitude";

    // Phase is hidden this far below the peak: there it is tap rounding flipping between +-180.
    private const double PhaseFloorDb = 60;

    private const double ImpulseFloorDb = 120;

    private readonly PlotModel responseModel;
    private readonly PlotModel impulseModel;
    private readonly LineSeries magnitudeSeries;
    private readonly LineSeries targetSeries;
    private readonly LineSeries phaseSeries;
    private readonly LineSeries impulseSeries;
    private readonly LinearAxis amplitudeAxis;

    private readonly FirConstructorSession session = new();

    private bool suppressEdits;

    private const int RebuildSettleMs = 60;

    public FirConstructorPanel()
    {
        InitializeComponent();
        Ui.ThemedScrollBars.Apply(this);

        responseModel = PlotModelStyle.CreateTitledModel("Magnitude and phase (phase referenced to the kernel's delay)");
        responseModel.TitleFontSize = 12;
        PlotModelStyle.AddFrequencyAxis(responseModel);
        PlotModelStyle.InsertAxis(responseModel, 0, new LinearAxis
        {
            Key = MagnitudeAxisKey,
            Position = AxisPosition.Left,
            Minimum = -100,
            Maximum = 10,
            MajorGridlineStyle = LineStyle.Solid,
            MinorGridlineStyle = LineStyle.Dot,
            Title = "dB"
        });
        PlotModelStyle.AddAxis(responseModel, new LinearAxis
        {
            Key = PhaseAxisKey,
            Position = AxisPosition.Right,
            Minimum = -180,
            Maximum = 180,
            MajorStep = 90,
            MajorGridlineStyle = LineStyle.None,
            MinorGridlineStyle = LineStyle.None,
            TextColor = PhaseColor,
            TitleColor = PhaseColor,
            TicklineColor = PhaseColor,
            Title = "Phase (°)"
        });
        targetSeries = new LineSeries
        {
            Title = "Target",
            Color = TargetColor,
            StrokeThickness = 1.5,
            LineStyle = LineStyle.Dash,
            YAxisKey = MagnitudeAxisKey
        };
        magnitudeSeries = new LineSeries
        {
            Title = "Magnitude",
            Color = KernelColor,
            StrokeThickness = 2,
            YAxisKey = MagnitudeAxisKey
        };
        phaseSeries = new LineSeries
        {
            Title = "Phase",
            Color = OxyColor.FromAColor(200, PhaseColor),
            StrokeThickness = 1,
            YAxisKey = PhaseAxisKey
        };
        responseModel.Series.Add(targetSeries);
        responseModel.Series.Add(phaseSeries);
        responseModel.Series.Add(magnitudeSeries);

        impulseModel = PlotModelStyle.CreateTitledModel("Impulse response (time from the kernel's peak)");
        impulseModel.TitleFontSize = 12;
        PlotModelStyle.AddAxis(impulseModel, new LinearAxis
        {
            Position = AxisPosition.Bottom,
            MajorGridlineStyle = LineStyle.Solid,
            MinorGridlineStyle = LineStyle.Dot,
            Title = "ms"
        });
        amplitudeAxis = new LinearAxis
        {
            Key = AmplitudeAxisKey,
            Position = AxisPosition.Left,
            MajorGridlineStyle = LineStyle.Solid,
            MinorGridlineStyle = LineStyle.Dot,
            Title = "Amplitude"
        };
        PlotModelStyle.AddAxis(impulseModel, amplitudeAxis);
        // Decimated: an imported kernel may carry 131072 taps, and GDI+ through all of them takes seconds per repaint.
        impulseSeries = new LineSeries
        {
            Color = KernelColor,
            StrokeThickness = 1,
            YAxisKey = AmplitudeAxisKey,
            Decimator = Decimator.Decimate
        };
        impulseModel.Series.Add(impulseSeries);

        plotResponse.Model = responseModel;
        plotImpulse.Model = impulseModel;
        PlotInteraction.Enable(plotResponse);
        PlotInteraction.Enable(plotImpulse);

        InitializeChoices();
        WireEvents();
        UpdateSessionControls();
        OnDesignEdited();
        Resize += (_, _) => LayoutPlots();
        LayoutPlots();
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Action<FirConstructorReturnToken, FirFilter, FirCrossoverDesign>? ReturnFirRequested { get; set; }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Action? BackToVirtualDspRequested { get; set; }

    internal FirCrossoverDesign? CurrentDesign => session.Design;

    internal FirFilter? CurrentKernel => session.Kernel;

    internal bool InVirtualDspHandoff => session.InHandoff;

    internal bool RebuildPending => session.RebuildPending;

    /// <summary>Installs a side's design (rebuilt at the processor rate), bare kernel, or a seed from its IIR corners.</summary>
    internal void BeginVirtualDspHandoff(FirConstructorHandoffRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        session.BeginHandoff(request, ReadControls());
        suppressEdits = true;
        try
        {
            SelectRate(request.ProcessorSampleRateHz);
            if (request.Design is { } arrived)
            {
                WriteControls(arrived);
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

        UpdateSessionControls();
        if (request.Design == null && request.Kernel is { } bare)
        {
            ShowBareKernel(bare, request.KernelName);
        }
        else
        {
            OnDesignEdited();
        }
    }

    /// <summary>Restores the pre-session standalone work. Back to Virtual DSP does not end a session.</summary>
    internal void EndVirtualDspHandoff()
    {
        FirConstructorStandaloneWork? work = session.EndHandoff();
        UpdateSessionControls();
        if (work == null)
        {
            UpdateActions();
            return;
        }

        suppressEdits = true;
        try
        {
            SelectRate(work.Controls.SampleRateHz);
            WriteControls(work.Controls);
        }
        finally
        {
            suppressEdits = false;
        }

        if (work.BareKernel is { } bare)
        {
            UpdateControlAvailability();
            ShowBareKernel(bare, work.BareName);
        }
        else
        {
            OnDesignEdited();
        }
    }

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
            foreach (ThemedComboBox family in new[] { comboBoxHighPassFamily, comboBoxLowPassFamily })
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
        foreach (ThemedComboBox combo in new[]
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
        foreach (ThemedNumericUpDown numeric in new[] { numericHighPassHz, numericLowPassHz, numericKaiserBeta })
        {
            numeric.ValueChanged += (_, _) => OnDesignEdited();
        }

        numericTaps.ValueChanged += (_, _) => OnTapsChanged();
        buttonImport.Click += (_, _) => ImportFile();
        buttonExport.Click += (_, _) => ExportFile();
        buttonReturnToDsp.Click += (_, _) => ReturnToVirtualDsp();
        buttonBackToDsp.Click += (_, _) => BackToVirtualDspRequested?.Invoke();
        checkBoxImpulseDb.CheckedChanged += (_, _) => ApplyImpulse(session.Rendering, rescale: true);
    }

    private void LayoutPlots()
    {
        int gap = plotImpulse.Top - plotResponse.Bottom;
        int available = ClientSize.Height - plotResponse.Top * 2 - gap;
        if (available < 2)
        {
            return;
        }

        plotResponse.Height = available / 2;
        plotImpulse.Top = plotResponse.Bottom + gap;
        plotImpulse.Height = available - available / 2;
    }

    private void OnFamilyChanged(ThemedComboBox family, ThemedComboBox slope)
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

    // Only a typed even count lands here (arrows step by two); stepped to the odd neighbour in the user's direction.
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

    private void OnDesignEdited()
    {
        if (suppressEdits)
        {
            return;
        }

        UpdateControlAvailability();
        FirFilter? before = session.Kernel;
        FirConstructorRebuild? rebuild = session.Edit(ReadControls());
        labelProblem.Text = session.Problem;
        if (rebuild == null)
        {
            ApplyRendering(before);
            return;
        }

        _ = ShowAsync(rebuild);
    }

    private void ShowBareKernel(FirFilter bare, string? name)
    {
        FirConstructorRebuild rebuild = session.ShowBare(bare, name, Selected(comboBoxSampleRate, 48_000));
        labelProblem.Text = session.Problem;
        _ = ShowAsync(rebuild);
    }

    private async Task ShowAsync(FirConstructorRebuild rebuild)
    {
        using FirConstructorRebuild owned = rebuild;
        UpdateActions();
        CancellationToken token = rebuild.Token;
        try
        {
            if (rebuild.Settle)
            {
                await Task.Delay(RebuildSettleMs, token);
            }

            FirConstructorRendering rendering = await Task.Run(
                () => Render(rebuild.BareKernel ?? rebuild.Design!.Build(), rebuild.Design, rebuild.RateHz, token),
                token);
            FirFilter? before = session.Kernel;
            if (IsDisposed || !session.Land(rebuild, rendering))
            {
                return;
            }

            ApplyRendering(before);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (session.IsCurrent(rebuild) && !IsDisposed)
        {
            FirFilter? before = session.Kernel;
            session.Fail(exception.Message);
            labelProblem.Text = session.Problem;
            ApplyRendering(before);
        }
        finally
        {
            session.Finish(rebuild);
        }
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
        CrossoverEdge edge, ThemedNumericUpDown frequency, ThemedComboBox family, ThemedComboBox slope)
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

    private static void FillSlopes(ThemedComboBox slope, CrossoverFilterFamily family, int preferred)
    {
        IReadOnlyList<int> slopes = FirCrossoverDesign.SupportedSlopes(family);
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

    private void UpdateSessionControls()
    {
        bool linked = session.Handoff != null;
        buttonReturnToDsp.Visible = linked;
        buttonBackToDsp.Visible = linked;
        comboBoxSampleRate.Enabled = !linked;
        string? note = session.Handoff is { Design: { } arrived } request &&
            arrived.SampleRateHz != request.ProcessorSampleRateHz
                ? $"It was designed at {FirCrossoverDescription.Rate(arrived.SampleRateHz)} and is " +
                    $"rebuilt here at {FirCrossoverDescription.Rate(request.ProcessorSampleRateHz)}."
                : null;
        labelSession.Text = linked
            ? $"Editing {session.Handoff!.ChannelLabel}. The rate is the processor's." +
                (note == null ? string.Empty : " " + note)
            : "Standalone: design a kernel and export it to a file.";
    }

    private void UpdateActions()
    {
        buttonExport.Enabled = session.Kernel != null && !session.RebuildPending;
        // Only a design returns; bare kernel files are imported on the Virtual DSP side, where they keep their name.
        buttonReturnToDsp.Enabled =
            session.Handoff != null && session.Kernel != null && session.Design != null && !session.RebuildPending;
    }

    private void ReturnToVirtualDsp()
    {
        if (!session.RebuildPending && session.Handoff is { Token: var token } &&
            session.Kernel is { } built && session.Design is { } designed)
        {
            ReturnFirRequested?.Invoke(token, built, designed);
        }
    }

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
        if (session.RebuildPending || session.Kernel is not { } exported)
        {
            return;
        }

        using var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = "wav",
            Filter = FirFilterFiles.ExportFileDialogFilter,
            FileName = session.Design is { } named
                ? $"FIR {FirCrossoverDescription.Short(named)}"
                : Path.GetFileNameWithoutExtension(session.KernelName) is { Length: > 0 } stem ? stem : "FIR",
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
                session.RateHz,
                session.KernelName,
                session.Design is { } described ? FirCrossoverDescription.Long(described) : null);
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

    private static FirConstructorRendering Render(
        FirFilter shown,
        FirCrossoverDesign? designed,
        int rate,
        CancellationToken cancellation)
    {
        double highHz = Math.Min(20_000, rate / 2.0);
        const int Points = 800;
        // Symmetric kernel: exact centre (N-1)/2 (half-sample off grid for even N); otherwise the peak.
        double referenceSamples = shown.IsSymmetric ? shown.LinearPhaseDelaySamples : shown.PeakIndex;
        var magnitude = new DataPoint[Points + 1];
        var target = new List<DataPoint>(designed is { HasTargetMagnitude: true } ? Points + 1 : 0);
        var phase = new DataPoint[Points + 1];
        double loudestDb = double.NegativeInfinity;
        for (int i = 0; i <= Points; i++)
        {
            if (i % 50 == 0)
            {
                cancellation.ThrowIfCancellationRequested();
            }

            double frequency = 20 * Math.Pow(highHz / 20, (double)i / Points);
            Complex response = shown.Response(frequency, rate);
            magnitude[i] = new DataPoint(frequency, 20 * Math.Log10(Math.Max(response.Magnitude, 1e-10)));
            loudestDb = Math.Max(loudestDb, magnitude[i].Y);
            if (designed is { HasTargetMagnitude: true })
            {
                double targetDb = 20 * Math.Log10(Math.Max(designed.TargetMagnitude(frequency), 1e-10));
                target.Add(new DataPoint(frequency, targetDb));
            }

            // Removing the linear-phase delay reads 0 deg in the passband instead of thousands of wraps.
            Complex aligned = response *
                Complex.FromPolarCoordinates(1, Math.Tau * frequency * referenceSamples / rate);
            phase[i] = new DataPoint(
                frequency,
                response.Magnitude > 1e-9 ? aligned.Phase * 180 / Math.PI : double.NaN);
        }

        for (int i = 0; i <= Points; i++)
        {
            if (magnitude[i].Y < loudestDb - PhaseFloorDb)
            {
                phase[i] = new DataPoint(phase[i].X, double.NaN);
            }
        }

        // Negative time is the pre-ringing a linear-phase kernel costs.
        ReadOnlySpan<double> taps = shown.Taps;
        double largest = Math.Max(Math.Abs(taps[shown.PeakIndex]), double.Epsilon);
        var impulse = new DataPoint[taps.Length];
        var impulseDb = new DataPoint[taps.Length];
        for (int i = 0; i < taps.Length; i++)
        {
            double timeMs = (i - shown.PeakIndex) * 1_000.0 / rate;
            impulse[i] = new DataPoint(timeMs, taps[i]);
            impulseDb[i] = new DataPoint(
                timeMs,
                Math.Max(20 * Math.Log10(Math.Abs(taps[i]) / largest), -ImpulseFloorDb));
        }

        cancellation.ThrowIfCancellationRequested();
        double deviation = designed?.WorstDeviationDb(shown) ?? double.NaN;
        return new FirConstructorRendering(shown, magnitude, target.ToArray(), phase, impulse, impulseDb, deviation);
    }

    // Before is the kernel shown until now: a new one refits the impulse view, the same one redrawn keeps the zoom.
    private void ApplyRendering(FirFilter? before)
    {
        FirConstructorRendering? rendering = session.Rendering;
        magnitudeSeries.Points.Clear();
        targetSeries.Points.Clear();
        phaseSeries.Points.Clear();
        if (rendering != null)
        {
            magnitudeSeries.Points.AddRange(rendering.Magnitude);
            targetSeries.Points.AddRange(rendering.Target);
            phaseSeries.Points.AddRange(rendering.Phase);
        }

        UpdateReadouts(session.RateHz, rendering?.DeviationDb ?? double.NaN);
        UpdateActions();
        responseModel.InvalidatePlot(true);
        ApplyImpulse(rendering, rescale: !ReferenceEquals(before, rendering?.Kernel));
    }

    // A new kernel or scale refits the view; the same kernel redrawn keeps the user's zoom.
    private void ApplyImpulse(FirConstructorRendering? rendering, bool rescale)
    {
        impulseSeries.Points.Clear();
        bool decibels = checkBoxImpulseDb.Checked;
        if (rendering != null)
        {
            impulseSeries.Points.AddRange(decibels ? rendering.ImpulseDb : rendering.Impulse);
        }

        amplitudeAxis.Title = decibels ? "dB" : "Amplitude";
        if (rescale)
        {
            impulseModel.ResetAllAxes();
        }

        impulseModel.InvalidatePlot(true);
    }

    private void UpdateReadouts(int rate, double deviation)
    {
        if (session.Kernel is not { } shown)
        {
            labelLatency.Text = string.Empty;
            labelDeviation.Text = string.Empty;
            return;
        }

        if (session.Design is { } designed)
        {
            labelLatency.Text = string.Create(
                CultureInfo.InvariantCulture,
                $"Latency {designed.LatencyMs:0.00} ms ({designed.LatencySamples} samples at {FirCrossoverDescription.Rate(rate)})");
            labelDeviation.Text = double.IsNaN(deviation)
                ? "A brick wall has no slope to compare with: read the plot."
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"Worst deviation from the target: {deviation:0.00} dB above −30 dB");
        }
        else
        {
            labelLatency.Text = string.Create(
                CultureInfo.InvariantCulture,
                $"{session.KernelName ?? "Kernel"}: {shown.Length} taps, shown as it is at {FirCrossoverDescription.Rate(rate)}");
            labelDeviation.Text = "Any change to the controls designs a new kernel in its place.";
        }
    }

    private static T Selected<T>(ThemedComboBox combo, T fallback) =>
        combo.SelectedItem is Choice<T> choice ? choice.Value : fallback;

    private static void SelectChoice<T>(ThemedComboBox combo, T value)
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

    private sealed record Choice<T>(T Value, string Text)
    {
        public override string ToString() => Text;
    }
}
