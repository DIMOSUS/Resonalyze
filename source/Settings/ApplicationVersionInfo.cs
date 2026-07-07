using System.Globalization;
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

    public static string? GetSparklePublicKey() =>
        Assembly
            .GetEntryAssembly()?
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute =>
                string.Equals(attribute.Key, "SparklePublicKey", StringComparison.Ordinal))?
            .Value;

    public static bool IsOlderThan(string otherVersion) =>
        IsOlderThan(GetInformationalVersion(), otherVersion);

    // Exposed for tests: pure comparison of two raw version strings.
    internal static bool IsOlderThan(string currentRawVersion, string otherVersion)
    {
        if (!TryParseComparableVersion(currentRawVersion, out Version? current, out string? currentPrerelease) ||
            !TryParseComparableVersion(otherVersion, out Version? other, out string? otherPrerelease))
        {
            return false;
        }

        int versionCompare = current!.CompareTo(other!);
        if (versionCompare != 0)
        {
            return versionCompare < 0;
        }

        if (currentPrerelease != null && IsDevelopmentBuild(currentRawVersion))
        {
            return false;
        }

        return ComparePrereleaseIdentifiers(currentPrerelease, otherPrerelease) < 0;
    }

    // SemVer 2.0 §11: a prerelease sorts before its release; identifiers are compared
    // dot by dot — numerics numerically, alphanumerics ordinally, numeric before
    // alphanumeric — and a shorter identifier list sorts before a longer equal prefix.
    private static int ComparePrereleaseIdentifiers(string? current, string? other)
    {
        if (current == null)
        {
            return other == null ? 0 : 1;
        }
        if (other == null)
        {
            return -1;
        }

        string[] currentIdentifiers = current.Split('.');
        string[] otherIdentifiers = other.Split('.');
        int sharedCount = Math.Min(currentIdentifiers.Length, otherIdentifiers.Length);
        for (int i = 0; i < sharedCount; i++)
        {
            bool currentIsNumeric = int.TryParse(
                currentIdentifiers[i], NumberStyles.None, CultureInfo.InvariantCulture, out int currentNumber);
            bool otherIsNumeric = int.TryParse(
                otherIdentifiers[i], NumberStyles.None, CultureInfo.InvariantCulture, out int otherNumber);
            int compare = (currentIsNumeric, otherIsNumeric) switch
            {
                (true, true) => currentNumber.CompareTo(otherNumber),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(currentIdentifiers[i], otherIdentifiers[i])
            };
            if (compare != 0)
            {
                return compare;
            }
        }

        return currentIdentifiers.Length.CompareTo(otherIdentifiers.Length);
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
        out string? prerelease)
    {
        prerelease = null;
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
            prerelease = normalized[(prereleaseSeparator + 1)..];
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

    private static bool IsDevelopmentBuild(string rawVersion) =>
        rawVersion.Contains("-dev.", StringComparison.OrdinalIgnoreCase);
}
