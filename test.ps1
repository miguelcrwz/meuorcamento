# Runs every Meu Orcamento check (behavior, screens and fonts).
#   .\test.ps1         -> runs the tests; results go to Tests\output\
#   .\test.ps1 -Real   -> also captures the screens of a COPY of your real data (Tests\output-real\)
# The command exits with an error if any check fails.
param([switch]$Real)

$tests = Join-Path $PSScriptRoot 'Tests'
dotnet run --project $tests -c Release
$failures = $LASTEXITCODE

if ($Real) {
    # same lookup as the app: the saved location (e.g. OneDrive) when there is one, otherwise the default
    $data = Join-Path $env:APPDATA 'MeuOrcamento\dados.json'
    $location = Join-Path $env:APPDATA 'MeuOrcamento\data-location.txt'
    if (Test-Path $location) { $data = (Get-Content $location -Raw -Encoding UTF8).Trim() }
    if (Test-Path $data) { dotnet run --project $tests -c Release --no-build -- real $data }
    else { Write-Host "No real data at $data" }
}

exit $failures
