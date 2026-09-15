using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using OxyPlot;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <remarks>The set offset is a median over every channel with a capture, not the DRAWN ones: a drawn-only median moved the rest ~0.25 dB per mute on real cabins.</remarks>
public sealed class VirtualCrossoverMuteStabilityTests
{
    private const int SampleRate = 48_000;
    private const int Points = 128;

    private static IReadOnlyList<double> Frequencies() =>
        EqualizationCurve.LogFrequencyGrid(200, 15_000, Points);

    private static Complex[] Impulse()
    {
        var samples = new Complex[4_096];
        samples[64] = new Complex(1.0, 0.0);
        return samples;
    }

    private static LiveCaptureDocument Capture(double levelDb)
    {
        IReadOnlyList<double> grid = Frequencies();
        return new LiveCaptureDocument
        {
            SavedAtUtc = DateTimeOffset.UnixEpoch,
            Title = $"{levelDb:0} dB",
            Method = SpatialAverageMethod.MovingMic,
            CurveDb = grid.Select(_ => levelDb).ToArray(),
            GridStartHz = grid[0],
            GridStopHz = grid[^1],
            Recipe = new LiveCaptureRecipe { SampleRateHz = SampleRate }
        };
    }

    private static VirtualCrossoverChannel Channel(string name, double captureLevelDb)
    {
        var channel = new VirtualCrossoverChannel(name);
        VirtualCrossoverChannelState state = channel.SideState(false);
        state.TransferImpulseResponse = Impulse();
        state.TransferPeakIndex = 64;
        state.SampleRate = SampleRate;
        state.SpatialAverage = Capture(captureLevelDb);
        return channel;
    }

    private static object Panel(IReadOnlyList<VirtualCrossoverChannel> channels)
    {
        object panel = RuntimeHelpers.GetUninitializedObject(typeof(VirtualCrossoverPanel));
        Set(panel, "project", new VirtualCrossoverProjectFile
        {
            SpatialAverageMode = VirtualCrossoverSpatialAverageMode.MovingMic
        });
        Set(panel, "channels", channels.ToList());
        Set(panel, "magnitudeGate", new VirtualCrossoverPanel.MagnitudeGateSnapshot(
            new PhaseAnalysisSettings(
                PhaseWindowMode.Fixed,
                PhaseAnalysisSettings.DefaultFdwCycles,
                PhaseDetrendMode.Off,
                ManualDetrendMilliseconds: 0.0,
                GateOffsetMs: 0.0,
                FrequencyResponseOptions.SteadyStateLeftMs,
                FrequencyResponseOptions.SteadyStatePlateauMs,
                FrequencyResponseOptions.SteadyStateRightMs,
                Unwrap: false,
                SmoothingInverseOctaves: 0.0),
            PinnedOffsetMs: null,
            OppositePinnedOffsetMs: null,
            SmoothingInverseOctaves: 0));
        return panel;
    }

    private static void Set(object target, string name, object? value) =>
        typeof(VirtualCrossoverPanel)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(target, value);

    private static double SetOffsetDb(
        object panel, IReadOnlyList<VirtualCrossoverChannel> drawn)
    {
        List<ProcessedChannel> processed = drawn
            .Select(channel => new ProcessedChannel(
                channel,
                channel.SideState(false).TransferImpulseResponse!,
                channel.SideState(false).TransferPeakIndex,
                SampleRate,
                OxyColors.White))
            .ToList();
        object result = typeof(VirtualCrossoverPanel)
            .GetMethod("ResolveRawHybridOffsetsDb", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(panel, [processed, false])!;
        // ValueTuple element names do not survive to runtime; the fields are Item1..Item3.
        return (double)result.GetType().GetField("Item2")!.GetValue(result)!;
    }

    [Fact]
    public void TheSetOffsetIsTheSameWhicheverChannelsAreDrawn()
    {
        VirtualCrossoverChannel[] channels =
        [
            Channel("A", -30.0),
            Channel("B", -34.0),
            Channel("C", -41.0)
        ];
        object panel = Panel(channels);

        double all = SetOffsetDb(panel, channels);
        Assert.Equal(all, SetOffsetDb(panel, [channels[0], channels[1]]), 9);
        Assert.Equal(all, SetOffsetDb(panel, [channels[2]]), 9);
        Assert.Equal(all, SetOffsetDb(panel, [channels[1]]), 9);
    }

    [Fact]
    public void TheSetTheWarningJudgesIsTheWholeSideMutedChannelsIncluded()
    {
        VirtualCrossoverChannel[] channels =
        [
            Channel("A", -30.0),
            Channel("B", -34.0),
            Channel("C", -41.0)
        ];
        object panel = Panel(channels);

        object result = typeof(VirtualCrossoverPanel)
            .GetMethod("ResolveRawHybridOffsetsDb", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(panel, [
                new List<ProcessedChannel>
                {
                    new(
                        channels[2],
                        channels[2].SideState(false).TransferImpulseResponse!,
                        64,
                        SampleRate,
                        OxyColors.White)
                },
                false])!;
        var set = (IReadOnlyList<SetDatum>)result.GetType().GetField("Item3")!.GetValue(result)!;

        Assert.Equal(3, set.Count);
        Assert.Equal(["A", "B", "C"], set.Select(entry => entry.Channel.Name));
        Assert.All(set, entry => Assert.True(entry.DatumDb.HasValue));
    }

    [Fact]
    public void TheDatumsThemselvesStillDifferPerChannel()
    {
        // Captures 4 and 7 dB apart, so the median differs for every subset.
        VirtualCrossoverChannel[] channels =
        [
            Channel("A", -30.0),
            Channel("B", -34.0),
            Channel("C", -41.0)
        ];
        object panel = Panel(channels);

        object result = typeof(VirtualCrossoverPanel)
            .GetMethod("ResolveRawHybridOffsetsDb", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(panel, [
                channels.Select(channel => new ProcessedChannel(
                    channel,
                    channel.SideState(false).TransferImpulseResponse!,
                    64,
                    SampleRate,
                    OxyColors.White)).ToList(),
                false])!;
        double?[] datums = (double?[])result.GetType().GetField("Item1")!.GetValue(result)!;

        Assert.All(datums, datum => Assert.True(datum.HasValue));
        // Datum = reference - capture, so a quieter capture reads HIGHER.
        Assert.Equal(-4.0, datums[0]!.Value - datums[1]!.Value, 6);
        Assert.Equal(-7.0, datums[1]!.Value - datums[2]!.Value, 6);
    }
}
