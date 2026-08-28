using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Resonalyze.Dsp;
using Resonalyze.History;
using Xunit.Abstractions;

namespace Resonalyze.App.Tests;

/// <summary>
/// Runs a folder of measurements carrying microphone arrays through the two tools
/// that consume one — the Virtual DSP hybrid and the EQ Wizard — and reports what
/// they make of it.
/// </summary>
/// <remarks>
/// Skipped unless the folder is named, like the Auto delay battery: measurements do
/// not live in the repository. It was written against the synthetic seven-driver
/// set (each driver's array modelled on the owner's own v8 positions around his own
/// moving-microphone curve), but nothing here knows that — point it at real arrays
/// and it reports on those.
/// <para>
/// It REPORTS and asserts only the invariants that cannot be a property of one car:
/// that every array resolves, that the hybrid draws, that a boost is never allowed
/// where the positions disagree past the limit. The numbers themselves are evidence
/// to read, not bounds to pin — the next cabin is a different car.
/// </para>
/// </remarks>
public sealed class SyntheticArrayHarness(ITestOutputHelper output)
{
    private const int PreviewSmoothing = 6;

    private static string DescribeCalibration(LiveCaptureDocument document) =>
        document.Calibration?.Name
            ?? (document.CalibrationIsAggregate ? "several (one per position)" : "none");

    [ArrayHarnessFact]
    public void EveryArrayReachesTheHybridAndTheWizard()
    {
        string root = ArrayHarnessFactAttribute.RootDirectory!;
        string[] paths = Directory
            .EnumerateFiles(root, "*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.NotEmpty(paths);

        var report = new StringBuilder();
        var channels = new List<VirtualCrossoverChannel>();
        var loaded = new List<(string Name, ImpulseResponseFile File)>();

        report.AppendLine("--- the measurements, as the tools resolve them");
        foreach (string path in paths)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            ImpulseResponseFile file;
            try
            {
                file = ImpulseResponseFile.LoadAsync(path).GetAwaiter().GetResult();
            }
            catch (InvalidDataException)
            {
                // A folder of measurements is also where a Virtual DSP project ends
                // up, and where a session autosave lands. Not something to fail on:
                // the harness is pointed at a working directory, not at a curated one.
                report.AppendLine($"  {name,-14} not a measurement — skipped");
                continue;
            }

            if (file.ArrayMicrophones is null)
            {
                report.AppendLine($"  {name,-14} no array — skipped");
                continue;
            }

            MeasurementHistorySnapshot snapshot = MeasurementHistoryService.CreateSnapshot(file);
            ResolvedVirtualDspSource? source = ResolvedVirtualDspSource.FromSnapshot(snapshot);
            Assert.NotNull(source);

            // Every array in the folder has to survive the trip into the tool. A
            // curve on a grid this build does not use, or a set nothing could be
            // levelled onto, would come back null here and quietly disable the
            // hybrid rather than fail.
            Assert.NotNull(source!.ArrayCapture);
            Assert.NotNull(source.ArraySpreadDb);

            var channel = new VirtualCrossoverChannel(name);
            source.ApplyTo(channel.PhysicalSideState(false));
            source.ApplyTo(channel.PhysicalSideState(true));
            channel.Pair.Bypass = true;
            channels.Add(channel);
            loaded.Add((name, file));

            MeasuredBand band = source.MeasuredBand;
            report.AppendLine(
                $"  {name,-14} {source.ArrayCapture!.Recipe.MicrophoneCount} positions, " +
                $"band {band.LowEdgeHz,7:0.0}-{(double.IsPositiveInfinity(band.HighEdgeHz) ? double.NaN : band.HighEdgeHz),8:0} Hz, " +
                // "several" is not "none", and the document says only that no ONE
                // curve describes it — which is what an array of individually
                // calibrated capsules looks like, and the state a consumer must not
                // read as uncalibrated.
                $"calibration {DescribeCalibration(source.ArrayCapture)}");
        }

        Assert.True(channels.Count >= 2, "the hybrid needs at least two channels.");

        ReportHybrid(channels, report);
        ReportWizard(loaded, report);
        output.WriteLine(report.ToString());
    }

    // ------------------------------------------------------------- Virtual DSP

    private static void ReportHybrid(
        IReadOnlyList<VirtualCrossoverChannel> channels, StringBuilder report)
    {
        report.AppendLine();
        report.AppendLine("--- Virtual DSP, reading the project as a microphone array");

        object panel = ArrayPanel();
        var processed = new List<ProcessedChannel>(channels.Count);
        var references = new List<AnalysisCurve>(channels.Count);
        foreach (VirtualCrossoverChannel channel in channels)
        {
            VirtualCrossoverChannelState state = channel.PhysicalSideState(false);
            Complex[] impulse = state.TransferImpulseResponse!;
            int anchor = ProcessedChannels.StartAnchorIndex(
                impulse, state.TransferPeakIndex, state.SampleRate);
            processed.Add(new ProcessedChannel(
                channel,
                impulse,
                state.TransferPeakIndex,
                state.SampleRate,
                OxyPlot.OxyColors.White,
                default,
                state.MeasuredBand));
            references.Add(DataHelper.GetGatedPrimarySpectrum(
                new ImpulseMeasurementView(impulse, anchor, state.SampleRate)
                {
                    LowestMeasuredFrequencyHz = state.MeasuredBand.LowEdgeHz,
                    HighestMeasuredFrequencyHz = state.MeasuredBand.HighEdgeHz
                },
                SteadyStateGate(anchor, state.SampleRate),
                calibration: null,
                PreviewSmoothing));
        }

        HybridMagnitudes? hybrid = BuildHybrid(panel, processed, references);
        Assert.NotNull(hybrid);

        report.AppendLine(
            $"  set offset {hybrid!.OffsetDb:0.00} dB, " +
            $"spread {hybrid.SpreadDb:0.00} dB " +
            $"(the panel warns past {1.5:0.0}), " +
            $"{hybrid.PointMeasuredCount} channel(s) drawn from a point measurement");
        for (int i = 0; i < processed.Count; i++)
        {
            IReadOnlyList<SignalPoint> curve = hybrid.Channels[i];
            int drawn = curve.Count(point => double.IsFinite(point.Y));
            // Which bands went missing, not just how many: a gap in the middle is a
            // different animal from an edge the measured band cut off.
            double[] blank = curve
                .Where(point => !double.IsFinite(point.Y))
                .Select(point => point.X)
                .ToArray();
            string gaps = blank.Length == 0
                ? string.Empty
                : $"   blank {blank[0]:0.#}-{blank[^1]:0.#} Hz";
            report.AppendLine(
                $"    {processed[i].Channel.Name,-14} " +
                (hybrid.ChannelOffsetsDb[i] is { } offset
                    ? $"datum {offset,6:0.00} dB"
                    : "    no datum") +
                $", {drawn,4} of {curve.Count} bands drawn{gaps}" +
                (hybrid.PointMeasuredChannels[i] ? "   (point measurement)" : string.Empty));
        }

        // The whole reason the hybrid exists: every channel that HAS an array must be
        // drawn from it. A channel silently falling back would put one point
        // measurement's dips into a tune fitted to spatial averages.
        Assert.Equal(0, hybrid.PointMeasuredCount);
        Assert.All(hybrid.ChannelOffsetsDb, offset => Assert.NotNull(offset));
    }

    // ---------------------------------------------------------------- EQ Wizard

    private static void ReportWizard(
        IReadOnlyList<(string Name, ImpulseResponseFile File)> loaded, StringBuilder report)
    {
        report.AppendLine();
        report.AppendLine("--- EQ Wizard, with the array as its source");

        foreach ((string name, ImpulseResponseFile file) in loaded)
        {
            EqWizardCurveSource? source = EqWizardSourceResolver.TryCreateFromArray(
                file, name, EqWizardSourceResolver.DescribeArray(file, name));
            Assert.NotNull(source);
            Assert.Equal(EqWizardSourceKind.SpatialAverage, source!.Kind);
            Assert.NotNull(source.Coherence);

            SignalPoint[] measured = source.Points
                .Where(point => double.IsFinite(point.Y))
                .ToArray();
            Assert.NotEmpty(measured);

            double lowHz = measured[0].X;
            double highHz = measured[^1].X;
            IReadOnlyList<SignalPoint> agreement = source.Coherence!;
            int refused = agreement.Count(point =>
                point.X >= lowHz && point.X <= highHz && point.Y == 0.0);
            int inBand = agreement.Count(point => point.X >= lowHz && point.X <= highHz);

            // A flat target across what the driver measured: the fit itself is not the
            // subject, the gate's effect on it is.
            double level = measured.Select(point => point.Y).OrderBy(value => value)
                .ElementAt(measured.Length / 2);
            SignalPoint[] target = source.Points
                .Select(point => new SignalPoint(point.X, level))
                .ToArray();

            EqualizationCurve cuts = EqAutoTuner.Tune(
                source.Points, target, Options(lowHz, highHz, cutsOnly: true), agreement);
            EqualizationCurve boosts = EqAutoTuner.Tune(
                source.Points, target, Options(lowHz, highHz, cutsOnly: false), agreement);

            report.AppendLine(
                $"  {name,-14} measured {lowHz,7:0.0}-{highHz,8:0} Hz " +
                $"({measured.Length,4} bands), boost refused on {refused,3}/{inBand,4} " +
                $"({(inBand == 0 ? 0 : 100.0 * refused / inBand),4:0.0}%)");
            int boosted = boosts.Bands.Count(band => band.GainDb > 0.0);
            report.AppendLine(
                $"                 cuts only: {cuts.Bands.Count,2} bands (all cuts), " +
                $"preamp {cuts.PreampDb,5:0.0} dB   |   " +
                $"boosts allowed: {boosts.Bands.Count,2} bands of which " +
                $"{boosted,2} boost, preamp {boosts.PreampDb,5:0.0} dB");

            // Cuts-only must never boost, whatever the curve looks like.
            Assert.All(cuts.Bands, band => Assert.True(
                band.GainDb <= 0.0,
                $"{name}: a cuts-only fit placed {band.GainDb:+0.0} dB at {band.FrequencyHz:0} Hz"));

            // And a boost must never be CENTRED where the positions disagree past the
            // limit — that is the whole job of the agreement curve.
            foreach (PeqBand band in boosts.Bands.Where(band => band.GainDb > 0.0))
            {
                SignalPoint nearest = agreement
                    .MinBy(point => Math.Abs(point.X - band.FrequencyHz));
                Assert.True(
                    nearest.Y != 0.0,
                    $"{name}: a boost of {band.GainDb:+0.0} dB was centred at " +
                    $"{band.FrequencyHz:0} Hz, where the positions disagree past the limit");
            }
        }
    }

    private static EqAutoTuner.Options Options(double lowHz, double highHz, bool cutsOnly) =>
        new()
        {
            MaxBands = 10,
            MinFrequencyHz = lowHz,
            MaxFrequencyHz = highHz,
            CutsOnlyMode = cutsOnly,
            TotalGainMaxDb = cutsOnly ? 0 : double.PositiveInfinity,
            BandGainMinDb = -12,
            BandGainMaxDb = 12,
            SampleRateHz = 96_000
        };

    // ------------------------------------------------------------------ plumbing

    // The panel with only what the hybrid builder reads: a project that says the
    // spatial average is a microphone array, and the canonical magnitude gate.
    private static object ArrayPanel()
    {
        object panel = RuntimeHelpers.GetUninitializedObject(typeof(VirtualCrossoverPanel));
        SetField(panel, "project", new VirtualCrossoverProjectFile
        {
            SpatialAverageMode = VirtualCrossoverSpatialAverageMode.MicArray
        });
        SetField(panel, "magnitudeGate", new VirtualCrossoverPanel.MagnitudeGateSnapshot(
            SteadyStateGate(anchorIndex: 0, sampleRate: 96_000),
            PinnedOffsetMs: null,
            OppositePinnedOffsetMs: null,
            SmoothingInverseOctaves: PreviewSmoothing));
        return panel;
    }

    private static PhaseAnalysisSettings SteadyStateGate(int anchorIndex, int sampleRate) =>
        new(
            PhaseWindowMode.Fixed,
            PhaseAnalysisSettings.DefaultFdwCycles,
            PhaseDetrendMode.Off,
            ManualDetrendMilliseconds: 0.0,
            GateOffsetMs: anchorIndex * 1_000.0 / sampleRate,
            LeftMs: FrequencyResponseOptions.SteadyStateLeftMs,
            PlateauMs: FrequencyResponseOptions.SteadyStatePlateauMs,
            RightMs: FrequencyResponseOptions.SteadyStateRightMs,
            Unwrap: false,
            SmoothingInverseOctaves: 0.0);

    private static void SetField(object target, string name, object value) =>
        typeof(VirtualCrossoverPanel)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(target, value);

    private static HybridMagnitudes? BuildHybrid(
        object panel,
        IReadOnlyList<ProcessedChannel> processed,
        IReadOnlyList<AnalysisCurve> references) =>
        typeof(VirtualCrossoverPanel)
            .GetMethod("BuildHybridMagnitudes", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(panel, [processed, references, false, PreviewSmoothing])
            as HybridMagnitudes;
}

/// <summary>
/// Runs the array harness only when a folder of measurements carrying arrays is
/// named, the way the Auto delay battery is gated.
/// </summary>
public sealed class ArrayHarnessFactAttribute : FactAttribute
{
    public const string RootVariable = "RESONALYZE_ARRAY_SET";

    internal static string? RootDirectory =>
        Environment.GetEnvironmentVariable(RootVariable);

    public ArrayHarnessFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(RootDirectory))
        {
            Skip =
                $"Set {RootVariable} to a folder of measurements carrying microphone " +
                "arrays to run them through the Virtual DSP hybrid and the EQ Wizard.";
        }
    }
}
