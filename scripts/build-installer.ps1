param(
    [switch]$SkipObfuscation
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")

if ($SkipObfuscation) {
    & (Join-Path $PSScriptRoot "build-release.ps1") -SkipObfuscation
} else {
    & (Join-Path $PSScriptRoot "build-release.ps1")
}

$iscc = Get-Command ISCC.exe -ErrorAction SilentlyContinue
if (-not $iscc) {
    $default = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
    if (Test-Path $default) {
        $iscc = Get-Item $default
    }
}

if (-not $iscc) {
    throw "Inno Setup compiler ISCC.exe was not found. Install Inno Setup 6, then rerun this script."
}

& $iscc.FullName (Join-Path $root "installer/DoubleMark.iss")

$installer = Join-Path $root "installer/Output/DoubleMarkSetup-2.1.0.exe"
if ($env:SIGN_CERT_PATH -and $env:SIGN_CERT_PASSWORD) {
    $signtool = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($signtool -and (Test-Path $installer)) {
        & $signtool.Source sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 `
            /f $env:SIGN_CERT_PATH /p $env:SIGN_CERT_PASSWORD $installer
    }
}

Write-Host "Installer:"
Write-Host $installer
