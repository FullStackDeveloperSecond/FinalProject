[CmdletBinding()]
param(
    [string] $RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryPath = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$failures = [Collections.Generic.List[string]]::new()
$nugetPackageCount = 0
$npmDirectPackageCount = 0
$npmLockedPackageCount = 0

function Add-Failure {
    param([Parameter(Mandatory)][string] $Message)
    $failures.Add($Message)
}

function Test-ExactVersion {
    param([Parameter(Mandatory)][string] $Version)
    return $Version -match '^\d+(\.\d+){1,3}([+-][0-9A-Za-z.-]+)?$'
}

function Get-JsonPropertyValue {
    param(
        [Parameter(Mandatory)][object] $Object,
        [Parameter(Mandatory)][string] $Name
    )

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

$nugetConfigPath = Join-Path $repositoryPath 'NuGet.config'
if (-not (Test-Path -LiteralPath $nugetConfigPath -PathType Leaf)) {
    Add-Failure "NuGet.config is required at '$nugetConfigPath'."
}
else {
    [xml] $nugetConfig = Get-Content -Raw -LiteralPath $nugetConfigPath
    $packageSources = @($nugetConfig.configuration.packageSources.add)
    if ($null -eq $nugetConfig.configuration.packageSources.clear) {
        Add-Failure 'NuGet.config must clear inherited package sources.'
    }

    if ($packageSources.Count -ne 1) {
        Add-Failure 'NuGet.config must define exactly one package source.'
    }
    else {
        $source = $packageSources[0]
        $sourceUri = $null
        if (-not [Uri]::TryCreate([string] $source.value, [UriKind]::Absolute, [ref] $sourceUri) -or
            $sourceUri.Scheme -ne 'https' -or
            $sourceUri.Host -ne 'api.nuget.org' -or
            $sourceUri.AbsolutePath -ne '/v3/index.json') {
            Add-Failure 'NuGet source must be the official HTTPS v3 endpoint https://api.nuget.org/v3/index.json.'
        }

        if ([string] $source.key -ne 'nuget.org') {
            Add-Failure "NuGet source key must be 'nuget.org'."
        }
    }

    $mappings = @($nugetConfig.configuration.packageSourceMapping.packageSource)
    if ($mappings.Count -ne 1 -or [string] $mappings[0].key -ne 'nuget.org') {
        Add-Failure "Package source mapping must contain only 'nuget.org'."
    }
    else {
        $patterns = @($mappings[0].package | ForEach-Object { [string] $_.pattern })
        if ($patterns.Count -ne 1 -or $patterns[0] -ne '*') {
            Add-Failure "The nuget.org source mapping must contain only the '*' pattern."
        }
    }
}

$centralVersionsPath = Join-Path $repositoryPath 'Directory.Packages.props'
$centralVersions = @{}
if (-not (Test-Path -LiteralPath $centralVersionsPath -PathType Leaf)) {
    Add-Failure "Directory.Packages.props is required at '$centralVersionsPath'."
}
else {
    [xml] $centralFile = Get-Content -Raw -LiteralPath $centralVersionsPath
    $centralEnabled = @(@($centralFile.Project.PropertyGroup.ManagePackageVersionsCentrally) |
        Where-Object { ([string] $_).Trim().ToLowerInvariant() -eq 'true' })
    if ($centralEnabled.Count -eq 0) {
        Add-Failure 'ManagePackageVersionsCentrally must be true.'
    }

    foreach ($packageVersion in @($centralFile.Project.ItemGroup.PackageVersion)) {
        $packageId = [string] $packageVersion.Include
        $version = [string] $packageVersion.Version
        if ([string]::IsNullOrWhiteSpace($packageId)) {
            Add-Failure 'Every PackageVersion must have an Include value.'
            continue
        }

        if ($centralVersions.ContainsKey($packageId)) {
            Add-Failure "Duplicate central NuGet version entry '$packageId'."
            continue
        }

        if (-not (Test-ExactVersion -Version $version)) {
            Add-Failure "NuGet package '$packageId' must use an exact version, found '$version'."
        }

        $centralVersions[$packageId] = $version
    }
}

$referencedNugetPackages = @{}
foreach ($projectFile in @(Get-ChildItem -LiteralPath $repositoryPath -Recurse -File -Filter '*.csproj')) {
    [xml] $project = Get-Content -Raw -LiteralPath $projectFile.FullName
    foreach ($reference in @($project.SelectNodes('//PackageReference'))) {
        $packageId = $reference.GetAttribute('Include')
        if ([string]::IsNullOrWhiteSpace($packageId)) {
            Add-Failure "PackageReference without Include in '$($projectFile.FullName)'."
            continue
        }

        if (-not [string]::IsNullOrWhiteSpace($reference.GetAttribute('Version')) -or
            -not [string]::IsNullOrWhiteSpace($reference.GetAttribute('VersionOverride'))) {
            Add-Failure "PackageReference '$packageId' in '$($projectFile.Name)' must not override the central version."
        }

        if (-not $centralVersions.ContainsKey($packageId)) {
            Add-Failure "PackageReference '$packageId' in '$($projectFile.Name)' has no central version entry."
        }

        $referencedNugetPackages[$packageId] = $true
    }
}

foreach ($packageId in $centralVersions.Keys) {
    if (-not $referencedNugetPackages.ContainsKey($packageId)) {
        Add-Failure "Central NuGet version '$packageId' is not referenced by any project."
    }
}
$nugetPackageCount = $referencedNugetPackages.Count

$requiredNpmConfigPaths = @(
    (Join-Path $repositoryPath 'frontend/customer-web/.npmrc')
    (Join-Path $repositoryPath 'frontend/admin-web/.npmrc')
)
foreach ($npmConfigPath in $requiredNpmConfigPaths) {
    if (-not (Test-Path -LiteralPath $npmConfigPath -PathType Leaf)) {
        Add-Failure ".npmrc is required beside each npm manifest; missing '$npmConfigPath'."
        continue
    }

    $npmConfigLines = @(Get-Content -LiteralPath $npmConfigPath |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_ -and -not $_.StartsWith('#') })
    $registrySettings = @($npmConfigLines |
        Where-Object { $_ -match '(^|:)registry\s*=' })
    if ($registrySettings.Count -ne 1 -or $registrySettings[0] -ne 'registry=https://registry.npmjs.org/') {
        Add-Failure "'$npmConfigPath' must define exactly one registry: https://registry.npmjs.org/."
    }

    $strictScriptSettings = @($npmConfigLines |
        Where-Object { $_ -match '^strict-allow-scripts\s*=' })
    if ($strictScriptSettings.Count -ne 1 -or $strictScriptSettings[0] -ne 'strict-allow-scripts=true') {
        Add-Failure "'$npmConfigPath' must enforce strict-allow-scripts=true."
    }

    $dangerousScriptSettings = @($npmConfigLines |
        Where-Object { $_ -match '^dangerously-allow-all-scripts\s*=\s*true$' })
    if ($dangerousScriptSettings.Count -gt 0) {
        Add-Failure "'$npmConfigPath' must not bypass script review with dangerously-allow-all-scripts=true."
    }
}

$allowedNpmConfigPaths = @{}
foreach ($requiredNpmConfigPath in $requiredNpmConfigPaths) {
    $allowedNpmConfigPaths[[IO.Path]::GetFullPath($requiredNpmConfigPath)] = $true
}
$additionalNpmConfigs = @(Get-ChildItem -LiteralPath $repositoryPath -Recurse -File -Force -Filter '.npmrc' |
    Where-Object {
        -not $allowedNpmConfigPaths.ContainsKey([IO.Path]::GetFullPath($_.FullName)) -and
        $_.FullName -notmatch '[\\/]node_modules[\\/]'
    })
foreach ($additionalNpmConfig in $additionalNpmConfigs) {
    Add-Failure "Only package-level .npmrc files beside the two frontend manifests are allowed; found '$($additionalNpmConfig.FullName)'."
}
$allowedLocalPackages = @{
    '@doselect/web-shared' = 'file:../shared'
}
$lockFiles = @(Get-ChildItem -LiteralPath (Join-Path $repositoryPath 'frontend') -Recurse -File -Filter 'package-lock.json')
if ($lockFiles.Count -eq 0) {
    Add-Failure 'At least one npm package-lock.json is required.'
}

foreach ($lockFile in $lockFiles) {
    $manifestPath = Join-Path $lockFile.DirectoryName 'package.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        Add-Failure "package.json is missing beside '$($lockFile.FullName)'."
        continue
    }

    $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
    # Windows PowerShell 5 cannot parse an empty JSON property name. npm lockfile v3
    # uses one for the root package, so rename only that parser-local key.
    $lockJson = (Get-Content -Raw -LiteralPath $lockFile.FullName) -replace
        '(?m)^(\s*)"":\s*\{', '$1"__doselectRoot__": {'
    $lock = $lockJson | ConvertFrom-Json
    if ([int] $lock.lockfileVersion -ne 3) {
        Add-Failure "'$($lockFile.FullName)' must use lockfileVersion 3."
    }

    $allowScripts = Get-JsonPropertyValue -Object $manifest -Name 'allowScripts'
    if ($null -ne $allowScripts) {
        foreach ($approval in $allowScripts.PSObject.Properties) {
            if ($approval.Value -isnot [bool]) {
                Add-Failure "npm install-script policy '$($approval.Name)' in '$manifestPath' must be boolean."
                continue
            }

            if ($approval.Value -eq $true) {
                if ($approval.Name -notmatch '^(@[^/]+/[^@]+|[^@/]+)@\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') {
                    Add-Failure "npm install-script approval '$($approval.Name)' in '$manifestPath' must use an exact package@version key."
                }
            }
            elseif ($approval.Name -notmatch '^(@[^/]+/[^@]+|[^@/]+)$') {
                Add-Failure "npm install-script denial '$($approval.Name)' in '$manifestPath' must use a name-only package key."
            }
        }
    }

    $rootLock = Get-JsonPropertyValue -Object $lock.packages -Name '__doselectRoot__'
    if ($null -eq $rootLock) {
        Add-Failure "'$($lockFile.FullName)' has no root package entry."
        continue
    }

    foreach ($sectionName in @('dependencies', 'devDependencies')) {
        $manifestSection = Get-JsonPropertyValue -Object $manifest -Name $sectionName
        if ($null -eq $manifestSection) {
            continue
        }

        $rootLockSection = Get-JsonPropertyValue -Object $rootLock -Name $sectionName
        foreach ($dependency in $manifestSection.PSObject.Properties) {
            $packageId = $dependency.Name
            $declaredVersion = [string] $dependency.Value
            $lockedDirectVersion = if ($null -eq $rootLockSection) {
                $null
            }
            else {
                Get-JsonPropertyValue -Object $rootLockSection -Name $packageId
            }

            if ([string] $lockedDirectVersion -ne $declaredVersion) {
                Add-Failure "npm package '$packageId' in '$manifestPath' does not match its root lock entry."
            }

            if ($allowedLocalPackages.ContainsKey($packageId)) {
                if ($declaredVersion -ne $allowedLocalPackages[$packageId]) {
                    Add-Failure "Local npm package '$packageId' must use '$($allowedLocalPackages[$packageId])'."
                }
            }
            elseif (-not (Test-ExactVersion -Version $declaredVersion)) {
                Add-Failure "npm package '$packageId' in '$manifestPath' must use an exact version, found '$declaredVersion'."
            }

            $npmDirectPackageCount++
        }
    }

    foreach ($packageEntry in $lock.packages.PSObject.Properties) {
        if ($packageEntry.Name -eq '__doselectRoot__') {
            continue
        }

        $package = $packageEntry.Value
        $npmLockedPackageCount++
        if ($packageEntry.Name -eq '../shared') {
            $localPackageName = [string] (Get-JsonPropertyValue -Object $package -Name 'name')
            if ($localPackageName -ne '@doselect/web-shared') {
                Add-Failure "The only approved local npm package is '@doselect/web-shared'."
            }
            continue
        }

        $isLink = Get-JsonPropertyValue -Object $package -Name 'link'
        $resolved = [string] (Get-JsonPropertyValue -Object $package -Name 'resolved')
        if ($isLink -eq $true) {
            if ($packageEntry.Name -ne 'node_modules/@doselect/web-shared' -or
                $resolved -ne '../shared') {
                Add-Failure "Unapproved local or linked npm package '$($packageEntry.Name)' in '$($lockFile.FullName)'."
            }
            continue
        }

        $resolvedUri = $null
        if (-not [Uri]::TryCreate($resolved, [UriKind]::Absolute, [ref] $resolvedUri) -or
            $resolvedUri.Scheme -ne 'https' -or
            $resolvedUri.Host -ne 'registry.npmjs.org') {
            Add-Failure "npm lock entry '$($packageEntry.Name)' must resolve from https://registry.npmjs.org/."
        }

        $integrity = [string] (Get-JsonPropertyValue -Object $package -Name 'integrity')
        if ($integrity -notmatch '^(sha256|sha384|sha512)-[A-Za-z0-9+/=]+$') {
            Add-Failure "npm lock entry '$($packageEntry.Name)' must contain an approved integrity hash."
        }
    }
}

if ($failures.Count -gt 0) {
    Write-Error ("Package source verification failed:`n- " + ($failures -join "`n- "))
    exit 1
}

Write-Output "Package source verification passed."
Write-Output "NuGet direct packages: $nugetPackageCount (central exact versions; nuget.org only)."
Write-Output "npm direct declarations: $npmDirectPackageCount (exact versions or approved local workspace package)."
Write-Output "npm locked entries: $npmLockedPackageCount (registry.npmjs.org HTTPS plus integrity, or approved local link)."
$global:LASTEXITCODE = 0
