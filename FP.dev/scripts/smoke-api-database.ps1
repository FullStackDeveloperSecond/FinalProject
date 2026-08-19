[CmdletBinding()]
param(
    [ValidateRange(10, 120)]
    [int] $TimeoutSeconds = 60
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

$dotnet = 'C:\Users\alexy\.dotnet\dotnet.exe'
$apiProject = Join-Path $script:ProjectRoot 'src\backend\DoSelect.Api\DoSelect.Api.csproj'
$process = $null

if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) {
    throw "The pinned .NET SDK executable was not found at '$dotnet'."
}

Initialize-RunDirectory
Assert-PortAvailable -Port 5126 -ServiceName 'API database smoke test'

$stdout = Join-Path $script:RunRoot 'api-database-smoke.stdout.log'
$stderr = Join-Path $script:RunRoot 'api-database-smoke.stderr.log'
Remove-Item -LiteralPath $stdout, $stderr -Force -ErrorAction SilentlyContinue

$previousEnvironment = $env:ASPNETCORE_ENVIRONMENT
$previousUrls = $env:ASPNETCORE_URLS
try {
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_URLS = $script:ApiUrl
    $process = Start-Process `
        -FilePath $dotnet `
        -ArgumentList @('run', '--no-launch-profile', '--project', $apiProject) `
        -WorkingDirectory $script:ProjectRoot `
        -RedirectStandardOutput $stdout `
        -RedirectStandardError $stderr `
        -WindowStyle Hidden `
        -PassThru

    Wait-HttpEndpoint `
        -Name 'API database readiness' `
        -Uri "$($script:ApiUrl)/health/ready" `
        -TimeoutSeconds $TimeoutSeconds

    $response = Invoke-RestMethod `
        -Uri "$($script:ApiUrl)/health/ready" `
        -Method Get `
        -TimeoutSec 5
    if ($response.status -ne 'Healthy') {
        throw "API database readiness returned '$($response.status)'."
    }

    Write-Host 'API database readiness smoke test: PASS'
}
catch {
    if (Test-Path -LiteralPath $stderr) {
        $errorOutput = Get-Content -LiteralPath $stderr -Raw
        if (-not [string]::IsNullOrWhiteSpace($errorOutput)) {
            Write-Warning $errorOutput
        }
    }

    throw
}
finally {
    $env:ASPNETCORE_ENVIRONMENT = $previousEnvironment
    $env:ASPNETCORE_URLS = $previousUrls

    if ($null -ne $process) {
        $identities = @(Get-ServiceProcessIdentities `
            -RootProcessId $process.Id `
            -Port 5126)
        [array]::Reverse($identities)
        foreach ($identity in $identities) {
            $null = Stop-ManagedProcessIdentity -Identity $identity
        }
    }
}
