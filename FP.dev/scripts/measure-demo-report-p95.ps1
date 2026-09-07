[CmdletBinding()]
param(
    [ValidatePattern('^DoSelectDemo(?:_[0-9a-fA-F]{32})?$')]
    [string]$DatabaseName = 'DoSelectDemo'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$apiProject = Join-Path $projectRoot 'src\backend\DoSelect.Api'
$previousEnvironment = $env:ASPNETCORE_ENVIRONMENT
$previousConnection = $env:ConnectionStrings__DefaultConnection

Push-Location $projectRoot
try {
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:ConnectionStrings__DefaultConnection =
        "Server=.\SQL2025;Database=$DatabaseName;Integrated Security=True;TrustServerCertificate=True"

    & dotnet run --configuration Release --project $apiProject --no-launch-profile -- --benchmark-demo-reports
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}
finally {
    $env:ASPNETCORE_ENVIRONMENT = $previousEnvironment
    $env:ConnectionStrings__DefaultConnection = $previousConnection
    Pop-Location
}
