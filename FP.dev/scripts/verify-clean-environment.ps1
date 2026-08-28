[CmdletBinding()]
param(
    [Parameter()]
    [switch]$RunVerification
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

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

$null = Get-RequiredCommand -Name 'npm'
$sqlStatus = Test-SqlServerConnection
if (-not $sqlStatus.IsReady) {
    throw $sqlStatus.Detail
}

$configurationTemplate = Join-Path $script:ProjectRoot 'src\backend\DoSelect.Api\appsettings.Development.example.json'
if (-not (Test-Path -LiteralPath $configurationTemplate -PathType Leaf)) {
    throw "Development configuration template is missing: $configurationTemplate"
}

Write-Host "Prerequisites passed: .NET $requiredSdk, Node.js $requiredNode, SQL Server .\SQL2025."
if (-not $RunVerification) {
    Write-Host 'Use -RunVerification on a fresh clone to run restore, build, tests, lint and production builds.'
    return
}

Push-Location $script:ProjectRoot
try {
    & dotnet restore DoSelect.slnx --configfile NuGet.config --no-cache -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }
    & dotnet build DoSelect.slnx --no-restore -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed.' }
    & dotnet test DoSelect.slnx --no-build --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'dotnet test failed.' }

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
    }
}
finally {
    Pop-Location
}

Write-Host 'Clean-environment verification passed.'
