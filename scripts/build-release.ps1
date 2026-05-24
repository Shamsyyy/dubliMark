param(
    [string]$Runtime = "win-x64",
    [switch]$FolderPublish,
    [switch]$EnableObfuscation
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$solution = Join-Path $root "DoubleMark.sln"
$project = Join-Path $root "src\DoubleMark.Desktop\DoubleMark.Desktop.csproj"
$dist = Join-Path $root "dist\DoubleMark"
$obfuscated = Join-Path $root "dist\DoubleMark.obfuscated"

function Read-EnvFileValue([string]$Path, [string[]]$Names) {
    if (-not (Test-Path $Path)) { return $null }
    foreach ($line in Get-Content $Path) {
        $trimmed = $line.Trim()
        if ($trimmed.Length -eq 0 -or $trimmed.StartsWith("#")) { continue }
        $separator = $trimmed.IndexOf("=")
        if ($separator -le 0) { continue }
        $name = $trimmed.Substring(0, $separator).Trim()
        if ($Names -contains $name) {
            return $trimmed.Substring($separator + 1).Trim().Trim('"')
        }
    }
    return $null
}

function Read-JsonConfigValue([string]$Path, [string[]]$Names) {
    if (-not (Test-Path $Path)) { return $null }
    $json = Get-Content $Path -Raw | ConvertFrom-Json
    foreach ($name in $Names) {
        if ($name -eq "Supabase:Url" -and $json.Supabase.Url) { return [string]$json.Supabase.Url }
        if ($name -eq "Supabase:AnonKey" -and $json.Supabase.AnonKey) { return [string]$json.Supabase.AnonKey }
        if ($json.PSObject.Properties.Name -contains $name) { return [string]$json.$name }
    }
    return $null
}

Write-Host "Sync branding icons..."
$branding = Join-Path $root "src\DoubleMark.Desktop\Assets\Branding"
New-Item -ItemType Directory -Path $branding -Force | Out-Null
Copy-Item -Path (Join-Path $root "ico\*") -Destination $branding -Force

Write-Host "Cleaning release output..."
Remove-Item -Recurse -Force $dist, $obfuscated -ErrorAction SilentlyContinue
Get-ChildItem -Path $root -Recurse -Directory -Filter bin -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match '\\(src|tests)\\' } |
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
Get-ChildItem -Path $root -Recurse -Directory -Filter obj -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match '\\(src|tests)\\' } |
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $dist -Force | Out-Null

Set-Location $root
dotnet restore $solution
dotnet build $solution -c Release

$publishSingleFile = if ($FolderPublish) { "false" } else { "true" }
$publishReadyToRun = if ($FolderPublish -and $EnableObfuscation) { "false" } else { "true" }

dotnet publish $project `
    -c Release `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=$publishSingleFile `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:PublishReadyToRun=$publishReadyToRun `
    -p:PublishTrimmed=false `
    -o $dist

$desktopExe = Join-Path $dist "DoubleMark.Desktop.exe"
$productExe = Join-Path $dist "DoubleMark.exe"
if (Test-Path $desktopExe) {
    if (Test-Path $productExe) {
        Remove-Item -Path $productExe -Force -ErrorAction Stop
    }
    Move-Item -Path $desktopExe -Destination $productExe -Force
}

$envFiles = @((Join-Path $root ".env.local"), (Join-Path $root ".env"))
$jsonFiles = @((Join-Path $root "appsettings.local.json"), (Join-Path $root "appsettings.json"))

$supabaseUrl = $env:SUPABASE_URL
if (-not $supabaseUrl) { $supabaseUrl = $env:VITE_SUPABASE_URL }
$supabaseAnonKey = $env:SUPABASE_ANON_KEY
if (-not $supabaseAnonKey) { $supabaseAnonKey = $env:VITE_SUPABASE_ANON_KEY }

foreach ($file in $envFiles) {
    if (-not $supabaseUrl) { $supabaseUrl = Read-EnvFileValue $file @("SUPABASE_URL", "VITE_SUPABASE_URL") }
    if (-not $supabaseAnonKey) { $supabaseAnonKey = Read-EnvFileValue $file @("SUPABASE_ANON_KEY", "VITE_SUPABASE_ANON_KEY") }
}
foreach ($file in $jsonFiles) {
    if (-not $supabaseUrl) { $supabaseUrl = Read-JsonConfigValue $file @("SUPABASE_URL", "VITE_SUPABASE_URL", "Supabase:Url") }
    if (-not $supabaseAnonKey) { $supabaseAnonKey = Read-JsonConfigValue $file @("SUPABASE_ANON_KEY", "VITE_SUPABASE_ANON_KEY", "Supabase:AnonKey") }
}

if ($supabaseAnonKey -and $supabaseAnonKey.Contains("service_role")) {
    throw "Refusing to package Supabase service_role key. Use only anon/public key."
}

$buildUtc = (Get-Date).ToUniversalTime().ToString("o")
$buildId = (Get-Date).ToString("yyyyMMdd-HHmmss")

if ($supabaseUrl -and $supabaseAnonKey) {
    [ordered]@{ Supabase = [ordered]@{ Url = $supabaseUrl; AnonKey = $supabaseAnonKey } } |
        ConvertTo-Json -Depth 4 |
        Set-Content -Path (Join-Path $dist "appsettings.json") -Encoding UTF8
    Write-Host "Generated dist\DoubleMark\appsettings.json (Supabase anon key only)."
} else {
    Write-Warning "Supabase URL/anon key not found. Installed app will show configuration error on login."
}

function Get-ProjectVersion {
    param([string]$DefaultVersion = "2.1.0")
    $version = $DefaultVersion
    $propsPath = Join-Path $root "Directory.Build.props"
    if (Test-Path $propsPath) {
        $props = Get-Content $propsPath -Raw
        if ($props -match '<Version>([^<]+)</Version>') {
            return $Matches[1].Trim()
        }
    }
    $csproj = Get-Content $project -Raw
    if ($csproj -match '<Version>([^<]+)</Version>') {
        return $Matches[1].Trim()
    }
    return $version
}

$version = Get-ProjectVersion

[ordered]@{
    version = $version
    buildUtc = $buildUtc
    buildId = $buildId
    autoUpdateAvailable = $false
} | ConvertTo-Json -Depth 4 | Set-Content -Path (Join-Path $dist "buildinfo.json") -Encoding UTF8
Write-Host "Generated dist\DoubleMark\buildinfo.json (build $buildId)."

$versionTxt = @(
    "DoubleMark release",
    "Version: $version",
    "BuildId: $buildId",
    "BuildUtc: $buildUtc",
    "Exe: $productExe"
) -join [Environment]::NewLine
Set-Content -Path (Join-Path $dist "VERSION.txt") -Value $versionTxt -Encoding UTF8

if (Test-Path $productExe) {
    $fvi = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($productExe)
    Write-Host "Verified exe ProductVersion: $($fvi.ProductVersion)"
    Write-Host "Verified exe FileVersion: $($fvi.FileVersion)"
    if ($fvi.ProductVersion -notlike "$version*") {
        throw "EXE version mismatch: expected $version, got $($fvi.ProductVersion)"
    }
}

$forbiddenPatterns = @("service_role", "refresh_token", "access_token", "private key", "smtp", "webhook", "password")
$forbiddenFiles = @("*.pdb", ".env", ".env.local", "appsettings.local.json", "secrets.json")
Get-ChildItem $dist -Recurse -Include $forbiddenFiles -Force -ErrorAction SilentlyContinue |
    Remove-Item -Force -ErrorAction SilentlyContinue

$warnings = @()
foreach ($file in Get-ChildItem $dist -Recurse -File -ErrorAction SilentlyContinue) {
    if ($file.Extension -in ".exe", ".dll") { continue }
    $text = Get-Content $file.FullName -Raw -ErrorAction SilentlyContinue
    if (-not $text) { continue }
    foreach ($pattern in $forbiddenPatterns) {
        if ($text -match [regex]::Escape($pattern)) {
            $warnings += "Suspicious content '$pattern' in $($file.FullName)"
        }
    }
}
if ($warnings.Count -gt 0) {
    $warnings | ForEach-Object { Write-Warning $_ }
}

if ($EnableObfuscation -and $FolderPublish) {
    $obfuscar = Get-Command obfuscar.console -ErrorAction SilentlyContinue
    if (-not $obfuscar) { $obfuscar = Get-Command obfuscar -ErrorAction SilentlyContinue }
    if ($obfuscar) {
        Write-Warning "Obfuscar enabled - may increase Defender false positives. Use only with QA."
        & $obfuscar.Source (Join-Path $root "obfuscar.xml")
        if ($LASTEXITCODE -ne 0) { throw "Obfuscar failed with exit code $LASTEXITCODE." }
        if (Test-Path $obfuscated) {
            Copy-Item -Path (Join-Path $obfuscated "DoubleMark.Core.dll") -Destination $dist -Force
        }
    } else {
        throw "Obfuscar requested but not installed."
    }
} elseif ($EnableObfuscation) {
    Write-Warning "Obfuscation requires -FolderPublish. Skipping."
}

if (-not (Test-Path $productExe)) {
    throw "Release EXE was not created: $productExe"
}

if ($env:SIGN_CERT_PATH -and $env:SIGN_CERT_PASSWORD) {
    $signtool = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($signtool) {
        $signArgs = @(
            "sign", "/fd", "SHA256",
            "/tr", "http://timestamp.digicert.com", "/td", "SHA256",
            "/f", $env:SIGN_CERT_PATH, "/p", $env:SIGN_CERT_PASSWORD
        )
        & $signtool.Source @signArgs $productExe
        Get-ChildItem $dist -Recurse -Include *.dll -File -ErrorAction SilentlyContinue |
            ForEach-Object { & $signtool.Source @signArgs $_.FullName }
    }
}

Write-Host ""
Write-Host "Release EXE ready: $productExe"
