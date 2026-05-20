param(
    [string]$Runtime = "win-x64",
    [switch]$SkipObfuscation,
    [switch]$SingleFile
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$solution = Join-Path $root "DoubleMark.sln"
$project = Join-Path $root "src/DoubleMark.Desktop/DoubleMark.Desktop.csproj"
$dist = Join-Path $root "dist/DoubleMark"
$obfuscated = Join-Path $root "dist/DoubleMark.obfuscated"

function Read-EnvFileValue([string]$Path, [string[]]$Names) {
    if (-not (Test-Path $Path)) {
        return $null
    }

    foreach ($line in Get-Content $Path) {
        $trimmed = $line.Trim()
        if ($trimmed.Length -eq 0 -or $trimmed.StartsWith("#")) {
            continue
        }

        $separator = $trimmed.IndexOf("=")
        if ($separator -le 0) {
            continue
        }

        $name = $trimmed.Substring(0, $separator).Trim()
        if ($Names -contains $name) {
            return $trimmed.Substring($separator + 1).Trim().Trim('"')
        }
    }

    return $null
}

function Read-JsonConfigValue([string]$Path, [string[]]$Names) {
    if (-not (Test-Path $Path)) {
        return $null
    }

    $json = Get-Content $Path -Raw | ConvertFrom-Json
    foreach ($name in $Names) {
        if ($name -eq "Supabase:Url" -and $json.Supabase.Url) {
            return [string]$json.Supabase.Url
        }

        if ($name -eq "Supabase:AnonKey" -and $json.Supabase.AnonKey) {
            return [string]$json.Supabase.AnonKey
        }

        if ($json.PSObject.Properties.Name -contains $name) {
            return [string]$json.$name
        }
    }

    return $null
}

Write-Host "Cleaning release output..."
Remove-Item -Recurse -Force $dist, $obfuscated -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $dist | Out-Null

dotnet clean $solution -c Release
dotnet restore $solution
dotnet build $solution -c Release --no-restore
$publishSingleFile = if ($SingleFile) { "true" } else { "false" }
$publishReadyToRun = if ($SkipObfuscation) { "true" } else { "false" }

dotnet publish $project -c Release -r $Runtime --self-contained true `
    -p:PublishSingleFile=$publishSingleFile `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:PublishReadyToRun=$publishReadyToRun `
    -p:PublishTrimmed=false `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $dist

$desktopExe = Join-Path $dist "DoubleMark.Desktop.exe"
$productExe = Join-Path $dist "DoubleMark.exe"
if (Test-Path $desktopExe) {
    Move-Item -Path $desktopExe -Destination $productExe -Force
}

$envFiles = @(
    (Join-Path $root ".env.local"),
    (Join-Path $root ".env")
)
$jsonFiles = @(
    (Join-Path $root "appsettings.local.json"),
    (Join-Path $root "appsettings.json")
)

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
    throw "Refusing to package a Supabase service_role key. Use only anon/public key."
}

if ($supabaseUrl -and $supabaseAnonKey) {
    $productionConfig = [ordered]@{
        Supabase = [ordered]@{
            Url = $supabaseUrl
            AnonKey = $supabaseAnonKey
        }
    }
    $productionConfig | ConvertTo-Json -Depth 4 | Set-Content -Path (Join-Path $dist "appsettings.json") -Encoding UTF8
    Write-Host "Generated production Supabase appsettings.json in dist."
} else {
    Write-Warning "Supabase URL/anon key were not found. The installed app will show login configuration error."
}

Get-ChildItem $dist -Recurse -Include *.pdb,.env,.env.local,appsettings.local.json,secrets.json -Force |
    Remove-Item -Force -ErrorAction SilentlyContinue

if (-not $SkipObfuscation) {
    $obfuscar = Get-Command obfuscar.console -ErrorAction SilentlyContinue
    if (-not $obfuscar) {
        $obfuscar = Get-Command obfuscar -ErrorAction SilentlyContinue
    }

    if ($obfuscar) {
        Write-Host "Running Obfuscar..."
        & $obfuscar.Source (Join-Path $root "obfuscar.xml")
        if ($LASTEXITCODE -ne 0) {
            throw "Obfuscar failed with exit code $LASTEXITCODE."
        }
        if (Test-Path $obfuscated) {
            Copy-Item -Path (Join-Path $obfuscated "*.dll") -Destination $dist -Force
        }
    } else {
        Write-Warning "Obfuscar is not installed. Install Obfuscar.Console or run with -SkipObfuscation."
    }
}

if ($env:SIGN_CERT_PATH -and $env:SIGN_CERT_PASSWORD) {
    $signtool = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($signtool) {
        & $signtool.Source sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 `
            /f $env:SIGN_CERT_PATH /p $env:SIGN_CERT_PASSWORD `
            (Join-Path $dist "DoubleMark.exe")
    } else {
        Write-Warning "signtool.exe not found; skipping code signing."
    }
} else {
    Write-Host "Code signing skipped. Set SIGN_CERT_PATH and SIGN_CERT_PASSWORD to sign the EXE."
}

Write-Host "Release EXE:"
Write-Host (Join-Path $dist "DoubleMark.exe")
