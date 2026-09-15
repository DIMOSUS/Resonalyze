using Resonalyze.Integration.Rew;
using Resonalyze.Ui.Dialogs;

namespace Resonalyze;

public partial class Form1
{
    private bool rewImportInFlight;

    /// <summary>The dialog reads and checks the measurement; only an import that passed lands here.</summary>
    private async void buttonRewImport_Click(object? sender, EventArgs e)
    {
        if (rewImportInFlight || expSweepMeasurement.InProgress)
        {
            return;
        }

        rewImportInFlight = true;
        try
        {
            await RunRewImportAsync();
        }
        finally
        {
            rewImportInFlight = false;
        }
    }

    private async Task RunRewImportAsync()
    {
        await StopLiveCaptureAsync();

        int configuredSampleRate =
            measurementSettings.Measurement.BuildConfiguration().Signal.SampleRate;
        RewPreparedImport? import;
        using (var dialog = new RewImportDialog(
            RewBaseUrl,
            configuredSampleRate,
            (address, token) => CreateRewImport(address).ListAsync(RewProbeTimeout, token),
            (address, measurement, offsetSeconds, levelDbfs, token) => CreateRewImport(address)
                .PrepareAsync(measurement, offsetSeconds, levelDbfs, configuredSampleRate, token)))
        {
            DialogResult result = dialog.ShowDialog(this);
            if (dialog.AnsweringBaseUrl is { } answering)
            {
                measurementSettings.RewApiBaseUrl = answering;
                ScheduleMeasurementSettingsSave();
            }

            import = result == DialogResult.OK ? dialog.Import : null;
        }

        if (import == null || IsDisposed || Disposing || closingInProgress)
        {
            return;
        }

        if (!GetActiveModeDescriptor().ShowsLoadedMeasurement)
        {
            await SelectModeAsync(ModeTab.Frequency);
        }

        commandController.SetSaveAvailable(false);
        commandController.SetLoadAvailable(false);
        try
        {
            InstallRewMeasurementImport(import);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"Failed to import the REW measurement.\r\n\r\n{exception.Message}",
                "Import failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            commandController.SetSaveAvailable(expSweepMeasurement.HasImpulseResponse);
            FinalizeMeasurementCommandState();
        }
    }

    private void InstallRewMeasurementImport(RewPreparedImport import)
    {
        // Supersedes a load or Open in analyzers still reading, as a plain Load does.
        ++measurementActivationRevision;
        using (expSweepMeasurement.Claim())
        {
            RestoreRewImpulseResponse(
                import.Samples,
                import.Referenced,
                import.SampleRate,
                import.LowFrequencyHz,
                import.HighFrequencyHz,
                import.SweepLengthSamples,
                sweepCount: 1,
                import.Plan.Reference);
        }

        FinishRewImport(path: null, RewSourceName(import.Measurement.Title));
        NotifyRewMeasurementImportDecisions(import);
    }

    private RewMeasurementImport CreateRewImport(Uri baseAddress) =>
        new(new RewApiClient(RewHttpClient, baseAddress));

    /// <summary>The plot title reads the name as a file name, so path characters in a REW title would cut it.</summary>
    private static string RewSourceName(string? title)
    {
        string name = string.IsNullOrWhiteSpace(title) ? "REW measurement" : title.Trim();
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        return name;
    }

    private static string DescribeImportedSweep(RewPreparedImport import)
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

    private void NotifyRewMeasurementImportDecisions(RewPreparedImport import)
    {
        if (closingInProgress)
        {
            return;
        }

        var notes = new List<string>
        {
            FormattableString.Invariant(
                $"Imported {import.Samples.Length} samples at {import.SampleRate} Hz from “{import.Measurement.Title}”. The loopback reference sat at sample {import.TimeZeroIndex:0.###} of REW's buffer and is now sample 0 of the transfer response; the fractional part was shifted, not rounded."),
            RewImportCarriesNoCalibrationNote,
            FormattableString.Invariant(
                $"REW scales an impulse response to digital full scale, and a transfer function here is divided by the loopback, so the stated {import.SweepLevelDbfs:0.#} dBFS sweep level was taken back out. The level matches a measurement taken here when REW played at that level through a digital loopback; an analog loopback's gain is not in it."),
            FormattableString.Invariant(
                $"REW's API states no bit depth, no playback channel and no sweep count: {ImportedBitDepth}-bit, Mono and one sweep were assumed. None of them changes the samples."),
            DescribeImportedSweep(import)
        };

        // The plan's offset carries REW's IR shift; the user stated only the rest.
        RewImportTimingPlan stated = import.Plan with { OffsetSeconds = import.Plan.OffsetSeconds - import.IrShiftSeconds };
        notes.Add(DescribeImportedTiming(import.SampleRate, stated, DescribeOffsetWitness(import, stated.OffsetSeconds)));
        if (import.IrShiftSeconds != 0 && import.Plan.Reference == TimingReference.SynchronizedLoopback)
        {
            notes.Add(FormattableString.Invariant(
                $"REW had moved this response's t = 0 by {import.IrShiftSeconds * 1000.0:0.####} ms (Offset t=0); that shift was taken back out as well, so the arrival is the one against the loopback."));
        }

        MessageBox.Show(
            this,
            string.Join("\r\n\r\n", notes),
            "REW measurement imported",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }
}
