[CmdletBinding()]
param(
    [ValidateSet('customer-chromium', 'admin-chromium')]
    [string] $Project = 'customer-chromium',

    [string] $JourneyTitle
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($JourneyTitle)) {
    $JourneyTitle = if ($Project -eq 'admin-chromium') {
        'a seeded administrator can enroll TOTP, reject a wrong code, and sign in again'
    }
    else {
        'a public shopper can use AI search safely when the provider is disabled'
    }
}

$projectRoot = Split-Path -Parent $PSScriptRoot
$apiProject = Join-Path $projectRoot 'src\backend\DoSelect.Api'
$infrastructureProject = Join-Path $projectRoot 'src\backend\DoSelect.Infrastructure'
$customerWeb = Join-Path $projectRoot 'frontend\customer-web'
$databaseName = "DoSelectE2E_$([Guid]::NewGuid().ToString('N'))"
$dataRoot = Join-Path ([IO.Path]::GetTempPath()) $databaseName
$databaseCreated = $false

if ($databaseName -notmatch '^DoSelectE2E_[0-9a-f]{32}$') {
    throw "Refusing to use an invalid E2E database name '$databaseName'."
}

$connectionTemplate = $env:DOSELECT_SQLSERVER_TEST_CONNECTION
if ([string]::IsNullOrWhiteSpace($connectionTemplate)) {
    $connectionTemplate = 'Server=.\SQL2025;Integrated Security=True;TrustServerCertificate=True;MultipleActiveResultSets=True;Encrypt=False;'
}

$connectionBuilder = [System.Data.SqlClient.SqlConnectionStringBuilder]::new($connectionTemplate)
$connectionBuilder['Initial Catalog'] = $databaseName
$connectionBuilder['TrustServerCertificate'] = $true
$connectionString = $connectionBuilder.ConnectionString

$masterBuilder = [System.Data.SqlClient.SqlConnectionStringBuilder]::new($connectionString)
$masterBuilder['Initial Catalog'] = 'master'
$masterConnectionString = $masterBuilder.ConnectionString

function Test-DatabaseExists {
    param(
        [Parameter(Mandatory)]
        [string] $MasterConnectionString,

        [Parameter(Mandatory)]
        [string] $Name
    )

    $connection = [System.Data.SqlClient.SqlConnection]::new($MasterConnectionString)
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = 'SELECT CASE WHEN DB_ID(@databaseName) IS NULL THEN 0 ELSE 1 END;'
        $null = $command.Parameters.Add('@databaseName', [System.Data.SqlDbType]::NVarChar, 128)
        $command.Parameters['@databaseName'].Value = $Name
        return [int] $command.ExecuteScalar() -eq 1
    }
    finally {
        $connection.Dispose()
    }
}

function Remove-E2eDatabase {
    param(
        [Parameter(Mandatory)]
        [string] $MasterConnectionString,

        [Parameter(Mandatory)]
        [string] $Name
    )

    if ($Name -notmatch '^DoSelectE2E_[0-9a-f]{32}$') {
        throw "Refusing to delete database '$Name' because it is not an owned E2E database."
    }

    if (-not (Test-DatabaseExists -MasterConnectionString $MasterConnectionString -Name $Name)) {
        return
    }

    $connection = [System.Data.SqlClient.SqlConnection]::new($MasterConnectionString)
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandTimeout = 30
        $command.CommandText = "ALTER DATABASE [$Name] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$Name];"
        $null = $command.ExecuteNonQuery()
    }
    finally {
        $connection.Dispose()
    }
}

$previousConnectionString = $env:ConnectionStrings__DefaultConnection
$previousEnvironment = $env:ASPNETCORE_ENVIRONMENT
$previousAdminPassword = $env:Seed__AdminPassword
$previousMemberPassword = $env:Seed__MemberPassword
$previousDataRoot = $env:E2E_STORAGE_DATA_ROOT
$previousReuseExistingServer = $env:E2E_REUSE_EXISTING_SERVER
$previousApiEnvironment = $env:E2E_ASPNETCORE_ENVIRONMENT
$previousBackgroundJobsEnabled = $env:E2E_BACKGROUND_JOBS_ENABLED
$previousSimulationEndpointsEnabled = $env:E2E_SIMULATION_ENDPOINTS_ENABLED

Push-Location $projectRoot
try {
    if (Test-DatabaseExists -MasterConnectionString $masterConnectionString -Name $databaseName) {
        throw "Generated E2E database '$databaseName' already exists."
    }

    $env:ConnectionStrings__DefaultConnection = $connectionString
    $env:ASPNETCORE_ENVIRONMENT = 'E2E'
    $env:Seed__AdminPassword = 'E2e_Admin_123!'
    $env:Seed__MemberPassword = 'E2e_Member_123!'
    $env:E2E_STORAGE_DATA_ROOT = $dataRoot
    $env:E2E_REUSE_EXISTING_SERVER = 'false'
    $requiresPaymentCompletionInfrastructure =
        $JourneyTitle -eq 'a guest completes the prepared cart through checkout payment and invoice' -or
        $JourneyTitle -eq 'a seeded administrator can enroll TOTP, reject a wrong code, and sign in again' -or
        $JourneyTitle -eq 'H-R02 fulfills COD home delivery and store pickup exactly once'
    $env:E2E_ASPNETCORE_ENVIRONMENT = 'E2E'
    if ($requiresPaymentCompletionInfrastructure) {
        $env:E2E_BACKGROUND_JOBS_ENABLED = 'true'
        $env:E2E_SIMULATION_ENDPOINTS_ENABLED = 'true'
    }
    else {
        $env:E2E_BACKGROUND_JOBS_ENABLED = 'false'
        $env:E2E_SIMULATION_ENDPOINTS_ENABLED = 'false'
    }

    Write-Host "Preparing isolated E2E database '$databaseName'."
    & dotnet tool run dotnet-ef -- database update `
        --project $infrastructureProject `
        --startup-project $apiProject `
        --context DoSelectDbContext
    if ($LASTEXITCODE -ne 0) {
        throw 'Applying migrations to the isolated E2E database failed.'
    }

    $databaseCreated = Test-DatabaseExists -MasterConnectionString $masterConnectionString -Name $databaseName
    if (-not $databaseCreated) {
        throw "Migration command did not create expected E2E database '$databaseName'."
    }

    & dotnet run --project $apiProject --no-build --no-launch-profile -- --seed-minimal
    if ($LASTEXITCODE -ne 0) {
        throw 'Minimal E2E seed failed.'
    }

    Push-Location $customerWeb
    try {
        & npm run test:e2e -- --project $Project --grep $JourneyTitle
        if ($LASTEXITCODE -ne 0) {
            throw "E2E journey '$JourneyTitle' in project '$Project' failed."
        }
    }
    finally {
        Pop-Location
    }
}
finally {
    try {
        if ($databaseCreated -or (Test-DatabaseExists -MasterConnectionString $masterConnectionString -Name $databaseName)) {
            Write-Host "Removing isolated E2E database '$databaseName'."
            Remove-E2eDatabase -MasterConnectionString $masterConnectionString -Name $databaseName
        }

        $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        $resolvedDataRoot = [IO.Path]::GetFullPath($dataRoot)
        if ($resolvedDataRoot.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and
            (Split-Path -Leaf $resolvedDataRoot) -eq $databaseName -and
            (Test-Path -LiteralPath $resolvedDataRoot)) {
            Remove-Item -LiteralPath $resolvedDataRoot -Recurse -Force
        }
    }
    finally {
        $env:ConnectionStrings__DefaultConnection = $previousConnectionString
        $env:ASPNETCORE_ENVIRONMENT = $previousEnvironment
        $env:Seed__AdminPassword = $previousAdminPassword
        $env:Seed__MemberPassword = $previousMemberPassword
        $env:E2E_STORAGE_DATA_ROOT = $previousDataRoot
        $env:E2E_REUSE_EXISTING_SERVER = $previousReuseExistingServer
        $env:E2E_ASPNETCORE_ENVIRONMENT = $previousApiEnvironment
        $env:E2E_BACKGROUND_JOBS_ENABLED = $previousBackgroundJobsEnabled
        $env:E2E_SIMULATION_ENDPOINTS_ENABLED = $previousSimulationEndpointsEnabled
        Pop-Location
    }
}
