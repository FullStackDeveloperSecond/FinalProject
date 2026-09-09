[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$sourceRoot = Join-Path ([IO.Path]::GetTempPath()) "DoSelectBackupSource_$([Guid]::NewGuid().ToString('N'))"
$outsideRoot = Join-Path ([IO.Path]::GetTempPath()) "DoSelectBackupOutside_$([Guid]::NewGuid().ToString('N'))"
$backupRoot = Join-Path ([IO.Path]::GetTempPath()) "DoSelectBackupRetention_$([Guid]::NewGuid().ToString('N'))"
$pruneScript = Join-Path $PSScriptRoot 'prune-demo-backups.ps1'
. (Join-Path $PSScriptRoot 'common.ps1')

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
    $isoWeekCases = @(
        [pscustomobject]@{ Date = [DateTime] '2022-12-31'; Expected = '2022-52' }
        [pscustomobject]@{ Date = [DateTime] '2023-01-01'; Expected = '2022-52' }
        [pscustomobject]@{ Date = [DateTime] '2023-01-02'; Expected = '2023-01' }
        [pscustomobject]@{ Date = [DateTime] '2018-12-31'; Expected = '2019-01' }
    )
    foreach ($case in $isoWeekCases) {
        $actual = Get-IsoWeekKey -Date $case.Date
        if ($actual -ne $case.Expected) {
            throw "ISO week key for $($case.Date.ToString('yyyy-MM-dd')) expected '$($case.Expected)' but received '$actual'."
        }
    }

    $supportRoot = Join-Path (Join-Path $sourceRoot 'private') 'support'
    New-Item -ItemType Directory -Path $supportRoot -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $supportRoot 'probe.txt') -Value 'path-preservation-probe'
    $archivePath = Join-Path $backupRoot 'path-preservation.zip'
    $expandedRoot = Join-Path $backupRoot 'expanded'
    New-RelativeDirectoryArchive `
        -SourceRoot $sourceRoot `
        -RelativePaths @('private/support') `
        -DestinationPath $archivePath
    Expand-Archive -LiteralPath $archivePath -DestinationPath $expandedRoot
    if (-not (Test-Path -LiteralPath (Join-Path $expandedRoot 'private/support/probe.txt') -PathType Leaf)) {
        throw 'The file snapshot archive did not preserve private/support relative paths.'
    }
    if (Test-Path -LiteralPath (Join-Path $expandedRoot 'support/probe.txt') -PathType Leaf) {
        throw 'The file snapshot archive incorrectly flattened private/support to support.'
    }

    New-Item -ItemType Directory -Path $outsideRoot -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $outsideRoot 'outside-marker.txt') -Value 'synthetic-outside-marker'
    $linkType = if ($env:OS -eq 'Windows_NT') { 'Junction' } else { 'SymbolicLink' }
    New-Item -ItemType $linkType -Path (Join-Path $supportRoot 'link-outside') -Target $outsideRoot | Out-Null
    $reparsePointWasRejected = $false
    try {
        New-RelativeDirectoryArchive `
            -SourceRoot $sourceRoot `
            -RelativePaths @('private/support') `
            -DestinationPath (Join-Path $backupRoot 'reparse-point.zip')
    }
    catch {
        $reparsePointWasRejected = $_.Exception.Message -like 'Archive source *contains a reparse point*'
    }
    if (-not $reparsePointWasRejected) {
        throw 'The file snapshot archive did not reject a reparse point below DataRoot.'
    }

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
    $tempPrefix = Get-DirectoryPathPrefix -Path ([IO.Path]::GetTempPath())
    foreach ($cleanupTarget in @($sourceRoot, $outsideRoot, $backupRoot)) {
        $resolvedCleanupTarget = [IO.Path]::GetFullPath($cleanupTarget)
        if ($resolvedCleanupTarget.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase) -and
            (Split-Path -Leaf $resolvedCleanupTarget) -like 'DoSelectBackup*_*') {
            Remove-Item -LiteralPath $resolvedCleanupTarget -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}
