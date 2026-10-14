; ============================================================
;  RISA-Studio  —  Inno Setup 6 Installer Script
;  Product   : RISA-Studio (RisaBotApp)
;  Version   : 2.0.0
;  Author    : RISA Team
;  Target OS : Windows 10 / 11 (x64)
;  Runtime   : Self-contained (.NET 10 bundled inside the EXE)
; ============================================================

#define AppName        "RISA-Studio"
#define AppVersion     "2.0.0"
#define AppPublisher   "RISA"
#define AppURL         "https://github.com/eemrull/RISA-bot"
#define AppExeName     "RisaBotApp.exe"
#define AppDescription "ROS 2 Intelligent System Autonomy — Robot Dashboard"

; Source folder produced by  dotnet publish --self-contained true --runtime win-x64
#define PublishDir     "publish"

[Setup]
; ── Identity ─────────────────────────────────────────────────────────────────
AppId={{8F3A2D4E-7C1B-4F9A-B2E8-5D6A3C0F1E2B}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}/issues
AppUpdatesURL={#AppURL}/releases
VersionInfoVersion={#AppVersion}.0
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppDescription}
VersionInfoProductName={#AppName}

; ── Install destination ───────────────────────────────────────────────────────
DefaultDirName={autopf}\{#AppPublisher}\{#AppName}
DefaultGroupName={#AppPublisher}\{#AppName}
AllowNoIcons=yes

; ── Output ───────────────────────────────────────────────────────────────────
OutputDir=output
OutputBaseFilename=RISA-Studio-v{#AppVersion}-Setup
SetupIconFile=..\RisaBotApp\risa.ico

; ── Wizard appearance ─────────────────────────────────────────────────────────
; Classic style — no bitmaps needed, clean and professional
WizardStyle=modern
DisableWelcomePage=no

; ── Compression ──────────────────────────────────────────────────────────────
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes

; ── Privileges & OS requirement ───────────────────────────────────────────────
; admin preferred so the app can install to Program Files
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
MinVersion=10.0.17763
; Windows 10 1809+ required (needed by WPF/.NET 10 on Windows)

; ── Uninstaller ──────────────────────────────────────────────────────────────
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
CreateUninstallRegKey=yes

; ── Signing placeholder (uncomment and configure when you have a cert) ────────
; SignTool=signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /f "cert.pfx" /p "PASSWORD" $f

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
; ── Main application (self-contained, all .NET bundled) ───────────────────────
Source: "{#PublishDir}\{#AppExeName}";                DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\D3DCompiler_47_cor3.dll";      DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\PenImc_cor3.dll";              DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\PresentationNative_cor3.dll";  DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\vcruntime140_cor3.dll";        DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\wpfgfx_cor3.dll";              DestDir: "{app}"; Flags: ignoreversion

; ── Exclude debug symbols from end-user build ─────────────────────────────────
; RisaBotApp.pdb is intentionally excluded

[Icons]
; Start Menu
Name: "{group}\{#AppName}";                           Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\{#AppExeName}";
Name: "{group}\Uninstall {#AppName}";                 Filename: "{uninstallexe}"

; Desktop (optional)
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
; Offer to launch immediately after install
Filename: "{app}\{#AppExeName}"; \
    Description: "Launch {#AppName} now"; \
    Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Clean up any settings/logs the app wrote to AppData
Type: filesandordirs; Name: "{localappdata}\{#AppPublisher}\{#AppName}"
Type: filesandordirs; Name: "{userappdata}\{#AppPublisher}\{#AppName}"

[Code]
// ─────────────────────────────────────────────────────────────────────────────
//  Custom installer code
// ─────────────────────────────────────────────────────────────────────────────

// Check Windows version: require Windows 10 1809 (build 17763) or later
function IsWindows10OrLater: Boolean;
var
  Version: TWindowsVersion;
begin
  GetWindowsVersionEx(Version);
  Result := (Version.Major > 10) or
            ((Version.Major = 10) and (Version.Build >= 17763));
end;

function InitializeSetup: Boolean;
begin
  if not IsWindows10OrLater then
  begin
    MsgBox('RISA-Studio requires Windows 10 version 1809 or later.' + #13#10 +
           'Please update Windows and try again.',
           mbCriticalError, MB_OK);
    Result := False;
    Exit;
  end;
  Result := True;
end;

// After install: remind the user about the robot prerequisites
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    MsgBox('RISA-Studio was installed successfully!' + #13#10#13#10 +
           'Before connecting to your robot, ensure:' + #13#10 +
           '  • Your PC and the RISA-Bot are on the same Wi-Fi network' + #13#10 +
           '  • The robot is powered on and has completed boot' + #13#10 +
           '  • The robot''s ROS 2 dashboard service is running (port 8080)' + #13#10#13#10 +
           'Open the app and use the Discovery screen to find your robot.',
           mbInformation, MB_OK);
  end;
end;
