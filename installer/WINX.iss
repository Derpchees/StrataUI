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
DefaultDirName={autopf}\WINX
DefaultGroupName=WINX
; Asistente completo: bienvenida, carpeta (en la primera instalación; al actualizar se usa la
; misma), carpeta del menú Inicio, tareas y resumen.
DisableWelcomePage=no
DisableProgramGroupPage=auto
DisableDirPage=auto
DisableReadyPage=no
; Por defecto para el usuario actual (sin administrador, en %LOCALAPPDATA%\Programs); el asistente
; deja elegir instalarlo para todos los usuarios (Archivos de programa, pide UAC).
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog commandline
UsePreviousPrivileges=yes
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
; Antes de la 1.0 el acceso directo iba suelto en el menú Inicio; ahora va en su carpeta.
Type: files; Name: "{autoprograms}\WINX.lnk"

[Icons]
Name: "{group}\WINX"; Filename: "{app}\WINX.exe"
Name: "{group}\{cm:UninstallProgram,WINX}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\WINX"; Filename: "{app}\WINX.exe"; Tasks: desktopicon

[Registry]
; El valor de siempre; al abrirse, WINX lo cambia por una tarea programada (Services\AutoStart.cs).
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
var
  Code: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    Sleep(500);
    // Lecturas avanzadas (temperaturas, FPS): su tarea y la copia en Archivos de programa las creó
    // WINX con permiso de administrador; quitarlas también lo pide (solo si existen).
    if IsAdminInstallMode then
      Exec(ExpandConstant('{app}\WINX.exe'), '--remove-sensors-task', '', SW_HIDE, ewWaitUntilTerminated, Code)
    else if DirExists(ExpandConstant('{commonpf64}\WINX\Lecturas')) then
      ShellExec('runas', ExpandConstant('{app}\WINX.exe'), '--remove-sensors-task', '', SW_HIDE,
                ewWaitUntilTerminated, Code);
    // El inicio automático puede haberlo activado el Hub aunque no se eligiera al instalar.
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'WINX');
    // WINX pasa la clave Run a una tarea programada (Services\AutoStart.cs): se borra también.
    Exec(ExpandConstant('{sys}\schtasks.exe'), ExpandConstant('/Delete /TN "WINX ({username})" /F'), '',
         SW_HIDE, ewWaitUntilTerminated, Code);
  end;
end;
