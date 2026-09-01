using Xunit;

namespace Resonalyze.App.Tests;

/// <summary>
/// A <see cref="FactAttribute"/> for the tests that need a running REW, skipped
/// with a reason when <see cref="ApiUrlVariable"/> is unset.
/// </summary>
/// <remarks>
/// The audio suite's <c>HardwareFactAttribute</c> gates on the WASAPI endpoint
/// variables, which say nothing about REW; a REW test skipped for want of a sound
/// card, or run for want of one, would both be wrong. The pattern is the same and
/// the reason it exists is the same: a test that opens with an early return is
/// recorded by xUnit as a pass, so the gate has to be the attribute.
/// </remarks>
public sealed class RewFactAttribute : FactAttribute
{
    public const string ApiUrlVariable = "RESONALYZE_REW_API_URL";

    public RewFactAttribute()
    {
        if (ApiUrl() == null)
        {
            Skip = $"Set {ApiUrlVariable} (e.g. http://localhost:4735/) to run the " +
                "tests that talk to a running REW.";
        }
    }

    /// <summary>The address REW is listening on, or null when none was stated.</summary>
    public static string? ApiUrl()
    {
        string? url = Environment.GetEnvironmentVariable(ApiUrlVariable);
        return string.IsNullOrWhiteSpace(url) ? null : url;
    }
}
