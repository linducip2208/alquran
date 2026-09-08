#define MyAppName "Quran Desktop"
#define MyAppVersion "1.4.0"
#define MyAppPublisher "Lindu Cipta Pranayama"
#define MyAppExeName "QuranDesktop.exe"

[Setup]
AppId={{8E20D8A1-6A70-4C56-A3E6-4F6E95F9A101}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\Quran Desktop
DefaultGroupName={#MyAppName}
OutputDir=..\artifacts
OutputBaseFilename=QuranDesktop-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64
UninstallDisplayIcon={app}\{#MyAppExeName}

[Files]
Source: "..\artifacts\publish-win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Jalankan {#MyAppName}"; Flags: nowait postinstall skipifsilent
