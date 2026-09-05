$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'common.ps1')

$apiProject = Join-Path $script:ProjectRoot 'src\backend\DoSelect.Api\DoSelect.Api.csproj'
$dotnet = Get-RequiredCommand -Name 'dotnet.exe'

Write-Host 'Configure OpenAI credentials and the verified 2026-09-02 model prices for local AI evaluation.' -ForegroundColor Cyan
Write-Host 'The API key is stored in .NET User Secrets and must not be pasted into chat, source files, screenshots, or logs.' -ForegroundColor Yellow
Write-Host 'Product search: gpt-5.6-luna, input USD 0.20 / 1M tokens, output USD 1.20 / 1M tokens.'
Write-Host 'AI support: gpt-5.6-terra, input USD 2.00 / 1M tokens, output USD 12.00 / 1M tokens.'

$secureApiKey = Read-Host 'OpenAI API key (input is hidden)' -AsSecureString
$keyPointer = [IntPtr]::Zero
$apiKey = $null

try {
    $keyPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureApiKey)
    $apiKey = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($keyPointer)
    if ([string]::IsNullOrWhiteSpace($apiKey)) {
        Write-Error 'OpenAI API key is required.'
        exit 1
    }

    $settings = [ordered]@{
        'OpenAI:ApiKey' = $apiKey
        'OpenAI:ProductSearchInputCostPerMillionTokens' = '0.20'
        'OpenAI:ProductSearchOutputCostPerMillionTokens' = '1.20'
        'OpenAI:SupportInputCostPerMillionTokens' = '2.00'
        'OpenAI:SupportOutputCostPerMillionTokens' = '12.00'
    }

    foreach ($setting in $settings.GetEnumerator()) {
        $null = & $dotnet user-secrets set $setting.Key $setting.Value --project $apiProject
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to store User Secrets key '$($setting.Key)'."
        }
    }

    Write-Host 'OpenAI evaluation settings were stored for the current Windows user.' -ForegroundColor Green
    Write-Host 'This script does not enable the application AI feature and does not make an OpenAI request.'
    exit 0
}
finally {
    $apiKey = $null
    if ($keyPointer -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($keyPointer)
    }
}
