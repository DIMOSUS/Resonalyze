using Resonalyze.Dsp;

namespace Resonalyze.Integration.Rew;

/// <summary>What a REW import decided on the user's behalf, shown once it lands: an imported measurement looks like a measured one.</summary>
internal static class RewImportNotes
{
    private const string CarriesNoCalibration =
        "REW's impulse responses carry no coherence, no level meters and no SPL calibration, " +
        "and REW applies a microphone calibration to its own curves rather than to the " +
        "impulse response — so this measurement is uncalibrated here, whatever REW showed.";

    private const string ExportCannotWitnessOffset =
        "The export itself cannot confirm that: REW folds the offset into the start time and writes it nowhere in the export, so the arrival is true on your word rather than on the file's.";

    /// <summary>REW's text export, the only REW route stating sample 0's time.</summary>
    public static IReadOnlyList<string> Describe(
        RewImpulseResponseTextFile file,
        RewImportTimingPlan plan,
        EssSweepRateEstimate? sweepRate)
    {
        var notes = new List<string>
        {
            FormattableString.Invariant(
                $"Imported {file.Samples.Length} samples at {file.SampleRate} Hz. The loopback reference sits at sample {file.TimeZeroIndex:0.###} of REW's buffer and is now sample 0 of the transfer response; the fractional part was shifted, not rounded."),
            CarriesNoCalibration,
            FormattableString.Invariant(
                $"The export states no bit depth and no playback channel: {RewMeasurementImport.ImportedBitDepth}-bit and Mono were assumed. Neither changes the samples — they describe the sweep this result is filed under."),
            DescribeTiming(file.SampleRate, plan, ExportCannotWitnessOffset),
            file.SweepLevelDbfs is { } levelDbfs && levelDbfs <= 0
                ? FormattableString.Invariant(
                    $"The export states a {levelDbfs:0.#} dBFS sweep. REW scales an impulse response to digital full scale and a transfer function here is divided by the loopback, so that level was taken back out; an analog loopback's gain is not in it.")
                : "The export states no sweep level, so none was taken out: REW scales to digital full scale, and this " +
                    "measurement sits lower than one taken here by the level its loopback ran at."
        };
        if (file.WasNormalised)
        {
            notes.Add(FormattableString.Invariant(
                $"The export was normalised; its samples were multiplied back by the peak value REW states it had before normalisation ({file.PeakValueBeforeNormalisation:G6}), which restores the level."));
        }

        if (file.LowFrequencyHz == null || file.HighFrequencyHz == null)
        {
            notes.Add(FormattableString.Invariant(
                $"The header did not state the swept band, so {RewMeasurementImport.FallbackLowFrequencyHz:0.#} Hz to Nyquist was assumed; set it right if the sweep was narrower."));
        }

        notes.Add(sweepRate is { } rate
            ? FormattableString.Invariant(
                $"The sweep's rate was read from where its harmonics landed ({string.Join(", ", rate.Orders.Select(order => $"H{order}"))}; the second harmonic sits {rate.SecondsPerNeper * Math.Log(2) * 1000.0:0.#} ms before the arrival): the band and length REW's header states do not give its sweep's rate, and would misplace the distortion view's harmonic windows.")
            : file.SweepLengthSamples == null
                ? "No harmonic stood above the noise to read the sweep's rate from, and the header did not state the " +
                    "sweep's length, so the impulse response's own length stands in: harmonics may not be drawn."
                : "No harmonic stood above the noise to read the sweep's rate from, so the header's band and sweep " +
                    "length place the harmonic windows; harmonics may not be drawn.");
        return notes;
    }

    /// <summary>A measurement read from REW's API.</summary>
    public static IReadOnlyList<string> Describe(RewPreparedImport import)
    {
        var notes = new List<string>
        {
            FormattableString.Invariant(
                $"Imported {import.Samples.Length} samples at {import.SampleRate} Hz from “{import.Measurement.Title}”. The loopback reference sat at sample {import.TimeZeroIndex:0.###} of REW's buffer and is now sample 0 of the transfer response; the fractional part was shifted, not rounded."),
            CarriesNoCalibration,
            FormattableString.Invariant(
                $"REW scales an impulse response to digital full scale, and a transfer function here is divided by the loopback, so the stated {import.SweepLevelDbfs:0.#} dBFS sweep level was taken back out. The level matches a measurement taken here when REW played at that level through a digital loopback; an analog loopback's gain is not in it."),
            FormattableString.Invariant(
                $"REW's API states no bit depth, no playback channel and no sweep count: {RewMeasurementImport.ImportedBitDepth}-bit, Mono and one sweep were assumed. None of them changes the samples."),
            DescribeSweep(import)
        };

        // The plan's offset carries REW's IR shift; the user stated only the rest.
        RewImportTimingPlan stated = import.Plan with { OffsetSeconds = import.Plan.OffsetSeconds - import.IrShiftSeconds };
        notes.Add(DescribeTiming(import.SampleRate, stated, DescribeOffsetWitness(import, stated.OffsetSeconds)));
        if (import.IrShiftSeconds != 0 && import.Plan.Reference == TimingReference.SynchronizedLoopback)
        {
            notes.Add(FormattableString.Invariant(
                $"REW had moved this response's t = 0 by {import.IrShiftSeconds * 1000.0:0.####} ms (Offset t=0); that shift was taken back out as well, so the arrival is the one against the loopback."));
        }

        return notes;
    }

    /// <param name="witness">Who vouches for the stated offset, as the sentence's last clause.</param>
    private static string DescribeTiming(
        int sampleRate,
        RewImportTimingPlan plan,
        string witness)
    {
        double arrivalMs = plan.ArrivalSamples / sampleRate * 1000.0;
        if (plan.Reference == TimingReference.RecordedSweep)
        {
            return FormattableString.Invariant(
                $"The timing offset was left unstated, so this is filed as a recorded sweep: its shape is real and its position is not. Delays within it still mean what they say — a reflection 8 ms after the direct sound is 8 ms — but its arrival cannot be compared with another measurement's. Re-import it with the offset REW was running to place it on this session's time base.");
        }

        string statedAs = plan.OffsetSeconds == 0
            ? "You stated no timing offset"
            : FormattableString.Invariant(
                $"You stated a {plan.OffsetSeconds * 1000.0:0.####} ms timing offset, which was taken back out");
        return FormattableString.Invariant(
            $"{statedAs}, so this measurement is on the session's time base with an arrival of {arrivalMs:0.###} ms. {witness}");
    }

    private static string DescribeSweep(RewPreparedImport import)
    {
        string band = import.BandFromRew
            ? FormattableString.Invariant(
                $"The band, {import.LowFrequencyHz:0.###} Hz to {import.HighFrequencyHz:0.#} Hz, is the range REW lists for this measurement.")
            : FormattableString.Invariant(
                $"REW listed no range for this measurement, so {import.LowFrequencyHz:0.#} Hz to Nyquist was assumed; set it right if the sweep was narrower.");
        if (import.SweepRate is not { } rate)
        {
            return band + " REW states no sweep length and no harmonic stood above the noise to read the sweep's rate from, " +
                "so the impulse response's own length stands in: the distortion view's harmonic windows may sit in the wrong place.";
        }

        string orders = string.Join(", ", rate.Orders.Select(order => $"H{order}"));
        return band + FormattableString.Invariant(
            $" REW states no sweep length, so the sweep's rate was read from where its harmonics landed ({orders}; the second harmonic sits {rate.SecondsPerNeper * Math.Log(2) * 1000.0:0.#} ms before the arrival), which places the distortion view's harmonic windows. The sweep duration shown is the one that rate gives over this band, not REW's.");
    }

    private static string DescribeOffsetWitness(RewPreparedImport import, double statedOffsetSeconds)
    {
        if (import.Measurement.TimingOffsetSeconds is not { } recorded)
        {
            return "REW records no timing offset for this measurement, so the arrival is true on your word rather than on REW's.";
        }

        // The dialog rounds to 0.1 µs.
        return Math.Abs(recorded - statedOffsetSeconds) < 1e-7
            ? "That is the offset REW records for this measurement."
            : FormattableString.Invariant(
                $"REW records a {recorded * 1000.0:0.####} ms offset for this measurement, so the arrival is true on your word rather than on REW's.");
    }
}
