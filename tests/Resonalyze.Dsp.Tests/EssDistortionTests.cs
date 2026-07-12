using System;
using System.Linq;
using Resonalyze.Dsp;
using Xunit;

namespace Resonalyze.Dsp.Tests;

// Pins the relative-distortion stage: which curves it emits, that a distortion
// packet lands in the RIGHT order's curve (per-order windowing isolation), that
// THD is the energy root over the harmonics, and that a collapsing linear
// denominator yields NaN rather than a runaway percentage.
public sealed class EssDistortionTests
{
    private const int SampleRate = 48_000;
    private const int Octaves = 10;
    private const int SweepSamples = 200_000;
    private const int PeakIndex = 150_000;
    private const int ImpulseLength = 200_000;

    private static EssSweepMetadata Sweep() =>
        EssSweepMetadata.FromExponentialSweep(SampleRate, Octaves, SweepSamples, PeakIndex);

    private static AnalysisCurveKind[] Kinds(SpectrumCurves curves)
    {
        double[] impulse = new double[ImpulseLength];
        impulse[PeakIndex] = 1.0;
        return EssDistortion.ComputeDistortionCurves(
                impulse, Sweep(), new DistortionOptions(), calibration: null, curves)
            .Select(c => c.Kind)
            .ToArray();
    }

    [Fact]
    public void ComputeDistortionCurves_EmitsExactlyTheRequestedHarmonicCurves()
    {
        Assert.Empty(Kinds(SpectrumCurves.Primary)); // primary is not this stage's job
        Assert.Equal(new[] { AnalysisCurveKind.ThirdHarmonic }, Kinds(SpectrumCurves.ThirdHarmonic));
        Assert.Equal(
            new[]
            {
                AnalysisCurveKind.SecondHarmonic,
                AnalysisCurveKind.ThirdHarmonic,
                AnalysisCurveKind.FourthHarmonic,
                AnalysisCurveKind.ThdPlusNoise
            },
            Kinds(SpectrumCurves.Harmonics));
    }

    [Fact]
    public void ComputeDistortion_IsolatesAPacketToItsOwnOrderAndDrivesThd()
    {
        // A broadband delta at the linear peak makes |H1| flat and reliable across
        // the grid; a delta at the H2 packet location makes |H2| flat. HD3/HD4
        // windows stay empty, so those orders never light up and THD equals HD2.
        double[] impulse = new double[ImpulseLength];
        impulse[PeakIndex] = 1.0;
        impulse[PeakIndex - EssHarmonicAnalysis.HarmonicOffsetSamples(Sweep(), 2)] = 0.02;

        EssHarmonicDecomposition decomposition = EssHarmonicAnalysis.AnalyzeEssHarmonics(
            impulse, Sweep(), new HarmonicAnalysisOptions(MaxHarmonic: 4));
        DistortionSpectrum spectrum = EssDistortion.ComputeDistortion(
            decomposition, calibration: null, new DistortionOptions(MaxHarmonic: 4));

        int probe = NearestGridIndex(spectrum.Frequencies, 1_000.0);
        Assert.True(spectrum.Reliable[probe], "Linear denominator should be reliable at 1 kHz.");

        double hd2 = spectrum.HarmonicDistortionRatio[2][probe];
        double hd3 = spectrum.HarmonicDistortionRatio[3][probe];
        double hd4 = spectrum.HarmonicDistortionRatio[4][probe];

        Assert.True(double.IsFinite(hd2) && hd2 > 0.0, $"HD2 should carry the packet, was {hd2}.");
        Assert.True(double.IsNaN(hd3), "HD3 window is empty, should be NaN.");
        Assert.True(double.IsNaN(hd4), "HD4 window is empty, should be NaN.");

        // Only order 2 contributes, so THD = sqrt(H2^2)/H1 = HD2 exactly.
        Assert.Equal(hd2, spectrum.ThdRatio[probe], 9);
    }

    [Fact]
    public void ComputeDistortion_ThdIsTheEnergyRootOverHarmonics()
    {
        // Equal-amplitude deltas in the H2 and H3 packets: THD = sqrt(HD2^2+HD3^2).
        double[] impulse = new double[ImpulseLength];
        impulse[PeakIndex] = 1.0;
        impulse[PeakIndex - EssHarmonicAnalysis.HarmonicOffsetSamples(Sweep(), 2)] = 0.01;
        impulse[PeakIndex - EssHarmonicAnalysis.HarmonicOffsetSamples(Sweep(), 3)] = 0.01;

        EssHarmonicDecomposition decomposition = EssHarmonicAnalysis.AnalyzeEssHarmonics(
            impulse, Sweep(), new HarmonicAnalysisOptions(MaxHarmonic: 4));
        DistortionSpectrum spectrum = EssDistortion.ComputeDistortion(
            decomposition, calibration: null, new DistortionOptions(MaxHarmonic: 4));

        // Probe at 1 kHz, where both H2 (product 2 kHz) and H3 (3 kHz) are in band.
        int probe = NearestGridIndex(spectrum.Frequencies, 1_000.0);
        double hd2 = spectrum.HarmonicDistortionRatio[2][probe];
        double hd3 = spectrum.HarmonicDistortionRatio[3][probe];
        Assert.True(double.IsFinite(hd2) && double.IsFinite(hd3));

        double expected = Math.Sqrt(hd2 * hd2 + hd3 * hd3);
        Assert.Equal(expected, spectrum.ThdRatio[probe], 9);
    }

    [Fact]
    public void ComputeDistortion_MasksAnUnreliableDenominatorAsNaN()
    {
        // A pure tone as the linear packet: |H1| is large only near the tone's
        // excitation frequency and collapses elsewhere, so distant grid points are
        // marked unreliable and their ratios are NaN — never a huge percentage.
        double[] impulse = new double[ImpulseLength];
        int h1Start = PeakIndex - EssHarmonicAnalysis.HarmonicOffsetSamples(Sweep(), Math.Sqrt(2.0));
        int h1End = PeakIndex + (PeakIndex - h1Start);
        int length = h1End - h1Start;
        for (int i = 0; i < length; i++)
        {
            impulse[h1Start + i] = Math.Cos(2.0 * Math.PI * 200 * i / length);
        }

        EssHarmonicDecomposition decomposition = EssHarmonicAnalysis.AnalyzeEssHarmonics(
            impulse, Sweep(), new HarmonicAnalysisOptions(MaxHarmonic: 4));
        DistortionSpectrum spectrum = EssDistortion.ComputeDistortion(
            decomposition, calibration: null, new DistortionOptions(MaxHarmonic: 4));

        Assert.Contains(false, spectrum.Reliable);
        for (int i = 0; i < spectrum.Frequencies.Length; i++)
        {
            if (!spectrum.Reliable[i])
            {
                Assert.True(double.IsNaN(spectrum.ThdRatio[i]));
                Assert.True(double.IsNaN(spectrum.HarmonicDistortionRatio[2][i]));
            }
        }
    }

    [Fact]
    public void ComputeDistortionCurves_SmoothingDoesNotFillMaskedRegions()
    {
        // HD2 is only observable up to Nyquist/2; above that the product passes
        // Nyquist and the ratio is masked. Smoothing must keep those points NaN
        // rather than averaging in nearby finite bins.
        double[] impulse = new double[ImpulseLength];
        impulse[PeakIndex] = 1.0;
        impulse[PeakIndex - EssHarmonicAnalysis.HarmonicOffsetSamples(Sweep(), 2)] = 0.02;

        IReadOnlyList<AnalysisCurve> curves = EssDistortion.ComputeDistortionCurves(
            impulse,
            Sweep(),
            new DistortionOptions(MaxHarmonic: 4, SmoothingOctaves: 0.5),
            calibration: null,
            SpectrumCurves.SecondHarmonic);

        AnalysisCurve hd2 = curves.Single(c => c.Kind == AnalysisCurveKind.SecondHarmonic);
        double nyquistOverTwo = SampleRate / 2.0 / 2.0; // 12 kHz
        Assert.All(
            hd2.Points.Where(p => p.X > nyquistOverTwo + 500),
            p => Assert.True(double.IsNaN(p.Y), $"HD2 must stay masked at {p.X:0} Hz, was {p.Y}."));
        // And it does carry real values below the mask.
        Assert.Contains(hd2.Points, p => p.X < 2_000 && double.IsFinite(p.Y));
    }

    // A log-frequency grid spanning [low, high] with `count` points.
    private static double[] LogGrid(double low, double high, int count)
    {
        double[] f = new double[count];
        double logLow = Math.Log(low);
        double logHigh = Math.Log(high);
        for (int i = 0; i < count; i++)
        {
            f[i] = Math.Exp(logLow + (logHigh - logLow) * i / (count - 1));
        }

        return f;
    }

    [Fact]
    public void SmoothOctaves_ImpulseFwhmEqualsTheRequestedWidth()
    {
        // The width is a fractional-octave FWHM, not a Gaussian sigma. Smoothing a
        // single-bin spike must produce a bump whose full width at half maximum is
        // the requested width (here 1/3 octave), NOT ~2.35x that — the bug that made
        // a 1/12 setting blur across a whole octave.
        const double width = 1.0 / 3.0;
        double[] f = LogGrid(100, 10_000, 2_048);
        double[] db = new double[f.Length];
        int center = f.Length / 2;
        db[center] = 10.0;

        double[] smoothed = EssDistortion.SmoothOctaves(f, db, width);

        double peak = smoothed[center];
        Assert.True(peak > 0.0);
        int left = center;
        while (left > 0 && smoothed[left] > peak / 2.0)
        {
            left--;
        }

        int right = center;
        while (right < f.Length - 1 && smoothed[right] > peak / 2.0)
        {
            right++;
        }

        double fwhmOctaves = Math.Log2(f[right] / f[left]);
        Assert.InRange(fwhmOctaves, width * 0.85, width * 1.15);
    }

    [Fact]
    public void SmoothOctaves_TwelfthOctaveStaysLocalNotAnOctaveWide()
    {
        // A 1/12-octave smooth must leave a spike essentially gone half an octave
        // away — the visible symptom the user reported (harmonics blurred beside
        // HD1) was a spike spreading across a full octave.
        double[] f = LogGrid(100, 10_000, 4_096);
        double[] db = new double[f.Length];
        int center = f.Length / 2;
        db[center] = 12.0;

        double[] smoothed = EssDistortion.SmoothOctaves(f, db, 1.0 / 12.0);

        int halfOctaveAway = center;
        while (halfOctaveAway < f.Length - 1 &&
            Math.Log2(f[halfOctaveAway] / f[center]) < 0.5)
        {
            halfOctaveAway++;
        }

        Assert.True(
            smoothed[halfOctaveAway] < smoothed[center] * 0.01,
            $"a 1/12-octave smooth still had {smoothed[halfOctaveAway]:0.000} dB half an octave from a {smoothed[center]:0.0} dB peak");
    }

    [Fact]
    public void SmoothOctaves_PreservesNaNGapsAndNoOpAtZeroWidth()
    {
        double[] f = LogGrid(100, 10_000, 256);
        double[] db = new double[f.Length];
        for (int i = 0; i < db.Length; i++)
        {
            db[i] = 3.0;
        }

        db[100] = double.NaN;

        double[] noOp = EssDistortion.SmoothOctaves(f, db, 0.0);
        Assert.Equal(db, noOp);

        double[] smoothed = EssDistortion.SmoothOctaves(f, db, 1.0 / 6.0);
        Assert.True(double.IsNaN(smoothed[100]), "a masked bin must stay a gap");
        Assert.False(double.IsNaN(smoothed[101]), "a valid neighbour must stay finite");
    }

    private static int NearestGridIndex(double[] frequencies, double target)
    {
        int best = 0;
        double bestDistance = double.MaxValue;
        for (int i = 0; i < frequencies.Length; i++)
        {
            double distance = Math.Abs(frequencies[i] - target);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }
}
