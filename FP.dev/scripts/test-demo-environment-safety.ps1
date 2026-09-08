[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

$validDatabaseName = "DoSelectDemo_$([Guid]::NewGuid().ToString('N'))"
Assert-IsolatedDemoDatabaseName -DatabaseName $validDatabaseName

$connectionString = New-DemoConnectionString -DatabaseName $validDatabaseName
if ($connectionString -notlike "Server=.\SQL2025;Database=$validDatabaseName;*") {
    throw 'The Demo connection string did not bind the expected local SQL Server and isolated database.'
}

foreach ($rejectedDatabaseName in @(
    'DoSelectDb'
    'DoSelectDemo'
    'DoSelectDemo_not-a-guid'
    'DoSelectDemo_00000000000000000000000000000000;Encrypt=False'
)) {
    $wasRejected = $false
    try {
        Assert-IsolatedDemoDatabaseName -DatabaseName $rejectedDatabaseName
    }
    catch {
        $wasRejected = $_.Exception.Message -like 'Demo runtime requires an isolated database*'
    }

    if (-not $wasRejected) {
        throw "Unsafe Demo database name was accepted: $rejectedDatabaseName"
    }
}

$scriptSources = @{}
foreach ($scriptName in @('reset-demo-data.ps1', 'start-all.ps1')) {
    $scriptPath = Join-Path $PSScriptRoot $scriptName
    $tokens = $null
    $parseErrors = $null
    $null = [Management.Automation.Language.Parser]::ParseFile(
        $scriptPath,
        [ref] $tokens,
        [ref] $parseErrors)
    if ($parseErrors.Count -ne 0) {
        throw "$scriptName has PowerShell parse errors: $($parseErrors.Message -join '; ')"
    }

    $scriptSources[$scriptName] = Get-Content -Raw -LiteralPath $scriptPath
}

$startSource = $scriptSources['start-all.ps1']
if ($startSource -notmatch '(?m)\$env:ConnectionStrings__DefaultConnection\s*=\s*\$demoConnectionString') {
    throw 'start-all.ps1 does not bind the isolated Demo connection string before starting the API.'
}
if ($startSource -notmatch 'Read-DemoDatabaseState' -or
    $startSource -notmatch 'New-DemoConnectionString') {
    throw 'start-all.ps1 does not resolve and validate prepared Demo database state.'
}

$resetSource = $scriptSources['reset-demo-data.ps1']
foreach ($requiredCall in @('seed-demo-data.ps1', 'validate-demo-data.ps1', 'Write-DemoDatabaseState')) {
    if ($resetSource -notmatch [Regex]::Escape($requiredCall)) {
        throw "reset-demo-data.ps1 is missing required operation: $requiredCall"
    }
}
if ($resetSource -notmatch 'Test-ServiceProcesses') {
    throw 'reset-demo-data.ps1 does not reject preparation while managed services are running.'
}

Write-Output 'Demo environment safety tests passed: 1 allowed and 4 rejected database names; 2 scripts parsed and runtime bindings verified.'
