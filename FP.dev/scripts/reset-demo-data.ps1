[CmdletBinding()]
param(
    [ValidatePattern('^DoSelectDemo_[0-9a-fA-F]{32}$')]
    [string] $DatabaseName = "DoSelectDemo_$([Guid]::NewGuid().ToString('N'))"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

try {
    Assert-IsolatedDemoDatabaseName -DatabaseName $DatabaseName

    $processState = Read-ProcessState
    if ($null -ne $processState -and
        @(@($processState.Services) | Where-Object { Test-ServiceProcesses -Service $_ }).Count -gt 0) {
        throw "DoSelect managed services are running. Run '.\scripts\stop-all.ps1' before preparing new Demo data."
    }

    & (Join-Path $PSScriptRoot 'seed-demo-data.ps1') -DatabaseName $DatabaseName
    if ($LASTEXITCODE -ne 0) {
        throw 'Preparing isolated Demo data failed.'
    }

    & (Join-Path $PSScriptRoot 'validate-demo-data.ps1') -DatabaseName $DatabaseName
    if ($LASTEXITCODE -ne 0) {
        throw 'Validating isolated Demo data failed.'
    }

    Write-DemoDatabaseState -DatabaseName $DatabaseName
    Write-Host 'Isolated Demo data is ready.' -ForegroundColor Green
    Write-Host "Database: $DatabaseName"
    Write-Host ".\scripts\start-all.ps1 -Environment Demo -DatabaseName $DatabaseName"
    exit 0
}
catch {
    Write-Error $_.Exception.Message
    exit 1
}
