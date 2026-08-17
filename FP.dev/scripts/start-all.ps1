param(
    [ValidateSet('Development', 'Demo')]
    [string] $Environment = 'Development',

    [ValidateRange(10, 300)]
    [int] $StartupTimeoutSeconds = 60
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'common.ps1')

$startedProcesses = [System.Collections.Generic.List[object]]::new()

function Start-ManagedService {
    param(
        [Parameter(Mandatory)]
        [string] $Name,

        [Parameter(Mandatory)]
        [string] $FilePath,

        [Parameter(Mandatory)]
        [string[]] $ArgumentList,

        [Parameter(Mandatory)]
        [string] $WorkingDirectory,

        [Parameter(Mandatory)]
        [int] $Port
    )

    $safeName = $Name.ToLowerInvariant().Replace(' ', '-')
    $stdout = Join-Path $script:RunRoot "$safeName.stdout.log"
    $stderr = Join-Path $script:RunRoot "$safeName.stderr.log"
    Remove-Item -LiteralPath $stdout, $stderr -Force -ErrorAction SilentlyContinue

    $startParameters = @{
        FilePath = $FilePath
        ArgumentList = $ArgumentList
        WorkingDirectory = $WorkingDirectory
        RedirectStandardOutput = $stdout
        RedirectStandardError = $stderr
        WindowStyle = 'Hidden'
        PassThru = $true
    }
    $process = Start-Process @startParameters
    $startedProcesses.Add([pscustomobject]@{
        Process = $process
        Port = $Port
    })

    return [pscustomobject]@{
        Name = $Name
        LauncherProcessId = $process.Id
        StdOut = $stdout
        StdErr = $stderr
    }
}

try {
    Initialize-RunDirectory

    $existingState = Read-ProcessState
    if ($null -ne $existingState) {
        $running = @(@($existingState.Services) | Where-Object { Test-ServiceProcesses -Service $_ })
        if ($running.Count -gt 0) {
            throw "DoSelect already has managed processes. Run '.\scripts\status.ps1' or '.\scripts\stop-all.ps1' first."
        }

        Remove-ProcessState
    }

    $dotnet = Get-RequiredCommand -Name 'dotnet.exe'
    $null = Get-RequiredCommand -Name 'node.exe'
    $npm = Get-RequiredCommand -Name 'npm.cmd'
    $null = Get-RequiredCommand -Name 'sqlcmd.exe'

    $sql = Test-SqlServerConnection
    if (-not $sql.IsReady) {
        throw $sql.Detail
    }

    Assert-PortAvailable -Port 5126 -ServiceName 'API'
    Assert-PortAvailable -Port 5173 -ServiceName 'Customer Web'
    Assert-PortAvailable -Port 5174 -ServiceName 'Admin Web'

    $apiProject = Join-Path $script:ProjectRoot 'src\backend\DoSelect.Api\DoSelect.Api.csproj'
    $customerRoot = Join-Path $script:ProjectRoot 'frontend\customer-web'
    $adminRoot = Join-Path $script:ProjectRoot 'frontend\admin-web'

    $previousEnvironment = $env:ASPNETCORE_ENVIRONMENT
    $previousUrls = $env:ASPNETCORE_URLS
    try {
        $env:ASPNETCORE_ENVIRONMENT = $Environment
        $env:ASPNETCORE_URLS = $script:ApiUrl
        $apiParameters = @{
            Name = 'API'
            FilePath = $dotnet
            ArgumentList = @('run', '--no-launch-profile', '--project', $apiProject)
            WorkingDirectory = $script:ProjectRoot
            Port = 5126
        }
        $api = Start-ManagedService @apiParameters
    }
    finally {
        $env:ASPNETCORE_ENVIRONMENT = $previousEnvironment
        $env:ASPNETCORE_URLS = $previousUrls
    }

    $customerParameters = @{
        Name = 'Customer Web'
        FilePath = $npm
        ArgumentList = @('run', 'dev', '--', '--host', '127.0.0.1', '--port', '5173', '--strictPort')
        WorkingDirectory = $customerRoot
        Port = 5173
    }
    $customer = Start-ManagedService @customerParameters
    $adminParameters = @{
        Name = 'Admin Web'
        FilePath = $npm
        ArgumentList = @('run', 'dev', '--', '--host', '127.0.0.1', '--port', '5174', '--strictPort')
        WorkingDirectory = $adminRoot
        Port = 5174
    }
    $admin = Start-ManagedService @adminParameters

    Wait-HttpEndpoint -Name 'API readiness' -Uri "$($script:ApiUrl)/health/ready" -TimeoutSeconds $StartupTimeoutSeconds
    Wait-HttpEndpoint -Name 'Customer Web' -Uri $script:CustomerUrl -TimeoutSeconds $StartupTimeoutSeconds
    Wait-HttpEndpoint -Name 'Admin Web' -Uri $script:AdminUrl -TimeoutSeconds $StartupTimeoutSeconds

    $serviceDefinitions = @(
        [pscustomobject]@{ Service = $api; Port = 5126 }
        [pscustomobject]@{ Service = $customer; Port = 5173 }
        [pscustomobject]@{ Service = $admin; Port = 5174 }
    )
    $services = $serviceDefinitions | ForEach-Object {
        [pscustomobject]@{
            Name = $_.Service.Name
            Processes = @(Get-ServiceProcessIdentities -RootProcessId $_.Service.LauncherProcessId -Port $_.Port)
            StdOut = $_.Service.StdOut
            StdErr = $_.Service.StdErr
        }
    }
    Write-ProcessState -State ([pscustomobject]@{
        Environment = $Environment
        StartedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        Services = $services
    })

    Write-Host 'DoSelect started successfully.' -ForegroundColor Green
    Write-Host "API:          $($script:ApiUrl)"
    Write-Host "Customer Web: $($script:CustomerUrl)"
    Write-Host "Admin Web:    $($script:AdminUrl)"
    Write-Host "Environment:  $Environment"
    Write-Host "Runtime data: $($script:RunRoot)"
    exit 0
}
catch {
    foreach ($started in @($startedProcesses) | Sort-Object { $_.Process.StartTime } -Descending) {
        try {
            $identities = @(Get-ServiceProcessIdentities -RootProcessId $started.Process.Id -Port $started.Port)
            [array]::Reverse($identities)
            foreach ($identity in $identities) {
                $null = Stop-ManagedProcessIdentity -Identity $identity
            }
        }
        catch {
            # Preserve the original startup failure while attempting best-effort cleanup.
        }
    }

    Remove-ProcessState
    Write-Error $_.Exception.Message
    exit 1
}
