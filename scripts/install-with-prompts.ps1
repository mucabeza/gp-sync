param(
    [Parameter(Mandatory = $true)]
    [string]$MsiPath
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $MsiPath)) {
    throw "MSI not found at $MsiPath"
}

Write-Host "Enter installation values. Press Enter to keep an empty value."

$dynamicsConnection = Read-Host "Dynamics GP connection string"
$loginUrl = Read-Host "Salesforce Login URL"
$clientId = Read-Host "Salesforce Client ID"
$clientSecret = Read-Host "Salesforce Client Secret"
$username = Read-Host "Salesforce Username"
$password = Read-Host "Salesforce Password"
$securityToken = Read-Host "Salesforce Security Token"

$arguments = @(
    "/i",
    "`"$MsiPath`"",
    "MAXSFGP_CONNECTIONSTRINGS__DYNAMICSGP=$dynamicsConnection",
    "MAXSFGP_SALESFORCE__LOGINURL=$loginUrl",
    "MAXSFGP_SALESFORCE__CLIENTID=$clientId",
    "MAXSFGP_SALESFORCE__CLIENTSECRET=$clientSecret",
    "MAXSFGP_SALESFORCE__USERNAME=$username",
    "MAXSFGP_SALESFORCE__PASSWORD=$password",
    "MAXSFGP_SALESFORCE__SECURITYTOKEN=$securityToken"
)

Write-Host "Starting MSI installation..."
Start-Process -FilePath "msiexec.exe" -ArgumentList $arguments -Wait -NoNewWindow
Write-Host "Installation finished."
