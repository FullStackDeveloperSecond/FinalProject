[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$apiProject = Join-Path $projectRoot 'src\backend\DoSelect.Api'
# 依序尋找 dotnet：DOTNET_ROOT（由 開發環境.ps1 指向專案內 .tools\dotnet）、
# 目前 PATH、最後才是原本寫死的路徑，這樣每個人的機器都能跑。
$dotnetCandidates = @()
if ($env:DOTNET_ROOT) { $dotnetCandidates += (Join-Path $env:DOTNET_ROOT 'dotnet.exe') }
$dotnetOnPath = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue
if ($dotnetOnPath) { $dotnetCandidates += $dotnetOnPath.Source }
$dotnetCandidates += 'C:\Users\alexy\.dotnet\dotnet.exe'

$dotnet = $dotnetCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1

if (-not $dotnet) {
    throw "No .NET SDK executable was found. Set DOTNET_ROOT or put dotnet on PATH (run 開發環境.ps1 first). Tried: $($dotnetCandidates -join '; ')"
}

$previousEnvironment = $env:ASPNETCORE_ENVIRONMENT
Push-Location $projectRoot
try {
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    & $dotnet run --project $apiProject --no-launch-profile -- --seed-minimal
    if ($LASTEXITCODE -ne 0) {
        throw 'Minimal development seed failed.'
    }
}
finally {
    $env:ASPNETCORE_ENVIRONMENT = $previousEnvironment
    Pop-Location
}
