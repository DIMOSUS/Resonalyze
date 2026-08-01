using System;
using System.Numerics;
using Resonalyze.Dsp;
using Xunit;

namespace Resonalyze.Dsp.Tests;

// Pins the pure ESS decomposition: the harmonic-packet geometry (positions,
// ordering, neighbour boundaries, Nyquist/order reach) and the shared spectral
// normalization that makes H1 and every Hn directly comparable regardless of
// window length, zero-padding, sign or level. These invariants are what let a
// later stage compute HDn = |Hn|/|H1| as an honest ratio.
public sealed class EssHarmonicAnalysisTests
{
    private const int SampleRate = 48_000;
    private const int Octaves = 10;
    private const int SweepSamples = 200_000;
    private const int PeakIndex = 150_000;

    private static EssSweepMetadata Sweep() =>
        EssSweepMetadata.FromExponentialSweep(SampleRate, Octaves, SweepSamples, PeakIndex);

    [Fact]
    public void FromExponentialSweep_EndsAtNyquistAndSpansTheOctavesDownward()
    {
        EssSweepMetadata sweep = Sweep();

        Assert.Equal(24_000.0, sweep.EndFrequencyHz, 6);
        Assert.Equal(24_000.0 / 1024.0, sweep.StartFrequencyHz, 6);
        Assert.Equal(1024.0, sweep.FrequencyRatio, 6);
        Assert.Equal(SweepSamples / (double)SampleRate, sweep.DurationSeconds, 9);
    }

    [Fact]
    public void HarmonicTimeOffset_MatchesTheLogSweepLaw()
    {
        EssSweepMetadata sweep = Sweep();

        // Δt(n) = L · ln(n) / ln(f2/f1). For a 10-octave sweep ln(f2/f1)=10·ln2,
        // so H2 advances by exactly L/10.
        double duration = sweep.DurationSeconds;
        Assert.Equal(duration / 10.0, EssHarmonicAnalysis.HarmonicTimeOffsetSeconds(sweep, 2), 9);
        Assert.Equal(0.0, EssHarmonicAnalysis.HarmonicTimeOffsetSeconds(sweep, 1), 12);

        // H3 sits farther back than H2, H4 farther still (monotone in order).
        double h2 = EssHarmonicAnalysis.HarmonicTimeOffsetSeconds(sweep, 2);
        double h3 = EssHarmonicAnalysis.HarmonicTimeOffsetSeconds(sweep, 3);
        double h4 = EssHarmonicAnalysis.HarmonicTimeOffsetSeconds(sweep, 4);
        Assert.True(h3 > h2 && h4 > h3);
    }

    [Fact]
    public void HarmonicTimeOffset_ScalesWithSweepDurationNotLevel()
    {
        EssSweepMetadata shortSweep =
            EssSweepMetadata.FromExponentialSweep(SampleRate, Octaves, SweepSamples, PeakIndex);
        EssSweepMetadata longSweep =
            EssSweepMetadata.FromExponentialSweep(SampleRate, Octaves, SweepSamples * 2, PeakIndex);

        // Doubling the sweep length doubles the harmonic advance (same octave span).
        Assert.Equal(
            2.0 * EssHarmonicAnalysis.HarmonicTimeOffsetSeconds(shortSweep, 3),
            EssHarmonicAnalysis.HarmonicTimeOffsetSeconds(longSweep, 3),
            9);
    }

    [Fact]
    public void BuildWindow_PlacesPacketsBeforeThePeakInHarmonicOrder()
    {
        EssSweepMetadata sweep = Sweep();

        HarmonicWindowDefinition h1 = EssHarmonicAnalysis.BuildWindow(sweep, 1, 0.5);
        HarmonicWindowDefinition h2 = EssHarmonicAnalysis.BuildWindow(sweep, 2, 0.5);
        HarmonicWindowDefinition h3 = EssHarmonicAnalysis.BuildWindow(sweep, 3, 0.5);

        Assert.Equal(PeakIndex, h1.PeakSample);
        Assert.True(h2.PeakSample < h1.PeakSample, "H2 sits before the linear peak.");
        Assert.True(h3.PeakSample < h2.PeakSample, "H3 sits before H2.");

        // Each window brackets its own peak.
        Assert.InRange(h2.PeakSample, h2.StartSample, h2.EndSample);
        Assert.InRange(h3.PeakSample, h3.StartSample, h3.EndSample);
    }

    [Fact]
    public void BuildWindow_LinearPacketIsSymmetricAroundThePeak()
    {
        HarmonicWindowDefinition h1 = EssHarmonicAnalysis.BuildWindow(Sweep(), 1, 0.5);

        int before = h1.PeakSample - h1.StartSample;
        int after = h1.EndSample - h1.PeakSample;
        Assert.True(Math.Abs(before - after) <= 1, "H1 window should be symmetric about the peak.");
    }

    [Fact]
    public void BuildWindow_AdjacentPacketsMeetAtTheirSharedBoundaryWithoutOverlap()
    {
        EssSweepMetadata sweep = Sweep();
        HarmonicWindowDefinition h2 = EssHarmonicAnalysis.BuildWindow(sweep, 2, 0.5);
        HarmonicWindowDefinition h3 = EssHarmonicAnalysis.BuildWindow(sweep, 3, 0.5);

        // H3's earlier edge and H2's later edge are both defined by the SAME
        // geometric-mean boundary, so H3.End (larger index) meets H2.Start (the
        // shared √6 boundary) within a sample of rounding — no packet leaks into
        // the other's nominal window.
        int sharedBoundary = PeakIndex - EssHarmonicAnalysis.HarmonicOffsetSamples(sweep, Math.Sqrt(6.0));
        Assert.True(Math.Abs(h2.StartSample - sharedBoundary) <= 1);
        Assert.True(Math.Abs(h3.EndSample - sharedBoundary) <= 1);
        Assert.True(h3.EndSample <= h2.StartSample + 1, "H2 and H3 windows must not overlap.");
    }

    [Fact]
    public void MaxExcitationHz_StopsEachOrderAtNyquistOverOrder()
    {
        EssSweepMetadata sweep = Sweep();

        Assert.Equal(24_000.0, sweep.MaxExcitationHz(1), 6); // min(end, Nyq/1) = 24k
        Assert.Equal(12_000.0, sweep.MaxExcitationHz(2), 6); // Nyq/2
        Assert.Equal(8_000.0, sweep.MaxExcitationHz(3), 6);  // Nyq/3
    }

    [Fact]
    public void AnalyzeEssHarmonics_SeparatesLinearAndHarmonicPackets()
    {
        double[] impulse = new double[SweepSamples];
        impulse[PeakIndex] = 1.0;

        EssHarmonicDecomposition decomposition = EssHarmonicAnalysis.AnalyzeEssHarmonics(
            impulse,
            Sweep(),
            new HarmonicAnalysisOptions(MaxHarmonic: 5));

        Assert.Equal(1, decomposition.Linear.Order);
        Assert.Equal(4, decomposition.Harmonics.Count);
        Assert.Equal(new[] { 2, 3, 4, 5 }, decomposition.Harmonics.Select(h => h.Order).ToArray());

        // All packets share one FFT grid (shared sample rate and length), so their
        // bins line up for later cross-order combination.
        int fft = decomposition.Linear.Spectrum.FftLength;
        Assert.All(decomposition.Harmonics, h => Assert.Equal(fft, h.Spectrum.FftLength));
    }

    // ----- Shared spectral normalization invariants -----
    //
    // A packet is a contained impulse response under a unity plateau, so the FFT
    // magnitude IS the packet's transfer magnitude. These pin the invariant that
    // makes |Hn|/|H1| honest: the magnitude is independent of window length,
    // zero-padding, sign and placement, so a ratio of two packets recovers their
    // amplitude ratio exactly regardless of the two windows' lengths.

    private const int Field = 20_000;

    private static HarmonicWindowDefinition RectWindow(int start, int length) =>
        new(Order: 1, PeakSample: start + length / 2, StartSample: start, EndSample: start + length - 1,
            FadeInSamples: 0, FadeOutSamples: 0);

    private static double ImpulseMagnitude(double height, int start, int length, int fftLength, int bin)
    {
        // A single impulse sitting under the (rectangular, plateau=1) window.
        double[] field = new double[Field];
        field[start + length / 2] = height;
        WindowedSpectrum spectrum = EssHarmonicAnalysis.ComputeWindowedSpectrum(
            field, RectWindow(start, length), fftLength, SampleRate);
        return spectrum.AmplitudeAt(bin);
    }

    [Fact]
    public void ComputeWindowedSpectrum_ReadsAContainedImpulseAsItsHeight()
    {
        // The impulse has a flat spectrum, so every bin equals its height.
        Assert.Equal(0.5, ImpulseMagnitude(0.5, 2_000, 1_024, 1_024, 40), 9);
        Assert.Equal(0.5, ImpulseMagnitude(0.5, 2_000, 1_024, 1_024, 137), 9);
    }

    [Fact]
    public void ComputeWindowedSpectrum_RatioIsIndependentOfTheTwoWindowLengths()
    {
        // The load-bearing invariant for HDn: a "harmonic" impulse of 0.02 read
        // through a 4096-sample window and a "linear" impulse of 1.0 read through a
        // 1024-sample window still yield a ratio of exactly 0.02.
        double harmonic = ImpulseMagnitude(0.02, 3_000, 4_096, 8_192, 200);
        double linear = ImpulseMagnitude(1.0, 3_000, 1_024, 8_192, 200);
        Assert.Equal(0.02, harmonic / linear, 9);
    }

    [Fact]
    public void ComputeWindowedSpectrum_IsInvariantToTimeShift()
    {
        double a = ImpulseMagnitude(0.5, 1_000, 1_024, 1_024, 40);
        double b = ImpulseMagnitude(0.5, 6_000, 1_024, 1_024, 40);
        Assert.Equal(a, b, 12);
    }

    [Fact]
    public void ComputeWindowedSpectrum_ZeroPaddingChangesGridNotLevel()
    {
        double tight = ImpulseMagnitude(0.5, 2_000, 1_024, 1_024, 40);
        double padded = ImpulseMagnitude(0.5, 2_000, 1_024, 2_048, 80);
        Assert.Equal(tight, padded, 9);
    }

    [Fact]
    public void ComputeWindowedSpectrum_IsInvariantToSign()
    {
        double a = ImpulseMagnitude(0.5, 2_000, 1_024, 1_024, 40);
        double b = ImpulseMagnitude(-0.5, 2_000, 1_024, 1_024, 40);
        Assert.Equal(a, b, 12);
    }

    [Fact]
    public void ComputeWindowedSpectrum_ScalingByTwoRaisesLevelBy6Db()
    {
        double quiet = ImpulseMagnitude(0.25, 2_000, 1_024, 1_024, 40);
        double loud = ImpulseMagnitude(0.5, 2_000, 1_024, 1_024, 40);
        Assert.Equal(6.0206, 20.0 * Math.Log10(loud / quiet), 3);
    }

    // The distortion read behind the measurement layer's refusal message. A
    // delta at the peak plus one at the H2 offset is a channel with exactly that
    // much second harmonic, so the reported figure must be the amplitude ratio
    // in dB — this is the number a user is told their loopback is carrying. The
    // floors of this record are exactly zero, so the ceiling must equal the
    // detection: nothing could be hiding.
    [Theory]
    [InlineData(0.25, -12.04)]   // the field culprit's order of magnitude
    [InlineData(0.01, -40.0)]    // a healthy electrical path
    public void MeasureHarmonicEnergy_ReportsTheHarmonicAmplitudeRatio(
        double harmonicAmplitude,
        double expectedDb)
    {
        var impulseResponse = new double[1 << 19];
        impulseResponse[PeakIndex] = 1.0;
        impulseResponse[PeakIndex - EssHarmonicAnalysis.HarmonicOffsetSamples(Sweep(), 2)] =
            harmonicAmplitude;

        EssHarmonicEnergy? measured =
            EssHarmonicAnalysis.MeasureHarmonicEnergy(impulseResponse, Sweep());

        Assert.NotNull(measured);
        Assert.NotNull(measured!.Value.DetectedDb);
        Assert.Equal(expectedDb, measured.Value.DetectedDb!.Value, 1);
        Assert.Equal(measured.Value.DetectedDb.Value, measured.Value.CeilingDb, 6);
        Assert.True(measured.Value.CompleteCoverage);
    }

    // A record with nothing at the harmonic positions and zero floors is the
    // one case that certifies absolutely: nothing detected AND nothing could
    // be hiding, so the ceiling reads minus infinity. This distinction feeds
    // the refusal message's run counting — a certified-clean run counts toward
    // the runs the verdict covers, a run that could not be read does not.
    [Fact]
    public void MeasureHarmonicEnergy_CertifiesACleanRecord()
    {
        var impulseResponse = new double[1 << 19];
        impulseResponse[PeakIndex] = 1.0;

        EssHarmonicEnergy? measured =
            EssHarmonicAnalysis.MeasureHarmonicEnergy(impulseResponse, Sweep());

        Assert.NotNull(measured);
        Assert.Null(measured!.Value.DetectedDb);
        Assert.Equal(double.NegativeInfinity, measured.Value.CeilingDb);
        // Every order fits this geometry, so the certificate is a whole one.
        Assert.True(measured.Value.CompleteCoverage);
    }

    // Partial geometry must say so: here the peak sits close enough to the
    // record's front that H3's own packet still fits but its upper flank does
    // not — so H3 (which demonstrably carries a -20 dB harmonic) and every
    // order past it were never read. The ceiling honestly reads minus
    // infinity over the one order it covers; without the coverage flag that
    // reading would pass for a clean certificate of the whole record.
    [Fact]
    public void MeasureHarmonicEnergy_FlagsACeilingThatCoversOnlySomeOrders()
    {
        EssSweepMetadata sweep = EssSweepMetadata.FromExponentialSweep(
            SampleRate, Octaves, SweepSamples, deconvolutionPeakIndex: 34_000);
        var impulseResponse = new double[1 << 19];
        impulseResponse[34_000] = 1.0;
        // A -20 dB harmonic in H3's packet, physically inside the record.
        impulseResponse[34_000 - EssHarmonicAnalysis.HarmonicOffsetSamples(sweep, 3)] = 0.1;

        EssHarmonicEnergy? measured =
            EssHarmonicAnalysis.MeasureHarmonicEnergy(impulseResponse, sweep);

        Assert.NotNull(measured);
        Assert.Null(measured!.Value.DetectedDb);
        Assert.Equal(double.NegativeInfinity, measured.Value.CeilingDb);
        Assert.False(measured.Value.CompleteCoverage);
    }

    // The failure mode the floors exist for: a record that is pure noise has
    // energy at every packet position, and reporting that as distortion would
    // accuse the wrong thing on exactly the captures (bleed, dead reference)
    // that already have their own diagnosis. The flip side is the ceiling:
    // noise floors this high could conceal anything, so the same record must
    // not come out as a clean certificate either.
    [Fact]
    public void MeasureHarmonicEnergy_NoiseIsNeitherDistortionNorClean()
    {
        var impulseResponse = new double[1 << 19];
        uint state = 12_345u;
        for (int i = 0; i < impulseResponse.Length; i++)
        {
            state = state * 1_664_525u + 1_013_904_223u;
            impulseResponse[i] = state / 4_294_967_296.0 - 0.5;
        }

        EssHarmonicEnergy? measured =
            EssHarmonicAnalysis.MeasureHarmonicEnergy(impulseResponse, Sweep());

        if (measured is { } reading)
        {
            Assert.True(
                reading.DetectedDb is null or < -10.0,
                $"noise reported a detection of {reading.DetectedDb} dB");
            Assert.True(
                reading.CeilingDb > -10.0,
                $"noise certified cleanliness down to {reading.CeilingDb} dB");
        }
    }

    // The reviewer's counter-example to reading "no detection" as "clean": a
    // -19 dB second harmonic — 7 dB above the accusation threshold — hidden
    // under a floor high enough that the packet stays inside the 6 dB
    // detection margin. Nothing is detected, but the ceiling must expose that
    // this record could be hiding exactly such a harmonic; certifying it
    // clean is the bug.
    [Fact]
    public void MeasureHarmonicEnergy_DoesNotCertifyCleanOverAHighFloor()
    {
        var impulseResponse = new double[1 << 19];
        // A uniform background of 0.007 puts ~4.75e-3 of energy into every
        // 97-sample probe (the radius is 48 at this geometry). The detection
        // bar is floor * 10^0.6 ~ 1.89e-2, so a hidden H2 delta of amplitude
        // 0.11 (energy 1.21e-2, -19 dB re the linear packet) lands the packet
        // at ~1.69e-2 — under the bar, invisible to the detector.
        for (int i = 0; i < impulseResponse.Length; i++)
        {
            impulseResponse[i] = 0.007;
        }
        impulseResponse[PeakIndex] = 1.0;
        impulseResponse[PeakIndex - EssHarmonicAnalysis.HarmonicOffsetSamples(Sweep(), 2)] =
            0.11;

        EssHarmonicEnergy? measured =
            EssHarmonicAnalysis.MeasureHarmonicEnergy(impulseResponse, Sweep());

        Assert.NotNull(measured);
        Assert.Null(measured!.Value.DetectedDb);
        // The ceiling — the summed energy of the four isolation windows —
        // computes to ~+3 dB over this background: far too high to certify a
        // record clean of a -26 dB fault, which is the point.
        Assert.True(
            measured.Value.CeilingDb > -16.0,
            $"a floor hiding a -19 dB harmonic certified down to {measured.Value.CeilingDb} dB");
    }

    // The probe reads a ~2 ms core, but a harmonic impulse response has time
    // extent — ringing, band-limiting, an off-centre peak — and the ESS model
    // confines it only to its ISOLATION WINDOW (hundreds of ms here). The
    // review counter-example: a -20 dB harmonic ONE SAMPLE past the probe's
    // edge, still deep inside H2's window. Detection may miss it (the probe is
    // a detector, not a bound), but the ceiling must not: a probe-sized
    // ceiling read -infinity here and certified the record clean.
    [Fact]
    public void MeasureHarmonicEnergy_CeilingCoversTheWholeIsolationWindow()
    {
        EssSweepMetadata sweep = Sweep();
        var impulseResponse = new double[1 << 19];
        impulseResponse[PeakIndex] = 1.0;
        int h2 = PeakIndex - EssHarmonicAnalysis.HarmonicOffsetSamples(sweep, 2);
        // radius is 48 at this geometry: one sample outside the probe.
        impulseResponse[h2 + 49] = 0.1;

        EssHarmonicEnergy? measured =
            EssHarmonicAnalysis.MeasureHarmonicEnergy(impulseResponse, sweep);

        Assert.NotNull(measured);
        Assert.Null(measured!.Value.DetectedDb);
        Assert.True(measured.Value.CompleteCoverage);
        // 10*log10(0.01) = -20 dB: the window-based ceiling reads exactly the
        // energy that is hiding.
        Assert.Equal(-20.0, measured.Value.CeilingDb, 1);
    }

    // The second counter-example from review — the background is NOT uniform:
    // the flank beside H5 carries a floor while the packet's own interior is
    // silent, so the entire (undetected) packet is harmonic. The previous
    // ceiling subtracted the flank floor from what a packet could hide, which
    // under-reserved exactly this energy: it read -26.2 dB here and certified
    // the record clean of the -25.2 dB harmonic it demonstrably carries.
    [Fact]
    public void MeasureHarmonicEnergy_CeilingCoversAHarmonicOverAQuietInterior()
    {
        EssSweepMetadata sweep = Sweep();
        var impulseResponse = new double[1 << 19];
        impulseResponse[PeakIndex] = 1.0;
        // Raise ONLY the 5.5-order boundary probe — H5's upper flank, shared
        // with no analysed order, so every other floor stays zero. Amplitude
        // 0.00287 over the 97-sample probe puts the H5 floor at ~8.0e-4 and
        // the detection bar at ~3.18e-3.
        int flank = PeakIndex - EssHarmonicAnalysis.HarmonicOffsetSamples(sweep, 5.5);
        for (int i = flank - 48; i <= flank + 48; i++)
        {
            impulseResponse[i] = 0.00287;
        }
        // A hidden H5 of energy 3.03e-3 (-25.2 dB re the linear packet): under
        // the bar, undetected — and nothing else lives in its probe, so all of
        // that energy is harmonic.
        impulseResponse[PeakIndex - EssHarmonicAnalysis.HarmonicOffsetSamples(sweep, 5)] =
            0.055;

        EssHarmonicEnergy? measured =
            EssHarmonicAnalysis.MeasureHarmonicEnergy(impulseResponse, sweep);

        Assert.NotNull(measured);
        Assert.Null(measured!.Value.DetectedDb);
        // The ceiling must cover what is actually hiding.
        Assert.True(
            measured.Value.CeilingDb >= -25.3,
            $"a -25.2 dB harmonic hid under a ceiling of {measured.Value.CeilingDb} dB");
    }

    // The residue between packets is not stationary: leakage and packet tails
    // vary along the record, so one anomalously quiet stretch is normal. With a
    // single global floor that quiet stretch becomes the baseline for EVERY
    // order, and an ordinary background at the packet positions reads as gross
    // distortion — a confident, wrong accusation against the loopback. Each
    // order must stand against its own two flanks instead.
    [Fact]
    public void MeasureHarmonicEnergy_IgnoresOneAnomalouslyQuietProbe()
    {
        EssSweepMetadata sweep = Sweep();
        var impulseResponse = new double[1 << 19];
        impulseResponse[PeakIndex] = 1.0;
        // A uniform background everywhere except a narrow silent notch sitting
        // exactly on one boundary probe (the 4.5 order).
        uint state = 4_242u;
        for (int i = 0; i < impulseResponse.Length; i++)
        {
            state = state * 1_664_525u + 1_013_904_223u;
            impulseResponse[i] += (state / 4_294_967_296.0 - 0.5) * 0.001;
        }
        int notch = PeakIndex - EssHarmonicAnalysis.HarmonicOffsetSamples(sweep, 4.5);
        for (int i = notch - 200; i <= notch + 200; i++)
        {
            impulseResponse[i] = 0.0;
        }

        EssHarmonicEnergy? measured =
            EssHarmonicAnalysis.MeasureHarmonicEnergy(impulseResponse, sweep);

        // Nothing at the packet positions rises above its own neighbourhood,
        // so nothing may be DETECTED — that is what the notch would have
        // manufactured under a single global floor.
        Assert.True(
            measured is null || measured.Value.DetectedDb is null or <= -10.0,
            $"a background notch was read as {measured?.DetectedDb} dB of harmonic content");
    }

    // The deconvolution is a linear convolution: index 0 is the first sample of
    // the record and there is nothing before it. A packet geometry that lands
    // there is unmeasurable, and reading the far end of the buffer instead would
    // put unrelated samples into the verdict.
    [Fact]
    public void MeasureHarmonicEnergy_RefusesGeometryThatFallsOffTheRecord()
    {
        // The peak sits close to the start, so even H2 — the nearest packet —
        // would have to be read from before the record began.
        var early = new EssSweepMetadata(
            StartFrequencyHz: 20,
            EndFrequencyHz: 20_000,
            DurationSeconds: SweepSamples / (double)SampleRate,
            SampleRateHz: SampleRate,
            SweepSampleCount: SweepSamples,
            DeconvolutionPeakIndex: 100);
        var impulseResponse = new double[1 << 19];
        impulseResponse[100] = 1.0;
        impulseResponse[^2000] = 0.5;

        Assert.Null(EssHarmonicAnalysis.MeasureHarmonicEnergy(impulseResponse, early));
    }

    // NominalLength is a reserve API with no caller in the app yet; this is its
    // only consumer, and it pins the inclusive convention (Start and End are both
    // inside the window, so a single-sample window has length 1, not 0).
    [Theory]
    [InlineData(100, 199, 100)]
    [InlineData(100, 100, 1)]
    [InlineData(0, 4095, 4096)]
    public void HarmonicWindowDefinition_NominalLengthCountsBothEdges(
        int startSample,
        int endSample,
        int expected)
    {
        var window = new HarmonicWindowDefinition(
            Order: 2,
            PeakSample: (startSample + endSample) / 2,
            StartSample: startSample,
            EndSample: endSample,
            FadeInSamples: 8,
            FadeOutSamples: 8);

        Assert.Equal(expected, window.NominalLength);
    }
}
