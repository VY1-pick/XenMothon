#ifndef PublishDir
  #error PublishDir is required
#endif
#ifndef ReleaseDir
  #error ReleaseDir is required
#endif
#define AppVersion "0.9.0-beta.3"
[Setup]
AppId={{E911D190-7392-4D2B-A2D5-86544DDC7C45}
AppName=XenMotion
AppVersion={#AppVersion}
VersionInfoVersion=0.9.0.3
AppVerName=XenMotion {#AppVersion} Private Beta
AppPublisher=XenMotion
AppPublisherURL=https://github.com/VY1-pick/XenMothon
DefaultDirName={localappdata}\Programs\XenMotion
DefaultGroupName=XenMotion
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.19041
OutputDir={#ReleaseDir}
OutputBaseFilename=XenMotion-{#AppVersion}-Setup-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\src\XenMotion.UI\Assets\xenmotion-logo.ico
UninstallDisplayIcon={app}\XenMotion.exe
CloseApplications=yes
RestartApplications=no
LicenseFile=..\docs\legal\BETA-TERMS.txt

[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; Flags: unchecked
Name: launchatlogin; Description: "Start XenMotion with Windows"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\XenMotion"; Filename: "{app}\XenMotion.exe"; Parameters: "--show"
Name: "{autodesktop}\XenMotion"; Filename: "{app}\XenMotion.exe"; Parameters: "--show"; Tasks: desktopicon

[Run]
Filename: "{app}\XenMotion.exe"; Parameters: "--show"; Description: "Launch XenMotion"; Flags: nowait postinstall skipifsilent

[Registry]
Root: HKCU; Subkey: "Software\XenMotion"; ValueType: string; ValueName: "AcceptedBetaTerms"; ValueData: "1.0 / 2026-10-08"
Root: HKCU; Subkey: "Software\XenMotion"; ValueType: dword; ValueName: "PendingStartupChoice"; ValueData: "{code:StartupChoice}"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "XenMotion"; ValueData: """{app}\XenMotion.exe"" --minimized"; Tasks: launchatlogin

[Code]
function InitializeSetup(): Boolean;
begin
  Result := (not WizardSilent) or (CompareText(ExpandConstant('{param:ACCEPTTERMS|no}'), 'yes') = 0);
  if not Result then Log('Silent installation requires /ACCEPTTERMS=yes.');
end;

function StartupChoice(Param: String): String;
begin
  if WizardIsTaskSelected('launchatlogin') then Result := '1' else Result := '0';
end;

procedure RemoveInstalledStartupRegistration();
var
  Scheduler, Folder: Variant;
  Tasks: Variant;
  I: Integer;
  Task: Variant;
  Action: Variant;
  RunValue: String;
  TaskName: String;
  ActionPath: String;
begin
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'XenMotion', RunValue) then
      if (Pos(Lowercase(ExpandConstant('{app}\XenMotion.exe')), Lowercase(RunValue)) > 0) or
         (Pos(Lowercase(ExpandConstant('{app}\XenMotion.UI.exe')), Lowercase(RunValue)) > 0) then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'XenMotion');
    begin
      { Remove only registrations that point into this installed directory. }
      try
        Scheduler := CreateOleObject('Schedule.Service');
        Scheduler.Connect;
        Folder := Scheduler.GetFolder('\');
        Tasks := Folder.GetTasks(0);
        for I := Tasks.Count downto 1 do begin
          Task := Tasks.Item(I);
          TaskName := Task.Name;
          if Pos('XenMotion Startup-', TaskName) = 1 then begin
            Action := Task.Definition.Actions.Item(1);
            ActionPath := Action.Path;
            if (CompareText(ActionPath, ExpandConstant('{app}\XenMotion.exe')) = 0) or
               (CompareText(ActionPath, ExpandConstant('{app}\XenMotion.UI.exe')) = 0) then
              Folder.DeleteTask(TaskName, 0);
          end;
        end;
      except
        Log('Startup task cleanup was unavailable.');
      end;
    end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  { Clear only this installation's previous registration before applying the chosen task. }
  if CurStep = ssInstall then RemoveInstalledStartupRegistration();
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then RemoveInstalledStartupRegistration();
end;
