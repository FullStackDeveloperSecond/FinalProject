$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'common.ps1')

try {
    $state = Read-ProcessState
    if ($null -eq $state) {
        Write-Host 'No DoSelect managed process state was found.' -ForegroundColor Yellow
        exit 0
    }

    $stoppedCount = 0
    $services = @($state.Services)
    [array]::Reverse($services)
    foreach ($service in $services) {
        $identities = @($service.Processes)
        [array]::Reverse($identities)
        foreach ($identity in $identities) {
            if (Stop-ManagedProcessIdentity -Identity $identity) {
                $stoppedCount++
            }
        }
    }

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(5)
    do {
        $remaining = @(@($state.Services) | Where-Object { Test-ServiceProcesses -Service $_ })
        if ($remaining.Count -eq 0) {
            break
        }

        Start-Sleep -Milliseconds 250
    } while ([DateTimeOffset]::UtcNow -lt $deadline)

    if ($remaining.Count -gt 0) {
        $names = ($remaining | ForEach-Object Name) -join ', '
        throw "Some managed processes are still running: $names. Runtime state was retained for inspection."
    }

    Remove-ProcessState
    Write-Host "DoSelect stopped. $stoppedCount managed process(es) were terminated." -ForegroundColor Green
    exit 0
}
catch {
    Write-Error $_.Exception.Message
    exit 1
}
