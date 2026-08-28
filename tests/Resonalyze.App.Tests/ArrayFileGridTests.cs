using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// The grid an array file states its curves are on, and what happens when it is not
/// the grid this build reads.
/// </summary>
/// <remarks>
/// The endpoints have travelled with the file since the format existed, and nothing
/// looked at them. A file whose grid ran from somewhere else would have been read
/// band for band on this one, which shifts every position in FREQUENCY and leaves a
/// curve that looks entirely ordinary. The band COUNT was checked where the curves
/// are placed; the ends could only be checked at the file, because by then they are
/// gone.
/// </remarks>
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
        // The same logarithmic grid built two ways differs in its last ULPs — 20
        // against 20.000000000000004 — so the question is whether it is the same
        // grid, not whether it is the same double.
        Assert.Equal(2, Entry(20.0, 20_000.0).ToCurves().Count);
    }

    [Fact]
    public void AGridThisBuildDoesNotUseYieldsNoArray()
    {
        // The same band count, a different span: nothing downstream could tell, which
        // is exactly why it has to be told here.
        Assert.Empty(Entry(10.0, 24_000.0).ToCurves());
        Assert.Empty(Entry(Grid[0], 24_000.0).ToCurves());
        Assert.Empty(Entry(10.0, Grid[^1]).ToCurves());
    }
}
