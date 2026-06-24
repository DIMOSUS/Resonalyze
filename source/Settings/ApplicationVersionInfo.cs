using System.Reflection;

namespace Resonalyze;

internal static class ApplicationVersionInfo
{
    public static string GetDisplayVersion()
    {
        string version = GetInformationalVersion();
        int metadataSeparator = version.IndexOf('+');
        if (metadataSeparator >= 0)
        {
            version = version[..metadataSeparator];
        }

        return version.StartsWith("v", StringComparison.OrdinalIgnoreCase)
            ? version
            : $"v{version}";
    }

    public static bool IsOlderThan(string otherVersion)
    {
        if (!TryParseComparableVersion(GetInformationalVersion(), out Version? current, out bool currentPrerelease) ||
            !TryParseComparableVersion(otherVersion, out Version? other, out bool otherPrerelease))
        {
            return false;
        }

        int versionCompare = current.CompareTo(other);
        if (versionCompare != 0)
        {
            return versionCompare < 0;
        }

        return currentPrerelease && !otherPrerelease;
    }

    private static string GetInformationalVersion()
    {
        string? informationalVersion = Assembly
            .GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        return string.IsNullOrWhiteSpace(informationalVersion)
            ? Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.1.0"
            : informationalVersion;
    }

    private static bool TryParseComparableVersion(
        string rawVersion,
        out Version? version,
        out bool prerelease)
    {
        prerelease = false;
        version = null;
        if (string.IsNullOrWhiteSpace(rawVersion))
        {
            return false;
        }

        string normalized = rawVersion.Trim();
        if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[1..];
        }

        int metadataSeparator = normalized.IndexOf('+');
        if (metadataSeparator >= 0)
        {
            normalized = normalized[..metadataSeparator];
        }

        int prereleaseSeparator = normalized.IndexOf('-');
        if (prereleaseSeparator >= 0)
        {
            prerelease = true;
            normalized = normalized[..prereleaseSeparator];
        }

        string[] parts = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 2 or > 4)
        {
            return false;
        }

        var numbers = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out numbers[i]))
            {
                return false;
            }
        }

        version = parts.Length switch
        {
            2 => new Version(numbers[0], numbers[1]),
            3 => new Version(numbers[0], numbers[1], numbers[2]),
            _ => new Version(numbers[0], numbers[1], numbers[2], numbers[3])
        };
        return true;
    }
}
