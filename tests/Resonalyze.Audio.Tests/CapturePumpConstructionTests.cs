namespace Resonalyze.Audio.Tests;

/// <summary>
/// The shared pump base creates its worker thread but must not start it: a
/// derived constructor still validates its arguments and lays out its slots
/// after the base one returns, and anything it throws leaves the object
/// unreachable. A worker already running at that point could never be disposed
/// and would sit on Monitor.Wait for the process lifetime.
/// </summary>
public sealed class CapturePumpConstructionTests
{
    [Fact]
    public void TheBaseConstructor_LeavesTheWorkerUnstarted()
    {
        ProbePump pump = ProbePump.ConstructWithoutStarting();

        Assert.False(pump.WorkerStarted);

        // And a pump in that state still disposes cleanly — Thread.Join on an
        // unstarted thread would otherwise throw ThreadStateException.
        pump.Dispose();
    }

    [Fact]
    public void ADerivedConstructorThatThrows_LeavesNoRunningWorker()
    {
        Assert.Throws<InvalidOperationException>(() => new ProbePump(failInConstructor: true));

        // The instance the failed constructor was building, captured on the way
        // through: the base must not have started its worker.
        ProbePump abandoned = Assert.IsType<ProbePump>(ProbePump.LastConstructed);
        Assert.False(abandoned.WorkerStarted);
        abandoned.Dispose();
    }

    [Fact]
    public void AStartedPump_ReportsItsWorkerAndStillDisposes()
    {
        using var pump = new ProbePump(failInConstructor: false);

        Assert.True(pump.WorkerStarted);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void PcmPump_RejectsANonPositivePacketSize(int maximumPacketBytes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PcmCapturePump(maximumPacketBytes, _ => { }, (_, _) => { }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AsioPump_RejectsANonPositiveChannelCount(int channelCount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AsioCapturePump(channelCount, _ => { }, (_, _) => { }));
    }

    private sealed class ProbePump : CapturePump<ProbePump.ProbeSlot, int>
    {
        private ProbePump(bool failInConstructor, bool start)
            : base(2, "Probe", "probe overflow", _ => { }, (_, _) => { })
        {
            // Captured before the throw so the test can inspect the instance the
            // failed construction abandoned.
            LastConstructed = this;
            if (failInConstructor)
            {
                throw new InvalidOperationException("probe constructor failure");
            }

            if (start)
            {
                StartWorker();
            }
        }

        public ProbePump(bool failInConstructor)
            : this(failInConstructor, start: true)
        {
        }

        internal static ProbePump? LastConstructed { get; private set; }

        internal static ProbePump ConstructWithoutStarting() =>
            new(failInConstructor: false, start: false);

        protected override int CreateBlock(ProbeSlot slot) => slot.Generation;

        internal sealed class ProbeSlot : ICapturePumpSlot
        {
            public int Generation { get; set; }
        }
    }
}
