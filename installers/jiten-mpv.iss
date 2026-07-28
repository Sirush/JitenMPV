; Inno Setup script for the double-clickable Windows installer.
;
;   iscc /DAppVersion=1.2.3 /DSourceExe=path\to\JitenMPV.App.exe installers\jiten-mpv.iss
;
; Both defines are optional; the defaults below match a local
;   dotnet publish src\JitenMPV.App -c Release -r win-x64 -o publish

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

#ifndef SourceExe
  #define SourceExe "..\publish\JitenMPV.App.exe"
#endif

#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

[Setup]
; Never change: it is how Windows recognises an existing installation to upgrade.
AppId={{060C60BB-3BCA-416B-8A1B-043D4FB36AE5}
AppName=JitenMPV
AppVersion={#AppVersion}
; Without this the installed-apps list reads "JitenMPV version 1.2.3", duplicating the version it
; already shows in its own column.
AppVerName=JitenMPV
AppPublisher=Sirush
AppPublisherURL=https://github.com/Sirush/JitenMPV
AppSupportURL=https://github.com/Sirush/JitenMPV/issues
VersionInfoVersion={#AppVersion}

; Per-user throughout: no UAC prompt, and nothing lands outside the user's own profile.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=

; The mpv Lua script spawns %APPDATA%\jiten-mpv\JitenMPV.App.exe by that exact path, so the
; directory is a contract rather than a preference. Offering a directory page would let a user
; produce an installation that silently never starts.
DefaultDirName={userappdata}\jiten-mpv
DisableDirPage=yes
UsePreviousAppDir=yes
DefaultGroupName=JitenMPV
DisableProgramGroupPage=yes

; win-x64 is the only published build; Windows on ARM runs it emulated.
ArchitecturesAllowed=x64compatible

LicenseFile=..\LICENSE
OutputDir={#OutputDir}
OutputBaseFilename=JitenMPV-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; Launched with no arguments the executable opens the settings window, which is the only thing a
; user would ever want from a shortcut.
Name: "{autoprograms}\JitenMPV Settings"; Filename: "{app}\JitenMPV.App.exe"

[Run]
; The same code path the CLI and the GUI banner use, so mpv script placement has one implementation:
; it resolves mpv's real config directory, including a portable_config beside mpv.exe.
Filename: "{app}\JitenMPV.App.exe"; Parameters: "install --quiet"; \
    StatusMsg: "Installing the mpv script..."; Flags: runhidden

Filename: "{app}\JitenMPV.App.exe"; Description: "Open JitenMPV settings"; \
    Flags: postinstall nowait skipifsilent

[UninstallRun]
; Runs while the executable is still present. Without --all, so config.json and any ffmpeg JitenMPV
; downloaded survive; Inno then removes only what it installed.
Filename: "{app}\JitenMPV.App.exe"; Parameters: "uninstall --quiet"; \
    RunOnceId: "RemoveMpvScript"; Flags: runhidden
