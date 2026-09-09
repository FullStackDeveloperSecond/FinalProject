[CmdletBinding()]
param(
    [ValidatePattern('^DoSelectDemo_[0-9a-fA-F]{32}$')]
    [string] $DatabaseName
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
if (-not $DatabaseName) {
    $demoState = Read-DemoDatabaseState
    if ($null -eq $demoState) { throw 'Prepare an isolated Demo database first.' }
    $DatabaseName = [string] $demoState.DatabaseName
}
Assert-IsolatedDemoDatabaseName -DatabaseName $DatabaseName
$packDirectory = Join-Path $script:RunRoot ('demo-login-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $packDirectory
# Restrict the directory before writing any credentials; no inheritance from a broad workspace ACL.
$packAcl = [System.Security.AccessControl.DirectorySecurity]::new()
$packAcl.SetAccessRuleProtection($true, $false)
$packOwner = [System.Security.Principal.WindowsIdentity]::GetCurrent().User
$packAcl.SetOwner($packOwner)
foreach ($sid in @($packOwner, [System.Security.Principal.SecurityIdentifier]::new('S-1-5-18'))) {
    $rule = [System.Security.AccessControl.FileSystemAccessRule]::new($sid, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
    $packAcl.AddAccessRule($rule)
}
Set-Acl -LiteralPath $packDirectory -AclObject $packAcl
$packPath = Join-Path $packDirectory 'demo-login-pack.json'
$previous = @{}
$settings = @{
    ASPNETCORE_ENVIRONMENT = 'Development'
    ConnectionStrings__DefaultConnection = (New-DemoConnectionString -DatabaseName $DatabaseName)
    Features__AiEnabled = 'false'
    Features__EmailEnabled = 'false'
    Features__BackgroundJobsEnabled = 'false'
    Demo__LoginPackPath = $packPath
}
try {
    foreach ($key in $settings.Keys) {
        $previous[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
        [Environment]::SetEnvironmentVariable($key, $settings[$key], 'Process')
    }
    $buildDirectory = Join-Path $script:ProjectRoot '.tmp\demo-login-export'
    & dotnet build (Join-Path $script:ProjectRoot 'src\backend\DoSelect.Api') --no-restore -p:UseAppHost=false -o $buildDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Demo export build failed.' }
    Push-Location (Join-Path $script:ProjectRoot 'src\backend\DoSelect.Api')
    try { & dotnet (Join-Path $buildDirectory 'DoSelect.Api.dll') --export-demo-login-pack }
    finally { Pop-Location }
    if ($LASTEXITCODE -ne 0) { throw 'Demo login export failed; no account password was reset.' }
    Write-Host "本機 Demo 登入工作包：$packPath"
    Write-Host '此檔含測試帳號密碼，只限測試員保管，不可提交 Git 或公開分享。登入頁載入後可一鍵填寫，仍需按登入及完成 MFA。'
}
finally {
    foreach ($key in $settings.Keys) { [Environment]::SetEnvironmentVariable($key, $previous[$key], 'Process') }
}
