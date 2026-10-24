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
; Huella del certificado que firma StrataUI.ContextMenu.msix (la pasa scripts\build-installer.ps1).
#ifndef CertThumbprint
  #define CertThumbprint ""
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
; Al actualizar se usa la carpeta anterior, salvo la de cuando se llamaba WINX: esa pasa a
; ...\StrataUI y la vieja se borra al terminar (ver InstallDir y CurStepChanged). La carpeta del
; menú Inicio también pasa a la nueva (la vieja se borra en [InstallDelete]).
DefaultDirName={code:InstallDir}
UsePreviousAppDir=no
DefaultGroupName=Strata UI
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
SetupLogging=yes

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
es.ContextMenu=Agregar «Anclar a Inicio de Strata UI» al menú contextual de Windows 11 (confía en el certificado de Strata UI; puede pedir permiso de administrador)
en.ContextMenu=Add “Pin to Strata UI Start” to the Windows 11 context menu (trusts the Strata UI certificate; may ask for administrator permission)
es.Integration=Integración:
en.Integration=Integration:

[Tasks]
Name: "autostart"; Description: "{cm:AutoStart}"; GroupDescription: "{cm:Startup}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "contextmenu"; Description: "{cm:ContextMenu}"; GroupDescription: "{cm:Integration}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "Integration.ps1"; DestDir: "{app}"; Flags: ignoreversion

[InstallDelete]
; Restos de una versión anterior que ya no forman parte de la app.
Type: filesandordirs; Name: "{app}\*.pdb"
; Hasta la 3.9 las lecturas avanzadas venían en el instalador; ahora son un módulo opcional.
Type: files; Name: "{app}\LibreHardwareMonitorLib.dll"
Type: files; Name: "{app}\HidSharp.dll"
Type: files; Name: "{app}\DiskInfoToolkit.dll"
Type: files; Name: "{app}\BlackSharp.Core.dll"
Type: files; Name: "{app}\RAMSPDToolkit-NDD.dll"
Type: files; Name: "{app}\Microsoft.Diagnostics.*.dll"
Type: files; Name: "{app}\Dia2Lib.dll"
Type: files; Name: "{app}\TraceReloggerLib.dll"
Type: filesandordirs; Name: "{app}\amd64"
Type: filesandordirs; Name: "{app}\x86"
Type: filesandordirs; Name: "{app}\arm64"
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
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "StrataUI"; ValueData: """{app}\StrataUI.exe"" --autostart"; Tasks: autostart
; El de cuando se llamaba WINX apunta a un ejecutable que ya no existe.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "WINX"; Flags: deletevalue

[Run]
; Menú contextual moderno (installer\context-menu): Windows solo acepta el paquete si su certificado
; está en "Personas de confianza" del equipo, y escribir ahí pide administrador. Después se registra
; el paquete para el usuario, con los archivos en la carpeta de la app. Va antes de abrir Strata UI:
; al abrirse, si el paquete está, quita el verbo clásico para que no salga dos veces.
Filename: "{sys}\certutil.exe"; Parameters: "-addstore TrustedPeople ""{app}\StrataUI.cer"""; Flags: runhidden; Tasks: contextmenu; Check: CertNeeded and IsAdminInstallMode
Filename: "{sys}\certutil.exe"; Parameters: "-addstore TrustedPeople ""{app}\StrataUI.cer"""; Verb: runas; Flags: runhidden shellexec waituntilterminated; Tasks: contextmenu; Check: CertNeeded and not IsAdminInstallMode
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File ""{app}\Integration.ps1"" -Action Register"; Flags: runhidden runasoriginaluser; Tasks: contextmenu
; Si la app se mudó de la carpeta WINX y no se eligió el menú contextual, el paquete registrado antes
; apunta a la carpeta borrada: se quita (Strata UI vuelve a poner el verbo clásico al abrirse).
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File ""{app}\Integration.ps1"" -Action Remove"; Flags: runhidden runasoriginaluser; Check: MovedFromLegacy and not WizardIsTaskSelected('contextmenu')
Filename: "{app}\StrataUI.exe"; Description: "{cm:LaunchStrata}"; Flags: nowait postinstall skipifsilent
; Actualización silenciosa desde el Hub: Strata UI se vuelve a abrir solo. Como el usuario y no con los
; permisos del instalador (sin postinstall, Inno Setup lo abre elevado en una instalación para todos:
; Windows bloquea entonces los íconos de bandeja y lo que se abre desde Strata UI sale elevado).
Filename: "{app}\StrataUI.exe"; Flags: nowait runasoriginaluser; Check: WizardSilent

[UninstallRun]
; Cierra Strata UI limpiamente para que vuelva la barra de Windows antes de borrar los archivos.
Filename: "{app}\StrataUI.exe"; Parameters: "--exit"; Flags: runhidden waituntilterminated; RunOnceId: "ExitStrata"
; El paquete del menú contextual moderno (si se registró).
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File ""{app}\Integration.ps1"" -Action Remove"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveContextMenu"

[Code]
const
  UninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{8C1B9E52-4D3A-4F0B-9B7E-6A2E1D5C7F31}_is1';

var
  // Carpeta de una instalación de cuando se llamaba WINX que se reemplaza por ...\StrataUI.
  LegacyDir: String;

// Carpeta de instalación: la anterior si hay una; si se llamaba WINX, su hermana StrataUI.
function InstallDir(Param: String): String;
var
  Previous: String;
begin
  Result := ExpandConstant('{autopf}\StrataUI');
  if RegQueryStringValue(HKA, UninstallKey, 'Inno Setup: App Path', Previous) and (Previous <> '') then
  begin
    Previous := RemoveBackslashUnlessRoot(Previous);
    if CompareText(ExtractFileName(Previous), 'WINX') = 0 then
    begin
      LegacyDir := Previous;
      Result := AddBackslash(ExtractFilePath(Previous)) + 'StrataUI';
    end
    else
      Result := Previous;
  end;
end;

function MovedFromLegacy: Boolean;
begin
  Result := (LegacyDir <> '') and (CompareText(LegacyDir, RemoveBackslashUnlessRoot(ExpandConstant('{app}'))) <> 0);
end;

// El certificado del paquete del menú contextual todavía no está en "Personas de confianza" del equipo.
function CertNeeded: Boolean;
var
  Code: Integer;
begin
  Result := True;
  if '{#CertThumbprint}' <> '' then
    if Exec(ExpandConstant('{sys}\certutil.exe'), '-store TrustedPeople {#CertThumbprint}', '', SW_HIDE, ewWaitUntilTerminated, Code) then
      Result := Code <> 0;
end;

procedure CloseApp(Dir, Exe: String);
var
  Code: Integer;
begin
  if (Dir <> '') and FileExists(AddBackslash(Dir) + Exe) then
  begin
    Exec(AddBackslash(Dir) + Exe, '--exit', '', SW_HIDE, ewWaitUntilTerminated, Code);
    // --exit espera a que la instancia abierta termine; el margen es para que Windows suelte los archivos.
    Sleep(500);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
begin
  CloseApp(ExpandConstant('{app}'), 'StrataUI.exe');
  // Actualización desde cuando se llamaba WINX (en su carpeta vieja o ya con el nombre nuevo).
  CloseApp(ExpandConstant('{app}'), 'WINX.exe');
  CloseApp(LegacyDir, 'StrataUI.exe');
  CloseApp(LegacyDir, 'WINX.exe');
  // El proceso elevado de las lecturas avanzadas se cierra solo poco después que Strata UI; por si
  // quedó trabado leyendo el hardware, se termina (si no, sus archivos no se pueden reemplazar).
  if IsAdminInstallMode then
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM StrataUI.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Result := '';
end;

// Con la app ya en ...\StrataUI, la carpeta WINX sobra (su desinstalador también: el registro ya
// apunta al nuevo). Solo si de verdad era la de la app.
procedure CurStepChanged(CurStep: TSetupStep);
var
  Code: Integer;
begin
  // La tarea de inicio automático pudo crearla un Strata UI elevado: Strata UI sin elevar no puede
  // cambiarla, así que si la app cambió de carpeta la tarea seguiría apuntando a la vieja. Con
  // administrador, el instalador la apunta a la nueva, con una espera limitada y sin pedir contraseña.
  if (CurStep = ssPostInstall) and IsAdminInstallMode then
  begin
    Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
         ExpandConstant('-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "{app}\Integration.ps1" -Action UpdateTask'), '',
         SW_HIDE, ewWaitUntilTerminated, Code);
    // El permiso de administrador de Strata UI (lecturas avanzadas y las acciones de la lista fija
    // de Tweaks\ElevatedBroker): con el instalador ya elevado se deja listo, sin UAC después.
    Exec(ExpandConstant('{app}\StrataUI.exe'), '--install-sensors-task', '', SW_HIDE, ewWaitUntilTerminated, Code);
    Log('Tarea de permisos de administrador: código ' + IntToStr(Code));
  end;
  if (CurStep = ssPostInstall) and MovedFromLegacy and DirExists(LegacyDir)
     and (FileExists(LegacyDir + '\StrataUI.exe') or FileExists(LegacyDir + '\WINX.exe')) then
  begin
    if DelTree(LegacyDir, True, True, True) then
      Log('Carpeta anterior borrada: ' + LegacyDir)
    else
      Log('No se pudo borrar toda la carpeta anterior: ' + LegacyDir);
  end;
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
    RegDeleteKeyIncludingSubkeys(HKEY_CURRENT_USER, 'Software\StrataUI\ContextMenu');
    RegDeleteKeyIfEmpty(HKEY_CURRENT_USER, 'Software\StrataUI');
    // El certificado: solo con permiso de administrador (en una instalación por usuario se queda;
    // es el público, sin clave privada).
    if IsAdminInstallMode and ('{#CertThumbprint}' <> '') then
      Exec(ExpandConstant('{sys}\certutil.exe'), '-delstore TrustedPeople {#CertThumbprint}', '', SW_HIDE, ewWaitUntilTerminated, Code);
    // Strata UI pasa la clave Run a una tarea programada (Services\AutoStart.cs): se borra también.
    Exec(ExpandConstant('{sys}\schtasks.exe'), ExpandConstant('/Delete /TN "StrataUI ({username})" /F'), '',
         SW_HIDE, ewWaitUntilTerminated, Code);
  end;
end;
