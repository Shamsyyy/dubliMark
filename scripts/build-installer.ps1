param(
    [switch]$FolderPublish,
    [switch]$SkipObfuscation
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")

$params = @{}
if ($FolderPublish) { $params.FolderPublish = $true }
if ($SkipObfuscation) { $params.SkipObfuscation = $true }
& (Join-Path $PSScriptRoot "build-release.ps1") @params

$buildInfoPath = Join-Path $root "dist\DoubleMark\buildinfo.json"
if (Test-Path $buildInfoPath) {
    $buildInfo = Get-Content $buildInfoPath -Raw | ConvertFrom-Json
    if ($buildInfo.buildId) {
        $env:DOUBLEMARK_BUILD_ID = [string]$buildInfo.buildId
        Write-Host "Build ID: $env:DOUBLEMARK_BUILD_ID"
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
    throw "Inno Setup не найден. Установите Inno Setup 6 и повторите сборку."
}

$installerOut = Join-Path $root "dist\installer"
New-Item -ItemType Directory -Path $installerOut -Force | Out-Null

$buildId = ""
if ($env:DOUBLEMARK_BUILD_ID) {
    $buildId = "-$env:DOUBLEMARK_BUILD_ID"
}

& $iscc.FullName "/DMyAppBuildId=$buildId" (Join-Path $root "installer\DoubleMark.iss")

$installer = Get-ChildItem -Path $installerOut -Filter "DoubleMarkSetup-*.exe" -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $installer) {
    throw "Установщик не найден в $installerOut"
}

if ($env:SIGN_CERT_PATH -and $env:SIGN_CERT_PASSWORD) {
    $signtool = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($signtool) {
        & $signtool.Source sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 `
            /f $env:SIGN_CERT_PATH /p $env:SIGN_CERT_PASSWORD $installer.FullName
    }
}

$version = "2.1.0"
$csproj = Join-Path $root "src\DoubleMark.Desktop\DoubleMark.Desktop.csproj"
$csprojText = Get-Content $csproj -Raw
if ($csprojText -match '<Version>([^<]+)</Version>') {
    $version = $Matches[1]
}
if (Test-Path (Join-Path $root "Directory.Build.props")) {
    $props = Get-Content (Join-Path $root "Directory.Build.props") -Raw
    if ($props -match '<Version>([^<]+)</Version>') {
        $version = $Matches[1]
    }
}

$hash = (Get-FileHash -Path $installer.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
$installerFileName = $installer.Name
$stableInstallerName = "DoubleMarkSetup-$version.exe"
$downloadsDir = Join-Path $root "dist\downloads"
New-Item -ItemType Directory -Path $downloadsDir -Force | Out-Null
Copy-Item -Path $installer.FullName -Destination (Join-Path $downloadsDir $stableInstallerName) -Force
Write-Host "Stable copy for site: $(Join-Path $downloadsDir $stableInstallerName)"

$buildId = ""
if ($env:DOUBLEMARK_BUILD_ID) { $buildId = [string]$env:DOUBLEMARK_BUILD_ID }

$updatesDir = Join-Path $root "dist\updates"
New-Item -ItemType Directory -Path $updatesDir -Force | Out-Null

$notesFile = Join-Path $root "updates\release-notes-$version.json"
$releaseNotes = if (Test-Path $notesFile) {
    Get-Content $notesFile -Raw -Encoding UTF8 | ConvertFrom-Json
} else {
    @("Сборка DoubleMark $version", "Проверка и установка обновлений из приложения")
}
if ($buildId) {
    $releaseNotes += "Build ID: $buildId"
}

$updateManifest = [ordered]@{
    version = $version
    releaseDate = (Get-Date).ToString("yyyy-MM-dd")
    mandatory = $false
    title = "DoubleMark $version"
    notes = $releaseNotes
    installerUrl = "https://shamsyyy.github.io/doublemarksite/downloads/$stableInstallerName"
    sha256 = $hash
    minSupportedVersion = "2.0.0"
}

$updateJsonPath = Join-Path $updatesDir "update.json"
$repoUpdateJsonPath = Join-Path $root "updates\update.json"
$updateManifest | ConvertTo-Json -Depth 6 | Set-Content -Path $updateJsonPath -Encoding UTF8
$updateManifest | ConvertTo-Json -Depth 6 | Set-Content -Path $repoUpdateJsonPath -Encoding UTF8

Write-Host ""
Write-Host "Установщик готов: $($installer.FullName)"
Write-Host "update.json готов: $updateJsonPath"
Write-Host "update.json (repo, для сайта): $repoUpdateJsonPath"
Write-Host "SHA256: $hash"
