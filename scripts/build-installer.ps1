param(
    [switch]$FolderPublish,
    [switch]$EnableObfuscation,
    [switch]$AllowUnsigned
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")

$params = @{}
if ($FolderPublish) { $params.FolderPublish = $true }
if ($EnableObfuscation) { $params.EnableObfuscation = $true }
& (Join-Path $PSScriptRoot "build-release.ps1") @params

$buildInfoPath = Join-Path $root "dist\DoubleMark\buildinfo.json"
if (Test-Path $buildInfoPath) {
    $buildInfo = Get-Content $buildInfoPath -Raw | ConvertFrom-Json
    if ($buildInfo.buildId) {
        $env:DOUBLEMARK_BUILD_ID = [string]$buildInfo.buildId
        Write-Host "Build ID: $env:DOUBLEMARK_BUILD_ID"
    }
}

$version = "2.1.0"
$propsPath = Join-Path $root "Directory.Build.props"
if (Test-Path $propsPath) {
    $props = Get-Content $propsPath -Raw
    if ($props -match '<Version>([^<]+)</Version>') {
        $version = $Matches[1].Trim()
    }
}

$isccPaths = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
)

$iscc = $null
foreach ($path in $isccPaths) {
    if (Test-Path $path) {
        $iscc = Get-Item $path
        break
    }
}

if (-not $iscc) {
    $isccCmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($isccCmd) { $iscc = $isccCmd }
}

if (-not $iscc) {
    throw "Inno Setup not found. Install Inno Setup 6 and retry."
}

$installerOut = Join-Path $root "dist\installer"
New-Item -ItemType Directory -Path $installerOut -Force | Out-Null

$buildId = ""
if ($env:DOUBLEMARK_BUILD_ID) {
    $buildId = "-$env:DOUBLEMARK_BUILD_ID"
}

& $iscc.FullName "/DMyAppVersion=$version" "/DMyAppBuildId=$buildId" (Join-Path $root "installer\DoubleMark.iss")
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE."
}

$installer = Get-ChildItem -Path $installerOut -Filter "DoubleMarkSetup-$version*.exe" -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $installer) {
    throw "Installer not found in $installerOut"
}

if ($env:SIGN_CERT_PATH -and $env:SIGN_CERT_PASSWORD) {
    $signtool = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($signtool) {
        & $signtool.Source sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 `
            /f $env:SIGN_CERT_PATH /p $env:SIGN_CERT_PASSWORD $installer.FullName
    }
}

$hash = (Get-FileHash -Path $installer.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
$installerFileName = $installer.Name
$stableInstallerName = "DoubleMarkSetup-$version.exe"
$downloadsDir = Join-Path $root "dist\downloads"
New-Item -ItemType Directory -Path $downloadsDir -Force | Out-Null
Copy-Item -Path $installer.FullName -Destination (Join-Path $downloadsDir $stableInstallerName) -Force
Write-Host "Stable copy for site: $(Join-Path $downloadsDir $stableInstallerName)"

$updatesDir = Join-Path $root "dist\updates"
New-Item -ItemType Directory -Path $updatesDir -Force | Out-Null

$notesFile = Join-Path $root "updates\release-notes-$version.json"
$releaseNotes = if (Test-Path $notesFile) {
    Get-Content $notesFile -Raw -Encoding UTF8 | ConvertFrom-Json
} else {
    @("DoubleMark $version", "In-app updates and Chestny ZNAK scan/print")
}
if ($env:DOUBLEMARK_BUILD_ID) {
    $releaseNotes += "Build ID: $env:DOUBLEMARK_BUILD_ID"
}

$publishedAt = (Get-Date).ToUniversalTime().ToString("o")
$downloadUrl = "https://doublemark.ru/downloads/$stableInstallerName"
$fallbackUrl = "https://shamsyyy.github.io/doublemarksite/downloads/$stableInstallerName"

$updateManifest = [ordered]@{
    version             = $version
    publishedAt         = $publishedAt
    releaseDate         = (Get-Date).ToString("yyyy-MM-dd")
    mandatory           = $false
    title               = "DoubleMark $version"
    notes               = $releaseNotes
    downloadUrl         = $downloadUrl
    installerUrl        = $downloadUrl
    sha256              = $hash
    minSupportedVersion = "2.0.0"
    requireSignature    = $false
}

$updateJsonPath = Join-Path $updatesDir "update.json"
$repoUpdateJsonPath = Join-Path $root "updates\update.json"
$json = $updateManifest | ConvertTo-Json -Depth 6
$json | Set-Content -Path $updateJsonPath -Encoding UTF8
$json | Set-Content -Path $repoUpdateJsonPath -Encoding UTF8

$verifyParams = @{
    ReleaseRoot   = Join-Path $root "dist\DoubleMark"
    InstallerPath = $installer.FullName
}
if ($AllowUnsigned -or (-not $env:SIGN_CERT_PATH)) {
    $verifyParams.AllowUnsigned = $true
}

& (Join-Path $PSScriptRoot "verify-release-signatures.ps1") @verifyParams

Write-Host ""
Write-Host "Installer ready: $($installer.FullName)"
Write-Host "update.json: $updateJsonPath"
Write-Host "update.json (repo): $repoUpdateJsonPath"
Write-Host "SHA256: $hash"
Write-Host "downloadUrl: $downloadUrl"
Write-Host "fallback mirror: $fallbackUrl"
