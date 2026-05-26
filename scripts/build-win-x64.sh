#!/usr/bin/env bash
set -euo pipefail

CONFIGURATION="${1:-Release}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

echo "Publishing SalesforceDynamicsGpIntegration for win-x64 (framework-dependent)..."
dotnet publish "${PROJECT_ROOT}/SalesforceDynamicsGpIntegration.csproj" \
  -c "${CONFIGURATION}" \
  -p:PublishProfile=WinX64FrameworkDependent

echo "Publish completed at ${PROJECT_ROOT}/publish/win-x64"
