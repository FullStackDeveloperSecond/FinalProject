param(
    [ValidateRange(1, 30)]
    [int] $TimeoutSeconds = 5
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'common.ps1')

function Invoke-WebHealthCheck {
    param(
        [Parameter(Mandatory)]
        [string] $Name,

        [Parameter(Mandatory)]
        [string] $Uri,

        [switch] $RequireHealthPayload,

        [switch] $AllowDegraded
    )

    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    try {
        $response = Invoke-WebRequest -Uri $Uri -Method Get -TimeoutSec $TimeoutSeconds -UseBasicParsing
        $stopwatch.Stop()

        if ($response.StatusCode -lt 200 -or $response.StatusCode -ge 400) {
            return [pscustomobject]@{
                Check = $Name
                Status = 'Failed'
                Target = $Uri
                LatencyMs = $stopwatch.ElapsedMilliseconds
                Detail = "HTTP $($response.StatusCode)."
            }
        }

        if ($RequireHealthPayload) {
            try {
                $payload = $response.Content | ConvertFrom-Json
            }
            catch {
                return [pscustomobject]@{
                    Check = $Name
                    Status = 'Failed'
                    Target = $Uri
                    LatencyMs = $stopwatch.ElapsedMilliseconds
                    Detail = 'Response was not the expected health JSON payload.'
                }
            }

            $healthStatus = [string] $payload.status
            if ($healthStatus -eq 'Healthy') {
                return [pscustomobject]@{
                    Check = $Name
                    Status = 'Ready'
                    Target = $Uri
                    LatencyMs = $stopwatch.ElapsedMilliseconds
                    Detail = 'Health status is Healthy.'
                }
            }

            if ($AllowDegraded -and $healthStatus -eq 'Degraded') {
                return [pscustomobject]@{
                    Check = $Name
                    Status = 'Degraded'
                    Target = $Uri
                    LatencyMs = $stopwatch.ElapsedMilliseconds
                    Detail = 'Health status is Degraded; basic service remains available.'
                }
            }

            return [pscustomobject]@{
                Check = $Name
                Status = 'Failed'
                Target = $Uri
                LatencyMs = $stopwatch.ElapsedMilliseconds
                Detail = "Health status is '$healthStatus'."
            }
        }

        return [pscustomobject]@{
            Check = $Name
            Status = 'Ready'
            Target = $Uri
            LatencyMs = $stopwatch.ElapsedMilliseconds
            Detail = "HTTP $($response.StatusCode)."
        }
    }
    catch {
        $stopwatch.Stop()
        return [pscustomobject]@{
            Check = $Name
            Status = 'Failed'
            Target = $Uri
            LatencyMs = $stopwatch.ElapsedMilliseconds
            Detail = 'Endpoint was unreachable or returned an error response.'
        }
    }
}

try {
    $sqlStopwatch = [Diagnostics.Stopwatch]::StartNew()
    $sql = Test-SqlServerConnection
    $sqlStopwatch.Stop()

    $results = [Collections.Generic.List[object]]::new()
    $results.Add([pscustomobject]@{
        Check = 'SQL Server'
        Status = if ($sql.IsReady) { 'Ready' } else { 'Failed' }
        Target = $script:SqlInstance
        LatencyMs = $sqlStopwatch.ElapsedMilliseconds
        Detail = $sql.Detail
    })
    $results.Add((Invoke-WebHealthCheck -Name 'API Liveness' -Uri "$($script:ApiUrl)/health/live" -RequireHealthPayload))
    $results.Add((Invoke-WebHealthCheck -Name 'API Readiness' -Uri "$($script:ApiUrl)/health/ready" -RequireHealthPayload -AllowDegraded))
    $results.Add((Invoke-WebHealthCheck -Name 'Customer Web' -Uri $script:CustomerUrl))
    $results.Add((Invoke-WebHealthCheck -Name 'Admin Web' -Uri $script:AdminUrl))

    $results | Format-Table Check, Status, LatencyMs, Target, Detail -AutoSize -Wrap

    $failedCount = @($results | Where-Object { $_.Status -eq 'Failed' }).Count
    $degradedCount = @($results | Where-Object { $_.Status -eq 'Degraded' }).Count
    if ($failedCount -gt 0) {
        Write-Host "Health check failed: $failedCount required check(s) failed." -ForegroundColor Red
        exit 1
    }

    if ($degradedCount -gt 0) {
        Write-Host "Health check passed with $degradedCount degraded optional dependency check(s)." -ForegroundColor Yellow
        exit 0
    }

    Write-Host 'Health check passed: all required checks are ready.' -ForegroundColor Green
    exit 0
}
catch {
    Write-Error $_.Exception.Message
    exit 1
}
