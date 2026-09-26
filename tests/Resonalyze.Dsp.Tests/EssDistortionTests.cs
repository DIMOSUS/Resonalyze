using System;
using System.Linq;
using Resonalyze.Dsp;
using Xunit;

namespace Resonalyze.Dsp.Tests;

// Per-order windowing isolation, THD as energy root, NaN for a collapsing linear denominator.
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
    public void AKeptDecomposition_DrawsTheCurvesOfAFreshAnalysis()
    {
        // The shortest sweep that still separates HD2..HD4 and leaves room for the noise windows.
        EssSweepMetadata sweep = EssSweepMetadata.FromExponentialSweep(SampleRate, 8, 60_000, 50_000);
        double[] impulse = new double[60_000];
        impulse[50_000] = 1.0;
        impulse[50_000 - EssHarmonicAnalysis.HarmonicOffsetSamples(sweep, 2)] = 0.02;
        impulse[50_000 - EssHarmonicAnalysis.HarmonicOffsetSamples(sweep, 3)] = 0.005;
        var random = new Random(7);
        for (int i = 0; i < impulse.Length; i++)
        {
            impulse[i] += (random.NextDouble() - 0.5) * 1e-5;
        }

        var options = new DistortionOptions(SmoothingOctaves: 1.0 / 6.0, IncludeNoise: true);
        CalibrationFile calibration = CalibrationFile.Parse("20 -2\n1000 0.5\n8000 3\n20000 -1\n");
        EssHarmonicDecomposition decomposition = EssDistortion.Decompose(impulse, sweep, options);
        NoiseEstimate noise = EssNoise.EstimateNoise(impulse, decomposition, options);

        EssDistortion.DistortionCurveResult fresh = EssDistortion.ComputeDistortionCurvesResult(
            impulse, sweep, options, calibration, SpectrumCurves.Distortion);
        EssDistortion.DistortionCurveResult kept = EssDistortion.ComputeDistortionCurvesResult(
            decomposition, noise, options, calibration, SpectrumCurves.Distortion);

        Assert.NotEmpty(fresh.Curves);
        Assert.Equal(fresh.Warnings, kept.Warnings);
        Assert.Equal(fresh.PacketValidity, kept.PacketValidity);
        Assert.Equal(fresh.Curves.Select(curve => curve.Kind), kept.Curves.Select(curve => curve.Kind));
        foreach ((AnalysisCurve expected, AnalysisCurve actual) in fresh.Curves.Zip(kept.Curves))
        {
            Assert.Equal(expected.Points, actual.Points);
        }
    }

    [Fact]
    public void ComputeDistortion_IsolatesAPacketToItsOwnOrderAndDrivesThd()
    {
        // Deltas at the linear peak and H2 location: flat |H1| and |H2|, empty HD3/HD4, so THD equals HD2.
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

        Assert.Equal(hd2, spectrum.ThdRatio[probe], 9);
    }

    [Fact]
    public void ComputeDistortion_HarmonicCurvesStopWhereTheirProductEntersTheSweepFadeOut()
    {
        double[] impulse = new double[ImpulseLength];
        impulse[PeakIndex] = 1.0;
        impulse[PeakIndex - EssHarmonicAnalysis.HarmonicOffsetSamples(Sweep(), 2)] = 0.02;
        EssSweepMetadata sweep = Sweep() with { FullAmplitudeEndFrequencyHz = 20_000 };

        DistortionSpectrum spectrum = EssDistortion.ComputeDistortion(
            EssHarmonicAnalysis.AnalyzeEssHarmonics(impulse, sweep, new HarmonicAnalysisOptions(MaxHarmonic: 4)),
            calibration: null,
            new DistortionOptions(MaxHarmonic: 4));

        double[] hd2 = spectrum.HarmonicDistortionRatio[2];
        Assert.True(double.IsFinite(hd2[NearestGridIndex(spectrum.Frequencies, 9_000.0)]));
        // 11 kHz puts its product at 22 kHz, where the gate has already cut it down: no reading, not a low one.
        Assert.True(double.IsNaN(hd2[NearestGridIndex(spectrum.Frequencies, 11_000.0)]));
    }

    [Fact]
    public void ComputeDistortion_ThdIsTheEnergyRootOverHarmonics()
    {
        double[] impulse = new double[ImpulseLength];
        impulse[PeakIndex] = 1.0;
        impulse[PeakIndex - EssHarmonicAnalysis.HarmonicOffsetSamples(Sweep(), 2)] = 0.01;
        impulse[PeakIndex - EssHarmonicAnalysis.HarmonicOffsetSamples(Sweep(), 3)] = 0.01;

        EssHarmonicDecomposition decomposition = EssHarmonicAnalysis.AnalyzeEssHarmonics(
            impulse, Sweep(), new HarmonicAnalysisOptions(MaxHarmonic: 4));
        DistortionSpectrum spectrum = EssDistortion.ComputeDistortion(
            decomposition, calibration: null, new DistortionOptions(MaxHarmonic: 4));

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
        // A pure-tone linear packet collapses |H1| away from the tone: ratios there are NaN, never huge.
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
        // HD2 is observable only to Nyquist/2; smoothing must keep masked points NaN.
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
        Assert.Contains(hd2.Points, p => p.X < 2_000 && double.IsFinite(p.Y));
    }

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
        // The width is a fractional-octave FWHM, not a Gaussian sigma (~2.35x too wide before).
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
