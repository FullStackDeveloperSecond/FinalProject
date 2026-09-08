[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

$dotnet = Get-RequiredCommand -Name 'dotnet.exe'
$sqlcmd = Get-SqlCmdCommand
if ($null -eq $sqlcmd) {
    throw 'Required command sqlcmd.exe was not found in ODBC 18 tools or PATH.'
}

$sqlStatus = Test-SqlServerConnection
if (-not $sqlStatus.IsReady) {
    throw $sqlStatus.Detail
}

foreach ($endpoint in @(
    @{ Port = 5126; ServiceName = 'API' }
    @{ Port = 5173; ServiceName = 'Customer Web' }
    @{ Port = 5174; ServiceName = 'Admin Web' }
)) {
    Assert-PortAvailable -Port $endpoint.Port -ServiceName $endpoint.ServiceName
}

$infrastructureProject = Join-Path $script:ProjectRoot 'src\backend\DoSelect.Infrastructure\DoSelect.Infrastructure.csproj'
$schemaVerification = Join-Path $script:ProjectRoot 'database-deploy\initial-create\verify.sql'
$seedVerification = Join-Path $script:ProjectRoot 'database-deploy\initial-create\verify-minimal-seed.sql'
$previousConnection = $env:ConnectionStrings__DefaultConnection

Push-Location $script:ProjectRoot
try {
    # Pin every child process to this clone's local Development database instead
    # of inheriting a user- or machine-level connection-string override.
    $env:ConnectionStrings__DefaultConnection = New-DevelopmentConnectionString

    & $dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'dotnet tool restore failed.' }

    & $dotnet tool run dotnet-ef -- database update `
        --project $infrastructureProject `
        --startup-project $infrastructureProject `
        --context DoSelectDbContext
    if ($LASTEXITCODE -ne 0) { throw 'Development database migration failed.' }

    & (Join-Path $PSScriptRoot 'seed-minimal-development-data.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Minimal development seed failed.' }

    & $sqlcmd -S $script:SqlInstance -d DoSelectDb -E -C -b -i $schemaVerification
    if ($LASTEXITCODE -ne 0) { throw 'Development schema verification failed.' }

    & $sqlcmd -S $script:SqlInstance -d DoSelectDb -E -C -b -i $seedVerification
    if ($LASTEXITCODE -ne 0) { throw 'Minimal development seed verification failed.' }

    & (Join-Path $PSScriptRoot 'smoke-api-database.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'API database smoke test failed.' }
}
finally {
    $env:ConnectionStrings__DefaultConnection = $previousConnection
    Pop-Location
}

Write-Host 'Development database migration, seed and verification passed.' -ForegroundColor Green
