using System.Reflection;
using System.Runtime.CompilerServices;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// A channel with no spatial average, in a project reading microphone arrays.
/// </summary>
/// <remarks>
/// For a moving-microphone set this is all-or-nothing, and rightly: those captures
/// are levelled by one analyzer session and a channel drawn from its impulse
/// response instead would put a second reference on the same axis. An array is
/// levelled by the loopback the impulse responses already use, so the two are one
/// measurement — which is what makes a subwoofer without an array legitimate. It
/// gains almost nothing from one anyway: below the cabin's first mode a point and
/// an average are the same measurement.
/// </remarks>
public sealed class VirtualCrossoverArrayFallbackTests
{
    private const int Points = 64;

    private static IReadOnlyList<double> Frequencies() =>
        EqualizationCurve.LogFrequencyGrid(20, 20_000, Points);

    private static AnalysisCurve Reference(double db) =>
        new(
            "raw",
            Frequencies().Select(frequency => new SignalPoint(frequency, db)).ToList());

    private static LiveCaptureDocument Capture(double db, SpatialAverageMethod method)
    {
        IReadOnlyList<double> grid = Frequencies();
        return new LiveCaptureDocument
        {
            SavedAtUtc = DateTimeOffset.UnixEpoch,
            Title = $"{db:0} dB",
            Method = method,
            CurveDb = grid.Select(_ => db).ToArray(),
            GridStartHz = grid[0],
            GridStopHz = grid[^1],
            Recipe = new LiveCaptureRecipe { SampleRateHz = 48_000 }
        };
    }

    // The panel, uninitialized but for the two fields the builder reads: the
    // project (for the method) and nothing else. The channels list stays null, so
    // the method is set explicitly rather than resolved.
    private static object Panel(VirtualCrossoverSpatialAverageMode mode)
    {
        object panel = RuntimeHelpers.GetUninitializedObject(typeof(VirtualCrossoverPanel));
        SetField(panel, "project", new VirtualCrossoverProjectFile { SpatialAverageMode = mode });
        // The offset datum is read through the panel's canonical gate, which a
        // constructor sets: the same steady-state window the panel uses, so the test
        // measures what the panel would.
        SetField(panel, "magnitudeGate", new VirtualCrossoverPanel.MagnitudeGateSnapshot(
            new PhaseAnalysisSettings(
                PhaseWindowMode.Fixed,
                PhaseAnalysisSettings.DefaultFdwCycles,
                PhaseDetrendMode.Off,
                ManualDetrendMilliseconds: 0.0,
                GateOffsetMs: 0.0,
                LeftMs: FrequencyResponseOptions.SteadyStateLeftMs,
                PlateauMs: FrequencyResponseOptions.SteadyStatePlateauMs,
                RightMs: FrequencyResponseOptions.SteadyStateRightMs,
                Unwrap: false,
                SmoothingInverseOctaves: 0.0),
            PinnedOffsetMs: null,
            OppositePinnedOffsetMs: null,
            SmoothingInverseOctaves: 12));
        return panel;
    }

    private static void SetField(object target, string name, object value)
    {
        typeof(VirtualCrossoverPanel)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(target, value);
    }

    private static HybridMagnitudes? Build(
        object panel,
        IReadOnlyList<VirtualCrossoverChannel> channels,
        IReadOnlyList<AnalysisCurve> references)
    {
        MethodInfo method = typeof(VirtualCrossoverPanel).GetMethod(
            "BuildHybridMagnitudes",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("BuildHybridMagnitudes is gone.");
        // Only the Channel matters to the builder; the rest of a ProcessedChannel is
        // the plot's business.
        List<ProcessedChannel> processed = channels
            .Select(channel => new ProcessedChannel(
                channel,
                [],
                PeakIndex: 0,
                SampleRate: 48_000,
                OxyPlot.OxyColors.White))
            .ToList();
        return method.Invoke(panel, [processed, references, false, 0]) as HybridMagnitudes;
    }

    private static VirtualCrossoverChannel Channel(string name, LiveCaptureDocument? array)
    {
        var channel = new VirtualCrossoverChannel(name);
        foreach (bool side in new[] { false, true })
        {
            VirtualCrossoverChannelState state = channel.PhysicalSideState(side);
            state.SampleRate = 48_000;
            state.ArrayCapture = array;
            // A real impulse response, because the offset datum is read on the two
            // MEASUREMENTS: without one a channel contributes nothing whether or not
            // it has an array, and the test would pass for the wrong reason.
            var impulse = new System.Numerics.Complex[4_096];
            impulse[64] = System.Numerics.Complex.One;
            state.TransferImpulseResponse = impulse;
            state.TransferPeakIndex = 64;
        }

        return channel;
    }

    [Fact]
    public void AChannelWithoutAnArrayIsDrawnFromItsOwnMeasurementAndMarked()
    {
        object panel = Panel(VirtualCrossoverSpatialAverageMode.MicArray);
        var channels = new[]
        {
            Channel("mid", Capture(-20, SpatialAverageMethod.MicArray)),
            Channel("sub", array: null)
        };
        AnalysisCurve[] references = [Reference(-24), Reference(-30)];

        HybridMagnitudes? hybrid = Build(panel, channels, references);

        Assert.NotNull(hybrid);
        Assert.Equal([false, true], hybrid!.PointMeasuredChannels);
        Assert.Equal(1, hybrid.PointMeasuredCount);

        // The set's curves are held WITHOUT the offset, which is added on the way to
        // the plot — so the fallback curve, already on the impulse responses' axis,
        // arrives pre-subtracted and lands back where it started.
        double drawn = hybrid.Channels[1][Points / 2].Y + hybrid.OffsetDb;
        Assert.Equal(-30, drawn, 6);
    }

    [Fact]
    public void AChannelWithoutAnArrayContributesNoOffsetDatum()
    {
        object panel = Panel(VirtualCrossoverSpatialAverageMode.MicArray);
        var channels = new[]
        {
            Channel("mid", Capture(-20, SpatialAverageMethod.MicArray)),
            Channel("sub", array: null)
        };
        AnalysisCurve[] references = [Reference(-24), Reference(-30)];

        HybridMagnitudes? hybrid = Build(panel, channels, references);

        // It has no capture to compare against its measurement, so it says nothing
        // about whether the set hangs together — and a datum invented for it would
        // read as perfect agreement and pull the spread toward zero.
        Assert.NotNull(hybrid!.ChannelOffsetsDb[0]);
        Assert.Null(hybrid.ChannelOffsetsDb[1]);
    }

    [Fact]
    public void AMovingMicSetStillRefusesAChannelWithoutOne()
    {
        object panel = Panel(VirtualCrossoverSpatialAverageMode.MovingMic);
        var channels = new[]
        {
            Channel("mid", array: null),
            Channel("sub", array: null)
        };
        channels[0].PhysicalSideState(false).SpatialAverage =
            Capture(-20, SpatialAverageMethod.MovingMic);
        AnalysisCurve[] references = [Reference(-24), Reference(-30)];

        // Two references on one axis is exactly what that family cannot survive.
        Assert.Null(Build(panel, channels, references));
    }

    [Fact]
    public void EveryChannelWithAnArrayIsMarkedAsMeasured()
    {
        object panel = Panel(VirtualCrossoverSpatialAverageMode.MicArray);
        var channels = new[]
        {
            Channel("mid", Capture(-20, SpatialAverageMethod.MicArray)),
            Channel("tweeter", Capture(-22, SpatialAverageMethod.MicArray))
        };
        AnalysisCurve[] references = [Reference(-24), Reference(-26)];

        HybridMagnitudes? hybrid = Build(panel, channels, references);

        Assert.NotNull(hybrid);
        Assert.Equal(0, hybrid!.PointMeasuredCount);
    }
}
