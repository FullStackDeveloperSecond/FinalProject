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
    if (-not $connectionString.Contains($requiredFragment, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Development connection string is missing '$requiredFragment'."
    }
}

foreach ($forbiddenFragment in @('User ID=', 'Password=')) {
    if ($connectionString.Contains($forbiddenFragment, [StringComparison]::OrdinalIgnoreCase)) {
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

$initializer = $scriptContents['initialize-development-database.ps1']
foreach ($requiredOperation in @(
    'database update'
    'seed-minimal-development-data.ps1'
    'verify.sql'
    'verify-minimal-seed.sql'
    'smoke-api-database.ps1'
    'Assert-PortAvailable'
)) {
    if (-not $initializer.Contains($requiredOperation, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Development initializer is missing '$requiredOperation'."
    }
}

Write-Output "Development environment safety tests passed for $($scriptNames.Count) scripts."
