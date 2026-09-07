[CmdletBinding()]
param(
    [switch] $SkipExecutableProbe
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

$scriptNames = @(
    'configure-seed-secrets.ps1'
    'seed-minimal-development-data.ps1'
    'smoke-api-database.ps1'
)

foreach ($scriptName in $scriptNames) {
    $scriptPath = Join-Path $PSScriptRoot $scriptName
    $tokens = $null
    $parseErrors = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile(
        $scriptPath,
        [ref] $tokens,
        [ref] $parseErrors)

    if ($parseErrors.Count -ne 0) {
        throw "$scriptName has PowerShell parse errors: $($parseErrors.Message -join '; ')"
    }

    $hardcodedDotNet = @($ast.FindAll({
        param($node)
        $node -is [Management.Automation.Language.StringConstantExpressionAst] -and
        $node.Value -match '(?i)^[A-Z]:\\Users\\[^\\]+\\\.dotnet\\dotnet\.exe$'
    }, $true))
    if ($hardcodedDotNet.Count -ne 0) {
        throw "$scriptName contains a user-specific dotnet executable path."
    }

    $resolverCalls = @($ast.FindAll({
        param($node)
        $node -is [Management.Automation.Language.CommandAst] -and
        $node.GetCommandName() -eq 'Get-RequiredCommand' -and
        $node.Extent.Text -match "-Name\s+'dotnet\.exe'"
    }, $true))
    if ($resolverCalls.Count -ne 1) {
        throw "$scriptName must resolve dotnet.exe exactly once through Get-RequiredCommand."
    }
}

if (-not $SkipExecutableProbe) {
    $pathDotNet = Get-Command 'dotnet.exe' -CommandType Application -ErrorAction Stop
    $cleanPath = Split-Path -Parent $pathDotNet.Source
    $requiredSdk = [string](
        Get-Content -Raw -LiteralPath (Join-Path $script:ProjectRoot 'global.json') |
            ConvertFrom-Json).sdk.version
    $previousPath = $env:PATH

    Push-Location $script:ProjectRoot
    try {
        $env:PATH = $cleanPath
        $resolvedDotNet = Get-RequiredCommand -Name 'dotnet.exe'
        $actualSdk = (& $resolvedDotNet --version).Trim()
        if ($LASTEXITCODE -ne 0 -or $actualSdk -ne $requiredSdk) {
            throw "Clean-PATH dotnet resolution expected SDK $requiredSdk but received $actualSdk."
        }
    }
    finally {
        $env:PATH = $previousPath
        Pop-Location
    }
}

Write-Output "Dotnet script resolution tests passed for $($scriptNames.Count) scripts."
