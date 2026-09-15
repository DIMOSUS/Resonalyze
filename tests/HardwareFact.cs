using Xunit;

namespace Resonalyze.Testing;

/// <summary>A <see cref="FactAttribute"/> SKIPPED when the WASAPI endpoint variables are unset: an early return counted as a pass.
/// <see cref="FactAttribute.Skip"/> is evaluated at discovery. Linked into both test projects (see each .csproj).</summary>
public sealed class HardwareFactAttribute : FactAttribute
{
    public const string CaptureEndpointVariable = "RESONALYZE_WASAPI_CAPTURE_ENDPOINT_ID";
    public const string RenderEndpointVariable = "RESONALYZE_WASAPI_RENDER_ENDPOINT_ID";

    public HardwareFactAttribute()
    {
        if (!HasEndpoints())
        {
            Skip = SkipReason;
        }
    }

    internal static string SkipReason =>
        $"Set {CaptureEndpointVariable} and {RenderEndpointVariable} " +
        "to run the hardware smoke tests.";

    public static bool HasEndpoints() =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(CaptureEndpointVariable)) &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(RenderEndpointVariable));

    /// <summary>Only called from a <see cref="HardwareFactAttribute"/> test; the throw guards a plain <c>[Fact]</c>.</summary>
    public static (string Capture, string Render) Endpoints()
    {
        string? capture = Environment.GetEnvironmentVariable(CaptureEndpointVariable);
        string? render = Environment.GetEnvironmentVariable(RenderEndpointVariable);
        if (string.IsNullOrWhiteSpace(capture) || string.IsNullOrWhiteSpace(render))
        {
            throw new InvalidOperationException(
                $"{CaptureEndpointVariable}/{RenderEndpointVariable} are unset; " +
                $"mark the test [{nameof(HardwareFactAttribute)}] so it is skipped instead.");
        }

        return (capture, render);
    }
}

/// <summary>The <see cref="TheoryAttribute"/> counterpart of <see cref="HardwareFactAttribute"/>.</summary>
public sealed class HardwareTheoryAttribute : TheoryAttribute
{
    public HardwareTheoryAttribute()
    {
        if (!HardwareFactAttribute.HasEndpoints())
        {
            Skip = HardwareFactAttribute.SkipReason;
        }
    }
}
