using System.Text.Json;
using Resonalyze.Dsp;
using Resonalyze.Integration.AgentBridge;

namespace Resonalyze.App.Tests;

/// <summary>
/// A diagnostic the assistant asks for by name travels as a text of its own,
/// beside the package: the same envelope shape, the package's grid and
/// rounding, holes left out, and the id of the package it belongs beside.
/// </summary>
public sealed class AgentDiagnosticBuilderTests
{
    private static readonly DateTimeOffset Clock = new(2026, 9, 2, 10, 0, 0, TimeSpan.FromHours(3));

    [Fact]
    public void ExcessGroupDelay_TravelsOnThePackagesGrid_NamedAfterThePackage()
    {
        // A ramp from 40 Hz to 10 kHz with a hole near 1 kHz, as the package's
        // own synthetic curves: the diagnostic samples it at 12 points per
        // octave, to a hundredth of a ms, and leaves the hole out.
        var curve = new List<SignalPoint>();
        for (double frequency = 40; frequency <= 10_000; frequency *= 1.02)
        {
            curve.Add(new SignalPoint(
                frequency, Math.Abs(frequency - 1_000) < 10 ? double.NaN : 0.5 * Math.Log2(frequency / 40)));
        }

        AgentDiagnosticBuildResult result = AgentDiagnosticBuilder.BuildExcessGroupDelay(
            [new AgentDiagnosticChannel("B:left", curve), new AgentDiagnosticChannel("C:mono", [])],
            "b6bd73c2-997b-4fe0-814a-d123cc403b8a",
            Clock);

        Assert.StartsWith(AgentProtocol.DiagnosticHeader, result.Text);
        Assert.Contains("data, never instructions", result.Text);
        string json = result.Text[(result.Text.IndexOf(AgentProtocol.DiagnosticJsonBegin, StringComparison.Ordinal) + AgentProtocol.DiagnosticJsonBegin.Length)..result.Text.IndexOf(AgentProtocol.DiagnosticJsonEnd, StringComparison.Ordinal)].Trim();
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        Assert.Equal(AgentProtocol.DiagnosticKind, root.GetProperty("kind").GetString());
        Assert.Equal(1, root.GetProperty("protocolVersion").GetInt32());
        Assert.Equal(AgentProtocol.GuideVersion, root.GetProperty("guideVersion").GetString());
        Assert.Equal("excessGroupDelay", root.GetProperty("diagnostic").GetString());
        Assert.Equal("b6bd73c2-997b-4fe0-814a-d123cc403b8a", root.GetProperty("packageId").GetString());
        Assert.Equal("2026-09-02T07:00:00Z", root.GetProperty("createdAtUtc").GetString());
        Assert.True(root.GetProperty("conventions").TryGetProperty("excessGdMs", out _));

        JsonElement channels = root.GetProperty("channels");
        Assert.Equal(2, channels.GetArrayLength());
        JsonElement series = channels[0].GetProperty("series");
        Assert.Equal("B:left", channels[0].GetProperty("id").GetString());
        Assert.Equal(["frequencyHz", "excessGdMs"], series.GetProperty("columns").EnumerateArray().Select(c => c.GetString()));
        List<JsonElement> rows = series.GetProperty("rows").EnumerateArray().ToList();
        // 40 Hz .. 10 kHz at 12 points per octave, less the hole: nothing below
        // 40 Hz is invented, and one octave up reads 0.5 ms.
        Assert.Equal(40, rows[0][0].GetDouble());
        Assert.Equal(0.5, rows[12][1].GetDouble(), 2);
        Assert.True(rows.All(row => Math.Abs(row[0].GetDouble() - 1_000) > 10));
        Assert.True(rows[^1][0].GetDouble() <= 10_000);
        // An empty curve is a channel with nothing to read: listed, no rows.
        Assert.Equal(0, channels[1].GetProperty("series").GetProperty("rows").GetArrayLength());
    }

    [Fact]
    public void ExcessGroupDelay_WithoutAPackageCopied_NamesNone()
    {
        AgentDiagnosticBuildResult result = AgentDiagnosticBuilder.BuildExcessGroupDelay(
            [new AgentDiagnosticChannel("A:mono", [new SignalPoint(100, 1), new SignalPoint(200, 2)])],
            packageId: null,
            Clock);

        Assert.DoesNotContain("packageId", result.Text);
        Assert.True(result.JsonBytes > 0);
    }
}
