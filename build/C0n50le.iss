; c0n50le: per-user installer (no admin). Built by build\Publish-C0n50le.ps1:
;   ISCC /DAppVersion=0.1.0 /DSourceDir=<publish folder> /DOutputDir=<dist> C0n50le.iss

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #error SourceDir (the dotnet publish folder) is required
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

#define AppName "c0n50le"
#define AppExe "C0n50le.exe"

[Setup]
; Keep this AppId forever: it is how updates find the existing install.
AppId={{EBAD53CE-ADC1-468A-BA26-0E68C0996C37}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=1r0n51uv
AppPublisherURL=https://github.com/1r0n51uv/c0n50le
AppSupportURL=https://github.com/1r0n51uv/c0n50le/issues
AppUpdatesURL=https://github.com/1r0n51uv/c0n50le/releases
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=C0n50le-Setup-x64
SetupIconFile=..\src\C0n50le\Assets\c0n50le.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ShowLanguageDialog=yes
LanguageDetectionMethod=uilanguage
UsePreviousLanguage=yes
; Updates run silently from inside the app; close it through Restart Manager if it is still up.
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "it"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
it.StartWithWindows=Avvia con Windows
en.StartWithWindows=Start with Windows
it.LaunchApp=Apri c0n50le
en.LaunchApp=Open c0n50le

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startup"; Description: "{cm:StartWithWindows}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Tells the app it is installed: data goes to %LOCALAPPDATA%\c0n50le instead of next to the exe.
Source: "C0n50le.installed"; DestDir: "{app}"; Flags: ignoreversion

[InstallDelete]
; Up to 0.2.1 the resource index was resources.pri; now it is C0n50le.pri. WinUI loads resources.pri first, so a copy
; left behind by an update serves the old XAML to the new code and the app crashes at startup.
Type: files; Name: "{app}\resources.pri"

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Same value the app's "Start with Windows" setting writes (StartupRegistration).
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
    ValueName: "c0n50le"; ValueData: """{app}\{#AppExe}"" --startup"; Tasks: startup; Flags: uninsdeletevalue
; The language chosen in the installer; the app reads it on first run.
Root: HKCU; Subkey: "Software\c0n50le"; ValueType: string; ValueName: "Language"; ValueData: "it-IT"; Languages: it; Flags: uninsdeletekeyifempty uninsdeletevalue
Root: HKCU; Subkey: "Software\c0n50le"; ValueType: string; ValueName: "Language"; ValueData: "en-US"; Languages: en; Flags: uninsdeletekeyifempty uninsdeletevalue

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent
; In-app updates install with /SILENT: bring the app back afterwards.
Filename: "{app}\{#AppExe}"; Flags: nowait; Check: WizardSilent

[UninstallDelete]
Type: files; Name: "{app}\C0n50le.installed"
; User data in %LOCALAPPDATA%\c0n50le (settings, covers) is kept on purpose.
