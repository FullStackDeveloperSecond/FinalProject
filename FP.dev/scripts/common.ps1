Set-StrictMode -Version Latest

$script:ProjectRoot = Split-Path -Parent $PSScriptRoot
$script:RunRoot = Join-Path $script:ProjectRoot '.run'
$script:StateFile = Join-Path $script:RunRoot 'processes.json'
$script:SqlInstance = '.\SQL2025'
$script:SqlServiceName = 'MSSQL$SQL2025'
$script:ApiUrl = 'http://localhost:5126'
$script:CustomerUrl = 'http://localhost:5173'
$script:AdminUrl = 'http://localhost:5174/admin/'

function Initialize-RunDirectory {
    New-Item -ItemType Directory -Path $script:RunRoot -Force | Out-Null
}

function Get-RequiredCommand {
    param(
        [Parameter(Mandatory)]
        [string] $Name
    )

    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if ($null -eq $command) {
        throw "Required command '$Name' was not found in PATH."
    }

    return $command.Source
}

function Test-SqlServerConnection {
    $service = Get-Service -Name $script:SqlServiceName -ErrorAction SilentlyContinue
    if ($null -eq $service) {
        return [pscustomobject]@{
            IsReady = $false
            Detail = "Windows service '$($script:SqlServiceName)' was not found."
        }
    }

    if ($service.Status -ne 'Running') {
        return [pscustomobject]@{
            IsReady = $false
            Detail = "Windows service '$($script:SqlServiceName)' is $($service.Status)."
        }
    }

    $sqlcmd = Get-Command 'sqlcmd.exe' -ErrorAction SilentlyContinue
    if ($null -eq $sqlcmd) {
        return [pscustomobject]@{
            IsReady = $false
            Detail = "sqlcmd.exe was not found in PATH."
        }
    }

    $null = & $sqlcmd.Source -S $script:SqlInstance -E -b -l 5 -Q 'SET NOCOUNT ON; SELECT 1;' 2>&1
    if ($LASTEXITCODE -ne 0) {
        return [pscustomobject]@{
            IsReady = $false
            Detail = "Windows Authentication connection to SQL Server instance '$($script:SqlInstance)' failed."
        }
    }

    return [pscustomobject]@{
        IsReady = $true
        Detail = "SQL Server instance '$($script:SqlInstance)' is reachable with Windows Authentication."
    }
}

function Test-HttpEndpoint {
    param(
        [Parameter(Mandatory)]
        [string] $Uri,

        [int] $TimeoutSeconds = 3
    )

    try {
        $response = Invoke-WebRequest -Uri $Uri -Method Get -TimeoutSec $TimeoutSeconds -UseBasicParsing
        return $response.StatusCode -ge 200 -and $response.StatusCode -lt 400
    }
    catch {
        return $false
    }
}

function Wait-HttpEndpoint {
    param(
        [Parameter(Mandatory)]
        [string] $Name,

        [Parameter(Mandatory)]
        [string] $Uri,

        [int] $TimeoutSeconds = 60
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        if (Test-HttpEndpoint -Uri $Uri) {
            return
        }

        Start-Sleep -Milliseconds 500
    } while ([DateTimeOffset]::UtcNow -lt $deadline)

    throw "$Name did not become ready at '$Uri' within $TimeoutSeconds seconds."
}

function Assert-PortAvailable {
    param(
        [Parameter(Mandatory)]
        [int] $Port,

        [Parameter(Mandatory)]
        [string] $ServiceName
    )

    $listener = Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($null -ne $listener) {
        throw "Port $Port for $ServiceName is already in use by PID $($listener.OwningProcess). Stop that process before retrying."
    }
}

function Get-ProcessIdentity {
    param(
        [Parameter(Mandatory)]
        [int] $ProcessId
    )

    $process = Get-Process -Id $ProcessId -ErrorAction Stop
    $startedAtUtc = $process.StartTime.ToUniversalTime()
    return [pscustomobject]@{
        ProcessId = $process.Id
        StartedAtUtc = $startedAtUtc.ToString('O')
        StartedAtUtcTicks = $startedAtUtc.Ticks
    }
}

function Test-ProcessIdentity {
    param(
        [Parameter(Mandatory)]
        [object] $Identity
    )

    try {
        $process = Get-Process -Id ([int] $Identity.ProcessId) -ErrorAction Stop
        if ($null -ne $Identity.PSObject.Properties['StartedAtUtcTicks']) {
            $expected = [DateTime]::new([long] $Identity.StartedAtUtcTicks, [DateTimeKind]::Utc)
        }
        elseif ($Identity.StartedAtUtc -is [DateTime]) {
            $expected = ([DateTime] $Identity.StartedAtUtc).ToUniversalTime()
        }
        else {
            $expected = [DateTime]::Parse(
                [string] $Identity.StartedAtUtc,
                [Globalization.CultureInfo]::InvariantCulture,
                [Globalization.DateTimeStyles]::RoundtripKind).ToUniversalTime()
        }

        return [Math]::Abs(($process.StartTime.ToUniversalTime() - $expected.ToUniversalTime()).TotalSeconds) -lt 1
    }
    catch {
        return $false
    }
}

function Get-ServiceProcessIdentities {
    param(
        [Parameter(Mandatory)]
        [int] $RootProcessId,

        [Parameter(Mandatory)]
        [int] $Port
    )

    $processIds = @($RootProcessId)
    $listenerProcessIds = @(Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty OwningProcess -Unique)
    $processIds = @($processIds + $listenerProcessIds | Select-Object -Unique)

    $identities = [Collections.Generic.List[object]]::new()
    foreach ($processId in $processIds) {
        try {
            $identities.Add((Get-ProcessIdentity -ProcessId $processId))
        }
        catch {
            # A launcher may exit after handing off to the actual listener.
        }
    }

    return @($identities)
}

function Stop-ManagedProcessIdentity {
    param(
        [Parameter(Mandatory)]
        [object] $Identity
    )

    if (-not (Test-ProcessIdentity -Identity $Identity)) {
        return $false
    }

    Stop-Process -Id ([int] $Identity.ProcessId) -Force -ErrorAction Stop
    return $true
}

function Read-ProcessState {
    if (-not (Test-Path -LiteralPath $script:StateFile)) {
        return $null
    }

    return Get-Content -Raw -LiteralPath $script:StateFile | ConvertFrom-Json
}

function Write-ProcessState {
    param(
        [Parameter(Mandatory)]
        [object] $State
    )

    Initialize-RunDirectory
    $State | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $script:StateFile -Encoding utf8
}

function Remove-ProcessState {
    Remove-Item -LiteralPath $script:StateFile -Force -ErrorAction SilentlyContinue
}

function Test-ServiceProcesses {
    param(
        [Parameter(Mandatory)]
        [object] $Service
    )

    foreach ($identity in @($Service.Processes)) {
        if (Test-ProcessIdentity -Identity $identity) {
            return $true
        }
    }

    return $false
}

function Get-DoSelectStatus {
    $sql = Test-SqlServerConnection
    $state = Read-ProcessState
    $results = [Collections.Generic.List[object]]::new()
    $results.Add([pscustomobject]@{
        Service = 'SQL Server'
        Status = if ($sql.IsReady) { 'Ready' } else { 'Unavailable' }
        Url = $script:SqlInstance
        Detail = $sql.Detail
    })

    $definitions = @(
        [pscustomobject]@{ Name = 'API'; Url = "$($script:ApiUrl)/health/ready" }
        [pscustomobject]@{ Name = 'Customer Web'; Url = $script:CustomerUrl }
        [pscustomobject]@{ Name = 'Admin Web'; Url = $script:AdminUrl }
    )

    foreach ($definition in $definitions) {
        $serviceState = $null
        if ($null -ne $state) {
            $serviceState = @($state.Services) | Where-Object { $_.Name -eq $definition.Name } | Select-Object -First 1
        }

        $hasProcess = $null -ne $serviceState -and (Test-ServiceProcesses -Service $serviceState)
        $isHealthy = $hasProcess -and (Test-HttpEndpoint -Uri $definition.Url)
        $status = if ($isHealthy) { 'Ready' } elseif ($hasProcess) { 'Unhealthy' } else { 'Stopped' }
        $detail = if ($null -eq $serviceState) { 'No managed process record.' } elseif (-not $hasProcess) { 'Managed process is not running.' } elseif (-not $isHealthy) { 'Process is running but the endpoint did not respond successfully.' } else { 'Managed process and endpoint are healthy.' }

        $results.Add([pscustomobject]@{
            Service = $definition.Name
            Status = $status
            Url = $definition.Url
            Detail = $detail
        })
    }

    return @($results)
}
