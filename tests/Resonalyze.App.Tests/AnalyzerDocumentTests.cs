using System.Numerics;

namespace Resonalyze.App.Tests;

/// <summary>The open measurement: one holder at a time, whole results only, and the newest request wins.</summary>
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

        document.Install(result, @"C:\ir\a.json");

        Assert.Same(result, document.Result);
        Assert.Equal(@"C:\ir\a.json", document.SourceName);
        Assert.False(document.IsBusy);
        Assert.Equal(1, changes);
    }

    // A run or an import holds the document from its first read; a load landing meanwhile would be overwritten.
    [Fact]
    public void WhileHeldNothingElseInstallsOrHoldsIt()
    {
        var document = new AnalyzerDocument();
        MeasurementResult first = Result();
        document.Install(first, null);

        using (document.Acquire())
        {
            Assert.True(document.IsBusy);
            Assert.Throws<InvalidOperationException>(() => document.Install(Result(), null));
            Assert.Throws<InvalidOperationException>(() => document.Acquire());
            Assert.Same(first, document.Result);
        }

        Assert.False(document.IsBusy);
        MeasurementResult second = Result();
        document.Install(second, null);
        Assert.Same(second, document.Result);
    }

    [Fact]
    public void ReleasingTwiceDoesNotFreeANewerHolder()
    {
        var document = new AnalyzerDocument();
        IDisposable first = document.Acquire();
        first.Dispose();
        using IDisposable second = document.Acquire();

        first.Dispose();

        Assert.True(document.IsBusy);
    }

    [Fact]
    public void ARefusedResultLeavesTheOpenOneAlone()
    {
        var document = new AnalyzerDocument();
        MeasurementResult open = Result();
        document.Install(open, "open.json");
        MeasurementResult empty = open with
        {
            SweepDeconvolution = new MeasurementImpulseResponse([], 0)
        };

        Assert.Throws<ArgumentException>(() => document.Install(empty, "empty.json"));

        Assert.Same(open, document.Result);
        Assert.Equal("open.json", document.SourceName);
    }

    [Fact]
    public void OnlyTheNewestActivationIsCurrent()
    {
        var document = new AnalyzerDocument();

        long first = document.BeginActivation();
        long second = document.BeginActivation();

        Assert.False(document.IsCurrent(first));
        Assert.True(document.IsCurrent(second));
    }

    [Fact]
    public void ASaveRenamesAndAClearEmpties()
    {
        var document = new AnalyzerDocument();
        MeasurementResult result = Result();
        document.Install(result, null);
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
