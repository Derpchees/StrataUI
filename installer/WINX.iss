; Instalador de WINX (Inno Setup 6). Se compila con scripts\build-installer.ps1, que publica la
; app y pasa la versión de Directory.Build.props:
;   ISCC.exe /DAppVersion=0.12.0 /DSourceDir=..\artifacts\publish installer\WINX.iss
;
; Se instala para el usuario actual (sin administrador) en %LOCALAPPDATA%\Programs\WINX. Las
; tareas que sí lo necesitan (optimizaciones, audio) las pide WINX con UAC cuando hacen falta.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\publish"
#endif

[Setup]
AppId={{8C1B9E52-4D3A-4F0B-9B7E-6A2E1D5C7F31}
AppName=WINX
AppVersion={#AppVersion}
AppVerName=WINX {#AppVersion}
AppPublisher=Derpchees
AppPublisherURL=https://github.com/Derpchees/WINX
AppSupportURL=https://github.com/Derpchees/WINX/issues
AppUpdatesURL=https://github.com/Derpchees/WINX/releases
DefaultDirName={localappdata}\Programs\WINX
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
OutputDir=..\artifacts
OutputBaseFilename=WINX-Setup-{#AppVersion}
SetupIconFile=..\src\Winx.Shell\Assets\winx.ico
UninstallDisplayIcon={app}\WINX.exe
UninstallDisplayName=WINX
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Windows 11
MinVersion=10.0.22000
; WINX se cierra por su cuenta (ver PrepareToInstall): así devuelve la barra de Windows.
CloseApplications=no
RestartApplications=no

[Languages]
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
es.AutoStart=Iniciar WINX con Windows
en.AutoStart=Start WINX with Windows
es.LaunchWinx=Abrir WINX ahora
en.LaunchWinx=Open WINX now
es.Startup=Inicio:
en.Startup=Startup:

[Tasks]
Name: "autostart"; Description: "{cm:AutoStart}"; GroupDescription: "{cm:Startup}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
; Restos de una versión anterior que ya no forman parte de la app.
Type: filesandordirs; Name: "{app}\*.pdb"

[Icons]
Name: "{autoprograms}\WINX"; Filename: "{app}\WINX.exe"
Name: "{autodesktop}\WINX"; Filename: "{app}\WINX.exe"; Tasks: desktopicon

[Registry]
; El mismo valor que escribe el Hub (Services\AutoStart.cs).
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "WINX"; ValueData: """{app}\WINX.exe"""; Tasks: autostart

[Run]
Filename: "{app}\WINX.exe"; Description: "{cm:LaunchWinx}"; Flags: nowait postinstall skipifsilent
; Actualización silenciosa desde el Hub: WINX se vuelve a abrir solo.
Filename: "{app}\WINX.exe"; Flags: nowait; Check: WizardSilent

[UninstallRun]
; Cierra WINX limpiamente para que vuelva la barra de Windows antes de borrar los archivos.
Filename: "{app}\WINX.exe"; Parameters: "--exit"; Flags: runhidden waituntilterminated; RunOnceId: "ExitWinx"

[Code]
procedure CloseWinx();
var
  Code: Integer;
begin
  if FileExists(ExpandConstant('{app}\WINX.exe')) then
  begin
    Exec(ExpandConstant('{app}\WINX.exe'), '--exit', '', SW_HIDE, ewWaitUntilTerminated, Code);
    // --exit espera a que la instancia abierta termine; el margen es para que Windows suelte los archivos.
    Sleep(500);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  CloseWinx();
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    Sleep(500);
    // El inicio automático puede haberlo activado el Hub aunque no se eligiera al instalar.
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'WINX');
  end;
end;
