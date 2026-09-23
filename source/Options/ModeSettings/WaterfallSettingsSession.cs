namespace Resonalyze.Options;

/// <summary>The Waterfall or Burst Decay settings panel's state, each field as its control shows it, with the read-outs
/// of the rate and of the time the slices cover. See docs/tech/mode-settings.md#code-map.</summary>
internal sealed class WaterfallSettingsSession
{
    private int lastNonZeroStep = 4;

    private WaterfallSettingsSession(bool burstDecay) => IsBurstDecay = burstDecay;

    public bool IsBurstDecay { get; }

    public TukeyFades Fades { get; } = new();

    /// <summary>Waterfall only.</summary>
    public int SliceCount { get; private set; }

    /// <summary>Waterfall only: never zero, a step onto zero goes on past it.</summary>
    public int Step { get; private set; }

    public int Offset { get; set; }

    public int DbRange { get; set; }

    public int SmoothingInverseOctaves { get; set; }

    /// <summary>Burst Decay only.</summary>
    public int Periods { get; set; }

    public decimal SampleRateShown { get; private set; }

    public decimal CaptureTimeMs { get; private set; }

    public ModeSettingsMeasurement Measurement { get; private set; } = new(null, 0);

    public SampleWindowPreview Preview =>
        new(Measurement.Result, Fades.Window, Fades.Left, Fades.Right, Offset, IrPreviewSource.Primary);

    public static WaterfallSettingsSession ForWaterfall() => new(false);

    public static WaterfallSettingsSession ForBurstDecay() => new(true);

    /// <summary>The rate and the time the slices cover follow the open measurement.</summary>
    public void Follow(ModeSettingsMeasurement measurement)
    {
        Measurement = measurement;
        SampleRateShown = ModeSettingsLimits.SampleRate.Clamp(measurement.SampleRate);
        CaptureTimeMs = ModeSettingsLimits.CaptureTimeMs.Assign((decimal)CapturedMs());
    }

    public void Load(WaterfallGenerateOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        SampleRateShown = ModeSettingsLimits.SampleRate.Clamp(Measurement.SampleRate);
        int window = (int)ModeSettingsLimits.WaterfallWindow.Clamp(options.Window);
        if (IsBurstDecay)
        {
            Fades.Load(window, options.LeftTukeyWindow, options.RightTukeyWindow);
            CaptureTimeMs = ModeSettingsLimits.CaptureTimeMs.Assign((decimal)CapturedMs());
            DbRange = (int)ModeSettingsLimits.DbRange.Assign(options.DbRange);
            SmoothingInverseOctaves = SmoothingPresetOptions.Normalize(options.SmoothingInverseOctaves, includePsychoacoustic: false);
            Offset = (int)ModeSettingsLimits.SampleOffset.Assign(options.Offset);
            Periods = (int)ModeSettingsLimits.Periods.Assign((int)options.Periods);
            return;
        }

        SliceCount = (int)ModeSettingsLimits.Slices.Clamp(options.SliceCount);
        Step = (int)ModeSettingsLimits.Step.Clamp(options.Step == 0 ? 1 : options.Step);
        lastNonZeroStep = Step;
        CaptureTimeMs = ModeSettingsLimits.CaptureTimeMs.Clamp(CapturedMs());
        Fades.Load(window, options.LeftTukeyWindow, options.RightTukeyWindow);
        DbRange = (int)ModeSettingsLimits.DbRange.Clamp(options.DbRange);
        SmoothingInverseOctaves = SmoothingPresetOptions.Normalize(options.SmoothingInverseOctaves);
        Offset = (int)ModeSettingsLimits.SampleOffset.Clamp(options.Offset);
    }

    public void SetWindow(int window)
    {
        Fades.SetWindow(window);
        if (IsBurstDecay)
        {
            CaptureTimeMs = ModeSettingsLimits.CaptureTimeMs.Assign((decimal)CapturedMs());
        }
    }

    public void SetSliceCount(int sliceCount)
    {
        SliceCount = sliceCount;
        CaptureTimeMs = ModeSettingsLimits.CaptureTimeMs.Assign((decimal)CapturedMs());
    }

    public void SetStep(int step)
    {
        Step = step == 0 ? (lastNonZeroStep > 0 ? -1 : 1) : step;
        lastNonZeroStep = Step;
        CaptureTimeMs = ModeSettingsLimits.CaptureTimeMs.Assign((decimal)CapturedMs());
    }

    public void WriteTo(WaterfallGenerateOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Window = Fades.Window;
        if (IsBurstDecay)
        {
            options.Periods = Periods;
        }
        else
        {
            options.SliceCount = SliceCount;
            options.Step = Step;
        }

        options.LeftTukeyWindow = Fades.Left;
        options.RightTukeyWindow = Fades.Right;
        options.DbRange = DbRange;
        options.SmoothingInverseOctaves = SmoothingInverseOctaves;
        options.Offset = Offset;
    }

    // Waterfall: the slices times the step; Burst Decay: the window.
    private double CapturedMs()
    {
        int sampleRate = Measurement.SampleRate;
        if (sampleRate <= 0)
        {
            return 0;
        }

        return IsBurstDecay
            ? (double)Fades.Window / sampleRate * 1000.0
            : (double)SliceCount * Step / sampleRate * 1000.0;
    }
}
