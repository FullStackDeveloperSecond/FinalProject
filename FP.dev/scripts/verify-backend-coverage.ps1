[CmdletBinding()]
param(
    [Parameter()]
    [string]$CoverageRoot = '.coverage/backend',

    [Parameter()]
    [ValidateRange(0, 100)]
    [decimal]$MinimumLineRate = 70
)

$ErrorActionPreference = 'Stop'
$targetPackages = @(
    'DoSelect.Domain',
    'DoSelect.Application'
)

$resolvedRoot = Join-Path (Get-Location) $CoverageRoot
if (-not (Test-Path -LiteralPath $resolvedRoot -PathType Container)) {
    throw "Coverage directory does not exist: $resolvedRoot"
}

$coverageFiles = @(Get-ChildItem -LiteralPath $resolvedRoot -Recurse -Filter 'coverage.cobertura.xml')
if ($coverageFiles.Count -eq 0) {
    throw "No coverage.cobertura.xml files were found under: $resolvedRoot"
}

$lineHits = @{}
$lineFiles = @{}
function Get-NormalizedSourceFile {
    param(
        [Parameter(Mandatory)][string]$PackageName,
        [Parameter(Mandatory)][string]$FileName
    )

    $normalized = $FileName.Replace('\', '/')
    $projectMarker = "$PackageName/"
    $markerIndex = $normalized.LastIndexOf(
        $projectMarker,
        [StringComparison]::OrdinalIgnoreCase)
    if ($markerIndex -ge 0) {
        return $normalized.Substring($markerIndex + $projectMarker.Length)
    }

    return $normalized.TrimStart('/')
}

foreach ($coverageFile in $coverageFiles) {
    [xml]$coverageDocument = Get-Content -LiteralPath $coverageFile.FullName
    foreach ($package in @($coverageDocument.coverage.packages.package)) {
        if ($targetPackages -notcontains [string]$package.name) {
            continue
        }

        foreach ($class in @($package.classes.class)) {
            $sourceFile = Get-NormalizedSourceFile `
                -PackageName ([string]$package.name) `
                -FileName ([string]$class.filename)
            foreach ($line in @($class.lines.line)) {
                $lineKey = '{0}|{1}|{2}' -f $package.name, $sourceFile, $line.number
                $hits = [int]$line.hits
                if (-not $lineHits.ContainsKey($lineKey) -or $hits -gt $lineHits[$lineKey]) {
                    $lineHits[$lineKey] = $hits
                }
                $lineFiles[$lineKey] = '{0}|{1}' -f $package.name, $sourceFile
            }
        }
    }
}

if ($lineHits.Count -eq 0) {
    throw 'Coverage reports did not contain executable lines for DoSelect.Domain or DoSelect.Application.'
}

$coveredLines = @($lineHits.Values | Where-Object { $_ -gt 0 }).Count
$totalLines = $lineHits.Count
$lineRate = [math]::Round(($coveredLines / $totalLines) * 100, 2)

Write-Host "Domain + Application line coverage: $coveredLines / $totalLines ($lineRate%)"
Write-Host "Required minimum: $MinimumLineRate%"

if ($lineRate -lt $MinimumLineRate) {
    Write-Host 'Lowest-covered files with at least 20 executable lines:'
    $lineHits.Keys |
        Group-Object { $lineFiles[$_] } |
        ForEach-Object {
            $covered = @($_.Group | Where-Object { $lineHits[$_] -gt 0 }).Count
            [pscustomobject]@{
                File = $_.Name
                Covered = $covered
                Total = $_.Count
                Rate = [math]::Round(($covered / $_.Count) * 100, 2)
            }
        } |
        Where-Object { $_.Total -ge 20 } |
        Sort-Object Rate, Total |
        Select-Object -First 15 |
        Format-Table -AutoSize
    throw "Domain + Application line coverage $lineRate% is below the required $MinimumLineRate%."
}
