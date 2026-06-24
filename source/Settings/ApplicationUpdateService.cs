using System.Diagnostics;
using NetSparkleUpdater;
using NetSparkleUpdater.Enums;
using NetSparkleUpdater.SignatureVerifiers;
using NetSparkleUpdater.UI.WinForms;

namespace Resonalyze;

internal static class ApplicationUpdateService
{
    private const string AppCastUrl =
        "https://github.com/DIMOSUS/Resonalyze/releases/latest/download/appcast.xml";

    private static SparkleUpdater? updater;

    public static void ShowUpdateChoice(Form? owner, string releaseUrl)
    {
        DialogResult result = MessageBox.Show(
            owner,
            "A new Resonalyze release is available.\r\n\r\n" +
            "Choose Yes to download and run the automatic installer.\r\n" +
            "Choose No to open the GitHub release page and download manually.",
            "Update available",
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Information,
            MessageBoxDefaultButton.Button1);

        if (result == DialogResult.Yes)
        {
            StartAutomaticUpdate(owner);
            return;
        }

        if (result == DialogResult.No)
        {
            OpenReleasePage(releaseUrl);
        }
    }

    private static void StartAutomaticUpdate(Form? owner)
    {
        string publicKey = ApplicationVersionInfo.GetSparklePublicKey() ?? string.Empty;
        SecurityMode securityMode = string.IsNullOrWhiteSpace(publicKey)
            ? SecurityMode.UseIfPossible
            : SecurityMode.Strict;

        updater?.Dispose();
        updater = new SparkleUpdater(
            AppCastUrl,
            new Ed25519Checker(securityMode, publicKey, string.Empty))
        {
            UIFactory = new UIFactory(owner?.Icon),
            RelaunchAfterUpdate = false,
            CustomInstallerArguments = "/SP- /NORESTART"
        };
        updater.CheckForUpdatesAtUserRequest(ignoreSkippedVersions: true);
    }

    private static void OpenReleasePage(string releaseUrl)
    {
        string url = string.IsNullOrWhiteSpace(releaseUrl)
            ? GitHubReleaseChecker.ReleasesPageUrl
            : releaseUrl;
        Process.Start(new ProcessStartInfo(url)
        {
            UseShellExecute = true
        });
    }
}
