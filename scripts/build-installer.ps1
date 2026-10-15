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

Write-Host 'Generando el instalador...'
& $iscc /Q "/DAppVersion=$version" "/DSourceDir=$publish" (Join-Path $root 'installer\StrataUI.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup falló.' }

$setup = Join-Path $root "artifacts\StrataUI-Setup-$version.exe"
Write-Host ("Listo: {0} ({1:N1} MB)" -f $setup, ((Get-Item $setup).Length / 1MB))
