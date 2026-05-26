param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot

Write-Host "Publishing SalesforceDynamicsGpIntegration for win-x64 (framework-dependent)..."
dotnet publish "$projectRoot/SalesforceDynamicsGpIntegration.csproj" `
  -c $Configuration `
  -p:PublishProfile=WinX64FrameworkDependent

Write-Host "Publish completed at $projectRoot/publish/win-x64"
