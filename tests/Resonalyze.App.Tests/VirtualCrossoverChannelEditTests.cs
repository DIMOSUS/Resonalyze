using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class VirtualCrossoverChannelEditTests
{
    // Every shown value differs from its stored one, so a field that also wrote another would show up.
    private static readonly VirtualCrossoverChannelShown Shown = new(
        GainDb: -3,
        DelayMs: 1.23,
        Inverted: true,
        Mono: true,
        Zone: VirtualCrossoverZone.Rear,
        Muted: true,
        Bypass: true,
        ShowRaw: true,
        ShowProcessed: false,
        CrossoverKind: CrossoverKind.BandPass,
        HighPass: new CrossoverEdge(CrossoverFilterFamily.Butterworth, 84, 12, 0.5),
        LowPass: new CrossoverEdge(CrossoverFilterFamily.Bessel, 2_613, 18, 0.7),
        PhaseRotationDegrees: 90);

    private static readonly Dictionary<VirtualCrossoverChannelField, string[]> Writes = new()
    {
        [VirtualCrossoverChannelField.Gain] = ["GainDb"],
        [VirtualCrossoverChannelField.Delay] = ["DelayMs"],
        [VirtualCrossoverChannelField.Polarity] = ["Inverted"],
        [VirtualCrossoverChannelField.Mono] = ["Mono"],
        [VirtualCrossoverChannelField.Zone] = ["Zone"],
        [VirtualCrossoverChannelField.Mute] = ["Muted"],
        [VirtualCrossoverChannelField.Bypass] = ["Bypass"],
        [VirtualCrossoverChannelField.ShowRaw] = ["ShowRaw"],
        [VirtualCrossoverChannelField.ShowProcessed] = ["ShowProcessed"],
        [VirtualCrossoverChannelField.CrossoverKind] = ["CrossoverKind"],
        [VirtualCrossoverChannelField.HighPassCorner] = ["HighPassHz"],
        [VirtualCrossoverChannelField.HighPassFilter] = ["HighPassFamily", "HighPassSlope"],
        [VirtualCrossoverChannelField.HighPassRipple] = ["HighPassRippleDb"],
        [VirtualCrossoverChannelField.LowPassCorner] = ["LowPassHz"],
        [VirtualCrossoverChannelField.LowPassFilter] = ["LowPassFamily", "LowPassSlope"],
        [VirtualCrossoverChannelField.LowPassRipple] = ["LowPassRippleDb"],
        [VirtualCrossoverChannelField.PhaseRotation] = ["PhaseRotationDegrees"]
    };

    // By name: the field type is internal and a theory's parameters are public.
    public static TheoryData<string> Fields
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (string name in Enum.GetNames<VirtualCrossoverChannelField>())
            {
                data.Add(name);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Fields))]
    public void AnEdit_WritesTheShownValueOfItsOwnField_AndLeavesEveryOtherStoredValue(string name)
    {
        VirtualCrossoverChannelField field = Enum.Parse<VirtualCrossoverChannelField>(name);
        (VirtualCrossoverChannelPairSettings pair, VirtualCrossoverChannelSettings side) = Stored();
        Dictionary<string, object> before = Values(pair, side);

        VirtualCrossoverChannelEdit.Write(field, Shown, pair, side);

        Dictionary<string, object> after = Values(pair, side);
        Dictionary<string, object> shown = Values(Shown);
        Assert.Equal(Writes[field], before.Keys.Where(key => !Equals(before[key], after[key])));
        Assert.All(Writes[field], key => Assert.Equal(shown[key], after[key]));
    }

    [Theory]
    [InlineData(0.25, 0.2, 0.25)]
    [InlineData(0.0, 0.1, 0.1)]
    [InlineData(5.0, 3.0, 3.0)]
    [InlineData(double.NaN, 0.1, 0.1)]
    public void TurningAnEdgeChebyshev_KeepsAStoredRippleItCanBuild_AndTakesTheShownOneOtherwise(
        double storedRippleDb, double shownRippleDb, double expectedRippleDb)
    {
        (VirtualCrossoverChannelPairSettings pair, VirtualCrossoverChannelSettings side) = Stored();
        side.LowPassEdge = side.LowPassEdge with { RippleDb = storedRippleDb };
        VirtualCrossoverChannelShown shown = Shown with
        {
            LowPass = new CrossoverEdge(CrossoverFilterFamily.Chebyshev, 2_613, 24, shownRippleDb)
        };

        VirtualCrossoverChannelEdit.Write(VirtualCrossoverChannelField.LowPassFilter, shown, pair, side);

        Assert.Equal(
            new CrossoverEdge(CrossoverFilterFamily.Chebyshev, 2_612.5, 24, expectedRippleDb),
            side.LowPassEdge);
        side.Validate();
    }

    private static (VirtualCrossoverChannelPairSettings Pair, VirtualCrossoverChannelSettings Side) Stored()
    {
        var side = new VirtualCrossoverChannelSettings
        {
            GainDb = -2.97,
            DelayMs = 1.234,
            CrossoverKind = CrossoverKind.LowPass,
            HighPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 83.7, 24, 1.0),
            LowPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 2_612.5, 48, 1.0),
            PhaseRotationDegrees = 12.3456
        };
        var pair = new VirtualCrossoverChannelPairSettings { Left = side, ShowProcessedCurve = true };
        return (pair, side);
    }

    internal static Dictionary<string, object> Values(VirtualCrossoverChannelPairSettings pair, VirtualCrossoverChannelSettings side) =>
        Values(new VirtualCrossoverChannelShown(
            side.GainDb, side.DelayMs, side.InvertPolarity, pair.Mono, pair.Zone, !pair.Enabled, pair.Bypass,
            pair.ShowRawCurve, pair.ShowProcessedCurve, side.CrossoverKind, side.HighPassEdge, side.LowPassEdge,
            side.PhaseRotationDegrees));

    private static Dictionary<string, object> Values(VirtualCrossoverChannelShown shown) => new()
    {
        ["GainDb"] = shown.GainDb,
        ["DelayMs"] = shown.DelayMs,
        ["Inverted"] = shown.Inverted,
        ["Mono"] = shown.Mono,
        ["Zone"] = shown.Zone,
        ["Muted"] = shown.Muted,
        ["Bypass"] = shown.Bypass,
        ["ShowRaw"] = shown.ShowRaw,
        ["ShowProcessed"] = shown.ShowProcessed,
        ["CrossoverKind"] = shown.CrossoverKind,
        ["HighPassHz"] = shown.HighPass.FrequencyHz,
        ["HighPassFamily"] = shown.HighPass.Family,
        ["HighPassSlope"] = shown.HighPass.SlopeDbPerOctave,
        ["HighPassRippleDb"] = shown.HighPass.RippleDb,
        ["LowPassHz"] = shown.LowPass.FrequencyHz,
        ["LowPassFamily"] = shown.LowPass.Family,
        ["LowPassSlope"] = shown.LowPass.SlopeDbPerOctave,
        ["LowPassRippleDb"] = shown.LowPass.RippleDb,
        ["PhaseRotationDegrees"] = shown.PhaseRotationDegrees
    };
}
