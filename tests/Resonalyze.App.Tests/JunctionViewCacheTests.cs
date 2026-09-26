using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using OxyPlot;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

// Both view kinds share one cache entry type, so the key cases run on the cheaper coherence view.
public sealed class JunctionViewCacheTests
{
    private const int SampleRate = 48_000;
    private const int IrLength = 16_384;

    private static readonly JsonSerializerOptions Exact = new()
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    [Fact]
    public void AnUnmovedPair_ReadsTheViewABuildGives_OnceBuilt()
    {
        var cache = new JunctionViewCache();
        (AdjacentPair pair, List<ProcessedChannel> scope) = Junction();

        JunctionCorrelationView correlation = cache.Correlation(pair, scope);
        JunctionCoherenceView coherence = cache.Coherence(pair, scope);
        (AdjacentPair again, List<ProcessedChannel> againScope) = Rebuilt(pair, scope);

        AssertSameView(JunctionViews.BuildCorrelationView(pair, scope), correlation);
        AssertSameView(JunctionViews.BuildCoherenceView(pair, scope), coherence);
        Assert.Same(correlation, cache.Correlation(again, againScope));
        Assert.Same(coherence, cache.Coherence(again, againScope));
    }

    [Fact]
    public void WhatTheCroppedPairDoesNotRead_KeepsTheView()
    {
        (AdjacentPair pair, List<ProcessedChannel> scope) = Junction();
        var copied = pair.Upper with { ImpulseResponse = (Complex[])pair.Upper.ImpulseResponse.Clone() };
        ProcessedChannel later = Channel("E", Impulse(10_400, -0.7), default);
        var unread = new List<(AdjacentPair Pair, List<ProcessedChannel> Scope)>
        {
            (pair with { Upper = copied }, [pair.Lower, copied, scope[2]]),
            (pair, [pair.Lower, pair.Upper, later]),
            (pair, [pair.Lower, pair.Upper]),
        };

        var cache = new JunctionViewCache();
        JunctionCoherenceView coherence = cache.Coherence(pair, scope);
        foreach ((AdjacentPair same, List<ProcessedChannel> sameScope) in unread)
        {
            Assert.Same(coherence, cache.Coherence(same, sameScope));
        }

        AssertSameView(JunctionViews.BuildCoherenceView(pair, [pair.Lower, pair.Upper, later]), coherence);
    }

    [Fact]
    public void EveryInputTheViewReads_RebuildsIt()
    {
        (AdjacentPair pair, List<ProcessedChannel> scope) = Junction();
        var edited = pair.Upper with { ImpulseResponse = Nudged(pair.Upper.ImpulseResponse) };
        var editedLower = pair.Lower with { ImpulseResponse = Nudged(pair.Lower.ImpulseResponse) };
        var ranged = pair.Upper with { ValidRange = new ValidSampleRange(9_900, IrLength) };
        var rangedLower = pair.Lower with { ValidRange = new ValidSampleRange(9_900, IrLength) };
        var faster = pair.Lower with { SampleRate = 96_000 };
        ProcessedChannel earlier = Channel("F", Impulse(9_000, 0.4), default);
        var read = new List<(AdjacentPair Pair, List<ProcessedChannel> Scope)>
        {
            (pair with { Upper = edited }, [pair.Lower, edited, scope[2]]),
            (pair with { Lower = editedLower }, [editedLower, pair.Upper, scope[2]]),
            (pair with { Upper = ranged }, [pair.Lower, ranged, scope[2]]),
            (pair with { Lower = rangedLower }, [rangedLower, pair.Upper, scope[2]]),
            (pair with { Lower = faster }, [faster, pair.Upper, scope[2]]),
            (pair with { CrossoverHz = 1_600 }, scope),
            (pair with { BandLowHz = 700 }, scope),
            (pair with { BandHighHz = 3_200 }, scope),
            (pair, [pair.Lower, pair.Upper, earlier]),
        };

        var cache = new JunctionViewCache();
        JunctionCoherenceView before = cache.Coherence(pair, scope);
        foreach ((AdjacentPair changed, List<ProcessedChannel> changedScope) in read)
        {
            Assert.NotSame(before, cache.Coherence(changed, changedScope));
            before = cache.Coherence(pair, scope);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [InlineData(null)]
    public void ARenamedChannel_IsRetitled(bool? lower)
    {
        var cache = new JunctionViewCache();
        (AdjacentPair pair, List<ProcessedChannel> scope) = Junction();
        pair.Lower.Channel.Name = "A-B";
        pair.Upper.Channel.Name = "C";
        JunctionCoherenceView before = cache.Coherence(pair, scope);

        if (lower is { } which)
        {
            (which ? pair.Lower : pair.Upper).Channel.Name = "Renamed";
        }
        else
        {
            // The same title from other names.
            pair.Lower.Channel.Name = "A";
            pair.Upper.Channel.Name = "B-C";
        }

        JunctionCoherenceView after = cache.Coherence(pair, scope);
        Assert.NotSame(before, after);
        Assert.Equal(pair.Upper.Channel.Name, after.UpperName);
    }

    private static (AdjacentPair Pair, List<ProcessedChannel> Scope) Junction()
    {
        // Fronts past the crop's 8192-sample lead, so an earlier channel moves the shared window.
        ProcessedChannel lower = Channel("C", Impulse(10_000, 0.5), new ValidSampleRange(9_800, IrLength));
        ProcessedChannel upper = Channel("D", Impulse(10_024, -0.3), new ValidSampleRange(9_800, IrLength));
        ProcessedChannel third = Channel("E", Impulse(10_120, 0.4), default);
        return (new AdjacentPair(lower, upper, 1_500, 750, 3_000), [lower, upper, third]);
    }

    // Equal records around the same arrays, as the next render hands them over.
    private static (AdjacentPair Pair, List<ProcessedChannel> Scope) Rebuilt(
        AdjacentPair pair, List<ProcessedChannel> scope)
    {
        List<ProcessedChannel> copies = [.. scope.Select(item => item with { })];
        return (new AdjacentPair(
            copies[scope.IndexOf(pair.Lower)],
            copies[scope.IndexOf(pair.Upper)],
            pair.CrossoverHz,
            pair.BandLowHz,
            pair.BandHighHz), copies);
    }

    private static Complex[] Nudged(Complex[] ir)
    {
        Complex[] nudged = (Complex[])ir.Clone();
        nudged[10_524] = new Complex(Math.BitIncrement(nudged[10_524].Real), 0);
        return nudged;
    }

    private static Complex[] Impulse(int front, double echo)
    {
        var ir = new Complex[IrLength];
        ir[front] = 1.0;
        ir[front + 240] = echo;
        ir[front + 1_700] = 0.2;
        return ir;
    }

    private static ProcessedChannel Channel(string name, Complex[] ir, ValidSampleRange range) =>
        new(
            new VirtualCrossoverChannel(name) { SampleRate = SampleRate },
            ir,
            VirtualCrossoverAnalysis.FindPeakIndex(ir),
            SampleRate,
            OxyColors.White,
            range);

    private static void AssertSameView(object expected, object actual) =>
        Assert.Equal(JsonSerializer.Serialize(expected, Exact), JsonSerializer.Serialize(actual, Exact));
}
