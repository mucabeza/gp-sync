param(
    [Parameter(Mandatory = $true)]
    [string]$MsiPath,
    [string]$CertificatePath,
    [string]$CertificatePassword,
    [string]$CertificateThumbprint,
    [string]$TimestampUrl = "http://timestamp.digicert.com",
    [string]$DigestAlgorithm = "SHA256"
)

$ErrorActionPreference = "Stop"

if (-not $IsWindows) {
    throw "MSI signing must run on Windows."
}

if (-not (Test-Path $MsiPath)) {
    throw "MSI file not found: $MsiPath"
}

$signtool = Get-Command signtool.exe -ErrorAction SilentlyContinue
if (-not $signtool) {
    throw "signtool.exe was not found. Install Windows SDK signing tools and retry."
}

if ([string]::IsNullOrWhiteSpace($CertificatePath) -and [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
    throw "Provide either -CertificatePath (PFX) or -CertificateThumbprint (cert store)."
}

$arguments = @(
    "sign",
    "/fd", $DigestAlgorithm,
    "/td", $DigestAlgorithm,
    "/tr", $TimestampUrl
)

if (-not [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
    $arguments += @("/sha1", $CertificateThumbprint, "/sm")
} else {
    $arguments += @("/f", $CertificatePath)
    if (-not [string]::IsNullOrWhiteSpace($CertificatePassword)) {
        $arguments += @("/p", $CertificatePassword)
    }
}

$arguments += $MsiPath

Write-Host "Signing MSI: $MsiPath"
& $signtool.Source @arguments
if ($LASTEXITCODE -ne 0) {
    throw "signtool.exe failed with exit code $LASTEXITCODE"
}

Write-Host "MSI signature applied successfully."
