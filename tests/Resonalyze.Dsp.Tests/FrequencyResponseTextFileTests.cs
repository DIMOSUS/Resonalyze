namespace Resonalyze.Dsp.Tests;

public sealed class FrequencyResponseTextFileTests
{
    private const string RewHeader =
        "* Measurement data measured by REW V5.40 beta 135\n" +
        "* Source: l mid\n" +
        "* Format: Imported Impulse Response, 96000.0 Hz sampling\n" +
        "* Dated: 2026 Sep 26 19:02:55\n" +
        "* REW Settings:\n" +
        "*  C-weighting compensation: Off\n" +
        "*  Target level: 75.0 dB\n" +
        "* Measurement: l mid\n" +
        "* Smoothing: None\n" +
        "* Frequency Step: 0.3662109 Hz\n" +
        "* Start Frequency: 0.36621094 Hz\n" +
        "*\n";

    [Fact]
    public void RewExportsWithAndWithoutPhase_ReadTheSame()
    {
        string withPhase = RewHeader +
            "* Freq(Hz) SPL(dB) Phase(degrees)\n" +
            "0.366211 11.181 6.1078\n" +
            "0.732422 11.626 9.9875\r\n" +
            "47999.629883 -65.155 -135.9570\n";
        string withoutPhase = RewHeader +
            "* Freq(Hz) SPL(dB)\n" +
            "0.366211 11.181\n" +
            "0.732422 11.626\r\n" +
            "47999.629883 -65.155\n";

        Assert.True(FrequencyResponseTextFile.TryParse(withPhase, out FrequencyResponseTextFile? phase, out _));
        Assert.True(FrequencyResponseTextFile.TryParse(withoutPhase, out FrequencyResponseTextFile? plain, out _));

        foreach (FrequencyResponseTextFile file in new[] { phase!, plain! })
        {
            Assert.Equal([0.366211, 0.732422, 47999.629883], file.FrequenciesHz);
            Assert.Equal([11.181, 11.626, -65.155], file.LevelsDb);
            Assert.True(file.WrittenByRew);
            Assert.Equal("l mid", file.MeasurementName);
            Assert.Equal(96_000, file.SampleRateHz);
            Assert.False(file.StatesSmoothing);
        }
    }

    [Fact]
    public void ASmoothedExport_SaysSo()
    {
        string text = RewHeader.Replace("Smoothing: None", "Smoothing: 1/6 octave") +
            "* Freq(Hz) SPL(dB)\n20 70\n40 71\n";

        Assert.True(FrequencyResponseTextFile.TryParse(text, out FrequencyResponseTextFile? file, out _));
        Assert.True(file!.StatesSmoothing);
        Assert.Equal("1/6 octave", file.Smoothing);
    }

    [Theory]
    [InlineData("20.5 75.3")]
    [InlineData("20.5\t75.3")]
    [InlineData("20,5;75,3")]
    [InlineData("20,5 75,3")]
    [InlineData("20.5,75.3")]
    [InlineData("20.5, 75.3")]
    public void AHeaderlessTable_ReadsPointsAndCommas(string row)
    {
        Assert.True(FrequencyResponseTextFile.TryParse(
            "Freq(Hz),SPL(dB)\n" + row + "\n1000 80\n", out FrequencyResponseTextFile? file, out _));

        Assert.False(file!.WrittenByRew);
        Assert.Equal([20.5, 1000], file.FrequenciesHz);
        Assert.Equal([75.3, 80], file.LevelsDb);
    }

    [Theory]
    [InlineData("Frequency Magnitude\n20 70\n40 71\n")]
    [InlineData("* Freq(Hz) dBFS\n20 -40\n40 -41\n")]
    [InlineData("Frequency (Hz)\tSPL (dB)\n20 70\n40 71\n")]
    [InlineData("Freq (Hz), Level (dB)\n20, 70\n40, 71\n")]
    [InlineData("# Frequency response of the left tweeter\n20 70\n40 71\n")]
    [InlineData("Frequency Level dB\n20 70\n40 71\n")]
    [InlineData("Frequency Magnitude Phase\n20 70 1\n40 71 2\n")]
    public void ALevelColumnWithoutAForeignUnit_IsAccepted(string text)
    {
        Assert.True(FrequencyResponseTextFile.TryParse(text, out FrequencyResponseTextFile? file, out string? problem), problem);
        Assert.Equal(2, file!.FrequenciesHz.Length);
    }

    [Theory]
    [InlineData("* Freq(Hz) Impedance(ohms) Phase(degrees)\n20 4.1 3\n40 4.3 5\n")]
    [InlineData("Frequency Level V\n20 0.1\n40 0.2\n")]
    [InlineData("Frequency Amplitude Pa\n20 0.1\n40 0.2\n")]
    [InlineData("Frequency Level W\n20 0.1\n40 0.2\n")]
    [InlineData("* Impulse Response data saved by REW V5.40\n* Data start\n0.1\n0.2\n")]
    [InlineData("* Freq(Hz) SPL(dB)\n40 70\n20 71\n")]
    [InlineData("* Freq(Hz) SPL(dB)\n40 70\n")]
    [InlineData("* Freq(Hz) SPL(dB)\n20 70\n40 71\nnot a number\n")]
    public void WhatIsNotALevelTable_IsRefusedWithAReason(string text)
    {
        Assert.False(FrequencyResponseTextFile.TryParse(text, out FrequencyResponseTextFile? file, out string? problem));
        Assert.Null(file);
        Assert.False(string.IsNullOrWhiteSpace(problem));
    }

    [Fact]
    public void CoarsestPointsPerOctave_CountsTheSparsestOctave()
    {
        // Linear 10 Hz spacing: 20-40 Hz holds 2 points, 10-20 kHz holds 1000.
        string rows = string.Join("\n", Enumerable.Range(1, 2_400).Select(i => $"{i * 10} 70"));
        Assert.True(FrequencyResponseTextFile.TryParse(rows, out FrequencyResponseTextFile? file, out _));

        Assert.Equal(2, file!.CoarsestPointsPerOctave(20, 20_000));
    }
}
