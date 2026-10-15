<#
.SYNOPSIS
    Arma y firma el paquete sparse del menú contextual moderno (StrataUI.ContextMenu.msix) y
    exporta el certificado público (StrataUI.cer) en la carpeta indicada.
.DESCRIPTION
    makeappx y signtool salen del paquete oficial Microsoft.Windows.SDK.BuildTools de NuGet
    (se descarga una vez en artifacts\tools). El certificado es autofirmado ("CN=Strata UI") y su
    clave privada vive solo en Cert:\CurrentUser\My de quien construye; si no existe se crea. El
    instalador confía en el .cer (Personas de confianza del equipo) y registra el paquete.
.EXAMPLE
    .\scripts\build-context-menu.ps1 -OutDir artifacts\publish -Version 2.0.0
#>
param(
    [Parameter(Mandatory)] [string]$OutDir,
    [Parameter(Mandatory)] [string]$Version
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$subject = 'CN=Strata UI'

# ── Herramientas ────────────────────────────────────────────────────────────
$toolsVersion = '10.0.28000.2270'
$tools = Join-Path $root "artifacts\tools\sdk-buildtools-$toolsVersion"
$bin = Get-ChildItem (Join-Path $tools 'pkg\bin') -Directory -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $bin) {
    Write-Host "Descargando Microsoft.Windows.SDK.BuildTools $toolsVersion..."
    New-Item -ItemType Directory -Force $tools | Out-Null
    $nupkg = Join-Path $tools 'pkg.zip'
    $ProgressPreference = 'SilentlyContinue'
    Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/microsoft.windows.sdk.buildtools/$toolsVersion/microsoft.windows.sdk.buildtools.$toolsVersion.nupkg" -OutFile $nupkg
    Expand-Archive $nupkg (Join-Path $tools 'pkg') -Force
    $bin = Get-ChildItem (Join-Path $tools 'pkg\bin') -Directory | Select-Object -First 1
}
$makeappx = Join-Path $bin.FullName 'x64\makeappx.exe'
$signtool = Join-Path $bin.FullName 'x64\signtool.exe'
foreach ($exe in $makeappx, $signtool) {
    if ((Get-AuthenticodeSignature $exe).SignerCertificate.Subject -notlike 'CN=Microsoft Corporation*') {
        throw "$exe no está firmado por Microsoft."
    }
}

# ── Certificado ─────────────────────────────────────────────────────────────
$cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $subject -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date).AddMonths(1) } |
    Sort-Object NotAfter -Descending | Select-Object -First 1
if (-not $cert) {
    Write-Host "Creando el certificado $subject..."
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $subject -CertStoreLocation Cert:\CurrentUser\My `
        -KeyUsage DigitalSignature -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -NotAfter (Get-Date).AddYears(15) `
        -TextExtension @('2.5.29.19={text}')
}

# ── Contenido del paquete: manifiesto con la versión y logos sacados de docs\logo.png ──
$layout = Join-Path $root 'artifacts\context-menu-layout'
if (Test-Path $layout) { Remove-Item $layout -Recurse -Force }
New-Item -ItemType Directory -Force (Join-Path $layout 'Assets') | Out-Null
$manifest = Get-Content (Join-Path $root 'installer\context-menu\AppxManifest.xml') -Raw
$manifest = $manifest.Replace('$VERSION$', "$Version.0")
[IO.File]::WriteAllText((Join-Path $layout 'AppxManifest.xml'), $manifest, (New-Object Text.UTF8Encoding $false))

Add-Type -AssemblyName System.Drawing
$logo = [System.Drawing.Image]::FromFile((Join-Path $root 'docs\logo.png'))
foreach ($asset in @(@('StoreLogo.png', 50), @('Square150x150Logo.png', 150), @('Square44x44Logo.png', 44))) {
    $bmp = New-Object System.Drawing.Bitmap $asset[1], $asset[1]
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.DrawImage($logo, 0, 0, $asset[1], $asset[1])
    $g.Dispose()
    $bmp.Save((Join-Path $layout "Assets\$($asset[0])"), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}
$logo.Dispose()

# ── Empaquetar, firmar y exportar el certificado público ───────────────────
New-Item -ItemType Directory -Force $OutDir | Out-Null
$msix = Join-Path $OutDir 'StrataUI.ContextMenu.msix'
& $makeappx pack /o /nv /d $layout /p $msix | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'makeappx falló.' }
& $signtool sign /q /fd SHA256 /sha1 $cert.Thumbprint $msix
if ($LASTEXITCODE -ne 0) { throw 'signtool falló.' }
Export-Certificate -Cert $cert -FilePath (Join-Path $OutDir 'StrataUI.cer') -Type CERT | Out-Null
Write-Host "Paquete del menú contextual: $msix (certificado $($cert.Thumbprint))"
