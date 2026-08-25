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
    foreach ($relativeFile in @('NuGet.config', '.npmrc', 'Directory.Packages.props')) {
        Copy-Item -LiteralPath (Join-Path $repositoryRoot $relativeFile) -Destination (Join-Path $Destination $relativeFile)
    }

    $sourceFiles = @(
        Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'src') -Recurse -File -Filter '*.csproj'
        Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'tests') -Recurse -File -Filter '*.csproj'
        Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'frontend') -Recurse -File |
            Where-Object {
                $_.Name -in @('package.json', 'package-lock.json') -and
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

    $output = @(& $enginePath -NoProfile -NonInteractive -File $verifierPath -RepositoryRoot $TargetRoot 2>&1)
    return [pscustomobject]@{
        ExitCode = $LASTEXITCODE
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

    $invalidNestedNpmRoot = Join-Path $fixtureRoot 'invalid-nested-npmrc'
    Copy-VerificationInputs -Destination $invalidNestedNpmRoot
    $nestedNpmConfigPath = Join-Path $invalidNestedNpmRoot 'frontend/customer-web/.npmrc'
    [IO.File]::WriteAllText(
        $nestedNpmConfigPath,
        'registry=https://registry.example.invalid/',
        [Text.UTF8Encoding]::new($false))
    $invalidNestedNpmResult = Invoke-Verifier -TargetRoot $invalidNestedNpmRoot
    if ($invalidNestedNpmResult.ExitCode -eq 0) {
        throw 'Verifier did not reject a nested npm registry override.'
    }

    Write-Output 'Package source verifier self-test passed: valid fixture accepted; invalid NuGet, npm lock, and nested npm sources rejected.'
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
}

$global:LASTEXITCODE = 0
