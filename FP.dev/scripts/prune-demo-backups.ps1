[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter()]
    [string]$BackupRoot = 'E:\FinalProjectBackups',

    [Parameter()]
    [ValidateRange(1, 31)]
    [int]$DailyCount = 7,

    [Parameter()]
    [ValidateRange(1, 12)]
    [int]$WeeklyCount = 4
)

$ErrorActionPreference = 'Stop'
$resolvedRoot = [IO.Path]::GetFullPath($BackupRoot)
if (-not (Test-Path -LiteralPath $resolvedRoot -PathType Container)) {
    Write-Host "Backup root does not exist: $resolvedRoot"
    return
}

$sets = @(Get-ChildItem -LiteralPath $resolvedRoot -Directory | ForEach-Object {
    $manifestPath = Join-Path $_.FullName 'manifest.json'
    if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
        $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
        if ($manifest.schemaVersion -eq 1 -and $manifest.result -eq 'success') {
            $restoreVerified = $manifest.lastRestoreVerification.result -eq 'success'
            $hasExplicitFileRecoveryResult =
                $null -ne $manifest.lastRestoreVerification -and
                $manifest.lastRestoreVerification.PSObject.Properties.Name -contains 'fileRecoveryResult'
            $fileRecoveryVerified = if ($hasExplicitFileRecoveryResult) {
                $manifest.lastRestoreVerification.fileRecoveryResult -eq 'success'
            }
            else {
                # Backward compatibility: legacy manifests can authorize pruning only when a
                # file archive existed and the overall restore verification succeeded.
                $restoreVerified -and $null -ne $manifest.files
            }

            [pscustomobject]@{
                Directory = $_.FullName
                CreatedAtUtc = [DateTimeOffset]::Parse($manifest.createdAtUtc)
                RestoreVerified = $restoreVerified
                FileRecoveryVerified = $fileRecoveryVerified
            }
        }
    }
})

$daily = @($sets |
    Group-Object { $_.CreatedAtUtc.UtcDateTime.ToString('yyyy-MM-dd') } |
    ForEach-Object { $_.Group | Sort-Object CreatedAtUtc -Descending | Select-Object -First 1 } |
    Sort-Object CreatedAtUtc -Descending |
    Select-Object -First $DailyCount)
$weekly = @($sets |
    Group-Object { '{0}-{1:D2}' -f [Globalization.ISOWeek]::GetYear($_.CreatedAtUtc.UtcDateTime), [Globalization.ISOWeek]::GetWeekOfYear($_.CreatedAtUtc.UtcDateTime) } |
    ForEach-Object { $_.Group | Sort-Object CreatedAtUtc -Descending | Select-Object -First 1 } |
    Sort-Object CreatedAtUtc -Descending |
    Select-Object -First $WeeklyCount)
$keep = @($daily + $weekly | Select-Object -ExpandProperty Directory -Unique)

if (-not ($sets | Where-Object {
    $_.RestoreVerified -and $_.FileRecoveryVerified -and $keep -contains $_.Directory
})) {
    throw 'No retained complete Backup Set has successful database and file recovery verification; pruning is refused.'
}

foreach ($set in $sets | Where-Object { $keep -notcontains $_.Directory }) {
    $resolvedSet = [IO.Path]::GetFullPath($set.Directory)
    $rootPrefix = [IO.Path]::TrimEndingDirectorySeparator($resolvedRoot) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedSet.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Resolved Backup Set escaped BackupRoot: $resolvedSet"
    }

    if ($PSCmdlet.ShouldProcess($resolvedSet, 'Delete expired complete Backup Set')) {
        Remove-Item -LiteralPath $resolvedSet -Recurse -Force
    }
}
