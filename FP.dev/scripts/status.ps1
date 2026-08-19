$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'common.ps1')

try {
    $results = @(Get-DoSelectStatus)
    $results | Format-Table Service, Status, Url, Detail -AutoSize -Wrap

    if (@($results | Where-Object { $_.Status -ne 'Ready' }).Count -gt 0) {
        exit 1
    }

    exit 0
}
catch {
    Write-Error $_.Exception.Message
    exit 1
}
