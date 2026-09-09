[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

$connectionString = New-DevelopmentConnectionString
foreach ($requiredFragment in @(
    'Server=.\SQL2025'
    'Database=DoSelectDb'
    'Integrated Security=True'
)) {
    if ($connectionString.IndexOf($requiredFragment, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
        throw "Development connection string is missing '$requiredFragment'."
    }
}

foreach ($forbiddenFragment in @('User ID=', 'Password=')) {
    if ($connectionString.IndexOf($forbiddenFragment, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
        throw "Development connection string must not contain '$forbiddenFragment'."
    }
}

$scriptNames = @(
    'initialize-development-database.ps1'
    'seed-minimal-development-data.ps1'
    'smoke-api-database.ps1'
    'start-all.ps1'
)
$scriptContents = @{}
foreach ($scriptName in $scriptNames) {
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

    $content = Get-Content -Raw -LiteralPath $scriptPath
    if (-not $content.Contains('$env:ConnectionStrings__DefaultConnection = New-DevelopmentConnectionString')) {
        throw "$scriptName must explicitly bind child processes to New-DevelopmentConnectionString."
    }
    $scriptContents[$scriptName] = $content
}

foreach ($scriptName in @('smoke-api-database.ps1', 'start-all.ps1')) {
    if ($scriptContents[$scriptName].IndexOf('`"$apiProject`"', [StringComparison]::Ordinal) -lt 0) {
        throw "$scriptName must quote the API project path passed to Start-Process."
    }
}

$schemaVerification = Get-Content -Raw -LiteralPath (
    Join-Path $script:ProjectRoot 'database-deploy\initial-create\verify.sql')
foreach ($requiredSchemaMarker in @(
    'IF @ApplicationTableCount <> 106'
    'IF @ExplicitIndexCount <> 357'
    "MigrationId = N'20260909040927_AddGuestCheckoutEmailVerification'"
)) {
    if ($schemaVerification.IndexOf($requiredSchemaMarker, [StringComparison]::Ordinal) -lt 0) {
        throw "Development schema verification is missing '$requiredSchemaMarker'."
    }
}

$initializer = $scriptContents['initialize-development-database.ps1']
foreach ($requiredOperation in @(
    'database update'
    'seed-minimal-development-data.ps1'
    'verify.sql'
    'verify-minimal-seed.sql'
    'smoke-api-database.ps1'
    'Assert-PortAvailable'
)) {
    if ($initializer.IndexOf($requiredOperation, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
        throw "Development initializer is missing '$requiredOperation'."
    }
}

Write-Output "Development environment safety tests passed for $($scriptNames.Count) scripts."
