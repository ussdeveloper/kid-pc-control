; Kid PC Control — Inno Setup script
; Build with Inno Setup Compiler after publishing to installer\payload

#define MyAppName "Kid PC Control"
#define MyAppVersion "0.2.7"
#define MyAppPublisher "ussdeveloper"
#define MyAppURL "https://github.com/ussdeveloper/kid-pc-control"

[Setup]
AppId={{A7C2E9B1-4D55-4F2A-9C1E-8B0F6D3A2E11}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
DefaultDirName={autopf}\KidPcControl
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=output
OutputBaseFilename=KidPcControl-Setup-v{#MyAppVersion}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=icons\setup.ico
UninstallDisplayIcon={app}\KidPcControl.Setup.exe
PrivilegesRequiredOverridesAllowed=dialog
CloseApplications=force
RestartApplications=yes
AppMutex=KidPcControlSetupMutex

[Languages]
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "payload\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs restartreplace

[Icons]
Name: "{group}\Kid PC Control Setup"; Filename: "{app}\KidPcControl.Setup.exe"
Name: "{group}\Kid PC Control Admin (tray)"; Filename: "{app}\KidPcControl.Admin.exe"
Name: "{commondesktop}\Kid PC Control Admin"; Filename: "{app}\KidPcControl.Admin.exe"
Name: "{commondesktop}\Kid PC Control Setup"; Filename: "{app}\KidPcControl.Setup.exe"

[Run]
Filename: "{app}\KidPcControl.Setup.exe"; Description: "Uruchom konfigurację Admin/Kid (wymaga Administratora)"; Flags: nowait postinstall skipifsilent runascurrentuser

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';
  Exec('sc.exe', 'stop KidPcControlService', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(1500);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    Exec('sc.exe', 'start KidPcControlService', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;