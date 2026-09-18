using System.Globalization;
using System.Numerics;
using System.Text;
using Resonalyze.Dsp;
using Resonalyze.History;
using Xunit.Abstractions;

namespace Resonalyze.App.Tests;

/// <summary>
/// Measures the raw-pair datum on archived cabins to calibrate <c>VirtualCrossoverWarnings.HybridSpreadWarningDb</c>.
/// Reports rather than asserts: the number is evidence for a constant.
/// </summary>
public sealed class HybridOffsetDatumMeasurement(ITestOutputHelper output)
{
    [SessionBatteryFact]
    public void TheRawDatumOfEveryArchivedSetIsReported()
    {
        string root = SessionBatteryHarness.RootDirectory!;
        var report = new StringBuilder();
        int measured = 0;

        foreach (string sessionPath in Directory
            .EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            VirtualCrossoverProjectFile project;
            try
            {
                project = VirtualCrossoverProjectFile.LoadFrom(sessionPath);
            }
            catch
            {
                // Cabins keep .json measurements beside their sessions.
                continue;
            }

            foreach (bool rightSide in new[] { false, true })
            {
                List<double> offsets = MeasureSide(project, rightSide, report, sessionPath);
                if (offsets.Count < 2)
                {
                    continue;
                }

                measured++;
                report.AppendLine(
                    $"    {(rightSide ? "right" : "left")}: spread " +
                    $"{(offsets.Max() - offsets.Min()).ToString("0.00", CultureInfo.InvariantCulture)} dB " +
                    $"over {offsets.Count} channels");
            }
        }

        output.WriteLine(measured == 0
            ? "No archived session carries a spatial average on two or more channels."
            : report.ToString());
    }

    // Uncalibrated: the same correction on both curves cancels in their difference.
    private static List<double> MeasureSide(
        VirtualCrossoverProjectFile project,
        bool rightSide,
        StringBuilder report,
        string sessionPath)
    {
        var offsets = new List<double>();
        var rows = new StringBuilder();
        for (int i = 0; i < project.Pairs.Count; i++)
        {
            VirtualCrossoverChannelSettings settings = rightSide
                ? project.Pairs[i].Right
                : project.Pairs[i].Left;
            if (!settings.HasSource ||
                string.IsNullOrWhiteSpace(settings.SpatialAveragePath))
            {
                continue;
            }

            if (VirtualCrossoverSourceLocator.Locate(
                settings.SourceFilePath,
                settings.SourceRelativePath,
                project.ProjectDirectory) is not { } irPath ||
                VirtualCrossoverSourceLocator.Locate(
                    settings.SpatialAveragePath,
                    settings.SpatialAverageRelativePath,
                    project.ProjectDirectory) is not { } capturePath ||
                !LiveCaptureDocument.TryLoad(capturePath, out LiveCaptureDocument capture))
            {
                continue;
            }

            ImpulseResponseFile file = ImpulseResponseFile.LoadAsync(irPath)
                .GetAwaiter().GetResult();
            var channel = new VirtualCrossoverChannel(VirtualCrossoverSheet.ChannelName(i));
            ResolvedVirtualDspSource.FromSnapshot(
                MeasurementHistoryService.CreateSnapshot(file))
                ?.ApplyTo(channel.SideState(rightSide));
            VirtualCrossoverChannelState state = channel.SideState(rightSide);
            if (state.TransferImpulseResponse is not { } ir || state.SampleRate <= 0)
            {
                continue;
            }

            List<SignalPoint> rawIr = GatedRawCurve(ir, state.TransferPeakIndex, state.SampleRate);
            IReadOnlyList<SignalPoint>? rawCapture = SpatialAverageHybrid.BuildChannelCurve(
                capture,
                DspChannelChain.Identity,
                state.SampleRate,
                SpatialAverageCalibration.Off,
                rawIr.Select(point => point.X).ToList(),
                smoothingCode: 0);
            if (rawCapture == null || MedianDifference(rawCapture, rawIr) is not { } offset)
            {
                continue;
            }

            offsets.Add(offset);
            rows.AppendLine(
                $"        {settings.DisplayName,-16} " +
                $"{offset.ToString("+0.00;-0.00", CultureInfo.InvariantCulture)} dB");
        }

        if (offsets.Count >= 2)
        {
            report.AppendLine(Path.GetFileName(sessionPath));
            report.Append(rows);
        }

        return offsets;
    }

    private static List<SignalPoint> GatedRawCurve(
        Complex[] impulseResponse, int peakIndex, int sampleRate)
    {
        int anchor = ProcessedChannels.StartAnchorIndex(impulseResponse, peakIndex, sampleRate);
        var gate = new PhaseAnalysisSettings(
            PhaseWindowMode.Fixed,
            FdwCycles: 0,
            PhaseDetrendMode.Off,
            ManualDetrendMilliseconds: 0.0,
            GateOffsetMs: anchor * 1_000.0 / sampleRate,
            FrequencyResponseOptions.SteadyStateLeftMs,
            FrequencyResponseOptions.SteadyStatePlateauMs,
            FrequencyResponseOptions.SteadyStateRightMs,
            Unwrap: false,
            SmoothingInverseOctaves: 0.0);
        (AnalysisCurve display, _) = DataHelper.GetGatedPrimarySpectrumPair(
            new ImpulseMeasurementView(impulseResponse, anchor, sampleRate),
            gate,
            calibration: null,
            smoothingInverseOctaves: 0.0);
        return display.Points.ToList();
    }

    // Restated panel rule: median difference within 20 dB of the IR peak.
    private static double? MedianDifference(
        IReadOnlyList<SignalPoint> capture, IReadOnlyList<SignalPoint> reference)
    {
        int count = Math.Min(capture.Count, reference.Count);
        double peak = double.NegativeInfinity;
        for (int i = 0; i < count; i++)
        {
            if (double.IsFinite(reference[i].Y) && double.IsFinite(capture[i].Y))
            {
                peak = Math.Max(peak, reference[i].Y);
            }
        }

        if (double.IsNegativeInfinity(peak))
        {
            return null;
        }

        var differences = new List<double>();
        for (int i = 0; i < count; i++)
        {
            double difference = reference[i].Y - capture[i].Y;
            if (double.IsFinite(difference) && reference[i].Y >= peak - 20)
            {
                differences.Add(difference);
            }
        }

        if (differences.Count == 0)
        {
            return null;
        }

        differences.Sort();
        int middle = differences.Count / 2;
        return differences.Count % 2 == 1
            ? differences[middle]
            : 0.5 * (differences[middle - 1] + differences[middle]);
    }
}
