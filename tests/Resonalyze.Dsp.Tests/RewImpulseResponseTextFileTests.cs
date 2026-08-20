using System.Globalization;
using System.Text;

namespace Resonalyze.Dsp.Tests;

// REW's text impulse-response export, read back. The header lines below are the ones REW
// 5.40 Beta 132 actually wrote for a loopback-referenced car measurement (96 kHz, one
// sweep) — kept in that shape because the two lines that decide whether the file is
// usable at all say so in prose ("IR is not normalised", "IR window has not been
// applied"), and because the start time is the only place the absolute time base exists.
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

    // A band-limited pulse centred at a fractional position — what a loopback arrival
    // actually is, since REW pins its buffer to the microphone peak and lets the
    // reference fall where it may. Band-limited rather than a bare sample spike, because
    // a spike carries energy at Nyquist, where there is no phase to shift and therefore
    // no meaningful fractional position.
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

    // Sub-sample position of the largest peak, so a fractional shift can be asserted.
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
        // The sweep is longer than the impulse response it produced, so the sweep's own
        // length is worth reading rather than inferring from the buffer.
        Assert.Equal(512 * 1024, file.SweepLengthSamples);
        Assert.Equal(1, file.SweepCount);
        Assert.True(file.IsLoopbackReferenced);
    }

    [Fact]
    public void ToLoopbackReferencedImpulseResponse_PutsTheReferenceArrivalOnSampleZero()
    {
        // t = 0 at sample 20.25: rounding it to 20 would move the arrival by a quarter of
        // a sample — 2.6 us, 0.9 mm — on every channel placed this way.
        double[] samples = ImpulseAt(64, 34.0);
        RewImpulseResponseTextFile file = RewImpulseResponseTextFile.Parse(
            Export(samples, -20.25 / SampleRate));

        double[] referenced = file.ToLoopbackReferencedImpulseResponse();

        // The same pulse, now 13.75 samples after the reference rather than 34 after
        // the buffer's start: 34 − 20.25, with the quarter sample carried not rounded.
        double[] expected = ImpulseAt(64, 13.75);
        for (int i = 0; i < referenced.Length; i++)
        {
            Assert.Equal(expected[i], referenced[i], 9);
        }

        Assert.Equal(13.75, ArgMaxParabolic(referenced), 1);

        // The samples themselves are untouched by reading: they stay REW's buffer, which
        // is what a raw deconvolution looks like on this side too.
        Assert.Equal(samples, file.Samples);
    }

    [Fact]
    public void Parse_RefusesAnExportThatCannotCarryWhatItClaims()
    {
        double[] samples = ImpulseAt(64, 34.0);
        double startTime = -20.25 / SampleRate;

        // Normalised: the peak has been scaled to 1, so the file holds no level relation
        // to any other channel — and nothing in the samples says so.
        Assert.False(RewImpulseResponseTextFile.TryParse(
            Export(samples, startTime, normalised: "* IR is normalised"), out _, out string? problem));
        Assert.Contains("normalised", problem);

        // Windowed: a view of the response, not the response.
        Assert.False(RewImpulseResponseTextFile.TryParse(
            Export(samples, startTime, window: "* IR window has been applied"), out _, out problem));
        Assert.Contains("window", problem);

        // Minimum phase: the arrival time has been removed, which is the one thing this
        // import exists to preserve.
        Assert.False(RewImpulseResponseTextFile.TryParse(
            Export(samples, startTime, minPhase: "* IR is the min phase version"), out _, out problem));
        Assert.Contains("minimum-phase", problem);

        // Headers off: the samples are fine and unplaceable.
        Assert.False(RewImpulseResponseTextFile.TryParse(
            string.Join('\n', samples.Select(s => s.ToString("R", CultureInfo.InvariantCulture))),
            out _,
            out problem));
        Assert.Contains("headers", problem);

        // Truncated: the header says how many samples there should be.
        Assert.False(RewImpulseResponseTextFile.TryParse(
            Export(samples[..40], startTime, declaredLength: 64), out _, out problem));
        Assert.Contains("truncated", problem);

        // A start time that puts t = 0 outside the buffer: the reference arrival is not
        // in these samples, so nothing can be referenced to it.
        Assert.False(RewImpulseResponseTextFile.TryParse(
            Export(samples, 0.5), out _, out problem));
        Assert.Contains("outside the buffer", problem);
    }

    [Fact]
    public void Parse_ReadsButDoesNotPlaceASweepOffTheLoopbackBase()
    {
        // An acoustic reference gives a real impulse response whose position means
        // something only within its own measurement. The file still reads — the caller
        // decides whether to refuse it or import it as a recorded sweep — but it must not
        // be silently treated as the loopback base.
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
        // The file was written on someone else's machine, so neither the invariant
        // culture nor the one this program runs under is the authority on what "20,1"
        // means. Both conventions must read the same, whichever culture is current —
        // and trying invariant-then-current would read "20,1" as 201.
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

            // A lone separator three digits from the end is a thousands group in both
            // conventions; anything else is a decimal point.
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            RewImpulseResponseTextFile grouped = RewImpulseResponseTextFile.Parse(
                Export(samples, startTime, band: "* Response measured over: 1,250 to 12.500 Hz"));
            Assert.Equal(1250.0, grouped.LowFrequencyHz);
            Assert.Equal(12500.0, grouped.HighFrequencyHz);

            // A band it cannot read leaves the band absent rather than failing the file.
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
    public void Parse_RefusesAnArrivalThatPrecedesTheReference()
    {
        // A timing offset applied in REW is invisible in this format: the header of a
        // measurement taken with one is word for word the header of one taken without,
        // and the offset is simply baked into the start time. What it cannot hide is
        // the consequence. Measured on REW 5.40 Beta 132: the same channel exported
        // with a 4 ms offset carries "96000 // Peak index" against a start time of
        // -1.0013229166666666 s, which puts t = 0 at sample 96127 — 127 samples after
        // the peak — and REW's own API reports that measurement's delay as -1.32 ms.
        // Sound does not reach the microphone before it reaches the loopback.
        double[] samples = ImpulseAt(64, 34.0);

        Assert.False(RewImpulseResponseTextFile.TryParse(
            Export(samples, -20.25 / SampleRate, peakIndex: 20),
            out _,
            out string? problem));
        Assert.Contains("precede the reference", problem);

        // The honest limit: an offset smaller than the arrival keeps the peak after
        // t = 0 and is indistinguishable from a shorter path. This one still imports,
        // and the notice says the arrival it implies so a user can catch it.
        RewImpulseResponseTextFile file = RewImpulseResponseTextFile.Parse(
            Export(samples, -20.25 / SampleRate, peakIndex: 40));
        Assert.Equal(19.75, file.ImpliedArrivalSamples, 6);
    }
}
