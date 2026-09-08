[CmdletBinding()]
param(
    [switch] $IncludeAiAnonymousIdentity
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

$apiProject = Join-Path $script:ProjectRoot 'src\backend\DoSelect.Api\DoSelect.Api.csproj'
$dotnet = Get-RequiredCommand -Name 'dotnet.exe'

function Set-RandomUserSecret {
    param(
        [Parameter(Mandatory)]
        [string] $Key
    )

    $secretBytes = New-Object byte[] 48
    $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
    $secretValue = $null
    try {
        $generator.GetBytes($secretBytes)
        $secretValue = [Convert]::ToBase64String($secretBytes)
        $null = & $dotnet user-secrets set $Key $secretValue --project $apiProject
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to store User Secrets key '$Key'."
        }
    }
    finally {
        $secretValue = $null
        [Array]::Clear($secretBytes, 0, $secretBytes.Length)
        $generator.Dispose()
    }
}

$keys = [Collections.Generic.List[string]]::new()
$keys.Add('GuestOrderAccess:Pepper')
$keys.Add('Idempotency:ActorScopePepper')
$keys.Add('Security:CouponGuestUsageHmacKeyV1')
if ($IncludeAiAnonymousIdentity) {
    $keys.Add('OpenAI:AnonymousIdentityPepper')
}

Write-Host 'Generating independent local security secrets for the current Windows user.' -ForegroundColor Cyan
Write-Host 'Secret values are written directly to .NET User Secrets and are never displayed.' -ForegroundColor Yellow

foreach ($key in $keys) {
    Set-RandomUserSecret -Key $key
}

Write-Host "Configured $($keys.Count) local security key(s)." -ForegroundColor Green
Write-Host 'Do not run or publish dotnet user-secrets list.' -ForegroundColor Yellow
