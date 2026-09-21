using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class TimeAlignmentSessionTests
{
    [Fact]
    public void ForgettingTheReads_ForgetsTheSharedAutoBandToo()
    {
        var session = new TimeAlignmentSession(new TimeAlignmentOptions(), new AnalyzerDocument(), new CompareSelection());
        session.Land(TimeAlignmentOutcome.Failed("no signal", new DominantBand(200, 2_000, 800), autoBandShared: true));

        session.ForgetReads();

        Assert.Null(session.AutoBand);
        Assert.False(session.AutoBandShared);
        Assert.Equal("detected: waiting for a record", TimeAlignmentBand.AutoBandCaption(session));
    }
}
