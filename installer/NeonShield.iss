#define AppName "NeonShield"
#ifndef AppVersion
  #define AppVersion "1.4.1"
#endif
#define AppPublisher "Yiertex"
#define AppExeName "NeonShield.exe"

[Setup]
AppId={{2A254276-956F-4EB1-BD18-73B76478AC76}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\release\installer
OutputBaseFilename=NeonShield-Setup-{#AppVersion}-win-x64
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#AppExeName}
CloseApplications=yes
RestartApplications=yes
SetupLogging=yes

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[Files]
Source: "..\release\win-x64\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Desktop-Verknüpfung erstellen"; GroupDescription: "Zusätzliche Verknüpfungen:"

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{#AppName} starten"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}\Engine"
Type: filesandordirs; Name: "{app}\Engine.previous"
Type: filesandordirs; Name: "{localappdata}\NeonShield\Database"
Type: files; Name: "{localappdata}\NeonShield\freshclam.conf"
Type: files; Name: "{localappdata}\NeonShield\engine-install.log"

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    WizardForm.StatusLabel.Caption :=
      'Die aktuelle ClamAV-Engine und Virensignaturen werden heruntergeladen ...';
    WizardForm.ProgressGauge.Style := npbstMarquee;

    if (not Exec(
      ExpandConstant('{app}\{#AppExeName}'),
      '--install-engine',
      ExpandConstant('{app}'),
      SW_HIDE,
      ewWaitUntilTerminated,
      ResultCode)) or (ResultCode <> 0) then
    begin
      RaiseException(
        'ClamAV konnte nicht heruntergeladen oder eingerichtet werden. ' +
        'Bitte prüfen Sie die Internetverbindung und starten Sie die Installation erneut. ' +
        'Details: %LOCALAPPDATA%\NeonShield\engine-install.log');
    end;

    WizardForm.ProgressGauge.Style := npbstNormal;
  end;
end;
