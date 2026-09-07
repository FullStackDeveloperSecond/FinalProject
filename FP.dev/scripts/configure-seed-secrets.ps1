[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

$apiProject = Join-Path $script:ProjectRoot 'src\backend\DoSelect.Api'
$dotnet = Get-RequiredCommand -Name 'dotnet.exe'

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

Push-Location $script:ProjectRoot
try {
    Write-Host 'Passwords require at least 6 characters with uppercase, lowercase, number, and special characters.'
    Set-SeedSecret -Key 'Seed:AdminPassword' -Prompt 'Seed admin password'
    Set-SeedSecret -Key 'Seed:MemberPassword' -Prompt 'Seed member password'

    Write-Host 'Seed passwords were stored in .NET User Secrets. No values were written to the repository.'
}
finally {
    Pop-Location
}
