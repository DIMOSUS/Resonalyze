namespace Resonalyze.Options;

/// <summary>The sweep the next run would play, as the session's fields describe it (the rate as selected, not applied).</summary>
internal sealed record SweepFileExport(
    double LowFrequencyHz,
    double HighFrequencyHz,
    double TotalSeconds,
    int SampleRate,
    int Bits,
    PlaybackChannel Channel)
{
    public string SuggestedFileName =>
        SweepWavExport.SuggestFileName(LowFrequencyHz, HighFrequencyHz, TotalSeconds, SampleRate);

    /// <summary>A 24-bit WAV exactly as a measurement would play it, for playback from another device.</summary>
    public void Write(string path)
    {
        // Own instance: generating into the live one would discard the result on screen.
        using var sweep = new ExponentialSineSweep();
        sweep.FillData(LowFrequencyHz, HighFrequencyHz, TotalSeconds, Bits, SampleRate);
        AudioFileCodec.WriteWav(path, SweepWavExport.BuildContent(sweep.SweepData, SampleRate, Channel));
    }
}

internal sealed record SweepBandView(string Text, Color Color, string ToolTip);

/// <summary>The band the configured sweep covers at full amplitude, and why it may fall short of the one asked for.</summary>
internal static class SweepBandPreview
{
    public const string CoveredToolTip =
        "The band the sweep covers at full amplitude, with the fades outside it, and how long it takes.";

    public static SweepBandView Read(RecordSettingsSession session)
    {
        if (session.SampleRate.SelectedItem is not int sampleRate)
        {
            // No rate opens: the 44.1 kHz fallback would describe an unrunnable sweep.
            return new SweepBandView(
                "—",
                UiPalette.Warning,
                "No sample rate opens for the current configuration, so there is nothing to compute the sweep against.");
        }

        double lowHz = (double)session.LowFrequency.Value;
        double highHz = (double)session.HighFrequency.Value;
        double perOctaveSeconds = (double)session.OctavePaceMilliseconds.Value * 0.001;
        double totalSeconds = ExponentialSineSweep.TotalDurationForOctavePace(lowHz, highHz, perOctaveSeconds, sampleRate);
        ExpSweepSpec spec = ExponentialSineSweep.ComputeSpec(lowHz, highHz, totalSeconds, sampleRate);
        string text = spec.IsValid
            ? $"{spec.LowFrequencyHz:0.#}–{spec.HighFrequencyHz:0} Hz · " +
                $"{spec.OctaveSpan:0.00} oct · {spec.ComputedDurationSeconds:0.00} s"
            : "—";
        string? warning = DescribeShortfall(spec, lowHz, highHz, totalSeconds);
        return new SweepBandView(text, warning == null ? UiPalette.Success : UiPalette.Warning, warning ?? CoveredToolTip);
    }

    public static SweepFileExport Export(RecordSettingsSession session)
    {
        int sampleRate = session.SelectedSampleRate;
        return new SweepFileExport(
            (double)session.LowFrequency.Value,
            (double)session.HighFrequency.Value,
            session.RequestedDurationSeconds(sampleRate),
            sampleRate,
            (int)session.Bits.Value,
            session.SelectedPlaybackChannel);
    }

    /// <summary>Null when the sweep reaches the requested band at full amplitude within the length cap.</summary>
    public static string? DescribeShortfall(
        ExpSweepSpec spec,
        double requestedLowHz,
        double requestedHighHz,
        double requestedTotalSeconds)
    {
        if (!spec.IsValid)
        {
            return null;
        }

        if (requestedTotalSeconds > ExponentialSineSweep.MaxDurationSeconds && spec.OctaveSpan > 0)
        {
            double effectivePace = spec.ComputedDurationSeconds / spec.OctaveSpan;
            return $"⚠ Capped at {ExponentialSineSweep.MaxDurationSeconds:0} s " +
                $"(asked for {requestedTotalSeconds:0} s), so the sweep really " +
                $"paces {effectivePace * 1000.0:0} ms per octave.";
        }

        if (spec.Covers(requestedLowHz, requestedHighHz))
        {
            return null;
        }

        // Full amplitude needs a whole cycle plus fade room, so a short sweep falls short at the bottom first.
        return $"⚠ Full amplitude only from {spec.FullAmplitudeLowFrequencyHz:0.#} " +
            $"to {spec.FullAmplitudeHighFrequencyHz:0} Hz: one cycle at " +
            $"{requestedLowHz:0.#} Hz already takes " +
            $"{1000.0 / Math.Max(requestedLowHz, 1e-9):0} ms. Raise the " +
            "per-octave time to reach the requested band.";
    }
}
