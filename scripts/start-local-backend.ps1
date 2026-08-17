param(
    [switch]$SkipDocker,
    [switch]$SkipApi
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $root

if (-not (Test-Path ".env")) {
    Copy-Item ".env.example" ".env"
    Write-Host "Created .env from .env.example"
}

if (-not $SkipDocker) {
    Write-Host "Starting Postgres..."
    docker compose up -d
    if ($LASTEXITCODE -ne 0) {
        throw "Docker Compose failed. Start Docker Desktop and retry."
    }
}

if (-not $SkipApi) {
    Write-Host "Starting DoubleMark.Api on http://localhost:5080 ..."
    dotnet run --project (Join-Path $root "src\DoubleMark.Api\DoubleMark.Api.csproj")
}
