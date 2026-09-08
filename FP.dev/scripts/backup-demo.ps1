[CmdletBinding()]
param(
    [Parameter()]
    [ValidatePattern('^[A-Za-z0-9_]+$')]
    [string]$DatabaseName = 'DoSelectDb',

    [Parameter()]
    [ValidateSet('Development', 'Demo')]
    [string]$Environment = 'Demo',

    [Parameter()]
    [string]$DataRoot = 'E:\FinalProjectData',

    [Parameter()]
    [string]$BackupRoot = 'E:\FinalProjectBackups',

    [Parameter()]
    [ValidatePattern('^[A-Za-z0-9._-]+$')]
    [string]$Reason = 'manual'
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

$sqlcmd = Get-SqlCmdCommand
if ($null -eq $sqlcmd) {
    throw 'sqlcmd.exe was not found in ODBC 18 tools or PATH.'
}

$resolvedDataRoot = [IO.Path]::GetFullPath($DataRoot)
$resolvedBackupRoot = [IO.Path]::GetFullPath($BackupRoot)
if ($resolvedBackupRoot.StartsWith(
    [IO.Path]::TrimEndingDirectorySeparator($resolvedDataRoot) + [IO.Path]::DirectorySeparatorChar,
    [StringComparison]::OrdinalIgnoreCase)) {
    throw 'BackupRoot must be outside DataRoot so a file snapshot cannot include its own backup output.'
}

$createdAtUtc = [DateTimeOffset]::UtcNow
$backupSetId = '{0}-{1}' -f $createdAtUtc.ToString('yyyyMMddTHHmmssZ'), ([Guid]::NewGuid().ToString('N'))
$backupSetDirectory = Join-Path $resolvedBackupRoot $backupSetId
New-Item -ItemType Directory -Path $backupSetDirectory -Force | Out-Null

$databaseBackupPath = Join-Path $backupSetDirectory "$DatabaseName.bak"
$filesArchivePath = Join-Path $backupSetDirectory 'files.zip'
$manifestPath = Join-Path $backupSetDirectory 'manifest.json'
$escapedBackupPath = $databaseBackupPath.Replace("'", "''")

function Get-FileEvidence {
    param([Parameter(Mandatory)][string]$Path)

    $item = Get-Item -LiteralPath $Path
    return [ordered]@{
        fileName = $item.Name
        sizeBytes = $item.Length
        sha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

$manifest = [ordered]@{
    schemaVersion = 1
    backupSetId = $backupSetId
    environment = $Environment
    reason = $Reason
    createdAtUtc = $createdAtUtc.ToString('O')
    gitCommit = $null
    migration = $null
    database = $null
    files = $null
    fileSnapshot = [ordered]@{
        status = 'pending'
        dataRootAvailable = $false
        expectedRoots = @('product-images', 'private-files', 'private/support')
        capturedRoots = @()
    }
    result = 'failed'
    lastRestoreVerification = $null
}

try {
    $manifest.gitCommit = (& git -C $script:ProjectRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to resolve the current Git commit.'
    }

    $migrationQuery = "SET NOCOUNT ON; SELECT TOP (1) [MigrationId] FROM [$DatabaseName].[dbo].[__EFMigrationsHistory] ORDER BY [MigrationId] DESC;"
    $manifest.migration = (& $sqlcmd -S $script:SqlInstance -E -C -b -h -1 -W -Q $migrationQuery).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($manifest.migration)) {
        throw "Unable to read the EF migration from database '$DatabaseName'."
    }

    $backupQuery = "BACKUP DATABASE [$DatabaseName] TO DISK = N'$escapedBackupPath' WITH COPY_ONLY, INIT, CHECKSUM, COMPRESSION, STATS = 10; RESTORE VERIFYONLY FROM DISK = N'$escapedBackupPath' WITH CHECKSUM;"
    & $sqlcmd -S $script:SqlInstance -E -C -b -Q $backupQuery
    if ($LASTEXITCODE -ne 0) {
        throw "SQL backup or VERIFYONLY failed for database '$DatabaseName'."
    }
    $manifest.database = Get-FileEvidence -Path $databaseBackupPath

    $manifest.fileSnapshot.dataRootAvailable = Test-Path -LiteralPath $resolvedDataRoot -PathType Container
    $snapshotCandidates = @(
        [pscustomobject]@{
            RelativePath = 'product-images'
            FullPath = Join-Path $resolvedDataRoot 'product-images'
        },
        [pscustomobject]@{
            RelativePath = 'private-files'
            FullPath = Join-Path $resolvedDataRoot 'private-files'
        },
        [pscustomobject]@{
            RelativePath = 'private/support'
            FullPath = Join-Path (Join-Path $resolvedDataRoot 'private') 'support'
        }
    )
    $snapshotSources = @($snapshotCandidates | Where-Object {
        Test-Path -LiteralPath $_.FullPath -PathType Container
    })
    $manifest.fileSnapshot.capturedRoots = @($snapshotSources | ForEach-Object { $_.RelativePath })
    if ($snapshotSources.Count -gt 0) {
        New-RelativeDirectoryArchive `
            -SourceRoot $resolvedDataRoot `
            -RelativePaths @($snapshotSources | ForEach-Object { $_.RelativePath }) `
            -DestinationPath $filesArchivePath
        $manifest.files = Get-FileEvidence -Path $filesArchivePath
        $manifest.fileSnapshot.status = 'complete'
    }
    elseif ($manifest.fileSnapshot.dataRootAvailable) {
        $manifest.fileSnapshot.status = 'complete_empty'
    }
    else {
        $manifest.fileSnapshot.status = 'not_captured'
    }

    $manifest.result = 'success'
}
finally {
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding utf8
}

Write-Host "Backup Set created: $backupSetId"
Write-Host "Manifest: $manifestPath"
