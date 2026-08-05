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
- Default schedule: Daily at `00:10`, `01:10` and `02:10`
- Task action argument: `--scheduled`
- Install folder: `C:\Program Files\SkyPlanner\GpSalesforceSync`

### Why three runs a night
Salesforce runs its match process at `04:00`, so the GP data has to be there before then. The task
fires three times to leave room for retries if a run fails (Salesforce down, SQL unreachable,
credentials expired). Because the task passes `--scheduled`, the runs at `01:10` and `02:10` only do
work that is still **pending**:

- A sync data setting that already synchronized **successfully today** is skipped.
- A sync data setting that **failed** (or was never reached) is retried.
- A sync data setting **created during the night** — for example by the monthly backfill Flow — is
  picked up by the next run, not the next day.
- The skip lasts for the local calendar day only; at `00:00` everything is eligible again.

### Run modes
| Invocation | Behavior |
|---|---|
| `SalesforceDynamicsGpIntegration.exe` (no arguments) | **Manual run: always synchronizes everything.** |
| `... --scheduled` | Skips sync data settings that already synchronized successfully today. Used by the task. |
| `... --scheduled --force` | Synchronizes everything, ignoring the saved state. |
| `... --seal` | Encodes the secrets in `appsettings.json` and exits. |
| `... --help` | Lists the options. |

### Exit codes
- `0` - the run completed: everything succeeded, was skipped, or there were no active sync data
  settings. Also returned when the run stood down because another instance was already running.
- `1` - at least one sync data setting failed, or Salesforce authentication / the settings fetch
  failed. Whatever failed is **not** recorded as done, so the next run retries it.

This is what Task Scheduler shows as **Last Run Result**, so a `1` there is a real failure worth
looking into (before this change the app always reported `0`).

## Daily Health Check (5 minutes)
1. Confirm scheduled task exists and is enabled.

```powershell
schtasks /Query /TN \MaxSfGpSync /V /FO LIST
```

2. Confirm the latest run result is `0`. A `1` means something failed - see Exit codes above.
3. Confirm today log file exists in `C:\Program Files\SkyPlanner\GpSalesforceSync\logs`.
4. Open newest log and confirm:
- Salesforce authentication succeeded
- Sync settings were loaded
- Pages processed, or `No active sync data settings found` (if expected)
- The closing summary line, e.g. `Synchronization process completed - 2 succeeded, 0 skipped, 0 failed`
5. Expected pattern across the night: the `00:10` run processes pages, and the `01:10` / `02:10` runs
   log `Skipping sync data setting (RecordId: ...) - already synced successfully today at ...`. If the
   later runs are processing pages instead, the earlier run failed - check its errors.
6. If needed, run the sync manually (**always synchronizes, skips nothing**):

```powershell
cd "C:\Program Files\SkyPlanner\GpSalesforceSync"
.\RunSyncNow.cmd
```

Do **not** use `schtasks /Run /TN \MaxSfGpSync` for a manual run: it starts the task with
`--scheduled`, so it skips whatever already synchronized successfully today and may appear to do
nothing. If you do want to start it through the task, force it:

```powershell
& "C:\Program Files\SkyPlanner\GpSalesforceSync\SalesforceDynamicsGpIntegration.exe" --scheduled --force
```

## App Log Review (Code-Based)
Log behavior implemented by the app:
- Log folder is created under the app base directory: `logs`
- Log file name format is `yyyy-MM-dd_sync-log.txt` (or `yyyy-MM-dd_` + configured `Logger:LogFileName`)
- Log retention deletes `.txt` files older than `Logger:RetentionDays` (default 7)
- Log entry format is `[yyyy-MM-dd HH:mm:ss] [LEVEL] message`

Minimum messages to confirm a healthy run:
1. `Synchronization Service initialized`
2. `Run mode: scheduled ...` or `Run mode: manual ...`
3. `Starting synchronization process...`
4. `Attempting to connect to Salesforce...`
5. `Successfully authenticated with Salesforce.`
6. `Found N sync data settings`
7. `Total pages to process: N`
8. `Processing page X of Y`
9. `Successfully sent to Salesforce Page Number: X`
10. `Sync data setting (RecordId: ...) completed successfully (N page(s))`
11. `Synchronization process completed - N succeeded, M skipped, K failed`

Expected messages on the second and third run of the night (not problems):
1. `Skipping sync data setting (RecordId: ...) - already synced successfully today at ...`
2. `Another sync instance is already running - this scheduled run is skipped.` (the previous run was
   still going; it finishes on its own)

Messages that indicate configuration or runtime issues:
1. `Salesforce Authentication Failed: ...`
2. `Failed to authenticate with Salesforce. Exiting...`
3. `Authentication failed. Status: ...`
4. `Salesforce Auth Error: ...`
5. `Failed to sync page X to Salesforce. Message: ...`
6. `Exception while syncing page X to Salesforce`
7. `Failed to prepare the GP query, skipping sync data setting (RecordId: ...)`
8. `Sync data setting (RecordId: ...) finished with errors - it will be retried on the next run`
9. `Exception processing sync data settings`
10. `Fatal exception during synchronization`
11. `Failed to save sync state to ... - the next run may repeat work already done`

## Sync State (which settings already ran today)
The app remembers what already synchronized so the `01:10` / `02:10` runs do not repeat work:

- File: `C:\Program Files\SkyPlanner\GpSalesforceSync\state\sync-state.json`
  (override with `Run:StateFilePath` / `MAXSFGP_RUN__STATEFILEPATH`)
- One entry per sync data setting (`RecordId`), holding a fingerprint of its filters, the local date
  and time of the last success, and the page count.
- A setting is skipped **only** if the `RecordId`, the fingerprint **and** the local calendar date all
  match. If its filters changed in Salesforce, it runs again the same day.
- The fingerprint does **not** include `Start_Date__c` / `End_Date__c`: Salesforce advances the window
  of a recurring filter itself as soon as it receives the closing payload
  (`GPDataSyncService.updateFilter`), so the window we just synced is never the one the next GET
  returns. An ad-hoc window (a backfill) arrives as a **new** filter record with its own `RecordId`,
  so it is picked up normally.
- Entries older than 30 days are pruned automatically.
- A missing, unreadable or corrupt file never blocks the sync: it just means nothing gets skipped.
- Only successes are recorded. A failed setting is never written, so it is retried.

To force a full re-sync:

```powershell
cd "C:\Program Files\SkyPlanner\GpSalesforceSync"
.\SalesforceDynamicsGpIntegration.exe --scheduled --force
# or, equivalently, delete the state and let the next run rebuild it:
Remove-Item .\state\sync-state.json
```

## Re-registering the Task on an Existing Install
The MSI creates the three triggers automatically. On a machine installed **before** this change,
either reinstall/upgrade the MSI or re-register the task once:

```powershell
Register-ScheduledTask -TaskName MaxSfGpSync `
  -Action (New-ScheduledTaskAction -Execute 'C:\Program Files\SkyPlanner\GpSalesforceSync\SalesforceDynamicsGpIntegration.exe' -Argument '--scheduled') `
  -Trigger @((New-ScheduledTaskTrigger -Daily -At '00:10'), (New-ScheduledTaskTrigger -Daily -At '01:10'), (New-ScheduledTaskTrigger -Daily -At '02:10')) `
  -Principal (New-ScheduledTaskPrincipal -UserId SYSTEM -RunLevel Highest) -Force
```

Verify:

```powershell
schtasks /Query /TN \MaxSfGpSync /V /FO LIST
```

Three start times and an action ending in `--scheduled`.

If no log file is created after running the task:
1. Verify task action points to `SalesforceDynamicsGpIntegration.exe` in `C:\Program Files\SkyPlanner\GpSalesforceSync`
2. Verify the install folder and `logs` folder exist
3. Verify account permissions for the task run account (`SYSTEM`) to write in the install path

## Validation After Changes
1. Re-run the sync manually with `.\RunSyncNow.cmd` (never skips), or
   `.\SalesforceDynamicsGpIntegration.exe --scheduled --force` if you want to go through the task path.
2. Confirm new execution appears in Task Scheduler history.
3. Confirm fresh entries in today's log and the closing summary line.
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

6. Run the sync manually and verify logs (this always synchronizes, it never skips):

```powershell
cd "C:\Program Files\SkyPlanner\GpSalesforceSync"
.\RunSyncNow.cmd
```

Final sign-off checklist:
1. All required `MAXSFGP_*` variables are set in Machine scope.
2. Task run under `SYSTEM` succeeds (Last Run Result `0`).
3. Log confirms successful Salesforce auth and page processing.
4. Test record reaches Salesforce.
5. `state\sync-state.json` exists after a successful run.

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
   `ShippingZipCode`, `ShippingAddress`. The underlying tables/joins/aliases can be
   changed freely.
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
- Check for `Skipping sync data setting (RecordId: ...)` lines - the work was already done earlier
  today, which is expected on the `01:10` / `02:10` runs. Use `--force` to re-send anyway.
- Verify active sync settings from Salesforce endpoint
- Verify date/filter values
5. SQL errors:
- Verify connection string and DB permissions/network reachability
- A SQL failure now fails the sync data setting (it is not recorded as done), so the next run retries
  it. It no longer looks like an empty successful sync.
6. Sync ran but nothing happened at all:
- Check for `Another sync instance is already running` - a previous run was still in flight.
7. `Stopping at page X of Y to keep the Salesforce window open for a retry`:
- A page failed, so the run deliberately did **not** send the closing payload. Salesforce advances the
  filter's window when it receives that payload, so sending it would have moved the window past rows
  that never arrived. The window is still open and the next run resends it from page 1 (the upsert is
  keyed on the GP line item identifier, so resending is safe). Fix the underlying error - if all three
  runs of the night stop this way, the window stays open and nothing arrives.

## Escalation Trigger
Escalate if any of these persist after one retry:
- Repeated auth failures
- Repeated SQL connection failures
- Task missing/corrupted after reinstall
- No logs generated after manual run
