[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$BackupSetDirectory,

    [Parameter()]
    [ValidatePattern('^[A-Za-z0-9_]+$')]
    [string]$VerificationDatabaseName = "DoSelectRestoreVerify_$([DateTime]::UtcNow.ToString('yyyyMMddHHmmss'))",

    [Parameter()]
    [string]$VerificationDataRoot = 'E:\FinalProjectRestoreVerification'
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

if ($VerificationDatabaseName -eq 'DoSelectDb') {
    throw 'The restore verification database must not be the sole DoSelectDb demo database.'
}

$sqlcmd = Get-SqlCmdCommand
if ($null -eq $sqlcmd) {
    throw 'sqlcmd.exe was not found in ODBC 18 tools or PATH.'
}

$resolvedSetDirectory = [IO.Path]::GetFullPath($BackupSetDirectory)
$manifestPath = Join-Path $resolvedSetDirectory 'manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Backup manifest was not found: $manifestPath"
}

$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1 -or $manifest.result -ne 'success') {
    throw 'Only a successful schemaVersion 1 Backup Set can be restored.'
}

function Assert-FileEvidence {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][object]$Evidence
    )

    $path = Join-Path $Root $Evidence.fileName
    $item = Get-Item -LiteralPath $path
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($item.Length -ne [long]$Evidence.sizeBytes -or $hash -ne [string]$Evidence.sha256) {
        throw "Backup artifact evidence mismatch: $($Evidence.fileName)"
    }
    return $path
}

$databaseBackupPath = Assert-FileEvidence -Root $resolvedSetDirectory -Evidence $manifest.database
$escapedBackupPath = $databaseBackupPath.Replace("'", "''")
$fileListQuery = "SET NOCOUNT ON; RESTORE FILELISTONLY FROM DISK = N'$escapedBackupPath';"
$fileList = @(& $sqlcmd -S $script:SqlInstance -E -C -b -h -1 -W -s '|' -Q $fileListQuery)
if ($LASTEXITCODE -ne 0 -or $fileList.Count -lt 2) {
    throw 'RESTORE FILELISTONLY did not return both data and log files.'
}

$dataLine = $fileList | Where-Object { ($_ -split '\|')[2].Trim() -eq 'D' } | Select-Object -First 1
$logLine = $fileList | Where-Object { ($_ -split '\|')[2].Trim() -eq 'L' } | Select-Object -First 1
$dataLogicalName = (($dataLine -split '\|')[0]).Trim()
$logLogicalName = (($logLine -split '\|')[0]).Trim()
if ([string]::IsNullOrWhiteSpace($dataLogicalName) -or [string]::IsNullOrWhiteSpace($logLogicalName)) {
    throw 'Unable to identify the logical SQL data and log file names.'
}

$verificationRoot = Join-Path ([IO.Path]::GetFullPath($VerificationDataRoot)) $manifest.backupSetId
New-Item -ItemType Directory -Path $verificationRoot -Force | Out-Null
$databaseDataPath = Join-Path $verificationRoot "$VerificationDatabaseName.mdf"
$databaseLogPath = Join-Path $verificationRoot "$VerificationDatabaseName`_log.ldf"
$escapedDataPath = $databaseDataPath.Replace("'", "''")
$escapedLogPath = $databaseLogPath.Replace("'", "''")
$escapedDataLogicalName = $dataLogicalName.Replace("'", "''")
$escapedLogLogicalName = $logLogicalName.Replace("'", "''")
$restoreQuery = "IF DB_ID(N'$VerificationDatabaseName') IS NOT NULL THROW 51000, 'Verification database already exists.', 1; RESTORE DATABASE [$VerificationDatabaseName] FROM DISK = N'$escapedBackupPath' WITH MOVE N'$escapedDataLogicalName' TO N'$escapedDataPath', MOVE N'$escapedLogLogicalName' TO N'$escapedLogPath', CHECKSUM, RECOVERY; DBCC CHECKDB ([$VerificationDatabaseName]) WITH NO_INFOMSGS;"

try {
    & $sqlcmd -S $script:SqlInstance -E -C -b -Q $restoreQuery
    if ($LASTEXITCODE -ne 0) {
        throw "Restore verification failed for '$VerificationDatabaseName'."
    }

    if ($null -ne $manifest.files) {
        $filesArchivePath = Assert-FileEvidence -Root $resolvedSetDirectory -Evidence $manifest.files
        $filesRestoreRoot = Join-Path $verificationRoot 'files'
        Expand-Archive -LiteralPath $filesArchivePath -DestinationPath $filesRestoreRoot
    }

    $manifest.lastRestoreVerification = [ordered]@{
        verifiedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        result = 'success'
        verificationDatabase = $VerificationDatabaseName
    }
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding utf8
}
catch {
    $manifest.lastRestoreVerification = [ordered]@{
        verifiedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        result = 'failed'
        verificationDatabase = $VerificationDatabaseName
    }
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding utf8
    throw
}

Write-Host "Restore verification succeeded: $VerificationDatabaseName"
Write-Host "Verification files: $verificationRoot"
