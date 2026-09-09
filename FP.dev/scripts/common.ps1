Set-StrictMode -Version Latest

$script:ProjectRoot = Split-Path -Parent $PSScriptRoot
$script:RunRoot = Join-Path $script:ProjectRoot '.run'
$script:StateFile = Join-Path $script:RunRoot 'processes.json'
$script:DemoDatabaseStateFile = Join-Path $script:RunRoot 'demo-database.json'
$script:SqlInstance = '.\SQL2025'
$script:SqlServiceName = 'MSSQL$SQL2025'
$script:ApiUrl = 'http://localhost:5126'
$script:CustomerUrl = 'http://localhost:5173'
$script:AdminUrl = 'http://localhost:5174/admin/'

function Assert-IsolatedDemoDatabaseName {
    param(
        [Parameter(Mandatory)]
        [string] $DatabaseName
    )

    if ($DatabaseName -notmatch '^DoSelectDemo_[0-9a-fA-F]{32}$') {
        throw "Demo runtime requires an isolated database named 'DoSelectDemo_<32-hex>'. Shared databases are not allowed."
    }
}

function New-DemoConnectionString {
    param(
        [Parameter(Mandatory)]
        [string] $DatabaseName
    )

    Assert-IsolatedDemoDatabaseName -DatabaseName $DatabaseName
    return "Server=$($script:SqlInstance);Database=$DatabaseName;Integrated Security=True;TrustServerCertificate=True;MultipleActiveResultSets=True"
}

function New-DevelopmentConnectionString {
    return "Server=$($script:SqlInstance);Database=DoSelectDb;Integrated Security=True;TrustServerCertificate=True;MultipleActiveResultSets=True"
}

function Read-DemoDatabaseState {
    if (-not (Test-Path -LiteralPath $script:DemoDatabaseStateFile -PathType Leaf)) {
        return $null
    }

    $state = Get-Content -Raw -LiteralPath $script:DemoDatabaseStateFile | ConvertFrom-Json
    Assert-IsolatedDemoDatabaseName -DatabaseName ([string] $state.DatabaseName)
    return $state
}

function Write-DemoDatabaseState {
    param(
        [Parameter(Mandatory)]
        [string] $DatabaseName
    )

    Assert-IsolatedDemoDatabaseName -DatabaseName $DatabaseName
    Initialize-RunDirectory
    $temporaryPath = "$($script:DemoDatabaseStateFile).$([Guid]::NewGuid().ToString('N')).tmp"
    try {
        [ordered]@{
            DatabaseName = $DatabaseName
            PreparedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        } | ConvertTo-Json | Set-Content -LiteralPath $temporaryPath -Encoding utf8
        Move-Item -LiteralPath $temporaryPath -Destination $script:DemoDatabaseStateFile -Force
    }
    finally {
        Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
    }
}

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

function Get-DirectoryPathPrefix {
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    $resolvedPath = [IO.Path]::GetFullPath($Path)
    $separatorCharacters = [char[]] @(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    return $resolvedPath.TrimEnd($separatorCharacters) + [IO.Path]::DirectorySeparatorChar
}

function Get-IsoWeekKey {
    param(
        [Parameter(Mandatory)]
        [DateTime] $Date
    )

    $calendar = [Globalization.CultureInfo]::InvariantCulture.Calendar
    $dayOfWeek = $calendar.GetDayOfWeek($Date)
    $isoDayOfWeek = if ($dayOfWeek -eq [DayOfWeek]::Sunday) {
        7
    }
    else {
        [int] $dayOfWeek
    }
    # ISO week-year is the calendar year containing that week's Thursday. Anchoring every
    # date to Thursday handles both directions of a year boundary (for example 2023-01-01
    # belongs to 2022-W52, while 2018-12-31 belongs to 2019-W01).
    $Date = $Date.AddDays(4 - $isoDayOfWeek)

    $week = $calendar.GetWeekOfYear(
        $Date,
        [Globalization.CalendarWeekRule]::FirstFourDayWeek,
        [DayOfWeek]::Monday)
    return '{0}-{1:D2}' -f $Date.Year, $week
}

function Get-SqlCmdCommand {
    $preferredSqlCmd = Join-Path $env:ProgramFiles 'Microsoft SQL Server\Client SDK\ODBC\180\Tools\Binn\SQLCMD.EXE'
    if (Test-Path -LiteralPath $preferredSqlCmd -PathType Leaf) {
        return $preferredSqlCmd
    }

    $command = Get-Command 'sqlcmd.exe' -ErrorAction SilentlyContinue
    if ($null -eq $command) {
        return $null
    }

    return $command.Source
}

function New-RelativeDirectoryArchive {
    param(
        [Parameter(Mandatory)]
        [string] $SourceRoot,

        [Parameter(Mandatory)]
        [string[]] $RelativePaths,

        [Parameter(Mandatory)]
        [string] $DestinationPath
    )

    $resolvedSourceRoot = [IO.Path]::GetFullPath($SourceRoot)
    $sourcePrefix = Get-DirectoryPathPrefix -Path $resolvedSourceRoot
    $resolvedDestinationPath = [IO.Path]::GetFullPath($DestinationPath)
    if ($resolvedDestinationPath.StartsWith($sourcePrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'DestinationPath must be outside SourceRoot.'
    }

    $destinationDirectory = Split-Path -Parent $resolvedDestinationPath
    New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
    $stagingRoot = Join-Path $destinationDirectory ".snapshot-$([Guid]::NewGuid().ToString('N'))"

    try {
        New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null
        foreach ($relativePath in $RelativePaths) {
            if ([IO.Path]::IsPathRooted($relativePath)) {
                throw "Archive source path must be relative: $relativePath"
            }

            $sourcePath = [IO.Path]::GetFullPath((Join-Path $resolvedSourceRoot $relativePath))
            if (-not $sourcePath.StartsWith($sourcePrefix, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Archive source path escaped SourceRoot: $relativePath"
            }
            if (-not (Test-Path -LiteralPath $sourcePath -PathType Container)) {
                throw "Archive source directory was not found: $sourcePath"
            }

            $pathToCheck = $sourcePath
            while ($true) {
                $pathItem = Get-Item -LiteralPath $pathToCheck -Force
                if (($pathItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                    throw "Archive source path contains a reparse point: $relativePath"
                }
                if ($pathToCheck.Equals($resolvedSourceRoot, [StringComparison]::OrdinalIgnoreCase)) {
                    break
                }

                $pathToCheck = Split-Path -Parent $pathToCheck
            }

            $nestedReparsePoint = Get-ChildItem -LiteralPath $sourcePath -Force -Recurse |
                Where-Object { ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 } |
                Select-Object -First 1
            if ($null -ne $nestedReparsePoint) {
                throw "Archive source directory contains a reparse point: $relativePath"
            }

            $stagedPath = Join-Path $stagingRoot $relativePath
            New-Item -ItemType Directory -Path (Split-Path -Parent $stagedPath) -Force | Out-Null
            if ($env:OS -eq 'Windows_NT') {
                $robocopy = Get-RequiredCommand -Name 'robocopy.exe'
                & $robocopy $sourcePath $stagedPath /E /COPY:DAT /DCOPY:DAT /SL /SJ /R:0 /W:0 /NP /NFL /NDL /NJH /NJS | Out-Null
                $robocopyExitCode = $LASTEXITCODE
                if ($robocopyExitCode -ge 8) {
                    throw "robocopy failed while staging archive source '$relativePath' with exit code $robocopyExitCode."
                }
            }
            else {
                $copyCommand = Get-RequiredCommand -Name 'cp'
                & $copyCommand '-a' $sourcePath $stagedPath
                if ($LASTEXITCODE -ne 0) {
                    throw "cp failed while staging archive source '$relativePath'."
                }
            }

            $stagedItem = Get-Item -LiteralPath $stagedPath -Force
            $stagedReparsePoint = @($stagedItem) + @(Get-ChildItem -LiteralPath $stagedPath -Force -Recurse) |
                Where-Object { ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 } |
                Select-Object -First 1
            if ($null -ne $stagedReparsePoint) {
                throw "Staged archive source contains a reparse point: $relativePath"
            }
        }

        $archiveRoots = @(Get-ChildItem -LiteralPath $stagingRoot -Force)
        if ($archiveRoots.Count -eq 0) {
            throw 'At least one relative directory is required to create the archive.'
        }

        Compress-Archive -LiteralPath @($archiveRoots | ForEach-Object { $_.FullName }) `
            -DestinationPath $resolvedDestinationPath `
            -CompressionLevel Optimal
    }
    finally {
        if (Test-Path -LiteralPath $stagingRoot) {
            Remove-Item -LiteralPath $stagingRoot -Recurse -Force
        }
    }
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

    $sqlcmd = Get-SqlCmdCommand
    if ($null -eq $sqlcmd) {
        return [pscustomobject]@{
            IsReady = $false
            Detail = "sqlcmd.exe was not found in ODBC 18 tools or PATH."
        }
    }

    $null = & $sqlcmd -S $script:SqlInstance -E -C -b -l 5 -Q 'SET NOCOUNT ON; SELECT 1;' 2>&1
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
