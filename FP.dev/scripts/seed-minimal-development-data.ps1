[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

$apiProject = Join-Path $script:ProjectRoot 'src\backend\DoSelect.Api'
$dotnet = Get-RequiredCommand -Name 'dotnet.exe'

$previousEnvironment = $env:ASPNETCORE_ENVIRONMENT
Push-Location $script:ProjectRoot
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
