# Operations Runbook (1-Page)

## Purpose
This runbook explains how to verify and operate the MAX Salesforce GP Sync service in production.

## Scope
- Windows server hosting the app
- Scheduled task execution
- Salesforce and GP connectivity
- Daily operational checks

## Service Identity
- Scheduled task name: `\MaxSfGpSync`
- Run account: `NT AUTHORITY\SYSTEM`
- Default schedule: Daily at `00:10`
- Install folder: `C:\Program Files\SkyPlanner\GpSalesforceSync`

## Daily Health Check (5 minutes)
1. Confirm scheduled task exists and is enabled.

```powershell
schtasks /Query /TN \MaxSfGpSync /V /FO LIST
```

2. Confirm latest run result indicates success (or expected code).
3. Confirm today log file exists in `C:\Program Files\SkyPlanner\GpSalesforceSync\logs`.
4. Open newest log and confirm:
- Salesforce authentication succeeded
- Sync settings were loaded
- Pages processed, or `No active sync data settings found` (if expected)
5. If needed, run task manually:

```powershell
schtasks /Run /TN \MaxSfGpSync
```

## App Log Review (Code-Based)
Log behavior implemented by the app:
- Log folder is created under the app base directory: `logs`
- Log file name format is `yyyy-MM-dd_sync-log.txt` (or `yyyy-MM-dd_` + configured `Logger:LogFileName`)
- Log retention deletes `.txt` files older than `Logger:RetentionDays` (default 7)
- Log entry format is `[yyyy-MM-dd HH:mm:ss] [LEVEL] message`

Minimum messages to confirm a healthy run:
1. `Synchronization Service initialized`
2. `Starting synchronization process...`
3. `Attempting to connect to Salesforce...`
4. `Successfully authenticated with Salesforce.`
5. `Found N sync data settings`
6. `Total pages to process: N`
7. `Processing page X of Y`
8. `Successfully sent to Salesforce Page Number: X`
9. `Synchronization process completed`

Messages that indicate configuration or runtime issues:
1. `Salesforce Authentication Failed: ...`
2. `Failed to authenticate with Salesforce. Exiting...`
3. `Authentication failed. Status: ...`
4. `Salesforce Auth Error: ...`
5. `Failed to sync page X to Salesforce. Message: ...`
6. `Exception while syncing page X to Salesforce`
7. `Exception processing sync data settings`
8. `Fatal exception during synchronization`

If no log file is created after running the task:
1. Verify task action points to `SalesforceDynamicsGpIntegration.exe` in `C:\Program Files\SkyPlanner\GpSalesforceSync`
2. Verify the install folder and `logs` folder exist
3. Verify account permissions for the task run account (`SYSTEM`) to write in the install path

## Validation After Changes
1. Re-run task manually.
2. Confirm new execution appears in Task Scheduler history.
3. Confirm fresh entries in today's log.
4. Validate data arrival in Salesforce for a known test case.

## Final Admin Sign-Off - Configure appsettings.json in max-sf-gp
Configuration precedence used by the application:
1. Machine environment variables with prefix `MAXSFGP_` (highest priority)
2. Local `appsettings.json` fallback at `C:\Program Files\SkyPlanner\GpSalesforceSync\appsettings.json`

Important behavior:
1. The app loads `appsettings.json` first.
2. It supports sealed (encoded) secrets in `appsettings.json` and decodes them at runtime.
3. Machine-level environment variables override `appsettings.json` values at runtime.
4. Use machine scope (not user scope) because the scheduled task runs as `SYSTEM`.

Required keys and environment variable mapping:
1. `ConnectionStrings:DynamicsGP` -> `MAXSFGP_CONNECTIONSTRINGS__DYNAMICSGP`
2. `Salesforce:LoginUrl` -> `MAXSFGP_SALESFORCE__LOGINURL`
3. `Salesforce:ClientId` -> `MAXSFGP_SALESFORCE__CLIENTID`
4. `Salesforce:ClientSecret` -> `MAXSFGP_SALESFORCE__CLIENTSECRET`
5. `Salesforce:SyncEndPointName` -> `MAXSFGP_SALESFORCE__SYNCENDPOINTNAME`

**Auth model**: the app authenticates to Salesforce using the OAuth 2.0 **Client Credentials
Flow** (`grant_type=client_credentials`) — only `ClientId`/`ClientSecret` are needed, there is
no Salesforce username/password/security token in this app's config anymore. On the Salesforce
side, the Connected App (`App Manager` → the app → **Edit**) must have a **Run As** user set
under the **Client Credentials Flow** section — that's the Salesforce user the sync runs as.
If auth starts failing with `invalid_grant`, check that Run As user is active/not locked out and
that the Connected App's OAuth Policies (`Permitted Users`, `IP Relaxation`) are configured
permissively (`All users may self-authorize`, `Relax IP restrictions`) before assuming the
Client Secret is wrong.

How to set configuration (recommended in production):
1. Open PowerShell as Administrator.
2. Set machine-level environment variables:

```powershell
[Environment]::SetEnvironmentVariable("MAXSFGP_CONNECTIONSTRINGS__DYNAMICSGP", "Server=YOUR_SQL_SERVER;Database=YOUR_GP_DATABASE;User Id=YOUR_DB_USER;Password=YOUR_DB_PASSWORD;TrustServerCertificate=True;", "Machine")
[Environment]::SetEnvironmentVariable("MAXSFGP_SALESFORCE__LOGINURL", "https://your-org.my.salesforce.com", "Machine")
[Environment]::SetEnvironmentVariable("MAXSFGP_SALESFORCE__CLIENTID", "YOUR_CLIENT_ID", "Machine")
[Environment]::SetEnvironmentVariable("MAXSFGP_SALESFORCE__CLIENTSECRET", "YOUR_CLIENT_SECRET", "Machine")
[Environment]::SetEnvironmentVariable("MAXSFGP_SALESFORCE__SYNCENDPOINTNAME", "gp-data-sync", "Machine")
```

3. Validate values are present in machine scope:

```powershell
[Environment]::GetEnvironmentVariable("MAXSFGP_CONNECTIONSTRINGS__DYNAMICSGP", "Machine")
[Environment]::GetEnvironmentVariable("MAXSFGP_SALESFORCE__LOGINURL", "Machine")
[Environment]::GetEnvironmentVariable("MAXSFGP_SALESFORCE__CLIENTID", "Machine")
```

4. Keep `appsettings.json` with safe fallback defaults and non-secret operational settings.
5. Optional hardening: seal secrets in `appsettings.json` if storing local secrets is required.

```powershell
cd "C:\Program Files\SkyPlanner\GpSalesforceSync"
.\SalesforceDynamicsGpIntegration.exe --seal
```

6. Run the scheduled task manually and verify logs:

```powershell
schtasks /Run /TN \MaxSfGpSync
```

Final sign-off checklist:
1. All required `MAXSFGP_*` variables are set in Machine scope.
2. Task run under `SYSTEM` succeeds.
3. Log confirms successful Salesforce auth and page processing.
4. Test record reaches Salesforce.

## GP Sync Query (`GpSyncQuery.sql`)
The SQL query used to pull sales data from Dynamics GP is **not** hardcoded in the
app — it lives in `GpSyncQuery.sql`, next to `appsettings.json` in the install folder
(`C:\Program Files\SkyPlanner\GpSalesforceSync\GpSyncQuery.sql`). This means it can be
updated when the client's custom GP objects (`CS_SHIPT`, `CS_STATE`, `CS_NASTATE`,
`CSUSRep`, `CS_SRepList`) change, **without rebuilding or reinstalling the app**.

Editing rules (also documented in the file's header comment):
1. Must be a single, read-only `SELECT` statement (no `INSERT`/`UPDATE`/`DELETE`/
   `DROP`/`ALTER`/`TRUNCATE`/`EXEC`/`EXECUTE`/`MERGE`/`GRANT`/`REVOKE`/`CREATE`, no
   multiple statements).
2. Must keep these exact output column aliases: `DocumentDate`, `SalesPersonID`,
   `SalesPerson`, `SOPNumber`, `SOPType`, `ComponentSequence`, `LineItemSequence`,
   `CustomerNumber`, `CustomerName`, `BillingCity`, `ItemNumber`, `ItemDesc`,
   `ItemFamily`, `Qty`, `Amount`, `ItemClassCode`, `ShippingState`, `ShippingCity`,
   `ShippingZipCode`. The underlying tables/joins/aliases can be changed freely.
3. Must keep the `@userid`, `@StartDate`, `@EndDate` parameters.
4. Must **not** include `ORDER BY`, `OFFSET`/`FETCH`, or the optional filters (item
   class, sales rep, customer, product) — those are appended automatically by the
   app against the output columns above.

Path override: `GpSyncQuery:FilePath` in `appsettings.json`, or
`MAXSFGP_GPSYNCQUERY__FILEPATH` as a machine environment variable (same precedence as
the other `MAXSFGP_*` variables above).

If the query is invalid, the log will show `GP sync query is invalid, skipping sync
data setting (RecordId: ...)` with the specific reason (missing file, disallowed
keyword, missing required column, or a SQL Server error) — that sync data setting is
skipped for the run (no data is sent to Salesforce for it, and it is **not** reported
as a successful empty sync), while the rest of the sync data settings still run
normally. The fully assembled query (with filters/order/pagination applied) is logged
once per sync data setting via `Assembled GP sync query: ...` for troubleshooting.

## Standard Incident Triage
1. Task did not start:
- Check TaskScheduler Operational log
- Check action path points to `SalesforceDynamicsGpIntegration.exe`
2. Task started but sync failed:
- Check app log for auth/SQL errors and Salesforce endpoint response messages
3. Authentication failed:
- Verify Salesforce URL, client credentials, username/password/token
4. No data synced:
- Verify active sync settings from Salesforce endpoint
- Verify date/filter values
5. SQL errors:
- Verify connection string and DB permissions/network reachability

## Escalation Trigger
Escalate if any of these persist after one retry:
- Repeated auth failures
- Repeated SQL connection failures
- Task missing/corrupted after reinstall
- No logs generated after manual run
