param(
    [string]$InstallPath = "$env:ProgramFiles\MaxSfGpSync",
    [string]$TaskName = "MaxSfGpSync",
    [switch]$RegisterStartup
)

$ErrorActionPreference = "Stop"

$appSettingsPath = Join-Path $InstallPath "appsettings.json"
if (-not (Test-Path $appSettingsPath)) {
    throw "appsettings.json was not found at $appSettingsPath"
}

Write-Host "Updating runtime configuration in $appSettingsPath"

$config = Get-Content $appSettingsPath -Raw | ConvertFrom-Json

$sqlServer = Read-Host "SQL Server (example: SERVER\\INSTANCE,PORT)"
$sqlDatabase = Read-Host "SQL Database"
$sqlUser = Read-Host "SQL User"
$sqlPassword = Read-Host "SQL Password"

$sfLoginUrl = Read-Host "Salesforce Login URL"
$sfClientId = Read-Host "Salesforce Client ID"
$sfClientSecret = Read-Host "Salesforce Client Secret"
$sfUsername = Read-Host "Salesforce Username"
$sfPassword = Read-Host "Salesforce Password"
$sfToken = Read-Host "Salesforce Security Token"

$config.ConnectionStrings.DynamicsGP = "Server=$sqlServer;Database=$sqlDatabase;User Id=$sqlUser;Password=$sqlPassword;TrustServerCertificate=True;"
$config.Salesforce.LoginUrl = $sfLoginUrl
$config.Salesforce.ClientId = $sfClientId
$config.Salesforce.ClientSecret = $sfClientSecret
$config.Salesforce.Username = $sfUsername
$config.Salesforce.Password = $sfPassword
$config.Salesforce.SecurityToken = $sfToken

$config | ConvertTo-Json -Depth 16 | Set-Content -Path $appSettingsPath -Encoding UTF8
Write-Host "Configuration updated."

if ($RegisterStartup) {
    $exePath = Join-Path $InstallPath "SalesforceDynamicsGpIntegration.exe"
    if (-not (Test-Path $exePath)) {
        throw "Executable not found at $exePath"
    }

    # Legacy/manual path. The MSI now creates/removes this scheduled task automatically.
    $currentUser = "$env:USERDOMAIN\$env:USERNAME"
    $quotedExe = '"' + $exePath + '"'
    $createTaskArgs = @(
        "/Create",
        "/TN", $TaskName,
        "/SC", "ONLOGON",
        "/TR", $quotedExe,
        "/RL", "HIGHEST",
        "/F",
        "/RU", $currentUser
    )

    schtasks.exe @createTaskArgs | Out-Null
    Write-Host "Scheduled task '$TaskName' created for user $currentUser."
}
