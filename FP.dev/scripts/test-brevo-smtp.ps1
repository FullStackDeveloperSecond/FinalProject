param(
    [ValidateNotNullOrEmpty()]
    [string] $RecipientAddress = 'alexyang920528@gmail.com',

    [ValidateRange(1000, 60000)]
    [int] $TimeoutMilliseconds = 15000
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'common.ps1')

function Get-UserSecretsConfiguration {
    param(
        [Parameter(Mandatory)]
        [string] $ProjectPath
    )

    $dotnet = Get-RequiredCommand -Name 'dotnet.exe'
    $output = @(& $dotnet user-secrets list --json --project $ProjectPath 2>$null)
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to read .NET User Secrets for DoSelect.Api.'
    }

    $json = ($output | Where-Object { $_ -notmatch '^\s*//(?:BEGIN|END)\s*$' }) -join [Environment]::NewLine
    if ([string]::IsNullOrWhiteSpace($json)) {
        throw 'No .NET User Secrets were found for DoSelect.Api.'
    }

    return $json | ConvertFrom-Json
}

function Get-RequiredSecretValue {
    param(
        [Parameter(Mandatory)]
        [object] $Configuration,

        [Parameter(Mandatory)]
        [string] $Key
    )

    $property = $Configuration.PSObject.Properties[$Key]
    if ($null -eq $property -or [string]::IsNullOrWhiteSpace([string] $property.Value)) {
        throw "Required User Secrets key '$Key' is missing."
    }

    return [string] $property.Value
}

$apiProject = Join-Path $script:ProjectRoot 'src\backend\DoSelect.Api\DoSelect.Api.csproj'
$configuration = $null
$smtpPassword = $null
$message = $null
$client = $null

try {
    $configuration = Get-UserSecretsConfiguration -ProjectPath $apiProject
    $smtpHost = Get-RequiredSecretValue -Configuration $configuration -Key 'Email:SmtpHost'
    $smtpPortText = Get-RequiredSecretValue -Configuration $configuration -Key 'Email:SmtpPort'
    $smtpUserName = Get-RequiredSecretValue -Configuration $configuration -Key 'Email:UserName'
    $smtpPassword = Get-RequiredSecretValue -Configuration $configuration -Key 'Email:Password'
    $senderAddress = Get-RequiredSecretValue -Configuration $configuration -Key 'Email:SenderAddress'
    $emailEnabled = Get-RequiredSecretValue -Configuration $configuration -Key 'Features:EmailEnabled'

    $smtpPort = 0
    if (-not [int]::TryParse($smtpPortText, [ref] $smtpPort) -or $smtpPort -lt 1 -or $smtpPort -gt 65535) {
        throw "User Secrets key 'Email:SmtpPort' is invalid."
    }

    $enabled = $false
    if (-not [bool]::TryParse($emailEnabled, [ref] $enabled) -or -not $enabled) {
        throw "User Secrets key 'Features:EmailEnabled' must be true for the Brevo test."
    }

    $null = [Net.Mail.MailAddress]::new($RecipientAddress)
    $from = [Net.Mail.MailAddress]::new($senderAddress, 'alex')
    $to = [Net.Mail.MailAddress]::new($RecipientAddress)
    $timestamp = [DateTimeOffset]::Now.ToString('yyyy-MM-dd HH:mm:ss zzz')

    $message = [Net.Mail.MailMessage]::new($from, $to)
    $message.Subject = '[DoSelect] Brevo SMTP verification'
    $message.Body = "DoSelect Brevo SMTP verification succeeded at $timestamp. This message contains no customer data or authentication token."
    $message.SubjectEncoding = [Text.Encoding]::UTF8
    $message.BodyEncoding = [Text.Encoding]::UTF8
    $message.IsBodyHtml = $false

    $client = [Net.Mail.SmtpClient]::new($smtpHost, $smtpPort)
    $client.EnableSsl = $true
    $client.UseDefaultCredentials = $false
    $client.Credentials = [Net.NetworkCredential]::new($smtpUserName, $smtpPassword)
    $client.Timeout = $TimeoutMilliseconds

    Write-Host 'Sending one Brevo SMTP verification message...' -ForegroundColor Cyan
    $client.Send($message)
    Write-Host "Brevo accepted the verification message for $RecipientAddress." -ForegroundColor Green
    Write-Host 'Confirm final delivery in the inbox and Brevo transactional log before marking TECH-04 verified.'
    exit 0
}
catch {
    $failureType = $_.Exception.GetType().Name
    Write-Error "Brevo SMTP verification failed ($failureType). Check the verified sender, SMTP username/key, network, and Brevo transactional log. Secret values were not printed."
    exit 1
}
finally {
    if ($null -ne $message) {
        $message.Dispose()
    }

    if ($null -ne $client) {
        $client.Dispose()
    }

    $smtpPassword = $null
    $configuration = $null
}
