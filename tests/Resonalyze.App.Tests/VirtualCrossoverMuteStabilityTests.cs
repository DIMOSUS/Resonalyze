using System.Numerics;
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

    private static VirtualCrossoverHybrid Reader(IReadOnlyList<VirtualCrossoverChannel> channels)
    {
        var session = new VirtualCrossoverSession
        {
            Project = new VirtualCrossoverProjectFile
            {
                SpatialAverageMode = VirtualCrossoverSpatialAverageMode.MovingMic
            },
            MagnitudeGate = MagnitudeGateSnapshot.Initial with { SmoothingInverseOctaves = 0 }
        };
        session.Channels.AddRange(channels);
        return new VirtualCrossoverHybrid(session);
    }

    private static double SetOffsetDb(
        VirtualCrossoverHybrid reader, IReadOnlyList<VirtualCrossoverChannel> drawn) =>
        reader.ResolveRawOffsetsDb(Processed(drawn), rightSide: false).SetOffsetDb;

    private static List<ProcessedChannel> Processed(IEnumerable<VirtualCrossoverChannel> drawn) =>
        drawn
            .Select(channel => new ProcessedChannel(
                channel,
                channel.SideState(false).TransferImpulseResponse!,
                channel.SideState(false).TransferPeakIndex,
                SampleRate,
                OxyColors.White))
            .ToList();

    [Fact]
    public void TheSetOffsetIsTheSameWhicheverChannelsAreDrawn()
    {
        VirtualCrossoverChannel[] channels =
        [
            Channel("A", -30.0),
            Channel("B", -34.0),
            Channel("C", -41.0)
        ];
        VirtualCrossoverHybrid reader = Reader(channels);

        double all = SetOffsetDb(reader, channels);
        Assert.Equal(all, SetOffsetDb(reader, [channels[0], channels[1]]), 9);
        Assert.Equal(all, SetOffsetDb(reader, [channels[2]]), 9);
        Assert.Equal(all, SetOffsetDb(reader, [channels[1]]), 9);
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
        VirtualCrossoverHybrid reader = Reader(channels);

        IReadOnlyList<SetDatum> set =
            reader.ResolveRawOffsetsDb(Processed([channels[2]]), rightSide: false).SetDatums;

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
        VirtualCrossoverHybrid reader = Reader(channels);

        double?[] datums =
            reader.ResolveRawOffsetsDb(Processed(channels), rightSide: false).PerChannel;

        Assert.All(datums, datum => Assert.True(datum.HasValue));
        // Datum = reference - capture, so a quieter capture reads HIGHER.
        Assert.Equal(-4.0, datums[0]!.Value - datums[1]!.Value, 6);
        Assert.Equal(-7.0, datums[1]!.Value - datums[2]!.Value, 6);
    }
}
