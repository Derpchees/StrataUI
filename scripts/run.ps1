<#
.SYNOPSIS
    Cierra Strata UI si está abierto, lo compila y lo vuelve a abrir.
.EXAMPLE
    .\scripts\run.ps1
    .\scripts\run.ps1 -Configuration Release
#>
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root "src\Strata.Shell\bin\$Configuration\net10.0-windows10.0.19041.0\StrataUI.exe"

# Cierre limpio: restaura la barra de Windows y libera el .exe para poder sobrescribirlo.
$running = Get-Process StrataUI -ErrorAction SilentlyContinue
if ($running) {
    if (Test-Path $exe) { Start-Process $exe -ArgumentList '--exit' }
    $running | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
}

dotnet build (Join-Path $root 'StrataUI.sln') -c $Configuration -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'La compilación falló.' }

Start-Process $exe
