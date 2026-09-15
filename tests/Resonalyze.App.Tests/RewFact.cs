using Xunit;

namespace Resonalyze.App.Tests;

/// <summary>Skipped unless the REW API URL is set; an early return would be recorded as a pass, so the gate is the attribute.</summary>
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

    public static string? ApiUrl()
    {
        string? url = Environment.GetEnvironmentVariable(ApiUrlVariable);
        return string.IsNullOrWhiteSpace(url) ? null : url;
    }
}
