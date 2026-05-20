# Synthetic DataMatrix PNGs for integrity QA (not production codes).
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

dotnet test (Join-Path $root "tests\DoubleMark.Core.Tests\DoubleMark.Core.Tests.csproj") `
    --filter "FullyQualifiedName~Generate_mutation_datamatrix_pngs" `
    --no-restore:$false

$out = Join-Path $root "artifacts\mutation-samples"
Write-Host "PNG samples: $out"
if (Test-Path (Join-Path $out "index.txt")) {
    Get-Content (Join-Path $out "index.txt") | Select-Object -First 15
}
