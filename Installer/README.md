# MSI Installer Scaffold

This folder contains the initial WiX v4 setup project for building a Windows x64 MSI.

## Current status

- Packages files from `publish/win-x64` into MSI.
- Executes `dotnet publish` automatically before MSI build.
- Supports major upgrade behavior by using a fixed `UpgradeCode`.
- Includes an operator script at `scripts/configure-install.ps1` to prompt for required settings and optionally register startup execution.
- Uses WiX Heat harvesting, which requires running the installer build on Windows.
- Installs under `Program Files\\MaxSfGpSync`.
- Supports MSI property injection for runtime secrets via machine-level environment variables consumed by the app (`MAXSFGP_` prefix).
- Registers a startup scheduled task (`MaxSfGpSync`) during install and removes it during uninstall.
- Blocks installation when the x64 .NET runtime host is not present.
- Shows a native MSI dialog to collect configuration values during fresh install.
- Enforces required fields in the MSI dialog before allowing the installer to continue.

## Build command (on Windows with WiX Toolset SDK available)

```powershell
dotnet build .\Installer\Installer.wixproj -c Release
```

Or run:

```powershell
.\scripts\build-installer.ps1
```

### Build and sign MSI

Sign with a PFX file:

```powershell
.\scripts\build-installer.ps1 -SignMsi -CertificatePath C:\certs\company-signing.pfx -CertificatePassword "<pfx-password>"
```

Sign with a certificate from the local machine store:

```powershell
.\scripts\build-installer.ps1 -SignMsi -CertificateThumbprint "THUMBPRINT"
```

You can also sign an already-built MSI directly:

```powershell
.\scripts\sign-msi.ps1 -MsiPath .\Installer\bin\Release\MaxSfGpSync.msi -CertificateThumbprint "THUMBPRINT"
```

## CI build (GitHub Actions)

This repository includes a Windows CI workflow at `.github/workflows/build-installer.yml`.

- Triggers on pushes to `main`, tags like `v*`, and manual dispatch.
- Builds the MSI on `windows-latest`.
- Uploads MSI as a workflow artifact.
- Signs the MSI automatically when signing secrets are configured.

Supported signing secret options:

1. Certificate thumbprint in machine store:
	- `WIN_CERT_THUMBPRINT`
2. PFX certificate file:
	- `WIN_CERT_PFX_BASE64` (base64 content of `.pfx`)
	- `WIN_CERT_PFX_PASSWORD`

## Release automation (GitHub tags)

Tag pushes like `v1.0.0` run `.github/workflows/release-installer.yml`.

- Builds an unsigned MSI on Windows first.
- Runs a silent smoke test: install, task/env checks, uninstall, cleanup checks.
- Runs an upgrade smoke test when a previous release MSI exists: install previous MSI, upgrade to current MSI, validate, uninstall.
- If no signing secrets are configured, publishes unsigned MSI to the GitHub Release.
- If signing secrets are configured, requires the `production-signing` GitHub Environment approval before signing and publishing.
- Verifies Authenticode signature status for signed MSI before publishing release assets.
- Creates/updates the GitHub Release for the tag and uploads the MSI asset.
- Downloads the published release MSI asset again and runs a post-release install/uninstall verification on a fresh runner.
- Uses reusable composite action `.github/actions/msi-smoke-test` for shared silent install/uninstall assertions.
- Upgrade test now reuses the same composite action in two phases (install previous without uninstall, then upgrade to current with full cleanup validation).

### Configure environment approval gate

1. In GitHub, go to Settings > Environments and create `production-signing`.
2. Add required reviewers in that environment.
3. Add signing secrets (`WIN_CERT_THUMBPRINT` or `WIN_CERT_PFX_BASE64` + `WIN_CERT_PFX_PASSWORD`) to that environment.
4. Push a release tag (for example `v1.0.0`) and approve the pending environment deployment when prompted.

## Install with prompts

The MSI now includes a native configuration dialog. The wrapper below is still available for unattended/scripted installs that pass values from CLI.

Use this wrapper to collect values interactively and pass them to `msiexec`:

```powershell
.\scripts\install-with-prompts.ps1 -MsiPath .\Installer\bin\Release\MaxSfGpSync.msi
```

Equivalent direct install command:

```powershell
msiexec /i .\Installer\bin\Release\MaxSfGpSync.msi MAXSFGP_CONNECTIONSTRINGS__DYNAMICSGP="Server=...;Database=...;User Id=...;Password=...;TrustServerCertificate=True;" MAXSFGP_SALESFORCE__LOGINURL="https://your-org.my.salesforce.com" MAXSFGP_SALESFORCE__CLIENTID="..." MAXSFGP_SALESFORCE__CLIENTSECRET="..." MAXSFGP_SALESFORCE__USERNAME="..." MAXSFGP_SALESFORCE__PASSWORD="..." MAXSFGP_SALESFORCE__SECURITYTOKEN="..."
```

## Next implementation steps

1. Extract repeated MSI path-resolution steps into a second composite action to simplify workflow maintenance.

## Upgrade and uninstall validation checklist

1. Install v1 MSI and complete the configuration dialog.
2. Confirm environment variables exist in machine scope: `MAXSFGP_*`.
3. Confirm scheduled task exists: `schtasks /Query /TN MaxSfGpSync`.
4. Build v2 MSI with increased `Package Version` and install over v1.
5. Verify major upgrade replaces old product and task still exists only once.
6. Verify app starts successfully after logon via scheduled task.
7. Uninstall from Apps and Features.
8. Confirm scheduled task is removed.
9. Confirm `MAXSFGP_*` environment variables are removed.
10. Confirm install directory is removed (except user-added files, if any).
