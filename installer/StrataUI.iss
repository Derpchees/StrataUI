; Instalador de Strata UI (Inno Setup 6). Se compila con scripts\build-installer.ps1, que publica la
; app y pasa la versión de Directory.Build.props:
;   ISCC.exe /DAppVersion=0.12.0 /DSourceDir=..\artifacts\publish installer\StrataUI.iss
;
; Se instala para el usuario actual (sin administrador) en %LOCALAPPDATA%\Programs\StrataUI. Las
; tareas que sí lo necesitan (optimizaciones, audio) las pide Strata UI con UAC cuando hacen falta.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\publish"
#endif

[Setup]
AppId={{8C1B9E52-4D3A-4F0B-9B7E-6A2E1D5C7F31}
AppName=Strata UI
AppVersion={#AppVersion}
AppVerName=Strata UI {#AppVersion}
AppPublisher=Derpchees
AppPublisherURL=https://github.com/Derpchees/StrataUI
AppSupportURL=https://github.com/Derpchees/StrataUI/issues
AppUpdatesURL=https://github.com/Derpchees/StrataUI/releases
DefaultDirName={autopf}\StrataUI
DefaultGroupName=Strata UI
; Antes se llamaba WINX: al actualizar, la carpeta de la app se conserva pero la del menú Inicio
; pasa a la nueva (la vieja se borra en [InstallDelete]).
UsePreviousGroup=no
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
OutputBaseFilename=StrataUI-Setup-{#AppVersion}
SetupIconFile=..\src\Strata.Shell\Assets\strataui.ico
UninstallDisplayIcon={app}\StrataUI.exe
UninstallDisplayName=Strata UI
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Windows 11
MinVersion=10.0.22000
; Strata UI se cierra por su cuenta (ver PrepareToInstall): así devuelve la barra de Windows.
CloseApplications=no
RestartApplications=no

[Languages]
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
es.AutoStart=Iniciar Strata UI con Windows
en.AutoStart=Start Strata UI with Windows
es.LaunchStrata=Abrir Strata UI ahora
en.LaunchStrata=Open Strata UI now
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
; Cuando se llamaba WINX (hasta la 1.3): su ejecutable, su carpeta del menú Inicio y su acceso directo.
Type: files; Name: "{app}\WINX.exe"
Type: files; Name: "{app}\WINX.dll"
Type: files; Name: "{app}\WINX.deps.json"
Type: files; Name: "{app}\WINX.runtimeconfig.json"
Type: filesandordirs; Name: "{autoprograms}\WINX"
Type: files; Name: "{autodesktop}\WINX.lnk"

[Icons]
Name: "{group}\Strata UI"; Filename: "{app}\StrataUI.exe"
Name: "{group}\{cm:UninstallProgram,Strata UI}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Strata UI"; Filename: "{app}\StrataUI.exe"; Tasks: desktopicon

[Registry]
; El valor de siempre; al abrirse, Strata UI lo cambia por una tarea programada (Services\AutoStart.cs).
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "StrataUI"; ValueData: """{app}\StrataUI.exe"""; Tasks: autostart
; El de cuando se llamaba WINX apunta a un ejecutable que ya no existe.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "WINX"; Flags: deletevalue

[Run]
Filename: "{app}\StrataUI.exe"; Description: "{cm:LaunchStrata}"; Flags: nowait postinstall skipifsilent
; Actualización silenciosa desde el Hub: Strata UI se vuelve a abrir solo.
Filename: "{app}\StrataUI.exe"; Flags: nowait; Check: WizardSilent

[UninstallRun]
; Cierra Strata UI limpiamente para que vuelva la barra de Windows antes de borrar los archivos.
Filename: "{app}\StrataUI.exe"; Parameters: "--exit"; Flags: runhidden waituntilterminated; RunOnceId: "ExitStrata"

[Code]
procedure CloseApp(Exe: String);
var
  Code: Integer;
begin
  if FileExists(ExpandConstant('{app}\') + Exe) then
  begin
    Exec(ExpandConstant('{app}\') + Exe, '--exit', '', SW_HIDE, ewWaitUntilTerminated, Code);
    // --exit espera a que la instancia abierta termine; el margen es para que Windows suelte los archivos.
    Sleep(500);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  CloseApp('StrataUI.exe');
  // Actualización desde cuando se llamaba WINX.
  CloseApp('WINX.exe');
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
    // Strata UI con permiso de administrador; quitarlas también lo pide (solo si existen).
    if IsAdminInstallMode then
      Exec(ExpandConstant('{app}\StrataUI.exe'), '--remove-sensors-task', '', SW_HIDE, ewWaitUntilTerminated, Code)
    else if DirExists(ExpandConstant('{commonpf64}\StrataUI\Lecturas')) then
      ShellExec('runas', ExpandConstant('{app}\StrataUI.exe'), '--remove-sensors-task', '', SW_HIDE,
                ewWaitUntilTerminated, Code);
    // El inicio automático puede haberlo activado el Hub aunque no se eligiera al instalar.
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'StrataUI');
    // "Anclar a Inicio de Strata UI" del menú del Explorador (lo pone la app: Services\StartMenu\ExplorerMenu.cs).
    RegDeleteKeyIncludingSubkeys(HKEY_CURRENT_USER, 'Software\Classes\AllFilesystemObjects\shell\StrataUI.PinToStart');
    // Strata UI pasa la clave Run a una tarea programada (Services\AutoStart.cs): se borra también.
    Exec(ExpandConstant('{sys}\schtasks.exe'), ExpandConstant('/Delete /TN "StrataUI ({username})" /F'), '',
         SW_HIDE, ewWaitUntilTerminated, Code);
  end;
end;
