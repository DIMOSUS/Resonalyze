using System.Globalization;
using System.Text;

namespace Resonalyze.Dsp.Tests;

// Headers as REW 5.40 Beta 132 wrote them: usability is stated in prose, and the start time is the only absolute time base.
public sealed class RewImpulseResponseTextFileTests
{
    private const int SampleRate = 96000;

    private static string Export(
        IReadOnlyList<double> samples,
        double startTimeSeconds,
        string normalised = "* IR is not normalised",
        string window = "* IR window has not been applied",
        string minPhase = "* IR is not the min phase version",
        string excitation =
            "* Excitation: 512k Log Swept Sine, 1 sweep at -10.0 dBFS using a loopback as a timing reference",
        int? declaredLength = null,
        string band = "* Response measured over: 20.1 to 19,999.9 Hz",
        int? peakIndex = null)
    {
        var text = new StringBuilder();
        text.AppendLine("* Impulse Response data saved by REW V5.40 Beta 132");
        text.AppendLine(normalised);
        text.AppendLine(window);
        text.AppendLine(minPhase);
        text.AppendLine("* Source: Scarlett 2i2 4th Gen, Scarlett 2i2 4th Gen , 1, volume: no control");
        text.AppendLine("* Dated: Jun 15, 2026, 2:47:02 PM");
        text.AppendLine("* Measurement: w-L_01 (sw)");
        text.AppendLine(excitation);
        text.AppendLine(band);
        text.AppendLine("0.0034054601565003395 // Peak value before normalisation");
        text.AppendLine(Invariant(peakIndex ?? (samples.Count > 0 ? Peak(samples) : 0)) + " // Peak index");
        text.AppendLine(Invariant(declaredLength ?? samples.Count) + " // Response length");
        text.AppendLine("1.0416666666666666E-5 // Sample interval (seconds)");
        text.AppendLine(startTimeSeconds.ToString("R", CultureInfo.InvariantCulture) +
            " // Start time (seconds)");
        text.AppendLine("120.0 // Data offset (dB)");
        text.AppendLine("* Data start");
        foreach (double sample in samples)
        {
            text.AppendLine(sample.ToString("R", CultureInfo.InvariantCulture));
        }

        return text.ToString();
    }

    private static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static int Peak(IReadOnlyList<double> samples)
    {
        int peak = 0;
        for (int i = 1; i < samples.Count; i++)
        {
            if (Math.Abs(samples[i]) > Math.Abs(samples[peak]))
            {
                peak = i;
            }
        }

        return peak;
    }

    // Band-limited, not a spike: a spike's Nyquist energy has no meaningful fractional position.
    private static double[] ImpulseAt(int length, double position)
    {
        int highest = length / 4;
        double peak = (2 * highest) + 1;
        return
        [
            .. Enumerable.Range(0, length).Select(i =>
                (1.0 + Enumerable.Range(1, highest).Sum(k =>
                    2.0 * Math.Cos(2.0 * Math.PI * k * (i - position) / length))) / peak)
        ];
    }

    private static double ArgMaxParabolic(double[] x)
    {
        int i = 0;
        for (int k = 1; k < x.Length; k++)
        {
            if (Math.Abs(x[k]) > Math.Abs(x[i]))
            {
                i = k;
            }
        }

        double a = Math.Abs(x[(i - 1 + x.Length) % x.Length]);
        double b = Math.Abs(x[i]);
        double c = Math.Abs(x[(i + 1) % x.Length]);
        double denominator = a - (2 * b) + c;
        return i + (denominator == 0 ? 0 : 0.5 * (a - c) / denominator);
    }

    [Fact]
    public void Parse_ReadsTheHeaderRewWrites()
    {
        RewImpulseResponseTextFile file = RewImpulseResponseTextFile.Parse(
            Export(ImpulseAt(64, 34.0), -20.25 / SampleRate));

        Assert.Equal(SampleRate, file.SampleRate);
        Assert.Equal(64, file.Samples.Length);
        Assert.Equal(20.25, file.TimeZeroIndex, 9);
        Assert.Equal(120.0, file.DataOffsetDb);
        Assert.Equal(0.0034054601565003395, file.PeakValueBeforeNormalisation);
        Assert.Equal("w-L_01 (sw)", file.MeasurementName);
        Assert.Equal(20.1, file.LowFrequencyHz);
        Assert.Equal(19999.9, file.HighFrequencyHz);
        Assert.Equal(512 * 1024, file.SweepLengthSamples);
        Assert.Equal(1, file.SweepCount);
        Assert.True(file.IsLoopbackReferenced);
    }

    [Fact]
    public void ToLoopbackReferencedImpulseResponse_PutsTheReferenceArrivalOnSampleZero()
    {
        // t = 0 at sample 20.25: rounding would move every arrival by 2.6 us.
        double[] samples = ImpulseAt(64, 34.0);
        RewImpulseResponseTextFile file = RewImpulseResponseTextFile.Parse(
            Export(samples, -20.25 / SampleRate));

        double[] referenced = file.ToLoopbackReferencedImpulseResponse();

        double[] expected = ImpulseAt(64, 13.75);
        for (int i = 0; i < referenced.Length; i++)
        {
            Assert.Equal(expected[i], referenced[i], 9);
        }

        Assert.Equal(13.75, ArgMaxParabolic(referenced), 1);

        Assert.Equal(samples, file.Samples);
    }

    [Fact]
    public void Parse_RefusesAnExportThatCannotCarryWhatItClaims()
    {
        double[] samples = ImpulseAt(64, 34.0);
        double startTime = -20.25 / SampleRate;

        // Normalised: no level relation to other channels, and nothing in the samples says so.
        Assert.False(RewImpulseResponseTextFile.TryParse(
            Export(samples, startTime, normalised: "* IR is normalised"), out _, out string? problem));
        Assert.Contains("normalised", problem);

        Assert.False(RewImpulseResponseTextFile.TryParse(
            Export(samples, startTime, window: "* IR window has been applied"), out _, out problem));
        Assert.Contains("window", problem);

        // Minimum phase removes the arrival time, the one thing this import preserves.
        Assert.False(RewImpulseResponseTextFile.TryParse(
            Export(samples, startTime, minPhase: "* IR is the min phase version"), out _, out problem));
        Assert.Contains("minimum-phase", problem);

        Assert.False(RewImpulseResponseTextFile.TryParse(
            string.Join('\n', samples.Select(s => s.ToString("R", CultureInfo.InvariantCulture))),
            out _,
            out problem));
        Assert.Contains("headers", problem);

        Assert.False(RewImpulseResponseTextFile.TryParse(
            Export(samples[..40], startTime, declaredLength: 64), out _, out problem));
        Assert.Contains("truncated", problem);

        Assert.False(RewImpulseResponseTextFile.TryParse(
            Export(samples, 0.5), out _, out problem));
        Assert.Contains("outside the buffer", problem);
    }

    [Fact]
    public void Parse_ReadsButDoesNotPlaceASweepOffTheLoopbackBase()
    {
        RewImpulseResponseTextFile file = RewImpulseResponseTextFile.Parse(Export(
            ImpulseAt(64, 34.0),
            -20.25 / SampleRate,
            excitation: "* Excitation: 256k Log Swept Sine, 4 sweeps at -12.0 dBFS using an " +
                "acoustic timing reference"));

        Assert.False(file.IsLoopbackReferenced);
        Assert.Equal(4, file.SweepCount);
        Assert.Equal(256 * 1024, file.SweepLengthSamples);
    }
    [Fact]
    public void Parse_ReadsTheBandInEitherGroupingConvention()
    {
        // Written on another machine: '20,1' must read the same under any culture (invariant-then-current reads 201).
        double[] samples = ImpulseAt(64, 34.0);
        double startTime = -20.25 / SampleRate;
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            foreach (string culture in new[] { "en-US", "de-DE" })
            {
                CultureInfo.CurrentCulture = new CultureInfo(culture);

                RewImpulseResponseTextFile dotted = RewImpulseResponseTextFile.Parse(
                    Export(samples, startTime, band: "* Response measured over: 20.1 to 19,999.9 Hz"));
                RewImpulseResponseTextFile commaed = RewImpulseResponseTextFile.Parse(
                    Export(samples, startTime, band: "* Response measured over: 20,1 to 19.999,9 Hz"));

                Assert.Equal(20.1, dotted.LowFrequencyHz);
                Assert.Equal(19999.9, dotted.HighFrequencyHz);
                Assert.Equal(20.1, commaed.LowFrequencyHz);
                Assert.Equal(19999.9, commaed.HighFrequencyHz);
            }

            // A lone separator three digits from the end is a thousands group in both conventions.
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            RewImpulseResponseTextFile grouped = RewImpulseResponseTextFile.Parse(
                Export(samples, startTime, band: "* Response measured over: 1,250 to 12.500 Hz"));
            Assert.Equal(1250.0, grouped.LowFrequencyHz);
            Assert.Equal(12500.0, grouped.HighFrequencyHz);

            RewImpulseResponseTextFile unreadable = RewImpulseResponseTextFile.Parse(
                Export(samples, startTime, band: "* Response measured over: unknown"));
            Assert.Null(unreadable.LowFrequencyHz);
            Assert.Null(unreadable.HighFrequencyHz);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
    [Fact]
    public void Parse_KeepsAnArrivalThatPrecedesTheReference()
    {
        // A REW timing offset is baked into the start time only (4 ms offset: t = 0 at sample 96127, 127 after the peak).
        // Not refused at parse time: the importer's offset question exists to rescue this file.
        double[] samples = ImpulseAt(64, 34.0);

        RewImpulseResponseTextFile offsetFile = RewImpulseResponseTextFile.Parse(
            Export(samples, -20.25 / SampleRate, peakIndex: 20));
        Assert.Equal(-0.25, offsetFile.ImpliedArrivalSamples, 6);

        // An offset smaller than the arrival is indistinguishable from a shorter path.
        RewImpulseResponseTextFile file = RewImpulseResponseTextFile.Parse(
            Export(samples, -20.25 / SampleRate, peakIndex: 40));
        Assert.Equal(19.75, file.ImpliedArrivalSamples, 6);
    }

    [Fact]
    public void StatedOffsetMovesTheReferenceAndTheArrival()
    {
        // A positive REW offset delays the measurement: undoing it moves t = 0 earlier (verified on six captures, 384 samples at 96 kHz).
        double[] samples = ImpulseAt(64, 34.0);
        RewImpulseResponseTextFile file = RewImpulseResponseTextFile.Parse(
            Export(samples, -20.25 / SampleRate, peakIndex: 20));

        double offsetSeconds = 8.0 / SampleRate;
        Assert.Equal(12.25, file.ReferenceIndexWithOffset(offsetSeconds), 6);
        Assert.Equal(7.75, file.ArrivalSamplesWithOffset(offsetSeconds), 6);

        Assert.Equal(file.TimeZeroIndex, file.ReferenceIndexWithOffset(0), 12);
        Assert.Equal(
            file.ToLoopbackReferencedImpulseResponse(),
            file.ToLoopbackReferencedImpulseResponse(0));

        double[] uncorrected = file.ToLoopbackReferencedImpulseResponse();
        double[] corrected = file.ToLoopbackReferencedImpulseResponse(offsetSeconds);
        Assert.Equal(Peak(uncorrected) + 8, Peak(corrected));
    }
}
