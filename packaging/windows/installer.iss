#define AppName "RixelPOS"
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppPublisher "RixelDev"
#define AppURL "https://rixel.dev"
#define AppExeName "RixelPOS.exe"

[Setup]
AppId={{5FBE6D3B-8C4A-4FB8-9A5E-6E5A7B2C9F11}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
AppUpdatesURL={#AppURL}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputBaseFilename=RixelPOS-Setup-{#AppVersion}
SetupIconFile=..\..\src\RixelPOS.Desktop\Assets\rixelpos.ico
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no
OutputDir=..\..\dist\windows

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear un acceso directo en el escritorio"; GroupDescription: "Accesos directos:"; Flags: unchecked
Name: "dotnet"; Description: "Instalar .NET SDK 8 si falta (requiere internet)"; GroupDescription: "Componentes opcionales:"; Flags: checkedonce

[Files]
Source: "..\..\dist\windows\RixelPOS\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Abrir carpeta de datos"; Filename: "{userappdata}\rixelpos"
Name: "{group}\Desinstalar {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Abrir {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
function InitializeSetup(): Boolean;
begin
  Result := True;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  ScriptPath: String;
begin
  if CurStep = ssPostInstall then
  begin
    if WizardIsTaskSelected('dotnet') then
    begin
      ScriptPath := ExpandConstant('{app}\prerequisites\install-dotnet.ps1');
      if FileExists(ScriptPath) then
      begin
        // Optional SDK bootstrap. Exec's boolean result is intentionally ignored:
        // RixelPOS is self-contained and must install even without internet.
        Exec('powershell.exe',
          '-NoProfile -ExecutionPolicy Bypass -File "' + ScriptPath + '" -Channel 8.0 -Quiet',
          '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      end;
    end;
  end;
end;
