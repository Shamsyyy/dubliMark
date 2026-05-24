param(
    [string]$ReleaseRoot = "",
    [string]$InstallerPath = "",
    [switch]$AllowUnsigned
)

$ErrorActionPreference = "Stop"

function Test-AuthenticodeSignature {
    param([string]$FilePath)

    if (-not (Test-Path $FilePath)) {
        return [pscustomobject]@{ Path = $FilePath; Signed = $false; Status = "Missing" }
    }

    $sig = Get-AuthenticodeSignature -FilePath $FilePath
    $signed = $sig.Status -eq "Valid"
    return [pscustomobject]@{
        Path = $FilePath
        Signed = $signed
        Status = [string]$sig.Status
        Signer = if ($sig.SignerCertificate) { $sig.SignerCertificate.Subject } else { "" }
    }
}

$targets = New-Object System.Collections.Generic.List[string]

if ($ReleaseRoot -and (Test-Path $ReleaseRoot)) {
    Get-ChildItem -Path $ReleaseRoot -Recurse -File -Include *.exe, *.dll |
        ForEach-Object { $targets.Add($_.FullName) }
}

if ($InstallerPath -and (Test-Path $InstallerPath)) {
    if (-not $targets.Contains($InstallerPath)) {
        $targets.Add((Resolve-Path $InstallerPath).Path)
    }
}

if ($targets.Count -eq 0) {
    throw "No release files to verify. Pass -ReleaseRoot and/or -InstallerPath."
}

$unsigned = @()
$results = @()

foreach ($path in $targets) {
    $result = Test-AuthenticodeSignature -FilePath $path
    $results += $result

    $icon = if ($result.Signed) { "[OK]" } else { "[FAIL]" }
    Write-Host "$icon $($result.Status) $path"
    if ($result.Signer) {
        Write-Host "     Signer: $($result.Signer)"
    }

    if (-not $result.Signed) {
        $unsigned += $path
    }
}

if ($unsigned.Count -gt 0) {
    if ($AllowUnsigned) {
        Write-Warning "Unsigned production files (allowed for dev build):"
        $unsigned | ForEach-Object { Write-Warning "  $_" }
        exit 0
    }

    Write-Error @"
The following production binaries are not Authenticode-signed:
$($unsigned -join [Environment]::NewLine)

Set SIGN_CERT_PATH and SIGN_CERT_PASSWORD, re-run build-release.ps1 / build-installer.ps1,
or pass -AllowUnsigned only for local development.
"@
}

Write-Host ""
Write-Host "All $($results.Count) production file(s) have valid Authenticode signatures."
