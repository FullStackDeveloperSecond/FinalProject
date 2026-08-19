[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$apiProject = Join-Path $projectRoot 'src\backend\DoSelect.Api'
$dotnet = 'C:\Users\alexy\.dotnet\dotnet.exe'

if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) {
    throw "The pinned .NET SDK executable was not found at '$dotnet'."
}

$previousEnvironment = $env:ASPNETCORE_ENVIRONMENT
Push-Location $projectRoot
try {
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    & $dotnet run --project $apiProject --no-launch-profile -- --seed-minimal
    if ($LASTEXITCODE -ne 0) {
        throw 'Minimal development seed failed.'
    }
}
finally {
    $env:ASPNETCORE_ENVIRONMENT = $previousEnvironment
    Pop-Location
}
