$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'common.ps1')

$apiProject = Join-Path $script:ProjectRoot 'src\backend\DoSelect.Api\DoSelect.Api.csproj'
$dotnet = Get-RequiredCommand -Name 'dotnet.exe'

Write-Host 'Configure Brevo SMTP for the current Windows user.' -ForegroundColor Cyan
Write-Host 'Do not paste the SMTP key into chat, source files, screenshots, or logs.' -ForegroundColor Yellow

$smtpUserName = Read-Host 'Brevo SMTP username'
if ([string]::IsNullOrWhiteSpace($smtpUserName)) {
    Write-Error 'Brevo SMTP username is required.'
    exit 1
}

$secureSmtpKey = Read-Host 'Brevo SMTP key (input is hidden)' -AsSecureString
$keyPointer = [IntPtr]::Zero
$smtpKey = $null

try {
    $keyPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureSmtpKey)
    $smtpKey = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($keyPointer)
    if ([string]::IsNullOrWhiteSpace($smtpKey)) {
        Write-Error 'Brevo SMTP key is required.'
        exit 1
    }

    $settings = [ordered]@{
        'Features:EmailEnabled' = 'true'
        'Email:SmtpHost' = 'smtp-relay.brevo.com'
        'Email:SmtpPort' = '587'
        'Email:UserName' = $smtpUserName
        'Email:Password' = $smtpKey
        'Email:SenderAddress' = 'alexyang920528@gmail.com'
    }

    foreach ($setting in $settings.GetEnumerator()) {
        $null = & $dotnet user-secrets set $setting.Key $setting.Value --project $apiProject
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to store User Secrets key '$($setting.Key)'."
        }
    }

    Write-Host 'Brevo settings were stored in .NET User Secrets for the current Windows user.' -ForegroundColor Green
    Write-Host "Next: .\scripts\test-brevo-smtp.ps1"
    exit 0
}
finally {
    $smtpKey = $null
    if ($keyPointer -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($keyPointer)
    }
}
