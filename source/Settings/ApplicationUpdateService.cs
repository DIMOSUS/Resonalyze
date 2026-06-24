using System.Diagnostics;
using System.Text;
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
    private static bool installerLaunchAttempted;

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

        var sparkleUpdater = new SparkleUpdater(
            AppCastUrl,
            new Ed25519Checker(securityMode, publicKey, string.Empty))
        {
            UIFactory = new UIFactory(owner?.Icon),
            RelaunchAfterUpdate = false,
            CustomInstallerArguments = "/SP- /NORESTART"
        };

        sparkleUpdater.InstallerProcessAboutToStart += (_, downloadFilePath) =>
        {
            return StartDetachedInstallerLauncher(downloadFilePath);
        };

        sparkleUpdater.InstallUpdateFailed += (failureReason, installPath) =>
        {
            if (installerLaunchAttempted)
            {
                return true;
            }

            string details = string.IsNullOrWhiteSpace(installPath)
                ? "Installer path is unavailable."
                : $"Installer path: {installPath}";
            MessageBox.Show(
                ownerForm,
                $"Automatic update could not start.\r\n\r\nReason: {failureReason}\r\n{details}\r\n\r\nThe release page will be opened so you can install the update manually.",
                "Update failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            OpenReleasePage(latestReleaseUrl);
            return true;
        };

        sparkleUpdater.DownloadFinished += (_, _) =>
        {
            installerLaunchAttempted = false;
        };

        sparkleUpdater.CloseApplication += () =>
        {
            ownerForm?.BeginInvoke(() =>
            {
                if (!installerLaunchAttempted)
                {
                    return;
                }

                Application.Exit();
            });
        };

        return sparkleUpdater;
    }

    private static bool StartDetachedInstallerLauncher(string downloadFilePath)
    {
        if (string.IsNullOrWhiteSpace(downloadFilePath) || !File.Exists(downloadFilePath))
        {
            return true;
        }

        string commandProcessor =
            Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
        if (string.IsNullOrWhiteSpace(commandProcessor))
        {
            commandProcessor = "cmd.exe";
        }

        try
        {
            string appDataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Resonalyze");
            Directory.CreateDirectory(appDataDirectory);

            string scriptPath = Path.Combine(appDataDirectory, "run-update-installer.cmd");
            string logPath = Path.Combine(appDataDirectory, "update-launcher.log");
            string installerPath = EnsureInstallerExecutableCopy(downloadFilePath, appDataDirectory);
            File.WriteAllText(scriptPath, CreateInstallerLauncherScript(), Encoding.ASCII);

            using Process currentProcess = Process.GetCurrentProcess();
            var startInfo = new ProcessStartInfo
            {
                FileName = commandProcessor,
                Arguments =
                    $"/d /c \"\"{scriptPath}\" \"{currentProcess.Id}\" \"{installerPath}\" \"{logPath}\"\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = appDataDirectory
            };

            Process.Start(startInfo);
            installerLaunchAttempted = true;
            RequestApplicationExit();
            return false;
        }
        catch
        {
            return true;
        }
    }

    private static string EnsureInstallerExecutableCopy(
        string downloadFilePath,
        string appDataDirectory)
    {
        if (downloadFilePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return downloadFilePath;
        }

        string executablePath = Path.Combine(appDataDirectory, "Resonalyze-Update-Installer.exe");
        File.Copy(downloadFilePath, executablePath, overwrite: true);
        return executablePath;
    }

    private static string CreateInstallerLauncherScript() =>
        """
        @echo off
        setlocal
        set "APP_PID=%~1"
        set "INSTALLER=%~2"
        set "LOG=%~3"

        >>"%LOG%" echo [%date% %time%] Resonalyze update launcher started.
        >>"%LOG%" echo [%date% %time%] Waiting for process %APP_PID%.

        :wait_for_app
        tasklist /FI "PID eq %APP_PID%" /NH | findstr /C:"%APP_PID%" >nul
        if not errorlevel 1 (
            timeout /t 1 /nobreak >nul
            goto wait_for_app
        )

        if not exist "%INSTALLER%" (
            >>"%LOG%" echo [%date% %time%] Installer not found: %INSTALLER%
            exit /b 2
        )

        >>"%LOG%" echo [%date% %time%] Starting installer: %INSTALLER%
        start "" "%INSTALLER%" /SP- /NORESTART
        exit /b 0
        """;

    private static void RequestApplicationExit()
    {
        if (ownerForm?.IsHandleCreated == true)
        {
            ownerForm.BeginInvoke(Application.Exit);
            return;
        }

        Application.Exit();
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
