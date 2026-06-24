#define AppName "Resonalyze"
#ifndef AppVersion
#define AppVersion "0.1.0"
#endif
#ifndef SourceDir
#define SourceDir "..\source\bin\Release\net10.0-windows\win-x64\publish"
#endif
#ifndef OutputDir
#define OutputDir "..\artifacts"
#endif
#ifndef OutputBaseFilename
#define OutputBaseFilename "Resonalyze-Setup"
#endif

[Setup]
AppId={{9C3C57A1-5E8A-4E2D-BE15-2F840DC3B44B}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=DIMOSUS
AppPublisherURL=https://github.com/DIMOSUS/Resonalyze
AppSupportURL=https://github.com/DIMOSUS/Resonalyze/issues
AppUpdatesURL=https://github.com/DIMOSUS/Resonalyze/releases
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename={#OutputBaseFilename}
SetupIconFile=..\source\logo.ico
UninstallDisplayIcon={app}\Resonalyze.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
CloseApplications=yes
CloseApplicationsFilter=Resonalyze.exe
RestartApplications=no

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\Resonalyze.exe"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\Resonalyze.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Run]
Filename: "{app}\Resonalyze.exe"; Description: "Launch {#AppName}"; Flags: nowait postinstall
