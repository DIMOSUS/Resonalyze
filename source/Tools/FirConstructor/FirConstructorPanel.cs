using System.ComponentModel;
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
    private static readonly OxyColor KernelColor = UiPalette.CurveKernel.ToOxy();
    private static readonly OxyColor TargetColor = OxyColor.FromAColor(200, UiPalette.CurveTarget.ToOxy());
    private static readonly OxyColor PhaseColor = UiPalette.CurvePhase.ToOxy();

    private const string MagnitudeAxisKey = "magnitude";
    private const string PhaseAxisKey = "phase";
    private const string AmplitudeAxisKey = "amplitude";

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
        ShowFileDialog = ShowOverForm;
        Warn = WarnOverForm;
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

    private void OnFamilyChanged(ThemedComboBox family, ThemedComboBox slope)
    {
        if (suppressEdits)
        {
            return;
        }

        int current = Selected(slope, FirConstructorChoices.DefaultSlope);
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

    private void OnTapsChanged()
    {
        if (suppressEdits)
        {
            return;
        }

        int taps = (int)numericTaps.Value;
        int odd = FirConstructorChoices.OddTapCount(taps);
        if (odd != taps)
        {
            suppressEdits = true;
            try
            {
                numericTaps.Value = odd;
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
        FirConstructorRebuild rebuild = session.ShowBare(
            bare, name, Selected(comboBoxSampleRate, FirConstructorChoices.DefaultRateHz));
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
                () => FirConstructorRender.Run(rebuild, token),
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

    private void ReturnToVirtualDsp()
    {
        if (FirConstructorAvailability.Return(session) is { } back)
        {
            ReturnFirRequested?.Invoke(back.Token, back.Kernel, back.Design);
        }
    }
}
