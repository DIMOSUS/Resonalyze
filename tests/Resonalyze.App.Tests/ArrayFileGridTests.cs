using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>A file on a different grid would be read band for band, shifting every position in frequency; only the file still knows the ends.</summary>
public sealed class ArrayFileGridTests
{
    private static readonly IReadOnlyList<double> Grid = SpatialAverage.BuildGrid();

    private static ImpulseResponseFile.ArrayMicrophonesFileEntry Entry(
        double startHz, double stopHz) =>
        new()
        {
            GridStartHz = startHz,
            GridStopHz = stopHz,
            Microphones =
            [
                new ImpulseResponseFile.ArrayMicrophoneFileEntry
                {
                    ChannelOffset = 0,
                    IsMeasurementMicrophone = true,
                    AcceptedRunCount = 1,
                    LevelsDb = Enumerable.Repeat(70.0, Grid.Count).ToArray()
                },
                new ImpulseResponseFile.ArrayMicrophoneFileEntry
                {
                    ChannelOffset = 2,
                    AcceptedRunCount = 1,
                    LevelsDb = Enumerable.Repeat(70.0, Grid.Count).ToArray()
                }
            ]
        };

    [Fact]
    public void ThisBuildsOwnGridIsRead()
    {
        Assert.Equal(2, Entry(Grid[0], Grid[^1]).ToCurves().Count);
    }

    [Fact]
    public void TheGridEndpointsSurviveBeingBuiltTwoWays()
    {
        // The same grid built two ways differs in its last ULPs (20 vs 20.000000000000004).
        Assert.Equal(2, Entry(20.0, 20_000.0).ToCurves().Count);
    }

    [Fact]
    public void AGridThisBuildDoesNotUseYieldsNoArray()
    {
        Assert.Empty(Entry(10.0, 24_000.0).ToCurves());
        Assert.Empty(Entry(Grid[0], 24_000.0).ToCurves());
        Assert.Empty(Entry(10.0, Grid[^1]).ToCurves());
    }
}
