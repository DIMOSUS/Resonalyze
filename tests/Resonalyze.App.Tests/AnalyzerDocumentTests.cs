using System.Numerics;

namespace Resonalyze.App.Tests;

/// <summary>The open measurement: one holder at a time, whole results only, and the input started last wins.</summary>
public sealed class AnalyzerDocumentTests
{
    private static MeasurementResult Result(Complex[]? sweep = null) =>
        TestMeasurementResults.Restored(
            lowFrequencyHz: 20,
            highFrequencyHz: 20_000,
            sampleRate: 48_000,
            bits: 24,
            sweepDurationSeconds: 1.0,
            playChannel: PlaybackChannel.Mono,
            sweepDeconvolutionImpulseResponse: sweep ?? [Complex.Zero, Complex.One, Complex.Zero],
            sweepDeconvolutionPeakIndex: 1);

    [Fact]
    public void AnInstalledResultIsOpenUnderItsName()
    {
        var document = new AnalyzerDocument();
        int changes = 0;
        document.Changed += () => changes++;
        MeasurementResult result = Result();

        Assert.True(document.TryBegin()!.Install(result, @"C:\ir\a.json"));

        Assert.Same(result, document.Result);
        Assert.Equal(@"C:\ir\a.json", document.SourceName);
        Assert.False(document.IsBusy);
        Assert.Equal(1, changes);
    }

    // A run or an import holds the document from its first read; a load starting meanwhile would take its place.
    [Fact]
    public void WhileHeldNothingElseStarts()
    {
        var document = new AnalyzerDocument();
        MeasurementResult first = Result();
        document.TryBegin()!.Install(first, null);

        using (AnalyzerDocument.Request hold = document.TryAcquire()!)
        {
            Assert.True(document.IsBusy);
            Assert.Null(document.TryBegin());
            Assert.Null(document.TryAcquire());
            Assert.True(hold.IsCurrent);
            Assert.Same(first, document.Result);
        }

        Assert.False(document.IsBusy);
        MeasurementResult second = Result();
        Assert.True(document.TryBegin()!.Install(second, null));
        Assert.Same(second, document.Result);
    }

    // Views keep what they show while a producer holds the document, so they must hear when it starts.
    [Fact]
    public void AHolderIsAnnounced()
    {
        var document = new AnalyzerDocument();
        var busyWhenChanged = new List<bool>();
        document.Changed += () => busyWhenChanged.Add(document.IsBusy);

        using AnalyzerDocument.Request hold = document.TryAcquire()!;

        Assert.Equal([true], busyWhenChanged);
    }

    [Fact]
    public void AHolderThatLandsIsAnnouncedOnce()
    {
        var document = new AnalyzerDocument();
        AnalyzerDocument.Request hold = document.TryAcquire()!;
        MeasurementResult result = Result();
        var busyWhenChanged = new List<bool>();
        document.Changed += () => busyWhenChanged.Add(document.IsBusy);

        Assert.True(hold.Install(result, "import.wav"));
        hold.Dispose();

        Assert.Same(result, document.Result);
        Assert.Equal([false], busyWhenChanged);
    }

    // A run that aborts or an import that fails leaves the document as it was, but no longer held: the views must hear.
    [Fact]
    public void AHolderThatEndsWithoutAResultIsAnnounced()
    {
        var document = new AnalyzerDocument();
        AnalyzerDocument.Request hold = document.TryAcquire()!;
        var busyWhenChanged = new List<bool>();
        document.Changed += () => busyWhenChanged.Add(document.IsBusy);

        hold.Dispose();
        hold.Dispose();

        Assert.Equal([false], busyWhenChanged);
    }

    [Fact]
    public void ASupersededHolderIsAnnouncedWhenItLetsGo()
    {
        var document = new AnalyzerDocument();
        AnalyzerDocument.Request run = document.TryAcquire()!;
        document.Clear();
        var busyWhenChanged = new List<bool>();
        document.Changed += () => busyWhenChanged.Add(document.IsBusy);

        Assert.False(run.Install(Result(), null));

        Assert.Equal([false], busyWhenChanged);
    }

    [Fact]
    public void ReleasingTwiceDoesNotFreeANewerHolder()
    {
        var document = new AnalyzerDocument();
        AnalyzerDocument.Request first = document.TryAcquire()!;
        first.Dispose();
        using AnalyzerDocument.Request second = document.TryAcquire()!;

        first.Dispose();

        Assert.True(document.IsBusy);
    }

    // A load still reading when an import starts: the import is newer, whichever finishes first.
    [Fact]
    public void AnImportStartedAfterALoadWins()
    {
        var document = new AnalyzerDocument();
        MeasurementResult loaded = Result();
        MeasurementResult imported = Result();
        AnalyzerDocument.Request load = document.TryBegin()!;
        AnalyzerDocument.Request import = document.TryAcquire()!;

        Assert.False(load.Install(loaded, "slow.json"));
        Assert.Null(document.Result);
        Assert.True(document.IsBusy);

        Assert.True(import.Install(imported, "import.wav"));
        Assert.Same(imported, document.Result);
    }

    // New session while a run or an import is producing: its result must not land in the new session.
    [Fact]
    public void AClearSupersedesAHolder()
    {
        var document = new AnalyzerDocument();
        AnalyzerDocument.Request run = document.TryAcquire()!;

        document.Clear();

        Assert.False(run.IsCurrent);
        // Still producing: nothing else starts until it finishes.
        Assert.True(document.IsBusy);
        Assert.False(run.Install(Result(), null));
        Assert.False(document.HasResult);
        Assert.False(document.IsBusy);
    }

    [Fact]
    public void OnlyTheNewestRequestLands()
    {
        var document = new AnalyzerDocument();
        MeasurementResult older = Result();
        MeasurementResult newer = Result();
        AnalyzerDocument.Request first = document.TryBegin()!;
        AnalyzerDocument.Request second = document.TryBegin()!;

        Assert.True(second.Install(newer, "newer.json"));
        Assert.False(first.Install(older, "older.json"));

        Assert.Same(newer, document.Result);
        Assert.Equal("newer.json", document.SourceName);
    }

    [Fact]
    public void ARefusedResultLeavesTheOpenOneAlone()
    {
        var document = new AnalyzerDocument();
        MeasurementResult open = Result();
        document.TryBegin()!.Install(open, "open.json");
        MeasurementResult empty = open with
        {
            SweepDeconvolution = new MeasurementImpulseResponse([], 0)
        };

        Assert.Throws<ArgumentException>(() => document.TryBegin()!.Install(empty, "empty.json"));

        Assert.Same(open, document.Result);
        Assert.Equal("open.json", document.SourceName);
    }

    [Fact]
    public void ASaveRenamesAndAClearEmpties()
    {
        var document = new AnalyzerDocument();
        MeasurementResult result = Result();
        document.TryBegin()!.Install(result, null);
        int changes = 0;
        document.Changed += () => changes++;

        document.Rename(@"C:\ir\saved.json");
        Assert.Same(result, document.Result);
        Assert.Equal(@"C:\ir\saved.json", document.SourceName);

        document.Clear();
        Assert.False(document.HasResult);
        Assert.Null(document.SourceName);
        Assert.Equal(2, changes);
    }
}
