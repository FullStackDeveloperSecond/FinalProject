[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$apiProject = Join-Path $projectRoot 'src\backend\DoSelect.Api'
$dotnet = 'C:\Users\alexy\.dotnet\dotnet.exe'

if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) {
    throw "The pinned .NET SDK executable was not found at '$dotnet'."
}

function Set-SeedSecret {
    param(
        [Parameter(Mandatory)]
        [string] $Key,

        [Parameter(Mandatory)]
        [string] $Prompt
    )

    while ($true) {
        $secureValue = Read-Host -Prompt $Prompt -AsSecureString
        $valuePointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureValue)
        $plainValue = $null
        try {
            $plainValue = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($valuePointer)
            $isValid =
                -not [string]::IsNullOrWhiteSpace($plainValue) -and
                $plainValue.Length -ge 6 -and
                $plainValue -cmatch '\p{Lu}' -and
                $plainValue -cmatch '\p{Ll}' -and
                $plainValue -match '\p{Nd}' -and
                $plainValue -match '[^\p{L}\p{Nd}]'

            if (-not $isValid) {
                Write-Warning 'Password must have at least 6 characters and include uppercase, lowercase, number, and special characters.'
                continue
            }

            & $dotnet user-secrets set $Key $plainValue --project $apiProject | Out-Null
            if ($LASTEXITCODE -ne 0) {
                throw "Failed to store User Secrets key '$Key'."
            }

            return
        }
        finally {
            if ($null -ne $plainValue) {
                $plainValue = $null
            }

            [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($valuePointer)
        }
    }
}

Write-Host 'Passwords require at least 6 characters with uppercase, lowercase, number, and special characters.'
Set-SeedSecret -Key 'Seed:AdminPassword' -Prompt 'Seed admin password'
Set-SeedSecret -Key 'Seed:MemberPassword' -Prompt 'Seed member password'

Write-Host 'Seed passwords were stored in .NET User Secrets. No values were written to the repository.'
