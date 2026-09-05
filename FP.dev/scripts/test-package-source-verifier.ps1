[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$verifierPath = Join-Path $PSScriptRoot 'verify-package-sources.ps1'
$enginePath = (Get-Process -Id $PID).Path
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ("doselect-package-source-" + [Guid]::NewGuid().ToString('N'))

function Copy-VerificationInputs {
    param([Parameter(Mandatory)][string] $Destination)

    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    foreach ($relativeFile in @('NuGet.config', 'Directory.Packages.props')) {
        Copy-Item -LiteralPath (Join-Path $repositoryRoot $relativeFile) -Destination (Join-Path $Destination $relativeFile)
    }

    $sourceFiles = @(
        Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'src') -Recurse -File -Filter '*.csproj'
        Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'tests') -Recurse -File -Filter '*.csproj'
        Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'tools') -Recurse -File -Filter '*.csproj'
        Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'frontend') -Recurse -File -Force |
            Where-Object {
                $_.Name -in @('package.json', 'package-lock.json', '.npmrc') -and
                $_.FullName -notmatch '[\\/]node_modules[\\/]'
            }
    )
    foreach ($sourceFile in $sourceFiles) {
        $relativePath = $sourceFile.FullName.Substring($repositoryRoot.Length).TrimStart('\', '/')
        $destinationPath = Join-Path $Destination $relativePath
        New-Item -ItemType Directory -Path (Split-Path -Parent $destinationPath) -Force | Out-Null
        Copy-Item -LiteralPath $sourceFile.FullName -Destination $destinationPath
    }
}

function Invoke-Verifier {
    param([Parameter(Mandatory)][string] $TargetRoot)

    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = @(& $enginePath -NoProfile -NonInteractive -File $verifierPath -RepositoryRoot $TargetRoot 2>&1)
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    return [pscustomobject]@{
        ExitCode = $exitCode
        Output = ($output -join "`n")
    }
}

try {
    $validRoot = Join-Path $fixtureRoot 'valid'
    Copy-VerificationInputs -Destination $validRoot
    $validResult = Invoke-Verifier -TargetRoot $validRoot
    if ($validResult.ExitCode -ne 0) {
        throw "Valid fixture failed:`n$($validResult.Output)"
    }

    $invalidNugetRoot = Join-Path $fixtureRoot 'invalid-nuget'
    Copy-VerificationInputs -Destination $invalidNugetRoot
    $invalidNugetPath = Join-Path $invalidNugetRoot 'NuGet.config'
    $invalidNuget = [IO.File]::ReadAllText($invalidNugetPath).Replace(
        'https://api.nuget.org/v3/index.json',
        'https://packages.example.invalid/v3/index.json')
    [IO.File]::WriteAllText($invalidNugetPath, $invalidNuget, [Text.UTF8Encoding]::new($false))
    $invalidNugetResult = Invoke-Verifier -TargetRoot $invalidNugetRoot
    if ($invalidNugetResult.ExitCode -eq 0) {
        throw 'Verifier did not reject an unapproved NuGet source.'
    }

    $invalidNpmRoot = Join-Path $fixtureRoot 'invalid-npm'
    Copy-VerificationInputs -Destination $invalidNpmRoot
    $invalidLockPath = Join-Path $invalidNpmRoot 'frontend/customer-web/package-lock.json'
    $invalidLock = [IO.File]::ReadAllText($invalidLockPath).Replace(
        'https://registry.npmjs.org/',
        'https://registry.example.invalid/')
    [IO.File]::WriteAllText($invalidLockPath, $invalidLock, [Text.UTF8Encoding]::new($false))
    $invalidNpmResult = Invoke-Verifier -TargetRoot $invalidNpmRoot
    if ($invalidNpmResult.ExitCode -eq 0) {
        throw 'Verifier did not reject an unapproved npm lock source.'
    }

    $invalidUnexpectedNpmRoot = Join-Path $fixtureRoot 'invalid-unexpected-npmrc'
    Copy-VerificationInputs -Destination $invalidUnexpectedNpmRoot
    $unexpectedNpmConfigPath = Join-Path $invalidUnexpectedNpmRoot 'frontend/shared/.npmrc'
    [IO.File]::WriteAllText(
        $unexpectedNpmConfigPath,
        'registry=https://registry.example.invalid/',
        [Text.UTF8Encoding]::new($false))
    $invalidUnexpectedNpmResult = Invoke-Verifier -TargetRoot $invalidUnexpectedNpmRoot
    if ($invalidUnexpectedNpmResult.ExitCode -eq 0) {
        throw 'Verifier did not reject an unexpected npm registry override.'
    }

    $missingNpmConfigRoot = Join-Path $fixtureRoot 'missing-package-npmrc'
    Copy-VerificationInputs -Destination $missingNpmConfigRoot
    Remove-Item -LiteralPath (Join-Path $missingNpmConfigRoot 'frontend/admin-web/.npmrc') -Force
    $missingNpmConfigResult = Invoke-Verifier -TargetRoot $missingNpmConfigRoot
    if ($missingNpmConfigResult.ExitCode -eq 0) {
        throw 'Verifier did not reject a missing package-level npm configuration.'
    }
    $invalidStrictScriptsRoot = Join-Path $fixtureRoot 'invalid-strict-scripts'
    Copy-VerificationInputs -Destination $invalidStrictScriptsRoot
    $invalidStrictNpmConfigPath = Join-Path $invalidStrictScriptsRoot 'frontend/customer-web/.npmrc'
    $originalStrictNpmConfig = [IO.File]::ReadAllText($invalidStrictNpmConfigPath)
    $invalidStrictNpmConfig = $originalStrictNpmConfig.Replace(
        'strict-allow-scripts=true',
        'strict-allow-scripts=false')
    if ($invalidStrictNpmConfig -eq $originalStrictNpmConfig) {
        throw 'Strict install-script fixture could not be prepared.'
    }
    [IO.File]::WriteAllText(
        $invalidStrictNpmConfigPath,
        $invalidStrictNpmConfig,
        [Text.UTF8Encoding]::new($false))
    $invalidStrictScriptsResult = Invoke-Verifier -TargetRoot $invalidStrictScriptsRoot
    if ($invalidStrictScriptsResult.ExitCode -eq 0) {
        throw 'Verifier did not reject disabled strict install-script enforcement.'
    }
    $dangerousScriptRoot = Join-Path $fixtureRoot 'dangerous-global-script-approval'
    Copy-VerificationInputs -Destination $dangerousScriptRoot
    $dangerousNpmConfigPath = Join-Path $dangerousScriptRoot 'frontend/admin-web/.npmrc'
    $dangerousNpmConfig = [IO.File]::ReadAllText($dangerousNpmConfigPath) +
        "dangerously-allow-all-scripts=true`n"
    [IO.File]::WriteAllText(
        $dangerousNpmConfigPath,
        $dangerousNpmConfig,
        [Text.UTF8Encoding]::new($false))
    $dangerousScriptResult = Invoke-Verifier -TargetRoot $dangerousScriptRoot
    if ($dangerousScriptResult.ExitCode -eq 0) {
        throw 'Verifier did not reject dangerous global install-script approval.'
    }

    $invalidScriptApprovalRoot = Join-Path $fixtureRoot 'invalid-script-approval'
    Copy-VerificationInputs -Destination $invalidScriptApprovalRoot
    $invalidScriptManifestPath = Join-Path $invalidScriptApprovalRoot 'frontend/customer-web/package.json'
    $originalScriptManifest = [IO.File]::ReadAllText($invalidScriptManifestPath)
    $invalidScriptManifest = $originalScriptManifest.Replace(
        '"vue-demi@0.14.10": true',
        '"vue-demi": true')
    if ($invalidScriptManifest -eq $originalScriptManifest) {
        throw 'Install-script approval fixture could not be prepared.'
    }
    [IO.File]::WriteAllText(
        $invalidScriptManifestPath,
        $invalidScriptManifest,
        [Text.UTF8Encoding]::new($false))
    $invalidScriptApprovalResult = Invoke-Verifier -TargetRoot $invalidScriptApprovalRoot
    if ($invalidScriptApprovalResult.ExitCode -eq 0) {
        throw 'Verifier did not reject a non-versioned install-script approval.'
    }

    $invalidVersionedDenialRoot = Join-Path $fixtureRoot 'invalid-versioned-script-denial'
    Copy-VerificationInputs -Destination $invalidVersionedDenialRoot
    $invalidDenialManifestPath = Join-Path $invalidVersionedDenialRoot 'frontend/admin-web/package.json'
    $originalDenialManifest = [IO.File]::ReadAllText($invalidDenialManifestPath)
    $invalidDenialManifest = $originalDenialManifest.Replace(
        '"fsevents": false',
        '"fsevents@2.3.3": false')
    if ($invalidDenialManifest -eq $originalDenialManifest) {
        throw 'Install-script denial fixture could not be prepared.'
    }
    [IO.File]::WriteAllText(
        $invalidDenialManifestPath,
        $invalidDenialManifest,
        [Text.UTF8Encoding]::new($false))
    $invalidVersionedDenialResult = Invoke-Verifier -TargetRoot $invalidVersionedDenialRoot
    if ($invalidVersionedDenialResult.ExitCode -eq 0) {
        throw 'Verifier did not reject a version-pinned install-script denial.'
    }

    Write-Output 'Package source verifier self-test passed: valid fixture accepted; invalid NuGet, npm lock, unexpected or missing package npm config, disabled strict-script policy, dangerous global approval, non-versioned approval, and version-pinned denial rejected.'
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
}

$global:LASTEXITCODE = 0
