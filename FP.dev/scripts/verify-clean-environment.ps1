[CmdletBinding()]
param(
    [Parameter()]
    [switch]$RunVerification
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

$null = Get-RequiredCommand -Name 'git.exe'
$globalJson = Get-Content -Raw -LiteralPath (Join-Path $script:ProjectRoot 'global.json') | ConvertFrom-Json
$requiredSdk = [string]$globalJson.sdk.version
$actualSdk = (& dotnet --version).Trim()
if ($LASTEXITCODE -ne 0 -or $actualSdk -ne $requiredSdk) {
    throw "Required .NET SDK is $requiredSdk; current SDK is $actualSdk."
}

$requiredNode = (Get-Content -Raw -LiteralPath (Join-Path $script:ProjectRoot '.nvmrc')).Trim().TrimStart('v')
$actualNode = (& node --version).Trim().TrimStart('v')
$nodeMatches = if ($requiredNode.Contains('.')) {
    $actualNode -eq $requiredNode
}
else {
    $actualNode.Split('.')[0] -eq $requiredNode
}
if ($LASTEXITCODE -ne 0 -or -not $nodeMatches) {
    throw "Required Node.js is $requiredNode; current Node.js is $actualNode."
}

$npm = Get-RequiredCommand -Name 'npm.cmd'
$actualNpm = (& $npm --version).Trim()
if ($LASTEXITCODE -ne 0 -or $actualNpm.Split('.')[0] -ne '11') {
    throw "Required npm major is 11; current npm is $actualNpm."
}

$sqlStatus = Test-SqlServerConnection
if (-not $sqlStatus.IsReady) {
    throw $sqlStatus.Detail
}

$configurationTemplate = Join-Path $script:ProjectRoot 'src\backend\DoSelect.Api\appsettings.Development.example.json'
if (-not (Test-Path -LiteralPath $configurationTemplate -PathType Leaf)) {
    throw "Development configuration template is missing: $configurationTemplate"
}

Write-Host "Prerequisites passed: .NET $requiredSdk, Node.js $requiredNode, npm 11, SQL Server .\SQL2025."
if (-not $RunVerification) {
    Write-Host 'Use -RunVerification on a fresh clone to run restore, build, tests, lint and production builds.'
    return
}

Push-Location $script:ProjectRoot
try {
    $trackedChangesBefore = @(& git status --porcelain --untracked-files=no)
    if ($LASTEXITCODE -ne 0) { throw 'git status failed.' }
    if ($trackedChangesBefore.Count -gt 0) {
        throw 'RunVerification requires a clean tracked worktree; commit or restore tracked changes first.'
    }

    & (Join-Path $script:ProjectRoot 'scripts\verify-package-sources.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'package source policy verification failed.' }
    & dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'dotnet tool restore failed.' }
    & dotnet restore DoSelect.slnx --configfile NuGet.config --no-cache -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }
    & dotnet build DoSelect.slnx --no-restore -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed.' }
    & dotnet format DoSelect.slnx --verify-no-changes --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'dotnet format verification failed.' }
    & dotnet test DoSelect.slnx --no-build --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'dotnet test failed.' }
    & dotnet list DoSelect.slnx package --vulnerable --include-transitive --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'NuGet vulnerability audit failed.' }

    foreach ($application in @('customer-web', 'admin-web')) {
        $applicationRoot = Join-Path $script:ProjectRoot "frontend\$application"
        & npm ci --prefix $applicationRoot
        if ($LASTEXITCODE -ne 0) { throw "npm ci failed for $application." }
        & npm run typecheck --prefix $applicationRoot
        if ($LASTEXITCODE -ne 0) { throw "typecheck failed for $application." }
        & npm run lint --prefix $applicationRoot -- --max-warnings 0
        if ($LASTEXITCODE -ne 0) { throw "lint failed for $application." }
        & npm run test:coverage --prefix $applicationRoot
        if ($LASTEXITCODE -ne 0) { throw "coverage tests failed for $application." }
        & npm run build --prefix $applicationRoot
        if ($LASTEXITCODE -ne 0) { throw "production build failed for $application." }
        & npm audit --prefix $applicationRoot --omit=dev --audit-level=high
        if ($LASTEXITCODE -ne 0) { throw "production dependency audit failed for $application." }
    }

    $trackedChanges = @(& git status --porcelain --untracked-files=no)
    if ($LASTEXITCODE -ne 0) { throw 'git status failed.' }
    if ($trackedChanges.Count -gt 0) {
        throw 'Verification changed tracked files; inspect the worktree before continuing.'
    }
}
finally {
    Pop-Location
}

Write-Host 'Clean-environment verification passed.'
