; Process Core Optimizer 2.0.0 — Inno Setup script.
; Identity note: the historical 1.3.0 Setup.exe identity (AppId/ProductCode)
; is NOT recoverable from this repository (built externally, no .iss archived).
; This installer therefore uses a deliberate NEW identity documented here and in
; docs/INSTALLER.md. It is NOT advertised as an in-place upgrade over 1.3.0:
; uninstall the historical version first; %APPDATA%\ProcessCoreOptimizer data
; (profiles/settings/logs) is never touched and survives.
;
; Build (repeatable): powershell -ExecutionPolicy Bypass -File tools/Build-Installer.ps1
; Requires: dotnet SDK 10 + Inno Setup 6 (winget: JRSoftware.InnoSetup).

#define AppName "Process Core Optimizer"
#define AppVersion GetEnv("PCO_VERSION")
#if AppVersion == ""
  #error "PCO_VERSION env var is required (tools/Build-Installer.ps1 sets it from version.txt)."
#endif
#define AppPublisher "Eryk / 9Erza"
#define AppURL "https://github.com/9Erza/ProcessCoreOptimizer"
#define AppId "{6AA13087-A3D7-4501-9AAD-52D4CAC7668F}"
#define PublishDir "..\\ProcessCoreOptimizer\\bin\\Release\\net10.0-windows\\publish\\win-x64"

[Setup]
AppId={{#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
AppUpdatesURL={#AppURL}
DefaultDirName={autopf}\{#AppName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
SetupIconFile=..\ProcessCoreOptimizer\processcoreoptimizer.ico
WizardStyle=modern
SolidCompression=yes
Compression=lzma2/max
OutputDir=Output
OutputBaseFilename=ProcessCoreOptimizer_Setup_{#AppVersion}
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} Installer
VersionInfoProductName={#AppName}
UninstallDisplayName={#AppName} {#AppVersion}
UninstallDisplayIcon={app}\ProcessCoreOptimizer.exe
PrivilegesRequired=admin
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\ProcessCoreOptimizer.exe"; DestDir: "{app}"; Flags: ignoreversion
; Native WPF/host dependencies of the self-contained single-file build.
; REQUIRED at runtime: an install without them crashes on window creation
; (missing wpfgfx_cor3.dll / PresentationNative_cor3.dll). Keep in sync with
; tools/Validate-Release.ps1 payload checks.
Source: "{#PublishDir}\*_cor3.dll"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#PublishDir}\MonoPosixHelper.dll"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#PublishDir}\libMonoPosixHelper.dll"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
; PDB ships for diagnosability of field crash reports; harmless next to a single-file build.
Source: "{#PublishDir}\ProcessCoreOptimizer.pdb"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\ProcessCoreOptimizer.exe"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\ProcessCoreOptimizer.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\ProcessCoreOptimizer.exe"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Intentionally NOT deleting %APPDATA%\ProcessCoreOptimizer: user profiles and
; settings must survive uninstall/reinstall. Documented in docs/INSTALLER.md.
