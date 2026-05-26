param(
    [string]$Configuration = "Release",
    [switch]$SignMsi,
    [string]$CertificatePath,
    [string]$CertificatePassword,
    [string]$CertificateThumbprint,
    [string]$TimestampUrl = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"

if (-not $IsWindows) {
    throw "MSI build must run on Windows because WiX Heat requires Kernel32.dll."
}

$projectRoot = Split-Path -Parent $PSScriptRoot

& "$PSScriptRoot/build-win-x64.ps1" -Configuration $Configuration

dotnet build "$projectRoot/Installer/Installer.wixproj" -c $Configuration

$msiOutputRoot = Join-Path $projectRoot "Installer/bin/$Configuration"
$msiFile = Get-ChildItem -Path $msiOutputRoot -Filter *.msi -File -Recurse |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $msiFile) {
    throw "MSI output was not found under $msiOutputRoot"
}

if ($SignMsi) {
    & "$PSScriptRoot/sign-msi.ps1" `
        -MsiPath $msiFile.FullName `
        -CertificatePath $CertificatePath `
        -CertificatePassword $CertificatePassword `
        -CertificateThumbprint $CertificateThumbprint `
        -TimestampUrl $TimestampUrl
}

Write-Host "Installer build finished."
Write-Host "MSI output: $($msiFile.FullName)"
