using System.Numerics;

namespace Resonalyze.Dsp.Tests;

public sealed class BroadbandOnsetTests
{
    private const int Rate = 48_000;

    private static Complex[] Silence(int length) => new Complex[length];

    [Fact]
    public void EstimateBroadbandOnset_SilenceIsInvalid()
    {
        BroadbandOnsetEstimate estimate =
            VirtualCrossoverAnalysis.EstimateBroadbandOnset(Silence(4096), Rate);

        Assert.False(estimate.IsValid);
    }

    [Fact]
    public void EstimateBroadbandOnset_DeltaOnsetSitsAtTheDeltaWithTinySpread()
    {
        // A delta's Hilbert envelope carries a short ~1/t skirt, so the low
        // thresholds cross a couple of samples before the peak — the crossings
        // must still hug the delta within a few samples, with a spread far
        // below any crossover period the lock would gate on.
        Complex[] impulseResponse = Silence(4096);
        const int DeltaIndex = 1_000;
        impulseResponse[DeltaIndex] = Complex.One;

        BroadbandOnsetEstimate estimate =
            VirtualCrossoverAnalysis.EstimateBroadbandOnset(impulseResponse, Rate);

        double deltaMs = DeltaIndex * 1_000.0 / Rate;
        double sampleMs = 1_000.0 / Rate;
        Assert.True(estimate.IsValid);
        Assert.InRange(estimate.OnsetMs, deltaMs - 4 * sampleMs, deltaMs + sampleMs);
        Assert.InRange(estimate.LateMs - estimate.EarlyMs, 0, 6 * sampleMs);
    }

    [Fact]
    public void EstimateBroadbandOnset_CrossingsAreMonotonicInTheThreshold()
    {
        // A slow Hann-windowed tone burst: the envelope rises over many samples,
        // so the crossings must order early <= onset <= late and all sit before
        // the envelope peak at the burst's center.
        Complex[] impulseResponse = Silence(8192);
        const int Start = 2_000;
        const int Length = 2_048;
        for (int i = 0; i < Length; i++)
        {
            double window = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (Length - 1)));
            impulseResponse[Start + i] = new Complex(
                window * Math.Sin(2 * Math.PI * 1_000.0 * i / Rate), 0);
        }

        BroadbandOnsetEstimate estimate =
            VirtualCrossoverAnalysis.EstimateBroadbandOnset(impulseResponse, Rate);

        double centerMs = (Start + Length / 2.0) * 1_000.0 / Rate;
        Assert.True(estimate.IsValid);
        Assert.True(estimate.EarlyMs <= estimate.OnsetMs);
        Assert.True(estimate.OnsetMs <= estimate.LateMs);
        Assert.True(estimate.LateMs < centerMs);
        Assert.True(estimate.EarlyMs >= Start * 1_000.0 / Rate - 1.0);
    }

    [Fact]
    public void EstimateBroadbandOnset_StrongerLateReflectionDoesNotUsurpTheFront()
    {
        // A credible direct front (well within the first-arrival search depth)
        // followed by a five-times-stronger reflection 20 ms later. Thresholds
        // measured against the crop's GLOBAL maximum would put the 25 % and
        // 50 % crossings on the reflection's front — tens of ms late, while
        // the spread between them stayed deceptively tight. All three
        // crossings must sit on the direct front instead.
        Complex[] impulseResponse = Silence(8_192);
        const int DirectIndex = 480; // 10 ms
        impulseResponse[DirectIndex] = new Complex(0.2, 0.0);
        impulseResponse[DirectIndex + 960] = Complex.One; // 30 ms

        BroadbandOnsetEstimate estimate =
            VirtualCrossoverAnalysis.EstimateBroadbandOnset(impulseResponse, Rate);

        double directMs = DirectIndex * 1_000.0 / Rate;
        Assert.True(estimate.IsValid);
        Assert.InRange(estimate.EarlyMs, directMs - 0.5, directMs + 0.1);
        Assert.InRange(estimate.OnsetMs, directMs - 0.5, directMs + 0.1);
        Assert.InRange(estimate.LateMs, directMs - 0.5, directMs + 0.1);
    }

    [Fact]
    public void EstimateBroadbandOnset_ReflectionsWithDifferentInterChannelDelayCannotSteer()
    {
        // The field trap the onset lock must not fall into: two channels whose
        // direct fronts are 2 ms apart while their strong reflections are only
        // 0.1 ms apart. Global-maximum thresholds would latch onto the
        // reflections and report a stable-looking 0.1 ms difference — four
        // periods wrong at a 2 kHz junction, with every recovery path shut by
        // the lock. Anchored to the credible first arrivals, all three
        // threshold differences must read the true 2 ms.
        Complex[] BuildChannel(int directIndex, int reflectionIndex)
        {
            Complex[] impulseResponse = Silence(8_192);
            impulseResponse[directIndex] = new Complex(0.2, 0.0);
            impulseResponse[reflectionIndex] = Complex.One;
            return impulseResponse;
        }

        BroadbandOnsetEstimate first = VirtualCrossoverAnalysis.EstimateBroadbandOnset(
            BuildChannel(480, 1_440), Rate);          // direct 10 ms, refl 30 ms
        BroadbandOnsetEstimate second = VirtualCrossoverAnalysis.EstimateBroadbandOnset(
            BuildChannel(576, 1_445), Rate);          // direct 12 ms, refl 30.1 ms

        Assert.True(first.IsValid);
        Assert.True(second.IsValid);
        Assert.InRange(second.EarlyMs - first.EarlyMs, 1.8, 2.2);
        Assert.InRange(second.OnsetMs - first.OnsetMs, 1.8, 2.2);
        Assert.InRange(second.LateMs - first.LateMs, 1.8, 2.2);
    }

    [Fact]
    public void EstimateBroadbandOnset_NoiseGradesBelowTheLockFloor()
    {
        // A noise-only record still yields three finite crossings whose spread
        // can look stable — only the envelope SNR exposes it. The grade must
        // fall below the engine's lock floor so the onset lock stands down.
        var random = new Random(20_260_717);
        Complex[] impulseResponse = Silence(65_536);
        for (int i = 0; i < impulseResponse.Length; i++)
        {
            impulseResponse[i] = new Complex(random.NextDouble() * 2.0 - 1.0, 0.0);
        }

        BroadbandOnsetEstimate estimate =
            VirtualCrossoverAnalysis.EstimateBroadbandOnset(impulseResponse, Rate);

        Assert.True(
            !estimate.IsValid ||
            estimate.SnrDb < AutoAlignmentEngine.OnsetLockMinimumSnrDb,
            $"Noise graded {estimate.SnrDb:0.0} dB — above the " +
            $"{AutoAlignmentEngine.OnsetLockMinimumSnrDb:0} dB lock floor.");
    }

    [Fact]
    public void EstimateBroadbandOnset_ZeroPaddedTailDoesNotInflateTheSnr()
    {
        // ApplyChain rounds every processed IR up to a power-of-two FFT
        // length, so the record the engine analyzes carries a synthetic
        // silent tail. The envelope noise floor is quantile-based, and half a
        // record of manufactured silence used to collapse it: the same noise
        // that grades ~6 dB raw graded ~60 dB padded — waving a noise-only
        // record through every SNR gate in the engine. The padded grade must
        // match the raw one and stay below the lock floor.
        var random = new Random(20_260_717);
        Complex[] raw = Silence(65_536);
        for (int i = 0; i < raw.Length; i++)
        {
            raw[i] = new Complex(random.NextDouble() * 2.0 - 1.0, 0.0);
        }
        Complex[] padded = Silence(131_072);
        Array.Copy(raw, padded, raw.Length);

        BroadbandOnsetEstimate rawEstimate =
            VirtualCrossoverAnalysis.EstimateBroadbandOnset(raw, Rate);
        BroadbandOnsetEstimate paddedEstimate =
            VirtualCrossoverAnalysis.EstimateBroadbandOnset(padded, Rate);

        Assert.True(rawEstimate.IsValid);
        Assert.True(paddedEstimate.IsValid);
        Assert.InRange(
            paddedEstimate.SnrDb,
            rawEstimate.SnrDb - 1.0,
            rawEstimate.SnrDb + 1.0);
        Assert.True(
            paddedEstimate.SnrDb < AutoAlignmentEngine.OnsetLockMinimumSnrDb,
            $"Padded noise graded {paddedEstimate.SnrDb:0.0} dB — above the " +
            $"{AutoAlignmentEngine.OnsetLockMinimumSnrDb:0} dB lock floor.");
    }

    [Fact]
    public void EstimateBroadbandOnset_ShortRecordPaddedPastItsMidpointIsStillCaught()
    {
        // The review catch on the padding trim's first cut: a SHORT input
        // through ApplyChain gains the 8192-sample minimum tail and rounds
        // up, so its content can end well BEFORE the record's midpoint (4096
        // noise samples become a 16384 record). A midpoint-based padding
        // signature waves that record through untrimmed. The signature is the
        // content region's DENSITY instead: measured noise is dense right up
        // to where the padding begins, however short the record.
        var random = new Random(20_260_719);
        Complex[] raw = Silence(4_096);
        for (int i = 0; i < raw.Length; i++)
        {
            raw[i] = new Complex(random.NextDouble() * 2.0 - 1.0, 0.0);
        }
        Complex[] padded = Silence(16_384);
        Array.Copy(raw, padded, raw.Length);

        BroadbandOnsetEstimate rawEstimate =
            VirtualCrossoverAnalysis.EstimateBroadbandOnset(raw, Rate);
        BroadbandOnsetEstimate paddedEstimate =
            VirtualCrossoverAnalysis.EstimateBroadbandOnset(padded, Rate);

        Assert.True(rawEstimate.IsValid);
        Assert.True(paddedEstimate.IsValid);
        Assert.InRange(
            paddedEstimate.SnrDb,
            rawEstimate.SnrDb - 1.0,
            rawEstimate.SnrDb + 1.0);
        Assert.True(
            paddedEstimate.SnrDb < AutoAlignmentEngine.OnsetLockMinimumSnrDb,
            $"Short padded noise graded {paddedEstimate.SnrDb:0.0} dB — above " +
            $"the {AutoAlignmentEngine.OnsetLockMinimumSnrDb:0} dB lock floor.");
    }

    [Fact]
    public void EstimateBroadbandOnset_PaddingIsCaughtRegardlessOfTheLastSampleValue()
    {
        // The review catch on the trim's midpoint-based first cut: whether it
        // fired hinged on the value of the SINGLE last noise sample (one
        // quiet sample pushed the detected content end below the midpoint
        // and the whole padded record came back, ~60 dB again). Zero the
        // last sample explicitly: the grade must still match the raw
        // record's.
        var random = new Random(20_260_720);
        Complex[] raw = Silence(65_536);
        for (int i = 0; i < raw.Length; i++)
        {
            raw[i] = new Complex(random.NextDouble() * 2.0 - 1.0, 0.0);
        }
        raw[^1] = Complex.Zero;
        Complex[] padded = Silence(131_072);
        Array.Copy(raw, padded, raw.Length);

        BroadbandOnsetEstimate rawEstimate =
            VirtualCrossoverAnalysis.EstimateBroadbandOnset(raw, Rate);
        BroadbandOnsetEstimate paddedEstimate =
            VirtualCrossoverAnalysis.EstimateBroadbandOnset(padded, Rate);

        Assert.True(rawEstimate.IsValid);
        Assert.True(paddedEstimate.IsValid);
        Assert.InRange(
            paddedEstimate.SnrDb,
            rawEstimate.SnrDb - 1.0,
            rawEstimate.SnrDb + 1.0);
        Assert.True(
            paddedEstimate.SnrDb < AutoAlignmentEngine.OnsetLockMinimumSnrDb);
    }

    [Fact]
    public void EstimateBroadbandOnset_ApplyChainMetadataPinsTheAnalysisRange()
    {
        // The engine's own path for a short import: a 4096-sample noise
        // record through a REAL scale-only ApplyChain grows to 16384 (the
        // 8192-sample minimum tail plus power-of-two rounding), content
        // ending far before the record's midpoint. The ApplyChain
        // validSampleCount metadata pins the analysis to the measured range
        // no matter what any amplitude heuristic makes of the record — the
        // read must equal an explicit crop to the metadata, and a noise-only
        // record must stay below the lock floor.
        var random = new Random(20_260_721);
        var raw = new Complex[4_096];
        for (int i = 0; i < raw.Length; i++)
        {
            raw[i] = new Complex(random.NextDouble() * 2.0 - 1.0, 0.0);
        }

        Complex[] processed = VirtualCrossoverAnalysis.ApplyChain(
            raw, new DspChannelChain(GainDb: -3), Rate,
            out ValidSampleRange validRange);

        Assert.Equal(new ValidSampleRange(0, raw.Length), validRange);
        Assert.True(processed.Length >= 4 * raw.Length);

        BroadbandOnsetEstimate viaMetadata =
            VirtualCrossoverAnalysis.EstimateBroadbandOnset(
                processed, Rate, validRange);
        BroadbandOnsetEstimate viaCrop =
            VirtualCrossoverAnalysis.EstimateBroadbandOnset(
                processed.Take(validRange.EndSample).ToArray(), Rate);

        Assert.True(viaMetadata.IsValid);
        Assert.Equal(viaCrop.SnrDb, viaMetadata.SnrDb, 6);
        Assert.Equal(viaCrop.OnsetMs, viaMetadata.OnsetMs, 6);
        Assert.True(
            viaMetadata.SnrDb < AutoAlignmentEngine.OnsetLockMinimumSnrDb,
            $"Short padded noise graded {viaMetadata.SnrDb:0.0} dB via " +
            "metadata — above the lock floor.");
    }

    [Fact]
    public void EstimateBroadbandOnset_DelayPrefixDoesNotInflateTheSnr()
    {
        // The review catch on the end-only metadata: the chain DELAY shifts
        // the content right and manufactures a silent PREFIX inside
        // [0..end) — 25 ms of zeros ahead of a short noise record lifted its
        // grade from ~6 to ~26 dB, past the 20 dB onset-lock floor. The
        // range metadata must exclude the prefix too, and the reported
        // positions must stay in full-record coordinates: the onset of the
        // delayed record reads the delay later, not at zero.
        var random = new Random(20_260_724);
        var raw = new Complex[4_096];
        for (int i = 0; i < raw.Length; i++)
        {
            raw[i] = new Complex(random.NextDouble() * 2.0 - 1.0, 0.0);
        }

        Complex[] processed = VirtualCrossoverAnalysis.ApplyChain(
            raw, new DspChannelChain(DelayMs: 25), Rate,
            out ValidSampleRange validRange);

        int delaySamples = (int)(25.0 / 1_000.0 * Rate);
        Assert.Equal(
            new ValidSampleRange(delaySamples, delaySamples + raw.Length),
            validRange);

        BroadbandOnsetEstimate rawEstimate =
            VirtualCrossoverAnalysis.EstimateBroadbandOnset(raw, Rate);
        BroadbandOnsetEstimate viaMetadata =
            VirtualCrossoverAnalysis.EstimateBroadbandOnset(
                processed, Rate, validRange);

        Assert.True(rawEstimate.IsValid);
        Assert.True(viaMetadata.IsValid);
        Assert.InRange(
            viaMetadata.SnrDb, rawEstimate.SnrDb - 1.0, rawEstimate.SnrDb + 1.0);
        Assert.True(
            viaMetadata.SnrDb < AutoAlignmentEngine.OnsetLockMinimumSnrDb,
            $"Delayed noise graded {viaMetadata.SnrDb:0.0} dB — above the " +
            $"{AutoAlignmentEngine.OnsetLockMinimumSnrDb:0} dB lock floor.");
        Assert.InRange(viaMetadata.OnsetMs - rawEstimate.OnsetMs, 24.9, 25.1);
    }

    [Fact]
    public void EstimateBroadbandOnset_LongRingingChainMetadataStillBoundsTheAnalysis()
    {
        // A 20 Hz / Q 10 PEQ boost rings for seconds, so ApplyChain's tail
        // grows far past the minimum and the manufactured region is the
        // filter kernel's decay, not zeros — no amplitude heuristic can tell
        // it from measurement. The metadata still bounds the analysis at the
        // measured range: the read equals an explicit crop to it.
        var random = new Random(20_260_722);
        var raw = new Complex[65_536];
        for (int i = 0; i < raw.Length; i++)
        {
            raw[i] = new Complex(random.NextDouble() * 2.0 - 1.0, 0.0);
        }

        var chain = new DspChannelChain(
            Peq: new EqualizationCurve([new PeqBand(20, 10, 12)]));
        Complex[] processed = VirtualCrossoverAnalysis.ApplyChain(
            raw, chain, Rate, out ValidSampleRange validRange);

        Assert.Equal(new ValidSampleRange(0, raw.Length), validRange);
        Assert.True(processed.Length > 2 * raw.Length);

        BroadbandOnsetEstimate viaMetadata =
            VirtualCrossoverAnalysis.EstimateBroadbandOnset(
                processed, Rate, validRange);
        BroadbandOnsetEstimate viaCrop =
            VirtualCrossoverAnalysis.EstimateBroadbandOnset(
                processed.Take(validRange.EndSample).ToArray(), Rate);

        Assert.True(viaMetadata.IsValid);
        Assert.Equal(viaCrop.SnrDb, viaMetadata.SnrDb, 6);
        Assert.Equal(viaCrop.OnsetMs, viaMetadata.OnsetMs, 6);
    }

    [Fact]
    public void EstimateBroadbandOnset_SubCredibleDirectFollowsTheDominantArrival()
    {
        // A direct front below the first-arrival search depth (25 dB under the
        // dominant peak) is invisible to every arrival detector in the tool;
        // the onset then deliberately times the dominant arrival — consistent
        // with the stage-1 seeds and the Time Alignment display — rather than
        // guessing at energy the analysis chain does not trust.
        Complex[] impulseResponse = Silence(8_192);
        impulseResponse[480] = new Complex(0.03, 0.0);  // -30 dB direct, 10 ms
        impulseResponse[1_440] = Complex.One;           // dominant, 30 ms

        BroadbandOnsetEstimate estimate =
            VirtualCrossoverAnalysis.EstimateBroadbandOnset(impulseResponse, Rate);

        double dominantMs = 1_440 * 1_000.0 / Rate;
        Assert.True(estimate.IsValid);
        Assert.InRange(estimate.OnsetMs, dominantMs - 0.5, dominantMs + 0.1);
    }

    [Fact]
    public void EstimateBroadbandOnset_FrontAtTheCropStartClampsToZero()
    {
        // A front already above the thresholds at sample 0 (a crop boundary
        // cutting into the rise) reads 0.0 — never a negative time, which
        // would silently skew the onset anchor and the spread gate.
        Complex[] impulseResponse = Silence(4_096);
        impulseResponse[0] = Complex.One;

        BroadbandOnsetEstimate estimate =
            VirtualCrossoverAnalysis.EstimateBroadbandOnset(impulseResponse, Rate);

        Assert.True(estimate.IsValid);
        Assert.True(estimate.EarlyMs >= 0.0);
        Assert.True(estimate.OnsetMs >= 0.0);
        Assert.True(estimate.LateMs >= 0.0);
    }

    [Fact]
    public void EstimateBroadbandOnset_DelayShiftsTheOnsetOneToOne()
    {
        // The onset difference between an IR and its delayed copy is the delay —
        // the invariant the auto-delay onset anchor rests on.
        Complex[] original = Silence(8192);
        const int Start = 1_500;
        for (int i = 0; i < 512; i++)
        {
            double window = 0.5 * (1 - Math.Cos(2 * Math.PI * i / 511.0));
            original[Start + i] = new Complex(
                window * Math.Sin(2 * Math.PI * 2_000.0 * i / Rate), 0);
        }

        const int ShiftSamples = 733;
        Complex[] delayed = Silence(8192);
        Array.Copy(original, 0, delayed, ShiftSamples, 8192 - ShiftSamples);

        BroadbandOnsetEstimate first =
            VirtualCrossoverAnalysis.EstimateBroadbandOnset(original, Rate);
        BroadbandOnsetEstimate second =
            VirtualCrossoverAnalysis.EstimateBroadbandOnset(delayed, Rate);

        double expectedMs = ShiftSamples * 1_000.0 / Rate;
        Assert.True(first.IsValid);
        Assert.True(second.IsValid);
        Assert.InRange(
            second.OnsetMs - first.OnsetMs,
            expectedMs - 0.05,
            expectedMs + 0.05);
    }
}
