[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^DoSelectDemo_[0-9a-fA-F]{32}$')]
    [string] $DatabaseName
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

Assert-IsolatedDemoDatabaseName -DatabaseName $DatabaseName

$processState = Read-ProcessState
if ($null -ne $processState -and
    @(@($processState.Services) | Where-Object { Test-ServiceProcesses -Service $_ }).Count -gt 0) {
    throw "DoSelect managed services are running. Run '.\scripts\stop-all.ps1' before activating Demo accounts."
}

$apiProject = Join-Path $script:ProjectRoot 'src\backend\DoSelect.Api'
$dotnet = Get-RequiredCommand -Name 'dotnet.exe'
$previousEnvironment = $env:ASPNETCORE_ENVIRONMENT
$previousConnection = $env:ConnectionStrings__DefaultConnection
$previousAiEnabled = $env:Features__AiEnabled
$previousEmailEnabled = $env:Features__EmailEnabled
$previousBackgroundJobsEnabled = $env:Features__BackgroundJobsEnabled

Push-Location $script:ProjectRoot
try {
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:ConnectionStrings__DefaultConnection = New-DemoConnectionString -DatabaseName $DatabaseName
    $env:Features__AiEnabled = 'false'
    $env:Features__EmailEnabled = 'false'
    $env:Features__BackgroundJobsEnabled = 'false'

    $activationOutput = @(& $dotnet run --project $apiProject --no-launch-profile -- --activate-demo-accounts)
    $activationExitCode = $LASTEXITCODE
    if ($activationExitCode -ne 0) {
        throw 'Demo account activation failed.'
    }

    $resultPrefix = 'DEMO_ACCOUNT_ACTIVATION:'
    $resultLine = $activationOutput |
        Where-Object { $_.StartsWith($resultPrefix, [StringComparison]::Ordinal) } |
        Select-Object -Last 1
    if ([string]::IsNullOrWhiteSpace($resultLine)) {
        throw 'Demo account activation did not return its structured result.'
    }

    $result = $resultLine.Substring($resultPrefix.Length) | ConvertFrom-Json
    if ([Guid] $result.AdminPublicId -eq [Guid]::Empty -or
        [int] $result.MainBusinessRecordTotal -ne 10000) {
        throw 'Demo account activation returned an invalid account or data summary.'
    }

    $null = & $dotnet user-secrets set `
        'OpenAI:BudgetAlertRecipientAdminPublicId' `
        ([string] $result.AdminPublicId) `
        --project $apiProject
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to store User Secrets key 'OpenAI:BudgetAlertRecipientAdminPublicId'."
    }

    $activationOutput | Write-Output
    Write-Host 'The Demo SuperAdmin is configured as the local OpenAI budget alert recipient.' -ForegroundColor Green
}
finally {
    $env:ASPNETCORE_ENVIRONMENT = $previousEnvironment
    $env:ConnectionStrings__DefaultConnection = $previousConnection
    $env:Features__AiEnabled = $previousAiEnabled
    $env:Features__EmailEnabled = $previousEmailEnabled
    $env:Features__BackgroundJobsEnabled = $previousBackgroundJobsEnabled
    Pop-Location
}
