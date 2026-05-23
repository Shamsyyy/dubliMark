$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root "src\DoubleMark.Desktop\bin\Debug\net8.0-windows\DoubleMark.Desktop.exe"

if (-not (Test-Path $exe)) {
    Push-Location $root
    dotnet build src\DoubleMark.Desktop\DoubleMark.Desktop.csproj
    Pop-Location
}

Stop-Process -Name "DoubleMark.Desktop" -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500
Start-Process -FilePath $exe
