# Las integraciones opcionales nunca deben bloquear el final del instalador.
param(
    [ValidateSet('Register', 'Remove', 'UpdateTask')]
    [string]$Action = 'Register',
    [switch]$Worker,
    [ValidateRange(1, 300)]
    [int]$TimeoutSeconds = 60
)

$ErrorActionPreference = 'Stop'
$logDir = Join-Path $env:LOCALAPPDATA 'StrataUI\logs'
New-Item -ItemType Directory -Path $logDir -Force | Out-Null
$logPath = Join-Path $logDir 'installer-integration.log'
function Write-Log([string]$Message) {
    Add-Content -LiteralPath $logPath -Value "$(Get-Date -Format o) [$Action] $Message" -Encoding UTF8
}

try {
    if (-not $Worker) {
        # Proceso separado: también se limita una llamada AppX que no devuelve el control.
        $start = New-Object System.Diagnostics.ProcessStartInfo
        $start.FileName = Join-Path $PSHOME 'powershell.exe'
        $start.Arguments = '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + $PSCommandPath + '" -Worker -Action ' + $Action
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $process = [System.Diagnostics.Process]::Start($start)
        try {
            if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
                $process.Kill()
                Write-Log "Se agotaron $TimeoutSeconds segundos; la instalación puede continuar."
                exit 2
            }
            exit $process.ExitCode
        }
        finally { $process.Dispose() }
    }

    Write-Log 'Inicio'
    if ($Action -eq 'UpdateTask') {
        # COM conserva el tipo de inicio de sesión y evita preguntas de contraseña de schtasks.
        $scheduler = New-Object -ComObject 'Schedule.Service'
        $scheduler.Connect()
        $folder = $scheduler.GetFolder('\')
        $name = "StrataUI ($env:USERNAME)"
        try { $task = $folder.GetTask($name) }
        catch { Write-Log 'La tarea no existe; no se modifica.'; exit 0 }
        $definition = $task.Definition
        $definition.Actions.Item(1).Path = Join-Path $PSScriptRoot 'StrataUI.exe'
        $folder.RegisterTaskDefinition($name, $definition, 4, $null, $null, $definition.Principal.LogonType) | Out-Null
    }
    else {
        Get-AppxPackage -Name StrataUI.ContextMenu -ErrorAction Stop |
            Remove-AppxPackage -Confirm:$false -ErrorAction Stop
        if ($Action -eq 'Register') {
            Add-AppxPackage -Path (Join-Path $PSScriptRoot 'StrataUI.ContextMenu.msix') -ExternalLocation $PSScriptRoot -ErrorAction Stop
        }
    }
    Write-Log 'Terminado'
    exit 0
}
catch {
    Write-Log $_.Exception.ToString()
    exit 1
}
