using System.Diagnostics;
using NetSparkleUpdater;
using NetSparkleUpdater.Enums;
using NetSparkleUpdater.SignatureVerifiers;
using NetSparkleUpdater.UI.WinForms;
using Resonalyze.Ui.Dialogs;

namespace Resonalyze;

internal static class ApplicationUpdateService
{
    public const string ReleasesPageUrl = "https://github.com/DIMOSUS/Resonalyze/releases";

    private const string AppCastUrl =
        "https://github.com/DIMOSUS/Resonalyze/releases/latest/download/appcast.xml";

    private static SparkleUpdater? updater;
    private static Form? ownerForm;
    private static bool initialized;
    private static string latestReleaseUrl = ReleasesPageUrl;
    private static string? latestVersion;

    public static void Initialize(Form owner)
    {
        ownerForm = owner;
        if (initialized)
        {
            return;
        }

        updater = CreateUpdater(owner);
        initialized = true;
    }

    public static void ShowUpdateChoice(Form? owner, string releaseUrl)
    {
        latestReleaseUrl = string.IsNullOrWhiteSpace(releaseUrl)
            ? ReleasesPageUrl
            : releaseUrl;
        using var dialog = new ApplicationUpdateDialog(
            ApplicationVersionInfo.GetDisplayVersion(),
            latestVersion,
            supportsAutomaticUpdate: IsInstalledBuild());
        UpdatePromptChoice choice = dialog.ShowDialog(owner);
        if (choice == UpdatePromptChoice.Automatic)
        {
            StartAutomaticUpdate(owner);
            return;
        }

        if (choice == UpdatePromptChoice.Manual)
        {
            OpenReleasePage(latestReleaseUrl);
        }
    }

    private static void StartAutomaticUpdate(Form? owner)
    {
        EnsureInitialized(owner);
        if (updater == null)
        {
            OpenReleasePage(latestReleaseUrl);
            return;
        }

        if (!IsInstalledBuild())
        {
            OpenReleasePage(latestReleaseUrl);
            return;
        }

        updater.CheckForUpdatesAtUserRequest(ignoreSkippedVersions: true);
    }

    private static void OpenReleasePage(string releaseUrl)
    {
        string url = string.IsNullOrWhiteSpace(releaseUrl)
            ? ReleasesPageUrl
            : releaseUrl;
        Process.Start(new ProcessStartInfo(url)
        {
            UseShellExecute = true
        });
    }

    private static void EnsureInitialized(Form? owner)
    {
        if (initialized)
        {
            if (owner != null)
            {
                ownerForm = owner;
            }
            return;
        }

        if (owner == null)
        {
            return;
        }

        Initialize(owner);
    }

    private static SparkleUpdater CreateUpdater(Form? owner)
    {
        string publicKey = ApplicationVersionInfo.GetSparklePublicKey() ?? string.Empty;
        SecurityMode securityMode = string.IsNullOrWhiteSpace(publicKey)
            ? SecurityMode.UseIfPossible
            : SecurityMode.Strict;

        return new SparkleUpdater(
            AppCastUrl,
            new Ed25519Checker(securityMode, publicKey, string.Empty))
        {
            UIFactory = new UIFactory(owner?.Icon),
            RelaunchAfterUpdate = false,
            CustomInstallerArguments = "/SP- /NORESTART"
        };
    }

    private static bool IsInstalledBuild()
    {
        string executableDirectory = Path.GetDirectoryName(Application.ExecutablePath) ?? string.Empty;
        string installDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "Resonalyze");

        string normalizedExecutableDirectory = Path.GetFullPath(executableDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string normalizedInstallDirectory = Path.GetFullPath(installDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return string.Equals(
            normalizedExecutableDirectory,
            normalizedInstallDirectory,
            StringComparison.OrdinalIgnoreCase);
    }

    public static void SetDetectedRelease(
        string? version,
        string? releaseUrl)
    {
        latestVersion = string.IsNullOrWhiteSpace(version)
            ? null
            : version;
        latestReleaseUrl = string.IsNullOrWhiteSpace(releaseUrl)
            ? ReleasesPageUrl
            : releaseUrl;
    }
}
