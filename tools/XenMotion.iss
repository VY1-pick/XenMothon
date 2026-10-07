#ifndef PublishDir
  #error PublishDir is required
#endif
#ifndef ReleaseDir
  #error ReleaseDir is required
#endif
#define AppVersion "0.9.0-beta.1"
[Setup]
AppId={{E911D190-7392-4D2B-A2D5-86544DDC7C45}
AppName=XenMotion
AppVersion={#AppVersion}
AppVerName=XenMotion {#AppVersion} Private Beta
AppPublisher=XenMotion
AppPublisherURL=https://github.com/VY1-pick/Lumen-Wallpaper
DefaultDirName={localappdata}\Programs\XenMotion
DefaultGroupName=XenMotion
PrivilegesRequired=lowest
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
MinVersion=10.0.19041
OutputDir={#ReleaseDir}
OutputBaseFilename=XenMotion-{#AppVersion}-Setup-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\src\XenMotion.UI\Assets\xenmotion-logo.ico
UninstallDisplayIcon={app}\XenMotion.UI.exe
CloseApplications=yes
RestartApplications=no
LicenseFile=..\LICENSE

[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\XenMotion"; Filename: "{app}\XenMotion.UI.exe"; Parameters: "--show"
Name: "{autodesktop}\XenMotion"; Filename: "{app}\XenMotion.UI.exe"; Parameters: "--show"; Tasks: desktopicon

[Run]
Filename: "{app}\XenMotion.UI.exe"; Parameters: "--show"; Description: "Launch XenMotion"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; User library/settings are deliberately preserved.
Type: files; Name: "{userstartup}\XenMotion.lnk"

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
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
  if CurUninstallStep = usUninstall then begin
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'XenMotion', RunValue) then
      if Pos(Lowercase(ExpandConstant('{app}\XenMotion.UI.exe')), Lowercase(RunValue)) > 0 then
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
            if CompareText(ActionPath, ExpandConstant('{app}\XenMotion.UI.exe')) = 0 then
              Folder.DeleteTask(TaskName, 0);
          end;
        end;
      except
        Log('Startup task cleanup was unavailable.');
      end;
    end;
  end;
end;
