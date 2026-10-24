<#
.SYNOPSIS
    Publica Strata UI (autocontenido, sin depender de .NET instalado) y genera el instalador
    artifacts\StrataUI-Setup-X.Y.Z.exe con Inno Setup.
.DESCRIPTION
    Requiere Inno Setup 6 (winget install JRSoftware.InnoSetup). La versión sale de
    Directory.Build.props. El .exe resultante es el que se adjunta a la publicación de GitHub
    para que el Hub lo encuentre al buscar actualizaciones.
.EXAMPLE
    .\scripts\build-installer.ps1
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
$version = $props.Project.PropertyGroup.Version
if (-not $version) { throw 'No se encontró la versión en Directory.Build.props.' }

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Falta Inno Setup 6: winget install JRSoftware.InnoSetup' }

$publish = Join-Path $root 'artifacts\publish'
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }

Write-Host "Publicando Strata UI $version..."
# ReadyToRun: el código ya va compilado para el procesador, así Strata UI arranca más rápido.
dotnet publish (Join-Path $root 'src\Strata.Shell\Strata.Shell.csproj') -c Release -r win-x64 --self-contained true `
    -p:PublishReadyToRun=true -p:DebugType=none -o $publish -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'La publicación falló.' }

# Menú contextual moderno: el servidor COM (junto a StrataUI.exe, mismo runtime) y su paquete firmado.
dotnet publish (Join-Path $root 'src\Strata.ContextMenu\Strata.ContextMenu.csproj') -c Release -r win-x64 --self-contained true `
    -p:PublishReadyToRun=true -p:DebugType=none -o $publish -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'La publicación del menú contextual falló.' }
& (Join-Path $PSScriptRoot 'build-context-menu.ps1') -OutDir $publish -Version $version
$cert = Get-PfxCertificate (Join-Path $publish 'StrataUI.cer')

# Módulos opcionales: no van en el instalador. Se adjuntan a la publicación de GitHub (vX.Y.Z) y se
# instalan desde Hub › Módulos opcionales (Services\Modules). Tienen que ser del mismo commit que la app.
$modules = @()
foreach ($project in 'Strata.Optimization', 'Strata.Files') {
    $moduleBuild = Join-Path $root 'artifacts\module-build'
    if (Test-Path $moduleBuild) { Remove-Item $moduleBuild -Recurse -Force }
    dotnet build (Join-Path $root "src\$project\$project.csproj") -c Release -o $moduleBuild -nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "La compilación del módulo $project falló." }
    $name = 'StrataUI.' + $project.Substring('Strata.'.Length) + '.dll'
    $module = Join-Path $root "artifacts\$name"
    Copy-Item (Join-Path $moduleBuild $name) $module -Force
    Remove-Item $moduleBuild -Recurse -Force
    $modules += $module
}

# Lecturas avanzadas: trae varias DLL (LibreHardwareMonitor, TraceEvent): va comprimido, solo con lo
# que la app no tiene ya.
$moduleBuild = Join-Path $root 'artifacts\module-build'
if (Test-Path $moduleBuild) { Remove-Item $moduleBuild -Recurse -Force }
# Publicado (no compilado): así las DLL de Windows de los paquetes (runtimes\win\...) quedan en la raíz,
# donde el proceso elevado las encuentra.
dotnet publish (Join-Path $root 'src\Strata.Sensors\Strata.Sensors.csproj') -c Release -r win-x64 --self-contained false -o $moduleBuild -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'La compilación del módulo Lecturas avanzadas falló.' }
$sensors = Join-Path $root 'artifacts\sensors-module'
if (Test-Path $sensors) { Remove-Item $sensors -Recurse -Force }
Get-ChildItem $moduleBuild -Recurse -File | Where-Object {
    $relative = $_.FullName.Substring($moduleBuild.Length + 1)
    $relative -notmatch '\.(pdb|deps\.json|runtimeconfig\.json)$' -and $relative -notmatch '^(x86|arm64)\\' `
        -and -not (Test-Path (Join-Path $publish $relative))
} | ForEach-Object {
    $target = Join-Path $sensors $_.FullName.Substring($moduleBuild.Length + 1)
    New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
    Copy-Item $_.FullName $target
}
$sensorsZip = Join-Path $root 'artifacts\StrataUI.Sensors.zip'
if (Test-Path $sensorsZip) { Remove-Item $sensorsZip -Force }
Compress-Archive -Path (Join-Path $sensors '*') -DestinationPath $sensorsZip
Remove-Item $moduleBuild, $sensors -Recurse -Force
$modules += $sensorsZip

Write-Host 'Generando el instalador...'
& $iscc /Q "/DAppVersion=$version" "/DSourceDir=$publish" "/DCertThumbprint=$($cert.Thumbprint)" (Join-Path $root 'installer\StrataUI.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup falló.' }

$setup = Join-Path $root "artifacts\StrataUI-Setup-$version.exe"
Write-Host ("Listo: {0} ({1:N1} MB)" -f $setup, ((Get-Item $setup).Length / 1MB))
Write-Host 'Módulos para adjuntar a la publicación:'
$modules | ForEach-Object { Write-Host ("  {0} ({1:N1} MB)" -f $_, ((Get-Item $_).Length / 1MB)) }
