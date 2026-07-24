; Inno Setup script for ClaudeTimer (per-user installation, no administrator required)
; Compile: "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" installer\ClaudeTimer.iss
; The app payload is taken from artifacts\folder (produced by the Folder publish profile).

#define AppName "ClaudeTimer"
#define AppVersion "1.2.1"
#define AppPublisher "Timewinder"
#define AppURL "https://github.com/TimeWinder-dk/ClaudeTimer"
#define AppExe "ClaudeTimer.exe"

[Setup]
; Stable AppId so future versions upgrade in place instead of installing side by side.
AppId={{347FEC5C-4CE3-4537-94F7-2A63BE32E8A8}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
AppUpdatesURL={#AppURL}/releases
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=yes
; Per-user install: no elevation / UAC prompt.
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts\installer
OutputBaseFilename=ClaudeTimer-Setup-{#AppVersion}
SetupIconFile=..\src\ClaudeTimer\Logo\Logo.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName} {#AppVersion}
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes

[Languages]
Name: "danish"; MessagesFile: "compiler:Languages\Danish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startmenuicon"; Description: "Opret genvej i Start-menuen"; GroupDescription: "{cm:AdditionalIcons}"
Name: "pintaskbar"; Description: "Fastgør til proceslinjen (Windows kan ignorere dette; brug ellers højreklik → Fastgør)"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\artifacts\folder\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion
Source: "pin-taskbar.ps1"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: startmenuicon
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
; Best-effort taskbar pin (optional task). Windows 11 may refuse programmatic pinning.
Filename: "powershell.exe"; \
  Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\pin-taskbar.ps1"" ""{app}\{#AppExe}"""; \
  Tasks: pintaskbar; Flags: runhidden
; Offer to launch the app when the wizard finishes.
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
