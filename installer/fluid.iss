; ----------------------------------------------------------------------------
; fluidMonitor - Installer (Inno Setup 6+)
; Compile: ISCC.exe installer\fluid.iss
; ----------------------------------------------------------------------------

#define AppName       "fluidMonitor"
#define AppVersion    "1.0.0"
#define AppPublisher  "Matt Hakes"
#define AppExeName    "fluidMonitor.exe"
#define SvcExeName    "fluidMonitor.service.exe"
#define SvcName       "fluidsvc"
#define SvcDisplay    "fluidMonitor Sensor Service"
#define SvcDesc       "Reads hardware sensors and broadcasts them via named pipe for the fluidMonitor widget."

[Setup]
AppId={{C8E5B1A4-2D6F-4A9C-B712-3E7F9D8A5C01}
AppName={#AppName}
AppVersion={#AppVersion}
; Show just "fluidMonitor" in Add/Remove Programs (no version suffix)
AppVerName={#AppName}
AppPublisher={#AppPublisher}
AppCopyright=Copyright (C) 2026 {#AppPublisher}
VersionInfoVersion={#AppVersion}.0
VersionInfoDescription={#AppName} system monitor

DefaultDirName={commonpf32}\fluidMonitor
DefaultGroupName=fluidMonitor
DisableProgramGroupPage=yes

PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=

OutputDir=Output
OutputBaseFilename=fluidMonitor_installer_v{#AppVersion}
SetupIconFile=..\Fluid.App\Assets\fluid.ico
UninstallDisplayIcon={app}\app\{#AppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ShowLanguageDialog=no
CloseApplications=force
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"
Name: "startupicon"; Description: "Run fluidMonitor at Windows startup (current user)"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
Source: "..\Fluid.App\bin\Release\net8.0-windows\win-x64\publish\*";     DestDir: "{app}\app";     Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\Fluid.Service\bin\Release\net8.0-windows\win-x64\publish\*"; DestDir: "{app}\service"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
; v1.22: LHM 0.9.4 extracted the vulnerable WinRing0 driver next to the service
; exe at runtime. Remove it on upgrade installs â€” the file on disk is the exposure.
Type: files; Name: "{app}\service\WinRing0x64.sys"
Type: files; Name: "{app}\service\WinRing0x64.dll"

[Icons]
Name: "{group}\fluidMonitor";           Filename: "{app}\app\{#AppExeName}"
Name: "{group}\Uninstall fluidMonitor";  Filename: "{uninstallexe}"
Name: "{commondesktop}\fluidMonitor";    Filename: "{app}\app\{#AppExeName}"; Tasks: desktopicon
Name: "{userstartup}\fluidMonitor";      Filename: "{app}\app\{#AppExeName}"; Tasks: startupicon

[Run]
; Stop + delete any prior service (safe to fail on first install)
Filename: "{sys}\sc.exe"; Parameters: "stop {#SvcName}";   Flags: runhidden waituntilterminated; StatusMsg: "Stopping existing service..."
Filename: "{sys}\sc.exe"; Parameters: "delete {#SvcName}"; Flags: runhidden waituntilterminated; StatusMsg: "Removing existing service..."
; Create + configure
Filename: "{sys}\sc.exe"; Parameters: "create {#SvcName} binPath= ""{app}\service\{#SvcExeName}"" start= auto DisplayName= ""{#SvcDisplay}"""; Flags: runhidden waituntilterminated; StatusMsg: "Registering service..."
Filename: "{sys}\sc.exe"; Parameters: "description {#SvcName} ""{#SvcDesc}"""; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "failure {#SvcName} reset= 86400 actions= restart/5000/restart/5000/restart/10000"; Flags: runhidden waituntilterminated
; Start
Filename: "{sys}\sc.exe"; Parameters: "start {#SvcName}"; Flags: runhidden waituntilterminated; StatusMsg: "Starting service..."
; ---- Add firewall rule for remote monitoring (TCP 5199, private profile) ----
; Rule exists but TCP is disabled by default â€” user opts in via Settings.
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""fluidMonitor Remote Sensor"" dir=in action=allow protocol=tcp localport=5199 profile=private description=""fluidMonitor remote hardware sensor feed"""; Flags: runhidden waituntilterminated; StatusMsg: "Adding firewall rule..."
; Launch widget
Filename: "{app}\app\{#AppExeName}"; Description: "Launch fluidMonitor"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; v1.23.1: force-kill the widget BEFORE anything else. A graceful close fires
; App.OnExit which re-saves settings.json -- if that happens after the
; uninstaller's DelTree, the "wiped" settings resurrect from process memory
; (observed: pre-migration v1.22 settings written back after uninstall, then
; migrated by the next install). taskkill /F skips OnExit entirely.
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM fluidMonitor.exe"; Flags: runhidden waituntilterminated; RunOnceId: "FluidKillWidget"
Filename: "{sys}\sc.exe"; Parameters: "stop {#SvcName}";   Flags: runhidden waituntilterminated; RunOnceId: "FluidSvcStop"
Filename: "{sys}\sc.exe"; Parameters: "delete {#SvcName}"; Flags: runhidden waituntilterminated; RunOnceId: "FluidSvcDelete"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""fluidMonitor Remote Sensor"""; Flags: runhidden waituntilterminated; RunOnceId: "FluidFwRule"

[UninstallDelete]
Type: filesandordirs; Name: "{commonappdata}\fluidMonitor"
Type: filesandordirs; Name: "{app}"

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Path: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // Per-user settings (APPDATA\fluidMonitor)
    Path := ExpandConstant('{userappdata}\fluidMonitor');
    if DirExists(Path) then DelTree(Path, True, True, True);

    // Service config + cert (ProgramData\fluidMonitor)
    Path := ExpandConstant('{commonappdata}\fluidMonitor');
    if DirExists(Path) then DelTree(Path, True, True, True);

    // Install folder (any leftovers)
    Path := ExpandConstant('{app}');
    if DirExists(Path) then DelTree(Path, True, True, True);
  end;
end;




