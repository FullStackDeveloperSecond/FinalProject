[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$backupRoot = Join-Path ([IO.Path]::GetTempPath()) "DoSelectBackupRetention_$([Guid]::NewGuid().ToString('N'))"
$pruneScript = Join-Path $PSScriptRoot 'prune-demo-backups.ps1'

function Write-TestManifest {
    param(
        [Parameter(Mandatory)][string]$DirectoryName,
        [Parameter(Mandatory)][string]$CreatedAtUtc,
        [Parameter(Mandatory)][bool]$HasFiles,
        [Parameter()][AllowNull()][string]$FileRecoveryResult = $null
    )

    $directory = Join-Path $backupRoot $DirectoryName
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $restoreVerification = [ordered]@{ result = 'success' }
    if ($PSBoundParameters.ContainsKey('FileRecoveryResult')) {
        $restoreVerification.fileRecoveryResult = $FileRecoveryResult
    }

    $manifest = [ordered]@{
        schemaVersion = 1
        backupSetId = $DirectoryName
        createdAtUtc = $CreatedAtUtc
        result = 'success'
        files = if ($HasFiles) { [ordered]@{ fileName = 'files.zip' } } else { $null }
        lastRestoreVerification = $restoreVerification
    }
    $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $directory 'manifest.json') -Encoding utf8
}

try {
    New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
    Write-TestManifest `
        -DirectoryName 'database-only' `
        -CreatedAtUtc '2026-09-01T00:00:00Z' `
        -HasFiles $false `
        -FileRecoveryResult 'not_tested'

    $databaseOnlyWasRejected = $false
    try {
        & $pruneScript -BackupRoot $backupRoot -DailyCount 1 -WeeklyCount 1 -WhatIf
    }
    catch {
        $databaseOnlyWasRejected = $_.Exception.Message -like 'No retained complete Backup Set*'
    }

    if (-not $databaseOnlyWasRejected) {
        throw 'A database-only restore verification incorrectly authorized pruning.'
    }

    Write-TestManifest `
        -DirectoryName 'complete' `
        -CreatedAtUtc '2026-09-08T00:00:00Z' `
        -HasFiles $true

    & $pruneScript -BackupRoot $backupRoot -DailyCount 1 -WeeklyCount 1 -WhatIf
    Write-Host 'Backup retention safety tests passed.'
}
finally {
    $resolvedBackupRoot = [IO.Path]::GetFullPath($backupRoot)
    $tempPrefix = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetTempPath()) + [IO.Path]::DirectorySeparatorChar
    if ($resolvedBackupRoot.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $resolvedBackupRoot) -like 'DoSelectBackupRetention_*') {
        Remove-Item -LiteralPath $resolvedBackupRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
