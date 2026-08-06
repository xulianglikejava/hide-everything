; Inno Setup script for HideEverything — a friendly per-user installer.
; Build with: iscc installer\HideIt.iss   (requires Inno Setup 6+)
; It packages the single-file self-contained publish output, so publish first:
;   dotnet publish -c Release -r win-x64 --self-contained true ^
;     -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true

#define MyAppName "HideEverything"
#define MyAppVersion "1.1.2"
#define MyAppPublisher "HideEverything"
#define MyAppExeName "HideEverything.exe"
#define MyAppUrl "https://github.com/xulianglikejava/hide-everything"

[Setup]
; A stable, unique GUID for this product (keep it constant across versions).
AppId={{B4E6F7A2-3C5D-4E8F-9A1B-2C3D4E5F6A7B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppUrl}
AppSupportURL={#MyAppUrl}/issues
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
OutputDir=output
OutputBaseFilename=HideEverything-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
LicenseFile=..\LICENSE
DefaultLanguage=chinesesimp
; Per-user install — no admin needed (installs under the user's local programs).
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=..\Assets\app.ico

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimp"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked
Name: "startupwithwindows"; Description: "Start {#MyAppName} when Windows starts"; GroupDescription: "Startup:"

[Files]
Source: "..\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; "Run at startup" — only created if the user ticked the task; removed on uninstall.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
    ValueName: "HideEverything"; ValueData: """{app}\{#MyAppExeName}"" --startup"; \
    Tasks: startupwithwindows; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName} now"; Flags: nowait postinstall skipifsilent

[Code]
{ Always remove the startup entry on uninstall, even if the app (not the installer) created it. }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    RegDeleteValue(HKEY_CURRENT_USER,
      'Software\Microsoft\Windows\CurrentVersion\Run', 'HideEverything');
end;
